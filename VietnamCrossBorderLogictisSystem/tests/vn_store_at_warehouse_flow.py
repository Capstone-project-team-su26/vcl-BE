#!/usr/bin/env python3
"""
Kiểm thử trọn luồng hàng KHÁCH GỬI LẠI KHO VIỆT NAM (STORE_AT_VN), đối chiếu với giao ngay:

  khách chọn gửi kho → kho VN tiếp nhận (cân đo + ảnh) → tất toán → Sale báo kho → kho lập phiếu
  nhập kho → OM duyệt (tự xếp kệ) → tồn kho VN → phí lưu kho → khách đặt giao sau → OM duyệt →
  kho đặt giao (chặn khi chưa trả phí lưu kho) → đã giao.

Nhánh giao ngay (DIRECT_DELIVERY) đi cùng đơn để so: không vào phiếu nhập kho, không phí lưu kho
VN, đặt giao được ngay sau tất toán.

Bước "đặt giao GoShip" không gọi hãng thật trên môi trường thử: trạng thái sau khi đặt giao được
giả lập thẳng trong DB thử (simulate_dispatch của vn_arrival_flow). Số ngày nằm kho VN cũng được
"lùi giờ" trong DB thử để phát sinh phí lưu kho.

    APP_ENV=/root/vcl-migration-deploy/app.env API=http://127.0.0.1:8088 python3 vn_store_at_warehouse_flow.py
"""
import subprocess, sys

from wave1_consignment_flow import IDS, PASSED, FAILED, RUN, call, check, expect, item, get_order_quotation
from export_flow import KR, VN_WAREHOUSE, set_region, store, upload
from vn_arrival_flow import (arrival_photos, parcel_status, pending_putaway_ids, ship_to_vietnam,
                             simulate_dispatch, sql, tracking)


def sql_scalar(statement):
    """Đọc MỘT giá trị từ DB THỬ (AppDb_migration)."""
    env = subprocess.run(["docker", "inspect", "sqlserver", "--format", "{{range .Config.Env}}{{println .}}{{end}}"],
                         capture_output=True, text=True).stdout
    sa = next(l.split("=", 1)[1] for l in env.splitlines() if "SA_PASSWORD=" in l)
    out = subprocess.run(["docker", "exec", "sqlserver", "/opt/mssql-tools18/bin/sqlcmd", "-S", "localhost", "-U", "sa",
                          "-P", sa, "-C", "-d", "AppDb_migration", "-b", "-h", "-1", "-W",
                          "-Q", "SET NOCOUNT ON; " + statement],
                         check=True, capture_output=True, text=True).stdout
    return out.strip().splitlines()[0].strip() if out.strip() else ""


def received_order(names, suffix, handling):
    """Đơn ký gửi (chọn hướng lúc đặt đơn) đi tới lúc kho nguồn check-in đủ kiện."""
    s, r = call("POST", "/api/orders/consignments", "customer", {
        "route": "KR-VN", "shippingOption": "Express",
        "receiverName": "Test Gửi kho " + suffix, "receiverPhone": "0900000000", "receiverAddress": "HCM",
        "defaultDestinationHandling": handling,
        "items": [item(n, 1, 1.0) for n in names],
    })
    order = r.get("data", r)
    order_id = order["orderId"]
    call("POST", f"/api/orders/{order_id}/quotation/send", "sale", {"warehouseId": KR})
    _, q = get_order_quotation(order_id)
    _, pay = call("PUT", f"/api/quotations/{q['data']['quotationId']}/confirm-and-pay", "customer", {"paymentMethod": "OFFLINE"})
    pay = pay["data"]
    call("PUT", f"/api/admin/finance/transactions/{pay['paymentId']}/approve", "admin",
         {"transactionCode": f"FT-GK-{RUN}-{suffix}", "receivedAmount": pay["amount"]})
    _, r = call("POST", "/api/warehouse-receiving-notes", "sale", {"consignmentOrderId": order_id, "warehouseId": KR})
    note = r["receivingNote"]
    call("PUT", f"/api/warehouse-receiving-notes/{note['id']}/status", "om1", {"status": "APPROVED"})
    _, detail = call("GET", f"/api/warehouse-receiving-notes/{note['id']}", "om1")
    packages = [{"orderItemId": e["orderItemId"], "actualWeight": 1.0, "length": 20, "width": 20, "height": 10}
                for e in detail["data"]["expectedItems"]]
    s, r = call("POST", "/api/parcels/check-in", "staff", {"shippingCode": note["receivingNoteCode"], "packages": packages})
    data = r.get("data", {})
    parcels = data.get("parcels") or [data]
    return s, order_id, {p["productName"]: p["parcelId"] for p in parcels}


