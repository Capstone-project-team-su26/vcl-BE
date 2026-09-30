#!/usr/bin/env python3
"""
Kiểm thử dịch vụ theo từng kiện của đơn ký gửi (thùng gỗ + kiểm hàng/bảo hiểm/đóng lại carton)
qua HTTP thật trên môi trường thử, dùng chung khung của wave1_consignment_flow.py.

    APP_ENV=/root/vcl-migration-deploy/app.env API=http://127.0.0.1:8088 python3 item_services_flow.py
"""
import sys, uuid, urllib.request, urllib.error

from wave1_consignment_flow import API, IDS, TOK, PASSED, FAILED, RUN, call, check, expect, get_order_quotation


def item(name, weight, services=None, declared=100000, crate=True):
    body = {"productName": name, "productType": IDS["productTypeId"], "quantity": 1, "weight": weight,
            "length": 20, "width": 20, "height": 10, "declaredValue": declared,
            "services": [{"pricingRuleId": s} for s in (services or [])]}
    if crate:
        body["packageConfigurationId"] = IDS["packageConfigurationId"]
    return body


def create(items, extra=None):
    body = {"route": "KR-VN", "shippingOption": "Express", "receiverName": "Test DV " + RUN,
            "receiverPhone": "0900000000", "receiverAddress": "HCM", "items": items}
    body.update(extra or {})
    s, r = call("POST", "/api/orders/consignments", "customer", body)
    return s, r.get("data", r)


