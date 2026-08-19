# 📘 CONTRACT API MODULE XUẤT KHO (WRO) DÀNH CHO FRONTEND (FE)

> ⚠️ **Bảng trạng thái trong doc này đã lỗi thời từ 18/08/2026.** Xem `API_CHANGES_2026-08-18.md` — phiếu xuất kho nay duyệt một lần rồi hết việc, mọi mốc sau đó thuộc về lô vận chuyển.


> **Dự án**: Vietnam Cross Border Logistics System (`vcl-BE`)  
> **Module**: Warehouse Release Requests (WRO)  
> **Phiên bản API**: v1.2  
> **Ngày cập nhật**: 12/08/2026  
> **Base URL**: `https://api-vcl.zushin.io.vn` (hoặc `http://localhost:5209` ở môi trường Local)  
> **Authentication Header**: `Authorization: Bearer <JWT_TOKEN>`  

---

## 📋 MỤC LỤC
1. [Cấu Trúc Response Chuẩn (Canonical Response Shape)](#1-cấu-trúc-response-chuẩn)
2. [Bảng Ma Trận Enum & Trạng Thái (Status & Customs Status)](#2-bảng-ma-trận-enum--trạng-thái)
3. [Sơ Đồ Tiến Trình Xuất Kho (WRO Flow Chart)](#3-sơ-đồ-tiến-trình-xuất-kho)
4. [Chi Tiết Danh Sách API Contract (Request & Response)](#4-chi-tiết-danh-sách-api-contract)

---

## 1. CẤU TRÚC RESPONSE CHUẨN

Tất cả các API trong module WRO tuân thủ response shape thống nhất:

### Response Thành Công (Success Response)
```json
{
  "message": "Thông báo ngắn gọn cho người dùng",
  "data": { ... }
}
```

### Response Lỗi (Error Response)
```json
{
  "message": "Thông báo lỗi hiển thị trên giao diện FE",
  "error": "Chi tiết lỗi kỹ thuật (nếu có)"
}
```

---

## 2. BẢNG MA TRẬN ENUM & TRẠNG THÁI

### 📦 Loại Xuất Kho (`ExportType`)
* `SINGLE`: Xuất kho lẻ hỏa tốc đi riêng theo đơn hàng.
* `BATCH`: Xuất kho gom lô nhiều kiện nhiều đơn đi chung Pallet/Container.

---

### 📊 Trạng Thái Xuất Kho (`Status`)
| Mã Status | Tên Tiếng Việt | Mô Tả Ý Nghĩa | Thao Tác Trực Tiếp |
| :--- | :--- | :--- | :--- |
| `RELEASE_PENDING` | **Chờ Duyệt Xuất Kho** | Phiếu vừa khởi tạo, chờ OM duyệt | Tạo phiếu WRO (`POST /single` hoặc `POST /batch`) |
| `RELEASE_APPROVED` | **Đã Duyệt Xuất Kho** | OM chấp thuận xuất kho và gán tuyến | OM duyệt (`PUT /{id}/status`) |
| `RELEASE_REJECTED` | **Từ Chối Xuất Kho** | OM từ chối duyệt phiếu | OM từ chối (`PUT /{id}/status`) |
| `PICKING` | **Đang Soạn Hàng** | Kho nhặt hàng trong kệ | Kho tạo phiếu Picking (`POST /{id}/picking-list`) |
| `PICKED` | **Đã Soạn Hàng** | Kho đã nhặt đủ hàng ra bàn đóng gói | Kho xác nhận Picking (`PUT /picking-lists/{id}/confirm`) |
| `PACKED` | **Đã Đóng Gói** | Đã đo kích thước, cân nặng, up ảnh đóng gói | Kho đóng gói (`PUT /{id}/packing`) |
| `RELEASED` | **Hoàn Tất Xuất Kho** | Hàng xuất khỏi kho nguồn. **Dừng phí lưu kho** | Kho bấm hoàn tất (`PUT /{id}/complete`) |
| `HANDED_OVER` | **Đã Bàn Giao Vận Chuyển**| Đã giao cho tài xế / xe container | Kho/OM bàn giao (`POST /{id}/handover`) |
| `IN_TRANSIT` | **Đang Vận Chuyển Quốc Tế**| Hàng lên máy bay / tàu biển | OM gán tuyến/AWB (`PUT /{id}/shipping-route`) |
| `ARRIVED_IN_VN` | **Đã Đến Kho Việt Nam** | Hàng về đến kho Việt Nam | OM/Kho VN xác nhận đến |

---

### 🛃 Trạng Thái Thông Quan (`CustomsStatus`)
FE kiểm tra `customsStatus` hoặc `isCustomsCleared` để hiển thị badge màu trên giao diện:
* 🟡 `CUSTOMS_PENDING` (`isCustomsCleared = false`): **Chờ thông quan** (Chưa upload chứng từ tờ khai).
* 🟢 `CUSTOMS_CLEARED` (`isCustomsCleared = true`): **Đã thông quan** (Đã upload chứng từ tờ khai).

---

## 3. SƠ ĐỒ TIẾN TRÌNH XUẤT KHO

```
[1. POST /single hoặc /batch] -> Tạo phiếu WRO (RELEASE_PENDING)
       │
       ▼
[2. PUT /{id}/status] ---------> OM Duyệt phiếu (RELEASE_APPROVED)
       │
       ▼
[3. PUT /{id}/shipping-route] --> OM Gán ĐVVC, Tài xế, Mã AWB & Upload Hải Quan
       │
       ▼
[4. POST /{id}/picking-list] ---> Kho Tạo phiếu Picking (PICKING)
       │
       ▼
[5. PUT /picking-lists/{id}/confirm] -> Kho Xác nhận soạn xong (PICKED)
       │
       ▼
[6. PUT /{id}/packing] ---------> Kho Đóng gói, cân nặng, kích thước, up ảnh (PACKED)
       │
       ▼
[7. PUT /{id}/complete] --------> Kho Bấm Hoàn tất xuất kho (RELEASED - Dừng phí lưu kho)
       │
       ▼
[8. POST /{id}/handover] -------> Bàn giao cho Đơn vị vận chuyển (HANDED_OVER)
       │
       ▼
[9. Hàng đi chặng quốc tế] ----> Đang vận chuyển quốc tế (IN_TRANSIT)
```

---

## 4. CHI TIẾT DANH SÁCH API CONTRACT

---

### 1. API Tạo Phiếu Xuất Kho Lẻ Hỏa Tốc (TH1)
Cho phép khách hàng hoặc nhân viên tạo phiếu xuất hỏa tốc cho toàn bộ các kiện hàng thuộc 1 đơn.

* **HTTP Method**: `POST`
* **Endpoint**: `/api/warehouse-release-requests/single`
* **Authorization**: All Roles (`Customer`, `WarehouseStaff`, `Staff`, `OperationsManager`, `Admin`)

#### Request Body
> **Lưu ý cho FE**: FE có thể truyền `orderId` (ID của Đơn ký gửi/Mua hộ) HOẶC `purchaseRequestId` (ID của Yêu cầu mua hộ). Backend sẽ tự động truy vết và gom toàn bộ tồn kho tương ứng!

```json
{
  "orderId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "purchaseRequestId": null,
  "shelfCode": "Shelf 1",
  "exportReason": "Xuất kho hỏa tốc giao gấp cho khách hàng",
  "receiverName": "Trung Đình",
  "receiverPhone": "0987543267",
  "receiverAddress": "Tổ 3, Ấp Hóc Măng, Phường An Điền, TP Bến Cát, Bình Dương"
}
```

#### Success Response (`201 Created`)
```json
{
  "message": "Tạo yêu cầu xuất kho lẻ thành công.",
  "data": {
    "wroId": "e1f2a3b4-5678-90ab-cdef-111122223333",
    "wroCode": "WRO-20260812-1001",
    "exportBarcode": "EXP-20260812-1001",
    "exportType": "SINGLE",
    "status": "RELEASE_PENDING",
    "shelfCode": "Shelf 1",
    "exportReason": "Xuất kho hỏa tốc giao gấp cho khách hàng",
    "receiverName": "Trung Đình",
    "receiverPhone": "0987543267",
    "deliveryAddress": "Tổ 3, Ấp Hóc Măng, Phường An Điền, TP Bến Cát, Bình Dương",
    "consigneeName": "Trung Đình",
    "consigneePhone": "0987543267",
    "consigneeAddress": "Tổ 3, Ấp Hóc Măng, Phường An Điền, TP Bến Cát, Bình Dương",
    "customsStatus": "CUSTOMS_PENDING",
    "customsStatusText": "Chờ thông quan",
    "isCustomsCleared": false,
    "createdAt": "2026-08-12T07:00:00Z",
    "items": [
      {
        "itemId": "a1b2c3d4-0001",
        "inventoryId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
        "quantity": 1,
        "packageCode": "PKG-KG-8801",
        "orderCode": "KG-20260811-1001",
        "consignmentType": "KÝ GỬI"
      }
    ]
  }
}
```

---

### 2. API Tạo Phiếu Xuất Kho Gom Lô Nối Chuyến (TH2)
Cho phép gom nhiều kiện hàng lẻ từ các đơn Ký gửi & Mua hộ khác nhau thành 1 lô xuất chung.

* **HTTP Method**: `POST`
* **Endpoint**: `/api/warehouse-release-requests/batch`
* **Authorization**: `WarehouseStaff`, `Staff`, `OperationsManager`, `Operations`, `Admin`

#### Request Body
> **Lưu ý cho FE**: TH2 cho phép linh hoạt gom lô theo 3 cách (hoặc kết hợp cả 3):
> 1. Truyền mảng `items`: Gom lẻ từng kiện/tồn kho (`inventoryId`).
> 2. Truyền mảng `orderIds`: Gom toàn bộ tồn kho thuộc các Đơn hàng (Ký gửi / Mua hộ).
> 3. Truyền mảng `purchaseRequestIds`: Gom toàn bộ tồn kho thuộc các Yêu cầu mua hộ.

```json
{
  "shelfCode": "LOT-B102",
  "exportReason": "Xuất kho gom lô đi chuyến hàng ngày 12/08",
  "receiverName": "Kho Đích Tân Sơn Nhất - Cty VCL VN",
  "receiverPhone": "0988888888",
  "receiverAddress": "Kho Đích Tân Sơn Nhất, Phường 2, Quận Tân Bình, TP.HCM",
  "items": [
    { "inventoryId": "3fa85f64-5717-4562-b3fc-2c963f66afa6", "quantity": 1 }
  ],
  "orderIds": [
    "3fa85f64-5717-4562-b3fc-2c963f66afa6"
  ],
  "purchaseRequestIds": [
    "5c9803b9-1234-4567-89ab-cdef01234567"
  ]
}
```

#### Success Response (`201 Created`)
```json
{
  "message": "Tạo yêu cầu xuất kho gom lô thành công.",
  "data": {
    "wroId": "e2f3a4b5-6789-01ab-cdef-444455556666",
    "wroCode": "WRO-20260812-8899",
    "exportBarcode": "EXP-20260812-8899",
    "exportType": "BATCH",
    "status": "RELEASE_PENDING",
    "shelfCode": "LOT-B102",
    "exportReason": "Xuất kho gom lô đi chuyến hàng ngày 12/08",
    "receiverName": "Kho Đích Tân Sơn Nhất - Cty VCL VN",
    "receiverPhone": "0988888888",
    "deliveryAddress": "Kho Đích Tân Sơn Nhất, Phường 2, Quận Tân Bình, TP.HCM",
    "customsStatus": "CUSTOMS_PENDING",
    "customsStatusText": "Chờ thông quan",
    "isCustomsCleared": false,
    "createdAt": "2026-08-12T07:05:00Z",
    "items": [
      {
        "itemId": "b1c2d3e4-0001",
        "inventoryId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
        "quantity": 1,
        "packageCode": "PKG-KG-8801",
        "orderCode": "KG-20260811-1001",
        "consignmentType": "KÝ GỬI"
      },
      {
        "itemId": "b1c2d3e4-0002",
        "inventoryId": "4bb96f75-6828-5673-c4gd-3d074f77bfb7",
        "quantity": 2,
        "packageCode": "PKG-MH-2001",
        "orderCode": "MH-20260811-2001",
        "consignmentType": "MUA HỘ"
      }
    ]
  }
}
```

---

### 3. API Duyệt / Từ Chối Phiếu Xuất Kho
* **HTTP Method**: `PUT`
* **Endpoint**: `/api/warehouse-release-requests/{id}/status`
* **Authorization**: `OperationsManager`, `Operations`, `Admin`, `Staff`

#### Request Body (Duyệt)
```json
{
  "status": "RELEASE_APPROVED",
  "rejectionReason": null
}
```

#### Request Body (Từ Chối)
```json
{
  "status": "RELEASE_REJECTED",
  "rejectionReason": "Hàng đang vướng kiểm định chưa đủ điều kiện xuất"
}
```

#### Success Response (`200 OK`)
```json
{
  "message": "Cập nhật trạng thái yêu cầu xuất kho thành công.",
  "data": {
    "wroId": "e1f2a3b4-5678-90ab-cdef-111122223333",
    "wroCode": "WRO-20260812-1001",
    "status": "RELEASE_APPROVED"
  }
}
```

---

### 4. API Gán Tuyến Vận Chuyển & Upload Hải Quan
* **HTTP Method**: `PUT`
* **Endpoint**: `/api/warehouse-release-requests/{id}/shipping-route`
* **Authorization**: `OperationsManager`, `Operations`, `Admin`, `Staff`

#### Request Body
```json
{
  "carrierId": "5c9803b9-1234-4567-89ab-cdef01234567",
  "shippingRouteId": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
  "shippingRoute": "CN-VN-AIR",
  "driverName": "Nguyễn Văn Tài",
  "driverPhone": "0912345678",
  "vehicleNumber": "51C-123.45",
  "trackingNumber": "AWB-88992026",
  "handoverNotes": "Bàn giao xe container số 02 chuyến hàng đêm",
  "customsDocumentUrls": [
    "https://res.cloudinary.com/vcl/image/upload/to_khai_hai_quan_01.pdf"
  ]
}
```

#### Success Response (`200 OK`)
```json
{
  "message": "Gán tuyến vận chuyển cho yêu cầu xuất kho thành công.",
  "data": {
    "wroId": "e1f2a3b4-5678-90ab-cdef-111122223333",
    "wroCode": "WRO-20260812-1001",
    "status": "RELEASE_APPROVED",
    "carrierId": "5c9803b9-1234-4567-89ab-cdef01234567",
    "carrierName": "GHN Express",
    "shippingRouteId": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
    "shippingRoute": "CN-VN-AIR",
    "note": ""
  }
}
```

---

### 5. API Lấy Chi Tiết Phiếu Xuất Kho (Hiển Thị Đầy Đủ Chi Tiết Kho)
* **HTTP Method**: `GET`
* **Endpoint**: `/api/warehouse-release-requests/{id}`
* **Authorization**: All Roles

#### Success Response (`200 OK`)
```json
{
  "message": "Lấy chi tiết yêu cầu xuất kho thành công.",
  "data": {
    "wroId": "e1f2a3b4-5678-90ab-cdef-111122223333",
    "wroCode": "WRO-20260812-1001",
    "exportBarcode": "EXP-20260812-1001",
    "exportType": "SINGLE",
    "createdByName": "Bùi Đình Trọng",
    "createdByUserRole": "Customer",
    "warehouseName": "Kho Bằng Tường - Trung Quốc",
    "customerId": "cus-guid-001",
    "customerName": "Bùi Đình Trọng",
    "receiverName": "Trung Đình",
    "receiverPhone": "0987543267",
    "deliveryAddress": "Tổ 3, Ấp Hóc Măng, Phường An Điền, TP Bến Cát, Bình Dương",
    "consigneeName": "Trung Đình",
    "consigneePhone": "0987543267",
    "consigneeAddress": "Tổ 3, Ấp Hóc Măng, Phường An Điền, TP Bến Cát, Bình Dương",
    "status": "RELEASE_APPROVED",
    "shelfCode": "Shelf 1",
    "exportReason": "Xuất kho hỏa tốc giao gấp cho khách hàng",
    "carrierId": "5c9803b9-1234-4567-89ab-cdef01234567",
    "carrierName": "GHN Express",
    "shippingRouteId": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
    "shippingRoute": "CN-VN-AIR",
    "driverName": "Nguyễn Văn Tài",
    "driverPhone": "0912345678",
    "vehicleNumber": "51C-123.45",
    "trackingNumber": "AWB-88992026",
    "customsDocumentUrls": [
      "https://res.cloudinary.com/vcl/image/upload/to_khai_hai_quan_01.pdf"
    ],
    "customsStatus": "CUSTOMS_CLEARED",
    "customsStatusText": "Đã thông quan",
    "isCustomsCleared": true,
    "createdAt": "2026-08-12T07:00:00Z",
    "totalQuantity": 1,
    "items": [
      {
        "itemId": "a1b2c3d4-0001",
        "inventoryId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
        "quantity": 1,
        "packageCode": "PKG-KG-8801",
        "orderCode": "KG-20260811-1001",
        "consignmentType": "KÝ GỬI",
        "productName": "Quần áo thời trang mẫu A",
        "zoneName": "Khu A - Hàng Tiêu Dùng",
        "shelfCode": "Shelf 1",
        "binCode": "BIN-A101"
      }
    ]
  }
}
```

---

### 6. API Tạo Phiếu Soạn Hàng Picking (Nhân Viên Kho)
* **HTTP Method**: `POST`
* **Endpoint**: `/api/warehouse-release-requests/{id}/picking-list`
* **Authorization**: `WarehouseStaff`, `Staff`, `OperationsManager`, `Operations`, `Admin`

#### Request Body: *(Không cần Body)*

#### Success Response (`201 Created`)
```json
{
  "message": "Tạo phiếu picking thành công.",
  "data": {
    "pickingListId": "p1k2l3m4-5678-90ab-cdef-999988887777",
    "wroRequestId": "e1f2a3b4-5678-90ab-cdef-111122223333",
    "wroCode": "WRO-20260812-1001",
    "customerId": "cus-guid-001",
    "customerName": "Bùi Đình Trọng",
    "status": "PENDING",
    "items": [
      {
        "itemId": "pi-01",
        "inventoryId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
        "orderCode": "KG-20260811-1001",
        "consignmentType": "KÝ GỬI",
        "packageCode": "PKG-KG-8801",
        "productName": "Quần áo thời trang mẫu A",
        "binCode": "BIN-A101",
        "shelfCode": "Shelf 1",
        "quantity": 1
      }
    ]
  }
}
```

---

### 7. API Xác Nhận Soạn Hàng Picking
* **HTTP Method**: `PUT`
* **Endpoint**: `/api/picking-lists/{pickingListId}/confirm`
* **Authorization**: `WarehouseStaff`, `Staff`, `OperationsManager`, `Operations`, `Admin`

#### Request Body
```json
{
  "items": [
    {
      "inventoryId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "quantity": 1
    }
  ]
}
```

#### Success Response (`200 OK`)
```json
{
  "message": "Xác nhận picking thành công.",
  "data": {
    "pickingListId": "p1k2l3m4-5678-90ab-cdef-999988887777",
    "wroRequestId": "e1f2a3b4-5678-90ab-cdef-111122223333",
    "status": "PICKED",
    "confirmedBy": "user-guid-staff",
    "confirmedByName": "Nguyễn Văn Kho",
    "confirmedAt": "2026-08-12T07:15:00Z",
    "items": [
      {
        "inventoryId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
        "quantity": 1,
        "binCode": "BIN-A101",
        "shelfCode": "Shelf 1"
      }
    ]
  }
}
```

---

### 8. API Cập Nhật Đóng Gói (Cân/Đo/Up Ảnh/Note)
* **HTTP Method**: `PUT`
* **Endpoint**: `/api/warehouse-release-requests/{id}/packing`
* **Authorization**: `WarehouseStaff`, `Staff`, `OperationsManager`, `Operations`, `Admin`

#### Request Body
```json
{
  "packageType": "PALLET",
  "packageCount": 1,
  "weight": 85.5,
  "length": 120.0,
  "width": 100.0,
  "height": 110.0,
  "packagingPhotos": [
    "https://res.cloudinary.com/vcl/image/upload/photo_kien_1.jpg",
    "https://res.cloudinary.com/vcl/image/upload/photo_pallet_tong.jpg"
  ],
  "packingNotes": "Đã quấn màng co và nẹp gỗ định hình Pallet B102."
}
```

#### Success Response (`200 OK`)
```json
{
  "message": "Cập nhật đóng gói kiện xuất kho thành công.",
  "data": {
    "wroId": "e1f2a3b4-5678-90ab-cdef-111122223333",
    "wroCode": "WRO-20260812-1001",
    "exportBarcode": "EXP-20260812-1001",
    "status": "PACKED",
    "packageType": "PALLET",
    "packageCount": 1,
    "weight": 85.5,
    "volumeWeight": 26.4,
    "photosCount": 2,
    "packedAt": "2026-08-12T07:25:00Z"
  }
}
```

---

### 9. API Hoàn Tất Xuất Kho (Trừ Kho & Dừng Phí Lưu Kho)
* **HTTP Method**: `PUT`
* **Endpoint**: `/api/warehouse-release-requests/{id}/complete`
* **Authorization**: `WarehouseStaff`, `Staff`, `OperationsManager`, `Operations`, `Admin`

#### Request Body
```json
{
  "items": [
    {
      "inventoryId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "quantity": 1
    }
  ]
}
```

#### Success Response (`200 OK`)
```json
{
  "message": "Hoàn tất xuất kho thành công.",
  "data": {
    "wroRequestId": "e1f2a3b4-5678-90ab-cdef-111122223333",
    "wroCode": "WRO-20260812-1001",
    "status": "RELEASED",
    "releasedBy": "user-guid-staff",
    "releasedByName": "Nguyễn Văn Kho",
    "releasedAt": "2026-08-12T07:30:00Z",
    "warehouseName": "Kho Bằng Tường - Trung Quốc",
    "items": [
      {
        "inventoryId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
        "quantity": 1,
        "status": "RELEASED"
      }
    ]
  }
}
```

---

### 10. API Bàn Giao Hàng Cho Đơn Vị Vận Chuyển
* **HTTP Method**: `POST`
* **Endpoint**: `/api/warehouse-release-requests/{id}/handover`
* **Authorization**: `WarehouseStaff`, `Staff`, `OperationsManager`, `Operations`, `Admin`

#### Request Body
```json
{
  "carrierId": "5c9803b9-1234-4567-89ab-cdef01234567",
  "driverName": "Nguyễn Văn Tài",
  "driverPhone": "0912345678",
  "vehicleNumber": "51C-123.45",
  "trackingNumber": "AWB-88992026",
  "handoverNotes": "Đã bàn giao cho tài xế xe tải nhận hàng rời kho"
}
```

#### Success Response (`200 OK`)
```json
{
  "message": "Bàn giao đơn vị vận chuyển thành công.",
  "data": {
    "wroId": "e1f2a3b4-5678-90ab-cdef-111122223333",
    "wroCode": "WRO-20260812-1001",
    "exportBarcode": "EXP-20260812-1001",
    "status": "HANDED_OVER",
    "carrierId": "5c9803b9-1234-4567-89ab-cdef01234567",
    "carrierName": "GHN Express",
    "driverName": "Nguyễn Văn Tài",
    "driverPhone": "0912345678",
    "vehicleNumber": "51C-123.45",
    "trackingNumber": "AWB-88992026",
    "handoverNotes": "Đã bàn giao cho tài xế xe tải nhận hàng rời kho",
    "handoverAt": "2026-08-12T07:35:00Z"
  }
}
```

---

### 11. API Lấy Danh Sách Phiếu Xuất Kho (Phân Trang & Lọc)
* **HTTP Method**: `GET`
* **Endpoint**: `/api/warehouse-release-requests?status=RELEASE_APPROVED&searchTerm=WRO-2026&pageIndex=1&pageSize=10`
* **Authorization**: All Roles

#### Success Response (`200 OK`)
```json
{
  "message": "Lấy danh sách yêu cầu xuất kho thành công.",
  "data": {
    "totalCount": 1,
    "pageIndex": 1,
    "pageSize": 10,
    "items": [
      {
        "wroId": "e1f2a3b4-5678-90ab-cdef-111122223333",
        "wroCode": "WRO-20260812-1001",
        "exportBarcode": "EXP-20260812-1001",
        "exportType": "SINGLE",
        "status": "RELEASE_APPROVED",
        "receiverName": "Trung Đình",
        "customsStatus": "CUSTOMS_CLEARED",
        "customsStatusText": "Đã thông quan",
        "isCustomsCleared": true,
        "createdAt": "2026-08-12T07:00:00Z",
        "items": [...]
      }
    ]
  }
}
```

---

*Tài liệu API Contract được sinh tự động và đồng bộ trực tiếp từ codebase Backend (`vcl-BE`).*
