#!/usr/bin/env python3
"""
Kiểm thử luồng mua hộ chuẩn (docs/migrate-source-cu/08-luong-mua-ho.md mục 6):
khách tạo yêu cầu → Sale báo giá (trả trước = hàng + phí mua + ship nội địa + VAT phí; cước + thuế
tạm tính) → khách trả trước 100% → Sale lập đơn mua NCC → giá vượt ngưỡng thì khách duyệt + trả
chênh → Admin duyệt → Sale đặt NCC (mã + bằng chứng) → đơn kho PUR-xxx-n + phiếu tiếp nhận ACTIVE
→ tiến độ NCC → kho check-in theo phiếu → xuất kho → về VN → tất toán theo cân đo VN.

    APP_ENV=/root/vcl-migration-deploy/app.env API=http://127.0.0.1:8088 python3 purchase_flow.py
"""
import sys

from wave1_consignment_flow import IDS, PASSED, FAILED, RUN, call, check, expect
from export_flow import KR, VN_WAREHOUSE, set_region, store, upload
from vn_arrival_flow import arrival_photos, ship_to_vietnam

ELECTRONICS = "11111111-0000-0000-0000-000000000001"   # thuế NK 10%
CLOTHES = "11111111-0000-0000-0000-000000000002"       # thuế NK 8%


def data(r):
    return r.get("data", r) if isinstance(r, dict) else r


def create_request(suffix):
    s, r = call("POST", "/api/purchase-requests", "customer", {
        "route": "KR-VN", "shippingOption": "Express",
        "receiverName": "Mua ho " + suffix, "receiverPhone": "0900000000", "receiverAddress": "HCM",
        "items": [
            {"productLink": "https://shop.example/a", "productType": ELECTRONICS, "productName": f"Tai nghe {suffix}", "quantity": 2},
            {"productLink": "https://shop.example/b", "productType": CLOTHES, "productName": f"Ao khoac {suffix}", "quantity": 3},
        ]})
    d = data(r)
    return s, d.get("purchaseRequestId") or d.get("id"), d


def request_items(request_id):
    s, r = call("GET", f"/api/purchase-requests/{request_id}", "sale")
    d = data(r)
    items = d.get("items") or d.get("purchaseRequestItems") or []
    return {i["productName"]: (i.get("itemId") or i.get("purchaseRequestItemId") or i.get("id")) for i in items}


def pay_manually(payment_id, amount, suffix):
    return call("PUT", f"/api/admin/finance/transactions/{payment_id}/approve", "admin",
                {"transactionCode": f"FT-MH-{RUN}-{suffix}", "receivedAmount": amount})


