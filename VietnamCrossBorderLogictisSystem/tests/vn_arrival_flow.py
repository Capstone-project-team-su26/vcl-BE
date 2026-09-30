#!/usr/bin/env python3
"""
Kiểm thử luồng hàng về Việt Nam: kho VN tiếp nhận + cân đo → sự cố → khách chọn hướng từng kiện
→ tất toán theo cân đo VN → lưu kho / giao ngay → giao thất bại, hoàn hàng, giao lại có phí →
bằng chứng giao → khiếu nại → đóng đơn.

Bước "đặt giao GoShip" không gọi hãng thật trên môi trường thử: trạng thái sau khi đặt giao và
kết quả giao do hãng báo về được giả lập thẳng trong DB thử (hàm simulate_*).

    APP_ENV=/root/vcl-migration-deploy/app.env API=http://127.0.0.1:8088 python3 vn_arrival_flow.py
"""
import json, subprocess, sys, urllib.error, urllib.request, uuid

from wave1_consignment_flow import API, IDS, PASSED, FAILED, RUN, TOK, call, check, expect
from warehouse_zones_flow import received_parcels
from export_flow import KR, VN_WAREHOUSE, route_for_kr, set_region, store, upload


def sql(statement):
    """Chạy lệnh trên DB THỬ (AppDb_migration) — chỉ để giả lập phần hãng giao hàng báo về."""
    env = subprocess.run(["docker", "inspect", "sqlserver", "--format", "{{range .Config.Env}}{{println .}}{{end}}"],
                         capture_output=True, text=True).stdout
    sa = next(l.split("=", 1)[1] for l in env.splitlines() if "SA_PASSWORD=" in l)
    subprocess.run(["docker", "exec", "sqlserver", "/opt/mssql-tools18/bin/sqlcmd", "-S", "localhost", "-U", "sa",
                    "-P", sa, "-C", "-d", "AppDb_migration", "-b", "-Q", "SET NOCOUNT ON; " + statement],
                   check=True, capture_output=True)


def simulate_dispatch(delivery_id):
    """Giả lập kho đã đặt GoShip thành công: phiếu DISPATCHED, kiện chờ lấy, rời ô kệ."""
    sql(f"UPDATE DELIVERY_REQUESTS SET status = N'DELIVERY_DISPATCHED', dispatched_at = GETUTCDATE(), "
        f"carrier_tracking_code = N'GS-TEST-{RUN}' WHERE id = '{delivery_id}';"
        f"UPDATE p SET package_status = N'AWAITING_PICKUP', bin_id = NULL FROM PARCELS p "
        f"JOIN DELIVERY_REQUEST_ITEMS i ON i.parcel_id = p.id WHERE i.delivery_request_id = '{delivery_id}';")


def simulate_carrier_status(parcel_id, status):
    sql(f"UPDATE PARCELS SET package_status = N'{status}' WHERE id = '{parcel_id}';")


def parcel_status(order_id, parcel_id):
    s, r = call("GET", f"/api/orders/consignments/{order_id}/tracking", "sale")
    row = next((p for p in r["data"]["parcels"] if p["parcelId"].lower() == parcel_id.lower()), {})
    return row.get("packageStatus"), row.get("stage")


def tracking(order_id, who="customer"):
    s, r = call("GET", f"/api/orders/consignments/{order_id}/tracking", who)
    return r.get("data", {})


def pending_putaway_ids(who, search, region=None):
    """Id các kiện "chờ xếp kệ" (GET /api/parcels/pending-putaway) khớp mã đơn/mã kiện."""
    path = f"/api/parcels/pending-putaway?pageSize=100&search={search}"
    if region:
        path += f"&regionCode={region}"
    s, r = call("GET", path, who)
    items = (r.get("data") or {}).get("items", []) if isinstance(r, dict) else []
    return s, {str(i.get("id", "")).lower() for i in items}


