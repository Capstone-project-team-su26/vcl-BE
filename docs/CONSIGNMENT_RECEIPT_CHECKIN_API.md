# Phiếu xác nhận ký gửi & Check-in kiện hàng — API contract

Cập nhật 03/08/2026. Backend đã build; **chưa chạy thử với dữ liệu thật**.

## Luồng nghiệp vụ

```
Khách ký gửi → Sale báo giá (chọn kho quốc tế theo route)
→ Khách chấp nhận + thanh toán
→ Sale bấm "Xác nhận yêu cầu"  ⇒ sinh phiếu PDF, lưu link vào đơn
→ Kho mở phiếu, cân đo từng kiện, check-in
→ Nhận đủ kiện ⇒ đơn chuyển CHECKED_IN ⇒ put-away xếp kệ
```

## Quy ước dữ liệu

- **Mỗi dòng `OrderItem` là một kiện hàng.** Đơn 3 dòng hàng = 3 kiện.
- `weight`, `length/width/height`, `declaredValue` của một dòng là **của cả kiện**.
- `quantity` chỉ cho biết trong kiện có bao nhiêu món, **không dùng để nhân** cân nặng, thể tích hay giá trị.
- `volumetricWeight = dài × rộng × cao / 5000` (divisor lấy từ `PricingSettings:VolumetricDivisor`).
- `chargeableWeight = max(actualWeight, volumetricWeight)` — số dùng để tính cước.
- `receiptPdfUrl` ở **cấp đơn**: một đơn một phiếu, trong phiếu có đủ mọi kiện.
- `customerName` là người ký gửi; `receiverName` là người nhận hàng cuối tại Việt Nam.

Mọi thay đổi đều là **thêm field**, không đổi/xoá field cũ. Client hiện tại không vỡ.

## Bảng tra nhanh

| # | Endpoint | Tình trạng | Thay đổi chính |
| --- | --- | --- | --- |
| 1 | `PUT /api/orders/consignments/{orderId}/status` | Sửa | Sinh phiếu PDF khi `APPROVED`, trả `receiptPdfUrl` |
| 2 | `GET /api/orders/consignments/{orderId}/receipt` | **Mới** | Tải / sinh lại phiếu PDF |
| 3 | `GET /api/orders/consignments` | Sửa | Thêm 4 field kho + `receiptPdfUrl` |
| 4 | `GET /api/orders/consignments/{orderId}` | Sửa | Thêm 6 field kho trong `quotation` + `receiptPdfUrl` |
| 5 | `POST /api/parcels/check-in` | Sửa | `orderItemId`, `packages[]`, tiến độ nhận kiện, cảnh báo, chặn số phi lý, người nhận |
| 6 | `GET /api/parcels/pending-putaway` | Sửa | Thêm `orderItemId`, tên/loại/SL sản phẩm, ảnh, người nhận |
| 7 | `GET /api/parcels/{parcelId}` | Sửa | Thêm `orderItemId`, thông tin sản phẩm, `declaredWeight`, ảnh, người nhận |
| 8 | `POST /api/warehouses/{warehouseId}/locations` | Sửa | Validate đơn vị `maxVolume` (cm³) và `maxWeight` (kg) |
| 9 | `PUT /api/warehouses/locations/{locationId}` | Sửa | Như trên |
| — | `PUT /api/parcels/{id}/put-away` | Giữ contract | Thông báo lỗi sức chứa có thêm đơn vị |
| — | `split`, `merge` | Giữ nguyên | — |

## Chuẩn đơn vị

Ràng buộc cứng, không phải quy ước tuỳ chọn — hệ số quy đổi 5000 chỉ đúng với cm → kg:

```
volumetric_weight (kg) = dài(cm) × rộng(cm) × cao(cm) / 5000
chargeable_weight (kg) = max(actual_weight, volumetric_weight)
```

| Bảng | Trường | Đơn vị |
| --- | --- | --- |
| `ORDER_ITEMS` | `length` / `width` / `height` | cm |
| `ORDER_ITEMS` | `weight` | kg |
| `ORDER_ITEMS` | `declared_value` | VND |
| `PARCELS` | `length` / `width` / `height` | cm |
| `PARCELS` | `actual_weight` / `volumetric_weight` / `chargeable_weight` | kg |
| `ORDERS` | `total_weight` | kg |
| `ORDERS` | `total_volume` | cm³ |
| `BINS` | `max_volume` / `current_volume` | **cm³** |
| `BINS` | `max_weight` | kg |
| `PACKAGE_CONFIGURATIONS` | `length` / `width` / `height` | cm |
| `PACKAGE_CONFIGURATIONS` | `max_weight` | kg |
| `SERVICE_PRICINGS` | `price` | VND / kg |

Quy đổi: `1 lít = 1.000 cm³` · `1 m³ = 1.000.000 cm³`

> **Dữ liệu đang sai:** 23/25 ô kệ khai `max_volume` theo **lít** (80 · 100 · 200) thay vì cm³.
> Xem mục 12.

## Đơn mẫu dùng xuyên suốt

`VCL-20260803083512-482913` — khách Nguyễn Văn An, người nhận Trần Thị Bình, kho Quảng Châu, **3 kiện**:

