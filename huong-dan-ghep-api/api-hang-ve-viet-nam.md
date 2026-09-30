# Ghép API — Hàng về Việt Nam

Từ lúc lô vận chuyển về tới kho VN (`ARRIVED_DESTINATION`) đến khi khách nhận hàng và đơn hoàn thành.
Nối tiếp [`api-xuat-kho.md`](api-xuat-kho.md). Quy ước chung, tài khoản demo: [`README.md`](README.md).

**Luồng:**
```
① Lô về kho VN (Sale ghi mốc, xem api-xuat-kho.md mục I)
② Kho VN tiếp nhận: cân đo lại từng kiện — kiện tốt vào khu nhận, kiện lỗi vào cách ly + tự mở sự cố
③ Khách chọn từng kiện: GIAO NGAY hay GỬI KHO VN
④ Tất toán: cước tính lại theo cân đo VN → khách xem trước → Sale phát hành → khách trả
⑤a Gửi kho: phiếu nhập → quản lý kho duyệt → tự xếp ô        ⑤b Giao ngay: đi thẳng ⑥
⑥ Giao nội địa: khách/Sale đặt giao → quản lý kho duyệt → kho đặt GoShip
⑦ Giao xong (hãng báo, hoặc Sale ghi kèm ảnh ký nhận) · giao thất bại → kho nhận hàng hoàn → giao lại
⑧ Đóng đơn: khách xác nhận → hoàn thành ngay; không xác nhận → tự hoàn thành sau 3 ngày
   (còn khiếu nại / sự cố / bồi thường / khoản thu treo thì chưa đóng)
```

| Việc | Ai làm |
|---|---|
| Tiếp nhận, cân đo, mở sự cố, nhận hàng hoàn, đặt GoShip | `WarehouseStaff` (hoặc OM, Admin) |
| Chọn hướng từng kiện, xem trước tất toán, đặt giao, trả sự cố, khiếu nại, xác nhận đã nhận | `Customer` (đơn của mình) — Sale làm thay được |
| Phát hành đợt tất toán, báo phí giao lại, ghi bằng chứng giao | `Sale` |
| Quyết định sự cố, duyệt phiếu nhập / yêu cầu giao, chốt đơn tay | Quản lý kho VN; kho chưa gán quản lý → `OperationsManager` |
| Ghi nhận đã chi bồi thường | `Admin` |

---

## Trạng thái kiện ở Việt Nam (`packageStatus`)

| Giá trị | Nghĩa | Khách thấy (`stage` ở API theo dõi) |
|---|---|---|
| `ARRIVED_DESTINATION` | Lô đã về, kho chưa kiểm | `ARRIVED_DESTINATION` — Hàng đã về kho Việt Nam |
| `RECEIVED_AT_DESTINATION` | Kho đã kiểm, kiện tốt, ở khu nhận (chưa phải tồn lưu kho) | `RECEIVED_AT_VN` — Kho Việt Nam đã nhận và kiểm hàng |
| `QUARANTINED` | Kiện lỗi, ở khu cách ly chờ xử lý sự cố | `QUARANTINED` — Hàng đang được xử lý sự cố |
| `STORED` | Đã nhập kho VN, nằm trên kệ | `STORED_AT_VN` — Đang lưu tại kho Việt Nam |
| `AWAITING_PICKUP` · `PICKING_UP` · `PICKED_UP` · `OUT_FOR_DELIVERY` · `IN_TRANSIT` … | Đã đặt giao, hãng đang xử lý | `OUT_FOR_DELIVERY` — Đang giao hàng |
| `DELIVERY_FAILED` · `RETURNING` · `RETURNED` | Giao không thành công | `DELIVERY_FAILED` — Giao hàng chưa thành công |
| `DELIVERED` | Đã giao | `DELIVERED` — Đã giao hàng |
| `DISPOSED` | Huỷ theo quyết định sự cố — không tính cước, không giao | `DISPOSED` — Đã huỷ theo xử lý sự cố |

---

## A. Kho VN tiếp nhận — `PUT /api/international-shipments/{shipmentId}/destination-receiving`

