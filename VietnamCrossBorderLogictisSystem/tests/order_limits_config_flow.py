#!/usr/bin/env python3
"""
Kiểm thử cấu hình GIỚI HẠN TẠO ĐƠN do Admin chỉnh (SYSTEM_SETTINGS nhóm ORDER_LIMITS):
  A. Đọc: Admin / Sale / khách đều GET được; chỉ Admin thấy người sửa, thời điểm sửa.
  B. Sửa: chỉ Admin PUT được (Sale / khách / kho → 403); giá trị sai → 400 tiếng Việt; có nhật ký.
  C. Ký gửi (khách + Sale tạo hộ, cả xem trước): vượt / trong hạn mức MỚI → 400 kèm số giới hạn / tạo được.
  D. Mua hộ (khách + Sale tạo hộ): số lượng mỗi sản phẩm vượt / trong hạn mức mới.
  E. "Không giới hạn" (null) cho tổng cân nặng.
Cuối kịch bản luôn trả lại đúng giá trị ban đầu.

    APP_ENV=/root/vcl-migration-deploy/app.env API=http://127.0.0.1:8088 python3 order_limits_config_flow.py
"""
import sys

from wave1_consignment_flow import IDS, PASSED, FAILED, RUN, call, check, item
from purchase_flow import ELECTRONICS

PATH = "/api/system-settings/order-limits"
ADMIN_ID = str(IDS["admin"]["id"]).lower()


def data(r):
    return r.get("data", r) if isinstance(r, dict) else r


def msg(r):
    return (r or {}).get("message", "") if isinstance(r, dict) else ""


def is_vietnamese_400(status, resp):
    m = msg(resp)
    english = any(x in m for x in ("The JSON value", "One or more", "field is required", "must be"))
    return status == 400 and bool(m) and not english and any(ord(c) > 127 for c in m)


def values_of(resp):
    return {i["key"]: i.get("value") for i in data(resp).get("items", [])}


def put(who, items, note=None):
    body = {"items": [{"key": k, "value": v} for k, v in items.items()]}
    if note:
        body["note"] = note
    return call("PUT", PATH, who, body)


def consignment_body(items, **extra):
    body = {"route": "KR-VN", "shippingOption": "Express",
            "receiverName": "Gioi han " + RUN, "receiverPhone": "0900000000",
            "receiverAddress": "12 Lê Lợi, Quận 1, TP HCM", "items": items}
    body.update(extra)
    return body


def purchase_body(quantity, **extra):
    body = {"route": "KR-VN", "shippingOption": "Express",
            "receiverName": "Gioi han " + RUN, "receiverPhone": "0900000000", "receiverAddress": "HCM",
            "items": [{"productLink": "https://shop.example/limit", "productType": ELECTRONICS,
                       "productName": f"Tai nghe GH{RUN}", "quantity": quantity}]}
    body.update(extra)
    return body


def part_a():
    s, r = call("GET", PATH, "admin")
    d = data(r)
    items = d.get("items", []) if isinstance(d, dict) else []
    check("A1 Admin đọc được cấu hình giới hạn (11 khoá)", s == 200 and len(items) == 11, (s, r))
    keys = {i["key"] for i in items}
    for k in ("ORDER_MAX_TOTAL_WEIGHT_KG", "ORDER_MAX_TOTAL_DECLARED_VALUE", "ORDER_MAX_PARCEL_WEIGHT_KG",
              "ORDER_MAX_ITEM_DECLARED_VALUE", "PURCHASE_MAX_ITEM_QUANTITY", "PURCHASE_MAX_ITEMS"):
        check(f"A2 Có khoá {k}", k in keys, keys)
    defaults = {i["key"]: i.get("defaultValue") for i in items}
    check("A3 Mặc định giữ đúng số cũ (5 kg / 10.000.000 đ / 3 kg / 6.000.000 đ / 999)",
          defaults.get("ORDER_MAX_TOTAL_WEIGHT_KG") == 5 and defaults.get("ORDER_MAX_TOTAL_DECLARED_VALUE") == 10000000
          and defaults.get("ORDER_MAX_PARCEL_WEIGHT_KG") == 3 and defaults.get("ORDER_MAX_ITEM_DECLARED_VALUE") == 6000000
          and defaults.get("PURCHASE_MAX_ITEM_QUANTITY") == 999, defaults)
    original = values_of(r)

    s, r = call("GET", PATH, "customer")
    d = data(r)
    check("A4 Khách đọc được giới hạn đang áp dụng (phẳng theo ký gửi / mua hộ)",
          s == 200 and "maxTotalWeightKg" in d.get("consignment", {}) and "maxItemQuantity" in d.get("purchase", {}), (s, r))
    check("A5 Khách không thấy người sửa",
          all(i.get("updatedByName") is None and i.get("updatedById") is None for i in d.get("items", [])), d.get("items"))

    s, r = call("GET", PATH, "sale")
    check("A6 Sale đọc được giới hạn", s == 200, (s, r))
    s, r = call("GET", PATH)
    check("A7 Chưa đăng nhập → 401", s == 401, s)
    return original