| Kiện | Sản phẩm | SL | KL khai | Kích thước | DIM | Giá trị khai |
| --- | --- | --- | --- | --- | --- | --- |
| 01 | Áo khoác nữ | 10 | 12,0 kg | 60×40×30 | 14,4 kg | 2.500.000 ₫ |
| 02 | Giày thể thao | 4 | 6,0 kg | 45×30×20 | 5,4 kg | 1.200.000 ₫ |
| 03 | Túi xách da | 2 | 8,5 kg | 50×35×25 | 8,75 kg | 4.800.000 ₫ |

---

## 1. Sale xác nhận yêu cầu → sinh phiếu

```
PUT /api/orders/consignments/{orderId}/status
Roles: Admin, Sale, WarehouseStaff, OperationsManager, Delivery
```

```json
{ "status": "APPROVED", "rejectionReason": "" }
```

```json
{
  "message": "Cập nhật trạng thái thành công.",
  "status": "APPROVED",
  "consignmentCode": "VCL-20260803083512-482913",
  "receiptPdfUrl": "https://res.cloudinary.com/demo/raw/upload/v1754209512/vcl/consignment-receipts/PHIEU-KYGUI-VCL-20260803083512-482913-Bui-Dinh-Trong.pdf"
}
```

- Chỉ sinh phiếu khi chuyển sang `APPROVED` lần đầu.
- Sinh phiếu lỗi ⇒ `receiptPdfUrl: null`, status vẫn `APPROVED`; server log `GENERATE CONSIGNMENT RECEIPT ERROR`.
- Tên file: `PHIEU-KYGUI-{consignmentCode}-{tên Sale xác nhận, bỏ dấu}.pdf`.

### Phiếu có 2 trạng thái

| | Sau khi Sale xác nhận | Sau khi kho nhận đủ kiện |
| --- | --- | --- |
| Băng dưới mỗi thẻ kiện | nền vàng, chỗ trống để ghi tay | nền xanh, in số thực nhận + mã kiện |
| Dòng tổng kết nhận hàng | không có | `Đã nhận 3/3 kiện · Tổng thực nhận · Tổng tính cước · Thời điểm` |
| Ô **XÁC NHẬN KHO** | "Ký và ghi rõ họ tên" (trống) | **tên nhân viên kho** + giờ nhận |
| Trạng thái đơn in trên phiếu | `APPROVED` | `CHECKED_IN` |

Phiếu **tự sinh lại** khi `isFullyReceived = true`; nhận lẻ chưa đủ kiện thì giữ bản cũ.
Sinh lại làm **đổi URL** (timestamp Cloudinary mới) — client phải đọc lại `receiptPdfUrl` từ API, không cache link cũ.

Chữ ký trên phiếu lấy tự động từ token, người dùng không gõ tay:

| Ô chữ ký | Nguồn |
| --- | --- |
| NGƯỜI LẬP PHIẾU | Sale gọi `PUT .../status` → `ORDERS.receipt_generated_by_id` → `USERS.full_name` |
| XÁC NHẬN KHO | Nhân viên kho gọi `POST /api/parcels/check-in` → `PARCELS.checked_in_by_id` → `USERS.full_name` |

Nhiều nhân viên cùng nhận một đơn ở các lần khác nhau ⇒ phiếu in tên **người check-in gần nhất**.

---

## 2. Tải / sinh lại phiếu (endpoint mới)

```
GET /api/orders/consignments/{orderId}/receipt
Roles: Admin, Sale, WarehouseStaff, OperationsManager
```

```
200 OK
Content-Type: application/pdf
Content-Disposition: attachment; filename="PHIEU-KYGUI-VCL-20260803083512-482913-Bui-Dinh-Trong.pdf"
```

Không đổi trạng thái đơn, không ghi đè link đã lưu.

---

## 3. Danh sách đơn ký gửi

```
GET /api/orders/consignments?status=APPROVED&pageNumber=1&pageSize=20
```

```json
{
  "message": "Lấy danh sách yêu cầu ký gửi thành công.",
  "data": {
    "items": [
      {
        "orderId": "8f3a1c7e-4b2d-4c11-9a55-1f2e3d4c5b6a",
        "consignmentCode": "VCL-20260803083512-482913",
        "customerName": "Nguyễn Văn An",
        "consignmentType": "STANDARD",
        "status": "APPROVED",
        "totalWeight": 26.5,
        "totalVolume": 164750,
        "route": "Trung Quốc → Việt Nam",
        "receiverName": "Trần Thị Bình",
        "receiverPhone": "0987654321",
        "receiverAddress": "25 Nguyễn Huệ, Quận 1, TP. Hồ Chí Minh",
        "requiresInspection": true,
        "createdAt": "2026-07-28T09:14:22Z",
        "paymentConfirmedAt": "2026-08-02T21:05:47Z",
        "quotationCreatedAt": "2026-07-30T14:22:10Z",
        "statusUpdatedAt": "2026-08-02T21:05:47Z",
        "warehouseId": "c1d2e3f4-5555-6666-7777-888899990000",
        "warehouseName": "Kho Quảng Châu",
        "warehouseCode": "WH-CN-GZ01",
        "warehouseRegion": "CN",
        "warehouseType": "ORIGIN",
        "receiptPdfUrl": "https://res.cloudinary.com/demo/raw/upload/v1754209512/vcl/consignment-receipts/PHIEU-KYGUI-VCL-20260803083512-482913-Bui-Dinh-Trong.pdf",
        "pricingRuleIds": ["4829cedb-1111-2222-3333-444455556666"],
        "itemNames": ["Áo khoác nữ", "Giày thể thao", "Túi xách da"]
      }
    ],
    "totalCount": 1,
    "pageNumber": 1,
    "pageSize": 20,
    "totalPages": 1
  }
}
```

