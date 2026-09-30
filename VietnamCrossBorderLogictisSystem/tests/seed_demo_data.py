#!/usr/bin/env python3
"""
Tạo tài khoản demo cho từng role và dữ liệu mẫu luồng ký gửi (từ khách tạo đơn tới xếp kệ),
để người ghép API có sẵn đơn ở mọi trạng thái mà thử.

- Tài khoản tạo/cập nhật thẳng trong DB (mật khẩu băm PBKDF2 đúng như PasswordHelper). Mật khẩu
  đọc từ stdin dạng JSON {"email": "password"} — không ghi vào file nào.
- Dữ liệu đơn tạo bằng API thật, đăng nhập bằng chính tài khoản demo, nên đi đúng mọi luật nghiệp vụ.
- Chạy lại: tài khoản được đặt lại mật khẩu; dữ liệu đơn bỏ qua nếu khách demo đã có đơn [DEMO]
  (thêm --force để tạo thêm một bộ).

    python3 seed_demo_data.py --api http://127.0.0.1:8085 --db AppDb < passwords.json
"""
import argparse, base64, hashlib, json, os, subprocess, sys, time, urllib.error, urllib.request, uuid

DOMAIN = "vcl-demo.example"
CN_WAREHOUSE = "5F8A0B1C-2D3E-4F5A-6B7C-8D9E0F1A2B3C"
PRODUCT_TYPE = "11111111-0000-0000-0000-000000000002"   # Quần áo
PRODUCT_TYPE_2 = "11111111-0000-0000-0000-000000000004" # Phụ kiện
BOX_MEDIUM = "99999999-2222-2222-2222-222222222222"

ACCOUNTS = [
    # email, họ tên, role, user_type, vùng, sđt
    ("demo.admin", "Demo Admin", "Admin", "Employee", None, "0900000001"),
    ("demo.sale", "Demo Sale", "Sale", "Employee", None, "0900000002"),
    ("demo.quanlykho", "Demo Quản lý kho", "OperationsManager", "Employee", None, "0900000003"),
    ("demo.kho.cn", "Demo Nhân viên kho Quảng Châu", "WarehouseStaff", "Employee", "CN", "0900000004"),
    ("demo.kho.vn", "Demo Nhân viên kho HCM", "WarehouseStaff", "Employee", "VN", "0900000005"),
    ("demo.giaohang", "Demo Giao hàng", "Delivery", "Employee", None, "0900000006"),
    ("demo.khach1", "Nguyễn Văn Demo", "Customer", "Customer", None, "0911000001"),
    ("demo.khach2", "Trần Thị Demo", "Customer", "Customer", None, "0911000002"),
]


def email(local):
    return f"{local}@{DOMAIN}"


def hash_password(password):
    salt = os.urandom(16)
    key = hashlib.pbkdf2_hmac("sha256", password.encode(), salt, 10000, 32)
    return f"10000.{base64.b64encode(salt).decode()}.{base64.b64encode(key).decode()}"


def sql(db, statement):
    sa = subprocess.run(
        "docker inspect sqlserver --format '{{range .Config.Env}}{{println .}}{{end}}' | grep SA_PASSWORD | cut -d= -f2-",
        shell=True, capture_output=True, text=True, check=True).stdout.strip()
    out = subprocess.run(
        ["docker", "exec", "sqlserver", "/opt/mssql-tools18/bin/sqlcmd", "-S", "localhost", "-U", "sa", "-P", sa,
         "-C", "-b", "-d", db, "-W", "-h", "-1", "-Q", "SET NOCOUNT ON; " + statement],
        capture_output=True, text=True)
    if out.returncode != 0:
        raise RuntimeError(out.stdout + out.stderr)
    return out.stdout.strip()


def q(value):
    return "NULL" if value is None else "N'" + str(value).replace("'", "''") + "'"


