#!/usr/bin/env python3
"""
Kiểm thử CHẶNG THẬT của yêu cầu mua hộ: overallStage / overallStageText / isFullyCompleted trên
danh sách, chi tiết, bảng "mua hộ mới nhất" của Sale, và lọc GET /api/purchase-requests?stage=.

Vì sao: PURCHASE_REQUESTS.status = COMPLETED (nhãn cũ "Hoàn tất nghiệp vụ") được ghi cả khi chỉ
tất toán đợt cuối đời cũ — hàng còn nằm ở kho nước ngoài. "Đơn đã hoàn tất" chỉ khi mọi đơn kho đã
đóng (COMPLETED), yêu cầu đã được hệ thống chốt và không còn khoản hoàn chờ chuyển.

Các mốc hãng giao hàng / kho VN báo về được giả lập thẳng trong DB THỬ (AppDb_migration) như
vn_arrival_flow.py.

    APP_ENV=/root/vcl-migration-deploy/app.env API=http://127.0.0.1:8088 python3 purchase_stage_flow.py
"""
import sys
import urllib.parse

from wave1_consignment_flow import PASSED, FAILED, RUN, call, check, expect
from export_flow import KR, VN_WAREHOUSE, set_region
from vn_arrival_flow import sql
from purchase_flow import CLOTHES, data
from purchase_refund_flow import new_request, quote_and_prepay, order_and_place


def same_id(a, b):
    return str(a or "").lower() == str(b or "").lower()


def listing(code, stage=None, who="sale"):
    query = {"pageNumber": 1, "pageSize": 50, "searchKeyword": code}
    if stage:
        query["stage"] = stage
    s, r = call("GET", "/api/purchase-requests?" + urllib.parse.urlencode(query), who)
    return s, (data(r).get("items") or []) if s == 200 else r


def row(request_id, code, who="sale"):
    s, items = listing(code, who=who)
    return next((i for i in items if same_id(i.get("purchaseRequestId"), request_id)), {}) if s == 200 else {}


def in_stage(request_id, code, stage):
    s, items = listing(code, stage)
    return s == 200 and any(same_id(i.get("purchaseRequestId"), request_id) for i in items)


def expect_stage(name, request_id, code, stage, text, fully=False):
    r = row(request_id, code)
    ok = (r.get("overallStage") == stage and r.get("overallStageText") == text
          and r.get("isFullyCompleted") is fully and in_stage(request_id, code, stage))
    return check(name, ok, {k: r.get(k) for k in ("status", "statusDisplayName", "overallStage", "overallStageText", "isFullyCompleted")})