Field thêm trong đợt này: `warehouseName`, `warehouseCode`, `warehouseRegion`, `warehouseType`, `receiptPdfUrl`.

### Mốc thời gian (Đạt bổ sung ở commit `92c59f6e`, 29/07)

| Field | Ý nghĩa |
| --- | --- |
| `createdAt` | Ngày tạo đơn. **Cố định**, không đổi theo trạng thái |
| `quotationCreatedAt` | Thời điểm lập báo giá mới nhất |
| `paymentConfirmedAt` | Thời điểm xác nhận thanh toán |
| `statusUpdatedAt` | **Mốc ứng với trạng thái hiện tại** — client dùng field này để hiển thị "cập nhật lúc…" |

`statusUpdatedAt` ưu tiên: `paymentConfirmedAt` → `quotationCreatedAt` → `createdAt`.

> Chưa hỗ trợ lọc theo kho. Tham số `regionCode` mobile đang gửi hiện bị bỏ qua.

---

## 4. Chi tiết đơn — lấy `orderItemId` của từng kiện

```
GET /api/orders/consignments/{orderId}
```

```json
{
  "message": "Lấy chi tiết yêu cầu ký gửi thành công.",
  "data": {
    "orderId": "8f3a1c7e-4b2d-4c11-9a55-1f2e3d4c5b6a",
    "consignmentCode": "VCL-20260803083512-482913",
    "consignmentType": "STANDARD",
    "status": "APPROVED",
    "orderType": "CONSIGNMENT",
    "totalWeight": 26.5,
    "totalVolume": 164750,
    "note": "Hàng dễ vỡ, yêu cầu đóng thùng carton gia cố.",
    "route": "China-Vietnam",
    "receiverName": "Trần Thị Bình",
    "receiverPhone": "0987654321",
    "receiverAddress": "25 Nguyễn Huệ, Quận 1, TP. Hồ Chí Minh",
    "requiresInspection": true,
    "createdAt": "2026-07-28T09:14:22Z",
    "paymentConfirmedAt": "2026-08-02T21:05:47Z",
    "statusUpdatedAt": "2026-08-02T21:05:47Z",
    "receiptPdfUrl": "https://res.cloudinary.com/demo/raw/upload/v1754209512/vcl/consignment-receipts/PHIEU-KYGUI-VCL-20260803083512-482913-Bui-Dinh-Trong.pdf",
    "pricingRuleIds": ["4829cedb-1111-2222-3333-444455556666"],

    "customer": {
      "customerId": "b2c3d4e5-1111-2222-3333-444455556666",
      "fullName": "Nguyễn Văn An",
      "email": "an.nguyen@gmail.com",
      "phone": "0912345678"
    },

    "items": [
      {
        "id": "a1b2c3d4-1111-2222-3333-444455556666",
        "productName": "Áo khoác nữ",
        "productType": "Quần áo",
        "quantity": 10,
        "weight": 12.0,
        "width": 40,
        "height": 30,
        "length": 60,
        "declaredValue": 2500000,
        "referenceUrls": ["https://res.cloudinary.com/demo/image/upload/vcl/consignment-items/ao-khoac-1.jpg"],
        "domesticTrackingCode": null,
        "packageConfigurationId": "cf000001-1111-2222-3333-444455556666",
        "packageConfiguration": {
          "id": "cf000001-1111-2222-3333-444455556666",
          "configCode": "CARTON_M",
          "configName": "Carton size M",
          "length": 60, "width": 40, "height": 30,
          "maxWeight": 30,
          "packageFee": 50000,
          "status": "ACTIVE",
          "estimatedFee": null
        },
        "volumetricWeight": 14.4
      },
      {
        "id": "e5f6a7b8-7777-8888-9999-000011112222",
        "productName": "Giày thể thao",
        "productType": "Giày dép",
        "quantity": 4,
        "weight": 6.0,
        "width": 30,
        "height": 20,
        "length": 45,
        "declaredValue": 1200000,
        "referenceUrls": ["https://res.cloudinary.com/demo/image/upload/vcl/consignment-items/giay-1.jpg"],
        "domesticTrackingCode": null,
        "packageConfigurationId": "cf000002-1111-2222-3333-444455556666",
        "packageConfiguration": {
          "id": "cf000002-1111-2222-3333-444455556666",
          "configCode": "CARTON_S",
          "configName": "Carton size S",
          "length": 45, "width": 30, "height": 20,
          "maxWeight": 15,
          "packageFee": 35000,
          "status": "ACTIVE",
          "estimatedFee": null
        },
        "volumetricWeight": 5.4
      },
      {
        "id": "c9d0e1f2-3333-4444-5555-666677778888",
        "productName": "Túi xách da",
        "productType": "Phụ kiện",
        "quantity": 2,
        "weight": 8.5,
        "width": 35,
        "height": 25,
        "length": 50,
        "declaredValue": 4800000,
        "referenceUrls": ["https://res.cloudinary.com/demo/image/upload/vcl/consignment-items/tui-1.jpg"],
        "domesticTrackingCode": null,
        "packageConfigurationId": "cf000001-1111-2222-3333-444455556666",
        "packageConfiguration": {
          "id": "cf000001-1111-2222-3333-444455556666",
          "configCode": "CARTON_M",
          "configName": "Carton size M",
          "length": 60, "width": 40, "height": 30,
          "maxWeight": 30,
          "packageFee": 50000,
          "status": "ACTIVE",
          "estimatedFee": null
        },
        "volumetricWeight": 8.75
      }
    ],

    "quotation": {
      "quotationId": "9a8b7c6d-1111-2222-3333-444455556666",
      "quoteType": "OFFICIAL",
      "status": "ACCEPTED",
      "estimatedFreightCharge": 5300000,
      "domesticShippingFee": 5000,
      "serviceFee": 115000,
      "taxAndDuty": 0,
      "totalVolume": 164750,
      "totalEstimatedCost": 6420000,
      "createdAt": "2026-07-30T14:22:10Z",
      "expiredAt": "2026-08-06T14:22:10Z",
      "warehouseId": "c1d2e3f4-5555-6666-7777-888899990000",
      "warehouseName": "Kho Quảng Châu",
      "warehouseCode": "WH-CN-GZ01",
      "warehouseAddress": "Số 12, Đường Quảng Châu, Quảng Đông, Trung Quốc",
      "warehouseRegion": "CN",
      "warehouseType": "ORIGIN"
    }
  }
}
```

