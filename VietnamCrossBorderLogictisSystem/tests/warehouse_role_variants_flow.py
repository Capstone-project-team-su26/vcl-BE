#!/usr/bin/env python3
"""
Kiểm thử tài khoản kho có vai trò viết khác chuẩn (`Warehouse`, `WarehouseTQ`, `WarehouseVN`,
`Warehouse Staff`, `Warehouse Staff VN`, `Warehouse Staff QT`, `Warehouse Staff Cam`...) được nhận
như `WarehouseStaff` ở mọi endpoint kho, trong khi:
  - tập vai trò của từng endpoint giữ nguyên (Staff/OM không lọt vào endpoint chỉ dành cho kho),
  - `WarehouseManager` và `Customer` vẫn bị chặn như trước,
  - tài khoản trống vùng vẫn bị chặn thao tác kho với câu "chưa khai vùng" (backend KHÔNG tự suy
    vùng từ tên vai trò).

Kịch bản tự tạo tài khoản thử qua API Admin (mật khẩu ngẫu nhiên, không in ra), cuối cùng khoá
các tài khoản thử — không đụng tài khoản dùng chung của các bộ kiểm thử khác.

    APP_ENV=/root/vcl-migration-deploy/app.env API=http://127.0.0.1:8088 python3 warehouse_role_variants_flow.py
"""
import json, secrets, sys, urllib.request, urllib.error, uuid

from wave1_consignment_flow import API, IDS, TOK, PASSED, FAILED, RUN, call, check, expect, token
from export_flow import KR, route_for_kr

CREATED = []
PASSWORDS = {}


def create_user(key, role, region=None):
    """Tạo tài khoản thử và phát token mang đúng chuỗi vai trò thô (như token đăng nhập)."""
    email = f"rv.{key.lower()}.{RUN.lower()}@vcl-test.example"
    password = secrets.token_urlsafe(18)
    s, r = call("POST", "/api/User", "admin", {
        "fullName": f"Kiểm thử vai trò kho {key} {RUN}", "email": email,
        "password": password, "phone": "0900000000", "role": role, "region": region})
    user_id = (r or {}).get("id")
    if not check(f"Tạo tài khoản thử {key} (role '{role}', vùng {region or 'trống'})", s == 201 and user_id, f"HTTP {s}: {r}"):
        sys.exit(1)
    CREATED.append(user_id)
    PASSWORDS[key] = (email, password)
    TOK[key] = token(user_id, role)
    return user_id


def login(key):
    """Đăng nhập thật; trả (status, body). Mật khẩu không in ra."""
    email, password = PASSWORDS[key]
    req = urllib.request.Request(API + "/api/Auth/login", method="POST",
                                 data=json.dumps({"email": email, "password": password}).encode())
    req.add_header("Content-Type", "application/json")
    try:
        with urllib.request.urlopen(req, timeout=60) as r:
            return r.status, json.loads(r.read().decode() or "{}")
    except urllib.error.HTTPError as e:
        try:
            return e.code, json.loads(e.read().decode() or "{}")
        except json.JSONDecodeError:
            return e.code, {}


