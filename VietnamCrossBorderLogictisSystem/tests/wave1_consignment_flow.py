#!/usr/bin/env python3
"""
Kiểm thử luồng ký gửi đợt 1 (tạo đơn → tiếp nhận → báo giá → cọc → phiếu tiếp nhận → nhận hàng → xếp kệ)
qua HTTP thật, trên môi trường thử (vcl-migration-api, DB AppDb_migration).

Token JWT được ký bằng khoá của môi trường thử (đọc từ app.env), không dùng mật khẩu của ai.
Chỉ chạy trên DB thử — kịch bản tạo đơn, khoản thu và kiện thật trong DB.

    APP_ENV=/root/vcl-migration-deploy/app.env API=http://127.0.0.1:8088 python3 wave1_consignment_flow.py
"""
import base64, hashlib, hmac, json, os, sys, time, uuid, urllib.request, urllib.error

API = os.environ.get("API", "http://127.0.0.1:8088")
ENV = dict(l.split("=", 1) for l in open(os.environ.get("APP_ENV", "/root/vcl-migration-deploy/app.env")).read().splitlines() if "=" in l)
SECRET = ENV["JWT__SecretKey"].encode()
SEPAY_KEY = ENV.get("SePay__ApiKey", "")

# Danh tính có sẵn trong bản sao DB (xem README kiểm thử).
IDS = json.loads(os.environ.get("TEST_IDS") or open(os.environ.get("TEST_IDS_FILE", "/root/vcl-migration-deploy/test_ids.json")).read())

PASSED, FAILED = [], []


def b64(data: bytes) -> str:
    return base64.urlsafe_b64encode(data).rstrip(b"=").decode()


def token(user_id: str, role: str) -> str:
    header = {"alg": "HS256", "typ": "JWT"}
    now = int(time.time())
    payload = {"nameid": user_id, "unique_name": f"test-{role}", "email": "", "role": role, "region": "",
               "iss": ENV.get("JWT__ValidIssuer", "vcl-system"), "aud": ENV.get("JWT__ValidAudience", "vcl-clients"),
               "nbf": now - 5, "exp": now + 3600}
    signing = f"{b64(json.dumps(header).encode())}.{b64(json.dumps(payload).encode())}"
    sig = hmac.new(SECRET, signing.encode(), hashlib.sha256).digest()
    return f"{signing}.{b64(sig)}"


TOK = {k: token(v["id"], v["role"]) for k, v in IDS.items() if isinstance(v, dict) and "role" in v}


def call(method, path, who=None, body=None, headers=None):
    data = None if body is None else json.dumps(body).encode()
    req = urllib.request.Request(API + path, data=data, method=method)
    req.add_header("Content-Type", "application/json")
    if who:
        req.add_header("Authorization", "Bearer " + TOK[who])
    for k, v in (headers or {}).items():
        req.add_header(k, v)
    try:
        with urllib.request.urlopen(req, timeout=60) as r:
            raw = r.read().decode() or "{}"
            return r.status, json.loads(raw)
    except urllib.error.HTTPError as e:
        raw = e.read().decode() or "{}"
        try:
            return e.code, json.loads(raw)
        except json.JSONDecodeError:
            return e.code, {"raw": raw}


def check(name, cond, detail=""):
    (PASSED if cond else FAILED).append(name)
    print(("PASS " if cond else "FAIL ") + name + ("" if cond else f"  -> {detail}"), flush=True)
    return cond


def expect(name, status, resp, code):
    return check(name, status == code, f"HTTP {status}: {json.dumps(resp, ensure_ascii=False)[:400]}")


def put_away_photo(parcel_id, who="staff"):
    """
    Chụp ảnh kiện trong ô kệ — hệ thống bắt buộc có trước khi ghi nhận xếp kệ.

    Tự dựng multipart tại chỗ thay vì mượn upload() của export_flow: file đó lại import
    ngược từ file này, nối vào là hai module nạp vòng tròn.
    """
    boundary = "----vclw1" + uuid.uuid4().hex
    content = b"\x89PNG\r\n\x1a\n oke\n"
    parts = []
    for key, value in (("entityType", "PARCEL"), ("entityId", str(parcel_id)),
                       ("documentType", "PUT_AWAY_PROOF"), ("note", "kiểm thử")):
        parts.append(f"--{boundary}\r\nContent-Disposition: form-data; name=\"{key}\"\r\n\r\n{value}\r\n".encode())
    parts.append(f"--{boundary}\r\nContent-Disposition: form-data; name=\"file\"; filename=\"anh-o-ke.png\"\r\n"
                 f"Content-Type: image/png\r\n\r\n".encode() + content + b"\r\n")
    parts.append(f"--{boundary}--\r\n".encode())

    req = urllib.request.Request(API + "/api/attachments", data=b"".join(parts), method="POST")
    req.add_header("Content-Type", f"multipart/form-data; boundary={boundary}")
    req.add_header("Authorization", "Bearer " + TOK[who])
    try:
        with urllib.request.urlopen(req) as resp:
            return resp.status
    except urllib.error.HTTPError as e:
        return e.code


