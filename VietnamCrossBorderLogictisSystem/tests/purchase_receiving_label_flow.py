#!/usr/bin/env python3
"""
Phiếu tiếp nhận kho của hàng MUA HỘ phải nhận ra được và tìm được theo mã NCC.

Kiện NCC giao tới kho chỉ in mã đơn bên NCC (Taobao/1688/Shopee…) hoặc mã vận đơn nội địa; trước
đây kho chỉ tìm được phiếu theo mã WRN-, mã đơn VCL, tên khách nên không khớp được. Kiểm:

  - danh sách / chi tiết / quét phiếu trả orderType, isPurchase, purchaseCode, purchaseOrderCode,
    supplierName, supplierOrderCode, domesticTrackingCode;
  - ô tìm của danh sách khớp mã đơn NCC (bỏ khoảng trắng, không phân biệt hoa thường), mã PO-,
    mã yêu cầu mua hộ, mã vận đơn nội địa; lọc nhanh orderType=PURCHASE / CONSIGNMENT;
  - API của khách không lộ NCC / mã đơn NCC;
  - nhân viên kho đã được gán kho chỉ thấy / quét được phiếu của kho được gán (403 nêu tên kho).

    APP_ENV=/root/vcl-migration-deploy/app.env API=http://127.0.0.1:8088 python3 purchase_receiving_label_flow.py
"""
import sys
import urllib.parse

from wave1_consignment_flow import IDS, PASSED, FAILED, RUN, call, check, expect
from export_flow import KR, set_region, upload
from purchase_flow import create_request, request_items, pay_manually, data
from warehouse_assignment_flow import create_staff, assign

CN = IDS["warehouseCn"]


def notes(who, search="", **params):
    q = {"search": search, "pageSize": 200, **params}
    return call("GET", "/api/warehouse-receiving-notes?" + urllib.parse.urlencode(q), who)


def codes(resp):
    return [n["receivingNoteCode"] for n in (data(resp) or {}).get("items", [])]


