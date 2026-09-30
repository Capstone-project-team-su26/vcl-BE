#!/usr/bin/env python3
"""
Kiểm thử Admin gán nhân viên kho vào từng kho cụ thể (bảng USER_WAREHOUSES) trên môi trường thử:
gán / đổi / bỏ gán, nhân viên có gán chỉ thao tác ở kho được gán, nhân viên chưa gán vẫn giới hạn
theo vùng như cũ, gán khác vùng bị 400, không phải Admin thì không gán được, danh sách kho/tồn kho
của nhân viên được lọc theo kho được gán.

Kịch bản tự tạo 3 tài khoản nhân viên kho và 2 kho thử (vùng KR), cuối cùng bỏ gán, xoá kho thử và
khoá các tài khoản thử — không đụng tài khoản "staff" dùng chung của các bộ kiểm thử khác.

    APP_ENV=/root/vcl-migration-deploy/app.env API=http://127.0.0.1:8088 python3 warehouse_assignment_flow.py
"""
import secrets, sys, uuid

from wave1_consignment_flow import IDS, TOK, PASSED, FAILED, RUN, call, check, expect, token
from export_flow import KR, route_for_kr

CN = IDS["warehouseCn"]


def create_staff(key, region=None, role="WarehouseStaff"):
    """Tạo tài khoản nhân viên kho thử qua API Admin; mật khẩu ngẫu nhiên, không in ra."""
    email = f"uw.{key.lower()}.{RUN.lower()}@vcl-test.example"
    s, r = call("POST", "/api/User", "admin", {
        "fullName": f"Kiểm thử gán kho {key} {RUN}", "email": email,
        "password": secrets.token_urlsafe(18), "phone": "0900000000",
        "role": role, "region": region})
    user_id = (r or {}).get("id")
    if not check(f"Tạo tài khoản thử {key}", s == 201 and user_id, f"HTTP {s}: {r}"):
        sys.exit(1)
    TOK[key] = token(user_id, role)
    return user_id


def create_warehouse(suffix, region="KR", active=True):
    s, r = call("POST", "/api/warehouses", "admin", {
        "name": f"Kho thử gán {suffix} {RUN}", "code": f"UW_{suffix}_{RUN}", "address": "Seoul (kiểm thử)",
        "region": region, "warehouseType": "ORIGIN", "isActive": active})
    if not check(f"Tạo kho thử {suffix}", s == 201 and r.get("id"), f"HTTP {s}: {r}"):
        sys.exit(1)
    return r["id"], r["name"]


def assign(user_id, warehouse_ids, who="admin", note=None):
    return call("PUT", f"/api/users/{user_id}/warehouses", who, {"warehouseIds": warehouse_ids, "note": note})


def ids_of(items, key="id"):
    return {str(x.get(key, "")).lower() for x in items or []}


def active_ids(who):
    s, r = call("GET", "/api/warehouses/active", who)
    return s, ids_of(r.get("items", []))


def eligible(who, route_id):
    return call("GET", f"/api/warehouse-release-requests/eligible-parcels?shippingRouteId={route_id}&includeBlocked=true", who)


def set_profile_region(who, region):
    s, profile = call("GET", "/api/User/profile", who)
    return call("PUT", "/api/User/profile", who, {
        "fullName": (profile or {}).get("fullName") or "Kiểm thử", "phone": (profile or {}).get("phone") or "0900000000",
        "region": region})


