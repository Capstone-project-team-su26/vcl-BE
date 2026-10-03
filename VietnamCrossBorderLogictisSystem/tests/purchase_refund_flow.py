#!/usr/bin/env python3
"""
Kiểm thử tiền đi NGƯỢC của luồng mua hộ + thuế NK theo phần hàng thật sự tới tay khách
+ bảng việc của khách không đếm thuế / cước hai lần.

Quy tắc đã chốt:
  1. NCC hết hàng / mua ít hơn khách đặt / NCC giao thiếu → hoàn ĐỦ tiền hàng + phí mua hộ
     + VAT phần phí tương ứng (phí chia theo tỷ lệ tiền hàng đã báo).
  2. Khách huỷ SAU khi đã đặt NCC → hoàn tiền hàng KHÁCH THỰC TRẢ (gồm chênh giá đã trả, trừ phần
     chênh đã được hoàn) − phí huỷ (PURCHASE_CANCEL_FEE_RATE); giữ phí mua hộ + VAT phí.
  3. Thuế NK thu một lần trên hoá đơn đơn kho PUR theo giá mua thật; phần hàng bị huỷ (DISPOSE) /
     NCC giao thiếu → dòng TAX_ADJUSTMENT âm, đợt thu cuối không thu thuế phần đó.
  4. Tổng hoàn không bao giờ vượt tổng khách đã trả.

    APP_ENV=/root/vcl-migration-deploy/app.env API=http://127.0.0.1:8088 python3 purchase_refund_flow.py
"""
import sys

from wave1_consignment_flow import IDS, PASSED, FAILED, RUN, call, check, expect, create_order, item, get_order_quotation
from export_flow import KR, VN_WAREHOUSE, set_region, store, upload
from vn_arrival_flow import arrival_photos, ship_to_vietnam
from purchase_flow import ELECTRONICS, CLOTHES, data, request_items, pay_manually


def new_request(suffix, lines):
    """lines = [(tên, loại hàng, số lượng)]"""
    s, r = call("POST", "/api/purchase-requests", "customer", {
        "route": "KR-VN", "shippingOption": "Express",
        "receiverName": "Hoan tien " + suffix, "receiverPhone": "0900000000", "receiverAddress": "HCM",
        "items": [{"productLink": f"https://shop.example/{i}", "productType": t, "productName": f"{n} {suffix}", "quantity": q}
                  for i, (n, t, q) in enumerate(lines)]})
    d = data(r)
    request_id = d.get("purchaseRequestId") or d.get("id")
    return s, request_id, (request_items(request_id) if request_id else {})


def quote_and_prepay(request_id, prices, purchase_fee, domestic_fee, weight, tag):
    """Sale báo giá, khách chấp nhận + trả 100% phần trả trước. Trả về số trả trước."""
    s, r = call("POST", f"/api/purchase-requests/{request_id}/quotation", "sale", {
        "purchaseFee": purchase_fee, "domesticShippingFee": domestic_fee, "freightRatePerKg": 120000, "estimatedWeight": weight,
        "items": [{"purchaseRequestItemId": i, "unitPrice": p} for i, p in prices]})
    if s not in (200, 201):
        return None, r
    q = data(r)
    s, r = call("PUT", f"/api/purchase-requests/{request_id}/quotation/confirm-and-pay", "customer", {"paymentMethod": "SEPAY"})
    pay = data(r)
    pay_manually(pay.get("paymentId"), q["prepayAmount"], f"PRE-{tag}")
    return q["prepayAmount"], q


