#!/usr/bin/env python3
"""
Kiểm thử các chặn còn tồn ở backend (web đã chặn, gọi thẳng API trước đây vẫn lọt):
  A. Dữ liệu đầu vào mua hộ / ký gửi: số lượng có trần, số dòng ≤ 50, link http/https, độ dài chữ khớp cột DB,
     ghi chú chung tính cả tên dịch vụ ghép thêm; lỗi 400 luôn là câu tiếng Việt (kể cả lỗi đọc JSON).
  B. Báo giá mua hộ: ship nội địa chỉ tính MỘT lần dù Sale gửi cả ô domesticShippingFee lẫn rule DOMESTIC_FEE.
  C. Trang QR SePay / payOS đưa khách về returnUrl/cancelUrl web gửi (chỉ origin cho phép) kèm ?orderCode=&status=.
  D. Danh sách yêu cầu mua hộ có tên/mã/SĐT khách, số dòng/số lượng còn mua được, tiền trả trước đã thu.

    APP_ENV=/root/vcl-migration-deploy/app.env API=http://127.0.0.1:8088 python3 input_hardening_flow.py
"""
import http.client, json, sys, urllib.parse, urllib.request

from wave1_consignment_flow import API, IDS, PASSED, FAILED, RUN, call, check, expect, item
from export_flow import KR
from purchase_flow import ELECTRONICS, CLOTHES, create_request, data, pay_manually, request_items

FRONT = "https://logictic.site"


def raw(method, path, body=None, who=None, content_type="application/json"):
    """Gửi thô (body không phải JSON hợp lệ, hoặc không theo chuyển hướng)."""
    u = urllib.parse.urlsplit(API)
    conn = http.client.HTTPConnection(u.hostname, u.port or 80, timeout=60)
    headers = {"Content-Type": content_type}
    if who:
        from wave1_consignment_flow import TOK
        headers["Authorization"] = "Bearer " + TOK[who]
    conn.request(method, path, body=body, headers=headers)
    r = conn.getresponse()
    text = r.read().decode("utf-8", "replace")
    return r.status, dict(r.getheaders()), text


def is_vietnamese_400(status, resp):
    msg = (resp or {}).get("message", "") if isinstance(resp, dict) else ""
    english = any(x in msg for x in ("The JSON value", "One or more", "field is required", "must be"))
    return status == 400 and bool(msg) and not english and any(ord(c) > 127 for c in msg)


def purchase_body(items=None, **extra):
    body = {"route": "KR-VN", "shippingOption": "Express",
            "receiverName": "Hardening " + RUN, "receiverPhone": "0900000000", "receiverAddress": "HCM",
            "items": items or [{"productLink": "https://shop.example/a", "productType": ELECTRONICS,
                                "productName": f"Tai nghe H{RUN}", "quantity": 1}]}
    body.update(extra)
    return body


def line(**over):
    base = {"productLink": "https://shop.example/x", "productType": CLOTHES, "productName": f"Ao H{RUN}", "quantity": 1}
    base.update(over)
    return base


