#!/usr/bin/env python3
"""
Kiểm thử "không lộ tiếng Anh / mã thô ra màn hình":

  1. Đăng nhập sai mật khẩu → 401 với câu tiếng Việt (trước đây: "Invalid credentials").
  2. Lô về kho đích và kho VN tiếp nhận xong:
     - dòng thời gian lô của Sale có chữ cho MỌI mốc, mốc RECEIVED_AT_DESTINATION là
       "Kho đích đã nhận lô" (trước đây server trả lại chính mã → web hiện "RECEIVED_AT_DESTINATION");
     - dòng thời gian khách: không mốc nào mang tiêu đề là mã, và mốc "Kho Việt Nam đã nhận hàng"
       chỉ hiện MỘT lần (trước đây lô ghi RECEIVED_AT_DESTINATION + đơn ghi RECEIVED_AT_VN → hai dòng).
  3. Câu lỗi nghiệp vụ có nhắc trạng thái thì dùng tên tiếng Việt, không chèn mã thô.

    APP_ENV=/root/vcl-migration-deploy/app.env API=http://127.0.0.1:8088 python3 vietnamese_messages_flow.py
"""
import json, re, sys, urllib.error, urllib.request, uuid

from wave1_consignment_flow import API, IDS, PASSED, FAILED, RUN, call, check, expect
from warehouse_zones_flow import received_parcels
from export_flow import KR, VN_WAREHOUSE, set_region, store
from vn_arrival_flow import arrival_photos, ship_to_vietnam, tracking

CODE = re.compile(r"^[A-Z0-9]+(?:_[A-Z0-9]+)+$|^[A-Z]{3,}$")
CODE_IN_TEXT = re.compile(r"\b[A-Z][A-Z0-9]*(?:_[A-Z0-9]+)+\b")
VIETNAMESE = re.compile(r"[à-ỹđÀ-ỸĐ]")


def is_code(text):
    return bool(CODE.match(str(text or "").strip()))


def anonymous_login(email, password):
    req = urllib.request.Request(API + "/api/Auth/login", method="POST",
                                 data=json.dumps({"email": email, "password": password}).encode())
    req.add_header("Content-Type", "application/json")
    try:
        with urllib.request.urlopen(req, timeout=60) as r:
            return r.status, json.loads(r.read().decode() or "{}")
    except urllib.error.HTTPError as e:
        raw = e.read().decode() or "{}"
        try:
            return e.code, json.loads(raw)
        except json.JSONDecodeError:
            return e.code, {"raw": raw}