Role: `WarehouseStaff`, `OperationsManager`, `Admin`. Lô phải đang `ARRIVED_DESTINATION`.

**Bắt buộc cân đo lại TỪNG kiện** — số cân đo ở VN là số tính cước cuối. Không còn cách "nhận cả lô" hay gửi `parcelIds` trần (→ 400).

```json
{
  "items": [
    { "parcelId": "guid", "actualWeight": 1.0, "length": 20, "width": 20, "height": 10, "condition": "GOOD" },
    { "parcelId": "guid", "actualWeight": 1.0, "length": 20, "width": 20, "height": 10, "condition": "DAMAGED",
      "inspectionNote": "Thùng gỗ vỡ một góc, hàng móp" }
  ],
  "note": "Mở lô ca sáng"
}
```

| Trường | Ghi chú |
|---|---|
| `actualWeight`, `length`, `width`, `height` | **Bắt buộc, > 0** (kg, cm) |
| `condition` | `GOOD` (mặc định) · `DAMAGED` · `WRONG_ITEM` · `MISSING_ITEMS`. App cũ gửi `INTACT` / `WET` / `SEAL_BROKEN` / `MISSING` vẫn nhận |
| `inspectionNote` | Mô tả hiện trạng — thành mô tả của sự cố nếu kiện lỗi |
| `actualQuantity` | Tuỳ chọn, số món đếm được |
| `handling` | **Bỏ qua** — hướng xử lý giờ do khách chọn (mục B) |

Kết quả từng kiện:
- `GOOD` → `RECEIVED_AT_DESTINATION`.
- Khác `GOOD` → `QUARANTINED` + **tự mở sự cố** (loại = condition), báo khách chọn cách xử lý.
- Cân VN lệch **> 10%** so với cân lúc nhận ở kho nguồn → tự mở sự cố `WEIGHT_DEVIATION` cho quản lý kho xem (kiện vẫn ở khu nhận, khách không bị hỏi).
- Hướng xử lý: giữ lựa chọn khách đã đặt cho kiện, không có thì lấy lựa chọn lúc tạo đơn (`defaultDestinationHandling`), không có nữa thì `DIRECT_DELIVERY`.
- Khách nhận thông báo "kho Việt Nam đã nhận hàng".

Response `data.parcels[]`: `parcelId`, `packageCode`, `packageStatus`, `destinationHandling`, `customerIntent`, **`incidentCode`**, **`incidentType`** (null nếu không có sự cố), `inspection` (có thêm `length`, `width`, `height`).

Lỗi: kiện không thuộc lô → 400; kiện đã tiếp nhận rồi → 400; thiếu cân đo → 400 "phải nhập đủ cân nặng và dài/rộng/cao"; condition lạ → 400.

Cân lại một kiện sau khi tiếp nhận: `PUT /api/parcels/{parcelId}/inspection` `{ actualWeight, length, width, height, condition, note }` — số mới thay số cũ khi tính cước.

---

## B. Khách chọn hướng từng kiện

| API | Role |
|---|---|
| `GET /api/orders/consignments/{orderId}/destination-handling` | Khách (đơn mình), nhân viên |
| `PUT /api/orders/consignments/{orderId}/destination-handling` | Khách (đơn mình), Sale |

```json
{ "items": [ { "parcelId": "guid", "handling": "STORE_AT_VN" }, { "parcelId": "guid", "handling": "DIRECT_DELIVERY" } ] }
```

Response `data`: `[ { parcelId, packageCode, packageStatus, handling, canChange, lockedReason } ]`.

- Chọn được từ lúc tạo đơn tới khi kiện vào phiếu nhập kho VN hoặc yêu cầu giao (`canChange = false`, `lockedReason` nói lý do). Kiện đã giao / đã huỷ cũng khoá.
- Một đơn chia được: kiện này gửi kho, kiện kia giao ngay.
- Khách khác → 403; `handling` lạ → 400.

---

## C. Tất toán theo cân đo VN

### C1. Xem trước — `GET /api/orders/{orderId}/settlement-preview`

Role: khách (đơn mình), nhân viên. Không ghi gì, gọi bao nhiêu lần cũng được.

