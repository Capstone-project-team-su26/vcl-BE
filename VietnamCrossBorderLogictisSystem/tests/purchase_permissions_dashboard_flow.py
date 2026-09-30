#!/usr/bin/env python3
"""
Kiểm thử hai điểm của yêu cầu mua hộ:

A. Phân quyền: chỉ Sale và Admin được XEM XÉT (đổi trạng thái IN_REVIEW / NEED_MORE_INFO / REJECTED),
   LẬP BÁO GIÁ và từ chối báo giá thay khách. Nhân viên kho, giao vận, Operations Manager, STAFF chung
   vẫn xem được danh sách / chi tiết nhưng ghi thì 403.

B. Bảng việc của khách không đếm trùng hàng mua hộ: đơn kho PUR-xxx-n thuộc phía MUA HỘ (kind
   PURCHASE, gắn về yêu cầu sở hữu), mỗi phần hàng chỉ đếm một lần ở đúng chặng; chi tiêu tháng
   và cơ cấu chi phí không cộng hai lần.

Token của vai trò giao vận / STAFF chung được ký bằng id của tài khoản kho có sẵn — backend
chỉ đọc vai trò trong token, không tra lại DB.

    APP_ENV=/root/vcl-migration-deploy/app.env API=http://127.0.0.1:8088 python3 purchase_permissions_dashboard_flow.py
"""
import sys

import wave1_consignment_flow as w1
from wave1_consignment_flow import IDS, PASSED, FAILED, RUN, call, check, expect
from export_flow import KR, VN_WAREHOUSE, set_region, store, upload
from vn_arrival_flow import arrival_photos, ship_to_vietnam

ELECTRONICS = "11111111-0000-0000-0000-000000000001"
CLOTHES = "11111111-0000-0000-0000-000000000002"

# Vai trò không có tài khoản riêng trong bản sao DB: ký token với vai trò đó trên id tài khoản kho.
w1.TOK["delivery"] = w1.token(IDS["staff"]["id"], "Delivery")
w1.TOK["staffgeneric"] = w1.token(IDS["staff"]["id"], "STAFF")
w1.TOK["sales_upper"] = w1.token(IDS["sale"]["id"], "SALES")
w1.TOK["admin_lower"] = w1.token(IDS["admin"]["id"], "admin")

NOT_ALLOWED = ("staff", "om1", "delivery", "staffgeneric")

STAGES = ("waitingStaff", "waitingGoods", "atOriginWarehouse", "inTransit", "atVietnamWarehouse", "outForDelivery")


def data(r):
    return r.get("data", r) if isinstance(r, dict) else r


def create_request(suffix):
    s, r = call("POST", "/api/purchase-requests", "customer", {
        "route": "KR-VN", "shippingOption": "Express",
        "receiverName": "Quyen MH " + suffix, "receiverPhone": "0900000000", "receiverAddress": "HCM",
        "items": [
            {"productLink": "https://shop.example/a", "productType": ELECTRONICS, "productName": f"Loa {suffix}", "quantity": 2},
            {"productLink": "https://shop.example/b", "productType": CLOTHES, "productName": f"Mu {suffix}", "quantity": 3},
        ]})
    d = data(r)
    return s, d.get("purchaseRequestId") or d.get("id")


def request_items(request_id):
    s, r = call("GET", f"/api/purchase-requests/{request_id}", "sale")
    # Server không cam kết thứ tự dòng — xếp theo tên ("Loa …" trước "Mu …") cho item_a/item_b cố định.
    items = sorted(data(r).get("items") or [], key=lambda i: i.get("productName") or "")
    return [(i.get("itemId") or i.get("purchaseRequestItemId") or i.get("id")) for i in items]


def quote_body(item_a, item_b):
    return {"purchaseFee": 80000, "domesticShippingFee": 30000, "freightRatePerKg": 120000, "estimatedWeight": 2,
            "items": [{"purchaseRequestItemId": item_a, "unitPrice": 500000},
                      {"purchaseRequestItemId": item_b, "unitPrice": 200000}]}


def board():
    s, r = call("GET", "/api/customers/me/dashboard", "customer")
    return data(r)