def order_and_place(request_id, supplier_id, lines, tag, customer_pays_diff=None):
    """Lập đơn mua → (khách trả chênh nếu cần) → Admin duyệt → đặt NCC. Trả về dữ liệu đơn sau khi đặt."""
    s, r = call("POST", f"/api/purchase-requests/{request_id}/purchase-orders", "sale", {
        "supplierId": supplier_id, "warehouseId": KR, "currency": "VND",
        "items": [{"purchaseRequestItemId": i, "quantity": q, "unitPrice": p} for i, q, p in lines]})
    po = data(r)
    po_id = po.get("purchaseOrderId")
    if not po_id:
        return None, r
    s, r = call("POST", f"/api/purchase-orders/{po_id}/submit", "sale")
    submitted = data(r)
    if customer_pays_diff:
        s, r = call("POST", f"/api/purchase-orders/{po_id}/customer-decision", "customer", {"accept": True, "paymentMethod": "SEPAY"})
        pay_manually(data(r)["priceDifferencePaymentId"], customer_pays_diff, f"DIFF-{tag}")
    call("POST", f"/api/purchase-orders/{po_id}/decide", "admin", {"approve": True, "note": "OK"})
    upload("sale", "PURCHASE_ORDER", po_id, "PURCHASE_PROOF", f"ncc-{tag}.png")
    s, r = call("POST", f"/api/purchase-orders/{po_id}/place", "sale", {"supplierOrderCode": f"KR-{tag}-{RUN}"})
    placed = data(r)
    placed["_submitted"] = submitted
    return (placed if s == 200 else None), r


def refunds(request_id, who="sale"):
    s, r = call("GET", f"/api/purchase-requests/{request_id}/refunds", who)
    return s, data(r)


def stats():
    s, r = call("GET", "/api/customers/me/dashboard", "customer")
    st = data(r).get("stats") or {}
    return {k: st.get(k, 0) for k in ("totalFreight", "totalServiceFee", "totalTax")}


def delta(after, before):
    return {k: round(after[k] - before[k], 2) for k in after}


def with_cancel_fee(rate, fn):
    s, r = call("POST", "/api/additional-service-fees", "admin", {
        "feeName": "Phí huỷ đơn mua NCC", "feeCode": "PURCHASE_CANCEL_FEE_RATE", "calculationType": "PERCENTAGE",
        "value": rate, "unit": "%", "isActive": True, "description": "Kiểm thử hoàn tiền"})
    fee = data(r)
    fee_id = fee.get("id") or fee.get("feeId")
    if not check(f"Admin đặt phí huỷ {rate}%", s in (200, 201) and fee_id, r):
        return
    try:
        fn()
    finally:
        call("DELETE", f"/api/additional-service-fees/{fee_id}", "admin")


