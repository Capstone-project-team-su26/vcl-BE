#!/usr/bin/env python3
"""
Nhãn "Mua hộ" / "Ký gửi" trên các màn XUẤT KHO + chặn chốt biên bản kiểm đếm khi chưa có kiện.

  A. Kiện mua hộ (đơn kho OrderType=PURCHASE) và kiện ký gửi cùng đi một phiếu xuất / một lô:
     - eligible-parcels, chi tiết phiếu xuất (parcels[]), danh sách phiếu (purchaseParcelCount),
       ready-wro-requests, lô vận chuyển (tạo + chi tiết) đều trả isPurchase / orderType / purchaseCode;
     - kiện ký gửi isPurchase=false, orderType=CONSIGNMENT, purchaseCode=null;
     - không lộ NCC (supplierName / supplierOrderCode) ở các DTO này.
  B. Chốt biên bản kiểm đếm:
     - tổng thực tế > 0 mà phiếu chưa có kiện check-in và không gửi kèm kiện → 400 (câu tiếng Việt),
       biên bản không bị ghi;
     - gửi kèm "kiện cân đo" trong cùng lần chốt → OK;
     - đã check-in trước rồi chốt không kèm kiện → OK;
     - tổng thực tế = 0 → chốt được nhưng vào luồng lệch (không tự duyệt).

    APP_ENV=/root/vcl-migration-deploy/app.env API=http://127.0.0.1:8088 python3 export_purchase_label_and_count_guard_flow.py
"""
import sys
import urllib.parse

from wave1_consignment_flow import IDS, PASSED, FAILED, RUN, call, check, expect, item
from export_flow import KR, set_region, store, upload, route_for_kr, eligible
from purchase_flow import create_request, request_items, pay_manually, data
from warehouse_zones_flow import received_parcels
from receiving_discrepancy_decision_flow import paid_note, parcel as parcel_body

GUARD_MSG = "Chưa có kiện nào được check-in theo phiếu này"
SUPPLIER_KEYS = ("supplierName", "supplierOrderCode", "supplierId")


def find(rows, parcel_id):
    return next((x for x in rows or [] if str(x.get("parcelId", "")).lower() == str(parcel_id).lower()), None)


def label_ok(row, purchase_code):
    if not row:
        return False
    if purchase_code is None:
        return row.get("isPurchase") is False and row.get("orderType") == "CONSIGNMENT" and row.get("purchaseCode") is None
    return row.get("isPurchase") is True and row.get("orderType") == "PURCHASE" and row.get("purchaseCode") == purchase_code


def no_supplier(rows):
    return all(not any(k in (r or {}) for k in SUPPLIER_KEYS) for r in rows or [])


def place_purchase_orders(suffix):
    """Yêu cầu mua hộ 2 món → trả trước → 2 đơn mua NCC (mỗi món một đơn) → đặt NCC → 2 phiếu tiếp nhận ACTIVE."""
    s, r = call("POST", "/api/suppliers", "admin", {
        "supplierCode": f"NCC-{suffix}", "supplierName": f"NCC Xuat {suffix}", "supplierType": "GOODS",
        "country": "KR", "email": f"ncc.{suffix.lower()}@example.com"})
    supplier_id = data(r).get("id") or data(r).get("supplierId")
    if not check("Admin tạo NCC", s in (200, 201) and supplier_id, r):
        return None
    s, request_id, d = create_request(suffix)
    if not check("Khách tạo yêu cầu mua hộ", s in (200, 201) and request_id, d):
        return None
    purchase_code = d.get("purchaseCode") or data(call("GET", f"/api/purchase-requests/{request_id}", "sale")[1]).get("purchaseCode")
    items = request_items(request_id)
    item_a, item_b = items[f"Tai nghe {suffix}"], items[f"Ao khoac {suffix}"]
    s, r = call("POST", f"/api/purchase-requests/{request_id}/quotation", "sale", {
        "purchaseFee": 80000, "domesticShippingFee": 30000, "freightRatePerKg": 120000, "estimatedWeight": 2,
        "items": [{"purchaseRequestItemId": item_a, "unitPrice": 500000},
                  {"purchaseRequestItemId": item_b, "unitPrice": 200000}]})
    if not check("Sale báo giá", s in (200, 201), r):
        return None
    s, r = call("PUT", f"/api/purchase-requests/{request_id}/quotation/confirm-and-pay", "customer", {"paymentMethod": "SEPAY"})
    pay = data(r)
    s, r = pay_manually(pay.get("paymentId"), pay.get("amount"), f"PRE-{suffix}")
    expect("Khách trả trước", s, r, 200)

    notes = []
    for idx, (req_item, qty, price) in enumerate(((item_a, 2, 500000), (item_b, 3, 200000)), start=1):
        s, r = call("POST", f"/api/purchase-requests/{request_id}/purchase-orders", "sale", {
            "supplierId": supplier_id, "warehouseId": KR, "currency": "VND",
            "items": [{"purchaseRequestItemId": req_item, "quantity": qty, "unitPrice": price}]})
        po = data(r)
        if not check(f"Sale lập đơn mua NCC #{idx}", s == 201 and po.get("purchaseOrderId"), r):
            return None
        po_id = po["purchaseOrderId"]
        call("POST", f"/api/purchase-orders/{po_id}/submit", "sale")
        s, r = call("POST", f"/api/purchase-orders/{po_id}/decide", "admin", {"approve": True, "note": "OK"})
        check(f"Admin duyệt đơn mua #{idx}", s == 200 and data(r).get("status") == "APPROVED", r)
        upload("sale", "PURCHASE_ORDER", po_id, "PURCHASE_PROOF", "don-ncc.png")
        s, r = call("POST", f"/api/purchase-orders/{po_id}/place", "sale", {"supplierOrderCode": f"TB-{suffix}-{idx}"})
        placed = data(r)
        if not check(f"Đặt NCC #{idx} → phiếu tiếp nhận ACTIVE", s == 200 and placed.get("receivingNoteStatus") == "ACTIVE", r):
            return None
        s, r = call("GET", f"/api/warehouse-receiving-notes/scan?code={placed['receivingNoteCode']}", "staff")
        notes.append({"code": placed["receivingNoteCode"], "id": data(r).get("id") or data(r).get("receivingNoteId"),
                      "orderId": placed["warehouseOrderId"], "expected": data(r).get("expectedItems") or []})
    return purchase_code, notes