def part_b():
    for who in ("sale", "customer", "staff", "om1"):
        s, r = put(who, {"ORDER_MAX_TOTAL_WEIGHT_KG": 7})
        check(f"B1 {who} sửa giới hạn → 403", s == 403, (s, r))
    s, r = call("GET", PATH + "/history", "sale")
    check("B2 Sale không xem được nhật ký sửa → 403", s == 403, s)

    bad = [
        ({"ORDER_MAX_TOTAL_WEIGHT_KG": -1}, "số âm"),
        ({"ORDER_MAX_TOTAL_WEIGHT_KG": 0}, "số 0"),
        ({"KHONG_CO_KHOA": 5}, "khoá lạ"),
        ({"ORDER_MAX_PACKAGES": None}, "bỏ giới hạn số kiện (bắt buộc có)"),
        ({"ORDER_MAX_PACKAGES": 501}, "số kiện vượt trần cứng 500"),
        ({"ORDER_MAX_PARCEL_WEIGHT_KG": 1500}, "cân 1 kiện > 1000 kg (trần cân kho)"),
        ({"PURCHASE_MAX_ITEM_QUANTITY": 2.5}, "số lẻ cho giới hạn số nguyên"),
        ({"ORDER_MAX_TOTAL_WEIGHT_KG": 2, "ORDER_MAX_PARCEL_WEIGHT_KG": 3}, "tổng đơn < mức mỗi kiện"),
        ({"ORDER_MAX_TOTAL_DECLARED_VALUE": 1000000, "ORDER_MAX_ITEM_DECLARED_VALUE": 6000000}, "tổng giá trị < giá trị mỗi kiện"),
    ]
    for items, label in bad:
        s, r = put("admin", items)
        check(f"B3 Admin gửi {label} → 400 tiếng Việt", is_vietnamese_400(s, r), (s, r))

    s, r = call("PUT", PATH, "admin", {"items": []})
    check("B4 Danh sách rỗng → 400", s == 400, (s, r))

    s, r = put("admin", {"ORDER_MAX_TOTAL_WEIGHT_KG": 8, "ORDER_MAX_PARCEL_WEIGHT_KG": 4,
                         "ORDER_MAX_TOTAL_DECLARED_VALUE": 20000000, "PURCHASE_MAX_ITEM_QUANTITY": 3},
               note=f"Kiểm thử {RUN}")
    v = values_of(r)
    check("B5 Admin sửa được nhiều giới hạn một lần",
          s == 200 and v.get("ORDER_MAX_TOTAL_WEIGHT_KG") == 8 and v.get("ORDER_MAX_PARCEL_WEIGHT_KG") == 4
          and v.get("ORDER_MAX_TOTAL_DECLARED_VALUE") == 20000000 and v.get("PURCHASE_MAX_ITEM_QUANTITY") == 3, (s, r))
    d = data(r)
    row = next((i for i in d.get("items", []) if i["key"] == "ORDER_MAX_TOTAL_WEIGHT_KG"), {})
    check("B6 Lưu người sửa + thời điểm sửa",
          str(row.get("updatedById")).lower() == ADMIN_ID and row.get("updatedAt") and d.get("updatedAt"), row)

    s, r = call("GET", PATH + "/history?take=20", "admin")
    hist = data(r) if s == 200 else []
    mine = [h for h in hist if h.get("note") == f"Kiểm thử {RUN}"]
    check("B7 Nhật ký có 4 dòng vừa sửa, đúng người sửa, cũ → mới",
          len(mine) == 4 and all(str(h.get("changedById")).lower() == ADMIN_ID for h in mine)
          and any(h["key"] == "ORDER_MAX_TOTAL_WEIGHT_KG" and h.get("newValue") == 8 for h in mine), mine)

    s, r = call("GET", PATH, "customer")
    c = data(r).get("consignment", {})
    check("B8 Khách đọc thấy ngay giới hạn mới", s == 200 and c.get("maxTotalWeightKg") == 8 and c.get("maxParcelWeightKg") == 4, c)


def sale_customer_id():
    s, r = call("GET", "/api/customers", "sale")
    customers = r.get("items", []) if s == 200 else []
    return next((c["id"] for c in customers if c.get("id")), None)