`items[].id` chính là `orderItemId` truyền vào check-in.
Field thêm: 6 field `warehouse*` trong `quotation`, và `receiptPdfUrl` ở cấp đơn.

---

## 5. Kho check-in kiện hàng

```
POST /api/parcels/check-in
Authorization: Bearer <token nhân viên kho>
Roles: Staff, WarehouseStaff
```

Token dùng để ghi `PARCELS.checked_in_by_id` — tên nhân viên này sẽ được in vào ô XÁC NHẬN KHO
trên phiếu khi đơn nhận đủ kiện.

### 5.1 Nhận cả 3 kiện một lần (khuyến nghị)

```json
{
  "shippingCode": "VCL-20260803083512-482913",
  "packages": [
    { "orderItemId": "a1b2c3d4-1111-2222-3333-444455556666", "actualWeight": 12.4, "length": 61, "width": 40, "height": 30 },
    { "orderItemId": "e5f6a7b8-7777-8888-9999-000011112222", "actualWeight": 5.8,  "length": 45, "width": 30, "height": 20 },
    { "orderItemId": "c9d0e1f2-3333-4444-5555-666677778888", "actualWeight": 8.7,  "length": 52, "width": 36, "height": 26 }
  ]
}
```

```json
{
  "message": "Check-in kiện hàng thành công.",
  "data": {
    "parcelId": "3f9a2b81-7c44-4e10-9d33-a5b6c7d8e9f0",
    "shippingCode": "VCL-20260803083512-482913",
    "consignmentId": "8f3a1c7e-4b2d-4c11-9a55-1f2e3d4c5b6a",
    "consignmentCode": "VCL-20260803083512-482913",
    "customerName": "Nguyễn Văn An",
    "customerCode": "KH-000128",
    "receiverName": "Trần Thị Bình",
    "receiverPhone": "0987654321",
    "receiverAddress": "25 Nguyễn Huệ, Quận 1, TP. Hồ Chí Minh",
    "packageCode": "PCL-20260803084501-773912",
    "orderItemId": "a1b2c3d4-1111-2222-3333-444455556666",
    "productName": "Áo khoác nữ",
    "declaredWeight": 12.0,
    "actualWeight": 12.4,
    "length": 61,
    "width": 40,
    "height": 30,
    "volumetricWeight": 14.64,
    "chargeableWeight": 14.64,
    "packageStatus": "RECEIVED",
    "weightDifference": 0.4,
    "warnings": [],
    "declaredPackageCount": 3,
    "receivedPackageCount": 3,
    "remainingPackageCount": 0,
    "isFullyReceived": true,
    "orderStatus": "CHECKED_IN",

    "parcels": [
      {
        "parcelId": "3f9a2b81-7c44-4e10-9d33-a5b6c7d8e9f0",
        "packageCode": "PCL-20260803084501-773912",
        "orderItemId": "a1b2c3d4-1111-2222-3333-444455556666",
        "productName": "Áo khoác nữ",
        "declaredWeight": 12.0,
        "actualWeight": 12.4,
        "length": 61, "width": 40, "height": 30,
        "volumetricWeight": 14.64,
        "chargeableWeight": 14.64,
        "packageStatus": "RECEIVED",
        "weightDifference": 0.4,
        "warnings": []
      },
      {
        "parcelId": "5b1c3d92-8e55-4f21-8a44-b6c7d8e9f0a1",
        "packageCode": "PCL-20260803084501-773913",
        "orderItemId": "e5f6a7b8-7777-8888-9999-000011112222",
        "productName": "Giày thể thao",
        "declaredWeight": 6.0,
        "actualWeight": 5.8,
        "length": 45, "width": 30, "height": 20,
        "volumetricWeight": 5.4,
        "chargeableWeight": 5.8,
        "packageStatus": "RECEIVED",
        "weightDifference": -0.2,
        "warnings": []
      },
      {
        "parcelId": "7d2e4f03-9f66-4a32-9b55-c7d8e9f0a1b2",
        "packageCode": "PCL-20260803084501-773914",
        "orderItemId": "c9d0e1f2-3333-4444-5555-666677778888",
        "productName": "Túi xách da",
        "declaredWeight": 8.5,
        "actualWeight": 8.7,
        "length": 52, "width": 36, "height": 26,
        "volumetricWeight": 9.7344,
        "chargeableWeight": 9.7344,
        "packageStatus": "RECEIVED",
        "weightDifference": 0.2,
        "warnings": []
      }
    ],

    "packageProgress": [
      { "orderItemId": "a1b2c3d4-1111-2222-3333-444455556666", "productName": "Áo khoác nữ",
        "quantity": 10, "declaredWeight": 12.0, "isReceived": true,
        "packageCode": "PCL-20260803084501-773912", "actualWeight": 12.4 },
      { "orderItemId": "e5f6a7b8-7777-8888-9999-000011112222", "productName": "Giày thể thao",
        "quantity": 4, "declaredWeight": 6.0, "isReceived": true,
        "packageCode": "PCL-20260803084501-773913", "actualWeight": 5.8 },
      { "orderItemId": "c9d0e1f2-3333-4444-5555-666677778888", "productName": "Túi xách da",
        "quantity": 2, "declaredWeight": 8.5, "isReceived": true,
        "packageCode": "PCL-20260803084501-773914", "actualWeight": 8.7 }
    ],

    "declaredItems": [
      { "productName": "Áo khoác nữ", "productType": "Quần áo", "quantity": 10,
        "weight": 12.0, "length": 60, "width": 40, "height": 30, "declaredValue": 2500000 },
      { "productName": "Giày thể thao", "productType": "Giày dép", "quantity": 4,
        "weight": 6.0, "length": 45, "width": 30, "height": 20, "declaredValue": 1200000 },
      { "productName": "Túi xách da", "productType": "Phụ kiện", "quantity": 2,
        "weight": 8.5, "length": 50, "width": 35, "height": 25, "declaredValue": 4800000 }
    ]
  }
}
```

