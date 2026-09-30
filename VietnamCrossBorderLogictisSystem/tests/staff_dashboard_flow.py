#!/usr/bin/env python3
"""
Kiểm thử ba bảng tổng quan của nhân viên:

    GET /api/staff/dashboard       Sale / Operations Manager / Admin
    GET /api/operations/dashboard  Operations Manager / Admin (không có số tiền)
    GET /api/admin/dashboard       chỉ Admin

Trọng tâm: con số phải KHỚP dữ liệu — tạo đơn rồi thấy số tăng đúng chỗ, và đối chiếu chéo
với các endpoint danh sách/tổng hợp đang có (tồn kho, sự cố, dòng tiền, người dùng). Vai trò
sai phải bị 403.

    APP_ENV=/root/vcl-migration-deploy/app.env API=http://127.0.0.1:8088 python3 staff_dashboard_flow.py
"""
import datetime
import sys

from wave1_consignment_flow import PASSED, FAILED, RUN, call, check, expect, create_order, item
from purchase_flow import create_request

SALE = "/api/staff/dashboard"
OPS = "/api/operations/dashboard"
ADMIN = "/api/admin/dashboard"


def data(r):
    return r.get("data", r) if isinstance(r, dict) else r


def vn_today():
    return (datetime.datetime.utcnow() + datetime.timedelta(hours=7)).date().isoformat()


def vn_month():
    return vn_today()[:7]


def by_key(rows, key="key"):
    return {r[key]: r for r in rows}


def percents_ok(rows, field="percent", count="count"):
    total = sum(r[count] for r in rows)
    return sum(r[field] for r in rows) == (100 if total > 0 else 0)


def get(path, who):
    s, r = call("GET", path, who)
    return s, data(r)


# ─────────────────────────────── phân quyền ───────────────────────────────

def permissions():
    for path, allowed, denied in (
        (SALE, ("sale", "om1", "admin"), ("customer", "staff")),
        (OPS, ("om1", "admin"), ("sale", "customer", "staff")),
        (ADMIN, ("admin",), ("om1", "sale", "customer", "staff")),
    ):
        for who in allowed:
            s, r = call("GET", path, who)
            expect(f"{path}: {who} xem được", s, r, 200)
        for who in denied:
            s, r = call("GET", path, who)
            expect(f"{path}: {who} bị chặn 403", s, r, 403)
        s, r = call("GET", path, None)
        expect(f"{path}: chưa đăng nhập → 401", s, r, 401)


# ─────────────────────────────── Sale ───────────────────────────────

def sale_invariants(board, label):
    check(f"[{label}] Tổng đơn = ký gửi + mua hộ",
          board["totalOrders"] == board["consignmentTotal"] + board["purchaseTotal"], board["totalOrders"])
    check(f"[{label}] Tỷ lệ tuyến: tổng số đơn các tuyến = tổng đơn",
          sum(r["total"] for r in board["routes"]) == board["totalOrders"], board["routes"])
    check(f"[{label}] Tỷ lệ tuyến cộng đúng 100%", percents_ok(board["routes"], count="total"), board["routes"])
    check(f"[{label}] Luôn có đủ 4 tuyến chính KR, JP, CN, US theo thứ tự",
          [r["countryCode"] for r in board["routes"]][:4] == ["KR", "JP", "CN", "US"], board["routes"])
    groups = board["statusGroups"]
    check(f"[{label}] Bốn nhóm trạng thái đúng thứ tự",
          [g["key"] for g in groups] == ["PENDING", "QUOTED", "APPROVED", "REJECTED"], [g["key"] for g in groups])
    check(f"[{label}] Tổng bốn nhóm trạng thái = tổng đơn",
          sum(g["count"] for g in groups) == board["totalOrders"], [g["count"] for g in groups])
    check(f"[{label}] Nhóm trạng thái cộng đúng 100%", percents_ok(groups), [g["percent"] for g in groups])
    check(f"[{label}] Mỗi nhóm: chi tiết trạng thái gốc cộng lại đúng số của nhóm",
          all(sum(s["count"] for s in g["statuses"]) == g["count"] for g in groups), groups)
    days = board["last7Days"]
    check(f"[{label}] Xu hướng 7 ngày: đủ 7 ngày, ngày cuối là hôm nay (giờ VN)",
          len(days) == 7 and days[-1]["date"] == vn_today(), [d["date"] for d in days])
    check(f"[{label}] Mỗi ngày: tổng = ký gửi + mua hộ",
          all(d["total"] == d["consignmentCount"] + d["purchaseCount"] for d in days), days)
    check(f"[{label}] Bảng mới nhất không quá 5 dòng",
          len(board["recentConsignments"]) <= 5 and len(board["recentPurchaseRequests"]) <= 5, "")


