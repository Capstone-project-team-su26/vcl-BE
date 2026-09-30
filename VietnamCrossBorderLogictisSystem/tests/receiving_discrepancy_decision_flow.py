#!/usr/bin/env python3
"""
Kiểm thử bước quyết định lệch của phiếu tiếp nhận kho gốc (approvalStage DISCREPANCY_ACK).

Kho kiểm đếm đủ số lượng nhưng cân lệch khai báo → phiếu tự chốt APPROVED (xếp kệ được ngay) nhưng
vẫn nằm trong hàng đợi "Cần quyết định" của quản lý kho cho tới khi có người chấp nhận số thực tế.
Trước bản sửa: app kho hiện "Đã duyệt · Có lệch" còn màn OM không có phiếu nào để quyết.

    APP_ENV=/root/vcl-migration-deploy/app.env API=http://127.0.0.1:8088 python3 receiving_discrepancy_decision_flow.py
"""
import sys

from wave1_consignment_flow import (IDS, PASSED, FAILED, RUN, call, check, expect,
                                    create_order, item, get_order_quotation, sepay)


def paid_note(items, suffix):
    """Đơn đã cọc + phiếu đã duyệt cho nhận hàng. Trả (order_id, consignment_code, note_id, expected theo tên)."""
    kr = IDS["warehouseKr"]
    s, r = create_order(items, suffix)
    if not expect(f"[{suffix}] Khách tạo đơn ký gửi", s, r, 200):
        return None
    order_id = r["orderId"]

    s, r = call("POST", f"/api/orders/{order_id}/quotation/send", "sale", {"warehouseId": kr})
    if not expect(f"[{suffix}] Sale gửi báo giá", s, r, 200):
        return None
    _, q = get_order_quotation(order_id)
    s, pay = call("PUT", f"/api/quotations/{q['data']['quotationId']}/confirm-and-pay", "customer", {"paymentMethod": "SEPAY"})
    if not expect(f"[{suffix}] Khách xác nhận báo giá", s, pay, 200):
        return None
    pay = pay["data"]
    s, r = sepay(pay["orderCode"], pay["amount"])
    check(f"[{suffix}] Cọc xác nhận qua webhook", s == 200 and r.get("handled") is True, r)

    s, r = call("POST", "/api/warehouse-receiving-notes", "sale", {"consignmentOrderId": order_id, "warehouseId": kr})
    note = r.get("receivingNote") if s == 201 else None
    if not check(f"[{suffix}] Sale lập phiếu", note is not None, r):
        return None
    s, r = call("PUT", f"/api/warehouse-receiving-notes/{note['id']}/status", "om1", {"status": "APPROVED"})
    if not check(f"[{suffix}] Quản lý kho duyệt cho nhận hàng", s == 200 and r["data"]["status"] == "ACTIVE", r):
        return None
    check(f"[{suffix}] Phiếu vừa duyệt nhận hàng không còn chờ quyết",
          r["data"].get("awaitingApproval") is False and r["data"].get("approvalStage") is None, r["data"])

    _, nd = call("GET", f"/api/warehouse-receiving-notes/{note['id']}", "om1")
    expected = {e["productName"]: e for e in nd["data"]["expectedItems"]}
    return order_id, nd["data"]["consignmentCode"], note["id"], note["receivingNoteCode"], expected


def list_row(note_id, status=""):
    s, r = call("GET", f"/api/warehouse-receiving-notes?status={status}&pageSize=200", "om1")
    if s != 200:
        return None
    return next((n for n in r["data"]["items"] if n["id"] == note_id), None)


def parcel(line, weight):
    return {"orderItemId": line["orderItemId"], "actualWeight": weight, "length": 20, "width": 20, "height": 10}


