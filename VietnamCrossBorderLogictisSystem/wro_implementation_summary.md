# 📦 TÀI LIỆU TỔNG HỢP NÂNG CẤP HỆ THỐNG XUẤT KHO (WRO) - TH1 & TH2

> **Dự án**: Vietnam Cross Border Logistics System (`vcl-BE`)  
> **Module**: Warehouse Release Requests (WRO)  
> **Commit**: `c0e1f04` - `feat(wro): implement single and batch warehouse release endpoints and customs status tracking`  
> **Ngày cập nhật**: 12/08/2026

---

## 📋 MỤC LỤC
1. [Tổng Quan Yêu Cầu & Giải Pháp](#1-tổng-quan-yêu-cầu--giải-pháp)
2. [Danh Sách Các Endpoint API Mới & Nâng Cấp](#2-danh-sách-các-endpoint-api-mới--nâng-cấp)
3. [Luồng Nghiệp Vụ Chi Tiết (Flow Chart & Sequence)](#3-luồng-nghiệp-vụ-chi-tiết-flow-chart--sequence)
4. [Bảng Ma Trận Trạng Thái (Status & Customs Status)](#4-bảng-ma-trận-trạng-thái-status--customs-status)
5. [Quy Tắc Nghiệp Vụ Cước Phí & Lưu Kho](#5-quy-tắc-nghiệp-vụ-cước-phí--lưu-kho)
6. [Danh Sách File Đã Thay Đổi Trong Codebase](#6-danh-sách-file-đã-thay-đổi-trong-codebase)

---

## 1. TỔNG QUAN YÊU CẦU & GIẢI PHÁP

Hệ thống nâng cấp quy trình xuất kho từ **Kho Nguồn Quốc Tế** về **Kho Việt Nam** chia làm 2 trường hợp cụ thể:

### 🛵 TRƯỜNG HỢP 1: Xuất Kho Lẻ Đi Theo Đơn Hỏa Tốc (`ExportType = "SINGLE"`)
* **Đặc điểm**: Đơn đi riêng 1 mình 1 đơn hỏa tốc (Ký gửi hoặc Mua hộ).
* **Giải pháp**: Nhận vào `orderId`. Backend tự động quét gom toàn bộ các kiện hàng khả dụng (`AVAILABLE`) thuộc về đơn hàng đó đang nằm trong kho để tạo phiếu xuất kho lẻ.

### 📦 TRƯỜNG HỢP 2: Xuất Kho Gom Lô Nhiều Kiện Nhiều Đơn (`ExportType = "BATCH"`)
* **Đặc điểm**: Gộp nhiều kiện hàng lẻ từ các đơn khác nhau (cả Ký gửi lẫn Mua hộ) của nhiều khách hàng khác nhau thành 1 lô hàng duy nhất (đóng trên Pallet / Thùng gỗ / Container) để vận chuyển về VN.
* **Giải pháp**: Nhận mảng danh sách `items` chứa `inventoryId`s. Backend tạo phiếu gom lô chung, tự động trích xuất thông tin dịch vụ (`ConsignmentType`: KÝ GỬI / MUA HỘ) cho từng kiện lẻ.

---

## 2. DANH SÁCH CÁC ENDPOINT API MỚI & NÂNG CẤP

| STT | Method | HTTP Endpoint | Chức Năng | Vai Trò Thực Hiện |
| :---: | :---: | :--- | :--- | :--- |
| **1** | `POST` | `/api/warehouse-release-requests/single` | Tạo phiếu xuất kho lẻ hỏa tốc (TH1) | Khách hàng / Staff / OM |
| **2** | `POST` | `/api/warehouse-release-requests/batch` | Tạo phiếu xuất kho gom lô (TH2) | Staff / OM / Kho |
| **3** | `PUT` | `/api/warehouse-release-requests/{id}/status` | Duyệt phiếu xuất kho (`RELEASE_APPROVED` / `RELEASE_REJECTED`) | OM / Admin |
| **4** | `PUT` | `/api/warehouse-release-requests/{id}/shipping-route` | Gán ĐVVC, tài xế, chuyến bay/tàu & upload file hải quan | OM |
| **5** | `GET` | `/api/warehouse-release-requests/{id}` | Xem chi tiết phiếu xuất kho (Kèm thông tin dịch vụ & hải quan) | Kho / OM / Staff |
| **6** | `POST` | `/api/warehouse-release-requests/{id}/picking-list` | Tạo phiếu soạn hàng Picking (Xem mã ô kệ & loại dịch vụ) | Nhân viên kho |
| **7** | `PUT` | `/api/warehouse-release-requests/{id}/packing` | Xác nhận đóng gói (Điền kích thước, cân nặng, up nhiều ảnh & note) | Nhân viên kho |
| **8** | `PUT` | `/api/warehouse-release-requests/{id}/complete` | Hoàn tất xuất kho (**Trừ kho, nhả ô kệ, dừng tính phí lưu kho**) | Nhân viên kho |
| **9** | `POST` | `/api/warehouse-release-requests/{id}/handover` | Bàn giao hàng cho tài xế / xe container | Kho / OM |

---

## 3. LUỒNG NGHIỆP VỤ CHI TIẾT (FLOW CHART & SEQUENCE)

### 🔄 Tiến Trình 9 Bước Xuất Kho:

```
[BƯỚC 1: Tạo Phiếu WRO] -> [BƯỚC 2: OM Duyệt Phiếu] -> [BƯỚC 3: OM Gán Vận Chuyển & Up Hải Quan] 
       |
       v
[BƯỚC 4: Get Chi Tiết] -> [BƯỚC 5: Kho Tạo Picking] -> [BƯỚC 6: Kho Đóng Gói (Cân/Đo/Up Ảnh/Note)] 
       |
       v
[BƯỚC 7: Kho Hoàn Tất Xuất Kho (Dừng Phí Lưu Kho)] -> [BƯỚC 8: Bàn Giao ĐVVC] -> [BƯỚC 9: Hàng Lên Bay/Tàu (IN_TRANSIT)]
```

---

### 📥 Mẫu Request & Response Tiêu Biểu:

#### 1. API Tạo Phiếu Gom Lô TH2 (`POST /api/warehouse-release-requests/batch`):
* **Request Payload**:
```json
{
  "shelfCode": "LOT-B102",
  "exportReason": "Xuất kho gom lô đi chuyến hàng ngày 12/08",
  "receiverName": "Kho Đích Tân Sơn Nhất - Cty VCL VN",
  "receiverPhone": "0988888888",
  "receiverAddress": "Kho Đích Tân Sơn Nhất, Phường 2, Quận Tân Bình, TP.HCM",
  "items": [
    { "inventoryId": "inv-guid-001", "quantity": 1 },
    { "inventoryId": "inv-guid-002", "quantity": 1 }
  ]
}
```

* **Response Output**:
```json
{
  "message": "Tạo yêu cầu xuất kho gom lô thành công.",
  "data": {
    "wroId": "wro-batch-guid-8899",
    "wroCode": "WRO-20260811-8899",
    "exportBarcode": "EXP-20260811-8899",
    "exportType": "BATCH",
    "status": "RELEASE_PENDING",
    "receiverName": "Kho Đích Tân Sơn Nhất - Cty VCL VN",
    "customsStatus": "CUSTOMS_PENDING",
    "customsStatusText": "Chờ thông quan",
    "isCustomsCleared": false,
    "items": [
      {
        "itemId": "item-01",
        "inventoryId": "inv-guid-001",
        "quantity": 1,
        "packageCode": "PKG-KG-01",
        "orderCode": "ORD-KG-001",
        "consignmentType": "KÝ GỬI"
      },
      {
        "itemId": "item-02",
        "inventoryId": "inv-guid-002",
        "quantity": 1,
        "packageCode": "PKG-MH-02",
        "orderCode": "ORD-MH-002",
        "consignmentType": "MUA HỘ"
      }
    ]
  }
}
```

#### 2. API Kho Đóng Gói Lô (`PUT /api/warehouse-release-requests/{id}/packing`):
* **Request Payload**:
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
    "https://res.cloudinary.com/vcl/image/upload/photo_kien_2.jpg",
    "https://res.cloudinary.com/vcl/image/upload/photo_pallet_tong.jpg"
  ],
  "packingNotes": "Đã hoàn tất đóng gói: Kiện 1 đóng tiêu chuẩn; Kiện 2 bọc màng bóng khí chống sốc. Tất cả được nẹp cố định trên Pallet B102."
}
```

---

## 4. BẢNG MA TRẬN TRẠNG THÁI (STATUS & CUSTOMS STATUS)

### 📊 1. Trạng Thái Vận Chuyển (`Status`):
| Mã Status | Tên Tiếng Việt | Mô Tả Ý Nghĩa |
| :--- | :--- | :--- |
| `RELEASE_PENDING` | **Chờ Duyệt Xuất Kho** | Phiếu vừa khởi tạo, chờ OM duyệt. |
| `RELEASE_APPROVED` | **Đã Duyệt Xuất Kho** | OM chấp thuận xuất kho và gán tuyến vận chuyển. |
| `RELEASE_REJECTED` | **Từ Chối Xuất Kho** | OM từ chối duyệt phiếu. |
| `PICKING` | **Đang Soạn Hàng** | Kho tạo phiếu Picking và nhặt hàng trong kệ. |
| `PACKED` | **Đã Đóng Gói** | Kho đo kích thước/cân nặng, up ảnh và ghi note đóng gói. |
| `RELEASED` | **Hoàn Tất Xuất Kho** | Hàng xuất khỏi kho nguồn. **Trừ kho & Ngắt phí lưu kho**. |
| `HANDED_OVER` | **Đã Bàn Giao Vận Chuyển**| Đã giao cho tài xế / xe container kéo đi. |
| `IN_TRANSIT` | **Đang Vận Chuyển Quốc Tế**| Hàng đã lên máy bay / tàu biển (OM up mã AWB/Container). |
| `ARRIVED_IN_VN` | **Đã Đến Kho Việt Nam** | Máy bay hạ cánh / Tàu cập cảng. Hàng về Kho Việt Nam. |

### 🛃 2. Trạng Thái Thông Quan (`CustomsStatus`):
| Mã CustomsStatus | Tên Tiếng Việt (`customsStatusText`) | Điều Kiện |
| :--- | :--- | :--- |
| `CUSTOMS_PENDING` | 🟡 **Chờ Thông Quan** | Chưa upload chứng từ tờ khai thông quan (`customsDocumentUrls` rỗng). |
| `CUSTOMS_CLEARED` | 🟢 **Đã Thông Quan** | Đã upload ít nhất 1 chứng từ/ảnh tờ khai thông quan (`customsDocumentUrls` có link). |

---

## 5. QUY TẮC NGHIỆP VỤ CƯỚC PHÍ & LƯU KHO

1. **Quy Tắc Dừng Tính Phí Lưu Kho**:
   * Ngay khi Kho gọi API `PUT /complete`, hệ thống tự động sinh nhật ký `InventoryMovement` với `MovementType = "OUTBOUND"`.
   * Service `StorageFeeService` tự động nhận biết mốc thời gian này để **ngắt đếm số ngày lưu kho** của kiện hàng.

2. **Quy Tắc Cước Phí Ký Gửi & Mua Hộ Khi Gom Lô (TH2)**:
   * **Vận chuyển vật lý**: Gom chung 1 Pallet/Thùng hàng về Việt Nam.
   * **Hạch toán cước phí**: Tách biệt 100% theo từng Đơn hàng (`OrderId`). 
     * Đơn Ký gửi tính theo bảng giá Ký gửi.
     * Đơn Mua hộ tính theo đơn giá Mua hộ + Phí dịch vụ mua hộ (Tỷ giá, % service fee).

3. **Cước Phí Vận Chuyển Chặng Đầu (Kho -> Cảng/Máy Bay)**:
   * Đã thống nhất bao gộp vào **Đơn giá cước vận chuyển quốc tế tổng (VND/kg)** trong bảng giá `ServicePricing`.

---

## 6. DANH SÁCH FILE ĐÃ THAY ĐỔI TRONG CODEBASE

* `VCL_DLL/Models/WroRequest.cs`: Thêm thuộc tính `ExportType`, `PackingNotes`, `PackagingPhotos`.
* `VCL_BLL/DTOs/WroDtos.cs`: Thêm `CreateSingleWroRequestDto`, `CreateBatchWroRequestDto`, cập nhật `WroDetailResponseDto`, `PickingListResponseDto` bổ sung thông tin người nhận, dịch vụ từng kiện và trạng thái thông quan.
* `VCL_BLL/Interface/IWroService.cs`: Khai báo các interface method mới.
* `VCL_BLL/Services/WroService.cs`: Triển khai chi tiết logic xử lý TH1, TH2, mapping DTO, dừng phí lưu kho.
* `VCL_API/Controllers/WroController.cs`: Khai báo 2 endpoint RESTful mới `POST /single` và `POST /batch`.

---
*Tài liệu được khởi tạo tự động bởi AI Coding Assistant - Antigravity Agent.*