def sale_dashboard():
    s, before = get(SALE, "sale")
    if not check("Sale lấy được bảng tổng quan", s == 200 and isinstance(before, dict), before):
        return
    sale_invariants(before, "trước")

    # Tỷ giá phải đúng bảng tỷ giá công ty đang bật — không phải số mẫu.
    s, rates = call("GET", "/api/exchange-rates?activeOnly=true", None)
    rates = rates if isinstance(rates, list) else data(rates)
    want = sorted((r["currencyCode"].upper(), float(r["rateToVnd"])) for r in rates)
    got = sorted((r["currencyCode"], float(r["rateToVnd"])) for r in before["exchangeRates"])
    check("Tỷ giá trên bảng Sale = bảng tỷ giá đang bật", s == 200 and want == got and len(got) > 0, (want, got))

    # ── Tạo 1 đơn ký gửi + 1 yêu cầu mua hộ tuyến Hàn → số phải tăng đúng chỗ.
    s, order = create_order([item(f"Dashboard {RUN}", 1, 1.0)], f"DB{RUN}")
    order_id = order.get("orderId") or order.get("id")
    if not check("Khách tạo đơn ký gửi tuyến KR-VN", s == 200 and order_id, order):
        return
    s, request_id, req = create_request(f"DB{RUN}")
    if not check("Khách tạo yêu cầu mua hộ tuyến KR-VN", s in (200, 201) and request_id, req):
        return

    s, after = get(SALE, "sale")
    if not check("Sale đọc lại bảng tổng quan", s == 200, after):
        return
    sale_invariants(after, "sau")

    check("Tổng đơn ký gửi tăng 1", after["consignmentTotal"] == before["consignmentTotal"] + 1,
          (before["consignmentTotal"], after["consignmentTotal"]))
    check("Tổng yêu cầu mua hộ tăng 1", after["purchaseTotal"] == before["purchaseTotal"] + 1,
          (before["purchaseTotal"], after["purchaseTotal"]))

    kr0, kr1 = by_key(before["routes"], "countryCode")["KR"], by_key(after["routes"], "countryCode")["KR"]
    check("Tuyến Hàn Quốc: +1 ký gửi, +1 mua hộ",
          kr1["consignmentCount"] == kr0["consignmentCount"] + 1 and kr1["purchaseCount"] == kr0["purchaseCount"] + 1,
          (kr0, kr1))

    g0, g1 = by_key(before["statusGroups"]), by_key(after["statusGroups"])
    check("Nhóm 'Chờ duyệt' tăng 2 (1 ký gửi + 1 mua hộ)",
          g1["PENDING"]["consignmentCount"] == g0["PENDING"]["consignmentCount"] + 1
          and g1["PENDING"]["purchaseCount"] == g0["PENDING"]["purchaseCount"] + 1,
          (g0["PENDING"]["count"], g1["PENDING"]["count"]))

    t0, t1 = before["last7Days"][-1], after["last7Days"][-1]
    check("Cột hôm nay (giờ VN): +1 ký gửi, +1 mua hộ",
          t1["consignmentCount"] == t0["consignmentCount"] + 1 and t1["purchaseCount"] == t0["purchaseCount"] + 1,
          (t0, t1))

    w0, w1 = before["workQueue"], after["workQueue"]
    check("Việc chờ Sale: đơn ký gửi chờ duyệt +1", w1["consignmentsToReview"] == w0["consignmentsToReview"] + 1, (w0, w1))
    check("Việc chờ Sale: yêu cầu mua hộ chờ báo giá +1", w1["purchasesToQuote"] == w0["purchasesToQuote"] + 1, (w0, w1))

    check("Đơn ký gửi mới nhất đứng đầu bảng",
          after["recentConsignments"] and after["recentConsignments"][0]["orderId"] == order_id,
          after["recentConsignments"][:1])
    top = (after["recentPurchaseRequests"] or [{}])[0]
    check("Yêu cầu mua hộ mới nhất đứng đầu bảng, có số lượng sản phẩm (2 + 3)",
          top.get("purchaseRequestId") == request_id and top.get("totalQuantity") == 5 and top.get("itemCount") == 2, top)

    # Không đếm trùng: đơn kho sinh ra từ mua hộ không được tính vào ký gửi.
    s, r = call("GET", "/api/orders/consignments?pageSize=1&orderType=PURCHASE", "admin")
    purchase_orders = data(r).get("totalCount", 0) if isinstance(data(r), dict) else 0
    s, r = call("GET", "/api/orders/consignments?pageSize=1", "admin")
    all_orders = data(r).get("totalCount", 0) if isinstance(data(r), dict) else 0
    check("Ký gửi = mọi đơn kho trừ đơn kho mua hộ (không đếm trùng với yêu cầu mua hộ)",
          all_orders == 0 or after["consignmentTotal"] <= all_orders - purchase_orders,
          (all_orders, purchase_orders, after["consignmentTotal"]))

    return order_id, request_id