def create_order(items, suffix):
    s, r = call("POST", "/api/orders/consignments", "customer", {
        "route": "KR-VN", "shippingOption": "Express",
        "receiverName": "Test W1 " + suffix, "receiverPhone": "0900000000", "receiverAddress": "HCM",
        "defaultDestinationHandling": "DIRECT_DELIVERY",
        "items": items,
    })
    return (200 if s == 201 else s), r.get("data", r)


def item(name, qty, weight):
    return {"productName": name, "productType": IDS["productTypeId"], "quantity": qty, "weight": weight,
            "length": 20, "width": 20, "height": 10, "declaredValue": 100000,
            "packageConfigurationId": IDS["packageConfigurationId"]}


def get_order_quotation(order_id, who="customer"):
    return call("GET", f"/api/orders/{order_id}/quotation", who)


def sepay(order_code, amount, auth=True):
    headers = {"Authorization": f"Apikey {SEPAY_KEY}"} if auth else {}
    return call("POST", "/api/payments/sepay-webhook", None, {
        "id": int(time.time() * 1000), "gateway": "MB", "transactionDate": "2026-09-17 10:00:00",
        "accountNumber": "0", "transferAmount": amount, "content": f"VCL{order_code}",
        "code": f"VCL{order_code}", "referenceCode": "FT" + uuid.uuid4().hex[:10].upper(),
    }, headers)


RUN = uuid.uuid4().hex[:6].upper()