def part_a():
    # ---- Số lượng
    s, r = call("POST", "/api/purchase-requests", "customer", purchase_body([line(quantity=1000)]))
    check("A1 Mua hộ: số lượng 1000 > 999 → 400 tiếng Việt", is_vietnamese_400(s, r) and "999" in r.get("message", ""), (s, r))
    s, h, t = raw("POST", "/api/purchase-requests",
                  json.dumps(purchase_body([line()])).replace('"quantity": 1', '"quantity": 99999999999'), who="customer")
    resp = json.loads(t) if t.startswith("{") else {}
    check("A2 Số lượng tràn int (99999999999) → 400 tiếng Việt, không phải câu lỗi JSON tiếng Anh",
          is_vietnamese_400(s, resp), (s, t[:300]))
    s, r = call("POST", "/api/purchase-requests", "customer", purchase_body([line(quantity=999)]))
    check("A3 Số lượng đúng trần 999 vẫn tạo được", s in (200, 201), (s, r))

    # ---- Số dòng
    s, r = call("POST", "/api/purchase-requests", "customer", purchase_body([line(productName=f"Dong {i}") for i in range(51)]))
    check("A4 51 dòng sản phẩm → 400 (tối đa 50)", is_vietnamese_400(s, r) and "50" in r.get("message", ""), (s, r))

    # ---- Link
    for bad in ("shop.example/abc", "ftp://shop.example/a", "javascript:alert(1)"):
        s, r = call("POST", "/api/purchase-requests", "customer", purchase_body([line(productLink=bad)]))
        check(f"A5 Link không phải http/https ({bad}) → 400", is_vietnamese_400(s, r) and "http" in r.get("message", ""), (s, r))

    # ---- Độ dài chữ khớp cột DB
    too_long = {
        "productLink": "https://shop.example/" + "a" * 990,
        "sourceWebsite": "w" * 501, "productName": "n" * 256, "attributes": "t" * 501, "note": "g" * 501,
    }
    for field, value in too_long.items():
        s, r = call("POST", "/api/purchase-requests", "customer", purchase_body([line(**{field: value})]))
        check(f"A6 {field} quá dài → 400 tiếng Việt (không phải 500 SQL)", is_vietnamese_400(s, r), (s, r))
    for field, n in (("receiverName", 256), ("receiverPhone", 51), ("receiverAddress", 501), ("generalNote", 1001)):
        s, r = call("POST", "/api/purchase-requests", "customer", purchase_body(**{field: "x" * n}))
        check(f"A7 {field} {n} ký tự → 400", is_vietnamese_400(s, r), (s, r))

    # Ghi chú 990 ký tự hợp lệ nhưng ghép tên dịch vụ thì vượt 1000 → 400 nói rõ còn gõ được bao nhiêu.
    s, r = call("POST", "/api/purchase-requests", "customer",
                purchase_body(generalNote="z" * 990, requiresPacking=True, requiresWoodenCrate=True))
    check("A8 Ghi chú chung + tên dịch vụ ghép > 1000 → 400 báo số ký tự còn được",
          is_vietnamese_400(s, r) and "Ghi chú chung" in r.get("message", "") and "tối đa" in r.get("message", ""), (s, r))
    s, r = call("POST", "/api/purchase-requests", "customer",
                purchase_body(generalNote="z" * 950, requiresPacking=True))
    check("A9 Ghi chú + dịch vụ vừa 1000 → tạo được", s in (200, 201), (s, r))
    customer_id = data(r).get("customerId")

    # Sale lên hộ: dấu "Đơn do nhân viên lên hộ khách" cũng tính vào 1000 ký tự.
    s, r = call("POST", "/api/staff/purchase-requests", "sale",
                purchase_body(customerId=customer_id, generalNote="s" * 1000))
    check("A10 Sale lên hộ: ghi chú 1000 + dấu Sale lên hộ → 400 (không phải 500)", is_vietnamese_400(s, r), (s, r))

    # ---- JSON hỏng / thiếu body
    s, h, t = raw("POST", "/api/purchase-requests", "{ not json", who="customer")
    resp = json.loads(t) if t.startswith("{") else {}
    check("A11 Body không phải JSON → 400 tiếng Việt", is_vietnamese_400(s, resp), (s, t[:300]))

    # ---- Ký gửi
    s, r = call("POST", "/api/orders/consignments", "customer", {
        "route": "KR-VN", "shippingOption": "Express", "receiverName": "HK", "receiverPhone": "0900000000",
        "receiverAddress": "HCM", "items": [item(f"Kien H{RUN}", 6, 1.0)]})
    check("A12 Ký gửi: 6 sản phẩm/kiện → 400 (tối đa 5)", is_vietnamese_400(s, r) and "5" in r.get("message", ""), (s, r))
    s, r = call("POST", "/api/orders/consignments", "customer", {
        "route": "KR-VN", "shippingOption": "Express", "receiverName": "HK", "receiverPhone": "0900000000",
        "receiverAddress": "HCM", "items": [item(f"Kien {i}", 1, 0.05) for i in range(51)]})
    check("A13 Ký gửi: 51 kiện → 400 (tối đa 50)", is_vietnamese_400(s, r) and "50" in r.get("message", ""), (s, r))
    return customer_id


def domestic_rule_id():
    s, r = call("GET", "/api/pricing-rules?orderType=PURCHASE")
    rules = data(r)
    if isinstance(rules, dict):
        rules = rules.get("items") or []
    for rule in rules or []:
        if "DOMESTIC_FEE" in (str(rule.get("ruleCode", "")).upper(), str(rule.get("ruleType", "")).upper()):
            return rule.get("id") or rule.get("ruleId") or rule.get("pricingRuleId")
    s, r = call("GET", "/api/pricing-rules")
    for rule in data(r) or []:
        if "DOMESTIC_FEE" in (str(rule.get("ruleCode", "")).upper(), str(rule.get("ruleType", "")).upper()):
            return rule.get("id") or rule.get("ruleId") or rule.get("pricingRuleId")
    return None


def quote(request_id, domestic, extra_fees):
    items = request_items(request_id)
    a, b = items[f"Tai nghe {tag(request_id)}"], items[f"Ao khoac {tag(request_id)}"]
    return call("POST", f"/api/purchase-requests/{request_id}/quotation", "sale", {
        "purchaseFee": 80000, "domesticShippingFee": domestic, "freightRatePerKg": 120000, "estimatedWeight": 2,
        "items": [{"purchaseRequestItemId": a, "unitPrice": 500000}, {"purchaseRequestItemId": b, "unitPrice": 200000}],
        "additionalFees": extra_fees}), a, b


