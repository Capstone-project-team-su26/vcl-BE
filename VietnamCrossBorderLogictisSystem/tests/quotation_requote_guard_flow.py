#!/usr/bin/env python3
"""
Kiểm thử luật LẬP LẠI báo giá ký gửi của Sale.

Báo giá đã tới tay khách (gửi thẳng theo bảng giá, hoặc Admin đã duyệt giá ngoại lệ) thì Sale KHÔNG lập
/gửi báo giá mới (409). Chỉ lập lại được khi: khách từ chối, báo giá hết hạn, hoặc Admin từ chối giá ngoại
lệ. Bản còn chờ Admin duyệt giá thì Sale gửi bản mới để thay. Khách đã chấp nhận thì đơn rời mốc báo giá.

Bước "hết hạn" giả lập bằng cách lùi expired_at trong DB THỬ (AppDb_migration).

    APP_ENV=/root/vcl-migration-deploy/app.env API=http://127.0.0.1:8088 python3 quotation_requote_guard_flow.py
"""
import subprocess, sys

from wave1_consignment_flow import (IDS, PASSED, FAILED, RUN, call, check, expect,
                                    create_order, item, get_order_quotation)


def sql(statement):
    """Chạy lệnh trên DB THỬ (AppDb_migration) — chỉ để giả lập báo giá hết hạn."""
    env = subprocess.run(["docker", "inspect", "sqlserver", "--format", "{{range .Config.Env}}{{println .}}{{end}}"],
                         capture_output=True, text=True).stdout
    sa = next(l.split("=", 1)[1] for l in env.splitlines() if "SA_PASSWORD=" in l)
    subprocess.run(["docker", "exec", "sqlserver", "/opt/mssql-tools18/bin/sqlcmd", "-S", "localhost", "-U", "sa",
                    "-P", sa, "-C", "-d", "AppDb_migration", "-b", "-Q", "SET NOCOUNT ON; " + statement],
                   check=True, capture_output=True)

KR = IDS["warehouseKr"]
STANDARD = {"warehouseId": KR}


def manual(amount, reason="Hàng cồng kềnh"):
    return {"warehouseId": KR, "overrideReason": reason,
            "quotation": {"additionalFees": [{"label": "Phụ phí tay", "amount": amount, "enabled": True}]}}


def send(order_id, body):
    return call("POST", f"/api/orders/{order_id}/quotation/send", "sale", body)


def staff_view(order_id):
    s, r = get_order_quotation(order_id, "sale")
    return r.get("data", {}) if s == 200 else {}


def order_summary(order_id):
    s, r = call("GET", f"/api/orders/consignments/{order_id}", "sale")
    return (r.get("data") or {}).get("quotation") or {} if s == 200 else {}


def new_order(suffix):
    s, r = create_order([item(f"Hàng RQ {suffix}", 1, 2.0)], f"RQ{suffix}{RUN}")
    return r["orderId"] if expect(f"[{suffix}] Khách tạo đơn ký gửi", s, r, 200) else None