def main():
    # ---------------- 1. Đăng nhập sai
    s, r = anonymous_login(f"khong-ton-tai-{uuid.uuid4().hex[:8]}@vcl.test", "sai-mat-khau")
    msg = r.get("message", "")
    check("Email không tồn tại: 401 câu tiếng Việt",
          s == 401 and VIETNAMESE.search(msg) and "Invalid" not in msg, (s, r))

    s, r = call("GET", f"/api/User/{IDS['customer']['id']}", "admin")
    email = ((r.get("data") or r) if isinstance(r, dict) else {}).get("email")
    if email:
        s, r = anonymous_login(email, "sai-mat-khau-" + uuid.uuid4().hex[:6])
        msg = r.get("message", "")
        check("Sai mật khẩu: 401 câu tiếng Việt", s == 401 and VIETNAMESE.search(msg) and "Invalid" not in msg, (s, r))
    else:
        print("BỎ QUA sai mật khẩu với tài khoản thật: không đọc được email khách thử", flush=True)

    # ---------------- 2. Lô về kho đích, kho VN tiếp nhận
    call("PUT", f"/api/warehouses/{KR}/manager", "admin", {"managerId": None, "reason": "Kiểm thử nhãn tiếng Việt"})
    call("PUT", f"/api/warehouses/{VN_WAREHOUSE}/manager", "admin", {"managerId": None, "reason": "Kiểm thử nhãn tiếng Việt"})
    set_region("staff", "KR")

    s, order_id, parcels = received_parcels(["VI A"], f"VI{RUN}")
    if not check("Chuẩn bị đơn 1 kiện", s in (200, 201) and len(parcels) == 1, parcels):
        return finish()
    pid = parcels["VI A"]
    store(pid, "BIN-A1-01")
    shipment_id = ship_to_vietnam([pid], f"VI{RUN}")
    if not check("Lô về tới kho đích", shipment_id is not None, shipment_id):
        return finish()

    check("Chụp ảnh tiếp nhận", arrival_photos([pid]))
    base = f"/api/international-shipments/{shipment_id}/destination-receiving"
    s, r = call("PUT", base, "staff", {"items": [
        {"parcelId": pid, "actualWeight": 1.0, "length": 20, "width": 20, "height": 10, "condition": "GOOD"}]})
    if not expect("Kho VN tiếp nhận", s, r, 200):
        return finish()

    # Sale: dòng thời gian lô
    s, r = call("GET", f"/api/international-shipments/{shipment_id}/timeline", "sale")
    events = (r.get("data") or {}).get("events", []) if isinstance(r, dict) else []
    check("Dòng thời gian lô đọc được", s == 200 and events, (s, r))
    raw = [e for e in events if not str(e.get("statusText") or "").strip() or is_code(e.get("statusText"))]
    check("Mọi mốc lô có chữ tiếng Việt, không mốc nào là mã", not raw, raw)
    received = [e for e in events if e.get("status") == "RECEIVED_AT_DESTINATION"]
    check("Mốc RECEIVED_AT_DESTINATION là \"Kho đích đã nhận lô\"",
          received and all(e.get("statusText") == "Kho đích đã nhận lô" for e in received), received)

    # Khách: dòng thời gian đơn
    t = tracking(order_id)
    cevents = t.get("events", [])
    bad = [e for e in cevents if is_code(e.get("title")) or not str(e.get("title") or "").strip()
           or CODE_IN_TEXT.search(str(e.get("message") or ""))]
    check("Dòng thời gian khách không có tiêu đề / lời nhắn chứa mã", not bad, bad)
    titles = [e.get("title") for e in cevents]
    check("Khách chỉ thấy MỘT mốc \"Kho Việt Nam đã nhận hàng\"",
          titles.count("Kho Việt Nam đã nhận hàng") == 1, titles)
    check("Chặng hiện tại có chữ", not is_code(t.get("currentStageText")) and t.get("currentStageText"),
          t.get("currentStageText"))
    ship_rows = [x for x in t.get("shipments", []) if is_code(x.get("statusText"))]
    check("Thẻ lô của khách không in mã", not ship_rows, ship_rows)

    # ---------------- 3. Câu lỗi nhắc trạng thái dùng tên tiếng Việt
    s, r = call("PUT", base, "staff", {"items": [
        {"parcelId": pid, "actualWeight": 1.0, "length": 20, "width": 20, "height": 10}]})
    msg = r.get("message", "") if isinstance(r, dict) else ""
    check("Tiếp nhận lại bị chặn bằng câu tiếng Việt, không chèn mã trạng thái",
          s == 400 and VIETNAMESE.search(msg) and not CODE_IN_TEXT.search(msg), (s, r))

    s, r = call("PUT", f"/api/international-shipments/{shipment_id}/status", "sale", {"status": "DEPARTED"})
    msg = r.get("message", "") if isinstance(r, dict) else ""
    check("Đặt mốc sai thứ tự bị chặn bằng câu tiếng Việt, không chèn mã",
          s == 400 and VIETNAMESE.search(msg) and not CODE_IN_TEXT.search(msg), (s, r))

    return finish()


def finish():
    print(f"\n{len(PASSED)} PASS / {len(FAILED)} FAIL")
    if FAILED:
        print("Lỗi: " + ", ".join(FAILED))
    return 1 if FAILED else 0


if __name__ == "__main__":
    sys.exit(main() or 0)
