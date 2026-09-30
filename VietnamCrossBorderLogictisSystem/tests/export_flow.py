#!/usr/bin/env python3
"""
Kiểm thử luồng xuất kho quốc tế trên môi trường thử: lập phiếu → quản lý kho duyệt → bốc hàng
sang khu xuất → lập lô → bàn giao → hành trình về kho VN.

    APP_ENV=/root/vcl-migration-deploy/app.env API=http://127.0.0.1:8088 python3 export_flow.py
"""
import json, os, sys, urllib.request, urllib.error, uuid

from wave1_consignment_flow import API, IDS, TOK, PASSED, FAILED, RUN, call, check, expect
from warehouse_zones_flow import received_parcels

KR = IDS["warehouseKr"]
VN_WAREHOUSE = "DF34760C-0C3E-48AD-BDD6-3A30A4EC402A"   # Kho HCM - Tân Bình


def upload(who, entity_type, entity_id, document_type, name="bien-ban.pdf"):
    """Đính kèm một file PDF nhỏ (multipart) — giấy tờ của luồng xuất đều đi qua API này."""
    boundary = "----vcl" + uuid.uuid4().hex
    content = b"%PDF-1.4\n% test\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF\n"
    parts = []
    for key, value in (("entityType", entity_type), ("entityId", entity_id),
                       ("documentType", document_type), ("note", "kiểm thử")):
        parts.append(f"--{boundary}\r\nContent-Disposition: form-data; name=\"{key}\"\r\n\r\n{value}\r\n".encode())
    parts.append(f"--{boundary}\r\nContent-Disposition: form-data; name=\"file\"; filename=\"{name}\"\r\n"
                 f"Content-Type: application/pdf\r\n\r\n".encode() + content + b"\r\n")
    parts.append(f"--{boundary}--\r\n".encode())
    body = b"".join(parts)

    req = urllib.request.Request(API + "/api/attachments", data=body, method="POST")
    req.add_header("Content-Type", f"multipart/form-data; boundary={boundary}")
    req.add_header("Authorization", "Bearer " + TOK[who])
    try:
        with urllib.request.urlopen(req, timeout=60) as r:
            return r.status, json.loads(r.read().decode() or "{}")
    except urllib.error.HTTPError as e:
        raw = e.read().decode() or "{}"
        try:
            return e.code, json.loads(raw)
        except json.JSONDecodeError:
            return e.code, {"raw": raw}


def set_region(who, region):
    s, profile = call("GET", "/api/User/profile", who)
    body = {"fullName": (profile or {}).get("fullName") or "Test Warehouse Staff",
            "phone": (profile or {}).get("phone") or "0900000000", "region": region}
    return call("PUT", "/api/User/profile", who, body)


def store(parcel_id, bin_code, who="staff", warehouse_id=None):
    """
    Xếp kiện vào ô kệ. Hệ thống BẮT BUỘC có ảnh kiện trong ô trước khi ghi nhận, nên hàm
    này chụp ảnh rồi mới gọi — đúng thứ tự nhân viên kho làm ngoài thực tế.
    """
    upload(who, "PARCEL", parcel_id, "PUT_AWAY_PROOF", "anh-o-ke.png")
    return call("PUT", f"/api/parcels/{parcel_id}/put-away", who,
                {"warehouseId": warehouse_id or KR, "binCode": bin_code})


def route_for_kr():
    """Tuyến Seoul → HCM; tạo nếu môi trường thử chưa khai."""
    s, r = call("GET", "/api/shipping-routes", "admin")
    items = r.get("data", r) if isinstance(r, dict) else r
    if isinstance(items, dict):
        items = items.get("items", [])
    for x in items or []:
        if str(x.get("originWarehouseId", "")).lower() == KR.lower() and x.get("isActive", True):
            return x.get("id") or x.get("shippingRouteId")

    s, r = call("POST", "/api/shipping-routes", "admin", {
        "routeCode": f"KR_VN_AIR_{RUN}", "routeName": "Seoul → HCM (kiểm thử)",
        "originCountry": "KR", "destinationCountry": "VN",
        "originWarehouseId": KR, "destinationWarehouseId": VN_WAREHOUSE,
        "transportMode": "AIR", "estimatedTransitDays": 5, "isActive": True})
    data = r.get("data", r)
    return data.get("id") or data.get("shippingRouteId")


