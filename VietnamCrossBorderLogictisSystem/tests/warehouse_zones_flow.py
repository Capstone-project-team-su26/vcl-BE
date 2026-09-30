#!/usr/bin/env python3
"""
Kiểm thử luật xếp kệ theo loại khu (chỉ khu STORAGE nhận hàng lưu kho) trên môi trường thử:
xếp kệ bằng mã ô, chuyển ô, tách kiện, gợi ý vị trí, khai loại khu, báo cáo kiện nằm sai khu.

    APP_ENV=/root/vcl-migration-deploy/app.env API=http://127.0.0.1:8088 python3 warehouse_zones_flow.py
"""
import sys

from wave1_consignment_flow import IDS, PASSED, FAILED, RUN, call, check, expect, create_order, item, get_order_quotation


def received_parcels(names, suffix):
    """Đơn ký gửi đi hết tới lúc kho check-in đủ kiện; trả parcelId theo tên hàng."""
    kr = IDS["warehouseKr"]
    s, order = create_order([item(n, 1, 1.0) for n in names], suffix)
    order_id = order["orderId"]
    call("POST", f"/api/orders/{order_id}/quotation/send", "sale", {"warehouseId": kr})
    _, q = get_order_quotation(order_id)
    _, pay = call("PUT", f"/api/quotations/{q['data']['quotationId']}/confirm-and-pay", "customer", {"paymentMethod": "OFFLINE"})
    pay = pay["data"]
    call("PUT", f"/api/admin/finance/transactions/{pay['paymentId']}/approve", "admin",
         {"transactionCode": f"FT-KHU-{RUN}-{suffix}", "receivedAmount": pay["amount"]})
    _, r = call("POST", "/api/warehouse-receiving-notes", "sale", {"consignmentOrderId": order_id, "warehouseId": kr})
    note = r["receivingNote"]
    call("PUT", f"/api/warehouse-receiving-notes/{note['id']}/status", "om1", {"status": "APPROVED"})
    _, detail = call("GET", f"/api/warehouse-receiving-notes/{note['id']}", "om1")
    packages = [{"orderItemId": e["orderItemId"], "actualWeight": 1.0, "length": 20, "width": 20, "height": 10}
                for e in detail["data"]["expectedItems"]]
    s, r = call("POST", "/api/parcels/check-in", "staff", {"shippingCode": note["receivingNoteCode"], "packages": packages})
    data = r.get("data", {})
    parcels = data.get("parcels") or [data]
    return s, order_id, {p["productName"]: p["parcelId"] for p in parcels}