def main():
    call("PUT", f"/api/warehouses/{KR}/manager", "admin", {"managerId": None, "reason": "Kiểm thử chặng mua hộ"})
    call("PUT", f"/api/warehouses/{VN_WAREHOUSE}/manager", "admin", {"managerId": None, "reason": "Kiểm thử chặng mua hộ"})
    set_region("staff", "KR")

    s, r = call("POST", "/api/suppliers", "admin", {
        "supplierCode": f"NCC-CH-{RUN}", "supplierName": f"NCC chặng {RUN}", "supplierType": "GOODS",
        "country": "KR", "email": f"ncc-ch{RUN}@example.com"})
    supplier_id = data(r).get("id") or data(r).get("supplierId")
    if not check("Admin tạo NCC", s in (200, 201) and supplier_id, r):
        return 1

    # ── Tham số lọc ──────────────────────────────────────────────────────────────────────────
    s, r = call("GET", "/api/purchase-requests?stage=HOAN_TAT_NGHIEP_VU", "sale")
    check("Lọc chặng lạ → 400 kèm danh sách chặng hợp lệ", s == 400 and "COMPLETED" in r.get("message", ""), r)
    s, r = call("GET", "/api/purchase-requests?stage=ALL&pageSize=1", "sale")
    check("stage=ALL = không lọc", s == 200, r)

    # ── Một yêu cầu đi hết vòng đời ──────────────────────────────────────────────────────────
    suffix = f"CH{RUN}"
    s, request_id, items = new_request(suffix, [("Ao", CLOTHES, 2), ("Mu", CLOTHES, 1)])
    if not check("Khách đặt 2 áo + 1 mũ", s in (200, 201) and request_id, items):
        return 1
    s, r = call("GET", f"/api/purchase-requests/{request_id}", "sale")
    code = data(r).get("purchaseCode")
    ao, mu = items[f"Ao {suffix}"], items[f"Mu {suffix}"]

    expect_stage("Mới tạo → Chờ báo giá", request_id, code, "AWAITING_QUOTE", "Chờ báo giá")
    check("Chờ báo giá không lọt vào ?stage=COMPLETED", not in_stage(request_id, code, "COMPLETED"))
    mine = row(request_id, code, who="customer")
    check("Khách cũng nhận overallStage của yêu cầu mình", mine.get("overallStage") == "AWAITING_QUOTE", mine)

    prepay, q = quote_and_prepay(request_id, [(ao, 100000), (mu, 50000)], 20000, 10000, 1, suffix)
    if not check("Báo giá + khách trả trước", prepay, q):
        return 1
    expect_stage("Đã trả trước, chưa lập đơn mua → Đã thanh toán trước", request_id, code, "PREPAID", "Đã thanh toán trước")

    placed, r = order_and_place(request_id, supplier_id, [(ao, 2, 100000)], suffix)
    if not check("Đặt NCC 2 áo (mũ chưa lập đơn)", placed and placed.get("status") == "ORDERED", r):
        return 1
    po_id, order_id = placed["purchaseOrderId"], placed["warehouseOrderId"]
    expect_stage("Đã đặt NCC → Đang đặt hàng NCC", request_id, code, "ORDERING_SUPPLIER", "Đang đặt hàng NCC")

    s, r = call("POST", f"/api/purchase-requests/{request_id}/close-unfulfilled", "sale", {"reason": "NCC hết mũ"})
    refund = data(r).get("refund") or {}
    check("Đóng mũ không mua được → khoản hoàn chờ chuyển", s == 200 and refund.get("refundId"), r)

    s, r = call("PUT", f"/api/purchase-orders/{po_id}/progress", "sale",
                {"status": "SUPPLIER_SHIPPED", "domesticTrackingCode": f"CJ{RUN}", "domesticCarrier": "CJ"})
    check("NCC phát hàng", s == 200, r)
    expect_stage("NCC đã phát, kho nguồn chưa nhận → Đang vận chuyển", request_id, code, "IN_TRANSIT", "Đang vận chuyển")

    sql(f"UPDATE ORDERS SET status = N'WAREHOUSE_RECEIVED' WHERE id = '{order_id}';")
    expect_stage("Đã nhập kho nước ngoài → vẫn Đang vận chuyển (chưa về VN)", request_id, code, "IN_TRANSIT", "Đang vận chuyển")

    sql(f"UPDATE ORDERS SET status = N'AT_DESTINATION_WAREHOUSE' WHERE id = '{order_id}';")
    expect_stage("Đơn kho ở kho đích → Đã về kho VN", request_id, code, "ARRIVED_VN", "Đã về kho VN")

    sql(f"UPDATE ORDERS SET status = N'DELIVERED' WHERE id = '{order_id}';")
    expect_stage("Đã giao, chưa đóng → Đã giao – chờ đóng đơn", request_id, code, "DELIVERED", "Đã giao – chờ đóng đơn")

    sql(f"UPDATE ORDERS SET status = N'COMPLETED' WHERE id = '{order_id}';")
    expect_stage("Đơn kho đóng, còn khoản hoàn chờ chuyển → Chờ hoàn tiền cho khách", request_id, code,
                 "AWAITING_REFUND", "Chờ hoàn tiền cho khách")

    sql(f"UPDATE PURCHASE_REQUESTS SET status = N'COMPLETED' WHERE id = '{request_id}';")
    r_row = row(request_id, code)
    check("status COMPLETED nhưng còn khoản hoàn chờ → Chờ hoàn tiền, không phải hoàn tất",
          r_row.get("status") == "COMPLETED" and r_row.get("overallStage") == "AWAITING_REFUND"
          and r_row.get("isFullyCompleted") is False and not in_stage(request_id, code, "COMPLETED"), r_row)

    sql(f"UPDATE PURCHASE_REQUESTS SET status = N'PURCHASING' WHERE id = '{request_id}';")
    s, r = call("POST", f"/api/purchase-requests/{request_id}/refunds/{refund['refundId']}/complete", "sale",
                {"transactionCode": f"RF-CH-{RUN}", "amount": refund.get("amount")})
    check("Kế toán chuyển xong khoản hoàn", s == 200, r)
    expect_stage("Hết khoản hoàn, đơn kho đóng nhưng hệ thống chưa chốt yêu cầu → chưa hoàn tất", request_id, code,
                 "DELIVERED", "Đã giao – chờ đóng đơn")

    sql(f"UPDATE PURCHASE_REQUESTS SET status = N'COMPLETED' WHERE id = '{request_id}';")
    expect_stage("Mọi đơn kho đóng + yêu cầu đã chốt + hết khoản hoàn → Đơn đã hoàn tất", request_id, code,
                 "COMPLETED", "Đơn đã hoàn tất", fully=True)

    s, r = call("GET", f"/api/purchase-requests/{request_id}", "sale")
    d = data(r)
    check("Chi tiết trả cùng chặng", s == 200 and d.get("overallStage") == "COMPLETED"
          and d.get("overallStageText") == "Đơn đã hoàn tất" and d.get("isFullyCompleted") is True,
          {k: d.get(k) for k in ("status", "overallStage", "overallStageText", "isFullyCompleted")})

    s, r = call("GET", "/api/staff/dashboard", "sale")
    recent = next((x for x in data(r).get("recentPurchaseRequests") or [] if same_id(x.get("purchaseRequestId"), request_id)), None)
    check("Tổng quan Sale: bảng mua hộ mới nhất có chặng thật", s == 200 and recent is not None
          and recent.get("overallStage") == "COMPLETED" and recent.get("overallStageText") == "Đơn đã hoàn tất", recent)

    # Lỗi đã gặp trên production: status COMPLETED (tất toán đợt cuối) mà hàng còn ở kho nước ngoài.
    sql(f"UPDATE ORDERS SET status = N'WAREHOUSE_RECEIVED' WHERE id = '{order_id}';")
    r_row = row(request_id, code)
    check("status COMPLETED mà đơn kho còn ở kho nước ngoài → Đang vận chuyển, KHÔNG vào ?stage=COMPLETED",
          r_row.get("status") == "COMPLETED" and r_row.get("overallStage") == "IN_TRANSIT"
          and r_row.get("isFullyCompleted") is False and not in_stage(request_id, code, "COMPLETED"), r_row)
    sql(f"UPDATE ORDERS SET status = N'COMPLETED' WHERE id = '{order_id}';")

    # ── Yêu cầu bị từ chối / chờ khách thanh toán ─────────────────────────────────────────────
    s, rejected_id, _ = new_request(f"TC{RUN}", [("Ao", CLOTHES, 1)])
    s, r = call("GET", f"/api/purchase-requests/{rejected_id}", "sale")
    rejected_code = data(r).get("purchaseCode")
    s, r = call("PUT", f"/api/purchase-requests/{rejected_id}/status", "sale", {"status": "REJECTED", "reason": "Hàng cấm"})
    check("Sale từ chối yêu cầu", s == 200, r)
    expect_stage("Từ chối → Đã từ chối", rejected_id, rejected_code, "REJECTED", "Đã từ chối")

    s, waiting_id, waiting_items = new_request(f"CT{RUN}", [("Ao", CLOTHES, 1)])
    s, r = call("GET", f"/api/purchase-requests/{waiting_id}", "sale")
    waiting_code = data(r).get("purchaseCode")
    s, r = call("POST", f"/api/purchase-requests/{waiting_id}/quotation", "sale", {
        "purchaseFee": 10000, "domesticShippingFee": 0, "freightRatePerKg": 120000, "estimatedWeight": 1,
        "items": [{"purchaseRequestItemId": waiting_items[f"Ao CT{RUN}"], "unitPrice": 100000}]})
    check("Sale báo giá", s in (200, 201), r)
    expect_stage("Đã báo giá → Đã báo giá – chờ khách thanh toán", waiting_id, waiting_code,
                 "AWAITING_PAYMENT", "Đã báo giá – chờ khách thanh toán")

    print(f"\n{len(PASSED)} PASS, {len(FAILED)} FAIL")
    return 1 if FAILED else 0


if __name__ == "__main__":
    sys.exit(main())