### 5.2 Nhận từng kiện

```json
{
  "shippingCode": "VCL-20260803083512-482913",
  "orderItemId": "a1b2c3d4-1111-2222-3333-444455556666",
  "actualWeight": 12.4,
  "length": 61, "width": 40, "height": 30
}
```

Khi chưa nhận đủ, `parcels` rỗng (chỉ có khi gửi nhiều kiện một lượt), phần còn lại như sau:

```json
{
  "declaredPackageCount": 3,
  "receivedPackageCount": 1,
  "remainingPackageCount": 2,
  "isFullyReceived": false,
  "orderStatus": "APPROVED",
  "packageProgress": [
    { "orderItemId": "a1b2c3d4-...", "productName": "Áo khoác nữ",   "quantity": 10, "declaredWeight": 12.0, "isReceived": true,  "packageCode": "PCL-20260803084501-773912", "actualWeight": 12.4 },
    { "orderItemId": "e5f6a7b8-...", "productName": "Giày thể thao", "quantity": 4,  "declaredWeight": 6.0,  "isReceived": false, "packageCode": null, "actualWeight": null },
    { "orderItemId": "c9d0e1f2-...", "productName": "Túi xách da",   "quantity": 2,  "declaredWeight": 8.5,  "isReceived": false, "packageCode": null, "actualWeight": null }
  ]
}
```

Đơn giữ `APPROVED` nên vẫn nằm trong dropdown của app kho cho tới khi nhận đủ.

### 5.3 Tương thích ngược

Request cũ không có `orderItemId` và `packages` vẫn chạy — hệ thống gán vào **kiện chưa nhận đầu tiên**. Nên truyền `orderItemId` để tránh gán nhầm khi hàng về lẻ.

### 5.4 Cảnh báo (200, không chặn)

Chỉ báo khi vượt **cả** ngưỡng phần trăm lẫn mức chênh tuyệt đối:

| Loại | Ngưỡng % | Chênh tối thiểu |
| --- | --- | --- |
| Cân nặng | > 50% | ≥ 2 kg |
| Thể tích | > 80% | ≥ 20.000 cm³ |

```json
{
  "warnings": [
    "Kiện \"Quần áo sỉ\": cân nặng thực 16.0 kg lệch 60% so với khai báo 10.0 kg.",
    "Kiện \"Chăn ga gối\": thể tích thực lệch 483.3% so với khai báo. Kiểm tra lại số đo hoặc thùng đóng gói."
  ]
}
```

Cảnh báo xuất hiện cả ở cấp response và trong `parcels[].warnings` của kiện gây ra nó.

### 5.5 Lỗi chặn (400, không tạo kiện nào)