# ─────────────────────────────── vận hành ───────────────────────────────

MONEY_WORDS = ("amount", "paid", "revenue", "collected", "remaining", "bill")


def keys_deep(value):
    if isinstance(value, dict):
        for k, v in value.items():
            yield k
            yield from keys_deep(v)
    elif isinstance(value, list):
        for v in value:
            yield from keys_deep(v)


def operations_dashboard():
    s, board = get(OPS, "om1")
    if not check("Quản lý vận hành lấy được bảng tổng quan", s == 200 and isinstance(board, dict), board):
        return None

    check("Bảng vận hành không chứa số tiền nào",
          not [k for k in keys_deep(board) if any(w in k.lower() for w in MONEY_WORDS)],
          [k for k in keys_deep(board) if any(w in k.lower() for w in MONEY_WORDS)])

    whs, total = board["warehouses"], board["stockTotals"]
    check("Tồn kho: tổng các kho = dòng 'Tất cả kho'",
          sum(w["storedParcels"] for w in whs) == total["storedParcels"]
          and sum(w["activeBins"] for w in whs) == total["activeBins"]
          and sum(w["occupiedBins"] for w in whs) == total["occupiedBins"], total)
    check("Tỷ lệ lấp đầy = ô có hàng / ô đang bật",
          all(abs(w["occupancyPercent"] - (round(w["occupiedBins"] * 100 / w["activeBins"], 1) if w["activeBins"] else 0)) < 0.051
              for w in whs), whs)

    # Đối chiếu với danh sách tồn kho đang có: kiện AVAILABLE/RESERVED theo từng kho.
    mismatched = []
    for w in whs:
        s, r = call("GET", f"/api/inventories?warehouseId={w['warehouseId']}", "admin")
        rows = r.get("items", []) if isinstance(r, dict) else []
        parcels = {i["parcelId"] for i in rows
                   if i["status"].upper() in ("AVAILABLE", "RESERVED") and not (i["status"].upper() == "AVAILABLE" and i["quantity"] <= 0)}
        if s != 200 or len(parcels) != w["storedParcels"]:
            mismatched.append((w["warehouseName"], s, len(parcels), w["storedParcels"]))
    check("Số kiện lưu kho từng kho khớp danh sách tồn kho (/api/inventories)", not mismatched, mismatched)

    flow = board["flow7Days"]
    check("Luồng kiện: đủ 7 ngày, ngày cuối là hôm nay (giờ VN)",
          len(flow) == 7 and flow[-1]["date"] == vn_today(), [f["date"] for f in flow])
    check("Luồng kiện: không có số âm",
          all(f[k] >= 0 for f in flow for k in ("originInbound", "exported", "arrivedVn", "dispatchedDelivery")), flow)

    s, r = call("GET", "/api/parcel-incidents?status=OPEN,CUSTOMER_RESPONDED&pageSize=1", "om1")
    check("Sự cố đang mở khớp danh sách sự cố (OPEN + CUSTOMER_RESPONDED)",
          s == 200 and data(r).get("totalCount") == board["openIncidents"], (data(r).get("totalCount"), board["openIncidents"]))
    s, r = call("GET", "/api/parcel-incidents?pageSize=1", "om1")
    incident_total = data(r).get("totalCount")
    check("Tổng sự cố theo trạng thái = tổng theo loại = danh sách sự cố",
          sum(x["count"] for x in board["incidentsByStatus"]) == sum(x["total"] for x in board["incidentsByType"]) == incident_total,
          (incident_total, board["incidentsByStatus"]))
    check("Sự cố theo loại: số đang mở cộng lại = sự cố đang mở",
          sum(x["open"] for x in board["incidentsByType"]) == board["openIncidents"], board["incidentsByType"])

    in_progress = {"HANDED_OVER", "DEPARTED", "IN_TRANSIT", "DELAYED", "ON_HOLD", "CUSTOMS_CLEARED", "ARRIVED_VN"}
    check("Lô đang chạy = tổng các trạng thái từ bàn giao tới về VN",
          board["shipmentsInProgress"] == sum(x["count"] for x in board["shipmentsByStatus"] if x["key"] in in_progress),
          board["shipmentsByStatus"])
    check("Lô theo trạng thái cộng đúng 100%", percents_ok(board["shipmentsByStatus"]), board["shipmentsByStatus"])

    times = board["processingTimes"]
    check("Thời gian xử lý: đủ 5 loại chứng từ, không số âm, có mẫu mới có trung bình",
          [t["key"] for t in times] == ["WRO", "RECEIVING_NOTE", "INBOUND_VN", "DELIVERY", "INCIDENT"]
          and all((t["averageMinutes"] is None) == (t["sampleCount"] == 0) for t in times)
          and all((t["averageMinutes"] or 0) >= 0 for t in times), times)

    s, admin_view = get(OPS, "admin")
    check("Admin xem bảng vận hành ra cùng số tồn kho", s == 200 and admin_view["stockTotals"] == total, "")
    return board