def upsert_accounts(db, passwords):
    for local, name, role, user_type, region, phone in ACCOUNTS:
        mail = email(local)
        pw = passwords[mail]
        hashed = hash_password(pw)
        user_id = sql(db, f"SELECT CAST(id AS nvarchar(36)) FROM USERS WHERE email = {q(mail)}")
        if user_id:
            sql(db, f"UPDATE USERS SET password = {q(hashed)}, full_name = {q(name)}, role = {q(role)}, user_type = {q(user_type)}, "
                    f"status = N'Active', is_email_verified = 1, region = {q(region)}, phone = {q(phone)} WHERE id = {q(user_id)}")
        else:
            user_id = str(uuid.uuid4())
            sql(db, "INSERT INTO USERS (id, full_name, email, phone, password, role, user_type, status, created_at, "
                    "ResetPasswordFailedAttempts, is_email_verified, email_verification_failed_attempts, region) VALUES "
                    f"({q(user_id)}, {q(name)}, {q(mail)}, {q(phone)}, {q(hashed)}, {q(role)}, {q(user_type)}, N'Active', "
                    f"SYSUTCDATETIME(), 0, 1, 0, {q(region)})")
        if role == "Customer":
            exists = sql(db, f"SELECT COUNT(*) FROM CUSTOMERS WHERE user_id = {q(user_id)}")
            if exists == "0":
                code = "CUS-DEMO-" + local.split(".")[-1].upper()
                sql(db, "INSERT INTO CUSTOMERS (id, user_id, customer_code, full_name, email, phone, country, address, customer_type, status, created_at) VALUES "
                        f"({q(str(uuid.uuid4()))}, {q(user_id)}, {q(code)}, {q(name)}, {q(mail)}, {q(phone)}, N'VN', "
                        f"N'12 Lê Lợi, Quận 1, TP.HCM', N'Individual', N'Active', SYSUTCDATETIME())")
        print(f"  tài khoản {mail:<34} {role}{' · ' + region if region else ''}")


class Api:
    def __init__(self, base):
        self.base = base
        self.tokens = {}

    def login(self, local, password):
        s, r = self.call("POST", "/api/Auth/login", None, {"email": email(local), "password": password})
        if s != 200:
            raise RuntimeError(f"Đăng nhập {local} lỗi {s}: {r}")
        self.tokens[local] = r["token"]

    def call(self, method, path, who, body=None):
        data = None if body is None else json.dumps(body).encode()
        req = urllib.request.Request(self.base + path, data=data, method=method)
        req.add_header("Content-Type", "application/json")
        if who:
            req.add_header("Authorization", "Bearer " + self.tokens[who])
        try:
            with urllib.request.urlopen(req, timeout=120) as resp:
                raw = resp.read().decode() or "{}"
                return resp.status, json.loads(raw)
        except urllib.error.HTTPError as e:
            raw = e.read().decode() or "{}"
            try:
                return e.code, json.loads(raw)
            except json.JSONDecodeError:
                return e.code, {"raw": raw}

    def upload(self, who, entity_type, entity_id, document_type, name="anh.png"):
        """
        Đính kèm một file nhỏ qua /api/attachments (multipart).

        Cần cho các bước BẮT BUỘC có ảnh: xếp kiện vào ô kệ, duyệt phiếu xuất kho. Dựng
        dữ liệu mẫu cũng phải đi đúng luật như người dùng thật, nếu không bộ dữ liệu sẽ
        khác hệ thống thật ở đúng chỗ đang được kiểm.
        """
        boundary = "----vclseed" + uuid.uuid4().hex
        content = b"\x89PNG\r\n\x1a\n seed\n"
        parts = []
        for key, value in (("entityType", entity_type), ("entityId", str(entity_id)),
                           ("documentType", document_type), ("note", "seed")):
            parts.append(f"--{boundary}\r\nContent-Disposition: form-data; name=\"{key}\"\r\n\r\n{value}\r\n".encode())
        parts.append(f"--{boundary}\r\nContent-Disposition: form-data; name=\"file\"; filename=\"{name}\"\r\n"
                     f"Content-Type: image/png\r\n\r\n".encode() + content + b"\r\n")
        parts.append(f"--{boundary}--\r\n".encode())

        req = urllib.request.Request(self.base + "/api/attachments", data=b"".join(parts), method="POST")
        req.add_header("Content-Type", f"multipart/form-data; boundary={boundary}")
        req.add_header("Authorization", "Bearer " + self.tokens[who])
        try:
            with urllib.request.urlopen(req) as resp:
                return resp.status
        except urllib.error.HTTPError as e:
            return e.code

    def ok(self, method, path, who, body=None, expect=(200, 201)):
        s, r = self.call(method, path, who, body)
        if s not in expect:
            raise RuntimeError(f"{method} {path} ({who}) -> {s}: {json.dumps(r, ensure_ascii=False)[:500]}")
        return r