# ─────────────────────────────────────────────────────────────────────────────────────────────
# A. Đặt 5 áo × 200.000, NCC chỉ có 3 áo giá 190.000 → hoàn chênh + phần không mua (+ NCC giao thiếu)
# ─────────────────────────────────────────────────────────────────────────────────────────────
def scenario_shortage(supplier_id):
    suffix = f"A{RUN}"
    s0 = stats()
    s, request_id, items = new_request(suffix, [("Ao thun", CLOTHES, 5)])
    if not check("A: khách đặt 5 áo", s in (200, 201) and request_id, items):
        return
    ao = items[f"Ao thun {suffix}"]

    # Trả trước = 1.000.000 hàng + 50.000 phí mua (5%) + 30.000 ship nội địa + VAT 8% × 80.000 = 6.400 → 1.086.400
    prepay, q = quote_and_prepay(request_id, [(ao, 200000)], 50000, 30000, 2, "A")
    if not check("A: trả trước = hàng + phí mua 5% + ship nội địa + VAT 8% phần phí = 1.086.400", prepay == 1086400, q):
        return

    # Dashboard: báo giá mua hộ vào cơ cấu chi phí đúng một lần.
    # Cước tạm tính 2kg × 120.000 = 240.000; thuế = VAT (6.400 + 19.200) + thuế NK 1.000.000 × 8% = 80.000.
    s1 = stats()
    check("Dashboard: báo giá mua hộ góp cước 240.000, phí 50.000, thuế 105.600",
          delta(s1, s0) == {"totalFreight": 240000, "totalServiceFee": 50000, "totalTax": 105600}, delta(s1, s0))

    placed, r = order_and_place(request_id, supplier_id, [(ao, 3, 190000)], "A")
    if not check("A: đặt NCC 3 áo × 190.000", placed and placed.get("status") == "ORDERED", r):
        return
    po_id, order_id = placed["purchaseOrderId"], placed["warehouseOrderId"]
    check("A: giá thấp hơn báo → khoản hoàn chênh 3 × (200.000 − 190.000) = 30.000",
          placed["_submitted"].get("refundAmount") == 30000 and placed["_submitted"].get("refundType") == "REFUND_PRICE_DIFF",
          placed["_submitted"])

    # Đơn kho PUR đã có cước + thuế theo giá mua thật: cước 1,2kg (2kg × 3/5) × 120.000 = 144.000, VAT 11.520,
    # thuế NK 570.000 × 8% = 45.600. Báo giá mua hộ chỉ còn góp phí 50.000 + VAT phí 6.400.
    s2 = stats()
    check("Dashboard: không đếm cước / thuế mua hộ hai lần (cước 144.000, phí 50.000, thuế 6.400 + 11.520 + 45.600)",
          delta(s2, s0) == {"totalFreight": 144000, "totalServiceFee": 50000, "totalTax": 63520}, delta(s2, s0))

    # Phân quyền
    s, r = call("POST", f"/api/purchase-requests/{request_id}/close-unfulfilled", "customer", {"reason": "x"})
    expect("A: khách không tự đóng phần không mua được", s, r, 403)
    s, r = call("POST", f"/api/purchase-requests/{request_id}/close-unfulfilled", "staff", {"reason": "x"})
    expect("A: nhân viên kho không lập khoản hoàn", s, r, 403)
    s, r = call("POST", f"/api/purchase-requests/{request_id}/close-unfulfilled", "sale", {})
    check("A: đóng phần không mua được bắt buộc ghi lý do", s == 400, r)

    # Quy tắc 1: 2 áo không mua được → 2 × 200.000 = 400.000 + phí 50.000 × 400.000/1.000.000 = 20.000
    #            + VAT 8% × 20.000 = 1.600 → 421.600 (ship nội địa không hoàn)
    s, r = call("POST", f"/api/purchase-requests/{request_id}/close-unfulfilled", "sale",
                {"reason": "NCC chỉ còn 3 áo"})
    closed = data(r)
    refund = closed.get("refund") or {}
    line = (refund.get("lines") or [{}])[0]
    if not check("A: đóng 2 áo không mua được → hoàn 421.600", s == 200 and refund.get("amount") == 421600, r):
        return
    check("A: chi tiết dòng hoàn: 2 × 200.000 + phí 20.000 + VAT phí 1.600",
          line.get("reasonCode") == "UNFULFILLED" and line.get("quantity") == 2 and line.get("goodsAmount") == 400000
          and line.get("serviceFeeAmount") == 20000 and line.get("vatAmount") == 1600 and line.get("formula"), line)
    summary = closed.get("summary") or {}
    check("A: tổng hoàn = chênh giá 30.000 + 421.600 = 451.600, không vượt tổng đã thu 1.086.400",
          summary.get("totalPendingRefund") == 451600 and summary.get("totalCollected") == 1086400
          and summary.get("refundableRemaining") == 1086400 - 451600, summary)

    s, r = call("POST", f"/api/purchase-requests/{request_id}/close-unfulfilled", "sale", {"reason": "lần hai"})
    check("A: đóng lần hai không hoàn thêm", s == 400, r)
    s, r = call("POST", f"/api/purchase-requests/{request_id}/purchase-orders", "sale", {
        "supplierId": supplier_id, "warehouseId": KR, "currency": "VND",
        "items": [{"purchaseRequestItemId": ao, "quantity": 2, "unitPrice": 200000}]})
    check("A: phần đã đóng + hoàn tiền không mua lại được", s == 400, r)

    # NCC giao thiếu 1 trong 3 áo đã đặt. Chênh giá của đơn đã tất toán → khách trả 190.000/áo:
    # tiền hàng 200.000 − chênh 10.000 + phí 10.000 + VAT 800 = 200.800; thuế NK 45.600 × 1/3 = 15.200 trừ khi tất toán.
    s, r = call("POST", f"/api/purchase-requests/{request_id}/close-unfulfilled", "sale", {
        "reason": "NCC giao thiếu 1 áo",
        "supplierShortages": [{"purchaseOrderId": po_id, "purchaseRequestItemId": ao, "quantity": 5}]})
    check("A: NCC giao thiếu quá số đã đặt bị chặn", s == 400, r)
    s, r = call("POST", f"/api/purchase-requests/{request_id}/close-unfulfilled", "sale", {
        "reason": "NCC giao thiếu 1 áo",
        "supplierShortages": [{"purchaseOrderId": po_id, "purchaseRequestItemId": ao, "quantity": 1}]})
    short = (data(r).get("refund") or {})
    sl = (short.get("lines") or [{}])[0]
    check("A: NCC giao thiếu 1 áo → hoàn 200.800 (giá thực trả 190.000 + phí + VAT phí)",
          s == 200 and short.get("amount") == 200800 and sl.get("reasonCode") == "SUPPLIER_SHORT"
          and sl.get("unitPrice") == 190000 and sl.get("importTaxAdjustment") == 15200 and sl.get("importTaxInRefund") is False, r)
    s, r = call("GET", f"/api/orders/{order_id}/settlement-preview", "sale")
    pv = data(r)
    check("A: tất toán đơn kho không thu thuế NK phần NCC giao thiếu (−15.200)",
          s == 200 and pv.get("importTaxAdjustment") == -15200, pv)

    # Khách xem được công thức; khách khác thì không.
    s, sm = refunds(request_id, "customer")
    check("A: khách xem danh sách khoản hoàn kèm chi tiết",
          s == 200 and len(sm.get("refunds", [])) == 3 and all(x.get("lines") for x in sm["refunds"]), sm)
    s, r = call("GET", f"/api/purchase-requests/{request_id}/refunds", "customer2")
    expect("A: khách khác không xem được khoản hoàn", s, r, 403)
    s, r = call("GET", f"/api/purchase-orders/{po_id}", "customer")
    pod = data(r)
    check("A: đơn mua trả đủ danh sách hoàn liên quan (chênh giá + NCC giao thiếu)",
          s == 200 and {x["refundType"] for x in pod.get("refunds", [])} == {"REFUND_PRICE_DIFF", "REFUND_UNFULFILLED"}
          and pod.get("totalRefundAmount") == 30000 + 200800, pod.get("refunds"))

    # Đối chiếu đúng khoản khi kế toán xác nhận đã chuyển
    unfulfilled_id = refund["refundId"]
    base = f"/api/purchase-requests/{request_id}/refunds/{unfulfilled_id}/complete"
    s, r = call("POST", base, "sale", {"transactionCode": f"RF-A-{RUN}", "amount": 400000})
    check("A: số tiền chuyển không khớp khoản hoàn → chặn", s == 400 and "không khớp" in r.get("message", ""), r)
    s, r = call("POST", base, "customer", {"transactionCode": "X"})
    expect("A: khách không tự xác nhận đã được hoàn", s, r, 403)
    s, r = call("POST", base, "sale", {"transactionCode": f"RF-A-{RUN}", "amount": 421600})
    check("A: xác nhận đúng khoản 421.600", s == 200 and data(r).get("totalRefunded") == 421600, r)
    s, r = call("POST", f"/api/purchase-orders/{po_id}/refund/complete", "sale", {"transactionCode": f"RF-A2-{RUN}"})
    check("A: đơn có 2 khoản chờ (chênh giá + giao thiếu) → phải chỉ rõ refundId", s == 400 and "refundId" in r.get("message", ""), r)
    diff_id = next(x["refundId"] for x in pod["refunds"] if x["refundType"] == "REFUND_PRICE_DIFF")
    s, r = call("POST", f"/api/purchase-orders/{po_id}/refund/complete", "sale",
                {"transactionCode": f"RF-A2-{RUN}", "refundId": diff_id, "amount": 30000})
    check("A: đóng đúng khoản chênh giá 30.000 của đơn mua", s == 200 and any(
        x["refundId"] == diff_id and x["status"] == "REFUNDED" for x in data(r).get("refunds", [])), r)


