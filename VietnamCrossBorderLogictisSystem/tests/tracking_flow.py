#!/usr/bin/env python3
"""
Kiểm thử theo dõi hành trình: khách xem tiến độ đơn của mình và nhận thông báo từng mốc; Sale có
hàng đợi lô cần cập nhật, dòng thời gian lô, bước tiếp theo và giấy tờ còn thiếu.

    APP_ENV=/root/vcl-migration-deploy/app.env API=http://127.0.0.1:8088 python3 tracking_flow.py
"""
import json, sys

from wave1_consignment_flow import PASSED, FAILED, RUN, call, check, expect
from warehouse_zones_flow import received_parcels
from export_flow import KR, route_for_kr, set_region, store, upload


def tracking(order_id, who="customer"):
    return call("GET", f"/api/orders/consignments/{order_id}/tracking", who)


def notifications_text(who="customer"):
    s, r = call("GET", "/api/notifications?pageNumber=1&pageSize=50", who)
    return json.dumps(r, ensure_ascii=False)


def milestone(shipment_id, body, who="sale"):
    return call("PUT", f"/api/international-shipments/{shipment_id}/status", who, body)


def main():
    call("PUT", f"/api/warehouses/{KR}/manager", "admin", {"managerId": None, "reason": "Kiểm thử hành trình"})
    set_region("staff", "KR")
    route_id = route_for_kr()

    s, order_id, parcels = received_parcels(["Theo doi A"], f"T{RUN}")
    if not check("Chuẩn bị đơn có 1 kiện", s in (200, 201) and len(parcels) == 1, parcels):
        return 1
    pid = parcels["Theo doi A"]
    store(pid, "BIN-A1-01")

    # ---------------- Khách: hàng đang ở kho nguồn
    s, r = tracking(order_id)
    d = r.get("data", {})
    check("Khách xem hành trình đơn của mình", s == 200, r)
    check("Hàng đang lưu kho nguồn", d.get("currentStage") == "AT_ORIGIN_WAREHOUSE"
          and "Kho Seoul" in (d.get("currentStageText") or ""), d.get("currentStageText"))
    check("Dòng thời gian có mốc kho đã nhận hàng", any(e["title"] == "Kho đã nhận hàng" for e in d.get("events", [])), d.get("events"))
    s, r = tracking(order_id, "customer2")
    expect("Khách khác không xem được đơn", s, r, 403)
    s, r = tracking(order_id, "sale")
    expect("Sale xem được hành trình đơn", s, r, 200)

    # ---------------- Lập phiếu → chuẩn bị xuất kho
    s, r = call("POST", "/api/warehouse-release-requests", "staff", {"shippingRouteId": route_id, "parcelIds": [pid]})
    wro_id = r["data"]["id"]
    call("POST", f"/api/warehouse-release-requests/{wro_id}/submit", "staff")
    # Duyệt phiếu xuất bắt buộc có ảnh hiện trạng lô tại thời điểm duyệt.
    upload("om1", "WRO", wro_id, "WRO_APPROVAL_PROOF", "anh-lo-hang.png")
    call("POST", f"/api/warehouse-release-requests/{wro_id}/decide", "om1", {"decision": "APPROVED", "reason": "OK"})
    s, r = tracking(order_id)
    check("Phiếu đã duyệt thì khách thấy đang chuẩn bị xuất kho",
          r["data"]["currentStage"] == "PREPARING_EXPORT"
          and any(e["stage"] == "PREPARING_EXPORT" for e in r["data"]["events"]), r["data"]["currentStage"])
    check("Khách không thấy mã phiếu nội bộ", "WRO-" not in json.dumps(r, ensure_ascii=False), "lộ mã WRO")

    call("POST", f"/api/warehouse-release-requests/{wro_id}/pick", "staff", {"parcelId": pid, "stagingBinCode": "BIN-OUT-01"})
    s, r = call("POST", "/api/international-shipments", "staff", {"wroRequestIds": [wro_id], "shippingMethod": "AIR"})
    shipment_id = r["data"]["shipmentId"]
    s, r = tracking(order_id)
    check("Lô còn nháp thì khách vẫn thấy đang chuẩn bị xuất kho", r["data"]["currentStage"] == "PREPARING_EXPORT", r["data"]["currentStage"])

    # ---------------- Bàn giao
    s, carriers = call("GET", "/api/carriers/active", "staff")
    carrier_id = (carriers if isinstance(carriers, list) else carriers.get("data", []))[0]["id"]
    upload("staff", "SHIPMENT", shipment_id, "HANDOVER_RECORD")
    upload("staff", "SHIPMENT", shipment_id, "WAYBILL")
    waybill = f"WB-T-{RUN}"
    s, r = call("POST", f"/api/international-shipments/{shipment_id}/handover", "staff",
                {"carrierId": carrier_id, "recipientName": "Anh Park", "waybillCode": waybill, "handoverLocation": "Kho Seoul"})
    if not expect("Bàn giao lô", s, r, 200):
        return 1

    s, r = tracking(order_id)
    d = r["data"]
    check("Khách thấy hàng đã xuất kho", d["currentStage"] == "HANDED_OVER" and d["currentStageText"] == "Hàng đã xuất kho", d["currentStageText"])
    sh = (d.get("shipments") or [{}])[0]
    check("Khách có mã vận đơn và hãng để tự tra", sh.get("carrierTrackingCode") == waybill and sh.get("carrierName"), sh)
    check("Có ngày dự kiến về", bool(d.get("estimatedArrivalDate")), d.get("estimatedArrivalDate"))
    check("Khách nhận thông báo hàng đã xuất kho", "Hàng đã xuất kho" in notifications_text(), "thiếu thông báo")

    # ---------------- Sale: hàng đợi + dòng thời gian
    s, r = call("GET", "/api/international-shipments/tracking-queue", "sale")
    row = next((x for x in r.get("data", []) if x["shipmentId"] == shipment_id), None)
    check("Lô vừa bàn giao có trong hàng đợi của Sale", s == 200 and row is not None, r)
    if row:
        check("Hàng đợi cho biết bước tiếp theo", [m["status"] for m in row["nextMilestones"]] == ["DEPARTED", "IN_TRANSIT"], row["nextMilestones"])
        check("Hàng đợi có số đơn và số kiện", row["orderCount"] == 1 and row["parcelCount"] == 1, row)

    internal_note = f"Noi bo {RUN}: da goi hang xac nhan chuyen bay"
    s, r = milestone(shipment_id, {"status": "DEPARTED", "note": internal_note,
                                   "customerMessage": "Hàng đã lên chuyến bay KE-123, dự kiến hạ cánh sáng mai.",
                                   "location": "Sân bay Incheon"})
    expect("Sale ghi mốc khởi hành kèm lời nhắn cho khách", s, r, 200)

    s, r = tracking(order_id)
    raw = json.dumps(r, ensure_ascii=False)
    last = r["data"]["events"][-1]
    check("Khách thấy lời nhắn Sale viết cho mình",
          last["stage"] == "DEPARTED" and last["message"].startswith("Hàng đã lên chuyến bay KE-123") and last["location"] == "Sân bay Incheon", last)
    check("Khách không thấy ghi chú nội bộ", internal_note not in raw, "lộ ghi chú nội bộ")
    check("Khách nhận thông báo đúng lời nhắn", "KE-123" in notifications_text(), "thiếu thông báo")

    s, r = milestone(shipment_id, {"status": "IN_TRANSIT"})
    expect("Mốc đang vận chuyển (không lời nhắn)", s, r, 200)
    s, r = tracking(order_id)
    check("Không có lời nhắn thì khách nhận câu mặc định",
          "đang trên đường về Việt Nam" in r["data"]["events"][-1]["message"], r["data"]["events"][-1])

    s, r = call("GET", f"/api/international-shipments/{shipment_id}/timeline", "sale")
    t = r.get("data", {})
    check("Dòng thời gian lô cho Sale", s == 200 and len(t.get("events", [])) >= 3, r)
    dep = next((e for e in t.get("events", []) if e["status"] == "DEPARTED"), {})
    check("Sale thấy ghi chú nội bộ, lời nhắn khách và người ghi",
          dep.get("note") == internal_note and dep.get("customerMessage") and dep.get("createdByName") is not None, dep)
    customs = next((m for m in t.get("nextMilestones", []) if m["status"] == "CUSTOMS_CLEARED"), {})
    check("Sale biết thông quan còn thiếu tờ khai nhập", customs.get("missingDocuments") == ["CUSTOMS_IMPORT"], customs)
    check("Dòng thời gian liệt kê đơn bị ảnh hưởng",
          any(o["orderId"].lower() == order_id.lower() for o in t.get("orders", [])), t.get("orders"))

    s, r = milestone(shipment_id, {"status": "DELAYED", "note": "Hãng báo lùi 1 ngày"})
    s, r = call("GET", "/api/international-shipments/tracking-queue?attentionOnly=true", "sale")
    check("Lô trễ nằm trong danh sách cần xử lý", any(x["shipmentId"] == shipment_id for x in r.get("data", [])), r)
    s, r = tracking(order_id)
    check("Khách thấy hàng bị chậm", r["data"]["currentStageText"] == "Hàng bị chậm so với dự kiến", r["data"]["currentStageText"])

    # ---------------- Quyền: khách không xem được lô nội bộ
    for path, name in ((f"/api/international-shipments", "danh sách lô"),
                       (f"/api/international-shipments/{shipment_id}", "chi tiết lô"),
                       (f"/api/international-shipments/{shipment_id}/timeline", "dòng thời gian lô"),
                       ("/api/international-shipments/tracking-queue", "hàng đợi lô"),
                       ("/api/international-shipments/ready-wro-requests", "phiếu chờ gom lô"),
                       ("/api/warehouse-release-requests", "danh sách phiếu xuất kho"),
                       (f"/api/warehouse-release-requests/{wro_id}", "chi tiết phiếu xuất kho"),
                       (f"/api/warehouse-release-requests/{wro_id}/release-note", "PDF phiếu xuất kho"),
                       (f"/api/international-shipments/{shipment_id}/manifest", "manifest lô")):
        s, r = call("GET", path, "customer")
        expect(f"Khách không xem được {name}", s, r, 403)

    # ---------------- Danh sách đơn đang theo dõi
    s, r = call("GET", "/api/orders/consignments/tracking?pageSize=100", "customer")
    mine = next((x for x in r.get("data", {}).get("items", []) if x["orderId"].lower() == order_id.lower()), None)
    check("Khách thấy đơn trong danh sách đang theo dõi", s == 200 and mine is not None and mine["currentStage"] == "DELAYED", mine)
    s, r = call("GET", "/api/orders/consignments/tracking?pageSize=100", "customer2")
    check("Khách khác không thấy đơn này",
          all(x["orderId"].lower() != order_id.lower() for x in r.get("data", {}).get("items", [])), "lộ đơn")
    s, r = call("GET", "/api/orders/consignments/tracking?stage=AT_ORIGIN_WAREHOUSE&pageSize=100", "customer")
    check("Lọc theo chặng", all(x["currentStage"] == "AT_ORIGIN_WAREHOUSE" for x in r.get("data", {}).get("items", [])), r)

    # ---------------- Về tới kho
    milestone(shipment_id, {"status": "IN_TRANSIT", "note": "Đã bay"})
    s, r = milestone(shipment_id, {"status": "ARRIVED_VN", "location": "Sân bay Tân Sơn Nhất"})
    expect("Mốc về Việt Nam", s, r, 200)
    s, r = milestone(shipment_id, {"status": "ARRIVED_DESTINATION", "customerMessage": "Hàng đã về kho HCM, nhân viên sẽ gọi hẹn giao."})
    expect("Mốc về kho đích", s, r, 200)
    s, r = tracking(order_id)
    check("Khách thấy hàng đã về kho Việt Nam", r["data"]["currentStage"] == "ARRIVED_DESTINATION", r["data"]["currentStage"])
    check("Khách nhận thông báo về kho kèm lời nhắn", "nhân viên sẽ gọi hẹn giao" in notifications_text(), "thiếu thông báo")
    # Về tới kho VN chưa phải là xong: còn kiểm hàng, tất toán, giao nội địa — đơn vẫn nằm trong
    # danh sách theo dõi tới khi giao xong (xem vn_arrival_flow.py).
    s, r = call("GET", "/api/orders/consignments/tracking?pageSize=100", "customer")
    check("Đơn về kho VN nhưng chưa giao vẫn nằm trong danh sách theo dõi",
          any(x["orderId"].lower() == order_id.lower() for x in r["data"]["items"]), "thiếu")
    s, r = call("GET", "/api/international-shipments/tracking-queue", "sale")
    check("Lô đã về kho rời khỏi hàng đợi của Sale", all(x["shipmentId"] != shipment_id for x in r.get("data", [])), "vẫn còn")

    # ------------------------------------------------------------------
    # LỘ TRÌNH + TIỀN trên màn theo dõi (yêu cầu: hiện lộ trình và giá của đơn)
    # ------------------------------------------------------------------
    s, r = tracking(order_id)
    d = r["data"]

    route = d.get("route")
    if check("Màn theo dõi trả lộ trình của đơn", isinstance(route, dict), route):
        stops = route.get("stops") or []
        keys = [x.get("key") for x in stops]
        check("Lộ trình đủ 5 chặng từ kho nguồn tới nhà khách",
              keys == ["ORIGIN", "EXPORTED", "TRANSIT", "VN_WAREHOUSE", "DELIVERY"], keys)
        check("Mỗi chặng đều có tên đọc được", all(x.get("title") for x in stops), stops)
        check("Hàng đã về kho VN thì 4 chặng đầu đều đã qua",
              [bool(x.get("reached")) for x in stops] == [True, True, True, True, False],
              [(x.get("key"), x.get("reached")) for x in stops])
        # Chặng chưa tới thì KHÔNG được có thời gian — đoán thời gian là bịa dữ liệu.
        check("Chặng chưa tới không có mốc thời gian",
              all(not x.get("reachedAt") for x in stops if not x.get("reached")),
              [(x.get("key"), x.get("reachedAt")) for x in stops if not x.get("reached")])
        check("Lộ trình nói rõ đi bằng đường gì", bool(route.get("transportModeText")), route)
        check("Chặng vận chuyển quốc tế mang tên tuyến hoặc phương thức",
              bool(stops[2].get("subtitle")), stops[2])

    money = d.get("money")
    if check("Màn theo dõi trả tiền của đơn", isinstance(money, dict), money):
        total = float(money.get("totalEstimatedCost") or 0)
        paid = float(money.get("paidAmount") or 0)
        check("Tổng tiền của đơn lớn hơn 0", total > 0, money)
        check("Tổng = cước + phí dịch vụ + thuế",
              round(float(money.get("estimatedFreightCharge") or 0)
                    + float(money.get("serviceFee") or 0)
                    + float(money.get("taxAndDuty") or 0), 2) == round(total, 2), money)
        check("Còn phải trả = tổng trừ đã trả, không âm",
              round(float(money.get("outstandingAmount") or 0), 2) == round(max(0.0, total - paid), 2), money)
        check("Trạng thái thanh toán khớp số tiền",
              money.get("paymentStatus") == ("UNPAID" if paid <= 0 else "PAID" if paid + 0.01 >= total else "PARTIALLY_PAID"),
              money)

    # Đơn của người khác vẫn không xem được tiền lẫn lộ trình.
    s, r = tracking(order_id, "customer2")
    check("Khách khác không đọc được lộ trình và tiền của đơn này", s == 403, s)

    print(f"\n{len(PASSED)} PASS / {len(FAILED)} FAIL")
    if FAILED:
        print("Lỗi: " + ", ".join(FAILED))
    return 1 if FAILED else 0


if __name__ == "__main__":
    sys.exit(main() or 0)