```json
{
  "orderId": "guid", "consignmentCode": "VCL-...",
  "canIssue": true,
  "blockers": [],
  "freightRate": 120000, "quotedFreight": 360000,
  "minimumWeight": 1, "volumetricDivisor": 5000,
  "vnChargeableWeight": 2.5, "vnFreight": 300000,
  "freightAdjustment": -60000,
  "vatRatePercent": 8, "vatAdjustment": -4800,
  "adjustmentNote": null,
  "storageFee": 0,
  "invoiceTotalBefore": 450000, "depositPaid": 225000,
  "estimatedFinalAmount": 160200,
  "parcels": [
    { "parcelId": "guid", "packageCode": "PCL-...", "packageStatus": "RECEIVED_AT_DESTINATION", "isDisposed": false,
      "originWeight": 1.0, "vnWeight": 1.0, "vnLength": 20, "vnWidth": 20, "vnHeight": 10,
      "volumetricWeight": 0.8, "chargeableWeight": 1.0 }
  ]
}
```

- **Công thức:** mỗi kiện `chargeableWeight = max(vnWeight, dài×rộng×cao / volumetricDivisor)`; cộng các kiện còn hiệu lực (kiện `DISPOSED` không tính), không thấp hơn `minimumWeight` → `vnChargeableWeight`. `vnFreight = vnChargeableWeight × freightRate`. `freightAdjustment = vnFreight − quotedFreight` (âm = giảm tiền). `vatAdjustment = freightAdjustment × vatRatePercent%`.
- `freightRate` và `vatRatePercent` lấy **đúng từ báo giá khách đã chấp nhận**, không đọc bảng giá hiện hành.
- `adjustmentNote` khác null = không tính lại (đơn kho nhận theo quy trình cũ chưa cân đo VN, hoặc báo giá không tính theo kg) → giữ nguyên cước đã báo.
- `estimatedFinalAmount` chưa gồm phụ phí Sale nhập thêm khi phát hành.

`blockers[].code`:

| Mã | Nghĩa | Việc cần làm |
|---|---|---|
| `PARCEL_NOT_INSPECTED` | Còn kiện chưa về / kho VN chưa kiểm | Chờ kho tiếp nhận (mục A) |
| `OPEN_INCIDENT` | Còn sự cố lúc tiếp nhận chưa xử lý | Xử lý sự cố (mục E) |
| `NO_INVOICE` | Đơn chưa chấp nhận báo giá | — |
| `NO_DEPOSIT` | Chưa trả cọc | — |
| `ALREADY_SETTLED` | Đã tất toán | — |

### C2. Phát hành — `POST /api/orders/{orderId}/payments/final` (Sale)

Giữ nguyên body cũ (`extraFees`, `paymentMethod`, `returnUrl`, `cancelUrl`). Thay đổi:
- **Bị chặn** khi còn `PARCEL_NOT_INSPECTED` hoặc `OPEN_INCIDENT` → 400 kèm danh sách mã kiện / mã sự cố.
- Hoá đơn tự thêm dòng `FREIGHT_ADJUSTMENT` và `VAT_ADJUSTMENT` theo C1; response có thêm **`freightAdjustmentDelta`** (đã nằm trong `extraFeesTotal`).
- Phát hành lại (vd. phí lưu kho đổi theo ngày) **không cộng dồn** điều chỉnh — `freightAdjustmentDelta = 0` ở lần sau.

**Thử:** `demo.khach1` mở C1 của một đơn đã về kho → `demo.sale` phát hành → số `finalAmount` khớp `estimatedFinalAmount`.

---

## D. Nhập kho VN và giao hàng

### D1. Gửi kho (kiện `STORE_AT_VN`) — giữ nguyên API

1. Sale: `PUT /api/orders/consignments/{orderId}/notify-warehouse` `{ note }`.
2. Kho: `POST /api/warehouse-inbound-requests` `{ orderId, warehouseId }` — chỉ khi đơn **đã tất toán**, chỉ nhận kiện `STORE_AT_VN` đang `RECEIVED_AT_DESTINATION`.
3. Quản lý kho: `PUT /api/warehouse-inbound-requests/{id}/status` `{ status: "INBOUND_APPROVED" }` → hệ thống tự xếp vào ô khu lưu kho còn chỗ → kiện `STORED`.
4. Phí lưu kho tính từ lúc lên kệ, có ngày miễn phí: `GET /api/orders/{orderId}/storage-fee`, thu bằng `POST /api/orders/{orderId}/payments/storage-fee`.

