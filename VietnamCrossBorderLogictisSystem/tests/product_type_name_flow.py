#!/usr/bin/env python3
"""
Kiểm thử tên loại hàng (productTypeName) và tên thùng (packageConfiguration.displayName) trả kèm
dòng hàng, để màn hình không phải in GUID.

Lỗi thật 03/10/2026: bảng "Hàng khách khai trên đơn" ở web nội bộ hiện
"11111111-0000-0000-0000-000000000001" thay vì "Điện tử", vì ORDER_ITEMS.PRODUCT_TYPE lưu Id loại
hàng mà API chỉ trả nguyên cột đó. Thùng hiện "Medium Box" tiếng Anh trong khi web khách ghi
"Thùng cỡ vừa".

Mỗi endpoint đã sửa phải trả productTypeName là TÊN (không rỗng, không phải GUID):
  POST /api/orders/consignments (báo giá tạm tính) · GET /api/orders/consignments/{id} (khách + Sale)
  GET /api/warehouse-receiving-notes/{id} (expectedItems + items) · GET .../scan
  POST /api/parcels/check-in (declaredItems) · GET /api/parcels/pending-putaway · GET /api/parcels/{id}
  POST /api/purchase-requests · GET /api/purchase-requests/{id}

    APP_ENV=/root/vcl-migration-deploy/app.env API=http://127.0.0.1:8088 python3 product_type_name_flow.py
"""
import re
import sys

from wave1_consignment_flow import IDS, PASSED, FAILED, RUN, call, check, expect, item, create_order
from receiving_discrepancy_decision_flow import paid_note

GUID = re.compile(r"^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$", re.I)
LEGACY_NAME = "Quần áo"   # đơn cũ / đơn từ mua hộ lưu thẳng tên loại hàng thay vì Id


def data(r):
    return r.get("data", r) if isinstance(r, dict) else r


def is_name(value):
    return isinstance(value, str) and value.strip() != "" and not GUID.match(value.strip())


def check_names(label, rows, want_by_product, key="productName"):
    """Mỗi dòng có productTypeName đúng tên mong đợi và không dòng nào lộ GUID ở field tên."""
    rows = rows or []
    got = {r.get(key): r.get("productTypeName") for r in rows}
    ok = bool(rows) and all(is_name(got.get(p)) and got.get(p) == want for p, want in want_by_product.items() if p in got) \
        and all(p in got for p in want_by_product)
    return check(label, ok, got)