```json
{ "message": "Cân nặng thực tế phải lớn hơn 0." }
{ "message": "Cân nặng thực tế 1500 kg vượt ngưỡng cho phép (1000 kg). Kiểm tra lại số đã nhập." }
{ "message": "Chiều dài 610 cm vượt ngưỡng cho phép (500 cm). Kiểm tra lại số đã nhập." }
{ "message": "Kiện \"Áo khoác nữ\" đã được check-in trước đó." }
{ "message": "Kiện hàng không thuộc đơn ký gửi VCL-20260803083512-482913." }
{ "message": "Đơn ký gửi không có kiện hàng nào để nhận." }
{ "message": "Mã gửi hàng không hợp lệ." }
{ "message": "Yêu cầu ký gửi không ở trạng thái cho phép check-in (Hiện tại: PENDING_REVIEW)." }
```

404: `{ "message": "Không tìm thấy yêu cầu ký gửi." }`

Trạng thái đơn cho phép check-in: `APPROVED`, `CHECKED_IN`, `DEPOSIT_PAID`, `PAYMENT_CONFIRMED`, `WAITING_DEPOSIT`, `QUOTATION_CONFIRMED`.

---

## 6. Danh sách kiện chờ xếp kệ

```
GET /api/parcels/pending-putaway?pageNumber=1&pageSize=10&search=&status=
Roles: Staff, WarehouseStaff
```

Lọc theo **kiện**: `BinId == null` và `PackageStatus` thuộc `RECEIVED` / `RECEIVED_AT_DESTINATION`.
Kiện hiện lên ngay khi check-in xong, không chờ đơn nhận đủ kiện.

```json
{
  "message": "Lấy danh sách kiện chờ nhập vị trí kho thành công.",
  "data": {
    "items": [
      {
        "id": "3f9a2b81-7c44-4e10-9d33-a5b6c7d8e9f0",
        "packageCode": "PCL-20260803084501-773912",
        "consignmentCode": "VCL-20260803083512-482913",
        "customerName": "Nguyễn Văn An",
        "receiverName": "Trần Thị Bình",
        "receiverPhone": "0987654321",
        "receiverAddress": "25 Nguyễn Huệ, Quận 1, TP. Hồ Chí Minh",
        "actualWeight": 12.4,
        "length": 61,
        "width": 40,
        "height": 30,
        "volumetricWeight": 14.64,
        "chargeableWeight": 14.64,
        "packageStatus": "RECEIVED",
        "checkInTime": "2026-08-03T08:45:01Z",
        "orderItemId": "a1b2c3d4-1111-2222-3333-444455556666",
        "productName": "Áo khoác nữ",
        "productType": "Quần áo",
        "quantity": 10,
        "referenceUrls": ["https://res.cloudinary.com/demo/image/upload/vcl/consignment-items/ao-khoac-1.jpg"]
      },
      {
        "id": "5b1c3d92-8e55-4f21-8a44-b6c7d8e9f0a1",
        "packageCode": "PCL-20260803084501-773913",
        "consignmentCode": "VCL-20260803083512-482913",
        "customerName": "Nguyễn Văn An",
        "receiverName": "Trần Thị Bình",
        "receiverPhone": "0987654321",
        "receiverAddress": "25 Nguyễn Huệ, Quận 1, TP. Hồ Chí Minh",
        "actualWeight": 5.8,
        "length": 45,
        "width": 30,
        "height": 20,
        "volumetricWeight": 5.4,
        "chargeableWeight": 5.8,
        "packageStatus": "RECEIVED",
        "checkInTime": "2026-08-03T08:45:01Z",
        "orderItemId": "e5f6a7b8-7777-8888-9999-000011112222",
        "productName": "Giày thể thao",
        "productType": "Giày dép",
        "quantity": 4,
        "referenceUrls": ["https://res.cloudinary.com/demo/image/upload/vcl/consignment-items/giay-1.jpg"]
      },
      {
        "id": "7d2e4f03-9f66-4a32-9b55-c7d8e9f0a1b2",
        "packageCode": "PCL-20260803084501-773914",
        "consignmentCode": "VCL-20260803083512-482913",
        "customerName": "Nguyễn Văn An",
        "receiverName": "Trần Thị Bình",
        "receiverPhone": "0987654321",
        "receiverAddress": "25 Nguyễn Huệ, Quận 1, TP. Hồ Chí Minh",
        "actualWeight": 8.7,
        "length": 52,
        "width": 36,
        "height": 26,
        "volumetricWeight": 9.7344,
        "chargeableWeight": 9.7344,
        "packageStatus": "RECEIVED",
        "checkInTime": "2026-08-03T08:45:01Z",
        "orderItemId": "c9d0e1f2-3333-4444-5555-666677778888",
        "productName": "Túi xách da",
        "productType": "Phụ kiện",
        "quantity": 2,
        "referenceUrls": ["https://res.cloudinary.com/demo/image/upload/vcl/consignment-items/tui-1.jpg"]
      }
    ],
    "totalCount": 3,
    "pageNumber": 1,
    "pageSize": 10,
    "totalPages": 1
  }
}
```

Field thêm: `orderItemId`, `productName`, `productType`, `quantity`, `referenceUrls`.

---

## 7. Chi tiết một kiện

```
GET /api/parcels/{parcelId}
Roles: Staff, WarehouseStaff
```