def arrival_photo(entity_id, who="staff", entity_type="PARCEL", content_type="image/png", name="anh-tiep-nhan.png"):
    """
    Chụp ảnh kiện lúc kho VN tiếp nhận (VN_ARRIVAL_PROOF) — bắt buộc mỗi kiện trước khi xác nhận
    tiếp nhận. Trả (status, response). Dựng multipart tại chỗ như put_away_photo của wave1.
    """
    boundary = "----vclvn" + uuid.uuid4().hex
    content = b"\x89PNG\r\n\x1a\n vn arrival\n" if content_type.startswith("image/") else b"%PDF-1.4\n%%EOF\n"
    parts = []
    for key, value in (("entityType", entity_type), ("entityId", str(entity_id)),
                       ("documentType", "VN_ARRIVAL_PROOF"), ("note", "kiểm thử")):
        parts.append(f"--{boundary}\r\nContent-Disposition: form-data; name=\"{key}\"\r\n\r\n{value}\r\n".encode())
    parts.append(f"--{boundary}\r\nContent-Disposition: form-data; name=\"file\"; filename=\"{name}\"\r\n"
                 f"Content-Type: {content_type}\r\n\r\n".encode() + content + b"\r\n")
    parts.append(f"--{boundary}--\r\n".encode())

    req = urllib.request.Request(API + "/api/attachments", data=b"".join(parts), method="POST")
    req.add_header("Content-Type", f"multipart/form-data; boundary={boundary}")
    req.add_header("Authorization", "Bearer " + TOK[who])
    try:
        with urllib.request.urlopen(req, timeout=60) as resp:
            return resp.status, json.loads(resp.read().decode() or "{}")
    except urllib.error.HTTPError as e:
        raw = e.read().decode() or "{}"
        try:
            return e.code, json.loads(raw)
        except json.JSONDecodeError:
            return e.code, {"raw": raw}


def arrival_photos(parcel_ids, who="staff"):
    """Chụp ảnh tiếp nhận cho cả danh sách kiện; trả True nếu mọi ảnh đều lên được."""
    return all(arrival_photo(pid, who)[0] == 200 for pid in parcel_ids)


def ship_to_vietnam(parcels, suffix):
    """Đưa các kiện đã lưu kho Seoul đi hết luồng xuất tới mốc ARRIVED_DESTINATION."""
    route_id = route_for_kr()
    s, r = call("POST", "/api/warehouse-release-requests", "staff", {"shippingRouteId": route_id, "parcelIds": parcels})
    wro_id = r["data"]["id"]
    call("POST", f"/api/warehouse-release-requests/{wro_id}/submit", "staff")
    # Duyệt phiếu xuất bắt buộc có ảnh hiện trạng lô tại thời điểm duyệt.
    upload("om1", "WRO", wro_id, "WRO_APPROVAL_PROOF", "anh-lo-hang.png")
    call("POST", f"/api/warehouse-release-requests/{wro_id}/decide", "om1", {"decision": "APPROVED", "reason": "OK"})
    for pid in parcels:
        call("POST", f"/api/warehouse-release-requests/{wro_id}/pick", "staff", {"parcelId": pid, "stagingBinCode": "BIN-OUT-01"})
    s, r = call("POST", "/api/international-shipments", "staff", {"wroRequestIds": [wro_id], "shippingMethod": "AIR"})
    shipment_id = r["data"]["shipmentId"]
    upload("staff", "SHIPMENT", shipment_id, "HANDOVER_RECORD")
    upload("staff", "SHIPMENT", shipment_id, "WAYBILL")
    s, carriers = call("GET", "/api/carriers/active", "staff")
    carrier_id = (carriers if isinstance(carriers, list) else carriers.get("data", []))[0]["id"]
    call("POST", f"/api/international-shipments/{shipment_id}/handover", "staff",
         {"carrierId": carrier_id, "recipientName": "Anh Choi", "waybillCode": f"WB-VN-{suffix}"})
    for status in ("DEPARTED", "IN_TRANSIT", "ARRIVED_VN", "ARRIVED_DESTINATION"):
        s, r = call("PUT", f"/api/international-shipments/{shipment_id}/status", "sale", {"status": status})
        if s != 200:
            return None
    return shipment_id