def main():
    s, r = call("GET", "/api/product-types")
    catalog = {t["id"].lower(): t["name"] for t in data(r)}
    type_id = IDS["productTypeId"]
    type_name = catalog.get(type_id.lower())
    if not check("Danh mục có loại hàng dùng để kiểm thử", s == 200 and is_name(type_name), r):
        return

    crate_s, configs = call("GET", "/api/package-configurations")
    by_code = {c["configCode"]: c for c in configs} if isinstance(configs, list) else {}
    check("Danh mục thùng trả displayName tiếng Việt như web khách",
          by_code.get("SMALL", {}).get("displayName") == "Thùng cỡ nhỏ"
          and by_code.get("MEDIUM", {}).get("displayName") == "Thùng cỡ vừa"
          and by_code.get("LARGE", {}).get("displayName") == "Thùng cỡ lớn"
          and by_code.get("CUSTOM", {}).get("displayName") == "Thùng tùy chỉnh", configs)

    gadget, legacy = f"Tai nghe PT{RUN}", f"Áo cũ PT{RUN}"
    legacy_item = item(legacy, 1, 1.0)
    legacy_item["productType"] = LEGACY_NAME
    want = {gadget: type_name, legacy: LEGACY_NAME}

    # ---------------- Đơn ký gửi: báo giá tạm tính lúc tạo đơn
    s, created = create_order([item(gadget, 2, 1.0), legacy_item], "PT-EST")
    if expect("Khách tạo đơn (kiểm báo giá tạm tính)", s, created, 200):
        check_names("Báo giá tạm tính lúc tạo đơn: items có productTypeName",
                    (created.get("quotation") or {}).get("items", []), want)

    # ---------------- Đơn đã cọc: chi tiết đơn (khách, Sale)
    made = paid_note([item(gadget, 2, 1.0), legacy_item], "PT")
    if not made:
        return
    order_id, code, note_id, note_code, expected = made

    s, detail = call("GET", f"/api/orders/consignments/{order_id}", "customer")
    items = data(detail).get("items", [])
    check_names("Chi tiết đơn (khách): productTypeName là tên, kể cả đơn lưu thẳng tên", items, want)
    check("Chi tiết đơn vẫn giữ productType gốc (Id) cho FE cũ",
          any(i.get("productType") == type_id for i in items), items)
    crate = next((i.get("packageConfiguration") for i in items if i.get("packageConfiguration")), None)
    check("Thùng của dòng hàng có displayName tiếng Việt",
          crate is not None and is_name(crate.get("displayName")) and "Box" not in crate.get("displayName", ""), crate)

    s, staff = call("GET", f"/api/orders/consignments/{order_id}", "sale")
    check_names("Chi tiết đơn (Sale duyệt đơn): productTypeName là tên", data(staff).get("items", []), want)

    s, q = call("GET", f"/api/orders/{order_id}/quotation", "customer")
    q_items = data(q).get("items") or []
    if q_items:
        check_names("Báo giá của đơn: dòng hàng có productTypeName", q_items, want)

    # ---------------- Phiếu tiếp nhận kho: chi tiết + quét phiếu
    check_names("Phiếu nhập kho: expectedItems có productTypeName", list(expected.values()), want)
    check("Phiếu nhập kho: field productType cũ không còn là GUID",
          all(not GUID.match(e.get("productType") or "") for e in expected.values()), expected)

    s, scan = call("GET", f"/api/warehouse-receiving-notes/scan?code={note_code}", "staff")
    check_names("Quét phiếu (app kho): expectedItems có productTypeName", data(scan).get("expectedItems", []), want)

    # ---------------- Check-in một kiện → chờ xếp kệ → chi tiết kiện
    first = expected[gadget]
    s, r = call("POST", "/api/parcels/check-in", "staff", {"shippingCode": note_code, "packages": [
        {"orderItemId": first["orderItemId"], "actualWeight": 1.0, "length": 20, "width": 20, "height": 10}]})
    if not check("Kho check-in một kiện", s in (200, 201), r):
        return
    checkin = data(r)
    check_names("Check-in: declaredItems có productTypeName", checkin.get("declaredItems", []), want)
    parcel_id = checkin.get("parcelId")

    s, pend = call("GET", f"/api/parcels/pending-putaway?pageSize=200&search={code}", "staff")
    row = next((p for p in data(pend).get("items", []) if p["id"] == parcel_id), None)
    check("Chờ xếp kệ: kiện có productTypeName là tên",
          row is not None and row.get("productTypeName") == type_name, row or pend)

    s, pd = call("GET", f"/api/parcels/{parcel_id}", "staff")
    check("Chi tiết kiện: productTypeName là tên", s == 200 and data(pd).get("productTypeName") == type_name, pd)

    # ---------------- Chốt biên bản → dòng đối chiếu của phiếu
    other = expected[legacy]
    s, r = call("POST", f"/api/warehouse-receiving-notes/{note_id}/confirm-receipt", "staff",
                {"items": [{"orderItemId": first["orderItemId"], "actualQuantity": 2, "actualWeight": 1.0},
                           {"orderItemId": other["orderItemId"], "actualQuantity": 1, "actualWeight": 1.0}]})
    expect("Kho chốt biên bản", s, r, 200)
    s, nd = call("GET", f"/api/warehouse-receiving-notes/{note_id}", "om1")
    check_names("Biên bản nhập kho: dòng đối chiếu có productTypeName", data(nd).get("items", []), want)

    # ---------------- Mua hộ: tạo + chi tiết
    s, r = call("POST", "/api/purchase-requests", "customer", {
        "route": "KR-VN", "shippingOption": "Express",
        "receiverName": "Mua ho PT " + RUN, "receiverPhone": "0900000000", "receiverAddress": "HCM",
        "items": [{"productLink": "https://shop.example/pt", "productType": type_id,
                   "productName": f"Loa PT{RUN}", "quantity": 1}]})
    created = data(r)
    request_id = created.get("purchaseRequestId") or created.get("id")
    if not check("Khách tạo yêu cầu mua hộ chọn loại hàng theo Id", s in (200, 201) and request_id, r):
        return
    check_names("Tạo mua hộ: items trả productTypeName", created.get("items", []), {f"Loa PT{RUN}": type_name})
    s, pr = call("GET", f"/api/purchase-requests/{request_id}", "sale")
    check_names("Chi tiết mua hộ: items có productTypeName", data(pr).get("items", []), {f"Loa PT{RUN}": type_name})


if __name__ == "__main__":
    try:
        main()
    finally:
        print(f"\n{len(PASSED)} passed, {len(FAILED)} failed")
        sys.exit(1 if FAILED else 0)