def seed_orders(api, run):
    s, r = api.call("GET", "/api/orders/consignments/item-services", None)
    services = {x["code"]: x["pricingRuleId"] for x in r["data"]}
    inspection, insurance, repack = services["SUR_INSPECTION"], services["SUR_INSURANCE_3PERCENT"], services["CARTON_REPACK"]

    def item(name, qty=1, weight=1.0, value=300000, svc=(), ptype=PRODUCT_TYPE, dims=(20, 15, 10)):
        return {"productName": name, "productType": ptype, "quantity": qty, "weight": weight,
                "length": dims[0], "width": dims[1], "height": dims[2], "declaredValue": value,
                "referenceUrls": [], "packageConfigurationId": BOX_MEDIUM,
                "services": [{"pricingRuleId": x} for x in svc]}

    def create(customer, label, items):
        body = {"route": "CN-VN", "shippingOption": "Express", "receiverName": f"{label}",
                "receiverPhone": "0911000001", "receiverAddress": "12 Lê Lợi, Quận 1, TP.HCM",
                "defaultDestinationHandling": "DIRECT_DELIVERY", "note": f"[DEMO] {label}", "items": items}
        return api.ok("POST", "/api/orders/consignments", customer, body)["data"]

    def quote(order_id, fees=None, reason=None):
        body = {"warehouseId": CN_WAREHOUSE, "salesNote": "[DEMO] Báo giá theo cân nặng khai báo"}
        if fees:
            body["quotation"] = {"additionalFees": fees}
        if reason:
            body["overrideReason"] = reason
        return api.ok("POST", f"/api/orders/{order_id}/quotation/send", "demo.sale", body)

    def quotation(order_id, who):
        return api.ok("GET", f"/api/orders/{order_id}/quotation", who)["data"]

    def pay_offline(order_id, customer):
        q_ = quotation(order_id, customer)
        return api.ok("PUT", f"/api/quotations/{q_['quotationId']}/confirm-and-pay", customer, {"paymentMethod": "OFFLINE"})["data"]

    def approve_payment(pay, code):
        api.ok("PUT", f"/api/admin/finance/transactions/{pay['paymentId']}/approve", "demo.admin",
               {"transactionCode": code, "receivedAmount": pay["amount"], "note": "[DEMO] Đã thấy tiền trong sao kê"})

    def note(order_id):
        return api.ok("POST", "/api/warehouse-receiving-notes", "demo.sale",
                      {"consignmentOrderId": order_id, "warehouseId": CN_WAREHOUSE, "warehouseNote": "[DEMO] Khách hẹn mang hàng tới"})["receivingNote"]

    def approve_note(note_id):
        api.ok("PUT", f"/api/warehouse-receiving-notes/{note_id}/status", "demo.quanlykho", {"status": "APPROVED", "reason": "[DEMO] Đồng ý nhận hàng"})

    def expected(note_id):
        return api.ok("GET", f"/api/warehouse-receiving-notes/{note_id}", "demo.quanlykho")["data"]["expectedItems"]

    def deposit_paid(customer, label, items):
        o = create(customer, label, items)
        quote(o["orderId"])
        approve_payment(pay_offline(o["orderId"], customer), f"FT-DEMO-{run}-{len(created)}")
        return o

    created = []

    def record(o, state, who, hint):
        created.append((o["consignmentCode"], o["orderId"], state, who, hint))

    # 1. Khách vừa tạo đơn
    o = create("demo.khach1", "Đơn 01 · vừa tạo", [item("Áo khoác gió", 2, 1.2, 450000, (inspection, insurance)), item("Túi đeo chéo", 1, 0.6, 250000, (repack,), PRODUCT_TYPE_2)])
    record(o, "PENDING_REVIEW", "demo.sale", "Sale báo giá / yêu cầu bổ sung")

    # 2. Sale yêu cầu bổ sung thông tin
    o = create("demo.khach1", "Đơn 02 · cần bổ sung", [item("Giày thể thao", 1, 1.5, 900000)])
    api.ok("PUT", f"/api/orders/consignments/{o['orderId']}/status", "demo.sale", {"status": "NEED_MORE_INFO", "rejectionReason": "[DEMO] Thiếu ảnh sản phẩm"})
    record(o, "NEED_MORE_INFO", "demo.sale", "Sale báo giá sau khi khách bổ sung")

    # 3. Đã gửi báo giá, chờ khách
    o = create("demo.khach1", "Đơn 03 · chờ khách xác nhận báo giá", [item("Váy công sở", 2, 0.8, 600000, (inspection,))])
    quote(o["orderId"])
    record(o, "QUOTATION_SENT", "demo.khach1", "Khách xem báo giá, xác nhận & trả cọc hoặc từ chối")

    # 4. Giá ngoại lệ chờ Admin duyệt
    o = create("demo.khach1", "Đơn 04 · giá ngoại lệ chờ Admin", [item("Balo du lịch", 1, 1.4, 700000, (inspection,), PRODUCT_TYPE_2)])
    first_item = o["quotation"]["items"][0]["id"]
    quote(o["orderId"], fees=[{"feeId": inspection, "orderItemId": first_item, "amount": 5000, "enabled": True}], reason="[DEMO] Khách quen giảm phí kiểm hàng")
    record(o, "PENDING_PRICE_APPROVAL", "demo.admin", "Admin duyệt/từ chối giá ngoại lệ")

    # 5. Khách từ chối báo giá
    o = create("demo.khach1", "Đơn 05 · khách từ chối báo giá", [item("Mũ lưỡi trai", 3, 0.3, 150000, (), PRODUCT_TYPE_2)])
    quote(o["orderId"])
    q_ = quotation(o["orderId"], "demo.khach1")
    api.ok("PUT", f"/api/quotations/{q_['quotationId']}/reject", "demo.khach1", {"rejectionReason": "[DEMO] Giá cao hơn dự kiến"})
    record(o, "QUOTATION_REJECTED", "demo.sale", "Sale báo giá lại")

    # 6. Khách xác nhận, chờ Admin xác nhận tiền chuyển khoản
    o = create("demo.khach1", "Đơn 06 · chờ Admin xác nhận tiền cọc", [item("Áo len", 2, 0.9, 500000, (insurance,))])
    quote(o["orderId"])
    pay_offline(o["orderId"], "demo.khach1")
    record(o, "WAITING_DEPOSIT", "demo.admin", "Admin duyệt giao dịch chờ (pending-approval)")

    # 7. Đã cọc, chờ Sale lập phiếu nhập kho
    o = deposit_paid("demo.khach1", "Đơn 07 · đã cọc, chờ lập phiếu nhập kho", [item("Quần jean", 2, 1.0, 400000)])
    record(o, "DEPOSIT_PAID", "demo.sale", "Sale lập phiếu nhập kho (action-queue)")

    # 8. Phiếu chờ quản lý kho duyệt
    o = deposit_paid("demo.khach1", "Đơn 08 · phiếu chờ quản lý kho duyệt", [item("Áo sơ mi", 3, 0.9, 350000, (inspection,))])
    note(o["orderId"])
    record(o, "DEPOSIT_PAID · phiếu PENDING_APPROVAL", "demo.quanlykho", "Quản lý kho duyệt/từ chối phiếu")

    # 9. Phiếu đã duyệt, chờ khách mang hàng tới
    o = deposit_paid("demo.khach1", "Đơn 09 · phiếu đã duyệt, chờ khách mang hàng", [item("Đồng hồ", 1, 0.4, 1500000, (insurance,), PRODUCT_TYPE_2), item("Ví da", 1, 0.3, 400000, (), PRODUCT_TYPE_2)])
    n = note(o["orderId"]); approve_note(n["id"])
    record(o, f"phiếu ACTIVE · {n['receivingNoteCode']}", "demo.kho.cn", "Kho quét mã phiếu, check-in")

    # 10. Nhận một phần
    o = deposit_paid("demo.khach1", "Đơn 10 · kho mới nhận 1/2 kiện", [item("Áo thun", 2, 0.8, 200000), item("Quần short", 2, 0.7, 200000)])
    n = note(o["orderId"]); approve_note(n["id"])
    first = expected(n["id"])[0]
    api.ok("POST", "/api/parcels/check-in", "demo.kho.cn", {"shippingCode": n["receivingNoteCode"], "packages": [{"orderItemId": first["orderItemId"], "actualWeight": 0.85, "length": 20, "width": 15, "height": 10}]})
    record(o, f"phiếu PARTIALLY_RECEIVED · {n['receivingNoteCode']}", "demo.kho.cn", "Check-in kiện còn lại")

    # 11. Nhận lệch số lượng, chờ quản lý kho xem
    o = deposit_paid("demo.khach1", "Đơn 11 · nhận lệch, chờ quản lý kho xem", [item("Khăn choàng", 3, 0.6, 300000)])
    n = note(o["orderId"]); approve_note(n["id"])
    first = expected(n["id"])[0]
    api.ok("POST", f"/api/warehouse-receiving-notes/{n['id']}/confirm-receipt", "demo.kho.cn", {
        "warehouseNote": "[DEMO] Khách mang thiếu 1 khăn",
        "items": [{"orderItemId": first["orderItemId"], "actualQuantity": 2, "actualWeight": 0.4, "note": "Thiếu 1"}],
        "parcels": [{"orderItemId": first["orderItemId"], "actualWeight": 0.45, "length": 20, "width": 15, "height": 8}]})
    record(o, f"phiếu RECEIVED (requiresReview) · {n['receivingNoteCode']}", "demo.quanlykho", "Quản lý kho duyệt/từ chối biên bản lệch")

    # 12. Nhận đủ, chờ xếp kệ
    o = deposit_paid("demo.khach1", "Đơn 12 · nhận đủ, chờ xếp kệ", [item("Áo hoodie", 1, 1.1, 550000, (inspection,)), item("Dép sandal", 1, 0.7, 250000, (), PRODUCT_TYPE_2)])
    n = note(o["orderId"]); approve_note(n["id"])
    packages = [{"orderItemId": e["orderItemId"], "actualWeight": 1.0, "length": 22, "width": 16, "height": 10} for e in expected(n["id"])]
    api.ok("POST", "/api/parcels/check-in", "demo.kho.cn", {"shippingCode": n["receivingNoteCode"], "packages": packages})
    record(o, f"kiện RECEIVED chờ xếp kệ · {n['receivingNoteCode']}", "demo.kho.cn", "Xếp kệ (quét mã ô BIN-A1-01 / BIN-A1-02)")

    # 13. Đã xếp kệ
    o = deposit_paid("demo.khach1", "Đơn 13 · đã xếp kệ", [item("Áo vest", 1, 1.3, 800000, (insurance,)), item("Cà vạt", 2, 0.2, 150000, (), PRODUCT_TYPE_2)])
    n = note(o["orderId"]); approve_note(n["id"])
    packages = [{"orderItemId": e["orderItemId"], "actualWeight": 1.0, "length": 20, "width": 15, "height": 10} for e in expected(n["id"])]
    r = api.ok("POST", "/api/parcels/check-in", "demo.kho.cn", {"shippingCode": n["receivingNoteCode"], "packages": packages})["data"]
    for p in (r.get("parcels") or [r]):
        # Xếp kệ đòi ảnh kiện trong ô — dựng dữ liệu mẫu cũng phải đi đúng luật đó.
        api.upload("demo.kho.cn", "PARCEL", p["parcelId"], "PUT_AWAY_PROOF", "anh-o-ke.png")
        api.ok("PUT", f"/api/parcels/{p['parcelId']}/put-away", "demo.kho.cn", {"warehouseId": CN_WAREHOUSE, "binCode": "BIN-A1-01"})
    record(o, "STORED · WAREHOUSE_RECEIVED", "demo.kho.cn", "Xem tồn kho, chuyển ô, tách/gộp kiện")

    # 14. Khách huỷ
    o = create("demo.khach1", "Đơn 14 · khách đã huỷ", [item("Kính mát", 1, 0.2, 350000, (), PRODUCT_TYPE_2)])
    api.ok("PUT", f"/api/orders/consignments/{o['orderId']}/cancel", "demo.khach1", {"cancelReason": "[DEMO] Không gửi nữa"})
    record(o, "CANCELLED", "demo.khach1", "Chỉ xem")

    # 15. Khách thứ hai (kiểm quyền: khách 1 không xem được đơn này)
    o = create("demo.khach2", "Đơn 15 · của khách 2", [item("Áo dài", 1, 0.9, 1200000, (inspection, insurance))])
    quote(o["orderId"])
    record(o, "QUOTATION_SENT", "demo.khach2", "Khách 2 xác nhận; khách 1 gọi thì 403")

    return created


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--api", required=True)
    ap.add_argument("--db", required=True)
    ap.add_argument("--force", action="store_true")
    args = ap.parse_args()

    passwords = json.load(sys.stdin)
    print("Tài khoản:")
    upsert_accounts(args.db, passwords)

    api = Api(args.api)
    for local, *_ in ACCOUNTS:
        api.login(local, passwords[email(local)])

    s, existing = api.call("GET", "/api/orders/consignments?pageSize=100", "demo.khach1")
    has_demo = any("[DEMO]" in (x.get("note") or "") or (x.get("receiverName") or "").startswith("Đơn ")
                   for x in (existing.get("data", {}).get("items") or []))
    if has_demo and not args.force:
        print("\nKhách demo đã có đơn mẫu — bỏ qua tạo dữ liệu (dùng --force để tạo thêm).")
        return

    run = time.strftime("%m%d%H%M%S")
    print("\nĐơn mẫu:")
    for code, order_id, state, who, hint in seed_orders(api, run):
        print(f"  {code} | {order_id} | {state} | {who} | {hint}")


if __name__ == "__main__":
    main()