def main():
    call("PUT", f"/api/warehouses/{KR}/manager", "admin", {"managerId": None, "reason": "Kiểm thử mua hộ"})
    call("PUT", f"/api/warehouses/{VN_WAREHOUSE}/manager", "admin", {"managerId": None, "reason": "Kiểm thử mua hộ"})
    set_region("staff", "KR")

    s, r = call("POST", "/api/suppliers", "admin", {
        "supplierCode": f"NCC-{RUN}", "supplierName": f"NCC Hàn {RUN}", "supplierType": "GOODS", "country": "KR", "email": f"ncc{RUN}@example.com"})
    supplier_id = data(r).get("id") or data(r).get("supplierId")
    if not check("Admin tạo NCC trong danh mục", s in (200, 201) and supplier_id, r):
        return 1

    # ---------------- ① Khách tạo yêu cầu
    s, request_id, d = create_request(f"M{RUN}")
    if not check("Khách tạo yêu cầu mua hộ", s in (200, 201) and request_id, d):
        return 1
    items = request_items(request_id)
    item_a, item_b = items[f"Tai nghe M{RUN}"], items[f"Ao khoac M{RUN}"]

    # ---------------- ③ Sale báo giá
    s, r = call("POST", f"/api/purchase-requests/{request_id}/calculate-fees", "sale",
                {"items": [{"purchaseRequestItemId": item_a, "unitPrice": 500000, "quantity": 2},
                           {"purchaseRequestItemId": item_b, "unitPrice": 200000, "quantity": 3}]})
    calc = data(r)
    check("Gợi ý phí trả đơn giá cước /kg theo bảng giá tuyến KR Express",
          s == 200 and calc.get("freightRatePerKg") == 120000 and calc.get("estimatedPrepayAmount", 0) > 1600000, calc)

    quote_body = {"purchaseFee": 80000, "domesticShippingFee": 30000, "freightRatePerKg": 120000, "estimatedWeight": 2,
                  "items": [{"purchaseRequestItemId": item_a, "unitPrice": 500000},
                            {"purchaseRequestItemId": item_b, "unitPrice": 200000}]}
    s, r = call("POST", f"/api/purchase-requests/{request_id}/quotation", "customer", quote_body)
    expect("Khách không tự báo giá được", s, r, 403)
    s, r = call("POST", f"/api/purchase-requests/{request_id}/quotation", "sale", quote_body)
    q = data(r)
    if not expect("Sale báo giá", s, r, 201 if s == 201 else 200):
        return 1
    # hàng 1.600.000 + phí 80.000 + ship nội địa 30.000 + VAT 8% (110.000) = 8.800 → 1.718.800
    check("Phần trả trước = hàng + phí mua + ship nội địa + VAT phần phí", q.get("prepayAmount") == 1718800, q)
    # cước 2kg × 120.000 = 240.000 + VAT 19.200 + thuế NK (1.000.000×10% + 600.000×8% = 148.000) = 407.200
    check("Phần tạm tính = cước quốc tế + VAT cước + thuế NK", q.get("estimatedLaterAmount") == 407200, q)
    check("Tổng dự kiến = trả trước + tạm tính", q.get("totalAmount") == 1718800 + 407200, q)
    s, r = call("GET", f"/api/purchase-requests/{request_id}/quotation", "customer")
    check("Khách xem báo giá có phần trả trước / tạm tính", s == 200 and data(r).get("prepayAmount") == 1718800, r)

    # ---------------- Chưa trả trước thì chưa lập đơn mua
    po_body = {"supplierId": supplier_id, "warehouseId": KR, "currency": "VND",
               "items": [{"purchaseRequestItemId": item_a, "quantity": 2, "unitPrice": 510000}]}
    s, r = call("POST", f"/api/purchase-requests/{request_id}/purchase-orders", "sale", po_body)
    check("Chưa trả trước thì chưa lập đơn mua", s == 400, r)

    # ---------------- ④ Khách chấp nhận + trả 100% phần trả trước
    s, r = call("PUT", f"/api/purchase-requests/{request_id}/quotation/confirm-and-pay", "customer", {"paymentMethod": "SEPAY"})
    pay = data(r)
    check("Khách chấp nhận báo giá → khoản PREPAYMENT đúng số trả trước",
          s == 200 and pay.get("amount") == 1718800 and pay.get("depositAmount") == 1718800, r)
    s, r = pay_manually(pay.get("paymentId"), 1718800, "PRE")
    expect("Admin ghi nhận khách đã trả trước", s, r, 200)
    s, r = call("GET", f"/api/purchase-requests/{request_id}/payments", "customer")
    hist = data(r)
    check("Lịch sử thanh toán: loại PREPAYMENT, đã trả đủ phần trả trước",
          s == 200 and hist["payments"][0]["paymentType"] == "PREPAYMENT" and hist["outstanding"] == 0
          and hist["estimatedLaterAmount"] == 407200, hist)

    # ---------------- Endpoint cũ đã bỏ
    for method, path in (("PUT", "confirm-purchase"), ("POST", "approve-store"), ("POST", "final-payment")):
        s, r = call(method, f"/api/purchase-requests/{request_id}/{path}", "sale", {})
        check(f"Đã bỏ {path}", s in (404, 405), (s, r))

    # ---------------- ⑤ Sale lập đơn mua NCC (giá lệch trong ngưỡng 5%)
    s, r = call("POST", f"/api/purchase-requests/{request_id}/purchase-orders", "sale", {**po_body, "supplierId": None})
    check("Bắt buộc chọn NCC trong danh mục", s == 400, r)
    bad = {**po_body, "items": [{"purchaseRequestItemId": item_a, "quantity": 5, "unitPrice": 510000}]}
    s, r = call("POST", f"/api/purchase-requests/{request_id}/purchase-orders", "sale", bad)
    check("Không mua quá số lượng khách đặt", s == 400 and "từ 1 đến 2" in r.get("message", ""), r)
    s, r = call("POST", f"/api/purchase-requests/{request_id}/purchase-orders", "sale", po_body)
    po1 = data(r)
    if not check("Sale lập đơn mua NCC #1 (nháp)", s == 201 and po1.get("status") == "DRAFT", r):
        return 1
    po1_id = po1["purchaseOrderId"]
    s, r = call("POST", f"/api/purchase-requests/{request_id}/purchase-orders", "sale", po_body)
    check("Một sản phẩm chỉ nằm trong một đơn mua còn hiệu lực", s == 400 and "đơn mua khác" in r.get("message", ""), r)

    s, r = call("POST", f"/api/purchase-orders/{po1_id}/submit", "sale")
    check("Giá lệch 2% (trong ngưỡng) → chờ Admin duyệt luôn",
          s == 200 and data(r)["status"] == "PENDING_APPROVAL" and data(r)["priceDifferenceAmount"] == 0, r)
    s, r = call("POST", f"/api/purchase-orders/{po1_id}/decide", "sale", {"approve": True})
    expect("Sale không tự duyệt đơn mua", s, r, 403)
    s, r = call("POST", f"/api/purchase-orders/{po1_id}/decide", "admin", {"approve": False})
    check("Admin từ chối phải có lý do", s == 400, r)
    s, r = call("POST", f"/api/purchase-orders/{po1_id}/decide", "admin", {"approve": True, "note": "OK"})
    check("⑥ Admin duyệt đơn mua", s == 200 and data(r)["status"] == "APPROVED", r)

    # ---------------- ⑦ Sale đặt NCC
    s, r = call("POST", f"/api/purchase-orders/{po1_id}/place", "sale", {"supplierOrderCode": f"KR-{RUN}"})
    # Câu lỗi tiếng Việt, không chèn mã loại chứng từ (PURCHASE_PROOF).
    check("Đặt NCC bắt buộc ảnh bằng chứng", s == 400 and "chứng từ" in r.get("message", ""), r)
    upload("sale", "PURCHASE_ORDER", po1_id, "PURCHASE_PROOF", "don-ncc.png")
    s, r = call("POST", f"/api/purchase-orders/{po1_id}/place", "sale", {"supplierOrderCode": f"KR-{RUN}"})
    placed = data(r)
    if not check("Sale đặt NCC → đơn kho + phiếu tiếp nhận ACTIVE",
                 s == 200 and placed["status"] == "ORDERED" and placed["warehouseOrderCode"].endswith("-1")
                 and placed["receivingNoteStatus"] == "ACTIVE" and placed["warehouseOrderStatus"] == "DEPOSIT_PAID", r):
        return 1
    order_id, note_code = placed["warehouseOrderId"], placed["receivingNoteCode"]
    s, r = call("GET", f"/api/purchase-requests/{request_id}", "customer")
    check("Yêu cầu chuyển PURCHASING", data(r).get("status") == "PURCHASING", data(r).get("status"))

    s, r = call("GET", f"/api/purchase-requests/{request_id}/purchase-orders", "customer")
    rows = data(r)
    check("Khách xem đơn mua không thấy NCC / mã đơn NCC / giá gốc",
          s == 200 and len(rows) == 1 and rows[0]["supplierName"] is None and rows[0]["supplierOrderCode"] is None, rows)
    s, r = call("GET", f"/api/purchase-orders/{po1_id}", "customer2")
    expect("Khách khác không xem được đơn mua", s, r, 403)

    # ---------------- ⑨ Tiến độ NCC
    s, r = call("PUT", f"/api/purchase-orders/{po1_id}/progress", "sale", {"status": "SUPPLIER_SHIPPED"})
    check("NCC phát hàng phải có mã vận đơn nội địa", s == 400, r)
    s, r = call("PUT", f"/api/purchase-orders/{po1_id}/progress", "sale", {"status": "SUPPLIER_CONFIRMED"})
    check("NCC xác nhận", s == 200 and data(r)["status"] == "SUPPLIER_CONFIRMED", r)
    s, r = call("PUT", f"/api/purchase-orders/{po1_id}/progress", "sale",
                {"status": "SUPPLIER_SHIPPED", "domesticTrackingCode": f"CJ{RUN}", "domesticCarrier": "CJ"})
    check("NCC phát hàng", s == 200 and data(r)["status"] == "SUPPLIER_SHIPPED", r)

    # ---------------- ⑤ đơn mua #2 vượt ngưỡng → khách duyệt giá chênh
    po2_body = {"supplierId": supplier_id, "warehouseId": KR, "currency": "VND",
                "items": [{"purchaseRequestItemId": item_b, "quantity": 3, "unitPrice": 230000}]}
    s, r = call("POST", f"/api/purchase-requests/{request_id}/purchase-orders", "sale", po2_body)
    po2_id = data(r)["purchaseOrderId"]
    s, r = call("POST", f"/api/purchase-orders/{po2_id}/submit", "sale")
    check("Giá vượt 15% → chờ khách, phần chênh 90.000đ",
          s == 200 and data(r)["status"] == "AWAITING_CUSTOMER" and data(r)["priceDifferenceAmount"] == 90000, r)
    s, r = call("POST", f"/api/purchase-orders/{po2_id}/customer-decision", "customer2", {"accept": True})
    expect("Khách khác không quyết thay", s, r, 403)
    s, r = call("POST", f"/api/purchase-orders/{po2_id}/customer-decision", "customer", {"accept": False})
    check("Không đồng ý phải có lý do", s == 400, r)
    s, r = call("POST", f"/api/purchase-orders/{po2_id}/customer-decision", "customer", {"accept": False, "reason": "Đắt quá"})
    check("Khách không đồng ý → đơn về nháp cho Sale", s == 200 and data(r)["status"] == "DRAFT", r)
    s, r = call("POST", f"/api/purchase-orders/{po2_id}/submit", "sale")
    s, r = call("POST", f"/api/purchase-orders/{po2_id}/customer-decision", "customer", {"accept": True, "paymentMethod": "SEPAY"})
    diff = data(r)
    check("Khách đồng ý → có khoản trả phần chênh", s == 200 and diff["status"] == "AWAITING_CUSTOMER_PAYMENT"
          and diff["priceDifferencePaymentStatus"] == "PENDING", r)
    s, r = call("POST", f"/api/purchase-orders/{po2_id}/decide", "admin", {"approve": True})
    check("Chưa trả phần chênh thì Admin chưa duyệt được", s == 400, r)
    s, r = pay_manually(diff["priceDifferencePaymentId"], 90000, "DIFF")
    expect("Ghi nhận khách trả phần chênh", s, r, 200)
    s, r = call("GET", f"/api/purchase-orders/{po2_id}", "sale")
    check("Trả xong phần chênh → đơn tự sang chờ Admin duyệt", data(r)["status"] == "PENDING_APPROVAL", r)
    s, r = call("POST", f"/api/purchase-orders/{po2_id}/cancel", "sale", {"reason": "NCC hết hàng"})
    check("Sale huỷ được đơn chưa đặt NCC", s == 200 and data(r)["status"] == "CANCELLED", r)
    s, r = call("GET", "/api/purchase-orders?status=CANCELLED&search=" + f"PO-", "sale")
    check("Danh sách đơn mua lọc theo trạng thái", s == 200 and any(o["purchaseOrderId"] == po2_id for o in data(r)["items"]), r)

    # ---------------- ⑩ Kho nhận theo phiếu (dùng chung luồng ký gửi)
    s, r = call("GET", f"/api/warehouse-receiving-notes/scan?code={note_code}", "staff")
    expected = data(r).get("expectedItems") or []
    check("Kho quét phiếu thấy dòng hàng của đơn mua", s == 200 and len(expected) == 1 and expected[0]["quantity"] == 2, r)
    packages = [{"orderItemId": expected[0]["orderItemId"], "actualWeight": 1.2, "length": 30, "width": 20, "height": 10}]
    s, r = call("POST", "/api/parcels/check-in", "staff", {"shippingCode": note_code, "packages": packages})
    d = data(r)
    parcel = (d.get("parcels") or [d])[0]
    check("Kho check-in cân đo theo phiếu", s in (200, 201) and parcel.get("parcelId"), r)
    s, r = call("POST", f"/api/purchase-orders/{po1_id}/cancel", "admin", {"reason": "thử"})
    check("Kho đã nhận kiện thì không huỷ được đơn mua", s == 400, r)
    s, r = call("POST", f"/api/purchase-orders/{po1_id}/cancel", "sale", {"reason": "thử"})
    expect("Đơn đã đặt NCC chỉ Admin được huỷ", s, r, 403)

    # ---------------- ⑪ Như ký gửi: xếp kệ → xuất kho → về VN → tất toán theo cân đo VN
    store(parcel["parcelId"], "BIN-A1-01")
    shipment_id = ship_to_vietnam([parcel["parcelId"]], f"MH{RUN}")
    if not check("Kiện mua hộ đi luồng xuất kho về tới kho VN", shipment_id is not None, shipment_id):
        return 1
    arrival_photos([parcel["parcelId"]])  # ảnh tiếp nhận kho VN — bắt buộc mỗi kiện
    s, r = call("PUT", f"/api/international-shipments/{shipment_id}/destination-receiving", "staff",
                {"items": [{"parcelId": parcel["parcelId"], "actualWeight": 1.25, "length": 30, "width": 20, "height": 10,
                            "condition": "GOOD"}]})
    expect("Kho VN tiếp nhận có cân đo", s, r, 200)
    s, r = call("GET", f"/api/orders/{order_id}/settlement-preview", "customer")
    p = data(r)
    # cân 1.25 kg (thể tích 30×20×10/5000 = 1.2) × 120.000 = 150.000; báo 0.8 kg (2kg × 2/5 món) = 96.000
    check("Tất toán tính lại cước theo cân đo VN với đơn giá đã báo",
          s == 200 and p["freightRate"] == 120000 and p["vnFreight"] == 150000 and p["quotedFreight"] == 96000
          and p["depositPaid"] == 0 and not p["blockers"], p)
    s, r = call("POST", f"/api/orders/{order_id}/payments/final", "sale", {"paymentMethod": "SEPAY"})
    fin = data(r)
    # hoá đơn: cước 96.000 + VAT 7.680 + thuế NK 1.020.000×10% = 102.000 → 205.680; điều chỉnh +54.000 + VAT 4.320
    check("Đợt cuối = cước VN + VAT + thuế NK (không đòi cọc)", s in (200, 201) and fin.get("finalAmount") == 264000, r)

    # ---------------- Phân quyền xem đơn
    s, r = call("GET", f"/api/orders/{order_id}/storage-fee", "customer2")
    expect("Khách khác không xem được phí lưu kho của đơn", s, r, 403)
    s, r = call("GET", f"/api/orders/{order_id}/storage-fee", "customer")
    expect("Chủ đơn xem được phí lưu kho", s, r, 200)

    # ---------------- ⑫ Tiền đi ngược: hoàn phần thừa + huỷ sau khi đã đặt NCC
    refunds_and_cancellation(supplier_id)

    # ---------------- ⑬ Nhân viên thêm địa chỉ nhận hàng hộ khách
    staff_adds_address()

    # ---------------- ⑭ Bảng việc cần làm của khách
    customer_dashboard()

    print(f"\n{len(PASSED)} PASS, {len(FAILED)} FAIL")
    return 1 if FAILED else 0


