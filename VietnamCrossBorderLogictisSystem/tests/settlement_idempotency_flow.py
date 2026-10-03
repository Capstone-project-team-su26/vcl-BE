#!/usr/bin/env python3
"""
Kiểm thử CHỐT TẤT TOÁN MỘT LẦN (POST /api/orders/{id}/payments/final):

  - bấm đúp (hai request tới cùng lúc) → đúng một lần phát hành, lần kia 409;
  - bấm lại sau khi đã chốt → 409 với câu tiếng Việt, không tạo thêm khoản thu, không đổi hoá đơn;
  - chốt xong thì đơn rời hàng chờ tất toán của Sale, khách vẫn thấy dòng chờ trả kèm số tiền;
  - Sale chủ động phát hành lại (reissue: true) → huỷ mã cũ, vẫn chỉ MỘT đợt cuối chờ trả;
  - khách trả xong → chốt lại (kể cả reissue) bị 409 "đã tất toán xong".

    APP_ENV=/root/vcl-migration-deploy/app.env API=http://127.0.0.1:8088 python3 settlement_idempotency_flow.py
"""
import sys
import threading

from wave1_consignment_flow import PASSED, FAILED, RUN, call, check, expect
from warehouse_zones_flow import received_parcels
from export_flow import KR, VN_WAREHOUSE, set_region, store
from vn_arrival_flow import arrival_photo, ship_to_vietnam


def data(r):
    return (r.get("data") or {}) if isinstance(r, dict) else {}


def queue_row(order_id, who):
    s, r = call("GET", "/api/orders/awaiting-settlement", who)
    items = data(r).get("items", [])
    return s, next((i for i in items if str(i.get("orderId", "")).lower() == order_id.lower()), None)


def final_payments(order_id):
    s, r = call("GET", f"/api/orders/{order_id}/payments", "sale")
    payments = data(r).get("payments", [])
    finals = [p for p in payments if str(p.get("installmentType", "")).upper() == "FINAL_PAYMENT"]
    return s, payments, finals


def status_of(payment):
    return str(payment.get("paymentStatus", "")).upper()