TAGS = {}


def tag(request_id):
    return TAGS[request_id]


def new_request(suffix):
    s, request_id, d = create_request(suffix)
    TAGS[request_id] = suffix
    return s, request_id, d


def expected_prepay(q, domestic):
    vat = q.get("vatRate") or 0
    fees = 80000 + domestic
    return 1600000 + fees + round(fees * vat / 100)


def part_b(rule_id):
    fee = lambda amount: ({"pricingRuleId": rule_id, "feeName": "Phí vận chuyển nội địa", "feeType": "DOMESTIC_FEE", "amount": amount}
                          if rule_id else {"feeName": "Phí vận chuyển nội địa", "feeType": "DOMESTIC_FEE", "amount": amount})

    s, req1, d = new_request(f"HB1{RUN}")
    check("B0 Khách tạo yêu cầu mua hộ #1", s in (200, 201) and req1, d)
    (s, r), a1, b1 = quote(req1, 30000, [fee(5000)])
    q1 = data(r)
    check("B1 Có ô ship nội địa 30.000 + rule DOMESTIC_FEE 5.000 → chỉ tính 30.000 một lần",
          s in (200, 201) and q1.get("domesticShippingFee") == 30000 and q1.get("prepayAmount") == expected_prepay(q1, 30000), (s, r))
    check("B2 Không còn dòng phụ phí DOMESTIC_FEE trong báo giá",
          not any("DOMESTIC" in str(f.get("feeType", "")).upper() for f in (q1.get("additionalFees") or [])), q1.get("additionalFees"))

    s, req2, d = new_request(f"HB2{RUN}")
    (s, r), _, _ = quote(req2, 0, [fee(25000)])
    q2 = data(r)
    check("B3 Ô ship nội địa trống + rule DOMESTIC_FEE 25.000 → ship nội địa 25.000 (một lần)",
          s in (200, 201) and q2.get("domesticShippingFee") == 25000 and q2.get("prepayAmount") == expected_prepay(q2, 25000), (s, r))
    return req1, a1, b1, q1, req2


def checkout_path(checkout_url):
    u = urllib.parse.urlsplit(checkout_url)
    return u.path + (("?" + u.query) if u.query else "")


def part_c(req1, q1, req2):
    ret = f"{FRONT}/payment/lich-su?loai=mua-ho"
    cancel = f"{FRONT}/orders/mua-ho/{req1}/bao-gia"
    s, r = call("PUT", f"/api/purchase-requests/{req1}/quotation/confirm-and-pay", "customer",
                {"paymentMethod": "SEPAY", "returnUrl": ret, "cancelUrl": cancel})
    pay = data(r)
    code = str(pay.get("orderCode"))
    check("C1 confirm-and-pay SePay: link QR mang theo returnUrl/cancelUrl web gửi",
          s == 200 and "returnUrl=" in (pay.get("checkoutUrl") or "") and "cancelUrl=" in (pay.get("checkoutUrl") or ""), (s, r))

    s, h, html = raw("GET", checkout_path(pay.get("checkoutUrl") or ""))
    check("C2 Trang QR: nút Huỷ về cancelUrl + ?orderCode=&status=cancelled",
          s == 200 and f"{cancel}?orderCode={code}&amp;status=cancelled" in html, html[-2500:] if s == 200 else s)
    check("C3 Trang QR: trả xong chuyển về returnUrl + orderCode + status=success",
          f"orderCode={code}" in html and "status=success" in html and "lich-su" in html, s)

    s, h, html = raw("GET", f"/api/payments/sepay/checkout/{code}?returnUrl=https://evil.example/x&cancelUrl=https://evil.example/y")
    check("C4 Tự gõ returnUrl sang miền lạ → bỏ qua, về trang lịch sử cũ kèm mã (chống open-redirect)",
          s == 200 and "evil.example" not in html and f"/history/buy-on-behalf?orderCode={code}" in html, html[-1500:] if s == 200 else s)

    inj = urllib.parse.quote(f"{FRONT}/x?a=';alert(1)//", safe="")
    s, h, html = raw("GET", f"/api/payments/sepay/checkout/{code}?returnUrl={inj}&cancelUrl={inj}")
    check("C5 Dấu ' trong URL được mã hoá, không phá chuỗi JS/HTML", s == 200 and "';alert(1)" not in html, s)

    s, h, html = raw("GET", f"/api/payments/sepay/checkout/{code}")
    check("C6 Link QR cũ (không query) vẫn chạy: về /history/buy-on-behalf kèm mã",
          s == 200 and f"/history/buy-on-behalf?orderCode={code}&amp;status=cancelled" in html, s)

    s, r = pay_manually(pay.get("paymentId"), q1.get("prepayAmount"), f"HC{RUN}")
    expect("C7 Admin ghi nhận khách đã trả trước", s, r, 200)
    s, h, _ = raw("GET", checkout_path(pay.get("checkoutUrl") or ""))
    loc = h.get("Location") or h.get("location") or ""
    check("C8 Đã trả: mở lại link QR → chuyển về returnUrl kèm orderCode + status=success",
          s in (301, 302) and loc.startswith(ret) and f"orderCode={code}" in loc and "status=success" in loc, (s, loc))

    s, r = call("PUT", f"/api/purchase-requests/{req2}/quotation/confirm-and-pay", "customer",
                {"paymentMethod": "SEPAY", "returnUrl": "https://evil.example/pay", "cancelUrl": "javascript:alert(1)"})
    pay2 = data(r)
    check("C9 returnUrl miền lạ lúc tạo khoản thu → không đưa vào link QR",
          s == 200 and "evil.example" not in (pay2.get("checkoutUrl") or "") and "javascript" not in (pay2.get("checkoutUrl") or ""), (s, r))
    return pay