def main():
    call("PUT", f"/api/warehouses/{KR}/manager", "admin", {"managerId": None, "reason": "Kiểm thử luồng VN"})
    call("PUT", f"/api/warehouses/{VN_WAREHOUSE}/manager", "admin", {"managerId": None, "reason": "Kiểm thử luồng VN"})
    set_region("staff", "KR")

    # ---------------- Chuẩn bị: đơn 3 kiện đi tới kho VN
    s, order_id, parcels = received_parcels(["VN A", "VN B", "VN C"], f"V{RUN}")
    if not check("Chuẩn bị đơn 3 kiện", s in (200, 201) and len(parcels) == 3, parcels):
        return 1
    pa, pb, pc = parcels["VN A"], parcels["VN B"], parcels["VN C"]
    s, r = call("GET", f"/api/orders/consignments/{order_id}", "sale")
    order_code = ((r.get("data") or r) if isinstance(r, dict) else {}).get("consignmentCode") or ""
    s, ids = pending_putaway_ids("staff", order_code)
    check("Kiện vừa check-in ở kho nguồn là chờ xếp kệ của kho nguồn",
          s == 200 and {pa.lower(), pb.lower(), pc.lower()} <= ids, (s, order_code, ids))
    for pid, code in ((pa, "BIN-A1-01"), (pb, "BIN-A1-02"), (pc, "BIN-A2-01")):
        store(pid, code)
    s, ids = pending_putaway_ids("staff", order_code)
    check("Xếp kệ xong thì hết chờ xếp kệ", s == 200 and not ({pa.lower(), pb.lower(), pc.lower()} & ids), ids)
    shipment_id = ship_to_vietnam([pa, pb, pc], RUN)
    if not check("Lô chở 3 kiện về tới kho VN", shipment_id is not None, shipment_id):
        return 1
    s, r = call("GET", f"/api/international-shipments/{shipment_id}", "staff")
    check("Chi tiết lô (luồng xuất mới) liệt kê đủ 3 kiện", s == 200 and len(r["data"]["parcels"]) == 3, r)
    s, r = call("GET", "/api/international-shipments?statusTab=ARRIVED&pageSize=100", "staff")
    row = next((x for x in r["data"]["items"] if x["shipmentId"] == shipment_id), {})
    check("Danh sách lô đã về cũng có đủ kiện (kho VN mới thấy lô để nhận)", len(row.get("parcels", [])) == 3, row)

    # ---------------- Tất toán bị chặn khi kho VN chưa kiểm
    s, r = call("GET", f"/api/orders/{order_id}/settlement-preview", "customer")
    blockers = [b["code"] for b in r.get("data", {}).get("blockers", [])]
    check("Xem trước tất toán báo kho VN chưa kiểm", s == 200 and "PARCEL_NOT_INSPECTED" in blockers, r)
    s, r = call("GET", f"/api/orders/{order_id}/settlement-preview", "customer2")
    expect("Khách khác không xem được tất toán", s, r, 403)
    s, r = call("POST", f"/api/orders/{order_id}/payments/final", "sale", {"paymentMethod": "SEPAY"})
    check("Sale chưa phát hành đợt cuối được khi kho VN chưa kiểm", s == 400 and "chưa kiểm" in r.get("message", ""), r)

    # ---------------- ② Kho VN tiếp nhận
    base = f"/api/international-shipments/{shipment_id}/destination-receiving"
    s, r = call("PUT", base, "staff", {"parcelIds": [pa]})
    check("Tiếp nhận không cân đo bị chặn", s == 400 and "cân đo" in r.get("message", ""), r)
    s, r = call("PUT", base, "staff", {"items": [{"parcelId": pa, "actualWeight": 1.0}]})
    check("Thiếu kích thước bị chặn", s == 400 and "dài/rộng/cao" in r.get("message", ""), r)
    check("Chụp ảnh tiếp nhận cho 3 kiện", arrival_photos([pa, pb, pc]))
    s, r = call("PUT", base, "staff", {"items": [
        {"parcelId": pa, "actualWeight": 1.0, "length": 20, "width": 20, "height": 10, "condition": "GOOD"},
        {"parcelId": pb, "actualWeight": 1.0, "length": 20, "width": 20, "height": 10, "condition": "DAMAGED",
         "inspectionNote": "Thùng gỗ vỡ một góc, hàng bên trong móp"},
        {"parcelId": pc, "actualWeight": 1.5, "length": 20, "width": 20, "height": 10, "condition": "GOOD"}]})
    if not expect("Kho VN tiếp nhận 3 kiện có cân đo", s, r, 200):
        return 1
    rows = {p["parcelId"].lower(): p for p in r["data"]["parcels"]}
    check("Kiện tốt vào khu nhận", rows[pa.lower()]["packageStatus"] == "RECEIVED_AT_DESTINATION", rows[pa.lower()])
    check("Kiện hỏng vào khu cách ly và tự mở sự cố",
          rows[pb.lower()]["packageStatus"] == "QUARANTINED" and rows[pb.lower()]["incidentType"] == "DAMAGED", rows[pb.lower()])
    check("Cân lệch > 10% tự mở sự cố cho quản lý kho",
          rows[pc.lower()]["packageStatus"] == "RECEIVED_AT_DESTINATION" and rows[pc.lower()]["incidentType"] == "WEIGHT_DEVIATION",
          rows[pc.lower()])
    check("Biên bản lưu kích thước đo tại VN",
          (rows[pa.lower()].get("inspection") or {}).get("length") == 20, rows[pa.lower()].get("inspection"))
    s, r = call("PUT", base, "staff", {"items": [
        {"parcelId": pa, "actualWeight": 1.0, "length": 20, "width": 20, "height": 10}]})
    expect("Không tiếp nhận một kiện hai lần", s, r, 400)

    # Kiện đã rời kho nguồn, kho VN đã nhận (RECEIVED_AT_DESTINATION, không còn ô) KHÔNG được hiện
    # "chờ xếp kệ" ở kho nguồn — lỗi cũ: tính theo bin_id = NULL + gán về kho của phiếu tiếp nhận.
    arrived = {pa.lower(), pc.lower()}
    s, ids = pending_putaway_ids("staff", order_code)
    check("Kho nguồn không còn thấy kiện kho đích đã nhận là chờ xếp kệ", s == 200 and not (arrived & ids), ids)
    s, ids = pending_putaway_ids("admin", order_code, "KR")
    check("Lọc theo vùng kho nguồn cũng không còn kiện đã về VN", s == 200 and not (arrived & ids), ids)
    s, ids = pending_putaway_ids("admin", order_code, "VN")
    check("Kho VN chưa có phiếu nhập kho duyệt thì kiện chưa phải chờ xếp kệ", s == 200 and not (arrived & ids), ids)

    t = tracking(order_id)
    check("Khách thấy kho Việt Nam đã nhận hàng",
          any(e["title"] == "Kho Việt Nam đã nhận hàng" for e in t.get("events", []))
          and t.get("currentStage") in ("RECEIVED_AT_VN", "QUARANTINED"), t.get("currentStage"))

    # ---------------- ③ Khách chọn hướng từng kiện
    s, r = call("GET", f"/api/orders/consignments/{order_id}/destination-handling", "customer")
    check("Khách xem hướng xử lý từng kiện", s == 200 and len(r.get("data", [])) == 3, r)
    s, r = call("PUT", f"/api/orders/consignments/{order_id}/destination-handling", "customer2",
                {"items": [{"parcelId": pa, "handling": "STORE_AT_VN"}]})
    expect("Khách khác không đổi được hướng xử lý", s, r, 403)
    s, r = call("PUT", f"/api/orders/consignments/{order_id}/destination-handling", "customer",
                {"items": [{"parcelId": pa, "handling": "STORE_AT_VN"}, {"parcelId": pc, "handling": "DIRECT_DELIVERY"}]})
    handling = {h["parcelId"].lower(): h["handling"] for h in r.get("data", [])}
    check("Khách chọn: kiện A gửi kho, kiện C giao ngay",
          s == 200 and handling.get(pa.lower()) == "STORE_AT_VN" and handling.get(pc.lower()) == "DIRECT_DELIVERY", r)
    s, r = call("PUT", f"/api/orders/consignments/{order_id}/destination-handling", "customer",
                {"items": [{"parcelId": pa, "handling": "GIAO_BAY"}]})
    expect("Hướng xử lý lạ bị chặn", s, r, 400)

    # ---------------- Sự cố
    s, r = call("POST", f"/api/orders/{order_id}/payments/final", "sale", {"paymentMethod": "SEPAY"})
    check("Còn sự cố mở thì chưa tất toán", s == 400 and "sự cố" in r.get("message", ""), r)

    s, r = call("GET", f"/api/parcel-incidents?orderId={order_id}", "customer")
    incidents = {i["incidentType"]: i for i in r.get("data", {}).get("items", [])}
    check("Khách thấy sự cố của đơn mình", s == 200 and {"DAMAGED", "WEIGHT_DEVIATION"} <= set(incidents), r)
    s, r = call("GET", f"/api/parcel-incidents?orderId={order_id}", "customer2")
    check("Khách khác không thấy sự cố", s == 200 and r["data"]["totalCount"] == 0, r)
    damaged, deviation = incidents["DAMAGED"], incidents["WEIGHT_DEVIATION"]
    check("Sự cố hỏng chờ khách chọn, cân lệch thì không", damaged["awaitsCustomerChoice"] and not deviation["awaitsCustomerChoice"],
          (damaged, deviation))

    s, r = call("POST", f"/api/parcel-incidents/{deviation['id']}/customer-response", "customer", {"choice": "ACCEPT"})
    expect("Khách không chọn cho sự cố cân lệch", s, r, 400)
    s, r = call("POST", f"/api/parcel-incidents/{damaged['id']}/customer-response", "customer",
                {"choice": "DISPOSE", "note": "Hàng móp rồi, huỷ giúp tôi"})
    check("Khách chọn huỷ hàng hỏng", s == 200 and r["data"]["status"] == "CUSTOMER_RESPONDED", r)

    # Ảnh kho chụp lúc tiếp nhận kiện hỏng đã tự gắn vào sự cố làm ảnh hiện trạng.
    s, r = call("GET", f"/api/attachments?entityType=INCIDENT&entityId={damaged['id']}", "om1")
    check("Sự cố hỏng đã có sẵn ảnh hiện trạng từ ảnh tiếp nhận",
          s == 200 and any(a["documentType"] == "INCIDENT_PHOTO" for a in r.get("data", [])), r)
    s, up = upload("staff", "INCIDENT", damaged["id"], "INCIDENT_PHOTO", "anh-hong.pdf")
    expect("Kho đính kèm thêm ảnh hiện trạng", s, up, 200)
    s, r = call("POST", f"/api/parcel-incidents/{damaged['id']}/resolve", "staff", {"resolution": "DISPOSE"})
    expect("Người lập biên bản không tự quyết", s, r, 403)
    s, r = call("POST", f"/api/parcel-incidents/{damaged['id']}/resolve", "om1", {"resolution": "DISPOSE", "note": "Theo yêu cầu khách"})
    check("Quản lý kho quyết huỷ hàng", s == 200 and r["data"]["status"] == "RESOLVED", r)
    check("Kiện hỏng chuyển DISPOSED", parcel_status(order_id, pb)[0] == "DISPOSED", parcel_status(order_id, pb))

    s, r = call("POST", f"/api/parcel-incidents/{deviation['id']}/resolve", "om1", {"resolution": "COMPENSATE", "compensationAmount": 1})
    expect("Cân lệch không bồi thường được", s, r, 400)
    s, r = call("POST", f"/api/parcel-incidents/{deviation['id']}/resolve", "om1", {"resolution": "ACCEPT", "note": "Đã cân lại, đúng 1.5kg"})
    expect("Quản lý kho chấp nhận số cân VN", s, r, 200)

    # ---------------- ④ Tất toán theo cân đo VN
    s, r = call("GET", f"/api/orders/{order_id}/settlement-preview", "customer")
    pv = r.get("data", {})
    check("Hết vướng mắc thì phát hành được", s == 200 and pv.get("canIssue") is True, pv.get("blockers"))
    active = [p for p in pv.get("parcels", []) if not p["isDisposed"]]
    expected_chargeable = max(sum(p["chargeableWeight"] for p in active), pv.get("minimumWeight", 0))
    check("Kiện huỷ không tính cước", len(active) == 2 and abs(pv["vnChargeableWeight"] - expected_chargeable) < 0.001, pv)
    check("Cước tính lại đúng công thức",
          pv["freightRate"] > 0 and pv["vnFreight"] == round(pv["vnChargeableWeight"] * pv["freightRate"])
          and pv["freightAdjustment"] == pv["vnFreight"] - pv["quotedFreight"], pv)
    check("VAT điều chỉnh theo % trên báo giá",
          pv["vatAdjustment"] == round(pv["freightAdjustment"] * pv["vatRatePercent"] / 100), pv)

    s, r = call("POST", f"/api/orders/{order_id}/payments/final", "sale", {"paymentMethod": "SEPAY"})
    if not expect("Sale phát hành đợt cuối", s, r, 200):
        return 1
    final = r["data"]
    check("Đợt cuối ghi đúng phần điều chỉnh cước",
          final["freightAdjustmentDelta"] == pv["freightAdjustment"] + pv["vatAdjustment"], (final, pv))
    check("Số tiền đợt cuối khớp xem trước", abs(final["finalAmount"] - pv["estimatedFinalAmount"]) < 1, (final, pv))
    s, r = call("POST", f"/api/orders/{order_id}/payments/final", "sale", {"paymentMethod": "SEPAY"})
    check("Phát hành lại không cộng dồn điều chỉnh",
          s == 200 and r["data"]["freightAdjustmentDelta"] == 0 and abs(r["data"]["finalAmount"] - final["finalAmount"]) < 1, r)
    final = r["data"]
    s, r = call("PUT", f"/api/admin/finance/transactions/{final['paymentId']}/approve", "admin",
                {"transactionCode": f"FT-VN-{RUN}", "receivedAmount": final["finalAmount"]})
    expect("Admin xác nhận tiền tất toán", s, r, 200)
    t = tracking(order_id)
    check("Khách thấy mốc đã thanh toán đợt cuối", any(e["title"] == "Đã thanh toán đợt cuối" for e in t["events"]), t["events"])

    # ---------------- ⑤b Giao ngay kiện C: khách tự đặt giao, giao thất bại, hoàn hàng, giao lại có phí
    address = {"receiverName": "Nguyễn Test", "receiverPhone": "0900000000", "addressDetail": "1 Lê Lợi",
               "province": "Hồ Chí Minh", "district": "Quận 1", "ward": "Bến Nghé"}
    s, r = call("POST", "/api/delivery-requests", "customer2", {"orderId": order_id, "parcelIds": [pc], **address})
    expect("Khách khác không đặt giao được", s, r, 403)
    s, r = call("POST", "/api/delivery-requests", "customer", {"orderId": order_id, "parcelIds": [pc], "redeliveryFee": 10000, **address})
    expect("Khách không tự nhập phí giao lại", s, r, 403)
    s, r = call("POST", "/api/delivery-requests", "customer", {"orderId": order_id, "parcelIds": [pc], **address})
    if not expect("Khách tự đặt giao kiện giao ngay", s, r, 201):
        return 1
    d1 = r["data"]["deliveryRequestId"]
    call("PUT", f"/api/delivery-requests/{d1}/status", "om1", {"status": "DELIVERY_APPROVED"})
    s, r = call("POST", f"/api/delivery-requests/{d1}/receive-return", "staff", {"note": "thử"})
    expect("Chưa đặt giao thì chưa nhận hàng hoàn", s, r, 400)
    simulate_dispatch(d1)
    check("Khách thấy đang giao hàng", parcel_status(order_id, pc)[1] == "OUT_FOR_DELIVERY", parcel_status(order_id, pc))
    simulate_carrier_status(pc, "DELIVERY_FAILED")
    s, r = call("POST", f"/api/delivery-requests/{d1}/receive-return", "customer", {})
    expect("Khách không nhận hàng hoàn thay kho", s, r, 403)
    s, r = call("POST", f"/api/delivery-requests/{d1}/receive-return", "staff", {"note": "Khách vắng nhà 3 lần"})
    check("Kho nhận lại hàng hoàn", s == 200 and r["data"]["status"] == "DELIVERY_RETURNED", r)
    check("Kiện hoàn về khu nhận", parcel_status(order_id, pc)[0] == "RECEIVED_AT_DESTINATION", parcel_status(order_id, pc))
    t = tracking(order_id)
    check("Khách thấy giao hàng chưa thành công", any(e["title"] == "Giao hàng chưa thành công" for e in t["events"]), t["events"])

    s, r = call("POST", "/api/delivery-requests", "sale", {"orderId": order_id, "parcelIds": [pc], "redeliveryFee": 30000, **address})
    if not expect("Sale lập yêu cầu giao lại có phí", s, r, 201):
        return 1
    d2 = r["data"]
    check("Yêu cầu giao lại trỏ về yêu cầu cũ và có link thu phí",
          d2["sourceDeliveryRequestId"] and d2["sourceDeliveryRequestId"].lower() == d1.lower()
          and d2["redeliveryFeePaymentStatus"] == "PENDING" and d2["redeliveryFeeCheckoutUrl"], d2)
    call("PUT", f"/api/delivery-requests/{d2['deliveryRequestId']}/status", "om1", {"status": "DELIVERY_APPROVED"})
    s, r = call("POST", f"/api/delivery-requests/{d2['deliveryRequestId']}/dispatch", "staff", {})
    check("Chưa trả phí giao lại thì chưa đặt giao", s == 400 and "phí giao lại" in r.get("message", ""), r)
    s, r = call("PUT", f"/api/admin/finance/transactions/{d2['redeliveryFeePaymentId']}/approve", "admin",
                {"transactionCode": f"FT-GL-{RUN}", "receivedAmount": 30000})
    expect("Admin xác nhận phí giao lại", s, r, 200)
    simulate_dispatch(d2["deliveryRequestId"])
    s, r = call("POST", f"/api/delivery-requests/{d2['deliveryRequestId']}/proof", "sale", {"receivedBy": "Nguyễn Test"})
    check("Ghi bằng chứng giao thiếu ảnh bị chặn", s == 400 and "DELIVERY_PROOF" in r.get("message", ""), r)
    upload("sale", "DELIVERY_REQUEST", d2["deliveryRequestId"], "DELIVERY_PROOF", "ky-nhan.pdf")
    s, r = call("POST", f"/api/delivery-requests/{d2['deliveryRequestId']}/proof", "staff", {"receivedBy": "Nguyễn Test"})
    expect("Kho không ghi bằng chứng giao thay Sale", s, r, 403)
    s, r = call("POST", f"/api/delivery-requests/{d2['deliveryRequestId']}/proof", "sale", {"receivedBy": "Nguyễn Test"})
    check("Sale ghi bằng chứng giao", s == 200 and r["data"]["proofReceivedBy"] == "Nguyễn Test", r)
    check("Kiện C đã giao", parcel_status(order_id, pc)[0] == "DELIVERED", parcel_status(order_id, pc))

    # ---------------- ⑤a Lưu kho kiện A rồi giao
    s, r = call("PUT", f"/api/orders/consignments/{order_id}/destination-handling", "customer",
                {"items": [{"parcelId": pc, "handling": "STORE_AT_VN"}]})
    check("Kiện đã giao thì không đổi hướng được", s == 400, r)
    call("PUT", f"/api/orders/consignments/{order_id}/notify-warehouse", "sale", {"note": "Khách gửi kho kiện A"})
    s, r = call("POST", "/api/warehouse-inbound-requests", "staff", {"orderId": order_id, "warehouseId": VN_WAREHOUSE})
    if not expect("Lập phiếu nhập kho VN cho kiện gửi kho", s, r, 201 if s == 201 else 200):
        return 1
    inbound = r.get("data", r)
    inbound_id = inbound.get("inboundRequestId") or inbound.get("id")
    s, r = call("PUT", f"/api/warehouse-inbound-requests/{inbound_id}/status", "om1", {"status": "INBOUND_APPROVED"})
    expect("Quản lý kho duyệt nhập kho VN", s, r, 200)
    s, ids = pending_putaway_ids("admin", order_code)
    check("Duyệt nhập kho VN tự xếp kệ → kiện A không còn chờ xếp kệ ở đâu", s == 200 and pa.lower() not in ids, ids)
    check("Khách thấy kiện A đang lưu kho Việt Nam", parcel_status(order_id, pa)[1] == "STORED_AT_VN", parcel_status(order_id, pa))

    s, r = call("POST", "/api/delivery-requests", "sale", {"orderId": order_id, "parcelIds": [pa], **address})
    expect("Sale lập yêu cầu giao kiện đã lưu kho", s, r, 201)
    d3 = r["data"]["deliveryRequestId"]
    call("PUT", f"/api/delivery-requests/{d3}/status", "om1", {"status": "DELIVERY_APPROVED"})
    simulate_dispatch(d3)
    upload("sale", "DELIVERY_REQUEST", d3, "DELIVERY_PROOF", "ky-nhan-2.pdf")
    s, r = call("POST", f"/api/delivery-requests/{d3}/proof", "sale", {"receivedBy": "Nguyễn Test"})
    expect("Giao xong kiện A", s, r, 200)

    s, r = call("GET", f"/api/orders/consignments/{order_id}", "customer")
    order_status = (r.get("data") or r).get("status") if isinstance(r, dict) else None
    check("Mọi kiện còn hiệu lực đã giao thì đơn DELIVERED (kiện huỷ không giữ đơn lại)", order_status == "DELIVERED", order_status)
    t = tracking(order_id)
    check("Khách thấy chặng đã giao hàng", t.get("currentStage") == "DELIVERED", t.get("currentStage"))

    # ---------------- ⑧ Khiếu nại và đóng đơn
    s, r = call("POST", f"/api/orders/consignments/{order_id}/complaints", "customer", {"description": "Kiện A thiếu 1 món"})
    if not check("Khách khiếu nại sau khi nhận", s == 201 and len(r["data"]) == 2, r):
        return 1
    complaint = next(c for c in r["data"] if c["parcelId"].lower() == pa.lower())
    others = [c for c in r["data"] if c["id"] != complaint["id"]]
    s, r = call("PUT", f"/api/orders/consignments/{order_id}/customer-confirm", "customer", {})
    check("Còn khiếu nại mở thì khách xác nhận chưa đóng được đơn",
          s == 200 and (r.get("data") or r).get("status") == "CUSTOMER_CONFIRMED", r)
    s, r = call("PUT", f"/api/orders/consignments/{order_id}/complete", "om1", {"note": "thử"})
    check("OM cũng không đóng được khi còn khiếu nại", s == 400 and "khiếu nại" in r.get("message", ""), r)

    s, up = upload("customer", "INCIDENT", complaint["id"], "INCIDENT_PHOTO", "anh-thieu-hang.pdf")
    expect("Khách gửi ảnh cho khiếu nại của mình", s, up, 200)
    s, up = upload("customer", "INCIDENT", complaint["id"], "COMPENSATION_RECEIPT", "gia.pdf")
    expect("Khách không up chứng từ chi bồi thường", s, up, 403)
    s, r = call("POST", f"/api/parcel-incidents/{complaint['id']}/customer-response", "customer", {"choice": "DISPOSE"})
    expect("Hàng đã giao thì không chọn huỷ", s, r, 400)
    s, r = call("POST", f"/api/parcel-incidents/{complaint['id']}/customer-response", "customer", {"choice": "COMPENSATE"})
    expect("Khách chọn bồi thường", s, r, 200)
    s, r = call("POST", f"/api/parcel-incidents/{complaint['id']}/resolve", "om1", {"resolution": "COMPENSATE", "compensationAmount": 50000})
    expect("Quản lý kho duyệt bồi thường", s, r, 200)
    for other in others:
        upload("customer", "INCIDENT", other["id"], "INCIDENT_PHOTO", "anh.pdf")
        call("POST", f"/api/parcel-incidents/{other['id']}/resolve", "om1", {"resolution": "ACCEPT", "note": "Kiện C không có vấn đề"})

    s, r = call("PUT", f"/api/orders/consignments/{order_id}/complete", "om1", {"note": "thử"})
    check("Chưa chi bồi thường thì chưa đóng đơn", s == 400 and "bồi thường" in r.get("message", ""), r)
    s, r = call("POST", f"/api/parcel-incidents/{complaint['id']}/compensation-paid", "admin", {"reference": f"CK-{RUN}"})
    check("Chi bồi thường thiếu chứng từ bị chặn", s == 400 and "COMPENSATION_RECEIPT" in r.get("message", ""), r)
    upload("admin", "INCIDENT", complaint["id"], "COMPENSATION_RECEIPT", "uy-nhiem-chi.pdf")
    s, r = call("POST", f"/api/parcel-incidents/{complaint['id']}/compensation-paid", "om1", {"reference": f"CK-{RUN}"})
    expect("Chỉ Admin ghi nhận chi bồi thường", s, r, 403)
    s, r = call("POST", f"/api/parcel-incidents/{complaint['id']}/compensation-paid", "admin", {"reference": f"CK-{RUN}"})
    expect("Admin ghi nhận đã chi bồi thường", s, r, 200)

    s, r = call("PUT", f"/api/orders/consignments/{order_id}/complete", "om1", {"note": "Đã xử lý xong khiếu nại"})
    check("Hết vướng mắc thì đóng đơn", s == 200 and (r.get("data") or r).get("status") == "COMPLETED", r)

    s, r = call("GET", "/api/orders/consignments/tracking?pageSize=100", "customer")
    check("Đơn hoàn thành rời danh sách đang theo dõi",
          all(x["orderId"].lower() != order_id.lower() for x in r["data"]["items"]), "vẫn còn")
    s, r = call("GET", "/api/orders/consignments/tracking?includeFinished=true&pageSize=100", "customer")
    check("includeFinished=true vẫn xem lại được", any(x["orderId"].lower() == order_id.lower() for x in r["data"]["items"]), "thiếu")

    print(f"\n{len(PASSED)} PASS / {len(FAILED)} FAIL")
    if FAILED:
        print("Lỗi: " + ", ".join(FAILED))
    return 1 if FAILED else 0


if __name__ == "__main__":
    sys.exit(main() or 0)