# ─────────────────────────────────────────────────────────────────────────────────────────────
# B. Khách trả thêm chênh giá rồi huỷ (phí huỷ 10%) → hoàn tiền hàng thực trả − 10%
# ─────────────────────────────────────────────────────────────────────────────────────────────
def scenario_cancel_after_diff_paid(supplier_id):
    suffix = f"B{RUN}"
    s, request_id, items = new_request(suffix, [("Ao khoac", CLOTHES, 3)])
    ao = items.get(f"Ao khoac {suffix}")
    # 600.000 + 30.000 + VAT 2.400 = 632.400
    prepay, q = quote_and_prepay(request_id, [(ao, 200000)], 30000, 0, 1, "B")
    if not check("B: trả trước 632.400", prepay == 632400, q):
        return
    # Giá thật 3 × 240.000 = 720.000 → vượt ngưỡng 5% → khách trả thêm 120.000
    placed, r = order_and_place(request_id, supplier_id, [(ao, 3, 240000)], "B", customer_pays_diff=120000)
    if not check("B: khách trả chênh 120.000 rồi đặt NCC", placed and placed.get("status") == "ORDERED", r):
        return
    po_id = placed["purchaseOrderId"]
    s, r = call("GET", f"/api/purchase-orders/{po_id}", "sale")
    check("B: đơn kho có hoá đơn chặng VN đang chờ thu", data(r).get("warehouseInvoiceStatus") == "PENDING", data(r))

    def cancel():
        s, r = call("POST", f"/api/purchase-orders/{po_id}/cancel", "admin", {"reason": "Khách đổi ý", "cause": "CUSTOMER"})
        c = data(r)
        # Khách thực trả tiền hàng 600.000 + 120.000 = 720.000; phí huỷ 10% = 72.000 → hoàn 648.000; giữ phí mua + VAT phí
        check("B: huỷ sau đặt NCC → hoàn tiền hàng thực trả 720.000 − phí huỷ 10% = 648.000",
              s == 200 and c.get("refundAmount") == 648000 and c.get("cancelFeeAmount") == 72000, c)
        ref = next((x for x in c.get("refunds", []) if x["refundType"] == "REFUND_CANCEL"), {})
        check("B: chi tiết: tiền hàng 600.000 + chênh giá 120.000 − phí huỷ 72.000, không hoàn phí mua hộ",
              ref.get("goodsAmount") == 600000 and ref.get("priceDifferenceAmount") == 120000
              and ref.get("cancelFeeAmount") == 72000 and ref.get("serviceFeeAmount") == 0 and ref.get("vatAmount") == 0, ref)
        check("B: huỷ sau đặt NCC → đơn kho + hoá đơn chặng VN không còn PENDING",
              c.get("warehouseOrderStatus") == "CANCELLED" and c.get("warehouseInvoiceStatus") == "CANCELLED", c)

    with_cancel_fee(10, cancel)