def refunds_and_cancellation(supplier_id):
    """
    Hai chiều tiền đi NGƯỢC mà trước đây không có đường xử lý:

      A. Giá mua THỰC thấp hơn giá đã báo → khách đã trả trước 100% nên phần thừa
         phải quay về khách, không nằm im trong sổ.
      B. Huỷ đơn SAU khi đã đặt NCC → hoàn tiền hàng đã báo, trừ phí huỷ nếu
         Admin có cấu hình PURCHASE_CANCEL_FEE_RATE (mặc định 0 = hoàn đủ).
    """
    suffix = f"H{RUN}"
    s, request_id, d = create_request(suffix)
    if not check("Hoàn tiền: tạo yêu cầu mua hộ mới", s in (200, 201) and request_id, d):
        return
    items = request_items(request_id)
    item_a, item_b = items[f"Tai nghe {suffix}"], items[f"Ao khoac {suffix}"]

    s, r = call("POST", f"/api/purchase-requests/{request_id}/quotation", "sale", {
        "purchaseFee": 80000, "domesticShippingFee": 30000, "freightRatePerKg": 120000, "estimatedWeight": 2,
        "items": [{"purchaseRequestItemId": item_a, "unitPrice": 500000},
                  {"purchaseRequestItemId": item_b, "unitPrice": 200000}]})
    if not check("Hoàn tiền: Sale báo giá", s in (200, 201), r):
        return
    s, r = call("PUT", f"/api/purchase-requests/{request_id}/quotation/confirm-and-pay", "customer", {"paymentMethod": "SEPAY"})
    pay = data(r)
    pay_manually(pay.get("paymentId"), 1718800, "PRE-H")

    # ── A. Giá thực THẤP hơn giá báo: báo 2 × 500.000, mua thật 2 × 450.000 → thừa 100.000
    s, r = call("POST", f"/api/purchase-requests/{request_id}/purchase-orders", "sale", {
        "supplierId": supplier_id, "warehouseId": KR, "currency": "VND",
        "items": [{"purchaseRequestItemId": item_a, "quantity": 2, "unitPrice": 450000}]})
    po_a = data(r).get("purchaseOrderId")
    if not check("Hoàn tiền: lập đơn mua giá thấp hơn giá báo", s == 201 and po_a, r):
        return

    s, r = call("POST", f"/api/purchase-orders/{po_a}/submit", "sale")
    po = data(r)
    check("Giá thực thấp hơn giá báo → vẫn sang chờ Admin duyệt, không bắt khách làm gì",
          s == 200 and po.get("status") == "PENDING_APPROVAL" and po.get("priceDifferenceAmount") == 0, r)
    check("Giá thực thấp hơn giá báo → sinh khoản PHẢI HOÀN đúng phần thừa",
          po.get("refundAmount") == 100000 and po.get("refundStatus") == "PENDING"
          and po.get("refundType") == "REFUND_PRICE_DIFF", po)
    check("Khoản hoàn không lẫn vào ô chênh giá phải thu thêm",
          po.get("priceDifferencePaymentId") is None, po)

    # Gửi duyệt lại không được đẻ thêm dòng hoàn
    s, r = call("POST", f"/api/purchase-orders/{po_a}/decide", "admin", {"approve": False, "note": "xem lại"})
    s, r = call("POST", f"/api/purchase-orders/{po_a}/submit", "sale")
    check("Gửi duyệt lại → cập nhật khoản hoàn, không tạo thêm dòng",
          s == 200 and data(r).get("refundAmount") == 100000, r)
    s, r = call("GET", f"/api/purchase-requests/{request_id}/payments", "customer")
    refunds = [x for x in data(r).get("payments", []) if str(x.get("paymentType", "")).startswith("REFUND")]
    check("Sổ tiền của khách chỉ có MỘT khoản hoàn đang chờ", len(refunds) == 1, refunds)

    s, r = call("POST", f"/api/purchase-orders/{po_a}/refund/complete", "sale", {})
    check("Xác nhận hoàn tiền bắt buộc có mã giao dịch", s == 400, r)
    s, r = call("POST", f"/api/purchase-orders/{po_a}/refund/complete", "customer", {"transactionCode": "X"})
    expect("Khách không tự xác nhận đã được hoàn tiền", s, r, 403)
    s, r = call("POST", f"/api/purchase-orders/{po_a}/refund/complete", "sale", {"transactionCode": f"RF-{RUN}"})
    done = data(r)
    check("Sale xác nhận đã hoàn tiền → khoản hoàn đóng sổ, có mốc thời gian",
          s == 200 and done.get("refundStatus") == "REFUNDED" and done.get("refundedAt"), r)
    check("Đơn mua KHÔNG bị đổi trạng thái vì hoàn tiền", done.get("status") == "PENDING_APPROVAL", done)
    s, r = call("POST", f"/api/purchase-orders/{po_a}/refund/complete", "sale", {"transactionCode": f"RF-{RUN}-2"})
    check("Hoàn xong rồi thì không hoàn lần hai", s == 404, r)

    # ── B. Huỷ sau khi đã đặt NCC: báo 3 × 200.000 = 600.000
    s, r = call("POST", f"/api/purchase-requests/{request_id}/purchase-orders", "sale", {
        "supplierId": supplier_id, "warehouseId": KR, "currency": "VND",
        "items": [{"purchaseRequestItemId": item_b, "quantity": 3, "unitPrice": 200000}]})
    po_b = data(r).get("purchaseOrderId")
    if not check("Huỷ đơn: lập đơn mua đúng giá báo", s == 201 and po_b, r):
        return
    call("POST", f"/api/purchase-orders/{po_b}/submit", "sale")
    call("POST", f"/api/purchase-orders/{po_b}/decide", "admin", {"approve": True, "note": "OK"})
    upload("sale", "PURCHASE_ORDER", po_b, "PURCHASE_PROOF", "don-ncc-huy.png")
    s, r = call("POST", f"/api/purchase-orders/{po_b}/place", "sale", {"supplierOrderCode": f"KR-H{RUN}"})
    if not check("Huỷ đơn: đã đặt NCC", s == 200 and data(r).get("status") == "ORDERED", r):
        return

    s, r = call("POST", f"/api/purchase-orders/{po_b}/cancel", "sale", {"reason": "NCC báo hết hàng"})
    expect("Đã đặt NCC thì Sale không huỷ được", s, r, 403)
    s, r = call("POST", f"/api/purchase-orders/{po_b}/cancel", "admin", {})
    check("Huỷ đơn đã đặt NCC vẫn bắt buộc ghi lý do", s == 400, r)
    s, r = call("POST", f"/api/purchase-orders/{po_b}/cancel", "admin", {"reason": "NCC báo hết hàng"})
    cancelled = data(r)
    check("Admin huỷ đơn đã đặt NCC", s == 200 and cancelled.get("status") == "CANCELLED", r)
    check("Huỷ sau khi đặt NCC → hoàn tiền hàng đã báo, chưa cấu hình phí thì hoàn đủ",
          cancelled.get("refundAmount") == 600000 and cancelled.get("refundType") == "REFUND_CANCEL"
          and cancelled.get("refundStatus") == "PENDING", cancelled)
    check("Phí huỷ mặc định bằng 0 — hệ thống không tự trừ tiền khách",
          cancelled.get("cancelFeeRate") == 0 and cancelled.get("cancelFeeAmount") == 0, cancelled)

    # ── C. Admin đặt phí huỷ trong bảng phí → lần huỷ sau trừ đúng số đó.
    #    Đây là đường mà màn "Tham số vận hành" của Admin đi: ghi một dòng vào
    #    ADDITIONAL_SERVICE_FEES rồi backend đọc ngay, không phải sửa mã nguồn.
    s, r = call("POST", "/api/additional-service-fees", "admin", {
        "feeName": "Phí huỷ đơn mua NCC", "feeCode": "PURCHASE_CANCEL_FEE_RATE",
        "calculationType": "PERCENTAGE", "value": 10, "unit": "%", "isActive": True,
        "description": "Kiểm thử phí huỷ"})
    fee = data(r)
    fee_id = fee.get("id") or fee.get("feeId")
    if not check("Admin tạo được tham số phí huỷ bằng API", s in (200, 201) and fee_id, r):
        return

    s, r = call("POST", "/api/additional-service-fees", "sale", {
        "feeName": "X", "feeCode": f"X{RUN}", "calculationType": "FIXED", "value": 1})
    expect("Sale không sửa được tham số hệ thống", s, r, 403)

    try:
        s, r = call("POST", f"/api/purchase-requests/{request_id}/purchase-orders", "sale", {
            "supplierId": supplier_id, "warehouseId": KR, "currency": "VND",
            "items": [{"purchaseRequestItemId": item_b, "quantity": 3, "unitPrice": 200000}]})
        po_c = data(r).get("purchaseOrderId")
        if not check("Huỷ có phí: lập lại đơn mua cho cùng sản phẩm", s == 201 and po_c, r):
            return
        call("POST", f"/api/purchase-orders/{po_c}/submit", "sale")
        call("POST", f"/api/purchase-orders/{po_c}/decide", "admin", {"approve": True, "note": "OK"})
        upload("sale", "PURCHASE_ORDER", po_c, "PURCHASE_PROOF", "don-ncc-huy-2.png")
        s, r = call("POST", f"/api/purchase-orders/{po_c}/place", "sale", {"supplierOrderCode": f"KR-H2{RUN}"})
        if not check("Huỷ có phí: đã đặt NCC", s == 200 and data(r).get("status") == "ORDERED", r):
            return

        s, r = call("POST", f"/api/purchase-orders/{po_c}/cancel", "admin", {"reason": "Khách đổi ý"})
        withfee = data(r)
        # tiền hàng đã báo 600.000, phí huỷ 10% = 60.000 → hoàn 540.000
        check("Đặt phí huỷ 10% → hoàn 540.000 và ghi rõ đã trừ 60.000",
              s == 200 and withfee.get("refundAmount") == 540000
              and withfee.get("cancelFeeRate") == 10 and withfee.get("cancelFeeAmount") == 60000, withfee)
    finally:
        # Trả môi trường về nguyên trạng: lần chạy sau phải thấy mặc định 0 như cũ.
        call("DELETE", f"/api/additional-service-fees/{fee_id}", "admin")

    s, r = call("GET", f"/api/purchase-orders/{po_b}", "sale")
    check("Xoá tham số → các đơn huỷ trước đó giữ nguyên số tiền đã chốt",
          s == 200 and data(r).get("refundAmount") == 600000, r)


