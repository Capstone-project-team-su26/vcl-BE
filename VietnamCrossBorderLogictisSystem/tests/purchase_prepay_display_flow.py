#!/usr/bin/env python3
"""
Kiểm thử MỘT NGUỒN SỰ THẬT cho số tiền khách trả khi xác nhận báo giá mua hộ:
- chi tiết yêu cầu (GET /api/purchase-requests/{id}) và GET .../quotation trả sẵn depositAmount /
  remainingAmount / depositDescription / canPayOnline — web khách hiện đúng số này, không tự tính cọc;
- confirm-and-pay tạo khoản thu ĐÚNG bằng depositAmount đã hiện;
- rule thuế NK / VAT gửi kèm như "phụ phí" bị bỏ qua (backend tự tính) — không thu hai lần, thuế NK
  không bị kéo vào phần trả trước.

    APP_ENV=/root/vcl-migration-deploy/app.env API=http://127.0.0.1:8088 python3 purchase_prepay_display_flow.py
"""
import sys

from wave1_consignment_flow import PASSED, FAILED, RUN, call, check, expect
from purchase_flow import data, create_request, request_items


def main():
    s, rules = call("GET", "/api/pricing-rules", "admin")
    rules = rules if isinstance(rules, list) else rules.get("data", [])
    by_code = {x.get("ruleCode"): x for x in rules}
    if not check("Danh mục phí có VAT, thuế NK, thùng gỗ", {"VAT", "IMPORT_TAX", "WOOD_CRATE"} <= set(by_code), sorted(by_code)):
        return 1

    s, request_id, d = create_request(f"P{RUN}")
    if not check("Khách tạo yêu cầu mua hộ", s in (200, 201) and request_id, d):
        return 1
    items = request_items(request_id)
    item_a, item_b = items[f"Tai nghe P{RUN}"], items[f"Ao khoac P{RUN}"]

    def fee(code, amount):
        rule = by_code[code]
        return {"pricingRuleId": rule["id"], "feeName": rule.get("ruleName"), "feeType": rule.get("ruleType"), "amount": amount}

    # Web nhân viên luôn gửi kèm rule VAT + thuế NK như phụ phí → backend phải bỏ qua hai dòng này.
    quote_body = {"purchaseFee": 80000, "domesticShippingFee": 30000, "freightRatePerKg": 120000, "estimatedWeight": 2,
                  "items": [{"purchaseRequestItemId": item_a, "unitPrice": 500000},
                            {"purchaseRequestItemId": item_b, "unitPrice": 200000}],
                  "additionalFees": [fee("WOOD_CRATE", 35000), fee("IMPORT_TAX", 160000), fee("VAT", 16480)]}
    s, r = call("POST", f"/api/purchase-requests/{request_id}/quotation", "sale", quote_body)
    q = data(r)
    if not check("Sale báo giá kèm phụ phí thùng gỗ + (thừa) rule VAT/thuế NK", s in (200, 201), r):
        return 1
    # hàng 1.600.000 + phí mua 80.000 + ship nội địa 30.000 + thùng gỗ 35.000 + VAT 8% × 145.000 (11.600)
    prepay = 1600000 + 80000 + 30000 + 35000 + 11600
    # cước 2kg × 120.000 + VAT 19.200 + thuế NK theo loại hàng 148.000
    later = 240000 + 19200 + 148000
    check("Rule thuế NK / VAT gửi kèm không bị cộng vào phần trả trước", q.get("prepayAmount") == prepay, q)
    check("Thuế NK chỉ nằm ở phần tạm tính (một lần)", q.get("estimatedLaterAmount") == later, q)
    names = [f.get("feeType") for f in q.get("additionalFees", [])]
    check("Báo giá không lưu dòng phụ phí IMPORT_TAX / VAT", not ({"IMPORT_TAX", "VAT"} & set(names)), names)
    check("Response lập báo giá trả sẵn số khách phải trả khi xác nhận",
          q.get("depositAmount") == prepay and q.get("remainingAmount") == later and q.get("canPayOnline") is True, q)

    s, r = call("GET", f"/api/purchase-requests/{request_id}", "customer")
    dq = (data(r) or {}).get("quotation") or {}
    check("Chi tiết yêu cầu (màn khách) có phần trả trước / tạm tính",
          s == 200 and dq.get("prepayAmount") == prepay and dq.get("estimatedLaterAmount") == later and dq.get("isLegacy") is False, dq)
    check("Chi tiết yêu cầu trả depositAmount/remainingAmount/canPayOnline/depositDescription",
          dq.get("depositAmount") == prepay and dq.get("remainingAmount") == later and dq.get("canPayOnline") is True
          and "Trả trước" in (dq.get("depositDescription") or ""), dq)
    check("Chi tiết yêu cầu có ship nội địa / đơn giá cước / cân tạm tính",
          dq.get("domesticShippingFee") == 30000 and dq.get("freightRatePerKg") == 120000 and dq.get("estimatedWeight") == 2, dq)

    s, r = call("GET", f"/api/purchase-requests/{request_id}/quotation", "customer")
    gq = data(r) or {}
    check("GET báo giá cùng số với chi tiết yêu cầu",
          s == 200 and gq.get("depositAmount") == dq.get("depositAmount") and gq.get("depositDescription") == dq.get("depositDescription"), gq)

    s, r = call("PUT", f"/api/purchase-requests/{request_id}/quotation/confirm-and-pay", "customer", {"paymentMethod": "SEPAY"})
    pay = data(r) or {}
    check("confirm-and-pay tạo khoản thu ĐÚNG số màn khách đã hiện",
          s == 200 and pay.get("amount") == dq.get("depositAmount") and pay.get("depositAmount") == dq.get("depositAmount")
          and pay.get("remainingAmount") == dq.get("remainingAmount"), r)
    check("confirm-and-pay cùng diễn giải với màn khách", pay.get("depositDescription") == dq.get("depositDescription"), pay)

    s, r = call("GET", f"/api/purchase-requests/{request_id}", "customer")
    after = (data(r) or {}).get("quotation") or {}
    check("Sau khi xác nhận: báo giá ACCEPTED, vẫn giữ số trả trước", after.get("status") == "ACCEPTED" and after.get("depositAmount") == prepay, after)

    print(f"\n{len(PASSED)} PASS, {len(FAILED)} FAIL")
    return 1 if FAILED else 0


if __name__ == "__main__":
    sys.exit(main())