# ─────────────────────────────────────────────────────────────────────────────────────────────
# C. Đã hoàn chênh giá rồi huỷ → không hoàn thừa; tổng hoàn không vượt tổng đã thu
# ─────────────────────────────────────────────────────────────────────────────────────────────
def scenario_refunded_then_cancel(supplier_id):
    suffix = f"C{RUN}"
    s, request_id, items = new_request(suffix, [("Loa", ELECTRONICS, 2)])
    loa = items.get(f"Loa {suffix}")
    # 1.000.000 + 50.000 + VAT 4.000 = 1.054.000
    prepay, q = quote_and_prepay(request_id, [(loa, 500000)], 50000, 0, 1, "C")
    if not check("C: trả trước 1.054.000", prepay == 1054000, q):
        return
    placed, r = order_and_place(request_id, supplier_id, [(loa, 2, 400000)], "C")
    if not check("C: giá thật 2 × 400.000, đặt NCC", placed and placed.get("status") == "ORDERED", r):
        return
    po_id = placed["purchaseOrderId"]
    s, r = call("POST", f"/api/purchase-orders/{po_id}/refund/complete", "sale", {"transactionCode": f"RF-C-{RUN}", "amount": 200000})
    check("C: đã hoàn chênh giá 200.000", s == 200 and data(r).get("refundStatus") == "REFUNDED", r)

    s, r = call("POST", f"/api/purchase-orders/{po_id}/cancel", "admin", {"reason": "Khách đổi ý"})
    c = data(r)
    check("C: huỷ sau khi đã hoàn chênh → chỉ hoàn tiền hàng khách còn trả 800.000 (không phải 1.000.000)",
          s == 200 and c.get("refundAmount") == 800000, c)
    s, sm = refunds(request_id)
    live = sm.get("totalRefunded", 0) + sm.get("totalPendingRefund", 0)
    check("C: tổng hoàn 1.000.000 ≤ tổng đã thu 1.054.000 (giữ phí mua 50.000 + VAT 4.000)",
          live == 1000000 and sm.get("totalCollected") == 1054000, sm)

    # Mua lại rồi NCC huỷ: hoàn đủ 1.054.000 sẽ vượt phần còn lại 54.000 → chặn cứng.
    placed2, r = order_and_place(request_id, supplier_id, [(loa, 2, 500000)], "C2")
    if not check("C: lập lại đơn mua cùng sản phẩm", placed2 and placed2.get("status") == "ORDERED", r):
        return
    s, r = call("POST", f"/api/purchase-orders/{placed2['purchaseOrderId']}/cancel", "admin", {"reason": "NCC huỷ", "cause": "SUPPLIER"})
    check("C: tổng hoàn vượt tổng đã thu → chặn", s == 400 and "vượt" in r.get("message", ""), r)
    s, r = call("GET", f"/api/purchase-orders/{placed2['purchaseOrderId']}", "sale")
    check("C: bị chặn thì đơn vẫn nguyên trạng ORDERED, đơn kho chưa huỷ",
          data(r).get("status") == "ORDERED" and data(r).get("warehouseOrderStatus") != "CANCELLED", data(r))