def part_c(customer_id):
    # Tổng 7 kg: trước đây (5 kg) bị chặn, nay (8 kg) phải tạo được — chứng minh server đọc cấu hình.
    ok_items = [item(f"GH ok A {RUN}", 1, 3.5), item(f"GH ok B {RUN}", 1, 3.5)]
    over_total = [item(f"GH tong {i} {RUN}", 1, 3) for i in range(3)]          # 9 kg > 8
    over_parcel = [item(f"GH kien {RUN}", 1, 4.5)]                             # 4,5 kg > 4
    over_value = [dict(item(f"GH gia {i} {RUN}", 1, 1), declaredValue=6000000) for i in range(4)]  # 24tr > 20tr

    cases = [("khách", "customer", "/api/orders/consignments", {}),
             ("Sale tạo hộ", "sale", "/api/staff/consignments", {"customerId": customer_id})]
    for label, who, path, extra in cases:
        s, r = call("POST", path + "/preview", who, consignment_body(ok_items, **extra))
        check(f"C1 Ký gửi {label}: xem trước tổng 7 kg (≤ 8) → 200", s == 200, (s, r))
        s, r = call("POST", path, who, consignment_body(ok_items, **extra))
        check(f"C2 Ký gửi {label}: tạo đơn tổng 7 kg (≤ 8) → tạo được", s in (200, 201), (s, r))

        s, r = call("POST", path, who, consignment_body(over_total, **extra))
        check(f"C3 Ký gửi {label}: tổng 9 kg > 8 → 400 nêu giới hạn 8 kg",
              is_vietnamese_400(s, r) and "8 kg" in msg(r), (s, r))
        s, r = call("POST", path + "/preview", who, consignment_body(over_total, **extra))
        check(f"C4 Ký gửi {label}: xem trước tổng 9 kg → 400", is_vietnamese_400(s, r) and "8 kg" in msg(r), (s, r))

        s, r = call("POST", path, who, consignment_body(over_parcel, **extra))
        check(f"C5 Ký gửi {label}: kiện 4,5 kg > 4 → 400 nêu 4 kg",
              is_vietnamese_400(s, r) and "4 kg" in msg(r), (s, r))

        s, r = call("POST", path, who, consignment_body(over_value, **extra))
        check(f"C6 Ký gửi {label}: tổng giá trị 24.000.000 > 20.000.000 → 400 nêu 20.000.000 đ",
              is_vietnamese_400(s, r) and "20.000.000" in msg(r), (s, r))


def part_d(customer_id):
    cases = [("khách", "customer", "/api/purchase-requests", {}),
             ("Sale tạo hộ", "sale", "/api/staff/purchase-requests", {"customerId": customer_id})]
    for label, who, path, extra in cases:
        s, r = call("POST", path, who, purchase_body(3, **extra))
        check(f"D1 Mua hộ {label}: số lượng 3 (≤ 3) → tạo được", s in (200, 201), (s, r))
        s, r = call("POST", path, who, purchase_body(4, **extra))
        check(f"D2 Mua hộ {label}: số lượng 4 > 3 → 400 nêu giới hạn 3",
              is_vietnamese_400(s, r) and "3" in msg(r), (s, r))


def part_e():
    s, r = put("admin", {"ORDER_MAX_TOTAL_WEIGHT_KG": None}, note=f"Kiểm thử không giới hạn {RUN}")
    check("E1 Admin đặt tổng cân nặng = không giới hạn", s == 200 and values_of(r).get("ORDER_MAX_TOTAL_WEIGHT_KG") is None, (s, r))
    s, r = call("GET", PATH, "customer")
    check("E2 Khách đọc được null (không giới hạn)", s == 200 and data(r)["consignment"].get("maxTotalWeightKg") is None, r)
    items = [item(f"GH vo han {i} {RUN}", 1, 4) for i in range(3)]  # 12 kg, mỗi kiện 4 kg
    s, r = call("POST", "/api/orders/consignments/preview", "customer", consignment_body(items))
    check("E3 Không giới hạn tổng: 12 kg vẫn xem trước được (mỗi kiện vẫn ≤ 4 kg)", s == 200, (s, r))


def restore(original):
    if not original:
        return
    s, r = put("admin", original, note=f"Trả lại giá trị ban đầu sau kiểm thử {RUN}")
    check("Z1 Trả lại đúng giá trị ban đầu", s == 200 and values_of(r) == original, (s, values_of(r), original))


def main():
    original = part_a()
    try:
        part_b()
        customer_id = sale_customer_id()
        check("Có khách hàng để Sale tạo hộ", bool(customer_id), customer_id)
        part_c(customer_id)
        part_d(customer_id)
        part_e()
    finally:
        restore(original)
    print(f"\n{len(PASSED)} PASS, {len(FAILED)} FAIL")
    for f in FAILED:
        print("  FAIL:", f)
    return 1 if FAILED else 0


if __name__ == "__main__":
    sys.exit(main())