def main():
    call("PUT", f"/api/warehouses/{KR}/manager", "admin", {"managerId": None, "reason": "Kiểm thử chốt tất toán"})
    call("PUT", f"/api/warehouses/{VN_WAREHOUSE}/manager", "admin", {"managerId": None, "reason": "Kiểm thử chốt tất toán"})
    set_region("staff", "KR")

    # ---------------- Chuẩn bị: đơn 1 kiện về tới kho VN, kiểm khớp (không sự cố)
    s, order_id, parcels = received_parcels(["Tat toan A"], f"S{RUN}")
    if not check("Chuẩn bị đơn 1 kiện", s in (200, 201) and len(parcels) == 1, parcels):
        return 1
    pa = parcels["Tat toan A"]
    store(pa, "BIN-A1-01")
    shipment_id = ship_to_vietnam([pa], f"S{RUN}")
    if not check("Lô về tới kho VN", shipment_id is not None, shipment_id):
        return 1
    check("Chụp ảnh tiếp nhận kiện", arrival_photo(pa)[0] == 200)
    s, r = call("PUT", f"/api/international-shipments/{shipment_id}/destination-receiving", "staff", {"items": [
        {"parcelId": pa, "actualWeight": 1.0, "length": 20, "width": 20, "height": 10, "condition": "GOOD"}]})
    if not expect("Kho VN tiếp nhận kiện khớp khai báo", s, r, 200):
        return 1

    s, row = queue_row(order_id, "sale")
    check("Trước khi chốt: đơn nằm trong hàng chờ tất toán của Sale", s == 200 and row is not None, row)
    s, r = call("GET", f"/api/orders/{order_id}/settlement-preview", "sale")
    check("Xem trước cho phép phát hành", s == 200 and data(r).get("canIssue") is True, r)

    # ---------------- Bấm đúp: hai request tới cùng lúc
    results = [None, None]

    def fire(index):
        results[index] = call("POST", f"/api/orders/{order_id}/payments/final", "sale", {"paymentMethod": "SEPAY"})

    threads = [threading.Thread(target=fire, args=(i,)) for i in range(2)]
    for t in threads:
        t.start()
    for t in threads:
        t.join()
    codes = sorted(code for code, _ in results)
    check("Bấm đúp: đúng một lần phát hành (200), lần kia 409", codes == [200, 409], results)
    first = next((data(r) for code, r in results if code == 200), {})
    conflict = next((r for code, r in results if code == 409), {})
    check("409 có câu tiếng Việt", "chốt phí cuối" in conflict.get("message", ""), conflict)

    s, payments, finals = final_payments(order_id)
    check("Sau bấm đúp: chỉ MỘT khoản đợt cuối, đang chờ trả",
          s == 200 and len(finals) == 1 and status_of(finals[0]) == "PENDING"
          and str(finals[0].get("paymentId", "")).lower() == str(first.get("paymentId", "")).lower(), finals)
    invoice_ids = {str(p.get("invoiceId", "")).lower() for p in payments if p.get("invoiceId")}
    check("Không sinh hoá đơn mới (mọi khoản cùng một hoá đơn)", len(invoice_ids) == 1, invoice_ids)
    s, r = call("GET", f"/api/orders/{order_id}/settlement-preview", "sale")
    invoice_total = data(r).get("invoiceTotalBefore")

    # ---------------- Bấm lại sau khi đã chốt
    s, r = call("POST", f"/api/orders/{order_id}/payments/final", "sale",
                {"paymentMethod": "SEPAY", "extraFees": [{"name": "Phí bấm lại", "amount": 50000}]})
    check("Bấm chốt lần nữa bị chặn 409 (tiếng Việt)", s == 409 and "đang chờ khách thanh toán" in r.get("message", ""), r)
    s, payments, finals = final_payments(order_id)
    check("Bấm lại không tạo / không huỷ khoản nào",
          len(finals) == 1 and status_of(finals[0]) == "PENDING", finals)
    s, r = call("GET", f"/api/orders/{order_id}/settlement-preview", "sale")
    check("Bấm lại không cộng phí vào hoá đơn", data(r).get("invoiceTotalBefore") == invoice_total,
          (invoice_total, data(r).get("invoiceTotalBefore")))

    # ---------------- Hàng chờ sau khi chốt
    s, row = queue_row(order_id, "sale")
    check("Chốt xong: đơn rời hàng chờ tất toán của Sale", s == 200 and row is None, row)
    s, row = queue_row(order_id, "customer")
    check("Khách vẫn thấy đơn chờ mình trả, đúng số tiền",
          s == 200 and row is not None and abs((row.get("pendingPaymentAmount") or 0) - first.get("finalAmount", -1)) < 1,
          row)

    # ---------------- Sale chủ động phát hành lại
    s, r = call("POST", f"/api/orders/{order_id}/payments/final", "sale", {"paymentMethod": "SEPAY", "reissue": True})
    reissued = data(r)
    check("Phát hành lại (reissue) được, cùng số tiền",
          s == 200 and abs(reissued.get("finalAmount", -1) - first.get("finalAmount", 0)) < 1, r)
    s, payments, finals = final_payments(order_id)
    pending = [p for p in finals if status_of(p) == "PENDING"]
    check("Phát hành lại: mã cũ huỷ, vẫn chỉ MỘT đợt cuối chờ trả",
          len(pending) == 1 and str(pending[0].get("paymentId", "")).lower() == str(reissued.get("paymentId", "")).lower()
          and all(status_of(p) == "CANCELLED" for p in finals if p not in pending), finals)

    # ---------------- Khách trả xong → không chốt lại được nữa
    s, r = call("PUT", f"/api/admin/finance/transactions/{reissued.get('paymentId')}/approve", "admin",
                {"transactionCode": f"FT-ST-{RUN}", "receivedAmount": reissued.get("finalAmount")})
    expect("Admin xác nhận tiền tất toán", s, r, 200)
    for body, label in (({"paymentMethod": "SEPAY"}, "thường"), ({"paymentMethod": "SEPAY", "reissue": True}, "reissue")):
        s, r = call("POST", f"/api/orders/{order_id}/payments/final", "sale", body)
        check(f"Đã tất toán: chốt lại ({label}) bị 409", s == 409 and "đã tất toán xong" in r.get("message", ""), r)
    s, payments, finals = final_payments(order_id)
    check("Đã tất toán: không thêm khoản đợt cuối nào",
          sum(1 for p in finals if status_of(p) == "PAID") == 1 and not any(status_of(p) == "PENDING" for p in finals), finals)

    print(f"\n{len(PASSED)} PASS / {len(FAILED)} FAIL")
    if FAILED:
        print("Lỗi: " + ", ".join(FAILED))
    return 1 if FAILED else 0


if __name__ == "__main__":
    sys.exit(main() or 0)