def main():
    route_id = route_for_kr()
    check("Có tuyến xuất từ kho Seoul", bool(route_id))

    def eligible(who):
        return call("GET", f"/api/warehouse-release-requests/eligible-parcels?shippingRouteId={route_id}&includeBlocked=true", who)

    # Biến thể có vùng KR (đúng vùng kho Seoul) và biến thể trống vùng như trên production.
    with_region = {
        "rvStd": ("WarehouseStaff", "KR"),
        "rvWh": ("Warehouse", "KR"),
        "rvQT": ("Warehouse Staff QT", "KR"),
        "rvTQk": ("WarehouseTQ", "KR"),
    }
    no_region = {
        "rvTQ": ("WarehouseTQ", None),
        "rvVN": ("WarehouseVN", None),
        "rvWh0": ("Warehouse", None),
        "rvSVN": ("Warehouse Staff VN", None),
        "rvCam": ("Warehouse Staff Cam", None),
    }
    for key, (role, region) in {**with_region, **no_region}.items():
        create_user(key, role, region)
    create_user("rvMgr", "WarehouseManager", "KR")
    # API tạo tài khoản chuẩn hoá "Warehouse Staff" → "WarehouseStaff"; chuỗi có dấu cách vẫn tồn tại
    # trên production (lo@gmail.com...), nên phát token mang đúng chuỗi đó cho một tài khoản vùng KR.
    sp_id = create_user("rvSp", "WarehouseStaff", "KR")
    TOK["rvSp"] = token(sp_id, "Warehouse Staff")

    try:
        variants = list(with_region) + list(no_region) + ["rvSp"]

        # ---------------- Endpoint khai [Authorize(Roles=...WarehouseStaff...)] trước đây
        for key in variants:
            s, r = call("GET", "/api/parcels/pending-putaway?pageSize=5", key)
            expect(f"{key}: GET kiện chờ xếp kệ → 200", s, r, 200)
            s, r = call("GET", "/api/parcel-inspections", key)
            expect(f"{key}: GET biên bản kiểm đếm (attribute + service) → 200", s, r, 200)
            s, r = call("GET", f"/api/warehouses/{KR}/layout/status", key)
            expect(f"{key}: GET trạng thái sơ đồ kho (attribute + service) → 200", s, r, 200)
            s, r = call("GET", "/api/warehouse-inbound-requests/pending-orders", key)
            expect(f"{key}: GET đơn chờ nhập kho (service) → 200", s, r, 200)
            s, r = call("POST", f"/api/parcels/{uuid.uuid4()}/split", key, {"items": []})
            check(f"{key}: POST tách kiện (chỉ Admin,WarehouseStaff) không còn 403", s != 403, f"HTTP {s}: {r}")

        # ---------------- Thao tác kho gắn vùng: đúng vùng 200, trống vùng 403 "chưa khai vùng"
        for key in with_region:
            s, r = eligible(key)
            expect(f"{key} (vùng KR): lập phiếu xuất ở kho Seoul → 200", s, r, 200)
        s, r = eligible("rvSp")
        expect("rvSp ('Warehouse Staff', vùng KR): lập phiếu xuất ở kho Seoul → 200", s, r, 200)
        for key in no_region:
            s, r = eligible(key)
            expect(f"{key} (trống vùng): lập phiếu xuất → 403", s, r, 403)
            check(f"{key}: câu lỗi 'chưa khai vùng'", "chưa khai vùng" in (r.get("message") or ""), r)

        # ---------------- Không nới quyền cho vai trò khác
        for key, label in (("rvMgr", "WarehouseManager"), ("customer", "Customer")):
            for path in ("/api/parcels/pending-putaway?pageSize=5", "/api/parcel-inspections",
                         f"/api/warehouses/{KR}/layout/status"):
                s, r = call("GET", path, key)
                expect(f"{label}: GET {path.split('?')[0]} vẫn 403", s, r, 403)
            s, r = call("POST", f"/api/parcels/{uuid.uuid4()}/split", key, {"items": []})
            expect(f"{label}: POST tách kiện vẫn 403", s, r, 403)
        # (eligible-parcels chỉ kiểm phạm vi kho theo vùng, không kiểm vai trò — không đổi ở đây.)
        s, r = call("GET", "/api/warehouse-inbound-requests/pending-orders", "rvMgr")
        expect("WarehouseManager: đơn chờ nhập kho vẫn 403", s, r, 403)
        # Endpoint chỉ "Admin,WarehouseStaff": Operations Manager / Sale vẫn không lọt.
        for key in ("om1", "sale"):
            s, r = call("POST", f"/api/parcels/{uuid.uuid4()}/split", key, {"items": []})
            expect(f"{IDS[key]['role']}: POST tách kiện (Admin,WarehouseStaff) vẫn 403", s, r, 403)
        # Endpoint không có nhân viên kho: biến thể kho không được thêm quyền.
        s, r = call("GET", f"/api/warehouses/{KR}/staff", "rvWh")
        expect("Biến thể kho: GET nhân viên của kho (không dành cho kho) vẫn 403", s, r, 403)

        # ---------------- Đăng nhập thật (token do backend phát)
        s, r = login("rvSVN")
        check("Đăng nhập 'Warehouse Staff VN': token giữ vai trò thô, vùng trống",
              s == 200 and r.get("role") == "Warehouse Staff VN" and not r.get("region"), (s, {k: r.get(k) for k in ("role", "region")}))
        if s == 200:
            TOK["rvSVN_login"] = r["token"]
            s2, r2 = call("GET", "/api/parcels/pending-putaway?pageSize=5", "rvSVN_login")
            expect("Token đăng nhập 'Warehouse Staff VN': GET kiện chờ xếp kệ → 200", s2, r2, 200)
            s2, r2 = eligible("rvSVN_login")
            check("Token đăng nhập 'Warehouse Staff VN' trống vùng: thao tác kho → 403 'chưa khai vùng'",
                  s2 == 403 and "chưa khai vùng" in (r2.get("message") or ""), (s2, r2))
        s, r = login("rvWh")
        if check("Đăng nhập 'Warehouse' (vùng KR) thành công", s == 200 and r.get("token"), s):
            TOK["rvWh_login"] = r["token"]
            s2, r2 = eligible("rvWh_login")
            expect("Token đăng nhập 'Warehouse' vùng KR: lập phiếu xuất ở Seoul → 200", s2, r2, 200)

        # ---------------- /api/users/me/warehouses: app kho dựa vào để chọn giao diện
        s, r = call("GET", "/api/users/me/warehouses", "rvVN")
        d = (r or {}).get("data") or {}
        check("me/warehouses của 'WarehouseVN': role thô, vùng trống, là vai trò kho",
              s == 200 and d.get("role") == "WarehouseVN" and not d.get("region") and d.get("isWarehouseRole") is True, r)
        s, r = call("GET", "/api/users/me/warehouses", "rvMgr")
        d = (r or {}).get("data") or {}
        check("me/warehouses của 'WarehouseManager': không phải vai trò nhân viên kho",
              s == 200 and d.get("isWarehouseRole") is False, r)
    finally:
        # ---------------- Dọn dữ liệu thử
        locked = 0
        for uid in CREATED:
            s, _ = call("PUT", f"/api/User/{uid}/lock", "admin")
            locked += s == 200
        check("Dọn: khoá toàn bộ tài khoản thử", locked == len(CREATED), f"{locked}/{len(CREATED)}")

    print(f"\n{len(PASSED)} passed, {len(FAILED)} failed")
    for name in FAILED:
        print("  FAILED:", name)
    sys.exit(1 if FAILED else 0)


if __name__ == "__main__":
    main()
