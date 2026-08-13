# 📦 CONTRACT API — LUỒNG HÀNG VỀ TỚI KHO VN (FE + MOBILE)

> **Dự án**: Vietnam Cross Border Logistics System (`vcl-BE`)
> **Phạm vi**: từ lúc gom phiếu xuất kho thành lô → hàng về VN → khách tất toán → giao tận nhà
> **Commit**: `d46f76b1` · `b5d64421` · `2c60f33b` · `6ee03d95`
> **Ngày**: 13/08/2026
> **Nối tiếp**: `wro_fe_api_contract.md` (module xuất kho) — doc này bắt đầu từ chỗ doc đó kết thúc

Mọi ví dụ trong đây là **request/response thật** chạy trên server, không phải mẫu bịa.

---

## MỤC LỤC

1. [Sơ đồ luồng](#1-sơ-đồ-luồng)
2. [Bảng trạng thái](#2-bảng-trạng-thái)
3. [Giai đoạn 1 — OM gom phiếu thành lô](#3-giai-đoạn-1--om-gom-phiếu-thành-lô)
4. [Giai đoạn 2 — Sale theo dõi hành trình](#4-giai-đoạn-2--sale-theo-dõi-hành-trình)
5. [Giai đoạn 3 — Lô về VN, khách tất toán](#5-giai-đoạn-3--lô-về-vn-khách-tất-toán)
6. [Giai đoạn 4 — Kho mở lô, kiểm kiện, phân loại](#6-giai-đoạn-4--kho-mở-lô-kiểm-kiện-phân-loại)
7. [Giai đoạn 5 — Nhập kho VN (nhánh khách gửi lại)](#7-giai-đoạn-5--nhập-kho-vn-nhánh-khách-gửi-lại)
8. [Giai đoạn 6 — Yêu cầu giao hàng](#8-giai-đoạn-6--yêu-cầu-giao-hàng)
9. [Màn hình cần dựng](#9-màn-hình-cần-dựng)
10. [Sáu điểm dễ sai](#10-sáu-điểm-dễ-sai)

---

## 1. SƠ ĐỒ LUỒNG

```
[Phiếu xuất kho đã bàn giao]  ← kết thúc của wro_fe_api_contract.md
        │
        ▼
① OM GOM KIỆN THÀNH LÔ           POST /api/international-shipments
   (đơn vị gom là KIỆN, không phải phiếu — xem mục 3)
        │
        ▼
② SALE THEO DÕI HÀNH TRÌNH        PUT  /international-shipments/{id}/status
   (thông quan xuất → đang đi → thông quan nhập)
        │
        ▼
③ SALE XÁC NHẬN LÔ VỀ KHO VN      PUT  /international-shipments/{id}/arrive
   → KHÁCH TẤT TOÁN               POST /api/orders/{orderId}/payments/final
        │
        ▼
④ KHO MỞ LÔ, KIỂM KIỆN, PHÂN LOẠI PUT  /international-shipments/{id}/destination-receiving
        │
   ┌────┴─────────────────────────────┐
   │                                  │
   ▼ DIRECT_DELIVERY                  ▼ STORE_AT_VN
   (giao liền, không nhập kho)        ⑤ NHẬP KHO VN
   │                                  POST /api/warehouse-inbound-requests
   │                                  PUT  /{id}/status          (OM duyệt)
   │                                  PUT  /api/parcels/{id}/put-away  (xếp kệ)
   │                                  │
   │                                  │ (nằm kệ tới khi khách muốn nhận)
   └────┬─────────────────────────────┘
        ▼
⑥ YÊU CẦU GIAO HÀNG               POST /api/delivery-requests        (Sale lập)
                                   PUT  /{id}/status                  (OM duyệt)
                                   POST /{id}/dispatch                (Kho đặt giao)
```

---

## 2. BẢNG TRẠNG THÁI

### 2.1. Trạng thái LÔ VẬN CHUYỂN (`shipment.status`)

| Mã | Nghĩa | Tab |
| :--- | :--- | :--- |
| `MANIFESTED` | Đã gom phiếu thành lô, chờ đi | `PREPARING` |
| `CUSTOMS_EXPORT_PENDING` | Chờ thông quan xuất | `CUSTOMS` |
| `IN_TRANSIT` | Đang trên đường về VN | `IN_TRANSIT` |
| `CUSTOMS_IMPORT_PENDING` | Chờ thông quan nhập vào VN | `CUSTOMS` |
| `ARRIVED_DESTINATION` | Đã về tới kho VN | `ARRIVED` |
| `ISSUE` · `HOLD` · `CUSTOMS_REJECTED` | Sự cố | `ISSUE` |

**Thứ tự chuyển hợp lệ** — chuyển sai thứ tự là `400`:
```
MANIFESTED → CUSTOMS_EXPORT_PENDING → IN_TRANSIT → CUSTOMS_IMPORT_PENDING → ARRIVED_DESTINATION
             (hoặc MANIFESTED → IN_TRANSIT thẳng, bỏ qua thông quan xuất)
```

### 2.2. Trạng thái KIỆN HÀNG (`packageStatus`)

| Mã | Nghĩa |
| :--- | :--- |
| `RELEASED` | Đã xuất khỏi kho nguồn |
| `ARRIVED_DESTINATION` | Lô đã về VN, kho **chưa** mở lô đếm |
| `RECEIVED_AT_DESTINATION` | Kho đã mở lô, xác nhận kiện có mặt |
| `STORED` | Đã nhập kho VN, đang nằm trên ô kệ |
| `AWAITING_PICKUP` | Đã đặt giao, chờ shipper tới lấy. BE đặt ngay lúc bấm đặt giao, không đợi webhook |
| `PICKING_UP` · `DELIVERED` | Trạng thái giao hàng, do webhook bên giao hàng cập nhật |

### 2.3. Hướng xử lý kiện (`destinationHandling`)

| Mã | Nghĩa |
| :--- | :--- |
| `DIRECT_DELIVERY` | Giao thẳng cho khách, không nhập kho |
| `STORE_AT_VN` | Khách gửi lại kho VN chờ nhận sau |
| `null` | Kiện chưa qua bước phân loại |

### 2.4. Trạng thái PHIẾU NHẬP KHO (`status`)

`INBOUND_PENDING` (Chờ duyệt) → `INBOUND_APPROVED` (Đã duyệt) hoặc `INBOUND_REJECTED` (Bị từ chối)

### 2.5. Trạng thái YÊU CẦU GIAO HÀNG (`status`)

`DELIVERY_PENDING` (Chờ duyệt) → `DELIVERY_APPROVED` (Đã duyệt) → `DELIVERY_DISPATCHED` (Đã đặt giao)
hoặc `DELIVERY_REJECTED` (Bị từ chối)

> Cả hai loại phiếu đều trả kèm **`statusText`** là nhãn tiếng Việt sẵn — FE hiện thẳng, khỏi tự map.

---

## 3. GIAI ĐOẠN 1 — OM GOM KIỆN THÀNH LÔ

> ⚠️ **Đơn vị gom lô là KIỆN HÀNG, không phải phiếu xuất kho.**
> Lô là một container thật và container có sức chứa. Một phiếu 5 kiện mà xe chỉ còn chỗ cho 3 thì gửi 3 kiện chuyến này, 2 kiện chuyến sau. Vì vậy **một phiếu có thể nằm ở nhiều lô** — đó là chuyện đúng, không phải lỗi dữ liệu. Ngược lại, **một kiện chỉ nằm đúng một lô**.

### `POST /api/international-shipments`

**Quyền**: Staff · OperationsManager · Admin (**không** có Sale)

**Request — cách chính, chọn đích danh kiện**
```json
{
  "parcelIds": [
    "eba0eb49-4fec-4d6f-ba45-312b80df3592",
    "81dde3d3-d63e-4ef0-9b10-e0ff2069bde6"
  ],
  "originWarehouseId": "5f8a0b1c-2d3e-4f5a-6b7c-8d9e0f1a2b3c",
  "destinationWarehouseId": "df34760c-0c3e-48ad-bdd6-3a30a4ec402a",
  "carrierId": null,
  "shippingMethod": "Sea",
  "customsDocumentUrls": []
}
```

**Request — cách cũ, gom trọn kiện của mấy phiếu** *(vẫn chạy, giữ cho màn hình hiện tại)*
```json
{
  "wroRequestIds": ["f192349e-a8f7-4b22-895b-136ebe3e292c"],
  "originWarehouseId": "...",
  "destinationWarehouseId": "..."
}
```

Phải có ít nhất một trong hai. Truyền `parcelIds` thì `wroRequestIds` bị bỏ qua.

**Response `201`** — cấu trúc giống hệt `GET` chi tiết ở mục 4.4.

**Bốn chốt chặn** (đều trả `400`):
- Phiếu đứng sau kiện phải đã xuất kho xong: `RELEASED`, `HANDED_OVER` hoặc `IN_TRANSIT`
- **Một KIỆN không được nằm trong 2 lô** → `"Kiện PCL-... đã thuộc một lô vận chuyển khác."`
- Mọi kiện trong lô phải **cùng tuyến vận chuyển**
- Mọi kiện phải **cùng kho xuất** — suy từ vị trí thật của chính những kiện đã chọn

**Lấy id kiện ở đâu để tick?**
`GET /api/warehouse-release-requests/{id}` — mỗi dòng trong `items[]` nay trả kèm **`parcelId`**, cùng `packageCode`, `orderCode`, `binCode`, `actualWeight`. Đủ để dựng bảng cho OM tick và cộng dồn cân nặng xem đã đầy xe chưa.

---

## 4. GIAI ĐOẠN 2 — SALE THEO DÕI HÀNH TRÌNH

### 4.1. `PUT /api/international-shipments/{shipmentId}/status`

**Quyền**: Sale · Staff · OperationsManager · Admin

**Request**
```json
{
  "status": "CUSTOMS_EXPORT_PENDING",
  "note": "Chờ thông quan xuất",
  "carrierTrackingCode": "COSCO-TEST-99881"
}
```

**Response `200`**
```json
{
  "message": "Cập nhật trạng thái lô vận chuyển thành công.",
  "data": {
    "shipmentId": "46389d71-4310-42f3-b7e1-e2a87f165530",
    "shipmentCode": "INTL-20260813062414-3292",
    "status": "CUSTOMS_EXPORT_PENDING",
    "statusTab": "CUSTOMS",
    "note": "Chờ thông quan xuất",
    "carrierTrackingCode": "COSCO-TEST-99881",
    "updatedAt": "2026-08-13T06:24:15.3948Z",
    "updatedByName": "Nam"
  }
}
```

> **`carrierTrackingCode`** = mã vận đơn / số container **bên đối tác 3PL** cấp, sale nhập tay để tra cứu trên trang của họ.
> Bỏ trống ở lần cập nhật sau thì **giữ nguyên mã cũ**, không bị xoá.
>
> ⚠️ **Đừng nhầm với `trackingNumber`** ở phiếu xuất kho — cái đó là mã chặng kho→sân bay, gắn với tài xế và biển số xe. Một lô gom được nhiều phiếu nên hai mã này không thể là một.

**Lỗi thường gặp**
```json
{ "message": "Không thể chuyển sang IN_TRANSIT khi lô đang ở trạng thái ARRIVED_DESTINATION. Chỉ chuyển được từ: CUSTOMS_EXPORT_PENDING, MANIFESTED, HOLD." }
```

### 4.2. `GET /api/international-shipments?statusTab={tab}&pageIndex=1&pageSize=20`

`statusTab` nhận: `PREPARING` · `IN_TRANSIT` · `CUSTOMS` · `ARRIVED` · `ISSUE`

Response bọc `{ items: [...], totalCount }`, mỗi item cùng cấu trúc mục 4.4.

### 4.3. Timeline hành trình

Mỗi lần cập nhật trạng thái, BE **tự ghi một dòng lịch sử**. FE lấy về dựng timeline dọc cho khách theo dõi.

### 4.4. `GET /api/international-shipments/{shipmentId}`

**Response `200`**
```json
{
  "message": "Lấy chi tiết lô vận chuyển thành công.",
  "data": {
    "shipmentId": "46389d71-4310-42f3-b7e1-e2a87f165530",
    "shipmentCode": "INTL-20260813062414-3292",
    "originWarehouseId": "5f8a0b1c-2d3e-4f5a-6b7c-8d9e0f1a2b3c",
    "originWarehouseName": "Kho Quảng Châu (Trung Quốc)",
    "destinationWarehouseId": "df34760c-0c3e-48ad-bdd6-3a30a4ec402a",
    "destinationWarehouseName": "Kho HCM - Tân Bình",
    "carrierId": null,
    "carrierName": null,
    "shippingMethod": "Sea",
    "totalPackages": 1,
    "totalWeight": 1,
    "status": "ARRIVED_DESTINATION",
    "statusTab": "ARRIVED",
    "shippingRouteId": null,
    "shippingRouteName": null,
    "carrierTrackingCode": "COSCO-TEST-99881",
    "shippedAt": "2026-08-13T06:24:15.394864Z",
    "deliveredAt": "2026-08-13T06:24:16.0564071Z",
    "pdfUrls": ["https://api-vcl.zushin.io.vn/api/public/receipts/dcf711ae-..."],
    "exportImageUrls": [],
    "customsDocUrls": [],
    "wroRequests": [
      { "wroRequestId": "f192349e-a8f7-4b22-895b-136ebe3e292c", "wroCode": "WRO-20260810-7943" }
    ],
    "parcels": [
      {
        "parcelId": "d9df658d-a3ce-4d05-95d7-5f65a922725c",
        "packageCode": "PCL-20260809172614-989455",
        "weight": 1,
        "orderId": "07a23270-35d2-40f5-8827-0d5a72f328d1",
        "orderCode": "VCL-20260809152156-758603",
        "customerId": "77206b20-834f-409d-afbe-d5a23de71b25",
        "customerName": "Phong",
        "packageStatus": "RECEIVED_AT_DESTINATION",
        "destinationHandling": "STORE_AT_VN"
      }
    ]
  }
}
```

---

## 5. GIAI ĐOẠN 3 — LÔ VỀ VN, KHÁCH TẤT TOÁN

### 5.1. `PUT /api/international-shipments/{shipmentId}/arrive`

**Quyền**: Sale · Staff · OperationsManager · Admin

**Request**
```json
{ "note": "Lô đã về kho HCM" }
```

**Response `200`** — lô sang `ARRIVED_DESTINATION`, **toàn bộ kiện trong lô** cũng sang `ARRIVED_DESTINATION`.

**Điều kiện**: lô phải đang `IN_TRANSIT` hoặc `CUSTOMS_IMPORT_PENDING`.
Bấm lần 2 → `400` *"Lô vận chuyển này đã được xác nhận đến kho đích trước đó."*

> ⚠️ **BẮT BUỘC hiện popup xem trước.** Một lô gom lô chứa kiện của **nhiều đơn, nhiều khách**. Sale bấm một lần là mọi đơn cùng đổi trạng thái. Lấy danh sách từ `parcels[]` của mục 4.4, gom theo `orderCode` + `customerName` rồi hiện ra trước khi cho bấm.

### 5.2. `POST /api/orders/{orderId}/payments/final`

**Request**
```json
{ "paymentMethod": "SEPAY" }
```

**Response `200`** — tạo đợt thanh toán cuối. Kiểm lại bằng `GET /api/orders/{orderId}/payments`:

```json
{
  "orderStatus": "WAITING_FINAL_PAYMENT",
  "totalBillAmount": 150820,
  "totalPaid": 75410,
  "remaining": 75410,
  "payments": [
    { "installmentType": "DEPOSIT", "amount": 75410, "paymentStatus": "PAID", "paidAt": "..." },
    { "installmentType": "FINAL_PAYMENT", "amount": 75410, "paymentStatus": "PENDING",
      "checkoutUrl": "https://api-vcl.zushin.io.vn/api/payments/sepay/checkout/26081306241794" }
  ]
}
```

> Nếu đơn đang có đợt cuối **chưa trả**, hệ thống **huỷ đợt cũ và phát hành lại** theo số tiền mới (vì phí lưu kho chạy theo ngày).
> **FE luôn lấy `checkoutUrl` mới nhất, đừng cache mã QR cũ.**

### 5.3. `GET /api/orders/{orderId}/storage-fee`

```json
{
  "data": {
    "orderCode": "VCL-20260809152156-758603",
    "freeDays": 7,
    "unitPrice": 20000,
    "currency": "VND",
    "totalAmount": 0,
    "note": "Kiện lưu lâu nhất mới 1 ngày (cộng cả hành trình), còn trong 7 ngày miễn phí.",
    "parcels": [
      {
        "packageCode": "PCL-20260809172614-989455",
        "storedAt": "2026-08-13T08:36:03.179648Z",
        "releasedAt": null,
        "storedDays": 1,
        "chargeableDays": 0,
        "amount": 0,
        "legs": [
          {
            "warehouseName": "Kho HCM - Tân Bình",
            "warehouseType": "DESTINATION",
            "storedAt": "2026-08-13T08:36:03.179648Z",
            "releasedAt": null,
            "days": 1
          }
        ]
      }
    ]
  }
}
```

Phí tính ở **cả hai kho**, miễn **7 ngày cho cả hành trình** (không phải mỗi kho 7 ngày).
FE hiện tách từng chặng: `Kho Quảng Châu: 10 ngày · Kho HCM: 2 ngày · Miễn 7 ngày · Còn tính: 5 ngày`

---

## 6. GIAI ĐOẠN 4 — KHO MỞ LÔ, KIỂM KIỆN, PHÂN LOẠI

### `PUT /api/international-shipments/{shipmentId}/destination-receiving`

**Quyền**: WarehouseStaff · Staff · OperationsManager · Admin (**không** có Sale)

**Điều kiện**: lô phải đang `ARRIVED_DESTINATION` — tức **sale phải bấm xác nhận ở 5.1 trước**.

**Request — kiểu mới, phân loại từng kiện**
```json
{
  "items": [
    { "parcelId": "d9df658d-a3ce-4d05-95d7-5f65a922725c", "handling": "STORE_AT_VN" },
    { "parcelId": "bdcfe12d-cd0c-4a8b-853f-7dc08df76dbe", "handling": "DIRECT_DELIVERY" }
  ],
  "note": "Mở lô kiểm đếm, kiện 1 khách xin gửi lại kho"
}
```

**Cách gọi khác cũng chạy**
- `{ "parcelIds": [...], "note": "..." }` — chỉ tick kiện có mặt, không phân loại
- `{ "handling": "STORE_AT_VN", "note": "..." }` — cả lô cùng một hướng
- `{ "note": "..." }` — nhận hết kiện chưa nhận, mặc định `DIRECT_DELIVERY`

**Response `200`**
```json
{
  "message": "Xác nhận nhận kiện tại kho đích thành công.",
  "data": {
    "shipmentId": "46389d71-4310-42f3-b7e1-e2a87f165530",
    "shipmentCode": "INTL-20260813062414-3292",
    "destinationWarehouseId": "df34760c-0c3e-48ad-bdd6-3a30a4ec402a",
    "destinationWarehouseName": "Kho HCM - Tân Bình",
    "receivedAt": "2026-08-13T07:49:39.1919253Z",
    "receivedByName": "Nhân viên Kho Trung Quốc",
    "note": "Mở lô kiểm đếm, khách xin gửi lại kho",
    "receivedCount": 1,
    "parcels": [
      {
        "parcelId": "d9df658d-a3ce-4d05-95d7-5f65a922725c",
        "packageCode": "PCL-20260809172614-989455",
        "packageStatus": "RECEIVED_AT_DESTINATION",
        "destinationHandling": "STORE_AT_VN"
      }
    ]
  }
}
```

**Kiện thiếu**: chỉ tick kiện có mặt. Kiện không tick nằm lại `ARRIVED_DESTINATION` → FE lọc ra danh sách kiện thất lạc.

**Lỗi**
| Tình huống | HTTP | Message |
| :--- | :--- | :--- |
| Kiện đã nhận rồi | `400` | `Parcel PCL-... đã được xác nhận nhận tại kho đích trước đó.` |
| Hướng gõ sai | `400` | `Handling không hợp lệ: 'GIAO_LIEN'. Chỉ nhận DIRECT_DELIVERY ... hoặc STORE_AT_VN ...` |
| Kiện không thuộc lô | `400` | `Parcel ... không thuộc lô vận chuyển này.` |
| Sale gọi | `403` | `Chỉ Warehouse Staff, Staff hoặc Operations được xác nhận nhận kiện tại kho đích.` |
| Lô chưa về kho đích | `400` | `Lô chưa đến kho đích (phải ở trạng thái ARRIVED_DESTINATION, hiện tại: ...)` |

---

## 7. GIAI ĐOẠN 5 — NHẬP KHO VN (NHÁNH KHÁCH GỬI LẠI)

> Chỉ dành cho kiện phân loại `STORE_AT_VN`. Kiện `DIRECT_DELIVERY` **bỏ qua toàn bộ mục này**, đi thẳng sang mục 8.

### 7.1. `POST /api/warehouse-inbound-requests` — kho lập phiếu

**Quyền**: WarehouseStaff · Staff · OperationsManager · Admin

**Request**
```json
{
  "shipmentId": "46389d71-4310-42f3-b7e1-e2a87f165530",
  "warehouseId": null,
  "parcelIds": null,
  "note": "Khách xin gửi lại kho VN chờ nhận sau"
}
```
- `warehouseId` bỏ trống → lấy kho đích của lô
- `parcelIds` bỏ trống → **tự gom** mọi kiện `STORE_AT_VN` của lô chưa nằm trong phiếu nào

**Response `201`** — phiếu ở `INBOUND_PENDING`, cấu trúc như 7.3.

### 7.2. `PUT /api/warehouse-inbound-requests/{id}/status` — OM duyệt

**Quyền**: **chỉ** OperationsManager · Admin

```json
{ "status": "INBOUND_APPROVED" }
```
Từ chối: `{ "status": "INBOUND_REJECTED", "rejectionReason": "..." }` — **bắt buộc** có lý do.

### 7.3. `GET /api/warehouse-inbound-requests/{id}`

```json
{
  "message": "Lấy chi tiết phiếu nhập kho thành công.",
  "data": {
    "inboundRequestId": "ec14b339-740f-4e3b-8718-23cb9773fabc",
    "inboundCode": "WIN-20260813-5205",
    "shipmentId": "46389d71-4310-42f3-b7e1-e2a87f165530",
    "shipmentCode": "INTL-20260813062414-3292",
    "warehouseId": "df34760c-0c3e-48ad-bdd6-3a30a4ec402a",
    "warehouseName": "Kho HCM - Tân Bình",
    "status": "INBOUND_APPROVED",
    "statusText": "Đã duyệt",
    "note": "Khách xin gửi lại kho VN chờ nhận sau",
    "rejectionReason": null,
    "createdByName": "Nhân viên Kho Trung Quốc",
    "createdAt": "2026-08-13T08:20:11.2697582Z",
    "approvedByName": "Operations Manager 01",
    "approvedAt": "2026-08-13T08:20:12.2609324Z",
    "totalParcels": 1,
    "parcels": [
      {
        "parcelId": "d9df658d-a3ce-4d05-95d7-5f65a922725c",
        "packageCode": "PCL-20260809172614-989455",
        "packageStatus": "RECEIVED_AT_DESTINATION",
        "orderId": "07a23270-35d2-40f5-8827-0d5a72f328d1",
        "orderCode": "VCL-20260809152156-758603",
        "customerName": "Phong",
        "binId": null,
        "binCode": null
      }
    ]
  }
}
```

`GET /api/warehouse-inbound-requests?status=INBOUND_PENDING&shipmentId=...` trả `{ items, totalCount }`.

### 7.4. `PUT /api/parcels/{parcelId}/put-away` — kho xếp lên kệ

API này **đã có sẵn**, nhưng nay có thêm chốt: kiện về từ lô quốc tế phải thuộc **phiếu nhập kho đã duyệt**.

```json
{
  "warehouseId": "df34760c-0c3e-48ad-bdd6-3a30a4ec402a",
  "zoneId": "11111111-1111-1111-1111-111111111111",
  "shelfId": "22222222-2222-2222-2222-222222222222",
  "binId": "33333333-3333-3333-3333-333333333333"
}
```

Lấy danh sách ô kệ hợp lệ: `GET /api/warehouses/{warehouseId}/locations/active`

**Lỗi mới**
```json
{ "message": "Kiện PCL-... chưa có phiếu nhập kho. Lập phiếu nhập kho VN và chờ Operations Manager duyệt rồi mới xếp lên kệ được." }
{ "message": "Phiếu nhập kho WIN-20260813-5205 của kiện PCL-... đang chờ Operations Manager duyệt." }
```

> Xếp kệ xong là **mốc bắt đầu tính phí lưu kho tại kho VN**. Kiện chuyển sang `STORED`.

---

## 8. GIAI ĐOẠN 6 — YÊU CẦU GIAO HÀNG

> Điểm hội tụ: kiện `DIRECT_DELIVERY` vào đây ngay sau mục 6; kiện `STORE_AT_VN` nằm kệ tới khi khách muốn nhận thì cũng vào đây.

### 8.1. `POST /api/delivery-requests` — Sale lập

**Quyền**: **chỉ** Sale · Admin

**Request**
```json
{
  "orderId": "07a23270-35d2-40f5-8827-0d5a72f328d1",
  "parcelIds": null,
  "receiverName": "Nguyễn Văn Phong",
  "receiverPhone": "0912345678",
  "addressDetail": "123 Nguyễn Huệ",
  "province": "TP. Hồ Chí Minh",
  "district": "Quận 1",
  "ward": "Phường Bến Nghé",
  "scheduledDate": null,
  "note": "Giao giờ hành chính"
}
```

> ⚠️ **Tỉnh / Quận / Phường phải là 3 ô riêng biệt.** Đơn hàng chỉ lưu địa chỉ dạng một chuỗi, nhưng bên giao hàng bắt buộc tách 3 cấp. Tên, SĐT, số nhà thì BE lấy được từ đơn, riêng 3 ô này FE phải cho người dùng chọn.

**Hai chốt chặn**
- Đơn phải **tất toán xong** đợt cuối → `400` *"Đơn ... chưa tất toán xong. Khách trả nốt đợt cuối rồi mới lập được yêu cầu giao hàng."*
- Kiện phải **sẵn sàng giao**: `RECEIVED_AT_DESTINATION` + `DIRECT_DELIVERY`, hoặc `STORED`

### 8.2. `PUT /api/delivery-requests/{id}/status` — OM duyệt

**Quyền**: **chỉ** OperationsManager · Admin

```json
{ "status": "DELIVERY_APPROVED" }
```
Từ chối bắt buộc `rejectionReason`.

### 8.3. `POST /api/delivery-requests/{id}/dispatch` — kho đặt giao

**Quyền**: WarehouseStaff · Staff · OperationsManager · Admin

```json
{}
```

Phiếu phải đang `DELIVERY_APPROVED`. Thành công thì hệ thống làm bốn việc: gọi bên giao hàng, lưu mã vận đơn lên phiếu, **nhả ô kệ + ghi nhật ký xuất kho** cho kiện lấy từ kệ (đây là mốc **dừng tính phí lưu kho VN**), và đánh dấu mọi kiện sang `AWAITING_PICKUP`.

> **Trạng thái hiện tại — đọc kỹ chỗ này:**
> Token GoShip **đã có trên server** (nằm trong GitHub Secrets, được bơm vào lúc deploy). Nên bước này **sẽ chạy bình thường trên môi trường đã deploy**.
>
> Chỉ có **máy local của BE là chưa test được**, vì secret trên GitHub không đọc ngược ra được. Ở local, API trả `400` kèm *"Không gọi được đơn vị giao hàng. Kiểm tra token GoShip đã cấu hình chưa..."*
>
> **Phong cứ dựng màn bình thường, không phải chờ gì.** Khi thất bại thì phiếu **giữ nguyên** `DELIVERY_APPROVED` và ô kệ không bị nhả — bấm lại được, không sợ hỏng dữ liệu.

### 8.4. `GET /api/delivery-requests/{id}`

```json
{
  "message": "Lấy chi tiết yêu cầu giao hàng thành công.",
  "data": {
    "deliveryRequestId": "56271f68-0bf7-451e-9380-700247012cfe",
    "deliveryCode": "DLV-20260813-5419",
    "orderId": "07a23270-35d2-40f5-8827-0d5a72f328d1",
    "orderCode": "VCL-20260809152156-758603",
    "customerName": "Phong",
    "status": "DELIVERY_APPROVED",
    "statusText": "Đã duyệt",
    "receiverName": "Nguyễn Văn Phong",
    "receiverPhone": "0912345678",
    "addressDetail": "123 Nguyễn Huệ",
    "province": "TP. Hồ Chí Minh",
    "district": "Quận 1",
    "ward": "Phường Bến Nghé",
    "fullAddress": "123 Nguyễn Huệ, Phường Bến Nghé, Quận 1, TP. Hồ Chí Minh",
    "scheduledDate": null,
    "note": "Giao giờ hành chính",
    "rejectionReason": null,
    "carrierTrackingCode": null,
    "dispatchedAt": null,
    "createdByName": "Nam",
    "createdAt": "2026-08-13T08:36:04.0907774Z",
    "approvedByName": "Operations Manager 01",
    "approvedAt": "2026-08-13T08:36:04.6860785Z",
    "totalParcels": 1,
    "hasShelfPick": true,
    "parcels": [
      {
        "parcelId": "d9df658d-a3ce-4d05-95d7-5f65a922725c",
        "packageCode": "PCL-20260809172614-989455",
        "packageStatus": "STORED",
        "pickSource": "FROM_SHELF",
        "binId": "33333333-3333-3333-3333-333333333333",
        "binCode": "BIN-A1-01"
      }
    ]
  }
}
```

**Ba trường quan trọng**
- **`hasShelfPick`** — `true` thì màn gom hàng của kho **phải hiện cột mã ô kệ**; `false` thì toàn kiện giao thẳng, không có ô nào để đi tới
- **`pickSource`** từng kiện — `FROM_SHELF` (nhặt trên kệ) hay `FROM_STAGING` (khu tạm)
- **`fullAddress`** — ghép sẵn để hiện một dòng

`GET /api/delivery-requests?status=DELIVERY_PENDING&orderId=...` trả `{ items, totalCount }`.

### 8.5. Theo dõi sau khi đặt giao

| API | Việc |
| :--- | :--- |
| `POST /api/goship/rates` | So cước nhiều hãng trước khi đặt |
| `POST /api/goship/webhook` | Bên giao hàng tự bắn trạng thái về |
| `POST /api/goship/shipments/{trackingCode}/sync` | Nút đồng bộ tay (sandbox không bắn webhook) |

Mã trạng thái: `900`/`901` chờ lấy · `902` đang lấy · `905` giao thành công · `913` hoàn tất đối soát

---

## 9. MÀN HÌNH CẦN DỰNG

### Web nội bộ

| Vai | Màn | API chính |
| :--- | :--- | :--- |
| OM | **Gom KIỆN thành lô** — bảng kiện chờ gom, tick từng kiện, cộng dồn cân nặng | `GET /warehouse-release-requests/{id}` lấy `parcelId` · `POST /international-shipments` |
| Sale | Danh sách lô theo 5 tab | `GET /international-shipments?statusTab=` |
| Sale | Chi tiết lô + timeline + ô nhập mã đối tác | `GET /{id}` · `PUT /{id}/status` |
| Sale | Nút xác nhận lô về VN **+ popup xem trước** | `PUT /{id}/arrive` |
| Sale | Nút tạo đợt thanh toán cuối | `POST /orders/{id}/payments/final` |
| **OM** | **Duyệt phiếu nhập kho** *(mới)* | `GET /warehouse-inbound-requests?status=INBOUND_PENDING` · `PUT /{id}/status` |
| **Sale** | **Lập yêu cầu giao hàng** *(mới)* | `POST /delivery-requests` |
| **OM** | **Duyệt yêu cầu giao hàng** *(mới)* | `PUT /delivery-requests/{id}/status` |

### Mobile (kho)

| Màn | API chính | Ghi chú |
| :--- | :--- | :--- |
| Kiểm kiện + phân loại | `PUT /{id}/destination-receiving` | **Đã có màn, cần thêm ô chọn hướng xử lý mỗi kiện** |
| **Lập phiếu nhập kho** *(mới)* | `POST /warehouse-inbound-requests` | |
| Xếp kệ | `PUT /parcels/{id}/put-away` | **Đã có màn**, nay có thể báo lỗi "chưa có phiếu nhập kho" |
| **Gom hàng + đặt giao** *(mới)* | `POST /delivery-requests/{id}/dispatch` | Hiện cột ô kệ tuỳ `hasShelfPick` |

---

## 10. SÁU ĐIỂM DỄ SAI

**1. `mapShipment` đang cắt mất dữ liệu mới.**
Trong `consolidationWorkflowService.js`, hàm này chỉ giữ 3 trường mỗi kiện:
```js
parcels: (row?.parcels || []).map((item) => ({ parcelId, packageCode, weight }))
```
BE đã trả thêm `orderCode`, `customerName`, `packageStatus`, `destinationHandling` nhưng **bị cắt ở đây**. Popup xem trước ở 5.1 sẽ trống cho tới khi bổ sung.

**2. Mobile đang nuốt lỗi ở màn nhận kiện.**
Trong `vietnamWarehouseStore.js`, lời gọi `confirmDestinationReceiving` nằm trong `try/catch` chỉ `console.warn` rồi đi tiếp bằng dữ liệu local. **API hỏng mà màn hình vẫn báo thành công.** Cần hiện lỗi thật cho người dùng.

**3. Mobile gửi `actualQuantity` / `actualWeight` — BE không có hai trường đó**, bị bỏ qua im lặng. Cần thì báo để BE thêm, không thì bỏ khỏi payload.

**4. `null` nghĩa là "chưa tới bước đó", không phải lỗi.**
`carrierTrackingCode`, `dispatchedAt`, `rejectionReason`, `scheduledDate` — cứ ẩn dòng đó đi thay vì hiện chữ "null".

**5. `binCode` đi ngược chiều các trường khác.**
Nó **có** giá trị khi kiện nằm kệ, và **thành null** sau khi đặt giao (vì ô kệ được nhả). Cùng lúc đó `packageStatus` đổi từ `STORED` sang `AWAITING_PICKUP`. Đừng dùng `binCode != null` để quyết định hiện cột ô kệ — dùng `pickSource` hoặc `hasShelfPick`, hai cái đó không đổi theo thời gian.

**6. `POST /api/goship/orders` là cửa sau.**
API cũ này vẫn gọi thẳng được và **bỏ qua bước OM duyệt**. Sau khi dựng xong màn ở mục 8, đừng dùng nó nữa — mọi đơn giao phải đi qua yêu cầu giao hàng.

**7. Màn gom lô hiện đang chọn nhầm đơn vị.**
`OperationsShipmentsPage.jsx` đang cho tick **WRO** (`selectedWroIds`) rồi gửi `wroRequestIds`. Đơn vị đúng là **kiện** — xem mục 3. Đường cũ vẫn chạy nên không gãy ngay, nhưng cách đó không xếp được hàng lên container có sức chứa: một phiếu 5 kiện mà xe còn chỗ cho 3 thì không tách được.

---

## PHỤ LỤC — VIỆC CHƯA XONG PHÍA BACKEND

| Việc | Ảnh hưởng tới FE |
| :--- | :--- |
| **Bước đặt giao chưa chạy thử ở local** | Token GoShip đã có trên server nên môi trường deploy chạy được. **Không chặn FE**, cứ dựng màn |
| **Thu phí lưu kho phát sinh của nhánh gửi lại kho** | Khách đã tất toán mà kiện còn nằm kệ thì ngày lưu kho mới phát sinh chưa có đường thu. **Đang chờ anh Hoàng chốt** dời mốc tất toán hay thu thêm một đợt. Chưa dựng màn thu phí này vội |
| Cho kho **up ảnh lúc mở lô** | Đề xuất, chưa ai duyệt. Chưa làm |
| **Huỷ lô / gỡ kiện khỏi lô** | Chưa có API. Tick nhầm kiện lúc gom lô thì hiện chưa sửa được — cần chốt có làm không |

---

*Tài liệu do BE (Trọng) viết ngày 13/08/2026. Mọi ví dụ là request/response thật đã chạy qua server.*