Chưa xếp kệ:

```json
{
  "message": "Lấy chi tiết kiện hàng thành công.",
  "data": {
    "parcelId": "3f9a2b81-7c44-4e10-9d33-a5b6c7d8e9f0",
    "packageCode": "PCL-20260803084501-773912",
    "packageStatus": "RECEIVED",
    "consignmentId": "8f3a1c7e-4b2d-4c11-9a55-1f2e3d4c5b6a",
    "consignmentCode": "VCL-20260803083512-482913",
    "orderItemId": "a1b2c3d4-1111-2222-3333-444455556666",
    "productName": "Áo khoác nữ",
    "productType": "Quần áo",
    "quantity": 10,
    "declaredWeight": 12.0,
    "referenceUrls": ["https://res.cloudinary.com/demo/image/upload/vcl/consignment-items/ao-khoac-1.jpg"],
    "customerName": "Nguyễn Văn An",
    "customerCode": "KH-000128",
    "customerEmail": "an.nguyen@gmail.com",
    "customerPhone": "0912345678",
    "receiverName": "Trần Thị Bình",
    "receiverPhone": "0987654321",
    "receiverAddress": "25 Nguyễn Huệ, Quận 1, TP. Hồ Chí Minh",
    "actualWeight": 12.4,
    "length": 61,
    "width": 40,
    "height": 30,
    "volumetricWeight": 14.64,
    "chargeableWeight": 14.64,
    "checkInTime": "2026-08-03T08:45:01Z",
    "putAwayTime": null,
    "storageLocation": null,
    "hasInventory": false
  }
}
```

Sau khi xếp kệ, 4 field cuối đổi:

```json
{
  "packageStatus": "STORED",
  "putAwayTime": "2026-08-03T09:12:44Z",
  "storageLocation": {
    "warehouseName": "Kho Quảng Châu",
    "zoneName": "Khu A",
    "shelfCode": "A-01",
    "binCode": "A-01-03"
  },
  "hasInventory": true
}
```

Field thêm: `orderItemId`, `productName`, `productType`, `quantity`, `declaredWeight`, `referenceUrls`.

---

## 8. Xếp kiện vào ô kệ (không đổi)

```
PUT /api/parcels/{parcelId}/put-away
```

```json
{
  "warehouseId": "c1d2e3f4-5555-6666-7777-888899990000",
  "zoneId": "z1a2b3c4-1111-2222-3333-444455556666",
  "shelfId": "s1a2b3c4-1111-2222-3333-444455556666",
  "binId": "b1a2b3c4-1111-2222-3333-444455556666"
}
```

```json
{
  "message": "Nhập vị trí kho cho kiện hàng thành công.",
  "data": {
    "packageCode": "PCL-20260803084501-773912",
    "warehouseName": "Kho Quảng Châu",
    "zoneName": "Khu A",
    "shelfCode": "A-01",
    "binCode": "A-01-03",
    "packageStatus": "STORED",
    "putAwayTime": "2026-08-03T09:12:44Z"
  }
}
```

Xếp xong kiện có `BinId` nên rời khỏi `pending-putaway`.

---

## 9. Thay đổi schema

| Bảng | Cột thêm | Migration |
| --- | --- | --- |
| `ORDERS` | `receipt_pdf_url`, `receipt_generated_at`, `receipt_generated_by_id` | `20260803083028_AddOrderReceiptPdfFields` |
| `PARCELS` | `order_item_id` | `20260803150857_AddParcelOrderItemId` |
| `PARCELS` | `checked_in_by_id` | `20260803162612_AddParcelCheckedInBy` |

Đều nullable, đã apply lên DB chung.
Kiện check-in **trước** ngày 03/08/2026 có `order_item_id = null` nên `productName` trả về rỗng và không xuất hiện trong `packageProgress`.

---

## 10. Việc phía client

**Web nội bộ (`HeThongLogictic`, nhánh `develop`)**
- Hiển thị `receiptPdfUrl` sau khi bấm "Xác nhận yêu cầu".
- Đọc lại `receiptPdfUrl` mỗi lần mở phiếu; URL đổi sau khi kho check-in đủ kiện.
- Bỏ fallback lấy kho `DESTINATION` trong `WarehouseReceiptCreate.jsx`; dùng `quotation.warehouseName`.

**Mobile (`vcl-mobile`)**
- Truyền `status=APPROVED` vào `GET /api/orders/consignments` thay vì tải mọi trang rồi lọc ở client.
- Truyền `orderItemId` khi check-in; gọi API bằng token của **chính nhân viên kho** vì tên trên phiếu lấy từ token.
- Prefill lấy `items[].weight` và `items[].length/width/height` của **đúng kiện đang chọn**; không cộng dồn qua các kiện, không lấy từ `packageConfiguration` (thùng đó dành cho bước đóng gói sau, không phải hình dạng kiện lúc nhận).
- Hiển thị tiến độ từ `packageProgress`; `isFullyReceived = false` thì giữ người dùng ở màn để nhận tiếp.

---

## 11. Khai báo ô kệ — validate đơn vị

```
POST /api/warehouses/{warehouseId}/locations
PUT  /api/warehouses/locations/{locationId}
Roles: Admin, WarehouseStaff
```