def main():
    kr = IDS["warehouseKr"]

    # ---------------- Danh mục dịch vụ theo kiện
    s, r = call("GET", "/api/orders/consignments/item-services")
    codes = {x["code"]: x["pricingRuleId"] for x in r.get("data", [])}
    check("Danh mục dịch vụ theo kiện mở không cần đăng nhập", s == 200, r)
    check("Có kiểm hàng, bảo hiểm, đóng lại carton",
          {"SUR_INSPECTION", "SUR_INSURANCE_3PERCENT", "CARTON_REPACK"} <= set(codes), codes)
    ins_dto = next((x for x in r.get("data", []) if x["code"] == "SUR_INSURANCE_3PERCENT"), {})
    check("Danh mục trả kèm điều kiện áp dụng để màn khách hiện và chặn",
          ins_dto.get("conditionType") == "MIN_DECLARED_VALUE" and str(ins_dto.get("conditionValue", "")).startswith("5000000"),
          ins_dto)

    check("Không có thùng gỗ, thuế, cước nội địa trong danh mục",
          not ({"WOOD_CRATE", "VAT", "IMPORT_TAX", "DOMESTIC_FEE", "VOLUMETRIC_DIVISOR"} & set(codes)), codes)
    inspection, insurance, repack = codes["SUR_INSPECTION"], codes["SUR_INSURANCE_3PERCENT"], codes["CARTON_REPACK"]

    s, rules = call("GET", "/api/pricing-rules", "admin")
    rules = rules if isinstance(rules, list) else rules.get("data", [])
    wood = next(x["id"] for x in rules if x["ruleCode"] == "WOOD_CRATE")
    vat = next(x["id"] for x in rules if x["ruleCode"] == "VAT")

    # ---------------- Chặn dữ liệu sai lúc tạo đơn
    s, r = create([item("Thiếu thùng DV", 1.0, crate=False)])
    expect("Kiện thiếu cấu hình thùng gỗ bị chặn", s, r, 400)

    bad_crate = item("Thùng sai DV", 1.0)
    bad_crate["packageConfigurationId"] = str(uuid.uuid4())
    s, r = create([bad_crate])
    expect("Cấu hình thùng không tồn tại bị chặn (không rơi xuống lỗi 500)", s, r, 400)

    s, r = create([item("Chọn thùng gỗ ở dịch vụ", 1.0, [wood])])
    check("Không chọn rule thùng gỗ làm dịch vụ kèm kiện (tránh tính hai lần)", s == 400 and "packageConfigurationId" in r.get("message", ""), r)

    s, r = create([item("Chọn VAT", 1.0, [vat])])
    expect("Không chọn thuế làm dịch vụ kèm kiện", s, r, 400)

    s, r = create([item("Trùng DV", 1.0, [inspection, inspection])])
    expect("Một kiện chọn trùng dịch vụ bị chặn", s, r, 400)

    s, r = create([item("Bảo hiểm không giá trị", 1.0, [insurance], declared=None)])
    expect("Bảo hiểm tính theo giá trị hàng phải có giá trị khai báo", s, r, 400)

    s, r = create([item("Dịch vụ lạ", 1.0, [str(uuid.uuid4())])])
    expect("Dịch vụ không tồn tại bị chặn", s, r, 400)

    # ---------------- Tạo đơn: mỗi kiện một bộ dịch vụ
    # Bảo hiểm có điều kiện MIN_DECLARED_VALUE = 5.000.000 (PRICING_RULES.condition_value),
    # nên kiện mua bảo hiểm phải khai giá từ mức đó trở lên.
    s, r = create([item("Bảo hiểm dưới ngưỡng", 1.0, [insurance], declared=100000)])
    expect("Bảo hiểm chỉ cho kiện khai giá từ 5.000.000 đ", s, r, 400)

    s, order = create([item("Áo DV", 1.0, [inspection, insurance], declared=5000000), item("Giày DV", 1.5, [repack])])
    if not expect("Khách tạo đơn, mỗi kiện kèm dịch vụ riêng", s, order, 201 if s == 201 else 200):
        return
    order_id = order["orderId"]
    est = order["quotation"]
    items = {i["productName"]: i for i in est["items"]}
    shirt, shoes = items["Áo DV"], items["Giày DV"]
    check("Báo giá tạm tính trả dịch vụ theo từng kiện",
          {x["code"] for x in shirt["services"]} == {"SUR_INSPECTION", "SUR_INSURANCE_3PERCENT"}
          and [x["code"] for x in shoes["services"]] == ["CARTON_REPACK"], est["items"])
    check("Kiện trả kèm thùng gỗ đã chọn", shirt["packageConfiguration"] is not None, shirt)
    check("Đơn có kiện kiểm hàng thì requiresInspection = true", order["requiresInspection"] is True, order)

    fees = [f for f in est["additionalFees"] if f.get("orderItemId")]
    by = {(f["orderItemId"], f["code"]): f for f in fees}
    check("Phí kiểm hàng tính riêng cho kiện Áo (10.000đ)", by.get((shirt["id"], "SUR_INSPECTION"), {}).get("amount") == 10000, fees)
    check("Bảo hiểm 3% giá trị kiện (3% × 5.000.000 = 150.000đ)", by.get((shirt["id"], "SUR_INSURANCE_3PERCENT"), {}).get("amount") == 150000, fees)
    check("Phí đóng lại carton chỉ tính cho kiện Giày", by.get((shoes["id"], "CARTON_REPACK"), {}).get("amount") == 25500
          and (shirt["id"], "CARTON_REPACK") not in by, fees)
    check("Dòng phí dịch vụ ghi tên kiện", by.get((shoes["id"], "CARTON_REPACK"), {}).get("itemName") == "Giày DV", fees)
    check("Phí thùng gắn đúng từng kiện", sum(1 for f in fees if f["feeType"] == "PACKING_FEE") == 2, fees)

    s, detail = call("GET", f"/api/orders/consignments/{order_id}", "customer")
    ditems = {i["productName"]: i for i in detail["data"]["items"]}
    check("Chi tiết đơn trả dịch vụ theo kiện", len(ditems["Áo DV"]["services"]) == 2 and len(ditems["Giày DV"]["services"]) == 1, detail)

    # Trường dịch vụ cấp đơn cũ không còn tác dụng
    s, legacy = create([item("Cờ cũ DV", 0.5)], {"requiresInspection": True, "requiresInsurance": True, "pricingRuleIds": [inspection]})
    legacy_fees = [f for f in legacy["quotation"]["additionalFees"] if f.get("code") == "SUR_INSPECTION" and f["amount"] > 0] if s in (200, 201) else None
    check("Cờ/pricingRuleIds cấp đơn bị bỏ qua: không phát sinh phí", s in (200, 201) and legacy["requiresInspection"] is False and legacy_fees == [], legacy)

    # ---------------- Báo giá chính thức
    s, r = call("POST", f"/api/orders/{order_id}/quotation/send", "sale", {"warehouseId": kr})
    check("Sale báo giá: dịch vụ theo kiện tự vào báo giá", s == 200 and r["status"] == "QUOTATION_SENT", r)
    s, q = get_order_quotation(order_id)
    qfees = [f for f in q["data"]["additionalFees"] if f.get("orderItemId") and f["feeType"] == "SURCHARGE"]
    check("Báo giá chính thức có 3 dòng dịch vụ theo kiện, tổng 185.500đ", len(qfees) == 3 and sum(f["amount"] for f in qfees) == 185500, qfees)
    # Báo giá đã tới tay khách: khách từ chối thì Sale mới lập lại được.
    s, r = call("PUT", f"/api/quotations/{q['data']['quotationId']}/reject", "customer", {"rejectionReason": "Xem lại phụ phí"})
    expect("Khách từ chối báo giá để Sale lập lại", s, r, 200)

    echo = {"warehouseId": kr, "quotation": {"additionalFees": [
        {"feeId": inspection, "label": "Phụ phí kiểm hàng", "amount": 10000, "enabled": True}]}}
    s, r = call("POST", f"/api/orders/{order_id}/quotation/send", "sale", echo)
    s2, q2 = get_order_quotation(order_id)
    insp = [f for f in q2["data"]["additionalFees"] if f.get("code") == "SUR_INSPECTION"]
    check("FE gửi lại dòng dịch vụ không kèm kiện: không tính hai lần, không thành giá ngoại lệ",
          s == 200 and r["status"] == "QUOTATION_SENT" and len(insp) == 1 and insp[0]["amount"] == 10000, (r, insp))
    s, r = call("PUT", f"/api/quotations/{q2['data']['quotationId']}/reject", "customer", {"rejectionReason": "Muốn giảm phí kiểm hàng"})
    expect("Khách từ chối lần nữa để Sale sửa giá", s, r, 200)

    wrong_item = {"warehouseId": kr, "quotation": {"additionalFees": [
        {"feeId": inspection, "orderItemId": shoes["id"], "amount": 5000, "enabled": True}]}}
    s, r = call("POST", f"/api/orders/{order_id}/quotation/send", "sale", wrong_item)
    expect("Sale không sửa phí dịch vụ kiện đó không chọn", s, r, 400)

    discount = {"warehouseId": kr, "quotation": {"additionalFees": [
        {"feeId": inspection, "orderItemId": shirt["id"], "amount": 5000, "enabled": True}]}}
    s, r = call("POST", f"/api/orders/{order_id}/quotation/send", "sale", discount)
    expect("Sửa phí dịch vụ của kiện phải ghi lý do", s, r, 400)

    discount["overrideReason"] = "Khách quen giảm phí kiểm hàng"
    s, r = call("POST", f"/api/orders/{order_id}/quotation/send", "sale", discount)
    check("Sửa phí dịch vụ của kiện → chờ Admin duyệt giá", s == 200 and r["status"] == "PENDING_PRICE_APPROVAL", r)
    s, sv = get_order_quotation(order_id, "sale")
    override_id = sv["data"]["quotationId"]
    line = next((f for f in sv["data"]["additionalFees"] if f.get("orderItemId") == shirt["id"] and f["code"] == "SUR_INSPECTION"), {})
    check("Dòng kiểm hàng kiện Áo mang số Sale sửa (5.000đ)", line.get("amount") == 5000, sv["data"]["additionalFees"])
    s, r = call("PUT", f"/api/quotations/{override_id}/price-approval", "admin", {"decision": "APPROVED", "note": "OK"})
    expect("Admin duyệt giá dịch vụ theo kiện", s, r, 200)

    # ---------------- Cọc → phiếu → kho thấy dịch vụ của từng kiện
    s, pay = call("PUT", f"/api/quotations/{override_id}/confirm-and-pay", "customer", {"paymentMethod": "OFFLINE"})
    if not expect("Khách xác nhận báo giá", s, pay, 200):
        return
    pay = pay["data"]
    s, r = call("PUT", f"/api/admin/finance/transactions/{pay['paymentId']}/approve", "admin",
                {"transactionCode": f"FT-DV-{RUN}", "receivedAmount": pay["amount"]})
    check("Admin xác nhận cọc", s == 200 and r["data"]["orderStatusAfter"] == "DEPOSIT_PAID", r)

    s, r = call("POST", "/api/warehouse-receiving-notes", "sale", {"consignmentOrderId": order_id, "warehouseId": kr})
    note = r.get("receivingNote") if s == 201 else None
    if not check("Sale lập phiếu nhập kho", note is not None, r):
        return
    s, r = call("PUT", f"/api/warehouse-receiving-notes/{note['id']}/status", "om1", {"status": "APPROVED"})
    expect("Quản lý kho duyệt phiếu", s, r, 200)

    s, scan = call("GET", f"/api/warehouse-receiving-notes/scan?code={note['receivingNoteCode']}", "staff")
    exp = {e["productName"]: e for e in scan.get("data", {}).get("expectedItems", [])}
    check("Quét phiếu: kho thấy thùng gỗ và dịch vụ từng kiện",
          s == 200 and exp["Áo DV"]["packageConfiguration"] is not None
          and {x["code"] for x in exp["Áo DV"]["services"]} == {"SUR_INSPECTION", "SUR_INSURANCE_3PERCENT"}
          and [x["code"] for x in exp["Giày DV"]["services"]] == ["CARTON_REPACK"], scan)

    s, r = call("POST", "/api/parcels/check-in", "staff", {"shippingCode": note["receivingNoteCode"], "packages": [
        {"orderItemId": shirt["id"], "actualWeight": 1.0, "length": 20, "width": 20, "height": 10}]})
    data = r.get("data", {})
    prog = {p["productName"]: p for p in data.get("packageProgress", [])}
    check("Check-in: tiến độ từng kiện kèm dịch vụ", s == 200 and len(prog["Áo DV"]["services"]) == 2 and prog["Giày DV"]["packageConfiguration"] is not None, r)
    parcel_id = data.get("parcelId")

    s, pend = call("GET", f"/api/parcels/pending-putaway?pageSize=200&search={order['consignmentCode']}", "staff")
    row = next((p for p in pend.get("data", {}).get("items", []) if p["id"] == parcel_id), None)
    check("Danh sách chờ xếp kệ trả thùng và dịch vụ của kiện", row is not None and len(row["services"]) == 2 and row["packageConfiguration"] is not None, pend)

    s, pd = call("GET", f"/api/parcels/{parcel_id}", "staff")
    check("Chi tiết kiện trả thùng và dịch vụ", s == 200 and len(pd.get("data", pd)["services"]) == 2 and pd.get("data", pd)["packageConfiguration"] is not None, pd)

    req = urllib.request.Request(API + f"/api/orders/consignments/{order_id}/receipt")
    req.add_header("Authorization", "Bearer " + TOK["sale"])
    try:
        with urllib.request.urlopen(req, timeout=120) as resp:
            check("Phiếu PDF vẫn sinh được khi kiện có dịch vụ", resp.status == 200 and resp.read()[:4] == b"%PDF", resp.status)
    except urllib.error.HTTPError as e:
        check("Phiếu PDF vẫn sinh được khi kiện có dịch vụ", False, e.code)

    print(f"\n{len(PASSED)} passed, {len(FAILED)} failed")
    if FAILED:
        print("FAILED:", *FAILED, sep="\n  - ")
        sys.exit(1)


if __name__ == "__main__":
    main()