def customer_dashboard():
    """
    Một lời gọi trả đủ việc khách phải tự làm, gộp cả hai luồng.

    Trọng tâm kiểm: đúng bộ việc, đúng phạm vi khách (không lộ đơn của người khác), và
    khoản HOÀN mà đoạn trên vừa tạo phải hiện ra — trước đây màn hình chỉ đếm đơn ký gửi
    nên tiền hoàn của mua hộ không bao giờ xuất hiện.
    """
    s, r = call("GET", "/api/customers/me/dashboard", "customer")
    board = data(r)
    if not check("Khách lấy được bảng việc cần làm", s == 200 and isinstance(board, dict), r):
        return

    keys = [a.get("key") for a in board.get("actions", [])]
    check("Trả đủ 5 nhóm việc, đúng thứ tự đã thống nhất",
          keys == ["QUOTATION_CONFIRM", "PAYMENT_DUE", "PURCHASE_PRICE_DECISION",
                   "DELIVERY_CONFIRM", "REFUND_INCOMING"], keys)

    by_key = {a["key"]: a for a in board.get("actions", [])}

    refund = by_key.get("REFUND_INCOMING", {})
    check("Khoản hoàn của mua hộ hiện trong việc cần làm",
          refund.get("count", 0) >= 1 and refund.get("amount", 0) >= 600000
          and refund.get("purchaseCount", 0) >= 1, refund)

    pay = by_key.get("PAYMENT_DUE", {})
    check("Tổng phải trả khớp với thẻ khoản chờ thanh toán",
          board.get("totalAmountDue") == pay.get("amount"), (board.get("totalAmountDue"), pay.get("amount")))
    check("Tổng sẽ hoàn khớp với thẻ hoàn tiền",
          board.get("totalRefundIncoming") == refund.get("amount"), board.get("totalRefundIncoming"))

    check("Mỗi dòng ví dụ đều nói rõ thuộc luồng nào",
          all(i.get("kind") in ("CONSIGNMENT", "PURCHASE")
              for a in board["actions"] for i in a.get("items", [])), keys)
    check("Không thẻ nào trả quá 5 dòng ví dụ",
          all(len(a.get("items", [])) <= 5 for a in board["actions"]), keys)

    progress = board.get("inProgress") or {}
    check("Hàng đang chạy: tổng bằng đúng tổng các chặng",
          progress.get("total") == sum(progress.get(k, 0) for k in
              ("waitingStaff", "waitingGoods", "atOriginWarehouse",
               "inTransit", "atVietnamWarehouse", "outForDelivery")), progress)

    # Phạm vi: khách khác không được thấy mã đơn của khách này.
    my_codes = {i.get("code") for a in board["actions"] for i in a.get("items", [])}
    s, r = call("GET", "/api/customers/me/dashboard", "customer2")
    other = data(r)
    other_codes = {i.get("code") for a in other.get("actions", []) for i in a.get("items", [])}
    check("Khách khác có bảng riêng, không lẫn đơn của người này",
          s == 200 and not (my_codes & other_codes), sorted(my_codes & other_codes)[:3])

    # Tài khoản nhân viên không có hồ sơ khách: trả bảng rỗng chứ không lỗi.
    s, r = call("GET", "/api/customers/me/dashboard", "sale")
    staff = data(r)
    check("Tài khoản không phải khách → bảng rỗng, không ném lỗi",
          s == 200 and all(a.get("count", 0) == 0 for a in staff.get("actions", [])), r)

    s, r = call("GET", "/api/customers/me/dashboard", None)
    expect("Chưa đăng nhập thì không xem được", s, r, 401)