def eligible(route_id, include_blocked=True):
    s, r = call("GET", f"/api/warehouse-release-requests/eligible-parcels?shippingRouteId={route_id}&includeBlocked={str(include_blocked).lower()}", "staff")
    return s, (r.get("data") if isinstance(r, dict) else None) or []


def reasons_of(rows, parcel_id):
    row = next((x for x in rows if str(x["parcelId"]).lower() == parcel_id.lower()), None)
    return [c["code"] for c in (row or {}).get("reasons", [])] if row else None


def main():
    call("PUT", f"/api/warehouses/{KR}/manager", "admin", {"managerId": None, "reason": "Kiểm thử luồng xuất"})
    set_region("staff", "KR")

    route_id = route_for_kr()
    if not check("Có tuyến xuất từ kho Seoul", bool(route_id), route_id):
        return

    # ---------------- Chuẩn bị: 4 kiện đã xếp kệ ở khu lưu kho
    s, order_id, parcels = received_parcels(["Xuat A", "Xuat B", "Xuat C", "Xuat D"], f"X{RUN}")
    if not check("Check-in 4 kiện cho đơn kiểm thử", s in (200, 201) and len(parcels) == 4, parcels):
        return
    p1, p2, p3, p4 = (parcels["Xuat A"], parcels["Xuat B"], parcels["Xuat C"], parcels["Xuat D"])
    for pid, code in ((p1, "BIN-A1-01"), (p2, "BIN-A1-02"), (p3, "BIN-A2-01")):
        s, r = store(pid, code)
        if not check(f"Xếp kệ kiện {code}", s == 200, r):
            return

    # ---------------- Kiện đủ điều kiện
    s, rows = eligible(route_id)
    expect("Danh sách kiện đủ điều kiện xuất", s, {"n": len(rows)}, 200)
    ready = [x for x in rows if x["isEligible"]]
    check("Ba kiện vừa xếp kệ đủ điều kiện", all(reasons_of(rows, p) == [] for p in (p1, p2, p3)),
          {p: reasons_of(rows, p) for p in (p1, p2, p3)})
    check("Kiện chưa xếp kệ bị chặn đúng lý do",
          reasons_of(rows, p4) is None or "NOT_IN_STORAGE_ZONE" in (reasons_of(rows, p4) or []),
          reasons_of(rows, p4))

    # ---------------- Giữ hàng
    s, r = call("PUT", f"/api/orders/consignments/{order_id}/export-hold", "customer", {"hold": True})
    expect("Bật giữ hàng thiếu lý do bị chặn", s, r, 400)
    s, r = call("PUT", f"/api/orders/consignments/{order_id}/export-hold", "customer",
                {"hold": True, "reason": "Khách muốn gộp chuyến sau"})
    expect("Khách bật giữ hàng", s, r, 200)
    s, rows = eligible(route_id)
    check("Đơn giữ hàng thì kiện bị chặn EXPORT_HOLD", "EXPORT_HOLD" in (reasons_of(rows, p1) or []), reasons_of(rows, p1))
    s, r = call("PUT", f"/api/orders/consignments/{order_id}/export-hold", "customer", {"hold": False})
    expect("Khách tắt giữ hàng", s, r, 200)

    # ---------------- Lập phiếu + phạm vi vùng
    set_region("staff", "JP")
    s, r = call("POST", "/api/warehouse-release-requests", "staff", {"shippingRouteId": route_id, "parcelIds": [p1]})
    expect("Nhân viên kho khác vùng không lập phiếu được", s, r, 403)
    set_region("staff", "KR")

    s, r = call("POST", "/api/warehouse-release-requests", "staff",
                {"shippingRouteId": route_id, "parcelIds": [p1, p2, p3], "note": "Chuyến kiểm thử"})
    if not expect("Lập phiếu xuất kho", s, r, 201):
        return
    wro = r["data"]
    wro_id = wro["id"]
    check("Phiếu mới ở trạng thái nháp", wro["status"] == "DRAFT" and wro["parcelCount"] == 3, wro)

    s, r = call("POST", "/api/warehouse-release-requests", "staff", {"shippingRouteId": route_id, "parcelIds": [p1]})
    check("Kiện đã nằm trong phiếu khác thì không lập phiếu mới", s == 400 and "phiếu xuất" in r.get("message", "").lower(), r)

    s, r = call("PUT", f"/api/warehouse-release-requests/{wro_id}", "staff",
                {"shippingRouteId": route_id, "parcelIds": [p1, p2]})
    check("Sửa phiếu nháp bớt kiện", s == 200 and r["data"]["parcelCount"] == 2, r)
    s, r = call("PUT", f"/api/warehouse-release-requests/{wro_id}", "staff",
                {"shippingRouteId": route_id, "parcelIds": [p1, p2, p3]})
    check("Thêm lại kiện vào phiếu nháp", s == 200 and r["data"]["parcelCount"] == 3, r)

    # ---------------- Gửi duyệt / rút / duyệt
    s, r = call("POST", f"/api/warehouse-release-requests/{wro_id}/submit", "staff")
    check("Gửi duyệt phiếu", s == 200 and r["data"]["status"] == "PENDING_APPROVAL", r)

    s, rows = eligible(route_id)
    check("Gửi duyệt là khoá hàng", "INVENTORY_NOT_AVAILABLE" in (reasons_of(rows, p1) or []), reasons_of(rows, p1))

    s, r = call("POST", f"/api/warehouse-release-requests/{wro_id}/withdraw", "staff")
    check("Rút phiếu về nháp", s == 200 and r["data"]["status"] == "DRAFT", r)
    s, rows = eligible(route_id)
    # Phiếu rút về nháp vẫn giữ chỗ kiện (ALREADY_IN_EXPORT), nhưng hàng phải được nhả khoá.
    check("Rút phiếu thì nhả hàng", "INVENTORY_NOT_AVAILABLE" not in (reasons_of(rows, p1) or []), reasons_of(rows, p1))

    call("POST", f"/api/warehouse-release-requests/{wro_id}/submit", "staff")
    s, r = call("POST", f"/api/warehouse-release-requests/{wro_id}/decide", "staff", {"decision": "APPROVED"})
    expect("Người gửi duyệt không tự duyệt", s, r, 403)
    s, r = call("POST", f"/api/warehouse-release-requests/{wro_id}/decide", "om1", {"decision": "REJECTED"})
    expect("Từ chối thiếu lý do bị chặn", s, r, 400)

    # Duyệt xong là hàng rời kho, sau đó không chụp lại được nữa — nên phải có ảnh hiện
    # trạng lô TẠI THỜI ĐIỂM duyệt. Từ chối thì không đòi: hàng vẫn nằm trên kệ.
    s, r = call("POST", f"/api/warehouse-release-requests/{wro_id}/decide", "om1",
                {"decision": "APPROVED", "reason": "Đủ điều kiện"})
    check("Duyệt phiếu xuất mà chưa chụp ảnh thì bị chặn",
          s == 400 and "WRO_APPROVAL_PROOF" in r.get("message", ""), r)

    upload("om1", "WRO", wro_id, "WRO_APPROVAL_PROOF", "anh-lo-hang.png")
    s, r = call("POST", f"/api/warehouse-release-requests/{wro_id}/decide", "om1",
                {"decision": "APPROVED", "reason": "Đủ điều kiện"})
    check("Quản lý kho duyệt phiếu", s == 200 and r["data"]["status"] == "APPROVED", r)

    # ---------------- Khoá thao tác kho khi kiện đang trong phiếu
    s, inv = call("GET", f"/api/inventories?warehouseId={KR}", "staff")
    inv1 = next((i for i in inv.get("items", []) if str(i.get("parcelId", "")).lower() == p1.lower()), None)
    if inv1:
        s, r = call("POST", f"/api/inventories/{inv1['inventoryId']}/relocate", "staff",
                    {"toBinCode": "BIN-A2-01", "reason": "thử"})
        check("Không điều chuyển kiện đang trong phiếu xuất", s == 400 and "phiếu xuất" in r.get("message", ""), r)
    s, r = call("POST", f"/api/parcels/{p1}/split", "staff", {"children": [
        {"actualWeight": 0.5, "quantity": 1, "length": 10, "width": 10, "height": 5},
        {"actualWeight": 0.5, "quantity": 1, "length": 10, "width": 10, "height": 5}]})
    check("Không tách kiện đang trong phiếu xuất", s == 400 and "phiếu xuất" in r.get("message", ""), r)

    # ---------------- Bốc hàng sang khu xuất
    s, r = call("POST", f"/api/warehouse-release-requests/{wro_id}/pick", "staff",
                {"parcelId": p4, "stagingBinCode": "BIN-OUT-01"})
    expect("Quét kiện ngoài phiếu bị chặn", s, r, 400)
    s, r = call("POST", f"/api/warehouse-release-requests/{wro_id}/pick", "staff",
                {"parcelId": p1, "stagingBinCode": "BIN-A1-01"})
    # Câu lỗi nói tên khu tiếng Việt, không chèn mã loại khu (OUTBOUND).
    check("Bốc vào ô khu lưu kho bị chặn", s == 400 and "chỉ được xếp vào khu xuất" in r.get("message", ""), r)
    s, r = call("POST", f"/api/warehouse-release-requests/{wro_id}/pick", "staff",
                {"parcelId": p1, "stagingBinCode": "BIN-OUT-01"})
    if not check("Bốc kiện sang ô khu xuất", s == 200 and r["data"]["status"] == "PICKING", r):
        return
    row1 = next(x for x in r["data"]["parcels"] if str(x["parcelId"]).lower() == p1.lower())
    check("Kiện đã bốc ghi đủ ô nguồn và ô khu xuất",
          row1["status"] == "PICKED" and row1["stagingBinCode"] == "BIN-OUT-01" and row1["sourceBinCode"] == "BIN-A1-01", row1)

    s, r = call("POST", f"/api/warehouse-release-requests/{wro_id}/pick", "staff",
                {"parcelId": p1, "stagingBinCode": "BIN-OUT-01"})
    expect("Bốc lại kiện đã bốc bị chặn", s, r, 400)

    s, r = call("GET", f"/api/warehouses/{KR}/misplaced-parcels", "staff")
    codes = [str(x["parcelId"]).lower() for x in r.get("data", [])]
    check("Kiện ở khu xuất theo phiếu không bị báo sai khu", p1.lower() not in codes, codes[:5])

    # ---------------- Bỏ kiện khỏi phiếu
    s, detail = call("GET", f"/api/warehouse-release-requests/{wro_id}", "staff")
    row3 = next(x for x in detail["data"]["parcels"] if str(x["parcelId"]).lower() == p3.lower())
    s, r = call("POST", f"/api/warehouse-release-requests/{wro_id}/parcels/{p3}/remove", "staff", {"reason": "Kiện hỏng"})
    check("Bỏ kiện thiếu ảnh bị chặn", s == 400 and "PICKING_ISSUE" in r.get("message", ""), r)
    s, up = upload("staff", "WRO_PARCEL", row3["id"], "PICKING_ISSUE", "anh-kien-hong.pdf")
    expect("Đính kèm ảnh sự cố cho kiện", s, up, 200)
    s, r = call("POST", f"/api/warehouse-release-requests/{wro_id}/parcels/{p3}/remove", "staff", {"reason": "Kiện hỏng"})
    check("Bỏ kiện chưa bốc khỏi phiếu", s == 200 and r["data"]["parcelCount"] == 2, r)
    s, rows = eligible(route_id)
    check("Kiện bị bỏ khỏi phiếu về lại hàng sẵn sàng", reasons_of(rows, p3) == [], reasons_of(rows, p3))

    # ---------------- Phiếu bị từ chối thì kiện phải dùng lại được
    s, r = call("POST", "/api/warehouse-release-requests", "staff", {"shippingRouteId": route_id, "parcelIds": [p3]})
    if check("Lập phiếu thử cho kiện vừa bị bỏ", s == 201, r):
        bad_id = r["data"]["id"]
        call("POST", f"/api/warehouse-release-requests/{bad_id}/submit", "staff")
        s, r = call("POST", f"/api/warehouse-release-requests/{bad_id}/decide", "om1",
                    {"decision": "REJECTED", "reason": "Kiện chưa đúng tuyến"})
        check("Quản lý kho từ chối phiếu", s == 200 and r["data"]["status"] == "REJECTED", r)
        s, r = call("POST", "/api/warehouse-release-requests", "staff", {"shippingRouteId": route_id, "parcelIds": [p3]})
        if check("Kiện của phiếu bị từ chối lập lại phiếu mới được", s == 201, r):
            call("POST", f"/api/warehouse-release-requests/{r['data']['id']}/cancel", "staff")

    # ---------------- Bốc nốt → READY
    s, r = call("POST", f"/api/warehouse-release-requests/{wro_id}/pick", "staff",
                {"packageCode": None, "parcelId": p2, "stagingBinCode": "BIN-OUT-02"})
    check("Bốc đủ kiện thì phiếu sẵn sàng đi", s == 200 and r["data"]["status"] == "READY", r)

    # ---------------- Lô vận chuyển
    s, r = call("GET", f"/api/international-shipments/ready-wro-requests?originWarehouseId={KR}", "staff")
    ready_codes = [x["wroCode"] for x in r.get("data", [])]
    check("Phiếu đã bốc xong hiện ở danh sách chờ lập lô", wro["wroCode"] in ready_codes, ready_codes[:5])

    s, r = call("POST", "/api/international-shipments", "staff",
                {"wroRequestIds": [wro_id], "shippingMethod": "AIR"})
    if not expect("Lập lô vận chuyển", s, r, 201):
        return
    shipment = r["data"]
    shipment_id = shipment["shipmentId"] if "shipmentId" in shipment else shipment["id"]
    check("Lô mới ở trạng thái nháp", shipment.get("status") == "DRAFT", shipment)

    s, r = call("GET", f"/api/warehouse-release-requests/{wro_id}", "staff")
    check("Phiếu vào lô chuyển sang IN_SHIPMENT", r["data"]["status"] == "IN_SHIPMENT", r["data"]["status"])

    s, r = call("POST", f"/api/international-shipments/{shipment_id}/handover", "staff",
                {"carrierId": "00000000-0000-0000-0000-000000000000", "recipientName": "Anh Kim", "waybillCode": "WB-TEST"})
    expect("Bàn giao thiếu hãng vận chuyển bị chặn", s, r, 404)

    s, carriers = call("GET", "/api/carriers/active", "staff")
    carrier_list = carriers if isinstance(carriers, list) else carriers.get("data", [])
    check("Kho đọc được danh mục hãng vận chuyển để chọn lúc bàn giao", s == 200 and len(carrier_list) > 0, carriers)
    carrier_id = (carrier_list or [{}])[0].get("id") or (carrier_list or [{}])[0].get("carrierId")

    s, r = call("POST", f"/api/international-shipments/{shipment_id}/handover", "staff",
                {"carrierId": carrier_id, "recipientName": "Anh Kim", "waybillCode": f"WB-{RUN}"})
    check("Bàn giao thiếu biên bản và vận đơn bị chặn", s == 400 and "HANDOVER_RECORD" in r.get("message", ""), r)

    upload("staff", "SHIPMENT", shipment_id, "HANDOVER_RECORD")
    upload("staff", "SHIPMENT", shipment_id, "WAYBILL")

    s, r = call("POST", f"/api/international-shipments/{shipment_id}/handover", "staff",
                {"carrierId": carrier_id, "recipientName": "Anh Kim", "recipientPhone": "010-1234",
                 "waybillCode": f"WB-{RUN}", "vehicleNumber": "51C-123.45", "sealNumber": "SEAL-1",
                 "handoverLocation": "Kho Seoul"})
    if not check("Bàn giao lô cho hãng vận chuyển", s == 200, r):
        return

    s, r = call("GET", f"/api/warehouse-release-requests/{wro_id}", "staff")
    picked_all = all(x["status"] in ("HANDED_OVER", "REMOVED") for x in r["data"]["parcels"])
    check("Bàn giao xong phiếu và kiện đều HANDED_OVER", r["data"]["status"] == "HANDED_OVER" and picked_all, r["data"]["status"])

    s, inv = call("GET", f"/api/inventories?warehouseId={KR}", "staff")
    still_there = [i for i in inv.get("items", []) if str(i.get("parcelId", "")).lower() in (p1.lower(), p2.lower())
                   and i.get("status") not in ("RELEASED",)]
    check("Hàng đã xuất không còn nằm trong tồn kho khả dụng", not still_there, still_there[:3])

    # ---------------- Hành trình
    s, r = call("PUT", f"/api/international-shipments/{shipment_id}/status", "sale", {"status": "ARRIVED_VN"})
    expect("Không nhảy cóc mốc hành trình", s, r, 400)
    s, r = call("PUT", f"/api/international-shipments/{shipment_id}/status", "staff", {"status": "DEPARTED"})
    expect("Kho không cập nhật hành trình quốc tế", s, r, 403)
    s, r = call("PUT", f"/api/international-shipments/{shipment_id}/status", "sale", {"status": "DEPARTED"})
    expect("Sale ghi mốc rời kho", s, r, 200)
    s, r = call("PUT", f"/api/international-shipments/{shipment_id}/status", "sale", {"status": "IN_TRANSIT"})
    expect("Mốc đang trên đường", s, r, 200)
    s, r = call("PUT", f"/api/international-shipments/{shipment_id}/status", "sale", {"status": "DELAYED"})
    expect("Mốc trễ thiếu ghi chú bị chặn", s, r, 400)
    s, r = call("PUT", f"/api/international-shipments/{shipment_id}/status", "sale",
                {"status": "DELAYED", "note": "Kẹt sân bay"})
    expect("Mốc trễ có ghi chú", s, r, 200)
    s, r = call("PUT", f"/api/international-shipments/{shipment_id}/status", "sale", {"status": "CUSTOMS_CLEARED"})
    check("Thông quan thiếu tờ khai nhập bị chặn", s == 400 and "CUSTOMS_IMPORT" in r.get("message", ""), r)
    upload("sale", "SHIPMENT", shipment_id, "CUSTOMS_IMPORT", "to-khai.pdf")
    s, r = call("PUT", f"/api/international-shipments/{shipment_id}/status", "sale", {"status": "CUSTOMS_CLEARED"})
    expect("Ghi mốc thông quan sau khi có tờ khai", s, r, 200)
    s, r = call("PUT", f"/api/international-shipments/{shipment_id}/status", "sale", {"status": "ARRIVED_VN"})
    expect("Mốc hàng về Việt Nam", s, r, 200)
    s, r = call("PUT", f"/api/international-shipments/{shipment_id}/status", "sale", {"status": "ARRIVED_DESTINATION"})
    expect("Mốc về tới kho VN", s, r, 200)

    # ---------------- Giấy tờ: quyền xem
    s, r = call("GET", f"/api/attachments?entityType=SHIPMENT&entityId={shipment_id}", "customer")
    expect("Khách không xem được chứng từ lô", s, r, 403)
    s, r = call("GET", f"/api/attachments?entityType=SHIPMENT&entityId={shipment_id}", "sale")
    check("Nhân viên xem được chứng từ lô", s == 200 and len(r.get("data", [])) >= 3, r)

    # ---------------- PDF
    for path, name in ((f"/api/warehouse-release-requests/{wro_id}/release-note", "Phiếu xuất kho PDF"),
                       (f"/api/warehouse-release-requests/{wro_id}/picking-sheet", "Danh sách soạn hàng PDF"),
                       (f"/api/international-shipments/{shipment_id}/manifest", "Manifest lô PDF")):
        req = urllib.request.Request(API + path, method="GET")
        req.add_header("Authorization", "Bearer " + TOK["staff"])
        try:
            with urllib.request.urlopen(req, timeout=60) as resp:
                body = resp.read()
            check(name, resp.status == 200 and body[:4] == b"%PDF", f"{resp.status} {body[:20]}")
        except urllib.error.HTTPError as e:
            check(name, False, f"HTTP {e.code}: {e.read()[:200]}")

    # ---------------- Tách phiếu, gỡ phiếu khỏi lô, huỷ lô, chặn bàn giao khi đơn vướng
    s, order2, parcels2 = received_parcels(["Tach A", "Tach B", "Tach C"], f"S{RUN}")
    if check("Check-in 3 kiện cho kịch bản tách phiếu", s in (200, 201) and len(parcels2) == 3, parcels2):
        q1, q2, q3 = parcels2["Tach A"], parcels2["Tach B"], parcels2["Tach C"]
        for pid, code in ((q1, "BIN-A1-01"), (q2, "BIN-A1-02"), (q3, "BIN-A2-01")):
            store(pid, code)

        s, r = call("POST", "/api/warehouse-release-requests", "staff",
                    {"shippingRouteId": route_id, "parcelIds": [q1, q2, q3]})
        wro2_id = r["data"]["id"]
        call("POST", f"/api/warehouse-release-requests/{wro2_id}/submit", "staff")
        upload("om1", "WRO", wro2_id, "WRO_APPROVAL_PROOF", "anh-lo-hang-2.png")
        call("POST", f"/api/warehouse-release-requests/{wro2_id}/decide", "om1", {"decision": "APPROVED", "reason": "OK"})
        for pid, code in ((q1, "BIN-OUT-01"), (q2, "BIN-OUT-01"), (q3, "BIN-OUT-02")):
            call("POST", f"/api/warehouse-release-requests/{wro2_id}/pick", "staff",
                 {"parcelId": pid, "stagingBinCode": code})

        s, r = call("POST", f"/api/warehouse-release-requests/{wro2_id}/split", "staff",
                    {"parcelIds": [q1, q2, q3]})
        expect("Không tách hết kiện sang phiếu con", s, r, 400)
        s, r = call("POST", f"/api/warehouse-release-requests/{wro2_id}/split", "staff", {"parcelIds": [q3]})
        child = r.get("data", {})
        check("Tách phiếu giữ nguyên trạng thái đã duyệt và khoá hàng",
              s == 200 and child.get("status") == "READY" and child.get("parcelCount") == 1
              and child.get("splitFromCode"), child)
        child_id = child.get("id")

        s, r = call("GET", f"/api/warehouse-release-requests/{wro2_id}", "staff")
        check("Phiếu gốc còn lại 2 kiện", r["data"]["parcelCount"] == 2, r["data"]["parcelCount"])

        s, r = call("POST", "/api/international-shipments", "staff",
                    {"wroRequestIds": [wro2_id, child_id], "shippingMethod": "SEA"})
        lot2 = r.get("data", {})
        lot2_id = lot2.get("shipmentId") or lot2.get("id")
        expect("Lập lô gồm hai phiếu", s, r, 201)

        s, r = call("DELETE", f"/api/international-shipments/{lot2_id}/wro-requests/{child_id}", "staff")
        expect("Gỡ phiếu khỏi lô nháp", s, r, 200)
        s, r = call("GET", f"/api/warehouse-release-requests/{child_id}", "staff")
        check("Phiếu gỡ khỏi lô về lại chờ lập lô", r["data"]["status"] == "READY", r["data"]["status"])

        call("PUT", f"/api/orders/consignments/{order2}/export-hold", "customer",
             {"hold": True, "reason": "Khách đổi ý"})
        upload("staff", "SHIPMENT", lot2_id, "HANDOVER_RECORD")
        upload("staff", "SHIPMENT", lot2_id, "WAYBILL")
        s, r = call("POST", f"/api/international-shipments/{lot2_id}/handover", "staff",
                    {"carrierId": carrier_id, "recipientName": "Anh Lee", "waybillCode": f"WB2-{RUN}"})
        check("Đơn bật giữ hàng thì chặn bàn giao và chỉ rõ đơn nào",
              s == 400 and "giữ hàng" in r.get("message", ""), r)
        call("PUT", f"/api/orders/consignments/{order2}/export-hold", "customer", {"hold": False})

        s, r = call("POST", f"/api/international-shipments/{lot2_id}/cancel", "staff")
        expect("Huỷ lô nháp", s, r, 200)
        s, r = call("GET", f"/api/warehouse-release-requests/{wro2_id}", "staff")
        check("Huỷ lô thì phiếu về lại chờ lập lô", r["data"]["status"] == "READY", r["data"]["status"])

    print(f"\n{len(PASSED)} PASS / {len(FAILED)} FAIL")
    if FAILED:
        print("Lỗi: " + ", ".join(FAILED))
    return 1 if FAILED else 0


if __name__ == "__main__":
    sys.exit(main() or 0)