def main():
    kr = IDS["warehouseKr"]
    call("PUT", f"/api/warehouses/{kr}/manager", "admin", {"managerId": None, "reason": "Chuẩn bị kiểm thử khu kho"})

    # ---------------- Khu của kho (khôi phục khu lưu kho nếu lần chạy trước dừng giữa chừng)
    s, r = call("GET", f"/api/warehouses/{kr}/zones", "staff")
    for z in r.get("data", []):
        if z["zoneName"] == "Khu A":
            call("PUT", f"/api/warehouse-zones/{z['zoneId']}", "om1", {"zoneType": "STORAGE", "status": "ACTIVE"})
    s, r = call("GET", f"/api/warehouses/{kr}/zones", "staff")
    zones = r.get("data", [])
    by_name = {z["zoneName"]: z for z in zones}
    check("Danh sách khu kho trả loại khu", s == 200 and by_name.get("Khu A", {}).get("zoneType") == "STORAGE"
          and by_name.get("Khu xuất", {}).get("zoneType") == "OUTBOUND" and by_name.get("Khu nhận", {}).get("zoneType") == "RECEIVING", zones)
    check("Chỉ khu STORAGE đang dùng mới nhận hàng lưu kho", all(z["acceptsStorage"] == (z["zoneType"] == "STORAGE" and z["status"] == "ACTIVE") for z in zones), zones)
    storage_zone, receiving_zone = by_name["Khu A"], by_name["Receiving"]

    s, r = call("GET", f"/api/warehouses/{kr}/misplaced-parcels", "staff")
    misplaced_before = r.get("data", [])
    check("Báo cáo kiện đang nằm ở khu xuất (dữ liệu cũ)", s == 200 and any(m["zoneType"] == "OUTBOUND" for m in misplaced_before), r)

    # ---------------- Khai loại khu
    s, r = call("PUT", f"/api/warehouse-zones/{receiving_zone['zoneId']}", "staff", {"zoneType": "STORAGE"})
    expect("Nhân viên kho không đổi loại khu", s, r, 403)
    s, r = call("PUT", f"/api/warehouse-zones/{receiving_zone['zoneId']}", "om1", {"zoneType": "KHO_LANH"})
    expect("Loại khu lạ bị từ chối", s, r, 400)
    s, r = call("PUT", f"/api/warehouse-zones/{receiving_zone['zoneId']}", "om1", {"zoneType": "QUARANTINE"})
    check("Khu trống đổi loại được", s == 200 and r["data"]["zoneType"] == "QUARANTINE", r)
    call("PUT", f"/api/warehouse-zones/{receiving_zone['zoneId']}", "om1", {"zoneType": "RECEIVING"})

    # ---------------- Khai báo ô kệ
    new_zone = f"Khu test {RUN}"
    s, r = call("POST", f"/api/warehouses/{kr}/locations", "om1", {"zoneName": new_zone, "shelfCode": "S1", "binCode": f"BIN-T-{RUN}", "maxVolume": 100000, "maxWeight": 50})
    expect("Tạo khu mới qua khai báo ô kệ phải có loại khu", s, r, 400)
    s, r = call("POST", f"/api/warehouses/{kr}/locations", "om1", {"zoneName": new_zone, "zoneType": "OUTBOUND", "shelfCode": "S1", "binCode": f"BIN-T-{RUN}", "maxVolume": 100000, "maxWeight": 50})
    check("Tạo ô kệ ở khu mới có loại khu", s == 201 and r.get("zoneType") == "OUTBOUND", r)
    s, r = call("POST", f"/api/warehouses/{kr}/locations", "om1", {"zoneName": "Khu A", "zoneType": "OUTBOUND", "shelfCode": "S9", "binCode": f"BIN-T2-{RUN}"})
    expect("Không đổi loại khu có sẵn qua khai báo ô kệ", s, r, 400)

    # ---------------- Xếp kệ
    s, order_id, parcels = received_parcels(["Kiện K1", "Kiện K2", "Kiện K3"], "Z")
    if not expect("Chuẩn bị 3 kiện đã check-in", s, parcels, 200):
        return
    p1, p2, p3 = parcels["Kiện K1"], parcels["Kiện K2"], parcels["Kiện K3"]

    # Hệ thống bắt buộc có ảnh kiện trong ô trước khi ghi nhận xếp kệ. Chụp sẵn cho cả ba
    # kiện ở đây để các phép thử bên dưới kiểm ĐÚNG luật vị trí, không vướng luật ảnh.
    #
    # Nạp trễ trong thân hàm: export_flow lại import received_parcels từ chính file này,
    # để import ở đầu file là hai module nạp vòng tròn và cả hai cùng hỏng.
    from export_flow import upload

    for pid in (p1, p2, p3):
        upload("staff", "PARCEL", pid, "PUT_AWAY_PROOF", "anh-o-ke.png")

    s, r = call("PUT", f"/api/parcels/{p1}/put-away", "staff", {"warehouseId": kr, "binCode": "BIN-OUT-01"})
    # Câu lỗi nói tên khu tiếng Việt, không chèn mã loại khu (STORAGE).
    check("Không xếp hàng lưu kho vào ô khu xuất", s == 400 and "chỉ được xếp vào khu lưu kho" in r.get("message", ""), r)
    s, r = call("PUT", f"/api/parcels/{p1}/put-away", "staff", {"warehouseId": kr, "binCode": "BIN-RCV-01"})
    expect("Không xếp hàng lưu kho vào ô khu nhận", s, r, 400)
    s, r = call("PUT", f"/api/parcels/{p1}/put-away", "staff", {"warehouseId": kr, "binCode": "KHONG-CO"})
    expect("Mã ô không có trong kho", s, r, 404)
    s, r = call("PUT", f"/api/parcels/{p1}/put-away", "staff", {"warehouseId": kr, "binCode": "BIN-A1-02", "zoneId": by_name["Khu xuất"]["zoneId"]})
    expect("zoneId gửi kèm không khớp ô kệ thì bị chặn", s, r, 400)
    s, r = call("PUT", f"/api/parcels/{p1}/put-away", "staff", {"warehouseId": kr, "binCode": "bin-a1-02"})
    check("Quét mã ô kệ khu lưu kho để xếp kệ", s == 200 and r["data"]["zoneType"] == "STORAGE" and r["data"]["binCode"] == "BIN-A1-02", r)
    bin_ = IDS["bin"]
    s, r = call("PUT", f"/api/parcels/{p2}/put-away", "staff", {"warehouseId": kr, "zoneId": bin_["zoneId"], "shelfId": bin_["shelfId"], "binId": bin_["binId"]})
    expect("Client cũ gửi đủ 4 ID vẫn xếp kệ được", s, r, 200)

    s, r = call("PUT", f"/api/warehouse-zones/{storage_zone['zoneId']}", "om1", {"zoneType": "OUTBOUND"})
    expect("Khu lưu kho đang có hàng không đổi loại được", s, r, 400)
    s, r = call("PUT", f"/api/warehouse-zones/{storage_zone['zoneId']}", "om1", {"zoneType": "STORAGE", "status": "INACTIVE"})
    expect("Khu lưu kho đang có hàng không ngừng dùng được", s, r, 400)

    # ---------------- Gợi ý vị trí
    storage_bins = set()
    s, lr = call("GET", f"/api/warehouses/{kr}/layout/zones", "staff")
    for z in lr.get("data", []):
        for sh in z.get("shelves", []):
            for b in sh.get("bins", []):
                if z.get("zoneType") == "STORAGE":
                    storage_bins.add(b["binId"].lower())
    s, r = call("POST", "/api/ai/warehouse/putaway-suggestions", "staff", {"parcelId": p3, "warehouseId": kr})
    suggestions = r.get("data", {}).get("suggestedLocations", []) if s == 200 else None
    check("Gợi ý vị trí chỉ trả ô thuộc khu lưu kho",
          suggestions is not None and all((x.get("binId") or "").lower() in storage_bins for x in suggestions), r)

    # ---------------- Chuyển ô
    s, inv = call("GET", f"/api/inventories?warehouseId={kr}", "staff")
    inv1 = next((i for i in inv.get("items", []) if str(i.get("parcelId", "")).lower() == p1.lower()), None)
    if not check("Có tồn kho của kiện vừa xếp", inv1 is not None, inv):
        return
    s, r = call("POST", f"/api/inventories/{inv1['inventoryId']}/relocate", "staff", {"toBinCode": "BIN-OUT-01", "reason": "thử"})
    expect("Không chuyển hàng lưu kho sang ô khu xuất", s, r, 400)
    s, r = call("POST", f"/api/inventories/{inv1['inventoryId']}/relocate", "staff", {"toBinCode": "BIN-A1-01", "reason": "Sắp xếp lại"})
    check("Chuyển ô trong khu lưu kho bằng mã ô", s == 200 and str(r["data"]["binId"]).lower() != "", r)

    if misplaced_before:
        wrong = misplaced_before[0]
        s, inv_all = call("GET", f"/api/inventories?warehouseId={kr}", "staff")
        movable = [m for m in misplaced_before
                   if any(str(i.get("parcelId", "")).lower() == m["parcelId"].lower() and i.get("status") not in ("RELEASED", "PICKED")
                          for i in inv_all.get("items", []))]
        wrong = movable[0] if movable else wrong
        stuck = next((i for i in inv_all.get("items", []) if str(i.get("parcelId", "")).lower() == wrong["parcelId"].lower()
                      and i.get("status") not in ("RELEASED", "PICKED")), None)
        # Kiện đang nằm trong một phiếu xuất còn hiệu lực bị khoá không cho điều chuyển (luồng
        # xuất kho), nên thử lần lượt cho tới khi gặp kiện thật sự chuyển được.
        moved = None
        last = None
        for candidate in (movable or [wrong]):
            row = next((i for i in inv_all.get("items", []) if str(i.get("parcelId", "")).lower() == candidate["parcelId"].lower()
                        and i.get("status") not in ("RELEASED", "PICKED")), None)
            if not row:
                continue
            s, r = call("POST", f"/api/inventories/{row['inventoryId']}/relocate", "staff",
                        {"toBinCode": "BIN-A1-01", "reason": "Chuyển khỏi khu xuất"})
            last = r
            if s == 200:
                moved = candidate
                break

        if moved:
            s2, after = call("GET", f"/api/warehouses/{kr}/misplaced-parcels", "staff")
            check("Chuyển kiện nằm sai khu về khu lưu kho, báo cáo bớt đi",
                  all(m["parcelId"] != moved["parcelId"] for m in after.get("data", [])), after)
        elif stuck:
            check("Kiện sai khu còn lại đều đang bị khoá trong phiếu xuất",
                  "phiếu xuất" in (last or {}).get("message", ""), last)

    # ---------------- Tách kiện
    split_bad = {"children": [
        {"packageCode": f"SPL-{RUN}-1", "quantity": 0, "actualWeight": 0.5, "length": 10, "width": 10, "height": 10, "binId": None},
        {"packageCode": f"SPL-{RUN}-2", "quantity": 1, "actualWeight": 0.5, "length": 10, "width": 10, "height": 10}]}
    s, lay = call("GET", f"/api/warehouses/{kr}/locations", "staff")
    out_bin = next((b["locationId"] for b in lay.get("items", []) if b.get("binCode") == "BIN-OUT-01"), None)
    split_bad["children"][0]["binId"] = out_bin
    s, r = call("POST", f"/api/parcels/{p2}/split", "staff", split_bad)
    expect("Tách kiện không đưa kiện con sang ô khu xuất", s, r, 400)
    split_bad["children"][0].pop("binId")
    s, r = call("POST", f"/api/parcels/{p2}/split", "staff", split_bad)
    expect("Tách kiện giữ kiện con ở ô khu lưu kho cũ", s, r, 200)
    children = [c.get("parcelId") or c.get("id") for c in r.get("data", {}).get("children", [])]

    # ---------------- Gộp kiện
    if check("Tách kiện trả 2 kiện con", len(children) == 2 and all(children), r):
        merge = {"sourceParcelIds": children, "packageCode": f"MRG-{RUN}", "actualWeight": 1.0,
                 "length": 20, "width": 20, "height": 10, "binId": out_bin}
        s, r = call("POST", "/api/parcels/merge", "staff", merge)
        expect("Gộp kiện không đưa kiện gộp sang ô khu xuất", s, r, 400)
        merge.pop("binId")
        s, r = call("POST", "/api/parcels/merge", "staff", merge)
        expect("Gộp kiện về lại ô khu lưu kho", s, r, 200)


    print(f"\n{len(PASSED)} passed, {len(FAILED)} failed")
    if FAILED:
        print("FAILED:", *FAILED, sep="\n  - ")
        sys.exit(1)


if __name__ == "__main__":
    main()