def main():
    kr = KR.lower()
    route_id = route_for_kr()
    check("Có tuyến xuất từ kho Seoul", bool(route_id))

    kr2, kr2_name = create_warehouse("KR2")
    kr3_inactive, _ = create_warehouse("KR3", active=False)
    kr2 = kr2.lower()

    a = create_staff("uwA")                 # chưa có vùng — sẽ được đặt vùng theo kho
    b = create_staff("uwB", region="KR")    # có vùng, không bao giờ được gán — hành vi cũ
    c = create_staff("uwC")                 # không vùng, không gán — hành vi cũ (bị chặn)

    try:
        # ---------------- Quyền gọi API gán
        for who in ("om1", "sale", "staff", "uwA"):
            s, r = assign(a, [KR], who=who)
            expect(f"{who} không gán kho được (403)", s, r, 403)

        s, r = call("GET", f"/api/users/{a}/warehouses", "uwB")
        expect("Nhân viên kho không xem được kho gán của người khác", s, r, 403)
        s, r = call("GET", f"/api/warehouses/{KR}/staff", "uwB")
        expect("Nhân viên kho không xem danh sách nhân viên của kho", s, r, 403)

        # ---------------- Kiểm tra hợp lệ
        s, r = assign(a, [KR, CN])
        expect("Gán hai kho khác vùng bị 400", s, r, 400)
        check("Câu lỗi khác vùng nêu 'cùng một vùng'", "cùng một vùng" in (r.get("message") or ""), r)

        s, r = assign(a, [kr3_inactive])
        expect("Gán vào kho ngừng hoạt động bị 400", s, r, 400)

        s, r = assign(IDS["sale"]["id"], [KR])
        expect("Gán kho cho tài khoản Sale bị 400", s, r, 400)

        s, r = assign(a, [str(uuid.uuid4())])
        expect("Gán kho không tồn tại bị 404", s, r, 404)

        s, r = call("GET", f"/api/users/{a}/warehouses", "admin")
        check("Sau các lần lỗi, tài khoản A vẫn chưa gán và chưa có vùng",
              s == 200 and r["data"]["warehouses"] == [] and not r["data"]["region"], r)

        # ---------------- Chưa gán, chưa vùng: như cũ (bị chặn theo vùng)
        s, r = eligible("uwA", route_id)
        expect("A chưa gán, chưa vùng → 403 như cũ", s, r, 403)
        check("Câu lỗi 'chưa khai vùng'", "chưa khai vùng" in (r.get("message") or ""), r)

        # ---------------- Gán A vào kho Seoul: tự đặt vùng KR
        s, r = assign(a, [KR], note="Ca sáng kho Seoul")
        expect("Admin gán A vào kho Seoul", s, r, 200)
        d = r.get("data", {})
        check("Tài khoản trống vùng được đặt vùng = KR", d.get("regionChanged") is True and d.get("region") == "KR"
              and d.get("previousRegion") in (None, ""), d)
        check("Phản hồi có kho, ghi chú, người gán", len(d.get("warehouses", [])) == 1
              and d["warehouses"][0]["warehouseId"].lower() == kr and d["warehouses"][0]["note"] == "Ca sáng kho Seoul"
              and d["warehouses"][0]["assignedByName"] != "" and d.get("restricted") is True, d)

        s, r = call("GET", f"/api/users/{a}/warehouses", "om1")
        expect("Operations Manager xem được kho gán của nhân viên", s, r, 200)

        s, r = call("GET", "/api/users/me/warehouses", "uwA")
        check("A tự xem kho phụ trách (restricted=true)", s == 200 and r["data"]["restricted"] is True
              and ids_of(r["data"]["warehouses"], "warehouseId") == {kr}, r)
        s, r = call("GET", "/api/users/me/warehouses", "uwB")
        check("B chưa gán → restricted=false", s == 200 and r["data"]["restricted"] is False, r)

        s, r = call("GET", f"/api/warehouses/{KR}/staff", "om1")
        check("Danh sách nhân viên của kho Seoul có A", s == 200 and a.lower() in ids_of(r["data"]["staff"], "userId"), r)

        s, r = call("GET", "/api/User", "admin")
        row = next((u for u in (r if isinstance(r, list) else []) if str(u.get("id")).lower() == a.lower()), None)
        check("Danh sách người dùng có cột kho được gán", row is not None
              and ids_of(row.get("assignedWarehouses"), "warehouseId") == {kr}, row)

        s, ids = active_ids("uwA")
        check("Danh sách kho đang hoạt động của A chỉ còn kho Seoul", s == 200 and ids == {kr}, ids)

        s, r = eligible("uwA", route_id)
        expect("A thao tác được ở kho được gán (Seoul)", s, r, 200)

        s, r = call("GET", "/api/inventories", "uwA")
        items = r.get("items", [])
        check("Tồn kho của A chỉ gồm kho được gán", s == 200 and all(str(i.get("warehouseId", "")).lower() == kr for i in items), s)

        s, r = call("GET", "/api/parcels/pending-putaway?pageSize=200", "uwA")
        rows = (r.get("data") or {}).get("items") or (r.get("data") or {}).get("data") or []
        check("Kiện chờ xếp kệ của A chỉ gồm kho Seoul", s == 200 and all(
            (str(p.get("warehouseId") or "").lower() == kr) or (p.get("warehouseId") is None and p.get("warehouseRegion") == "KR")
            for p in rows), s)

        # ---------------- Đổi sang kho KR2 (cùng vùng): kho Seoul bị chặn
        s, r = assign(a, [kr2], note="Chuyển sang kho thử")
        expect("Admin đổi kho của A sang KR2", s, r, 200)
        check("Đổi kho cùng vùng không đổi vùng tài khoản", r["data"]["regionChanged"] is False and r["data"]["region"] == "KR", r)

        s, r = eligible("uwA", route_id)
        expect("A (gán KR2) bị 403 khi lập phiếu ở kho Seoul", s, r, 403)
        msg = r.get("message") or ""
        check("Câu 403 nêu tên kho được gán và kho bị chặn", kr2_name in msg and "Seoul" in msg, msg)

        s, r = call("PUT", f"/api/parcels/{uuid.uuid4()}/put-away", "uwA", {"warehouseId": KR, "binCode": "BIN-X"})
        expect("A (gán KR2) bị 403 khi xếp kệ ở kho Seoul", s, r, 403)
        s, r = call("PUT", f"/api/parcels/{uuid.uuid4()}/put-away", "uwA", {"warehouseId": kr2, "binCode": "BIN-X"})
        check("A xếp kệ ở kho được gán không bị chặn quyền (lỗi nghiệp vụ khác 403)", s != 403, f"HTTP {s}: {r}")

        s, r = call("GET", f"/api/inventories?warehouseId={KR}", "uwA")
        expect("A hỏi tồn kho kho Seoul bị 403", s, r, 403)
        s, r = call("GET", "/api/inventories", "uwA")
        check("Tồn kho của A (gán KR2) không còn hàng kho Seoul",
              s == 200 and all(str(i.get("warehouseId", "")).lower() == kr2 for i in r.get("items", [])), s)

        s, ids = active_ids("uwA")
        check("Danh sách kho của A chỉ còn KR2", ids == {kr2}, ids)

        # ---------------- Vai trò/vùng phải khớp với kho được gán
        s, r = assign(a, [CN])
        expect("Gán A (vùng KR) vào kho Trung Quốc bị 400", s, r, 400)
        check("Câu lỗi hướng dẫn đổi vùng trước", "đổi vùng" in (r.get("message") or ""), r)

        s, r = call("PUT", f"/api/User/{a}/role", "admin", {"role": "Sale"})
        expect("Không đổi A sang Sale khi còn gán kho", s, r, 400)
        s, r = call("PUT", f"/api/User/{a}/role", "admin", {"role": "WarehouseStaff", "region": "JP"})
        expect("Không đổi vùng A sang JP khi còn gán kho vùng KR", s, r, 400)
        s, r = set_profile_region("uwA", "JP")
        expect("Nhân viên đã gán kho không tự đổi vùng qua hồ sơ", s, r, 400)

        s, r = call("DELETE", f"/api/warehouses/{kr2}", "admin")
        expect("Không xoá được kho đang có nhân viên được gán", s, r, 400)

        # ---------------- Bỏ gán: quay về giới hạn theo vùng (KR)
        s, r = assign(a, [], note="Bỏ gán")
        expect("Admin bỏ gán A", s, r, 200)
        check("Bỏ gán trả danh sách rỗng, giữ vùng KR", r["data"]["warehouses"] == [] and r["data"]["region"] == "KR", r)
        s, r = eligible("uwA", route_id)
        expect("A bỏ gán → theo vùng KR, lập phiếu ở Seoul được", s, r, 200)
        s, ids = active_ids("uwA")
        check("A bỏ gán → danh sách kho không còn bị khoá theo kho gán", {kr, kr2} <= ids, ids)
        s, r = call("GET", "/api/users/me/warehouses", "uwA")
        check("A bỏ gán → restricted=false", s == 200 and r["data"]["restricted"] is False, r)

        # ---------------- Nhân viên chưa gán: hành vi vùng như cũ
        s, r = eligible("uwB", route_id)
        expect("B (vùng KR, chưa gán) lập phiếu ở Seoul được", s, r, 200)
        s, r = call("PUT", f"/api/parcels/{uuid.uuid4()}/put-away", "uwB", {"warehouseId": KR, "binCode": "BIN-X"})
        check("B chưa gán: xếp kệ không bị chặn quyền (như cũ)", s != 403, f"HTTP {s}: {r}")
        set_profile_region("uwB", "JP")
        s, r = eligible("uwB", route_id)
        expect("B đổi sang vùng JP → 403 ở kho Seoul như cũ", s, r, 403)
        check("Câu lỗi vùng như cũ", "phụ trách vùng" in (r.get("message") or ""), r)
        set_profile_region("uwB", "KR")

        s, r = eligible("uwC", route_id)
        expect("C chưa gán, chưa vùng → 403 như cũ", s, r, 403)

        s, r = eligible("admin", route_id)
        expect("Admin không bị giới hạn kho", s, r, 200)

        # ---------------- Gán nhiều kho cùng vùng
        s, r = assign(c, [KR, kr2], note="Hai kho Seoul")
        expect("Gán C vào hai kho cùng vùng KR", s, r, 200)
        check("C được đặt vùng KR và có 2 kho", r["data"]["region"] == "KR" and len(r["data"]["warehouses"]) == 2, r)
        s, ids = active_ids("uwC")
        check("Danh sách kho của C đúng 2 kho được gán", ids == {kr, kr2}, ids)
        s, r = eligible("uwC", route_id)
        expect("C thao tác được ở kho Seoul (một trong các kho được gán)", s, r, 200)
        s, r = call("GET", f"/api/warehouses/{kr2}/staff", "admin")
        check("Danh sách nhân viên kho KR2 có C", s == 200 and c.lower() in ids_of(r["data"]["staff"], "userId"), r)
    finally:
        # ---------------- Dọn dữ liệu thử
        for uid in (a, b, c):
            assign(uid, [], note="Dọn kiểm thử")
        s1, r1 = call("DELETE", f"/api/warehouses/{kr2}", "admin")
        s2, r2 = call("DELETE", f"/api/warehouses/{kr3_inactive}", "admin")
        check("Dọn: xoá được kho thử sau khi bỏ gán", s1 == 200 and s2 == 200, (s1, r1, s2, r2))
        for uid in (a, b, c):
            call("PUT", f"/api/User/{uid}/lock", "admin")

    print(f"\n{len(PASSED)} passed, {len(FAILED)} failed")
    for name in FAILED:
        print("  FAILED:", name)
    sys.exit(1 if FAILED else 0)


if __name__ == "__main__":
    main()
