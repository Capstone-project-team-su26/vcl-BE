#!/usr/bin/env python3
"""
Kiểm thử ẢNH BẰNG CHỨNG khi kho VN tiếp nhận kiện từ lô quốc tế (VN_ARRIVAL_PROOF):

  - mỗi kiện phải có ≥ 1 ảnh (PARCEL + VN_ARRIVAL_PROOF) mới xác nhận tiếp nhận được; câu lỗi nêu
    đích danh kiện thiếu ảnh, kiện hỏng / lệch cân đo ghi rõ "bắt buộc ảnh hiện trạng";
  - chỉ nhận file ảnh, chỉ gắn vào PARCEL (kiện trong lô quốc tế) hoặc SHIPMENT (ảnh toàn lô);
  - ảnh trả kèm: kết quả tiếp nhận, chi tiết lô (từng kiện + toàn lô), biên bản kiểm kiện của OM,
    xem trước tất toán; kiện hỏng tự có ảnh hiện trạng trên sự cố;
  - biên bản cân lại (PUT /api/parcels/{id}/inspection) ghi LỆCH thì phải có ảnh;
  - khách xem được ảnh tiếp nhận kiện của CHÍNH mình (không thấy ảnh ô kệ), khách khác thì không;
  - nhân viên kho đã được gán kho khác không chụp / không xem ảnh tiếp nhận của kho VN.

    APP_ENV=/root/vcl-migration-deploy/app.env API=http://127.0.0.1:8088 python3 vn_arrival_photo_proof_flow.py
"""
import sys
import urllib.error
import urllib.request

from wave1_consignment_flow import API, PASSED, FAILED, RUN, TOK, call, check, expect
from warehouse_zones_flow import received_parcels
from export_flow import KR, VN_WAREHOUSE, set_region, store, upload
from vn_arrival_flow import arrival_photo, ship_to_vietnam
from warehouse_assignment_flow import assign, create_staff


def download(attachment_id, who):
    """Tải file đính kèm; trả (status, content-type)."""
    req = urllib.request.Request(f"{API}/api/attachments/{attachment_id}/download", method="GET")
    req.add_header("Authorization", "Bearer " + TOK[who])
    try:
        with urllib.request.urlopen(req, timeout=60) as resp:
            resp.read()
            return resp.status, resp.headers.get("Content-Type", "")
    except urllib.error.HTTPError as e:
        return e.code, ""