def main():
    kr = IDS["warehouseKr"]

    # Dọn trạng thái của lần chạy trước (nếu bị dừng giữa chừng): kho KR chưa có quản lý riêng.
    call("PUT", f"/api/warehouses/{kr}/manager", "admin", {"managerId": None, "reason": "Chuẩn bị kiểm thử"})

    # ---------------- ① Tạo đơn, tiếp nhận
    s, r = create_order([item("Áo W1", 3, 1.0), item("Giày W1", 2, 1.5)], "A")
    if not expect("Khách tạo đơn ký gửi", s, r, 200):
        return
    order_id = r["orderId"]
    estimate_id = r["quotation"]["quotationId"]
    check("Đơn mới ở PENDING_REVIEW", r["status"] == "PENDING_REVIEW", r["status"])

    s, r = call("PUT", f"/api/quotations/{estimate_id}/confirm-and-pay", "customer", {"paymentMethod": "SEPAY"})
    expect("Khách không xác nhận được báo giá tạm tính", s, r, 400)

    s, r = call("PUT", f"/api/quotations/{estimate_id}/accept", "customer")
    check("Đã bỏ bước xác nhận báo giá không kèm thanh toán", s in (404, 405), r)

    s, r = call("PUT", f"/api/orders/consignments/{order_id}/status", "staff", {"status": "APPROVED"})
    expect("Nhân viên kho không tiếp nhận đơn được", s, r, 403)

    s, r = call("PUT", f"/api/orders/consignments/{order_id}/status", "sale", {"status": "NEED_MORE_INFO"})
    expect("Yêu cầu bổ sung phải có lý do", s, r, 400)

    s, r = call("PUT", f"/api/orders/consignments/{order_id}/status", "sale", {"status": "APPROVED"})
    expect("Không còn bước Sale duyệt đơn trước khi báo giá", s, r, 400)

    # ---------------- Báo giá
    s, r = call("POST", f"/api/orders/{order_id}/quotation/send", "customer", {"warehouseId": kr})
    expect("Khách không tự gửi báo giá được", s, r, 403)

    s, r = call("POST", f"/api/orders/{order_id}/quotation/send", "sale", {})
    expect("Báo giá bắt buộc có kho nhận hàng", s, r, 400)

    s, r = call("POST", f"/api/orders/{order_id}/quotation/send", "sale", {"warehouseId": kr, "serviceType": "NoSuchService"})
    expect("Không có bảng giá thì không báo giá (không dùng giá mặc định)", s, r, 400)

    s, r = call("POST", f"/api/orders/{order_id}/quotation/send", "sale", {"warehouseId": kr})
    check("Sale báo giá thẳng từ đơn mới (không cần bước tiếp nhận)", s == 200 and r["consignment"]["status"] == "QUOTATION_SENT", r)
    s, q1 = get_order_quotation(order_id)
    check("Khách thấy báo giá chính thức PENDING", s == 200 and q1["data"]["status"] == "PENDING" and q1["data"]["canCustomerAccept"], q1)

    # Báo giá đã tới tay khách thì Sale không gửi đè; khách từ chối rồi mới lập lại.
    s, r = call("POST", f"/api/orders/{order_id}/quotation/send", "sale", {"warehouseId": kr})
    expect("Báo giá đã gửi khách: Sale không gửi đè", s, r, 409)
    s, r = call("PUT", f"/api/quotations/{q1['data']['quotationId']}/reject", "customer", {"rejectionReason": "Muốn thương lượng phụ phí"})
    expect("Khách từ chối báo giá để Sale lập lại", s, r, 200)

    manual = {"warehouseId": kr, "quotation": {"additionalFees": [{"label": "Phụ phí tay", "amount": 50000, "enabled": True}]}}
    s, r = call("POST", f"/api/orders/{order_id}/quotation/send", "sale", manual)
    expect("Giá ngoại lệ phải ghi lý do", s, r, 400)

    manual["overrideReason"] = "Hàng cồng kềnh"
    s, r = call("POST", f"/api/orders/{order_id}/quotation/send", "sale", manual)
    check("Giá ngoại lệ chuyển Admin duyệt", s == 200 and r.get("status") == "PENDING_PRICE_APPROVAL", r)

    s, q2 = get_order_quotation(order_id)
    check("Khách vẫn xem báo giá cũ (đã từ chối) trong lúc chờ duyệt giá", s == 200 and q2["data"]["quotationId"] == q1["data"]["quotationId"], q2)

    s, staff_view = get_order_quotation(order_id, "sale")
    override_id = staff_view["data"]["quotationId"]
    check("Sales thấy báo giá chờ duyệt giá", staff_view["data"]["status"] == "PENDING_PRICE_APPROVAL", staff_view)

    s, r = call("PUT", f"/api/quotations/{override_id}/price-approval", "sale", {"decision": "APPROVED"})
    expect("Sales không tự duyệt giá ngoại lệ", s, r, 403)

    s, r = call("PUT", f"/api/quotations/{override_id}/price-approval", "admin", {"decision": "APPROVED", "note": "Đồng ý"})
    expect("Admin duyệt giá ngoại lệ", s, r, 200)

    s, r = call("POST", f"/api/orders/{order_id}/quotation/send", "sale", {"warehouseId": kr})
    check("Admin đã duyệt & gửi khách: Sale không báo giá lại", s == 409 and "Admin duyệt" in r.get("message", ""), (s, r))

    s, q3 = get_order_quotation(order_id)
    check("Sau duyệt khách thấy báo giá mới, bản cũ bị thay", s == 200 and q3["data"]["quotationId"] == override_id and q3["data"]["status"] == "PENDING", q3)
    s, r = call("PUT", f"/api/quotations/{q1['data']['quotationId']}/confirm-and-pay", "customer", {"paymentMethod": "SEPAY"})
    expect("Khách không xác nhận được báo giá đã bị thay", s, r, 400)

    s, other = call("GET", f"/api/orders/{order_id}/quotation", "customer2")
    expect("Khách khác không xem được báo giá của đơn này", s, other, 403)

    # ---------------- ② Cọc
    s, pay = call("PUT", f"/api/quotations/{override_id}/confirm-and-pay", "customer", {"paymentMethod": "SEPAY"})
    if not expect("Khách xác nhận báo giá và tạo khoản cọc", s, pay, 200):
        return
    pay = pay["data"]
    check("Đơn chờ cọc", pay["orderStatus"] == "WAITING_DEPOSIT", pay)

    s, r = call("POST", "/api/warehouse-receiving-notes", "sale", {"consignmentOrderId": order_id, "warehouseId": kr})
    expect("Chưa có cọc thì không lập phiếu tiếp nhận", s, r, 400)

    s, r = sepay(pay["orderCode"], pay["amount"], auth=False)
    expect("Webhook SePay không xác thực bị từ chối", s, r, 401)

    s, r = sepay(pay["orderCode"], pay["amount"] - 1000)
    check("Webhook chuyển thiếu tiền không xác nhận", s == 200 and r.get("handled") is False, r)

    s, r = sepay(pay["orderCode"], pay["amount"] + 5000)
    check("Webhook đúng xác nhận cọc (ghi cả tiền dư)", s == 200 and r.get("handled") is True, r)

    s, hist = call("GET", f"/api/orders/{order_id}/payments/history", "customer")
    deposit = next((p for p in hist["data"]["payments"] if p["installmentType"] == "DEPOSIT"), {})
    check("Khoản cọc SUCCESS", deposit.get("status") == "SUCCESS", hist)

    s, notes = call("GET", f"/api/warehouse-receiving-notes?search=&pageSize=200", "om1")
    auto_note = next((n for n in notes["data"]["items"] if n["consignmentOrderId"] == order_id), None) if s == 200 else None
    check("Thanh toán xong hệ thống không tự lập phiếu nhập kho", s == 200 and auto_note is None, notes)

    s, r = call("POST", "/api/warehouse-receiving-notes", "staff", {"consignmentOrderId": order_id, "warehouseId": kr})
    expect("Nhân viên kho không lập phiếu nhập kho", s, r, 403)

    s, r = call("POST", "/api/warehouse-receiving-notes", "sale", {"consignmentOrderId": order_id, "warehouseId": kr})
    note = r.get("receivingNote") if s == 201 else None
    if not check("Sale lập phiếu nhập kho, chờ quản lý kho duyệt", note is not None and note["status"] == "PENDING_APPROVAL" and note["createdByName"] != "", r):
        return
    note_id = note["id"]
    note_code = note["receivingNoteCode"]

    s, r = call("POST", "/api/warehouse-receiving-notes", "sale", {"consignmentOrderId": order_id, "warehouseId": kr})
    expect("Không lập trùng phiếu cho một đơn", s, r, 409)

    s, r = call("PUT", f"/api/warehouse-receiving-notes/{note_id}/status", "sale", {"status": "APPROVED"})
    check("Sale không tự duyệt phiếu mình lập", s in (400, 403), r)

    s, r = call("PUT", f"/api/orders/consignments/{order_id}/cancel", "customer", {"cancelReason": "đổi ý"})
    expect("Đã có tiền thì khách không tự huỷ được", s, r, 400)

    s, r = call("PUT", f"/api/orders/consignments/{order_id}/status", "sale", {"status": "REJECTED", "rejectionReason": "x"})
    expect("Đơn đã có báo giá được chấp nhận thì không đổi kết quả tiếp nhận", s, r, 400)

    # ---------------- ③ Duyệt nhận hàng, check-in, xếp kệ
    s, r = call("POST", "/api/parcels/check-in", "staff", {"shippingCode": note_code, "packages": [{"actualWeight": 1.0, "length": 20, "width": 20, "height": 10}]})
    expect("Phiếu chưa duyệt thì kho chưa nhận hàng", s, r, 400)

    s, r = call("PUT", f"/api/warehouse-receiving-notes/{note_id}/status", "staff", {"status": "APPROVED"})
    check("Nhân viên kho không duyệt phiếu", s in (400, 403), r)

    s, r = call("PUT", f"/api/warehouse-receiving-notes/{note_id}/status", "om1", {"status": "APPROVED"})
    check("Quản lý kho duyệt cho nhận hàng", s == 200 and r["data"]["status"] == "ACTIVE" and r["data"]["receiveApprovedAt"], r)
    check("Duyệt xong phát hành phiếu PDF cho khách mang tới kho", bool(r["data"].get("receiptPdfUrl")), r)

    req = urllib.request.Request(API + f"/api/orders/consignments/{order_id}/receipt")
    req.add_header("Authorization", "Bearer " + TOK["sale"])
    try:
        with urllib.request.urlopen(req, timeout=120) as resp:
            pdf = resp.read()
            check("Tải được phiếu PDF", resp.status == 200 and pdf[:4] == b"%PDF", resp.status)
    except urllib.error.HTTPError as e:
        check("Tải được phiếu PDF", False, e.code)

    # Gán quản lý riêng cho kho KR: từ đây om1 không còn quyền duyệt ở kho này.
    s, r = call("PUT", f"/api/warehouses/{kr}/manager", "admin", {"managerId": IDS["om2"]["id"], "reason": "Test phân công"})
    expect("Admin gán quản lý kho KR", s, r, 200)

    s2, noteDetail = call("GET", f"/api/warehouse-receiving-notes/{note_id}", "om2")
    expected = {e["productName"]: e for e in noteDetail["data"]["expectedItems"]}
    shirt, shoes = expected["Áo W1"], expected["Giày W1"]

    s, r = call("POST", "/api/parcels/check-in", "staff", {"shippingCode": note_code, "packages": [{"orderItemId": shirt["orderItemId"], "actualWeight": 1.0, "length": 20, "width": 20, "height": 10}]})
    expect("Khách mang phiếu tới: kho quét mã phiếu WRN- và nhận kiện 1", s, r, 200)
    parcel1 = r.get("data", r)["parcelId"]

    s, nd = call("GET", f"/api/warehouse-receiving-notes/{note_id}", "om2")
    check("Nhận một phần: phiếu PARTIALLY_RECEIVED, xếp kệ được", nd["data"]["status"] == "PARTIALLY_RECEIVED" and nd["data"]["canPutAway"], nd["data"])

    bin_ = IDS["bin"]
    put_away_photo(parcel1)
    s, r = call("PUT", f"/api/parcels/{parcel1}/put-away", "staff", {"warehouseId": IDS["warehouseCn"], "zoneId": bin_["zoneId"], "shelfId": bin_["shelfId"], "binId": bin_["binId"]})
    check("Không xếp kệ sang kho khác kho đã duyệt", s == 400 or s == 404, r)

    put_away_photo(parcel1)
    s, r = call("PUT", f"/api/parcels/{parcel1}/put-away", "staff", {"warehouseId": kr, "zoneId": bin_["zoneId"], "shelfId": bin_["shelfId"], "binId": bin_["binId"]})
    expect("Xếp kệ kiện 1 không cần duyệt lần hai", s, r, 200)

    s, inv = call("GET", f"/api/inventories?warehouseId={kr}", "om2")
    rows = inv.get("items", [])
    qty = sum(x.get("quantity", 0) for x in rows if str(x.get("parcelId", "")).lower() == parcel1.lower())
    check("Tồn kiện 1 = lượng của dòng hàng (3), không nhân tổng đơn (5)", qty == 3, inv)

    # Kiểm đếm dòng 2 lệch số lượng và kèm kiện cân sai → cả thao tác rollback
    bad = {"items": [{"orderItemId": shoes["orderItemId"], "actualQuantity": 1, "actualWeight": 1.5}],
           "parcels": [{"orderItemId": shoes["orderItemId"], "actualWeight": 5000}]}
    s, r = call("POST", f"/api/warehouse-receiving-notes/{note_id}/confirm-receipt", "staff", bad)
    expect("Kiện cân phi lý làm lỗi cả lần kiểm đếm", s, r, 400)
    s, nd = call("GET", f"/api/warehouse-receiving-notes/{note_id}", "om2")
    check("Rollback: biên bản không lưu dở dòng kiểm đếm", len(nd["data"]["items"]) == 0 and nd["data"]["status"] == "PARTIALLY_RECEIVED", nd["data"])

    good = {"items": [{"orderItemId": shoes["orderItemId"], "actualQuantity": 1, "actualWeight": 1.5}],
            "parcels": [{"orderItemId": shoes["orderItemId"], "actualWeight": 1.5, "length": 20, "width": 20, "height": 10}]}
    s, r = call("POST", f"/api/warehouse-receiving-notes/{note_id}/confirm-receipt", "staff", good)
    check("Kiểm đếm lệch số lượng: phiếu chờ quản lý kho xem", s == 200 and r["data"]["requiresReview"] and not r["data"]["canPutAway"], r)
    parcel2 = next(p["parcelId"] for p in r["data"]["parcels"] if p["parcelId"] != parcel1)

    put_away_photo(parcel2)
    s, r = call("PUT", f"/api/parcels/{parcel2}/put-away", "staff", {"warehouseId": kr, "zoneId": bin_["zoneId"], "shelfId": bin_["shelfId"], "binId": bin_["binId"]})
    expect("Chênh lệch chưa xem thì chưa xếp kệ", s, r, 400)

    s, r = call("PUT", f"/api/warehouse-receiving-notes/{note_id}/status", "om1", {"status": "APPROVED"})
    expect("Quản lý kho khác (không phải quản lý kho KR) không duyệt được", s, r, 403)

    s, r = call("PUT", f"/api/warehouse-receiving-notes/{note_id}/status", "admin2", {"status": "APPROVED"})
    expect("Admin duyệt thay quản lý kho phải ghi lý do", s, r, 400)

    s, r = call("PUT", f"/api/warehouse-receiving-notes/{note_id}/status", "om2", {"status": "APPROVED", "reason": "Khách xác nhận gửi thiếu 1 đôi"})
    check("Quản lý kho KR duyệt biên bản chênh lệch", s == 200 and r["data"]["status"] == "APPROVED" and r["data"]["canPutAway"], r)

    put_away_photo(parcel2)
    s, r = call("PUT", f"/api/parcels/{parcel2}/put-away", "staff", {"warehouseId": kr, "zoneId": bin_["zoneId"], "shelfId": bin_["shelfId"], "binId": bin_["binId"]})
    expect("Xếp kệ kiện 2 sau khi xem chênh lệch", s, r, 200)

    s, r = call("PUT", f"/api/warehouses/{kr}/manager", "admin", {"managerId": None, "reason": "Trả kho KR về trạng thái trước test"})
    expect("Gỡ quản lý kho KR sau test", s, r, 200)

    # ---------------- Tiền về khi đơn đã huỷ
    s, r = create_order([item("Mũ W1", 1, 0.5)], "B")
    order_b = r["orderId"]
    call("POST", f"/api/orders/{order_b}/quotation/send", "sale", {"warehouseId": kr})
    _, qb = get_order_quotation(order_b)
    s, pb = call("PUT", f"/api/quotations/{qb['data']['quotationId']}/confirm-and-pay", "customer", {"paymentMethod": "SEPAY"})
    pb = pb["data"]
    s, r = call("PUT", f"/api/orders/consignments/{order_b}/cancel", "customer", {"cancelReason": "không gửi nữa"})
    expect("Khách huỷ đơn chưa có tiền", s, r, 200)
    s, r = sepay(pb["orderCode"], pb["amount"])
    s, hb = call("GET", f"/api/orders/{order_b}/payments/history", "customer")
    dep_b = next((p for p in hb["data"]["payments"] if p["installmentType"] == "DEPOSIT"), {})
    check("Tiền về sau khi huỷ: đơn không sống lại, khoản thu chờ Admin xử lý",
          hb["data"]["orderStatus"] == "CANCELLED" and dep_b.get("status") == "RECEIVED_UNALLOCATED", hb)

    # ---------------- Admin duyệt chuyển khoản tay
    s, r = create_order([item("Túi W1", 1, 0.5)], "C")
    order_c = r["orderId"]
    call("POST", f"/api/orders/{order_c}/quotation/send", "sale", {"warehouseId": kr})
    _, qc = get_order_quotation(order_c)
    s, pc = call("PUT", f"/api/quotations/{qc['data']['quotationId']}/confirm-and-pay", "customer", {"paymentMethod": "OFFLINE"})
    pc = pc["data"]
    s, r = call("PUT", f"/api/orders/{order_c}/payment-confirmation", "sale", {"paymentId": pc["paymentId"], "confirmationNote": "đã xem UNC"})
    check("Đã bỏ bước Sale xác nhận thanh toán", s in (404, 405), r)
    s, r = call("PUT", f"/api/admin/finance/transactions/{pc['paymentId']}/approve", "admin", {"transactionCode": f"FT-W1-{RUN}", "receivedAmount": pc["amount"] - 1})
    expect("Admin nhập tiền thực nhận thiếu thì không xác nhận", s, r, 400)
    s, r = call("PUT", f"/api/admin/finance/transactions/{pc['paymentId']}/approve", "admin", {"transactionCode": f"FT-W1-{RUN}", "receivedAmount": pc["amount"]})
    check("Admin xác nhận chuyển khoản tay → DEPOSIT_PAID", s == 200 and r["data"]["orderStatusAfter"] == "DEPOSIT_PAID", r)

    s, r = create_order([item("Ví W1", 1, 0.3)], "D")
    order_d = r["orderId"]
    call("POST", f"/api/orders/{order_d}/quotation/send", "sale", {"warehouseId": kr})
    _, qd = get_order_quotation(order_d)
    s, pd = call("PUT", f"/api/quotations/{qd['data']['quotationId']}/confirm-and-pay", "customer", {"paymentMethod": "OFFLINE"})
    s, r = call("PUT", f"/api/admin/finance/transactions/{pd['data']['paymentId']}/approve", "admin", {"transactionCode": f"FT-W1-{RUN}"})
    expect("Một mã giao dịch ngân hàng không xác nhận hai khoản thu", s, r, 400)

    # ---------------- Sale tạo đơn ký gửi HỘ KHÁCH (POST /api/staff/consignments)
    # Màn "Tạo đơn hộ khách" của web quản trị: Sale chọn khách trong GET /api/customers
    # rồi gửi customerId kèm đơn. Ba khoá tra khách (customerId / customerPhone /
    # customerEmail) là "hoặc"; thiếu cả ba thì 400, sai thì 404.
    s, r = call("GET", "/api/customers", "sale")
    customers = r.get("items", []) if s == 200 else []
    check("Sale đọc được danh sách khách hàng", s == 200 and len(customers) > 0, r)

    s, r = call("GET", "/api/customers", "staff")
    check("Nhân viên kho không đọc được danh sách khách hàng", s == 403, f"HTTP {s}")

    customer = next((c for c in customers if c.get("id")), None)
    if not customer:
        check("Có khách hàng để tạo đơn hộ", False, customers)
    else:
        customer_id = customer["id"]

        # Tìm kiếm phía server — đúng tham số mà ô chọn khách của FE gửi lên.
        if customer.get("phone"):
            s, r = call("GET", f"/api/customers?search={customer['phone']}", "sale")
            check("Tìm khách theo số điện thoại",
                  s == 200 and any(c["id"] == customer_id for c in r.get("items", [])), r)

        staff_items = [dict(item("Ly sứ W1 hộ khách", 2, 1.0),
                            referenceUrls=["https://cdn.example.test/staff-w1.jpg"],
                            domesticTrackingCode=f"SPX-{RUN}")]

        base = {"route": "KR-VN", "shippingOption": "Express",
                "receiverName": "Người nhận hộ " + RUN, "receiverPhone": "0900000001",
                "receiverAddress": "12 Lê Lợi, Quận 1, TP HCM",
                "defaultDestinationHandling": "DIRECT_DELIVERY",
                "note": f"Sale tạo hộ {RUN}", "items": staff_items}

        s, r = call("POST", "/api/staff/consignments", "sale", base)
        check("Thiếu cả ba khoá tra khách → 400 kèm câu hướng dẫn",
              s == 400 and "chọn khách hàng" in json.dumps(r, ensure_ascii=False), f"HTTP {s}: {r}")

        s, r = call("POST", "/api/staff/consignments", "sale",
                    dict(base, customerId="00000000-0000-4000-8000-000000000123"))
        expect("customerId không có thật → 404", s, r, 404)

        s, r = call("POST", "/api/staff/consignments", "customer", dict(base, customerId=customer_id))
        check("Khách hàng không gọi được API tạo đơn hộ", s == 403, f"HTTP {s}")

        s, r = call("POST", "/api/orders/consignments/validate-items", "sale", {"items": staff_items})
        check("Sale kiểm được hàng cấm trước khi tạo đơn",
              s == 200 and "canCreate" in r.get("data", {}), r)

        # Ước tính TRƯỚC khi tạo phải bằng đúng báo giá sinh ra sau khi tạo — Sale đọc số
        # nào cho khách nghe thì hệ thống phải phát hành đúng số đó.
        staff_body = dict(base, customerId=customer_id)
        s, r = call("POST", "/api/staff/consignments/preview", "sale", staff_body)
        staff_preview = r.get("data", r)
        check("Sale xem trước được chi phí mà chưa tạo đơn", s == 200 and isinstance(staff_preview, dict), r)

        s, r = call("POST", "/api/staff/consignments/preview", "customer", staff_body)
        check("Khách không dùng endpoint xem trước của nhân viên", s == 403, f"HTTP {s}")

        s, r = call("POST", "/api/staff/consignments/preview", "sale",
                    dict(base, customerId="00000000-0000-4000-8000-000000000123"))
        expect("Xem trước cũng chặn customerId không có thật", s, r, 404)

        s, r = call("POST", "/api/staff/consignments", "sale", dict(base, customerId=customer_id))
        if expect("Sale tạo đơn hộ khách bằng customerId", s, r, 201):
            staff_order = r.get("data", r)
            check("Đơn Sale tạo hộ vào thẳng APPROVED", staff_order.get("status") == "APPROVED", staff_order)
            check("Đơn thuộc đúng khách đã chọn",
                  str(staff_order.get("customerId", "")).lower() == str(customer_id).lower(), staff_order)
            check("Có mã ký gửi trả về", bool(staff_order.get("consignmentCode")), staff_order)

            s, r = get_order_quotation(staff_order["orderId"], "sale")
            staff_quote = r.get("data", r)
            if check("Đọc được báo giá của đơn Sale vừa tạo", s == 200 and isinstance(staff_quote, dict), r):
                for field in ("estimatedFreightCharge", "serviceFee", "domesticShippingFee",
                              "vat", "importTax", "taxAndDuty", "totalEstimatedCost",
                              "chargeableWeight", "totalWeight", "totalVolume"):
                    check(f"Sale: xem trước khớp báo giá — {field}",
                          staff_preview.get(field) == staff_quote.get(field),
                          (staff_preview.get(field), staff_quote.get(field)))

                pv_fees = sorted((f.get("feeName"), f.get("amount")) for f in staff_preview.get("additionalFees", []))
                q_fees = sorted((f.get("feeName"), f.get("amount")) for f in staff_quote.get("additionalFees", []))
                check("Sale: xem trước khớp báo giá — từng dòng phụ phí", pv_fees == q_fees, (pv_fees[:3], q_fees[:3]))

        # Sổ địa chỉ CỦA KHÁCH — đúng endpoint mà màn tạo đơn hộ phải dùng.
        s, r = call("GET", f"/api/customers/{customer_id}/delivery-addresses", "sale")
        check("Sale đọc được sổ địa chỉ của một khách", s == 200 and isinstance(r.get("data"), list), r)

        s, r = call("POST", "/api/delivery-addresses", "sale", {"address": "Không được phép"})
        check("Sale KHÔNG thêm được địa chỉ hộ khách", s == 403, f"HTTP {s}")

    # ---------------- Lịch sử đơn
    s, r = call("GET", f"/api/orders/consignments/{order_id}/timeline", "sale")
    check("Có lịch sử trạng thái đơn", s == 200 and len(r.get("data", [])) >= 6, r)

    # ---------------- Ước tính trước khi tạo đơn phải KHỚP hoá đơn sau khi tạo
    preview_matches_created_order()

    print(f"\n{len(PASSED)} passed, {len(FAILED)} failed")
    if FAILED:
        print("FAILED:", *FAILED, sep="\n  - ")
        sys.exit(1)