### D2. Yêu cầu giao — `POST /api/delivery-requests`

Role: **`Customer` (đơn của mình)**, `Sale`, `Admin`. Đơn phải đã tất toán.

```json
{
  "orderId": "guid",
  "parcelIds": ["guid"],
  "receiverName": "Nguyễn Văn A", "receiverPhone": "0900000000",
  "addressDetail": "1 Lê Lợi", "province": "Hồ Chí Minh", "district": "Quận 1", "ward": "Bến Nghé",
  "scheduledDate": null, "note": "",
  "redeliveryFee": null
}
```

- Kiện giao được: `STORED`, hoặc `RECEIVED_AT_DESTINATION` + `DIRECT_DELIVERY`. **Kiện đang có sự cố mở → 400.**
- Khách đặt cho đơn người khác → 403. Khách gửi `redeliveryFee` → 403 (phí do Sale báo).
- `redeliveryFee` (chỉ Sale, khi giao lại — xem D4): > 0 thì hệ thống phát hành khoản thu `REDELIVERY_FEE` ngay; response có `redeliveryFeePaymentId`, `redeliveryFeePaymentStatus`, **`redeliveryFeeCheckoutUrl`** để khách trả.

Tiếp theo giữ nguyên: quản lý kho `PUT /api/delivery-requests/{id}/status` `{ status: "DELIVERY_APPROVED" | "DELIVERY_REJECTED", rejectionReason }` → kho `POST /api/delivery-requests/{id}/dispatch` (đặt GoShip; chặn nếu chưa trả phí lưu kho / **chưa trả phí giao lại**).

Trường mới trong `DeliveryRequestDto`: `sourceDeliveryRequestId` (giao lại của phiếu nào), `redeliveryFee`, `redeliveryFeePaymentId`, `redeliveryFeePaymentStatus`, `redeliveryFeeCheckoutUrl`, `returnedAt`, `proofReceivedBy`, `proofAt`.

Trạng thái phiếu giao: `DELIVERY_PENDING` → `DELIVERY_APPROVED` / `DELIVERY_REJECTED` → `DELIVERY_DISPATCHED` → (hoàn hàng) **`DELIVERY_RETURNED`**.

### D3. Giao xong

- Hãng (GoShip/GHN) báo `DELIVERED` qua webhook → kiện `DELIVERED`.
- Hãng không báo (giao tay): Sale ghi **`POST /api/delivery-requests/{id}/proof`** `{ "receivedBy": "Nguyễn Văn A", "note": "" }`. Bắt buộc đính kèm trước ảnh ký nhận: `POST /api/attachments` với `entityType = DELIVERY_REQUEST`, `entityId = {id}`, `documentType = DELIVERY_PROOF`. Role: Sale, OM, Admin (kho → 403). Phiếu phải `DELIVERY_DISPATCHED`.
- Mọi kiện còn hiệu lực của đơn đã `DELIVERED` (kiện `DISPOSED` không tính) → **đơn `DELIVERED`**, khách nhận thông báo "đã giao xong".

### D4. Giao thất bại → hàng hoàn → giao lại

1. Hãng báo `DELIVERY_FAILED` / `RETURNING` / `RETURNED` (khách thấy "Giao hàng chưa thành công").
2. Hàng về tới kho: kho gọi **`POST /api/delivery-requests/{id}/receive-return`** `{ "parcelIds": null, "note": "Khách vắng nhà 3 lần" }` (`parcelIds` bỏ trống = mọi kiện chưa giao của phiếu). Role: `WarehouseStaff`, OM, Admin (khách → 403). Phiếu phải `DELIVERY_DISPATCHED`.
   → kiện về `RECEIVED_AT_DESTINATION` (khu nhận, giao ngay), phiếu `DELIVERY_RETURNED`, báo khách + Sale.