def main():
    call("PUT", f"/api/warehouses/{KR}/manager", "admin", {"managerId": None, "reason": "Kiểm thử ảnh tiếp nhận VN"})
    call("PUT", f"/api/warehouses/{VN_WAREHOUSE}/manager", "admin", {"managerId": None, "reason": "Kiểm thử ảnh tiếp nhận VN"})
    set_region("staff", "KR")

    # ---------------- Chuẩn bị: đơn 4 kiện về tới kho VN
    s, order_id, parcels = received_parcels(["Anh A", "Anh B", "Anh C", "Anh D"], f"P{RUN}")
    if not check("Chuẩn bị đơn 4 kiện", s in (200, 201) and len(parcels) == 4, parcels):
        return 1
    pa, pb, pc, pd = (parcels[k] for k in ("Anh A", "Anh B", "Anh C", "Anh D"))
    for pid, code in ((pa, "BIN-A1-01"), (pb, "BIN-A1-02"), (pc, "BIN-A2-01"), (pd, "BIN-A2-01")):
        store(pid, code)

    s, r = arrival_photo(pa)
    check("Kiện chưa lên lô quốc tế thì chưa chụp ảnh tiếp nhận VN được",
          s == 400 and "lô vận chuyển quốc tế" in r.get("message", ""), r)

    shipment_id = ship_to_vietnam([pa, pb, pc, pd], f"P{RUN}")
    if not check("Lô chở 4 kiện về tới kho VN", shipment_id is not None, shipment_id):
        return 1

    s, r = call("GET", f"/api/international-shipments/{shipment_id}", "staff")
    rows = {p["parcelId"].lower(): p for p in r.get("data", {}).get("parcels", [])}
    codes = {pid: rows.get(pid.lower(), {}).get("packageCode", "") for pid in (pa, pb, pc, pd)}
    check("Chi tiết lô có trường ảnh tiếp nhận (rỗng khi chưa chụp)",
          s == 200 and all(rows.get(pid.lower(), {}).get("arrivalPhotos") == [] for pid in (pa, pb, pc, pd))
          and r["data"].get("arrivalPhotos") == [], r.get("data", {}).get("arrivalPhotos"))

    base = f"/api/international-shipments/{shipment_id}/destination-receiving"
    items = [
        {"parcelId": pa, "actualWeight": 1.0, "length": 20, "width": 20, "height": 10, "condition": "GOOD"},
        {"parcelId": pb, "actualWeight": 1.0, "length": 20, "width": 20, "height": 10, "condition": "DAMAGED",
         "inspectionNote": "Thùng móp một góc"},
        {"parcelId": pc, "actualWeight": 1.0, "length": 40, "width": 20, "height": 10, "condition": "GOOD"},
    ]

    # ---------------- Chưa có ảnh thì không tiếp nhận được
    s, r = call("PUT", base, "staff", {"items": items})
    msg = r.get("message", "")
    check("Tiếp nhận khi chưa chụp ảnh bị chặn (400, câu tiếng Việt)",
          s == 400 and "Phải chụp ảnh" in msg and all(codes[p] in msg for p in (pa, pb, pc)), r)
    check("Câu lỗi nói rõ kiện hỏng bắt buộc ảnh hiện trạng",
          f"{codes[pb]} (hư hỏng — bắt buộc ảnh hiện trạng)" in msg, msg)
    check("Câu lỗi nói rõ kiện lệch kích thước", f"{codes[pc]} (lệch kích thước" in msg, msg)

    # ---------------- Luật tải ảnh
    s, r = arrival_photo(pa, content_type="application/pdf", name="anh.pdf")
    check("Ảnh tiếp nhận không nhận PDF", s == 400 and "file ảnh" in r.get("message", ""), r)
    s, r = arrival_photo(order_id, entity_type="ORDER")
    expect("Ảnh tiếp nhận không gắn vào đơn", s, r, 400)
    s, r = arrival_photo(pa, who="customer")
    expect("Khách không tải ảnh tiếp nhận được", s, r, 403)

    s, photo_a = arrival_photo(pa)
    expect("Kho chụp ảnh kiện A", s, photo_a, 200)
    s, r = arrival_photo(pb)
    expect("Kho chụp ảnh kiện B (hỏng)", s, r, 200)

    s, r = call("PUT", base, "staff", {"items": items})
    msg = r.get("message", "")
    check("Còn kiện C chưa có ảnh thì vẫn chặn, chỉ nêu kiện C",
          s == 400 and codes[pc] in msg and codes[pa] not in msg and codes[pb] not in msg, r)

    s, r = arrival_photo(pc)
    expect("Kho chụp ảnh kiện C", s, r, 200)
    s, lot_photo = arrival_photo(shipment_id, entity_type="SHIPMENT", name="mo-container.png")
    expect("Kho chụp ảnh toàn lô lúc mở container (không bắt buộc)", s, lot_photo, 200)

    # ---------------- Tiếp nhận
    s, r = call("PUT", base, "staff", {"items": items, "note": "Mở lô có chụp ảnh"})
    if not expect("Đủ ảnh thì tiếp nhận được", s, r, 200):
        return 1
    res = {p["parcelId"].lower(): p for p in r["data"]["parcels"]}
    check("Kết quả tiếp nhận trả kèm ảnh của từng kiện",
          all(len((res[p.lower()].get("inspection") or {}).get("photos") or []) == 1 for p in (pa, pb, pc)), res)
    ph = (res[pa.lower()].get("inspection") or {}).get("photos", [{}])[0]
    check("Ảnh trả về có id, loại, đường tải kèm quyền",
          ph.get("documentType") == "VN_ARRIVAL_PROOF" and ph.get("downloadUrl", "").startswith("/api/attachments/")
          and ph.get("contentType") == "image/png", ph)

    s, r = call("GET", f"/api/international-shipments/{shipment_id}", "staff")
    rows = {p["parcelId"].lower(): p for p in r.get("data", {}).get("parcels", [])}
    check("Chi tiết lô: kiện đã nhận có ảnh tiếp nhận",
          all(len(rows[p.lower()].get("arrivalPhotos") or []) == 1 for p in (pa, pb, pc)), rows)
    check("Chi tiết lô: kiện D chưa nhận vẫn chưa có ảnh", rows[pd.lower()].get("arrivalPhotos") == [], rows[pd.lower()])
    check("Chi tiết lô: có ảnh toàn lô", len(r["data"].get("arrivalPhotos") or []) == 1, r["data"].get("arrivalPhotos"))

    # ---------------- Biên bản kiểm kiện của OM
    s, r = call("GET", f"/api/parcel-inspections?onlyDiscrepancy=false&shipmentId={shipment_id}", "om1")
    ins = {i["parcelId"].lower(): i for i in r.get("data", {}).get("items", [])}
    check("Biên bản kiểm kiện VN trả kèm ảnh", s == 200 and all(len(ins.get(p.lower(), {}).get("photos") or []) == 1
                                                              for p in (pa, pb, pc)), ins)

    # ---------------- Sự cố hàng hỏng tự có ảnh hiện trạng
    s, r = call("GET", f"/api/parcel-incidents?orderId={order_id}", "om1")
    damaged = next((i for i in r.get("data", {}).get("items", []) if i["incidentType"] == "DAMAGED"), None)
    if check("Kiện hỏng tự mở sự cố", damaged is not None, r):
        s, r = call("GET", f"/api/attachments?entityType=INCIDENT&entityId={damaged['id']}", "om1")
        check("Sự cố hỏng có sẵn ảnh hiện trạng từ ảnh tiếp nhận",
              s == 200 and any(a["documentType"] == "INCIDENT_PHOTO" for a in r.get("data", [])), r)

    # ---------------- Xem ảnh: kho / OM / Sale / khách
    photo_id = photo_a["data"]["id"]
    for who in ("staff", "om1", "sale"):
        s, ctype = download(photo_id, who)
        check(f"{who} xem được ảnh tiếp nhận", s == 200 and ctype.startswith("image/"), (s, ctype))
    s, ctype = download(photo_id, "customer")
    check("Khách chủ đơn xem được ảnh tiếp nhận kiện của mình", s == 200 and ctype.startswith("image/"), (s, ctype))
    s, _ = download(photo_id, "customer2")
    check("Khách khác không xem được ảnh", s == 403, s)
    s, r = call("GET", f"/api/attachments?entityType=PARCEL&entityId={pa}", "customer")
    docs = {a["documentType"] for a in r.get("data", [])}
    check("Khách liệt kê ảnh kiện chỉ thấy ảnh tiếp nhận (không thấy ảnh ô kệ)",
          s == 200 and docs == {"VN_ARRIVAL_PROOF"}, r)
    s, r = call("GET", f"/api/attachments?entityType=PARCEL&entityId={pa}", "customer2")
    expect("Khách khác không liệt kê được ảnh kiện", s, r, 403)
    s, r = call("GET", f"/api/attachments?entityType=PARCEL&entityId={pa}", "staff")
    docs = {a["documentType"] for a in r.get("data", [])}
    check("Nhân viên kho thấy cả ảnh ô kệ lẫn ảnh tiếp nhận", {"PUT_AWAY_PROOF", "VN_ARRIVAL_PROOF"} <= docs, docs)

    s, r = call("GET", f"/api/orders/{order_id}/settlement-preview", "customer")
    pv = {p["parcelId"].lower(): p for p in r.get("data", {}).get("parcels", [])}
    check("Xem trước tất toán trả kèm ảnh tiếp nhận từng kiện",
          s == 200 and len(pv.get(pa.lower(), {}).get("arrivalPhotos") or []) == 1, pv.get(pa.lower()))

    # ---------------- Cân lại / biên bản lệch phải có ảnh
    s, r = call("PUT", f"/api/parcels/{pd}/inspection", "staff", {"actualWeight": 1.0, "condition": "DAMAGED"})
    check("Biên bản ghi hỏng mà kiện chưa có ảnh bị chặn",
          s == 400 and "VN_ARRIVAL_PROOF" in r.get("message", ""), r)
    s, r = call("PUT", f"/api/parcels/{pd}/inspection", "staff", {"actualWeight": 3.0})
    check("Biên bản lệch cân mà kiện chưa có ảnh bị chặn", s == 400, r)
    s, r = call("PUT", f"/api/parcels/{pd}/inspection", "staff", {"actualWeight": 1.0, "condition": "GOOD"})
    check("Biên bản khớp thì không cần ảnh", s == 200 and r["data"]["photos"] == [], r)
    s, r = call("PUT", f"/api/parcels/{pa}/inspection", "staff", {"actualWeight": 1.3, "length": 20, "width": 20, "height": 10})
    check("Cân lại kiện đã có ảnh: lưu được, trả kèm ảnh",
          s == 200 and r["data"]["hasDiscrepancy"] and len(r["data"]["photos"]) == 1, r)

    # ---------------- Phân quyền theo kho được gán
    other = create_staff("vpKR")
    vn_staff = create_staff("vpVN")
    try:
        s, r = assign(other, [KR], note="Kiểm thử ảnh tiếp nhận VN")
        expect("Gán nhân viên thử vào kho Seoul", s, r, 200)
        s, r = assign(vn_staff, [VN_WAREHOUSE], note="Kiểm thử ảnh tiếp nhận VN")
        expect("Gán nhân viên thử vào kho HCM", s, r, 200)

        s, r = arrival_photo(pd, who="vpKR")
        expect("Nhân viên kho Seoul không chụp ảnh tiếp nhận cho kho HCM", s, r, 403)
        s, _ = download(photo_id, "vpKR")
        check("Nhân viên kho Seoul không xem ảnh tiếp nhận của kho HCM", s == 403, s)
        s, r = call("GET", f"/api/attachments?entityType=PARCEL&entityId={pa}", "vpKR")
        docs = {a["documentType"] for a in r.get("data", [])}
        check("Danh sách ảnh kiện của nhân viên kho Seoul bỏ ảnh tiếp nhận VN, vẫn thấy ảnh ô kệ",
              s == 200 and "VN_ARRIVAL_PROOF" not in docs and "PUT_AWAY_PROOF" in docs, docs)
        s, r = call("GET", f"/api/parcel-inspections?onlyDiscrepancy=false&shipmentId={shipment_id}", "vpKR")
        check("Biên bản kiểm kiện của kho HCM không lộ cho nhân viên kho Seoul",
              s == 200 and r["data"]["items"] == [], r)

        s, r = arrival_photo(pd, who="vpVN")
        expect("Nhân viên kho HCM chụp ảnh tiếp nhận được", s, r, 200)
        s, _ = download(photo_id, "vpVN")
        check("Nhân viên kho HCM xem được ảnh", s == 200, s)
        s, r = call("GET", f"/api/parcel-inspections?onlyDiscrepancy=false&shipmentId={shipment_id}", "vpVN")
        check("Nhân viên kho HCM thấy biên bản của lô về kho mình", s == 200 and len(r["data"]["items"]) >= 3, r)
        s, r = call("PUT", base, "vpVN", {"items": [
            {"parcelId": pd, "actualWeight": 1.0, "length": 20, "width": 20, "height": 10, "condition": "GOOD"}]})
        check("Nhân viên kho HCM tiếp nhận kiện D (đã có ảnh)",
              s == 200 and len((r["data"]["parcels"][0].get("inspection") or {}).get("photos") or []) == 1, r)
    finally:
        for uid in (other, vn_staff):
            assign(uid, [], note="Dọn kiểm thử")
            call("PUT", f"/api/User/{uid}/lock", "admin")

    print(f"\n{len(PASSED)} PASS / {len(FAILED)} FAIL")
    if FAILED:
        print("Lỗi: " + ", ".join(FAILED))
    return 1 if FAILED else 0


if __name__ == "__main__":
    sys.exit(main() or 0)