def progress_of(b):
    return b.get("inProgress") or {}


def delta(after, before, key):
    return progress_of(after).get(key, 0) - progress_of(before).get(key, 0)


def this_month(b):
    rows = (b.get("stats") or {}).get("monthlySpend") or []
    return rows[-1] if rows else {"paid": 0, "paymentCount": 0}


def action(b, key):
    return next((a for a in b.get("actions", []) if a.get("key") == key), {})


def permissions(supplier_id):
    s, request_id = create_request(f"Q{RUN}")
    if not check("Khách tạo yêu cầu mua hộ (phân quyền)", s in (200, 201) and request_id, request_id):
        return None
    item_a, item_b = request_items(request_id)

    # Xem danh sách / chi tiết: nhân viên nội bộ vẫn xem được như cũ.
    for who in NOT_ALLOWED:
        s, r = call("GET", f"/api/purchase-requests/{request_id}", who)
        expect(f"{who} vẫn xem được chi tiết yêu cầu mua hộ", s, r, 200)
        s, r = call("GET", "/api/purchase-requests?pageSize=5", who)
        expect(f"{who} vẫn xem được danh sách yêu cầu mua hộ", s, r, 200)

    # Ghi xem xét / báo giá: chỉ Sale, Admin.
    for who in NOT_ALLOWED:
        s, r = call("PUT", f"/api/purchase-requests/{request_id}/status", who, {"status": "IN_REVIEW"})
        check(f"{who} không đổi được trạng thái xem xét (403, câu tiếng Việt)",
              s == 403 and "Chỉ Sale hoặc Admin" in r.get("message", ""), (s, r))
        s, r = call("PUT", f"/api/purchase-requests/{request_id}/status", who,
                    {"status": "REJECTED", "reason": "thử quyền"})
        expect(f"{who} không từ chối được yêu cầu", s, r, 403)
        s, r = call("POST", f"/api/purchase-requests/{request_id}/quotation", who, quote_body(item_a, item_b))
        check(f"{who} không lập được báo giá (403, câu tiếng Việt)",
              s == 403 and "Chỉ Sale hoặc Admin" in r.get("message", ""), (s, r))

    s, r = call("GET", f"/api/purchase-requests/{request_id}", "sale")
    check("Các lần gọi bị chặn không làm đổi trạng thái yêu cầu", data(r).get("status") == "PENDING_REVIEW", data(r).get("status"))

    s, r = call("PUT", f"/api/purchase-requests/{request_id}/status", "sale", {"status": "IN_REVIEW"})
    check("Sale tiếp nhận xem xét (IN_REVIEW)", s == 200 and data(r).get("status") == "IN_REVIEW", r)
    s, r = call("PUT", f"/api/purchase-requests/{request_id}/status", "admin",
                {"status": "NEED_MORE_INFO", "reason": "Cần ảnh sản phẩm"})
    check("Admin yêu cầu bổ sung (NEED_MORE_INFO)", s == 200 and data(r).get("status") == "NEED_MORE_INFO", r)
    s, r = call("PUT", f"/api/purchase-requests/{request_id}/status", "sales_upper", {"status": "IN_REVIEW"})
    check("Biến thể vai trò SALES vẫn được xem xét", s == 200 and data(r).get("status") == "IN_REVIEW", r)

    # Báo giá bởi Admin (biến thể chữ thường) trên yêu cầu này.
    s, r = call("POST", f"/api/purchase-requests/{request_id}/quotation", "admin_lower", quote_body(item_a, item_b))
    check("Admin lập được báo giá mua hộ", s in (200, 201) and data(r).get("prepayAmount") == 1718800, r)

    # Yêu cầu thứ hai: Sale báo giá, kho/OM không từ chối thay khách được, Sale thì được.
    s, other_id = create_request(f"R{RUN}")
    oa, ob = request_items(other_id)
    s, r = call("POST", f"/api/purchase-requests/{other_id}/quotation", "sale", quote_body(oa, ob))
    check("Sale lập được báo giá mua hộ", s in (200, 201), r)
    for who in NOT_ALLOWED:
        s, r = call("PUT", f"/api/purchase-requests/{other_id}/quotation/reject", who, {"rejectionReason": "thử quyền"})
        check(f"{who} không từ chối báo giá thay khách được",
              s == 403 and "Chỉ Sale hoặc Admin" in r.get("message", ""), (s, r))
    s, r = call("PUT", f"/api/purchase-requests/{other_id}/quotation/reject", "sale", {"rejectionReason": "Khách báo qua điện thoại"})
    check("Sale từ chối báo giá thay khách được", s == 200 and data(r).get("status") == "QUOTATION_REJECTED", r)

    # Quy tắc duyệt đơn mua NCC giữ nguyên (không thuộc phạm vi thay đổi).
    return request_id, item_a, item_b