# ─────────────────────────────────────────────────────────────────────────────────────────────
# D. Kiện mua hộ bị huỷ (DISPOSE) ở kho VN → dòng điều chỉnh thuế NK âm, đợt cuối không thu thuế phần đó
# ─────────────────────────────────────────────────────────────────────────────────────────────
def scenario_dispose(supplier_id):
    suffix = f"D{RUN}"
    s, request_id, items = new_request(suffix, [("May anh", ELECTRONICS, 1), ("Vay", CLOTHES, 1)])
    cam, dress = items.get(f"May anh {suffix}"), items.get(f"Vay {suffix}")
    prepay, q = quote_and_prepay(request_id, [(cam, 500000), (dress, 200000)], 35000, 0, 2, "D")
    if not check("D: trả trước mua hộ 2 món", prepay and prepay > 0, q):
        return
    placed, r = order_and_place(request_id, supplier_id, [(cam, 1, 500000), (dress, 1, 200000)], "D")
    if not check("D: đặt NCC", placed and placed.get("status") == "ORDERED", r):
        return
    order_id, note_code = placed["warehouseOrderId"], placed["receivingNoteCode"]

    s, r = call("GET", f"/api/warehouse-receiving-notes/scan?code={note_code}", "staff")
    expected = data(r).get("expectedItems") or []
    packages = [{"orderItemId": e["orderItemId"], "actualWeight": 1.0, "length": 20, "width": 20, "height": 10} for e in expected]
    s, r = call("POST", "/api/parcels/check-in", "staff", {"shippingCode": note_code, "packages": packages})
    d = data(r)
    parcels = {p["productName"]: p["parcelId"] for p in (d.get("parcels") or [d])}
    p_cam, p_dress = parcels.get(f"May anh {suffix}"), parcels.get(f"Vay {suffix}")
    if not check("D: kho check-in 2 kiện theo phiếu", p_cam and p_dress, r):
        return
    store(p_cam, "BIN-A1-01")
    store(p_dress, "BIN-A1-02")
    shipment_id = ship_to_vietnam([p_cam, p_dress], f"MHD{RUN}")
    if not check("D: 2 kiện về tới kho VN", shipment_id is not None, shipment_id):
        return
    arrival_photos([p_cam, p_dress])  # ảnh tiếp nhận kho VN — bắt buộc mỗi kiện
    s, r = call("PUT", f"/api/international-shipments/{shipment_id}/destination-receiving", "staff", {"items": [
        {"parcelId": p_cam, "actualWeight": 1.0, "length": 20, "width": 20, "height": 10, "condition": "GOOD"},
        {"parcelId": p_dress, "actualWeight": 1.0, "length": 20, "width": 20, "height": 10, "condition": "DAMAGED",
         "inspectionNote": "Váy rách"}]})
    expect("D: kho VN tiếp nhận, váy hỏng vào cách ly", s, r, 200)

    s, r = call("GET", f"/api/parcel-incidents?orderId={order_id}", "customer")
    damaged = next((i for i in data(r).get("items", []) if i["incidentType"] == "DAMAGED"), None)
    if not check("D: có sự cố hàng hỏng", damaged is not None, r):
        return
    call("POST", f"/api/parcel-incidents/{damaged['id']}/customer-response", "customer", {"choice": "DISPOSE", "note": "Huỷ giúp"})
    upload("staff", "INCIDENT", damaged["id"], "INCIDENT_PHOTO", "vay-rach.pdf")
    s, r = call("POST", f"/api/parcel-incidents/{damaged['id']}/resolve", "om1", {"resolution": "DISPOSE", "note": "Theo khách"})
    expect("D: quản lý kho quyết huỷ váy", s, r, 200)

    s, r = call("GET", f"/api/orders/{order_id}/settlement-preview", "customer")
    pv = data(r)
    # Thuế NK váy 200.000 × 8% = 16.000 không thu; máy ảnh 500.000 × 10% = 50.000 vẫn thu.
    check("D: xem trước tất toán có điều chỉnh thuế NK −16.000 cho kiện huỷ",
          s == 200 and pv.get("importTaxAdjustment") == -16000 and pv.get("canIssue") is True, pv)
    check("D: đợt cuối = hoá đơn + điều chỉnh cước/VAT + điều chỉnh thuế (−16.000)",
          abs(pv["estimatedFinalAmount"] - max(0, pv["invoiceTotalBefore"] + pv["freightAdjustment"] + pv["vatAdjustment"]
                                              + pv["importTaxAdjustment"] + pv["storageFee"] - pv["depositPaid"])) < 1, pv)
    s, r = call("POST", f"/api/orders/{order_id}/payments/final", "sale", {"paymentMethod": "SEPAY"})
    fin = data(r)
    check("D: phát hành đợt cuối không thu thuế phần hàng huỷ",
          s in (200, 201) and abs(fin.get("finalAmount", -1) - pv["estimatedFinalAmount"]) < 1
          and fin.get("freightAdjustmentDelta") == pv["freightAdjustment"] + pv["vatAdjustment"] - 16000, (fin, pv))
    s, r = call("POST", f"/api/orders/{order_id}/payments/final", "sale", {"paymentMethod": "SEPAY", "reissue": True})
    check("D: phát hành lại không trừ thuế lần hai",
          s in (200, 201) and data(r).get("freightAdjustmentDelta") == 0
          and abs(data(r).get("finalAmount", -1) - fin.get("finalAmount", 0)) < 1, r)