def order_code_of(order_id):
    s, r = call("GET", f"/api/orders/consignments/{order_id}", "sale")
    return ((r.get("data") or r) if isinstance(r, dict) else {}).get("consignmentCode") or ""


def handling_map(order_id, who="customer"):
    s, r = call("GET", f"/api/orders/consignments/{order_id}/destination-handling", who)
    return s, {h["parcelId"].lower(): h for h in (r.get("data") or [])}


def pending_inbound(order_id):
    s, r = call("GET", "/api/warehouse-inbound-requests/pending-orders", "staff")
    rows = r.get("data") if isinstance(r, dict) else r
    rows = rows if isinstance(rows, list) else []
    return s, next((o for o in rows if str(o.get("orderId", "")).lower() == order_id.lower()), None)


def settle(order_id, who_pay="admin"):
    s, r = call("GET", f"/api/orders/{order_id}/settlement-preview", "customer")
    pv = r.get("data", {})
    if not check("Xem trước tất toán không còn vướng mắc", s == 200 and pv.get("canIssue") is True, pv.get("blockers")):
        return False
    s, r = call("POST", f"/api/orders/{order_id}/payments/final", "sale", {"paymentMethod": "SEPAY"})
    if not expect("Sale phát hành đợt tất toán", s, r, 200):
        return False
    final = r["data"]
    s, r = call("PUT", f"/api/admin/finance/transactions/{final['paymentId']}/approve", who_pay,
                {"transactionCode": f"FT-GK-F-{RUN}-{order_id[:6]}", "receivedAmount": final["finalAmount"]})
    return expect("Admin xác nhận tiền tất toán", s, r, 200)