# ─────────────────────────────── Admin ───────────────────────────────

def admin_dashboard(sale_before_admin, ops_board):
    s, board = get(ADMIN, "admin")
    if not check("Admin lấy được bảng tổng quan", s == 200 and isinstance(board, dict), board):
        return

    users = board["users"]
    s, r = call("GET", "/api/User", "admin")
    listed = data(r)
    listed = listed.get("items", listed) if isinstance(listed, dict) else listed
    check("Tổng người dùng khớp danh sách /api/User", s == 200 and users["total"] == len(listed), (users["total"], len(listed)))
    check("Người dùng theo vai trò cộng lại = tổng", sum(x["count"] for x in users["byRole"]) == users["total"], users["byRole"])
    active = sum(1 for u in listed if str(u.get("status", "")).lower() == "active")
    check("Số tài khoản đang hoạt động khớp danh sách", users["active"] == active, (users["active"], active))

    fin = board["finance"]
    s, r = call("GET", "/api/admin/finance/summary", "admin")
    summ = data(r)
    check("Phải thu / đã thu / còn nợ trùng trang Dòng tiền",
          s == 200 and fin["totalBillAmount"] == summ["totalBillAmount"] and fin["totalPaid"] == summ["totalPaid"]
          and fin["remaining"] == summ["remaining"] and fin["billedOrderCount"] == summ["orderCount"], (fin, summ))
    s, r = call("GET", "/api/admin/finance/transactions/pending-approval?pageSize=1", "admin")
    check("Khoản chờ đối soát khớp danh sách chờ duyệt",
          s == 200 and fin["pendingApprovalCount"] == data(r).get("totalCount"), (fin["pendingApprovalCount"], data(r)))
    check("Tiền thực thu = ký gửi + mua hộ",
          abs(fin["collectedTotal"] - fin["consignmentCollected"] - fin["purchaseCollected"]) < 0.01, fin)
    check("Thực thu theo tháng: đủ 6 tháng, tháng cuối là tháng này, mỗi tháng tổng = hai luồng",
          len(fin["collectedLast6Months"]) == 6 and fin["collectedLast6Months"][-1]["month"] == vn_month()
          and all(abs(m["total"] - m["consignment"] - m["purchase"]) < 0.01 for m in fin["collectedLast6Months"]),
          fin["collectedLast6Months"])
    check("Thực thu 6 tháng không vượt tổng thực thu",
          sum(m["total"] for m in fin["collectedLast6Months"]) <= fin["collectedTotal"] + 0.01, "")

    orders = board["orders"]
    s, sale_now = get(SALE, "admin")
    check("Số đơn trên bảng Admin = bảng Sale (cùng cách đếm)",
          orders["consignmentTotal"] == sale_now["consignmentTotal"] and orders["purchaseTotal"] == sale_now["purchaseTotal"],
          (orders["consignmentTotal"], sale_now["consignmentTotal"]))
    check("Đơn theo trạng thái cộng lại = tổng đơn",
          sum(x["count"] for x in orders["consignmentByStatus"]) == orders["consignmentTotal"]
          and sum(x["count"] for x in orders["purchaseByStatus"]) == orders["purchaseTotal"], "")
    months = orders["last6Months"]
    check("Đơn theo tháng: đủ 6 tháng, tháng cuối là tháng này (giờ VN)",
          len(months) == 6 and months[-1]["month"] == vn_month(), [m["month"] for m in months])

    if ops_board:
        check("Tồn kho trên bảng Admin = bảng vận hành", board["stock"] == ops_board["stockTotals"], (board["stock"], ops_board["stockTotals"]))
        check("Sự cố mở / lô đang chạy trên bảng Admin = bảng vận hành",
              board["openIncidents"] == ops_board["openIncidents"]
              and board["shipmentsInProgress"] == ops_board["shipmentsInProgress"], "")

    # Tạo thêm 1 đơn ký gửi → bảng Admin thấy ngay: tổng +1, tháng này +1, đang chạy +1.
    s, order = create_order([item(f"Dashboard admin {RUN}", 1, 1.0)], f"DA{RUN}")
    if not check("Khách tạo thêm 1 đơn ký gửi", s == 200 and (order.get("orderId") or order.get("id")), order):
        return
    s, after = get(ADMIN, "admin")
    o0, o1 = orders, after["orders"]
    check("Bảng Admin: tổng ký gửi +1, tháng này +1, đang chạy +1",
          o1["consignmentTotal"] == o0["consignmentTotal"] + 1
          and o1["last6Months"][-1]["consignmentCount"] == o0["last6Months"][-1]["consignmentCount"] + 1
          and o1["consignmentInProgress"] == o0["consignmentInProgress"] + 1,
          (o0["consignmentTotal"], o1["consignmentTotal"]))


def main():
    permissions()
    sale_dashboard()
    ops = operations_dashboard()
    admin_dashboard(None, ops)

    print(f"\n{len(PASSED)} PASS, {len(FAILED)} FAIL")
    for name in FAILED:
        print("  FAIL:", name)
    return 1 if FAILED else 0


if __name__ == "__main__":
    sys.exit(main())