# ─────────────────────────────────────────────────────────────────────────────────────────────
# E. Bảng việc của khách: báo giá ký gửi không cộng thuế hai lần
# ─────────────────────────────────────────────────────────────────────────────────────────────
def scenario_dashboard_consignment_tax():
    def pending_quotes():
        s, r = call("GET", "/api/customers/me/dashboard", "customer")
        return next((a for a in data(r).get("actions", []) if a["key"] == "QUOTATION_CONFIRM"), {})

    before = pending_quotes()
    s, order = create_order([item("Dashboard thue", 1, 1.0)], f"E{RUN}")
    order_id = order.get("orderId")
    s, r = call("POST", f"/api/orders/{order_id}/quotation/send", "sale", {"warehouseId": IDS["warehouseKr"]})
    _, qr = get_order_quotation(order_id)
    q = data(qr)
    after = pending_quotes()
    # Tổng báo giá = cước + phí dịch vụ + phí nội địa + thuế; thuế chỉ tính MỘT lần (taxAndDuty = VAT + thuế NK).
    expected = (q.get("estimatedFreightCharge", 0) + q.get("serviceFee", 0) + q.get("domesticShippingFee", 0)
                + q.get("vat", 0) + q.get("importTax", 0))
    check("E: báo giá có thuế để kiểm (taxAndDuty = VAT + thuế NK > 0)",
          q.get("taxAndDuty", 0) > 0 and abs(q["taxAndDuty"] - q.get("vat", 0) - q.get("importTax", 0)) < 1
          and abs(expected - q.get("totalEstimatedCost", -1)) < 1, q)
    check("E: thẻ 'báo giá chờ xác nhận' cộng đúng tổng báo giá (không gấp đôi thuế)",
          after.get("count") == before.get("count", 0) + 1
          and abs(after.get("amount", 0) - before.get("amount", 0) - expected) < 1,
          (before.get("amount"), after.get("amount"), expected, q.get("taxAndDuty")))


def main():
    call("PUT", f"/api/warehouses/{KR}/manager", "admin", {"managerId": None, "reason": "Kiểm thử hoàn tiền mua hộ"})
    call("PUT", f"/api/warehouses/{VN_WAREHOUSE}/manager", "admin", {"managerId": None, "reason": "Kiểm thử hoàn tiền mua hộ"})
    set_region("staff", "KR")

    s, r = call("POST", "/api/suppliers", "admin", {
        "supplierCode": f"NCC-HT-{RUN}", "supplierName": f"NCC hoàn tiền {RUN}", "supplierType": "GOODS",
        "country": "KR", "email": f"ncc-ht{RUN}@example.com"})
    supplier_id = data(r).get("id") or data(r).get("supplierId")
    if not check("Admin tạo NCC", s in (200, 201) and supplier_id, r):
        return 1

    scenario_shortage(supplier_id)
    scenario_cancel_after_diff_paid(supplier_id)
    scenario_refunded_then_cancel(supplier_id)
    scenario_dispose(supplier_id)
    scenario_dashboard_consignment_tax()

    print(f"\n{len(PASSED)} PASS, {len(FAILED)} FAIL")
    return 1 if FAILED else 0


if __name__ == "__main__":
    sys.exit(main())