def main():
    kr = IDS["warehouseKr"]
    # Kho KR không có quản lý riêng → OM / Admin quyết (giống lúc bắt đầu wave1).
    call("PUT", f"/api/warehouses/{kr}/manager", "admin", {"managerId": None, "reason": "Chuẩn bị kiểm thử lệch cân"})

    # ---------------- ① Đủ số lượng, lệch cân → tự chốt nhưng chờ quyết định lệch
    made = paid_note([item(f"Áo DA{RUN}", 2, 1.0), item(f"Quần DA{RUN}", 1, 2.0)], "DA")
    if not made:
        return
    order_id, code, note_id, note_code, expected = made
    shirt, pants = expected[f"Áo DA{RUN}"], expected[f"Quần DA{RUN}"]

    body = {"items": [{"orderItemId": shirt["orderItemId"], "actualQuantity": 2, "actualWeight": 1.4, "note": "thùng móp"},
                      {"orderItemId": pants["orderItemId"], "actualQuantity": 1, "actualWeight": 2.0}],
            "parcels": [parcel(shirt, 1.4), parcel(pants, 2.0)]}
    s, r = call("POST", f"/api/warehouse-receiving-notes/{note_id}/confirm-receipt", "staff", body)
    if not expect("Kho kiểm đếm đủ số lượng, lệch cân", s, r, 200):
        return
    d = r["data"]
    check("Phiếu lệch cân vẫn tự chốt APPROVED và xếp kệ được",
          d["status"] == "APPROVED" and d["canPutAway"] and not d["requiresReview"], d)
    check("Phiếu lệch cân chờ quản lý kho quyết định (DISCREPANCY_ACK)",
          d["hasDiscrepancy"] and d.get("awaitingApproval") is True and d.get("approvalStage") == "DISCREPANCY_ACK", d)
    check("Nhãn trạng thái không còn 'Đã duyệt' trơn", "quyết định lệch" in (d.get("statusText") or ""), d.get("statusText"))
    check("Ghi chú chốt nói rõ lệch cân chờ quyết", "lệch cân" in (d.get("approvalNote") or ""), d.get("approvalNote"))

    row = list_row(note_id)
    check("Danh sách: phiếu nằm trong hàng đợi chờ quyết (awaitingApproval + DISCREPANCY_ACK)",
          row is not None and row["awaitingApproval"] and row["approvalStage"] == "DISCREPANCY_ACK" and row["hasDiscrepancy"], row)
    row = list_row(note_id, "APPROVED")
    check("Lọc status=APPROVED vẫn thấy phiếu (trạng thái gốc không đổi)", row is not None, row)

    s, r = call("PUT", f"/api/warehouse-receiving-notes/{note_id}/status", "staff", {"status": "APPROVED"})
    check("Nhân viên kho (người kiểm đếm) không tự chấp nhận lệch", s in (400, 403), r)

    s, r = call("PUT", f"/api/warehouse-receiving-notes/{note_id}/status", "sale", {"status": "APPROVED"})
    check("Sale không quyết định lệch", s in (400, 403), r)

    s, r = call("PUT", f"/api/warehouse-receiving-notes/{note_id}/status", "om1", {"status": "REJECTED", "rejectionReason": "x"})
    expect("Phiếu đã tự chốt thì không từ chối được (chỉ chấp nhận số thực tế)", s, r, 400)

    s, r = call("PUT", f"/api/warehouse-receiving-notes/{note_id}/status", "om1",
                {"status": "APPROVED", "reason": f"Khách đồng ý cân thực {RUN}"})
    ok = expect("OM chấp nhận số thực tế", s, r, 200)
    if ok:
        d = r["data"]
        check("Sau khi chấp nhận: hết chờ quyết, vẫn APPROVED, vẫn xếp kệ được",
              d["status"] == "APPROVED" and d.get("awaitingApproval") is False and d.get("approvalStage") is None and d["canPutAway"], d)
        check("Ghi người quyết, thời điểm và ghi chú",
              d["approvedByName"] != "" and d["approvedAt"] and f"Khách đồng ý cân thực {RUN}" in (d.get("approvalNote") or ""), d)
        check("Nhãn trạng thái trở về 'Đã duyệt'", d.get("statusText") == "Đã duyệt", d.get("statusText"))

    s, r = call("PUT", f"/api/warehouse-receiving-notes/{note_id}/status", "om2", {"status": "APPROVED"})
    expect("Không quyết định lệch lần hai (409)", s, r, 409)

    row = list_row(note_id)
    check("Danh sách: phiếu ra khỏi hàng đợi chờ quyết", row is not None and not row["awaitingApproval"] and row["approvalStage"] is None, row)

    s, r = call("GET", "/api/notifications?pageSize=50", "customer")
    data = r.get("data") if s == 200 else None
    items = data if isinstance(data, list) else (data or {}).get("items", [])
    check("Khách nhận thông báo kho đã chốt số thực nhận (kèm dòng lệch)",
          any(code in (n.get("content") or "") and "cân khai" in (n.get("content") or "") for n in items), items[:3])

    # ---------------- ② Khớp hoàn toàn → không chờ ai
    made = paid_note([item(f"Mũ DB{RUN}", 1, 0.5)], "DB")
    if not made:
        return
    _, _, note2, _, exp2 = made
    hat = exp2[f"Mũ DB{RUN}"]
    body = {"items": [{"orderItemId": hat["orderItemId"], "actualQuantity": 1, "actualWeight": 0.5}],
            "parcels": [parcel(hat, 0.5)]}
    s, r = call("POST", f"/api/warehouse-receiving-notes/{note2}/confirm-receipt", "staff", body)
    if expect("Kho kiểm đếm khớp hoàn toàn", s, r, 200):
        d = r["data"]
        check("Khớp: APPROVED, không lệch, không chờ quyết",
              d["status"] == "APPROVED" and not d["hasDiscrepancy"] and d.get("awaitingApproval") is False and d.get("approvalStage") is None, d)
    s, r = call("PUT", f"/api/warehouse-receiving-notes/{note2}/status", "om1", {"status": "APPROVED"})
    expect("Phiếu khớp đã chốt thì không có gì để quyết (409)", s, r, 409)

    # ---------------- ③ Lệch số lượng vẫn đi bước DISCREPANCY cũ (chặn xếp kệ)
    made = paid_note([item(f"Giày DC{RUN}", 2, 1.5)], "DC")
    if not made:
        return
    _, _, note3, _, exp3 = made
    shoes = exp3[f"Giày DC{RUN}"]
    body = {"items": [{"orderItemId": shoes["orderItemId"], "actualQuantity": 1, "actualWeight": 1.5}],
            "parcels": [parcel(shoes, 1.5)]}
    s, r = call("POST", f"/api/warehouse-receiving-notes/{note3}/confirm-receipt", "staff", body)
    if expect("Kho kiểm đếm thiếu số lượng", s, r, 200):
        d = r["data"]
        check("Lệch số lượng: RECEIVED, chặn xếp kệ, bước DISCREPANCY",
              d["status"] == "RECEIVED" and not d["canPutAway"] and d.get("approvalStage") == "DISCREPANCY", d)
    s, r = call("PUT", f"/api/warehouse-receiving-notes/{note3}/status", "om1", {"status": "APPROVED", "reason": "Khách gửi thiếu"})
    check("OM chấp nhận lệch số lượng → APPROVED, xếp kệ được, hết chờ",
          s == 200 and r["data"]["status"] == "APPROVED" and r["data"]["canPutAway"] and r["data"].get("approvalStage") is None, r)
    s, r = call("PUT", f"/api/warehouse-receiving-notes/{note3}/status", "om2", {"status": "APPROVED"})
    expect("Phiếu lệch số lượng đã quyết thì không quyết lại (409)", s, r, 409)


if __name__ == "__main__":
    try:
        main()
    finally:
        print(f"\n{len(PASSED)} passed, {len(FAILED)} failed")
        sys.exit(1 if FAILED else 0)