```json
{
  "zoneName": "Khu A",
  "shelfCode": "A-01",
  "binCode": "BIN-RCV-01",
  "maxVolume": 200000,
  "maxWeight": 100
}
```

`maxVolume` tính bằng **cm³**, `maxWeight` bằng **kg**. Ô kệ 100 × 50 × 40 cm ⇒ `200000`.

Nhập nhầm theo lít bị chặn, thông báo kèm sẵn số quy đổi:

```json
400 { "message": "Sức chứa 200 cm³ nhỏ hơn 1 lít, nhiều khả năng đang nhập theo lít hoặc m³. Trường maxVolume tính bằng cm³: 200 lít = 200000 cm³, 200 m³ = 200000000 cm³." }
400 { "message": "Sức chứa (maxVolume) phải lớn hơn 0." }
400 { "message": "Sức chứa 80000000 cm³ vượt 50 m³, kiểm tra lại số đã nhập." }
400 { "message": "Tải trọng (maxWeight) phải lớn hơn 0." }
400 { "message": "Tải trọng 9000 kg vượt ngưỡng 5000 kg, kiểm tra lại số đã nhập." }
```

Ngưỡng: thể tích 1.000 – 50.000.000 cm³ (1 lít – 50 m³), tải trọng 0 – 5.000 kg.

Lỗi sức chứa ở put-away cũng nêu đơn vị để phân biệt "ô kệ đầy thật" với "khai sai đơn vị":

```json
400 { "message": "Ô kệ BIN-RCV-01 không đủ sức chứa. Kiện cần 73.200 cm³, ô kệ còn 200 cm³ (sức chứa khai báo 200 cm³). Nếu ô kệ thực tế lớn hơn, kiểm tra lại đơn vị của maxVolume — trường này tính bằng cm³." }
400 { "message": "Ô kệ BIN-RCV-01 không đủ tải trọng. Kiện nặng 12,40 kg, ô kệ còn chịu được 5,00 kg (tải trọng khai báo 150,00 kg)." }
```

---

## 12. Dữ liệu ô kệ đang sai đơn vị

Khảo sát DB chung ngày 04/08/2026 — 25 ô kệ thuộc 4 kho (VN_WH, CN_GZ, KR_WH, JP_WH):

| Nhóm | Số ô | `max_volume` hiện tại | Đúng phải là | kg/lít hiện tại | kg/lít sau sửa |
| --- | --- | --- | --- | --- | --- |
| `BIN-A2-01` | 4 | 80 | 80.000 | 500 | 0,5 |
| `BIN-A1-01/02` | 7 | 100 | 100.000 | 500 | 0,5 |
| `BIN-RCV/OUT` | 12 | 200 | 200.000 | 500 | 0,5 |
| `BIN-A1-01` (VN_WH) | 1 | 10.000.000 | **đã đúng** | 0,1 | 0,1 |
| `BIN-001` (CN_GZ) | 1 | `NULL` | **cần khai** | — | — |

Dấu hiệu nhận biết: tỉ lệ `max_weight / max_volume` = **0,5000** giống hệt ở cả 23 ô — hiểu theo lít thì hợp lý (0,5 kg mỗi lít), hiểu theo cm³ thì thành 500 kg mỗi lít, đặc gấp 25 lần chì.

Hệ quả: **không ô nào trong 23 ô xếp nổi một kiện** 61×40×30 cm (73.200 cm³). Riêng `BIN-001` lọt vì `max_volume` null nên code bỏ qua kiểm sức chứa.

Câu lệnh sửa (chưa chạy):

```sql
SELECT * INTO BINS_BACKUP_20260804 FROM BINS;

UPDATE BINS SET max_volume = max_volume * 1000 WHERE max_volume < 10000;
```

`NULL < 10000` cho ra `NULL` nên `BIN-001` không bị đụng; ô 10 triệu cũng không.
Kiểm tra sau khi chạy — cột cuối phải ra ~0,5:

```sql
SELECT bin_code, max_volume, max_weight,
       CAST(max_weight * 1000 / NULLIF(max_volume,0) AS decimal(10,3)) AS kg_moi_lit
FROM BINS ORDER BY max_volume;
```

---

## 13. Còn treo

- Lọc đơn theo kho ở `GET /api/orders/consignments` (mobile gửi `regionCode`, BE đang bỏ qua).
- 3 endpoint phía kho chưa trả `receiverName/receiverPhone/receiverAddress` — kho Việt Nam sẽ cần khi giao nội địa.
- Yêu cầu của anh Hoàng "người nhận nằm trong kiện thay vì ở đầu đơn" chưa được chốt; hiện người nhận vẫn ở cấp `Order`.
- **Chưa chạy SQL sửa 23 ô kệ** (mục 12) — put-away sẽ luôn báo hết chỗ cho tới khi chạy.
- `BIN-001` kho Quảng Châu còn `max_volume = NULL` ⇒ nhận hàng không giới hạn; cần khai số thật.
- `PutAwayParcelAsync` tạo `Inventory` với `orderItemId` lấy từ **dòng hàng đầu tiên** và `quantity` là **tổng mọi dòng** — sai với mô hình 1 kiện = 1 dòng hàng; nên đổi sang `parcel.OrderItemId`.