3. Khách hoặc Sale lập yêu cầu giao mới cho các kiện đó (D2). Sale thu phí giao lại thì gửi `redeliveryFee`; khách trả xong kho mới đặt giao được. Giao lại miễn phí thì bỏ trống.

---

## E. Sự cố hàng hoá — `/api/parcel-incidents`

| API | Role |
|---|---|
| `POST /api/parcel-incidents` `{ parcelId, incidentType, description }` | Kho mở tay cho kiện ở kho VN (`DAMAGED` / `WRONG_ITEM` / `MISSING_ITEMS`) |
| `GET /api/parcel-incidents?status=&orderId=&incidentType=&pageNumber=&pageSize=` | Nhân viên: tất cả · khách: đơn của mình |
| `GET /api/parcel-incidents/{id}` | Như trên — kèm `attachments` |
| `POST /api/parcel-incidents/{id}/customer-response` `{ choice, note }` | Khách (đơn mình), Sale |
| `POST /api/parcel-incidents/{id}/resolve` `{ resolution, compensationAmount, note }` | Quản lý kho VN / OM (khác người lập biên bản); Admin làm thay phải ghi `note` |
| `POST /api/parcel-incidents/{id}/compensation-paid` `{ reference, note }` | Admin |
| `POST /api/orders/consignments/{orderId}/complaints` `{ parcelIds, description }` | Khách, trong 3 ngày sau khi giao |

Trạng thái: `OPEN` → (khách chọn) `CUSTOMER_RESPONDED` → `RESOLVED`.

| Loại (`incidentType`) | Khách chọn (`choice`) | Quản lý kho quyết (`resolution`) |
|---|---|---|
| `DAMAGED` · `WRONG_ITEM` · `MISSING_ITEMS` | `ACCEPT` nhận như hiện trạng · `COMPENSATE` đòi bồi thường · `DISPOSE` huỷ hàng | `ACCEPT` / `COMPENSATE` → kiện về khu nhận đi tiếp · `DISPOSE` → kiện `DISPOSED` (không tính cước, không giao, nhả ô kệ) |
| `WEIGHT_DEVIATION` | Khách không chọn (→ 400) | Chỉ `ACCEPT` — dùng số cân VN (cân lại trước nếu cần) |
| `COMPLAINT` (khiếu nại sau giao) | `ACCEPT` / `COMPENSATE` (không `DISPOSE`) | `ACCEPT` đóng khiếu nại · `COMPENSATE` |

Quy tắc:
- `awaitsCustomerChoice = true` → hiện nút chọn cho khách.
- **Trước khi quyết phải có ảnh hiện trạng:** `POST /api/attachments` `entityType = INCIDENT`, `entityId = {id}`, `documentType = INCIDENT_PHOTO` (khách cũng gửi ảnh này được cho khiếu nại của mình). Thiếu → 400.
- Khách chưa chọn mà quản lý kho vẫn quyết → bắt buộc `note` (thường là khách không phản hồi).
- `COMPENSATE` bắt buộc `compensationAmount > 0`.
- Chi bồi thường: Admin đính kèm `COMPENSATION_RECEIPT` vào sự cố (`entityType = INCIDENT`) rồi gọi `compensation-paid` với mã giao dịch.
- Mỗi bước đều báo khách.

Sự cố **chặn**: tất toán (sự cố lúc tiếp nhận), lập yêu cầu giao cho kiện đó, đóng đơn (sự cố mở hoặc bồi thường chưa chi).

---

## F. Đóng đơn

| API | Role | Ghi chú |
|---|---|---|
| `PUT /api/orders/consignments/{orderId}/customer-confirm` | Khách | Đơn đang `DELIVERED`. Không vướng gì → **`COMPLETED` ngay**. Còn vướng → `CUSTOMER_CONFIRMED`, đơn tự đóng khi gỡ xong |
| `PUT /api/orders/consignments/{orderId}/complete` `{ note }` | OM, Admin | Chốt tay; còn vướng → 400 kèm lý do |
| (tự động) | — | Đơn `DELIVERED` / `CUSTOMER_CONFIRMED` quá **3 ngày** kể từ khi giao, không vướng gì → tự `COMPLETED` (`completedAutomatically = true`), báo khách |