def note_detail(note_id):
    s, r = call("GET", f"/api/warehouse-receiving-notes/{note_id}", "staff")
    return s, data(r) or {}


def part_labels(purchase_code, pnote):
    route_id = route_for_kr()
    if not check("Có tuyến xuất từ kho Seoul", bool(route_id), route_id):
        return

    # Kiện mua hộ: check-in theo phiếu mua hộ rồi xếp kệ.
    exp = pnote["expected"][0]
    s, r = call("POST", "/api/parcels/check-in", "staff", {"shippingCode": pnote["code"], "packages": [
        {"orderItemId": exp["orderItemId"], "actualWeight": 1.2, "length": 30, "width": 20, "height": 10}]})
    d = data(r)
    p_parcel = ((d.get("parcels") or [d])[0] or {}).get("parcelId")
    if not check("Kho check-in kiện mua hộ theo phiếu", s in (200, 201) and p_parcel, r):
        return
    s, r = store(p_parcel, "BIN-A1-02")
    if not check("Xếp kệ kiện mua hộ", s == 200, r):
        return

    # Kiện ký gửi.
    s, _, parcels = received_parcels(["Ky gui XL"], f"XL{RUN}")
    c_parcel = parcels.get("Ky gui XL")
    if not check("Check-in kiện ký gửi", s in (200, 201) and c_parcel, parcels):
        return
    s, r = store(c_parcel, "BIN-A1-02")
    if not check("Xếp kệ kiện ký gửi", s == 200, r):
        return

    # ---------------- eligible-parcels
    s, rows = eligible(route_id)
    pr, cr = find(rows, p_parcel), find(rows, c_parcel)
    check("eligible-parcels: kiện mua hộ isPurchase=true, orderType=PURCHASE, purchaseCode", label_ok(pr, purchase_code), pr)
    check("eligible-parcels: kiện ký gửi isPurchase=false, orderType=CONSIGNMENT", label_ok(cr, None), cr)
    check("eligible-parcels: không lộ NCC", no_supplier([pr, cr]), [pr, cr])
    s, r = call("GET", f"/api/warehouse-release-requests/eligible-parcels?shippingRouteId={route_id}"
                       f"&search={urllib.parse.quote(purchase_code)}", "staff")
    found = [x["parcelId"].lower() for x in (data(r) or [])]
    check("eligible-parcels: tìm theo mã yêu cầu mua hộ ra kiện mua hộ", s == 200 and p_parcel.lower() in found, found)

    # ---------------- Phiếu xuất kho
    s, r = call("POST", "/api/warehouse-release-requests", "staff",
                {"shippingRouteId": route_id, "parcelIds": [p_parcel, c_parcel], "note": "Nhãn mua hộ"})
    if not expect("Lập phiếu xuất gồm 1 kiện mua hộ + 1 kiện ký gửi", s, r, 201):
        return
    wro = data(r)
    wro_id = wro["id"]
    check("Phiếu mới: purchaseParcelCount = 1", wro.get("purchaseParcelCount") == 1, wro.get("purchaseParcelCount"))
    s, r = call("GET", f"/api/warehouse-release-requests/{wro_id}", "staff")
    rows = data(r).get("parcels", [])
    check("Chi tiết phiếu: kiện mua hộ có nhãn + mã yêu cầu", label_ok(find(rows, p_parcel), purchase_code), rows)
    check("Chi tiết phiếu: kiện ký gửi nhãn Ký gửi", label_ok(find(rows, c_parcel), None), rows)
    check("Chi tiết phiếu: không lộ NCC", no_supplier(rows), rows)
    s, r = call("GET", f"/api/warehouse-release-requests?search={wro['wroCode']}&pageSize=50", "om1")
    item_ = next((x for x in data(r).get("items", []) if x["id"] == wro_id), None)
    check("Danh sách phiếu (OM): purchaseParcelCount = 1 / parcelCount = 2",
          item_ and item_.get("purchaseParcelCount") == 1 and item_.get("parcelCount") == 2, item_)

    call("POST", f"/api/warehouse-release-requests/{wro_id}/submit", "staff")
    upload("om1", "WRO", wro_id, "WRO_APPROVAL_PROOF", "anh-lo-hang.png")
    s, r = call("POST", f"/api/warehouse-release-requests/{wro_id}/decide", "om1", {"decision": "APPROVED", "reason": "OK"})
    check("OM duyệt phiếu", s == 200 and data(r)["status"] == "APPROVED", r)
    for pid in (p_parcel, c_parcel):
        s, r = call("POST", f"/api/warehouse-release-requests/{wro_id}/pick", "staff",
                    {"parcelId": pid, "stagingBinCode": "BIN-OUT-01"})
    check("Bốc đủ 2 kiện → READY", s == 200 and data(r)["status"] == "READY", r)
    rows = data(r).get("parcels", [])
    check("Phản hồi bốc hàng vẫn giữ nhãn", label_ok(find(rows, p_parcel), purchase_code) and label_ok(find(rows, c_parcel), None), rows)

    # ---------------- Lô vận chuyển
    s, r = call("GET", f"/api/international-shipments/ready-wro-requests?originWarehouseId={KR}", "staff")
    ready = next((x for x in (data(r) or []) if x.get("wroRequestId") == wro_id), None)
    check("Chờ lập lô: phiếu có purchaseParcelCount = 1 / parcelCount = 2",
          ready and ready.get("purchaseParcelCount") == 1 and ready.get("parcelCount") == 2, ready)

    s, r = call("POST", "/api/international-shipments", "staff", {"wroRequestIds": [wro_id], "shippingMethod": "AIR"})
    if not expect("Lập lô vận chuyển", s, r, 201):
        return
    lot = data(r)
    lot_id = lot.get("shipmentId") or lot.get("id")
    rows = lot.get("parcels", [])
    check("Tạo lô: kiện mua hộ / ký gửi đúng nhãn",
          label_ok(find(rows, p_parcel), purchase_code) and label_ok(find(rows, c_parcel), None), rows)
    s, r = call("GET", f"/api/international-shipments/{lot_id}", "staff")
    rows = data(r).get("parcels", [])
    check("Chi tiết lô: kiện mua hộ / ký gửi đúng nhãn",
          label_ok(find(rows, p_parcel), purchase_code) and label_ok(find(rows, c_parcel), None), rows)
    check("Chi tiết lô: không lộ NCC", no_supplier(rows), rows)
    s, r = call("GET", f"/api/international-shipments?search={lot.get('shipmentCode', '')}&pageSize=20", "staff")
    lst = data(r)
    lst_items = lst.get("items") or lst.get("data") or [] if isinstance(lst, dict) else lst
    one = next((x for x in lst_items if (x.get("shipmentId") or x.get("id")) == lot_id), None)
    check("Danh sách lô: kiện trong lô có nhãn", one is not None
          and label_ok(find(one.get("parcels"), p_parcel), purchase_code), one and one.get("parcels"))
    s, r = call("GET", f"/api/international-shipments/{lot_id}", "customer")
    expect("Khách không xem được lô vận chuyển", s, r, 403)

    # Dọn: huỷ lô nháp để phiếu về chờ lập lô (không vướng kịch bản khác).
    s, r = call("POST", f"/api/international-shipments/{lot_id}/cancel", "staff")
    expect("Huỷ lô nháp sau kiểm thử", s, r, 200)