def main():
    call("PUT", f"/api/warehouses/{KR}/manager", "admin", {"managerId": None, "reason": "Kiểm thử nhãn mua hộ"})
    set_region("staff", "KR")

    s, r = call("POST", "/api/suppliers", "admin", {
        "supplierCode": f"NCC-L{RUN}", "supplierName": f"Taobao Shop {RUN}", "supplierType": "GOODS",
        "country": "KR", "email": f"ncc.l{RUN}@example.com"})
    supplier_id = data(r).get("id") or data(r).get("supplierId")
    if not check("Admin tạo NCC", s in (200, 201) and supplier_id, r):
        return 1

    # ---------------- Yêu cầu → báo giá → trả trước → đơn mua → duyệt → đặt NCC có mã đơn NCC + ảnh
    suffix = f"L{RUN}"
    s, request_id, d = create_request(suffix)
    if not check("Khách tạo yêu cầu mua hộ", s in (200, 201) and request_id, d):
        return 1
    purchase_code = d.get("purchaseCode") or data(call("GET", f"/api/purchase-requests/{request_id}", "sale")[1]).get("purchaseCode")
    items = request_items(request_id)
    item_a, item_b = items[f"Tai nghe {suffix}"], items[f"Ao khoac {suffix}"]

    s, r = call("POST", f"/api/purchase-requests/{request_id}/quotation", "sale", {
        "purchaseFee": 80000, "domesticShippingFee": 30000, "freightRatePerKg": 120000, "estimatedWeight": 2,
        "items": [{"purchaseRequestItemId": item_a, "unitPrice": 500000},
                  {"purchaseRequestItemId": item_b, "unitPrice": 200000}]})
    if not check("Sale báo giá", s in (200, 201), r):
        return 1
    s, r = call("PUT", f"/api/purchase-requests/{request_id}/quotation/confirm-and-pay", "customer", {"paymentMethod": "SEPAY"})
    pay = data(r)
    s, r = pay_manually(pay.get("paymentId"), pay.get("amount"), "PRE-L")
    expect("Khách trả trước", s, r, 200)

    s, r = call("POST", f"/api/purchase-requests/{request_id}/purchase-orders", "sale", {
        "supplierId": supplier_id, "warehouseId": KR, "currency": "VND",
        "items": [{"purchaseRequestItemId": item_a, "quantity": 2, "unitPrice": 500000},
                  {"purchaseRequestItemId": item_b, "quantity": 3, "unitPrice": 200000}]})
    po = data(r)
    if not check("Sale lập đơn mua NCC", s == 201 and po.get("purchaseOrderId"), r):
        return 1
    po_id, po_code = po["purchaseOrderId"], po["purchaseOrderCode"]
    call("POST", f"/api/purchase-orders/{po_id}/submit", "sale")
    s, r = call("POST", f"/api/purchase-orders/{po_id}/decide", "admin", {"approve": True, "note": "OK"})
    check("Admin duyệt đơn mua", s == 200 and data(r).get("status") == "APPROVED", r)
    upload("sale", "PURCHASE_ORDER", po_id, "PURCHASE_PROOF", "don-ncc.png")
    supplier_code = f"TB-{RUN}-8866"
    s, r = call("POST", f"/api/purchase-orders/{po_id}/place", "sale", {"supplierOrderCode": supplier_code})
    placed = data(r)
    if not check("Sale đặt NCC → phiếu tiếp nhận ACTIVE", s == 200 and placed.get("receivingNoteStatus") == "ACTIVE", r):
        return 1
    note_code = placed["receivingNoteCode"]
    order_id = placed["warehouseOrderId"]

    # ---------------- Danh sách: nhãn + trường NCC
    s, r = notes("staff", note_code)
    rows = data(r).get("items", [])
    row = rows[0] if rows else {}
    check("Danh sách: phiếu mua hộ có orderType=PURCHASE, isPurchase",
          s == 200 and len(rows) == 1 and row.get("orderType") == "PURCHASE" and row.get("isPurchase") is True, r)
    check("Danh sách: có mã yêu cầu, mã PO, NCC, mã đơn NCC",
          row.get("purchaseCode") == purchase_code and row.get("purchaseOrderCode") == po_code
          and row.get("supplierName") == f"Taobao Shop {RUN}" and row.get("supplierOrderCode") == supplier_code, row)
    check("Danh sách: chưa phát hàng thì chưa có vận đơn nội địa", row.get("domesticTrackingCode") is None, row)
    note_id = row.get("id")

    # ---------------- Tìm theo mã NCC (bỏ khoảng trắng, không phân biệt hoa thường)
    s, r = notes("staff", supplier_code)
    check("Tìm đúng mã đơn NCC ra đúng phiếu", s == 200 and codes(r) == [note_code], codes(r))
    messy = f"  tb-{RUN.lower()} -88 66 "
    s, r = notes("staff", messy)
    check("Tìm mã đơn NCC viết thường, có khoảng trắng vẫn ra phiếu", s == 200 and codes(r) == [note_code], codes(r))
    s, r = notes("staff", f"{RUN.lower()}-8866")
    check("Tìm một phần mã đơn NCC vẫn ra phiếu", s == 200 and note_code in codes(r), codes(r))
    s, r = notes("staff", po_code.lower())
    check("Tìm theo mã đơn mua PO- ra phiếu", s == 200 and codes(r) == [note_code], codes(r))
    s, r = notes("staff", purchase_code)
    check("Tìm theo mã yêu cầu mua hộ ra phiếu", s == 200 and note_code in codes(r), codes(r))
    s, r = notes("staff", note_code.lower())
    check("Tìm theo mã WRN- (viết thường) vẫn như cũ", s == 200 and codes(r) == [note_code], codes(r))
    s, r = notes("staff", f"TB-{RUN}-0000")
    check("Mã đơn NCC không có thì không ra gì", s == 200 and codes(r) == [], codes(r))

    # ---------------- Mã vận đơn nội địa sau khi NCC phát hàng
    call("PUT", f"/api/purchase-orders/{po_id}/progress", "sale", {"status": "SUPPLIER_CONFIRMED"})
    tracking = f"SF{RUN}7788"
    s, r = call("PUT", f"/api/purchase-orders/{po_id}/progress", "sale",
                {"status": "SUPPLIER_SHIPPED", "domesticTrackingCode": tracking, "domesticCarrier": "SF Express"})
    check("NCC phát hàng có mã vận đơn nội địa", s == 200, r)
    s, r = notes("staff", tracking.lower())
    rows = data(r).get("items", [])
    check("Tìm theo mã vận đơn nội địa ra phiếu, có hãng + mã vận đơn",
          s == 200 and [x["receivingNoteCode"] for x in rows] == [note_code]
          and rows[0].get("domesticTrackingCode") == tracking and rows[0].get("domesticCarrier") == "SF Express", r)

    # ---------------- Lọc nhanh Mua hộ / Ký gửi
    s, r = notes("staff", note_code, orderType="PURCHASE")
    check("Lọc Mua hộ có phiếu này", s == 200 and codes(r) == [note_code], codes(r))
    s, r = notes("staff", note_code, orderType="CONSIGNMENT")
    check("Lọc Ký gửi không có phiếu mua hộ", s == 200 and codes(r) == [], codes(r))
    s, r = notes("admin", "", orderType="CONSIGNMENT")
    consign = data(r).get("items", [])
    check("Phiếu ký gửi: orderType=CONSIGNMENT, không có trường NCC",
          s == 200 and all(n["orderType"] == "CONSIGNMENT" and n["isPurchase"] is False and n.get("supplierName") is None
                           for n in consign), consign[:2])
    s, r = notes("admin", "", orderType="PURCHASE")
    check("Lọc Mua hộ chỉ trả phiếu mua hộ",
          s == 200 and data(r)["items"] and all(n["isPurchase"] for n in data(r)["items"]), r)

    # ---------------- Chi tiết + quét phiếu
    s, r = call("GET", f"/api/warehouse-receiving-notes/{note_id}", "staff")
    d = data(r)
    check("Chi tiết phiếu: đủ trường mua hộ",
          s == 200 and d.get("isPurchase") is True and d.get("orderType") == "PURCHASE" and d.get("purchaseCode") == purchase_code
          and d.get("purchaseOrderCode") == po_code and d.get("supplierOrderCode") == supplier_code
          and d.get("supplierName") == f"Taobao Shop {RUN}" and d.get("domesticTrackingCode") == tracking, d)
    exp = {i["productName"]: i["quantity"] for i in d.get("expectedItems", [])}
    check("Chi tiết phiếu: danh sách sản phẩm + số lượng đã đặt NCC",
          exp == {f"Tai nghe {suffix}": 2, f"Ao khoac {suffix}": 3}, exp)

    s, r = call("GET", f"/api/warehouse-receiving-notes/scan?code={note_code}", "staff")
    d = data(r)
    check("Quét phiếu: có isPurchase + NCC + mã đơn NCC + nhãn trạng thái server",
          s == 200 and d.get("isPurchase") is True and d.get("supplierOrderCode") == supplier_code
          and d.get("statusText") == "Chờ kho nhận hàng" and len(d.get("expectedItems", [])) == 2, d)

    s, r = call("GET", f"/api/warehouse-receiving-notes/{note_id}", "sale")
    check("Sale xem chi tiết thấy NCC", s == 200 and data(r).get("supplierOrderCode") == supplier_code, r)
    s, r = notes("om1", supplier_code)
    check("OM tìm theo mã đơn NCC ra phiếu", s == 200 and codes(r) == [note_code], codes(r))

    # ---------------- Khách không thấy thông tin NCC
    s, r = call("GET", f"/api/warehouse-receiving-notes/my-order/{order_id}", "customer")
    d = data(r) or {}
    check("Khách xem phiếu: biết là mua hộ nhưng KHÔNG thấy NCC / mã đơn NCC / ghi chú kho",
          s == 200 and d.get("isPurchase") is True and d.get("supplierName") is None
          and d.get("supplierOrderCode") is None and d.get("warehouseNote") == "", d)
    s, r = call("GET", f"/api/warehouse-receiving-notes/my-order/{order_id}", "customer2")
    expect("Khách khác không xem được phiếu", s, r, 403)
    s, r = notes("customer", supplier_code)
    expect("Khách không gọi được danh sách phiếu", s, r, 403)

    # ---------------- Phạm vi kho được gán (danh sách + quét)
    cn_staff = create_staff("wrnCN")
    kr_staff = create_staff("wrnKR")
    try:
        s, r = assign(cn_staff, [CN], note="Kiểm thử phạm vi phiếu")
        expect("Gán nhân viên vào kho CN", s, r, 200)
        s, r = assign(kr_staff, [KR], note="Kiểm thử phạm vi phiếu")
        expect("Gán nhân viên vào kho KR", s, r, 200)

        s, r = notes("wrnCN", supplier_code)
        check("Nhân viên gán kho CN không thấy phiếu kho KR", s == 200 and codes(r) == [], codes(r))
        s, r = notes("wrnCN", "")
        check("Nhân viên gán kho CN chỉ thấy phiếu kho CN",
              s == 200 and all(n["warehouseId"].lower() == CN.lower() for n in data(r)["items"]), r)
        s, r = notes("wrnCN", "", warehouseId=KR)
        expect("Nhân viên gán kho CN hỏi đích danh kho KR → 403", s, r, 403)
        s, r = call("GET", f"/api/warehouse-receiving-notes/scan?code={note_code}", "wrnCN")
        wh_name = row.get("warehouseName") or ""
        check("Quét phiếu kho không được gán → 403, câu nêu tên kho",
              s == 403 and wh_name and wh_name in (r.get("message") or ""), (s, r, wh_name))
        s, r = call("GET", "/api/warehouse-receiving-notes/scan?code=WRN-00000000", "wrnCN")
        expect("Quét mã không tồn tại vẫn 404", s, r, 404)

        s, r = notes("wrnKR", supplier_code)
        check("Nhân viên gán kho KR tìm theo mã NCC ra phiếu", s == 200 and codes(r) == [note_code], codes(r))
        s, r = call("GET", f"/api/warehouse-receiving-notes/scan?code={note_code}", "wrnKR")
        check("Nhân viên gán kho KR quét được phiếu", s == 200 and data(r).get("isPurchase") is True, r)

        s, r = notes("staff", supplier_code)
        check("Nhân viên chưa gán kho giữ hành vi cũ (thấy phiếu)", s == 200 and codes(r) == [note_code], codes(r))
        s, r = notes("sale", supplier_code)
        check("Sale không bị giới hạn kho", s == 200 and codes(r) == [note_code], codes(r))
        s, r = call("GET", f"/api/warehouse-receiving-notes/scan?code={note_code}", "admin")
        expect("Admin quét được mọi phiếu", s, r, 200)
    finally:
        assign(cn_staff, [], note="Dọn kiểm thử")
        assign(kr_staff, [], note="Dọn kiểm thử")

    print(f"\n{len(PASSED)} PASS, {len(FAILED)} FAIL")
    return 1 if FAILED else 0


if __name__ == "__main__":
    sys.exit(main())