"Vướng" = còn sự cố / khiếu nại mở, bồi thường chưa chi, hoặc còn khoản thu `PENDING` (phí lưu kho, phí giao lại…).

Khiếu nại chỉ mở được khi đơn `DELIVERED` / `CUSTOMER_CONFIRMED` và chưa quá 3 ngày kể từ khi giao.

---

## G. Khách theo dõi

`GET /api/orders/consignments/{orderId}/tracking` (xem [`api-xuat-kho.md`](api-xuat-kho.md) mục L) nay đi tiếp sau `ARRIVED_DESTINATION`: `RECEIVED_AT_VN` → (`QUARANTINED`) → `STORED_AT_VN` → `OUT_FOR_DELIVERY` → (`DELIVERY_FAILED`) → `DELIVERED`. `events` thêm các mốc: kho VN đã nhận hàng, sự cố và kết quả xử lý, đã thanh toán đợt cuối, đã đặt giao (mã vận đơn nội địa), giao chưa thành công, đã giao, đơn hoàn thành.

`GET /api/orders/consignments/tracking` mặc định giữ đơn tới khi **giao xong**; `includeFinished=true` để xem cả đơn đã giao / hoàn thành (`includeArrived` là tên cũ, vẫn nhận).

---

## H. Màn hình FE cần có / sửa

| Màn | Việc |
|---|---|
| Tiếp nhận lô (app kho VN) | Mỗi kiện: ô cân + 3 ô kích thước + tình trạng + ghi chú; **bỏ nút "nhận cả lô"**; hiện mã sự cố vừa mở |
| Chọn hướng kiện (khách) | Danh sách kiện + công tắc giao ngay / gửi kho, khoá theo `canChange` |
| Xem trước tất toán (khách + Sale) | Bảng cân đo VN từng kiện, công thức cước, điều chỉnh (+/−), VAT, phí lưu kho, số phải trả; `blockers` thì hiện lý do thay nút trả |
| Sự cố (khách) | Ảnh hiện trạng, nút chọn ACCEPT / COMPENSATE / DISPOSE, kết quả xử lý |
| Sự cố (quản lý kho) | Danh sách `status=OPEN,CUSTOMER_RESPONDED`, xem ảnh, quyết định + số tiền bồi thường |
| Chi bồi thường (Admin) | Sự cố `resolution=COMPENSATE` chưa `compensationPaidAt`: up chứng từ + mã giao dịch |
| Đặt giao (khách) | Chọn kiện sẵn sàng + địa chỉ; nếu có phí giao lại thì mở `redeliveryFeeCheckoutUrl` |
| Phiếu giao (kho / Sale) | Nút **nhận hàng hoàn** (kho), **ghi bằng chứng giao** + up ảnh ký nhận (Sale), trạng thái `DELIVERY_RETURNED` |
| Đơn đã giao (khách) | Nút "Đã nhận hàng" + nút "Khiếu nại" (trong 3 ngày) |

---

## I. Thay đổi so với trước (FE cần sửa)

| Trước | Nay |
|---|---|
| `destination-receiving` nhận `parcelIds` hoặc rỗng (nhận cả lô) | **Bắt buộc `items` có cân đo từng kiện** |
| Kho chọn `handling` lúc mở lô, ghi đè ý khách | Kho không chọn; khách chọn ở mục B |
| Sale phát hành đợt cuối ngay khi lô về | Chỉ khi kho VN kiểm xong mọi kiện và không còn sự cố mở; tự tính lại cước theo cân đo VN |
| Chỉ Sale lập yêu cầu giao | Khách tự đặt giao cho đơn mình được |
| Giao thất bại thì kẹt | Kho nhận hàng hoàn → giao lại (có thể có phí) |
| Webhook GHN đặt đơn `COMPLETED` khi giao xong | Đơn dừng ở `DELIVERED`, đóng theo mục F |
| Khách xác nhận → chờ OM chốt | Khách xác nhận → hoàn thành ngay (nếu không vướng); không xác nhận → tự hoàn thành sau 3 ngày |