def dashboard_no_double_count(request_id, item_a, item_b, supplier_id):
    b0 = board()

    # ---- Khách trả trước: chi tiêu tháng tăng đúng một khoản, đúng số tiền
    s, r = call("PUT", f"/api/purchase-requests/{request_id}/quotation/confirm-and-pay", "customer", {"paymentMethod": "SEPAY"})
    pay = data(r)
    if not check("Khách chấp nhận báo giá → có khoản trả trước", s == 200 and pay.get("amount") == 1718800, r):
        return
    b_due = board()
    due_items = action(b_due, "PAYMENT_DUE").get("items", [])
    mine = [i for i in due_items if (i.get("id") or "").lower() == request_id.lower()]
    check("Khoản trả trước nằm ở PAYMENT_DUE, kind PURCHASE, không gắn đơn kho",
          len(mine) == 1 and mine[0]["kind"] == "PURCHASE" and not mine[0].get("warehouseOrderId"), mine)

    s, r = call("PUT", f"/api/admin/finance/transactions/{pay.get('paymentId')}/approve", "admin",
                {"transactionCode": f"FT-QMH-{RUN}", "receivedAmount": 1718800})
    expect("Admin ghi nhận khách đã trả trước", s, r, 200)
    b1 = board()
    m0, m1 = this_month(b0), this_month(b1)
    check("Chi tiêu tháng tăng đúng số trả trước, một lần thu",
          m1["paid"] - m0["paid"] == 1718800 and m1["paymentCount"] - m0["paymentCount"] == 1, (m0, m1))
    check("Yêu cầu đã trả trước (chưa có đơn kho) đếm 1 lần ở chờ hàng",
          delta(b1, b0, "waitingGoods") == 1 and delta(b1, b0, "total") == 1, (progress_of(b0), progress_of(b1)))

    # ---- Lập + duyệt + đặt NCC cho sản phẩm A → sinh đơn kho PUR-xxx-1
    po_body = {"supplierId": supplier_id, "warehouseId": KR, "currency": "VND",
               "items": [{"purchaseRequestItemId": item_a, "quantity": 2, "unitPrice": 500000}]}
    s, r = call("POST", f"/api/purchase-requests/{request_id}/purchase-orders", "sale", po_body)
    po1 = data(r).get("purchaseOrderId")
    call("POST", f"/api/purchase-orders/{po1}/submit", "sale")
    s, r = call("POST", f"/api/purchase-orders/{po1}/decide", "admin", {"approve": True, "note": "OK"})
    check("Admin duyệt đơn mua NCC", s == 200 and data(r).get("status") == "APPROVED", r)
    upload("sale", "PURCHASE_ORDER", po1, "PURCHASE_PROOF", "don-ncc.png")
    s, r = call("POST", f"/api/purchase-orders/{po1}/place", "sale", {"supplierOrderCode": f"KRQ-{RUN}"})
    placed = data(r)
    if not check("Sale đặt NCC → có đơn kho", s == 200 and placed.get("warehouseOrderId"), r):
        return
    order_id, note_code = placed["warehouseOrderId"], placed["receivingNoteCode"]

    b2 = board()
    check("Đặt NCC xong: yêu cầu PURCHASING + đơn kho DEPOSIT_PAID chỉ đếm MỘT lần ở chờ hàng",
          delta(b2, b1, "total") == 0 and delta(b2, b1, "waitingGoods") == 0, (progress_of(b1), progress_of(b2)))
    s1, s2 = b1.get("stats") or {}, b2.get("stats") or {}
    check("Cơ cấu chi phí: số đơn đã báo giá không tăng khi đơn kho sinh báo giá riêng",
          s2.get("quotedOrderCount") == s1.get("quotedOrderCount"), (s1.get("quotedOrderCount"), s2.get("quotedOrderCount")))
    check("Cơ cấu chi phí: tổng (cước + phí + thuế) không tăng vì đơn kho (không cộng trùng)",
          (s2.get("totalFreight", 0) + s2.get("totalServiceFee", 0) + s2.get("totalTax", 0))
          <= (s1.get("totalFreight", 0) + s1.get("totalServiceFee", 0) + s1.get("totalTax", 0)), (s1, s2))
    check("Chi tiêu tháng không đổi khi sinh đơn kho (khoản cọc 0đ không tính)",
          this_month(b2) == this_month(b1), (this_month(b1), this_month(b2)))

    # ---- Còn sản phẩm B đang đi mua (đơn mua nháp) nhưng đơn kho cũng đang chờ hàng → vẫn một dòng
    po2_body = {"supplierId": supplier_id, "warehouseId": KR, "currency": "VND",
                "items": [{"purchaseRequestItemId": item_b, "quantity": 3, "unitPrice": 200000}]}
    s, r = call("POST", f"/api/purchase-requests/{request_id}/purchase-orders", "sale", po2_body)
    po2 = data(r).get("purchaseOrderId")
    check("Sale lập đơn mua NCC #2 (nháp)", s == 201 and po2, r)
    b3 = board()
    check("Phần còn đi mua cùng chặng chờ hàng với đơn kho → không đếm thêm",
          delta(b3, b1, "total") == 0 and delta(b3, b1, "waitingGoods") == 0, (progress_of(b1), progress_of(b3)))

    # ---- Kho check-in đơn kho → đơn kho sang kho nguồn; phần B vẫn đang mua → yêu cầu hiện ở chờ hàng
    s, r = call("GET", f"/api/warehouse-receiving-notes/scan?code={note_code}", "staff")
    expected = data(r).get("expectedItems") or []
    packages = [{"orderItemId": expected[0]["orderItemId"], "actualWeight": 1.2, "length": 30, "width": 20, "height": 10}]
    s, r = call("POST", "/api/parcels/check-in", "staff", {"shippingCode": note_code, "packages": packages})
    d = data(r)
    parcel = (d.get("parcels") or [d])[0]
    check("Kho check-in đơn kho", s in (200, 201) and parcel.get("parcelId"), r)
    store(parcel["parcelId"], "BIN-A1-01")
    s, r = call("GET", f"/api/orders/consignments/{order_id}", "customer")
    order_status = (data(r) or {}).get("status")
    b4 = board()
    if order_status in ("CHECKED_IN", "WAREHOUSE_RECEIVED"):
        check("Đơn kho ở kho nguồn + phần B còn đang mua → hai phần hàng, mỗi phần một chặng",
              delta(b4, b1, "total") == 1 and delta(b4, b1, "atOriginWarehouse") == 1
              and delta(b4, b1, "waitingGoods") == 0, (order_status, progress_of(b1), progress_of(b4)))
    else:
        check("Đơn kho chuyển sang trạng thái kho nguồn sau check-in", False, order_status)

    s, r = call("POST", f"/api/purchase-orders/{po2}/cancel", "sale", {"reason": "NCC hết hàng"})
    check("Sale huỷ đơn mua #2 chưa đặt", s == 200, r)
    b5 = board()
    check("Không còn phần đi mua → yêu cầu không đếm riêng, chỉ còn đơn kho ở kho nguồn",
          delta(b5, b1, "total") == 0 and delta(b5, b1, "atOriginWarehouse") == 1
          and delta(b5, b1, "waitingGoods") == -1, (progress_of(b1), progress_of(b5)))

    # ---- Về VN, lập đợt tất toán đơn kho → PAYMENT_DUE thuộc phía mua hộ
    shipment_id = ship_to_vietnam([parcel["parcelId"]], f"QMH{RUN}")
    if not check("Kiện đơn kho mua hộ về tới kho VN", shipment_id is not None, shipment_id):
        return
    arrival_photos([parcel["parcelId"]])  # ảnh tiếp nhận kho VN — bắt buộc mỗi kiện
    s, r = call("PUT", f"/api/international-shipments/{shipment_id}/destination-receiving", "staff",
                {"items": [{"parcelId": parcel["parcelId"], "actualWeight": 1.25, "length": 30, "width": 20,
                            "height": 10, "condition": "GOOD"}]})
    expect("Kho VN tiếp nhận có cân đo", s, r, 200)
    b6 = board()
    check("Hàng về kho VN: tổng vẫn một dòng cho yêu cầu này",
          delta(b6, b1, "total") == 0 and delta(b6, b1, "waitingGoods") == -1, (progress_of(b1), progress_of(b6)))

    before_pay = action(board(), "PAYMENT_DUE")
    s, r = call("POST", f"/api/orders/{order_id}/payments/final", "sale", {"paymentMethod": "SEPAY"})
    fin = data(r)
    if not check("Sale lập đợt tất toán đơn kho", s in (200, 201) and fin.get("finalAmount"), r):
        return
    b7 = board()
    pay_due = action(b7, "PAYMENT_DUE")
    check("Tất toán đơn kho PUR tính vào mua hộ, không tính ký gửi",
          pay_due.get("purchaseCount", 0) - before_pay.get("purchaseCount", 0) == 1
          and pay_due.get("consignmentCount", 0) == before_pay.get("consignmentCount", 0),
          (before_pay.get("purchaseCount"), pay_due.get("purchaseCount"),
           before_pay.get("consignmentCount"), pay_due.get("consignmentCount")))
    rows = [i for i in pay_due.get("items", []) if (i.get("warehouseOrderId") or "").lower() == order_id.lower()]
    check("Dòng tất toán: kind PURCHASE, id = yêu cầu mua hộ, warehouseOrderId = đơn kho, mã PUR-xxx-1",
          len(rows) == 1 and rows[0]["kind"] == "PURCHASE" and rows[0]["id"].lower() == request_id.lower()
          and rows[0]["code"].endswith("-1") and rows[0]["amount"] == fin.get("finalAmount"), rows)
    check("Không còn dòng CONSIGNMENT nào mang id đơn kho",
          not any(i["kind"] == "CONSIGNMENT" and i["id"].lower() == order_id.lower()
                  for a in b7.get("actions", []) for i in a.get("items", [])), pay_due.get("items"))
    check("Tổng phải trả vẫn khớp thẻ PAYMENT_DUE", b7.get("totalAmountDue") == pay_due.get("amount"), b7.get("totalAmountDue"))
    p7 = progress_of(b7)
    check("Hàng đang chạy: tổng bằng tổng các chặng", p7.get("total") == sum(p7.get(k, 0) for k in STAGES), p7)


def main():
    call("PUT", f"/api/warehouses/{KR}/manager", "admin", {"managerId": None, "reason": "Kiểm thử quyền mua hộ"})
    call("PUT", f"/api/warehouses/{VN_WAREHOUSE}/manager", "admin", {"managerId": None, "reason": "Kiểm thử quyền mua hộ"})
    set_region("staff", "KR")

    s, r = call("POST", "/api/suppliers", "admin", {
        "supplierCode": f"NCCQ-{RUN}", "supplierName": f"NCC quyền {RUN}", "supplierType": "GOODS", "country": "KR",
        "email": f"nccq{RUN}@example.com"})
    supplier_id = data(r).get("id") or data(r).get("supplierId")
    if not check("Admin tạo NCC", s in (200, 201) and supplier_id, r):
        return 1

    got = permissions(supplier_id)
    if got:
        dashboard_no_double_count(*got, supplier_id)

    print(f"\n{len(PASSED)} PASS, {len(FAILED)} FAIL")
    return 1 if FAILED else 0


if __name__ == "__main__":
    sys.exit(main())