def preview_matches_created_order():
    """
    Điều kiện duy nhất phải giữ: SỐ Ở MÀN XÁC NHẬN = SỐ TRÊN HOÁ ĐƠN.

    Trước đây màn xác nhận tự cộng phí ở trình duyệt nên lệch với hệ thống (phí đóng thùng
    gỗ báo 60.000đ trong khi thu 25.000đ). Endpoint preview chạy chung một hàm tính với lúc
    tạo đơn; bài này so từng con số của hai bên, sai một đồng là hỏng.
    """
    body = {
        "route": "KR-VN", "shippingOption": "Express",
        "receiverName": "Preview " + RUN, "receiverPhone": "0900000000", "receiverAddress": "HCM",
        "defaultDestinationHandling": "DIRECT_DELIVERY",
        "items": [item(f"Preview A {RUN}", 2, 3), item(f"Preview B {RUN}", 1, 1.5)],
    }

    s, r = call("POST", "/api/orders/consignments/preview", "customer", body)
    pv = r.get("data", r)
    if not check("Khách xem trước được chi phí mà chưa tạo đơn", s == 200 and isinstance(pv, dict), r):
        return

    check("Xem trước KHÔNG sinh mã báo giá / mã đơn (chưa lưu gì)",
          str(pv.get("quotationId")) in ("00000000-0000-0000-0000-000000000000", "None", "")
          and str(pv.get("orderId")) in ("00000000-0000-0000-0000-000000000000", "None", ""), pv)

    s, r = call("POST", "/api/orders/consignments", "customer", body)
    created = r.get("data", r)
    if not check("Tạo đơn thật từ cùng payload", s in (200, 201) and created.get("orderId"), r):
        return

    s, r = get_order_quotation(created["orderId"])
    q = r.get("data", r)
    if not check("Đọc được báo giá của đơn vừa tạo", s == 200 and isinstance(q, dict), r):
        return

    for field in ("estimatedFreightCharge", "serviceFee", "domesticShippingFee",
                  "vat", "importTax", "taxAndDuty", "totalEstimatedCost",
                  "chargeableWeight", "volumetricWeight", "totalWeight", "totalVolume"):
        check(f"Xem trước khớp hoá đơn: {field}",
              pv.get(field) == q.get(field), (pv.get(field), q.get(field)))

    # Bảng kê kiểu hoá đơn: mọi dòng cộng lại phải BẰNG ĐÚNG tổng dự kiến.
    lines = pv.get("invoiceLines", [])
    check("Bảng kê có đủ dòng để in hoá đơn", len(lines) >= 3, lines)
    check("Không in dòng 0đ cho khách đọc",
          all(float(f.get("amount") or 0) > 0 for f in lines),
          [f.get("feeName") for f in lines if float(f.get("amount") or 0) <= 0])
    check("Cộng mọi dòng bảng kê = tổng dự kiến",
          round(sum(float(f.get("amount") or 0) for f in lines), 2)
          == round(float(pv.get("totalEstimatedCost") or 0), 2),
          (sum(float(f.get("amount") or 0) for f in lines), pv.get("totalEstimatedCost")))
    check("Mỗi dòng đều nói rõ thuộc nhóm nào",
          all(f.get("feeType") for f in lines), lines[:2])
    check("Cước quốc tế đứng đầu bảng kê",
          str(lines[0].get("feeType")).upper() == "MAIN_SERVICE", lines[0])
    check("Thuế xếp cuối bảng kê",
          str(lines[-1].get("feeType")).upper() == "TAX", lines[-1])

    # Từng nhóm dòng phải cộng đúng bằng ô tổng của nhóm đó — hoá đơn không được có
    # con số nào "rơi" ra ngoài bảng kê.
    def nhom(*loai):
        return round(sum(float(f.get("amount") or 0) for f in lines
                         if str(f.get("feeType")).upper() in loai), 2)

    check("Nhóm cước quốc tế khớp ô tổng",
          nhom("MAIN_SERVICE") == round(float(pv.get("estimatedFreightCharge") or 0), 2),
          (nhom("MAIN_SERVICE"), pv.get("estimatedFreightCharge")))
    check("Nhóm phí nội địa khớp ô tổng",
          nhom("DOMESTIC_FEE", "SERVICE_FEE") == round(float(pv.get("domesticShippingFee") or 0), 2),
          (nhom("DOMESTIC_FEE", "SERVICE_FEE"), pv.get("domesticShippingFee")))
    check("Nhóm dịch vụ và phụ phí khớp ô tổng",
          nhom("SURCHARGE", "PACKING_FEE") == round(float(pv.get("serviceFee") or 0), 2),
          (nhom("SURCHARGE", "PACKING_FEE"), pv.get("serviceFee")))
    check("Nhóm thuế khớp ô tổng",
          nhom("TAX") == round(float(pv.get("taxAndDuty") or 0), 2),
          (nhom("TAX"), pv.get("taxAndDuty")))

    # Màn báo giá (sau khi tạo đơn) phải in ĐÚNG tờ hoá đơn của màn xem trước — cùng dòng,
    # cùng thứ tự, cùng số. Lệch một dòng là khách thấy hai bảng kê cho một số tiền.
    q_lines = q.get("invoiceLines", [])

    def to_tuple(rows):
        return [(str(f.get("feeType")).upper(), f.get("feeName"),
                 round(float(f.get("amount") or 0), 2)) for f in rows]

    check("Báo giá của đơn cũng trả bảng kê hoá đơn", len(q_lines) == len(lines),
          (len(lines), len(q_lines)))
    check("Bảng kê màn báo giá giống hệt màn xem trước (dòng, thứ tự, số tiền)",
          to_tuple(q_lines) == to_tuple(lines),
          (to_tuple(lines)[:4], to_tuple(q_lines)[:4]))
    check("Báo giá không in dòng 0đ",
          all(float(f.get("amount") or 0) > 0 for f in q_lines),
          [f.get("feeName") for f in q_lines if float(f.get("amount") or 0) <= 0])
    check("Cộng bảng kê màn báo giá = tổng của chính nó",
          round(sum(float(f.get("amount") or 0) for f in q_lines), 2)
          == round(float(q.get("totalEstimatedCost") or 0), 2),
          (sum(float(f.get("amount") or 0) for f in q_lines), q.get("totalEstimatedCost")))
    check("Mỗi dòng báo giá đều có cách tính hoặc tên phí để khách đọc",
          all(f.get("feeName") for f in q_lines), q_lines[:2])

    pv_fees = sorted((f.get("feeName"), f.get("amount")) for f in pv.get("additionalFees", []))
    q_fees = sorted((f.get("feeName"), f.get("amount")) for f in q.get("additionalFees", []))
    check("Xem trước khớp hoá đơn: từng dòng phụ phí và phí đóng thùng", pv_fees == q_fees,
          (pv_fees[:4], q_fees[:4]))

    # Dữ liệu sai phải bị chặn NGAY ở bước xem trước, không đợi bấm tạo đơn.
    s, r = call("POST", "/api/orders/consignments/preview", "customer",
                {**body, "items": []})
    check("Xem trước cũng kiểm dữ liệu: không có mặt hàng thì chặn", s == 400, f"HTTP {s}")

    s, r = call("POST", "/api/orders/consignments/preview", "customer",
                {**body, "shippingOption": "KhongCoThat"})
    check("Xem trước cũng kiểm phương án vận chuyển", s == 400, f"HTTP {s}")

    s, r = call("POST", "/api/orders/consignments/preview", "sale", body)
    expect("Nhân viên không dùng endpoint xem trước của khách", s, r, 403)


if __name__ == "__main__":
    main()