def scenario_approved_then_blocked():
    order_id = new_order("A")
    if not order_id:
        return

    s, r = send(order_id, manual(50000))
    check("[A] Sale sửa giá → chờ Admin duyệt", s == 200 and r.get("status") == "PENDING_PRICE_APPROVAL", r)
    first = staff_view(order_id)
    check("[A] Chờ duyệt: Sale còn thay bản chờ duyệt",
          first.get("canSalesRequote") is True and first.get("requoteState") == "REPLACE_PENDING_PRICE_APPROVAL", first)

    s, r = send(order_id, manual(60000, "Hàng cồng kềnh, sửa lại số"))
    check("[A] Sale gửi bản khác khi còn chờ duyệt → thay bản cũ", s == 200 and r.get("status") == "PENDING_PRICE_APPROVAL", r)
    second = staff_view(order_id)
    check("[A] Bản chờ duyệt mới là bản khác", second.get("quotationId") not in (None, first.get("quotationId")), second)

    s, r = call("PUT", f"/api/quotations/{first['quotationId']}/price-approval", "admin", {"decision": "APPROVED", "note": "cũ"})
    expect("[A] Admin không duyệt được bản đã bị thay", s, r, 400)

    s, r = call("PUT", f"/api/quotations/{second['quotationId']}/price-approval", "admin", {"decision": "APPROVED", "note": "Đồng ý"})
    expect("[A] Admin duyệt giá ngoại lệ", s, r, 200)

    s, r = send(order_id, STANDARD)
    check("[A] Đã duyệt & gửi khách: Sale gửi lại theo bảng giá bị chặn 409",
          s == 409 and "Admin duyệt" in r.get("message", ""), (s, r))
    s, r = send(order_id, manual(40000))
    check("[A] Đã duyệt & gửi khách: Sale sửa giá lần nữa cũng bị chặn 409", s == 409, (s, r))

    view = staff_view(order_id)
    check("[A] Chi tiết báo giá báo không lập lại được",
          view.get("canSalesRequote") is False and view.get("requoteState") == "APPROVED_SENT_TO_CUSTOMER"
          and "không lập lại được" in (view.get("requoteBlockedReason") or ""), view)
    summary = order_summary(order_id)
    check("[A] Chi tiết đơn cũng báo không lập lại được",
          summary.get("canSalesRequote") is False and summary.get("requoteState") == "APPROVED_SENT_TO_CUSTOMER"
          and summary.get("priceApprovalStatus") == "APPROVED", summary)

    s, cust = get_order_quotation(order_id)
    check("[A] Khách không nhận cờ lập lại của Sale", s == 200 and cust["data"].get("canSalesRequote") is None, cust)

    s, r = call("PUT", f"/api/quotations/{second['quotationId']}/reject", "sale", {"rejectionReason": "Khách báo qua điện thoại"})
    check("[A] Sale không từ chối thay khách (403)", s == 403 and "Admin" in r.get("message", ""), (s, r))
    s, r = call("POST", f"/api/orders/{order_id}/quotation/reject", "sale", {"rejectionReason": "Khách báo qua điện thoại"})
    expect("[A] Sale không từ chối thay khách theo đơn (403)", s, r, 403)
    s, r = call("PUT", f"/api/quotations/{second['quotationId']}/reject", "staff", {"rejectionReason": "thử quyền"})
    expect("[A] Nhân viên kho không từ chối báo giá (403)", s, r, 403)
    view = staff_view(order_id)
    check("[A] Sau các lần bị chặn báo giá vẫn đang chờ khách", view.get("status") == "PENDING" and view.get("canSalesRequote") is False, view)

    s, r = call("PUT", f"/api/quotations/{second['quotationId']}/reject", "customer", {"rejectionReason": "Giá cao"})
    expect("[A] Khách từ chối báo giá", s, r, 200)
    view = staff_view(order_id)
    check("[A] Khách từ chối → Sale lập lại được",
          view.get("canSalesRequote") is True and view.get("requoteState") == "CUSTOMER_REJECTED", view)

    s, r = send(order_id, STANDARD)
    check("[A] Sale lập lại báo giá sau khi khách từ chối", s == 200 and r["consignment"]["status"] == "QUOTATION_SENT", r)
    s, r = send(order_id, STANDARD)
    check("[A] Báo giá theo bảng giá đã gửi khách: gửi lại bị chặn 409",
          s == 409 and "đã gửi khách" in r.get("message", ""), (s, r))
    view = staff_view(order_id)
    check("[A] Nhãn: đã gửi khách (không qua duyệt giá)", view.get("requoteState") == "SENT_TO_CUSTOMER", view)