def list_row(code, who="sale"):
    s, r = call("GET", f"/api/purchase-requests?pageSize=5&searchKeyword={urllib.parse.quote(code)}", who)
    rows = (data(r) or {}).get("items") or []
    return s, next((x for x in rows if x.get("purchaseCode") == code), {})


def part_d(req1, a1, q1):
    s, r = call("GET", f"/api/purchase-requests/{req1}", "sale")
    detail = data(r)
    code = detail.get("purchaseCode")
    s, row = list_row(code)
    # Tên: hồ sơ khách, trống thì lấy tên tài khoản của khách (chi tiết chỉ đọc hồ sơ khách nên có thể rỗng).
    name_ok = row.get("customerName") == detail.get("customerName") if detail.get("customerName") else bool(row.get("customerName"))
    check("D1 Danh sách có tên / mã / SĐT khách (không phải gọi danh bạ khách)",
          s == 200 and name_ok and row.get("customerCode") and "customerPhone" in row, row)
    check("D2 Tiền trả trước đã thu = số khách đã trả", row.get("prepaidAmount") == q1.get("prepayAmount"), row)
    check("D3 Chưa lập đơn mua: còn 2 dòng / 5 sản phẩm mua được, 0 đơn mua",
          row.get("openLineCount") == 2 and row.get("openQuantity") == 5 and row.get("activePurchaseOrderCount") == 0, row)

    s, r = call("POST", "/api/suppliers", "admin", {
        "supplierCode": f"NCC-H{RUN}", "supplierName": f"NCC Hardening {RUN}", "supplierType": "GOODS",
        "country": "KR", "email": f"ncch{RUN}@example.com"})
    supplier_id = data(r).get("id") or data(r).get("supplierId")
    s, r = call("POST", f"/api/purchase-requests/{req1}/purchase-orders", "sale", {
        "supplierId": supplier_id, "warehouseId": KR, "currency": "VND",
        "items": [{"purchaseRequestItemId": a1, "quantity": 2, "unitPrice": 500000}]})
    check("D4 Sale lập đơn mua cho dòng tai nghe", s in (200, 201), (s, r))
    s, row = list_row(code)
    check("D5 Sau khi lập đơn: 1 đơn mua, còn 1 dòng / 3 sản phẩm mua được",
          row.get("activePurchaseOrderCount") == 1 and row.get("openLineCount") == 1 and row.get("openQuantity") == 3, row)
    s, row = list_row(code, "customer")
    check("D6 Khách xem danh sách của mình vẫn chạy (có trường mới)", s == 200 and row.get("purchaseCode") == code, row)


def main():
    call("PUT", f"/api/warehouses/{KR}/manager", "admin", {"managerId": None, "reason": "Kiểm thử chặn đầu vào"})
    part_a()
    rule_id = domestic_rule_id()
    print("  (rule DOMESTIC_FEE:", "có" if rule_id else "không có — gửi feeType DOMESTIC_FEE", ")")
    req1, a1, b1, q1, req2 = part_b(rule_id)
    part_c(req1, q1, req2)
    part_d(req1, a1, q1)
    print(f"\n{len(PASSED)} PASS, {len(FAILED)} FAIL")
    for f in FAILED:
        print("  FAIL:", f)
    return 1 if FAILED else 0


if __name__ == "__main__":
    sys.exit(main())