def staff_adds_address():
    """
    Sale tạo đơn hộ khách mà khách chưa có địa chỉ nào thì trước đây tắc: đọc được
    sổ địa chỉ nhưng không có đường ghi. Endpoint mới chỉ dành cho nhân viên.
    """
    # test_ids.json lưu id TÀI KHOẢN; sổ địa chỉ gắn với hồ sơ KHÁCH, hai id khác nhau.
    s, r = call("GET", "/api/purchase-requests?pageSize=1", "customer")
    rows = data(r)
    rows = rows.get("items", rows) if isinstance(rows, dict) else rows
    customer_id = (rows or [{}])[0].get("customerId")
    if not check("Lấy được customerId của khách để thử sổ địa chỉ", bool(customer_id), r):
        return

    s, r = call("GET", f"/api/customers/{customer_id}/delivery-addresses", "sale")
    if not check("Nhân viên đọc được sổ địa chỉ của khách", s == 200, r):
        return
    before = len(data(r) or [])

    address = f"So {RUN} duong Thu Nghiem, Quan 1, TP HCM"
    s, r = call("POST", f"/api/customers/{customer_id}/delivery-addresses", "customer", {"address": address})
    expect("Khách không dùng endpoint của nhân viên", s, r, 403)
    s, r = call("POST", f"/api/customers/{customer_id}/delivery-addresses", "sale", {"address": "   "})
    check("Địa chỉ rỗng bị chặn", s == 400, r)
    s, r = call("POST", f"/api/customers/{customer_id}/delivery-addresses", "sale", {"address": address})
    created = data(r)
    check("Sale thêm địa chỉ hộ khách", s == 200 and created.get("id"), r)

    s, r = call("POST", f"/api/customers/{customer_id}/delivery-addresses", "sale", {"address": address})
    check("Bấm lại cùng địa chỉ → trả lại dòng cũ, không nhân đôi sổ",
          s == 200 and data(r).get("id") == created.get("id"), r)

    s, r = call("POST", "/api/customers/00000000-0000-0000-0000-000000000001/delivery-addresses", "sale", {"address": address})
    check("Khách không tồn tại → 404, không tự đẻ hồ sơ khách", s == 404, r)

    s, r = call("GET", f"/api/customers/{customer_id}/delivery-addresses", "customer")
    expect("Khách không đọc sổ địa chỉ qua đường của nhân viên", s, r, 403)

    s, r = call("GET", f"/api/customers/{customer_id}/delivery-addresses", "sale")
    check("Sổ địa chỉ của khách tăng đúng một dòng", s == 200 and len(data(r) or []) == before + 1, r)


if __name__ == "__main__":
    sys.exit(main())