def part_count_guard(pnote):
    # ---------------- B1. Phiếu mua hộ chưa có kiện nào → chốt không kèm kiện bị chặn
    exp = pnote["expected"][0]
    body = {"items": [{"orderItemId": exp["orderItemId"], "actualQuantity": exp["quantity"], "actualWeight": 1.5}]}
    s, r = call("POST", f"/api/warehouse-receiving-notes/{pnote['id']}/confirm-receipt", "staff", body)
    check("Chốt biên bản khi chưa check-in kiện nào → 400 câu tiếng Việt",
          s == 400 and GUARD_MSG in (r.get("message") or ""), (s, r))
    s, d = note_detail(pnote["id"])
    check("Bị chặn thì biên bản không ghi, phiếu vẫn chờ nhận",
          s == 200 and d.get("status") == "ACTIVE" and not d.get("items") and not d.get("parcels"), d)

    # ---------------- B2. Gửi kèm kiện cân đo trong cùng lần chốt → OK
    body["parcels"] = [{"orderItemId": exp["orderItemId"], "actualWeight": 1.5, "length": 30, "width": 20, "height": 15}]
    s, r = call("POST", f"/api/warehouse-receiving-notes/{pnote['id']}/confirm-receipt", "staff", body)
    d = data(r) or {}
    check("Chốt kèm kiện cân đo → OK, phiếu tự chốt (khớp số lượng), có 1 kiện",
          s == 200 and d.get("status") == "APPROVED" and len(d.get("parcels") or []) == 1, (s, r))

    # ---------------- B3. Đã check-in (một phần) trước rồi chốt không kèm kiện → OK
    # (check-in đủ mọi dòng thì phiếu tự chốt luôn, nên kịch bản thật là check-in một phần rồi chốt biên bản)
    made = paid_note([item(f"Ao CG{RUN}", 2, 1.0), item(f"Quan CG{RUN}", 1, 1.0)], "CG")
    if not made:
        return
    _, _, note_id, note_code, expected = made
    shirt, pants = expected[f"Ao CG{RUN}"], expected[f"Quan CG{RUN}"]
    s, r = call("POST", "/api/parcels/check-in", "staff",
                {"shippingCode": note_code, "packages": [parcel_body(shirt, 2.0)]})
    check("Check-in một phần kiện trước khi chốt", s in (200, 201), r)
    s, d = note_detail(note_id)
    check("Check-in một phần → phiếu nhận một phần", d.get("status") == "PARTIALLY_RECEIVED", d.get("status"))
    s, r = call("POST", f"/api/warehouse-receiving-notes/{note_id}/confirm-receipt", "staff",
                {"items": [{"orderItemId": shirt["orderItemId"], "actualQuantity": 2, "actualWeight": 2.0},
                           {"orderItemId": pants["orderItemId"], "actualQuantity": 1, "actualWeight": 1.0}]})
    check("Đã có kiện check-in thì chốt biên bản không kèm kiện được",
          s == 200 and data(r).get("status") == "APPROVED", (s, r))

    # ---------------- B4. Tổng thực tế = 0 → chốt được nhưng vào luồng lệch, không tự duyệt
    made = paid_note([item(f"Ao CZ{RUN}", 2, 1.0), item(f"Quan CZ{RUN}", 1, 1.0)], "CZ")
    if not made:
        return
    _, _, note_id, _, expected = made
    body = {"items": [{"orderItemId": e["orderItemId"], "actualQuantity": 0, "note": "NCC/khách không giao"}
                      for e in expected.values()]}
    s, r = call("POST", f"/api/warehouse-receiving-notes/{note_id}/confirm-receipt", "staff", body)
    d = data(r) or {}
    check("Kiểm đếm ra 0 món → chốt được (không đòi kiện)", s == 200, (s, r))
    check("… nhưng không tự duyệt: chờ quản lý kho quyết lệch",
          d.get("status") != "APPROVED" and d.get("approvalStage") == "DISCREPANCY" and d.get("requiresReview") is True,
          {k: d.get(k) for k in ("status", "approvalStage", "requiresReview", "awaitingApproval")})


def main():
    call("PUT", f"/api/warehouses/{KR}/manager", "admin", {"managerId": None, "reason": "Kiểm thử nhãn xuất kho"})
    set_region("staff", "KR")

    made = place_purchase_orders(f"XP{RUN}")
    if not made:
        print(f"\n{len(PASSED)} PASS, {len(FAILED)} FAIL")
        return 1
    purchase_code, (note_a, note_b) = made

    part_labels(purchase_code, note_a)
    part_count_guard(note_b)

    print(f"\n{len(PASSED)} PASS, {len(FAILED)} FAIL")
    if FAILED:
        print("Lỗi: " + ", ".join(FAILED))
    return 1 if FAILED else 0


if __name__ == "__main__":
    sys.exit(main())