def main():
    call("PUT", f"/api/warehouses/{KR}/manager", "admin", {"managerId": None, "reason": "Kiểm thử gửi kho VN"})
    call("PUT", f"/api/warehouses/{VN_WAREHOUSE}/manager", "admin", {"managerId": None, "reason": "Kiểm thử gửi kho VN"})
    set_region("staff", "KR")

    # ================= Chuẩn bị
    # Đơn 1: đặt đơn chọn GIAO NGAY, 2 kiện — khách đổi kiện A sang gửi kho khi hàng sắp về.
    # Đơn 2: đặt đơn chọn GỬI KHO VN ngay từ đầu, 1 kiện.
    s, o1, p1 = received_order(["GK A", "GK B"], f"G1{RUN}", "DIRECT_DELIVERY")
    if not check("Chuẩn bị đơn 1 (giao ngay, 2 kiện)", s in (200, 201) and len(p1) == 2, p1):
        return 1
    s, o2, p2 = received_order(["GK C"], f"G2{RUN}", "STORE_AT_VN")
    if not check("Chuẩn bị đơn 2 (chọn gửi kho VN lúc đặt đơn)", s in (200, 201) and len(p2) == 1, p2):
        return 1
    pa, pb, pc = p1["GK A"], p1["GK B"], p2["GK C"]
    code1 = order_code_of(o1)

    s, r = call("GET", f"/api/orders/consignments/{o2}", "customer")
    check("Đơn 2 lưu lựa chọn gửi kho VN lúc đặt đơn",
          (r.get("data") or {}).get("defaultDestinationHandling") == "STORE_AT_VN", r.get("data"))

    for pid, code in ((pa, "BIN-A1-01"), (pb, "BIN-A1-02"), (pc, "BIN-A2-01")):
        store(pid, code)
    shipment_id = ship_to_vietnam([pa, pb, pc], f"GK{RUN}")
    if not check("Lô chở 3 kiện (2 đơn) về tới kho VN", shipment_id is not None, shipment_id):
        return 1

    # ================= ① Khách chọn gửi lại kho VN (trước khi kho mở lô)
    s, h = handling_map(o1)
    check("Khách xem hướng xử lý: mặc định theo lựa chọn lúc đặt đơn (giao ngay)",
          s == 200 and h[pa.lower()]["handling"] == "DIRECT_DELIVERY" and h[pa.lower()]["canChange"], h)
    s, r = call("PUT", f"/api/orders/consignments/{o1}/destination-handling", "customer",
                {"items": [{"parcelId": pa, "handling": "STORE_AT_VN"}, {"parcelId": pb, "handling": "DIRECT_DELIVERY"}]})
    check("Khách chọn: kiện A gửi lại kho VN, kiện B giao ngay (lô chưa mở)", s == 200, r)
    s, r = call("PUT", f"/api/orders/consignments/{o1}/destination-handling", "customer2",
                {"items": [{"parcelId": pa, "handling": "DIRECT_DELIVERY"}]})
    expect("Khách khác không đổi hướng xử lý được", s, r, 403)
    s, h2 = handling_map(o2)
    check("Đơn 2: kiện C mặc định gửi kho VN", s == 200 and h2[pc.lower()]["handling"] == "STORE_AT_VN", h2)
    s, r = call("GET", f"/api/international-shipments/{shipment_id}", "staff")
    lot = {p["parcelId"].lower(): p for p in (r.get("data") or {}).get("parcels", [])}
    check("Kho VN mở lô thấy đúng ý khách TỪNG KIỆN (kiện A: gửi kho, dù đơn đặt giao ngay)",
          lot.get(pa.lower(), {}).get("customerIntent") == "STORE_AT_VN"
          and lot.get(pb.lower(), {}).get("customerIntent") == "DIRECT_DELIVERY", lot.get(pa.lower()))

    # ================= ② Kho VN tiếp nhận (cân đo + ảnh bắt buộc)
    base = f"/api/international-shipments/{shipment_id}/destination-receiving"
    s, r = call("PUT", base, "staff", {"items": [
        {"parcelId": pa, "actualWeight": 1.0, "length": 20, "width": 20, "height": 10, "condition": "GOOD"}]})
    check("Thiếu ảnh tiếp nhận thì bị chặn", s == 400 and "ảnh" in r.get("message", "").lower(), r)
    check("Chụp ảnh tiếp nhận cho 3 kiện", arrival_photos([pa, pb, pc]))
    s, r = call("PUT", base, "staff", {"items": [
        {"parcelId": p, "actualWeight": 1.0, "length": 20, "width": 20, "height": 10, "condition": "GOOD"}
        for p in (pa, pb, pc)]})
    if not expect("Kho VN tiếp nhận 3 kiện", s, r, 200):
        return 1
    rows = {p["parcelId"].lower(): p for p in r["data"]["parcels"]}
    check("Kiện A giữ lựa chọn gửi kho của khách sau khi tiếp nhận",
          rows[pa.lower()]["destinationHandling"] == "STORE_AT_VN" and rows[pa.lower()]["packageStatus"] == "RECEIVED_AT_DESTINATION",
          rows[pa.lower()])
    check("Kiện B giao ngay", rows[pb.lower()]["destinationHandling"] == "DIRECT_DELIVERY", rows[pb.lower()])
    check("Kiện C lấy lựa chọn lúc đặt đơn (gửi kho VN)", rows[pc.lower()]["destinationHandling"] == "STORE_AT_VN", rows[pc.lower()])
    t = tracking(o1)
    check("Khách thấy kho Việt Nam đã nhận hàng", t.get("currentStage") == "RECEIVED_AT_VN", t.get("currentStage"))

    # Đổi ý sau khi tiếp nhận vẫn được (chưa vào phiếu nhập kho / giao hàng).
    s, r = call("PUT", f"/api/orders/consignments/{o1}/destination-handling", "customer",
                {"items": [{"parcelId": pb, "handling": "STORE_AT_VN"}]})
    s2, r2 = call("PUT", f"/api/orders/consignments/{o1}/destination-handling", "customer",
                  {"items": [{"parcelId": pb, "handling": "DIRECT_DELIVERY"}]})
    check("Sau tiếp nhận khách vẫn đổi qua lại được khi kiện chưa vào phiếu", s == 200 and s2 == 200, (r, r2))

    # ================= ③ Chưa tất toán: chưa báo kho, chưa nhập kho, chưa giao
    s, r = call("PUT", f"/api/orders/consignments/{o1}/notify-warehouse", "sale", {"note": "thử"})
    check("Chưa tất toán thì Sale chưa báo kho được", s == 400 and "tất toán" in r.get("message", ""), r)
    s, r = call("POST", "/api/warehouse-inbound-requests", "staff", {"orderId": o1, "warehouseId": VN_WAREHOUSE})
    check("Chưa tất toán thì kho chưa lập phiếu nhập kho được", s == 400 and "tất toán" in r.get("message", ""), r)
    s, row = pending_inbound(o1)
    check("Đơn chưa tất toán không nằm trong 'Chờ lập phiếu' của kho VN", s == 200 and row is None, row)
    address = {"receiverName": "Nguyễn Test", "receiverPhone": "0900000000", "addressDetail": "1 Lê Lợi",
               "province": "Hồ Chí Minh", "district": "Quận 1", "ward": "Bến Nghé"}
    s, r = call("POST", "/api/delivery-requests", "customer", {"orderId": o1, "parcelIds": [pb], **address})
    check("Chưa tất toán thì khách chưa đặt giao được", s == 400 and "tất toán" in r.get("message", ""), r)

    # ================= ④ Tất toán đơn 1
    if not settle(o1):
        return 1

    # ================= ⑤ Sale báo kho → kho VN lập phiếu nhập kho
    s, row = pending_inbound(o1)
    check("Đã tất toán nhưng Sale chưa báo kho thì đơn ký gửi chưa vào 'Chờ lập phiếu'", s == 200 and row is None, row)
    s, r = call("POST", "/api/warehouse-inbound-requests", "staff", {"orderId": o1, "warehouseId": VN_WAREHOUSE})
    check("Chưa báo kho thì kho chưa lập phiếu được (nêu rõ lý do)", s == 400 and "Sale" in r.get("message", ""), r)
    s, r = call("PUT", f"/api/orders/consignments/{o1}/notify-warehouse", "staff", {"note": "thử"})
    expect("Kho không tự báo kho thay Sale", s, r, 403)
    s, r = call("PUT", f"/api/orders/consignments/{o1}/notify-warehouse", "sale", {"note": "Khách gửi kho kiện A"})
    check("Sale báo kho: 1 kiện gửi kho", s == 200 and (r.get("data") or {}).get("storeAtVnParcelCount") == 1, r)

    s, row = pending_inbound(o1)
    check("Đơn hiện ở 'Chờ lập phiếu' của kho VN, chỉ kiện gửi kho",
          s == 200 and row is not None and [x.lower() for x in row["parcelIds"]] == [pa.lower()], row)
    s, r = call("POST", "/api/warehouse-inbound-requests", "staff",
                {"orderId": o1, "warehouseId": VN_WAREHOUSE, "parcelIds": [pb]})
    check("Kiện giao ngay không lập phiếu nhập kho được", s == 400 and "giao thẳng" in r.get("message", ""), r)
    s, r = call("POST", "/api/warehouse-inbound-requests", "customer", {"orderId": o1, "warehouseId": VN_WAREHOUSE})
    expect("Khách không tự lập phiếu nhập kho", s, r, 403)
    s, r = call("POST", "/api/warehouse-inbound-requests", "staff",
                {"orderId": o1, "warehouseId": VN_WAREHOUSE, "parcelIds": row["parcelIds"] if row else [pa]})
    if not check("Kho VN lập phiếu nhập kho", s in (200, 201) and (r.get("data") or {}).get("status") == "INBOUND_PENDING", r):
        return 1
    inbound_id = r["data"]["inboundRequestId"]
    line = next((p for p in r["data"]["parcels"] if p["parcelId"].lower() == pa.lower()), {})
    check("Phiếu nhập kho ghi ý khách của kiện A là gửi kho (OM không bị báo nhầm 'muốn giao ngay')",
          line.get("customerIntent") == "STORE_AT_VN", line)
    s, row = pending_inbound(o1)
    check("Lập phiếu xong đơn rời 'Chờ lập phiếu'", s == 200 and row is None, row)
    s, h = handling_map(o1)
    check("Kiện đã vào phiếu nhập kho thì khoá đổi hướng", s == 200 and not h[pa.lower()]["canChange"], h[pa.lower()])
    s, r = call("PUT", f"/api/orders/consignments/{o1}/destination-handling", "customer",
                {"items": [{"parcelId": pa, "handling": "DIRECT_DELIVERY"}]})
    check("Khách đổi kiện đã vào phiếu thì báo lý do", s == 400 and "phiếu nhập kho" in r.get("message", ""), r)

    # ================= ⑥ OM duyệt nhập kho → tự xếp kệ
    s, ids = pending_putaway_ids("admin", code1, "VN")
    check("Phiếu chưa duyệt thì kiện chưa phải 'chờ xếp kệ' ở VN", s == 200 and pa.lower() not in ids, ids)
    s, r = call("PUT", f"/api/parcels/{pa}/put-away", "staff", {"warehouseId": VN_WAREHOUSE, "binCode": "BIN-VN-01"})
    check("Phiếu chưa duyệt thì kho không xếp kệ được", s == 400, r)
    s, r = call("PUT", f"/api/warehouse-inbound-requests/{inbound_id}/status", "staff", {"status": "INBOUND_APPROVED"})
    expect("Nhân viên kho không tự duyệt phiếu", s, r, 403)
    s, r = call("PUT", f"/api/warehouse-inbound-requests/{inbound_id}/status", "om1", {"status": "INBOUND_REJECTED"})
    check("Từ chối phải ghi lý do", s == 400 and "lý do" in r.get("message", ""), r)
    s, r = call("GET", "/api/warehouse-inbound-requests?status=INBOUND_PENDING", "om1")
    lst = ((r.get("data") or {}).get("items") or []) if isinstance(r, dict) else []
    check("OM thấy phiếu trong danh sách chờ duyệt",
          s == 200 and any(x["inboundRequestId"].lower() == inbound_id.lower() for x in (lst or [])), s)
    mark = sql_scalar("SELECT CONVERT(varchar(30), DATEADD(second, -1, GETUTCDATE()), 126)")
    s, r = call("PUT", f"/api/warehouse-inbound-requests/{inbound_id}/status", "om1", {"status": "INBOUND_APPROVED"})
    if not expect("OM duyệt nhập kho VN", s, r, 200):
        return 1
    line = next((p for p in r["data"]["parcels"] if p["parcelId"].lower() == pa.lower()), {})
    check("Duyệt xong kiện A tự lên kệ khu lưu kho VN", line.get("packageStatus") == "STORED" and line.get("binCode"), line)
    s, ids = pending_putaway_ids("admin", code1, "VN")
    check("Đã có ô thì không còn ở 'chờ xếp kệ'", s == 200 and pa.lower() not in ids, ids)
    check("Khách thấy kiện A đang lưu kho Việt Nam", parcel_status(o1, pa)[1] == "STORED_AT_VN", parcel_status(o1, pa))

    # ================= ⑦ Tồn kho VN
    s, r = call("GET", f"/api/inventories?warehouseId={VN_WAREHOUSE}", "staff")
    items = (r.get("items") or (r.get("data") or {}).get("items") or r.get("data") or []) if isinstance(r, dict) else r
    inv = next((x for x in (items if isinstance(items, list) else []) if str(x.get("parcelId", "")).lower() == pa.lower()), None)
    check("Kiện A có trong tồn kho VN", s == 200 and inv is not None, (s, str(r)[:300]))
    inv_b = next((x for x in (items if isinstance(items, list) else []) if str(x.get("parcelId", "")).lower() == pb.lower()), None)
    check("Kiện giao ngay không nằm tồn kho VN", inv_b is None, inv_b)

    # ================= ⑧ Phí lưu kho VN
    s, r = call("GET", f"/api/orders/{o1}/storage-fee", "customer")
    fee = r.get("data", {})
    fa = next((p for p in fee.get("parcels", []) if p["parcelId"].lower() == pa.lower()), {})
    fb = next((p for p in fee.get("parcels", []) if p["parcelId"].lower() == pb.lower()), {})
    check("Khách xem phí lưu kho: mới nhập kho nên 0đ, chỉ tính chặng kho VN",
          s == 200 and fee.get("totalAmount") == 0 and len(fa.get("legs", [])) == 1 and not fb.get("legs"), fee)
    # Lùi giờ nhập kho VN 30 ngày trên DB thử để phát sinh phí.
    sql(f"UPDATE INVENTORY_MOVEMENTS SET moved_at = DATEADD(day, -30, moved_at) "
        f"WHERE parcel_id = '{pa}' AND moved_at >= '{mark}';"
        f"UPDATE PARCELS SET put_away_time = DATEADD(day, -30, put_away_time) WHERE id = '{pa}';")
    s, r = call("GET", f"/api/orders/{o1}/storage-fee", "customer")
    fee = r.get("data", {})
    fa = next((p for p in fee.get("parcels", []) if p["parcelId"].lower() == pa.lower()), {})
    expected_days = 30 - fee.get("freeDays", 0) - fee.get("graceDays", 0)
    check("Nằm kho VN 30 ngày: tính phí sau ngày miễn phí + ân hạn",
          s == 200 and fa.get("chargeableDays") in (expected_days, expected_days + 1) and fee.get("totalAmount", 0) > 0, fee)

    # ================= ⑨ Khách đặt giao kiện đang lưu kho
    s, r = call("POST", "/api/delivery-requests", "customer", {"orderId": o1, "parcelIds": [pa], **address})
    if not expect("Khách tự đặt giao kiện đang lưu kho VN", s, r, 201):
        return 1
    d_store = r["data"]["deliveryRequestId"]
    s, r = call("POST", "/api/delivery-requests", "customer", {"orderId": o1, "parcelIds": [pa], **address})
    check("Không đặt giao trùng một kiện", s == 400 and "yêu cầu giao hàng khác" in r.get("message", ""), r)
    s, r = call("PUT", f"/api/delivery-requests/{d_store}/status", "sale", {"status": "DELIVERY_APPROVED"})
    expect("Sale không tự duyệt giao", s, r, 403)
    s, r = call("PUT", f"/api/delivery-requests/{d_store}/status", "om1", {"status": "DELIVERY_APPROVED"})
    expect("OM duyệt giao hàng", s, r, 200)
    s, r = call("POST", f"/api/delivery-requests/{d_store}/dispatch", "staff", {})
    check("Chưa chốt/thu phí lưu kho thì kho chưa đặt giao được", s == 400 and "phí lưu kho" in r.get("message", ""), r)
    s, r = call("POST", f"/api/orders/{o1}/payments/storage-fee", "customer", {})
    expect("Khách không tự chốt phí lưu kho", s, r, 403)
    s, r = call("POST", f"/api/orders/{o1}/payments/storage-fee", "staff", {})
    sf = r.get("data", {})
    if not check("Kho chốt phí lưu kho, có mã thanh toán", s == 200 and sf.get("storageFeeAmount", 0) > 0 and sf.get("paymentId"), r):
        return 1
    s, r = call("POST", f"/api/delivery-requests/{d_store}/dispatch", "staff", {})
    check("Mã phí lưu kho chưa trả thì vẫn chặn đặt giao", s == 400 and "phí lưu kho" in r.get("message", ""), r)
    s, r = call("PUT", f"/api/admin/finance/transactions/{sf['paymentId']}/approve", "admin",
                {"transactionCode": f"FT-GK-S-{RUN}", "receivedAmount": sf["storageFeeAmount"]})
    expect("Admin xác nhận tiền phí lưu kho", s, r, 200)
    s, r = call("POST", f"/api/delivery-requests/{d_store}/dispatch", "staff", {})
    check("Trả phí lưu kho rồi thì qua chốt phí (môi trường thử dừng ở bước gọi GoShip)",
          "phí lưu kho" not in r.get("message", ""), (s, r))
    if s != 200:
        simulate_dispatch(d_store)
    check("Kiện A rời kệ đi giao", parcel_status(o1, pa)[1] == "OUT_FOR_DELIVERY", parcel_status(o1, pa))
    upload("sale", "DELIVERY_REQUEST", d_store, "DELIVERY_PROOF", "ky-nhan-gk.pdf")
    s, r = call("POST", f"/api/delivery-requests/{d_store}/proof", "sale", {"receivedBy": "Nguyễn Test"})
    expect("Sale ghi bằng chứng giao kiện A", s, r, 200)
    check("Kiện A đã giao", parcel_status(o1, pa)[0] == "DELIVERED", parcel_status(o1, pa))

    # ================= ⑩ Nhánh giao ngay (đối chiếu)
    s, r = call("POST", "/api/delivery-requests", "customer", {"orderId": o1, "parcelIds": [pb], **address})
    if not expect("Giao ngay: khách đặt giao ngay sau tất toán, không qua nhập kho", s, r, 201):
        return 1
    d_direct = r["data"]["deliveryRequestId"]
    call("PUT", f"/api/delivery-requests/{d_direct}/status", "om1", {"status": "DELIVERY_APPROVED"})
    s, r = call("POST", f"/api/delivery-requests/{d_direct}/dispatch", "staff", {})
    check("Giao ngay không vướng phí lưu kho", "phí lưu kho" not in r.get("message", ""), (s, r))
    if s != 200:
        simulate_dispatch(d_direct)
    upload("sale", "DELIVERY_REQUEST", d_direct, "DELIVERY_PROOF", "ky-nhan-gn.pdf")
    s, r = call("POST", f"/api/delivery-requests/{d_direct}/proof", "sale", {"receivedBy": "Nguyễn Test"})
    expect("Sale ghi bằng chứng giao kiện B", s, r, 200)
    s, r = call("GET", f"/api/orders/consignments/{o1}", "customer")
    check("Hai kiện đã giao thì đơn 1 DELIVERED", (r.get("data") or {}).get("status") == "DELIVERED", (r.get("data") or {}).get("status"))

    # ================= ⑪ Đơn 2: chọn gửi kho lúc đặt đơn, chưa tất toán
    s, r = call("PUT", f"/api/orders/consignments/{o2}/notify-warehouse", "sale", {})
    check("Đơn 2 chưa tất toán thì chưa báo kho được", s == 400 and "tất toán" in r.get("message", ""), r)
    s, row = pending_inbound(o2)
    check("Đơn 2 chưa nằm 'Chờ lập phiếu'", s == 200 and row is None, row)

    print(f"\n{len(PASSED)} PASS / {len(FAILED)} FAIL")
    if FAILED:
        print("Lỗi: " + ", ".join(FAILED))
    return 1 if FAILED else 0


if __name__ == "__main__":
    sys.exit(main() or 0)