def scenario_price_rejected():
    order_id = new_order("B")
    if not order_id:
        return
    s, r = send(order_id, manual(70000))
    check("[B] Sale sửa giá → chờ Admin duyệt", s == 200 and r.get("status") == "PENDING_PRICE_APPROVAL", r)
    qid = staff_view(order_id).get("quotationId")

    s, r = call("PUT", f"/api/quotations/{qid}/price-approval", "admin", {"decision": "REJECTED", "note": "Giảm phụ phí"})
    expect("[B] Admin từ chối giá ngoại lệ", s, r, 200)
    view = staff_view(order_id)
    check("[B] Admin từ chối giá → Sale sửa lại được",
          view.get("canSalesRequote") is True and view.get("requoteState") == "PRICE_REJECTED", view)

    s, r = send(order_id, manual(30000, "Giảm theo góp ý Admin"))
    check("[B] Sale gửi lại giá ngoại lệ đã sửa → chờ duyệt", s == 200 and r.get("status") == "PENDING_PRICE_APPROVAL", r)
    s, r = send(order_id, STANDARD)
    check("[B] Hoặc gửi theo bảng giá → tới khách", s == 200 and r["consignment"]["status"] == "QUOTATION_SENT", r)

    s, r = call("POST", f"/api/orders/{order_id}/quotation/reject", "admin", {"rejectionReason": "Khách gọi điện từ chối, Admin ghi nhận"})
    check("[B] Admin từ chối báo giá thay khách được", s == 200 and r.get("status") == "QUOTATION_REJECTED", (s, r))
    view = staff_view(order_id)
    check("[B] Admin từ chối → Sale lập lại được", view.get("canSalesRequote") is True and view.get("requoteState") == "CUSTOMER_REJECTED", view)


def scenario_expired_then_accepted():
    order_id = new_order("C")
    if not order_id:
        return
    s, r = send(order_id, STANDARD)
    check("[C] Sale gửi báo giá theo bảng giá", s == 200 and r["consignment"]["status"] == "QUOTATION_SENT", r)
    s, r = send(order_id, STANDARD)
    expect("[C] Còn hạn: gửi lại bị chặn", s, r, 409)

    old = staff_view(order_id)
    sql(f"UPDATE QUOTATIONS SET expired_at = DATEADD(day, -1, GETUTCDATE()) WHERE id = '{old['quotationId']}';")
    view = staff_view(order_id)
    check("[C] Hết hạn → Sale lập lại được", view.get("canSalesRequote") is True and view.get("requoteState") == "EXPIRED", view)

    s, r = send(order_id, STANDARD)
    check("[C] Sale lập lại báo giá khi bản cũ hết hạn", s == 200 and r["consignment"]["status"] == "QUOTATION_SENT", r)
    s, q = get_order_quotation(order_id)
    fresh = q.get("data", {})
    check("[C] Khách thấy báo giá mới, xác nhận được",
          fresh.get("quotationId") != old["quotationId"] and fresh.get("canCustomerAccept") is True, fresh)

    s, pay = call("PUT", f"/api/quotations/{fresh['quotationId']}/confirm-and-pay", "customer", {"paymentMethod": "SEPAY"})
    expect("[C] Khách xác nhận báo giá", s, pay, 200)
    s, r = send(order_id, STANDARD)
    expect("[C] Khách đã chấp nhận: Sale không lập báo giá nữa", s, r, 400)
    view = staff_view(order_id)
    check("[C] Chi tiết báo giá: không lập lại (đơn rời mốc báo giá)",
          view.get("canSalesRequote") is False and view.get("requoteState") in ("ORDER_NOT_QUOTABLE", "ACCEPTED"), view)


def main():
    scenario_approved_then_blocked()
    scenario_price_rejected()
    scenario_expired_then_accepted()
    print(f"\n{len(PASSED)} PASS, {len(FAILED)} FAIL")
    if FAILED:
        print("FAILED:", *FAILED, sep="\n  ")
        sys.exit(1)


if __name__ == "__main__":
    main()
