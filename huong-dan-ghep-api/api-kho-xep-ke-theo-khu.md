# Ghép API kho — Xếp kệ theo khu

Tài liệu cho FE ghép luồng xếp hàng vào kệ theo loại khu, đang chạy trên production (cập nhật 17/09/2026).
Nối tiếp phần 1 [`api-ky-gui-1.md`](api-ky-gui-1.md) (từ tạo đơn tới khi kho check-in kiện).

- Base URL: `https://vcl.henrytech.cloud`
- Swagger: `https://vcl.henrytech.cloud/swagger/index.html`
- Mọi API gửi `Authorization: Bearer <token>`. Lỗi trả `{ "message": "..." }` — hiển thị nguyên `message`.
- Tài khoản demo, đơn demo: [`README.md`](README.md). Thử bằng `demo.kho.cn` (nhân viên kho Quảng Châu) và `demo.quanlykho` (quản lý kho) ở **Kho Quảng Châu** `5F8A0B1C-2D3E-4F5A-6B7C-8D9E0F1A2B3C`.

## Luật chính

**Hàng lưu kho chỉ được nằm ở ô kệ thuộc khu `STORAGE` đang dùng.**

Server chặn ở mọi luồng đưa kiện vào ô: xếp kệ, chuyển ô, tách kiện, gộp kiện, nhận lại hàng hoàn, tự xếp khi duyệt phiếu nhập kho VN, và gợi ý vị trí chỉ trả ô hợp lệ.

Cấu trúc kho: **Kho → Khu (zone) → Kệ (shelf) → Ô kệ (bin)**. Mỗi khu có một loại:

| `zoneType` | Khu | Xếp hàng lưu kho? | Dùng khi |
|---|---|---|---|
| `RECEIVING` | Khu nhận | Không | Hàng vừa check-in, chờ xếp kệ |
| `QUARANTINE` | Khu cách ly | Không | Hàng lệch/hỏng chờ quản lý kho quyết |
| `STORAGE` | Khu lưu kho | **Có** | Hàng đã nhập kho chính thức |
| `OUTBOUND` | Khu xuất | Không | Hàng đã soạn, chờ bàn giao |
| (trống) | Khu chưa phân loại | Không | Cần quản lý kho khai loại |

Khu có `status`: `ACTIVE` (đang dùng) / `INACTIVE` (ngừng dùng). Ô kệ có `isActive`.

## Luồng tổng quan

```
[Thiết lập — Admin / Quản lý kho]
Khai khu + loại khu → khai kệ, ô kệ (sức chứa cm³, tải trọng kg)
   ↓
[Nhận hàng — Nhân viên kho]  (phần 1: quét phiếu WRN-, check-in)
Kiện RECEIVED, chưa có ô
   ├─ có chênh lệch chưa duyệt → chờ quản lý kho, chưa xếp kệ được
   └─ phiếu cho phép xếp kệ
        ↓
[Xếp kệ]
Danh sách chờ xếp → (tuỳ chọn) gợi ý vị trí → quét mã ô → xác nhận
   ↓ server kiểm: đúng kho của phiếu, ô + khu đang dùng, khu STORAGE, đủ thể tích/tải trọng
Kiện STORED, tạo tồn kho, bắt đầu tính ngày lưu kho
   ↓ (khi cần)
Chuyển ô trong khu lưu kho / tách, gộp kiện
   ↓ (định kỳ)
Báo cáo kiện nằm sai khu → chuyển về khu lưu kho
```

## Vai trò

| Việc | Role |
|---|---|
| Xem khu, sơ đồ, danh sách vị trí, báo cáo kiện sai khu | `Admin`, `OperationsManager`, `WarehouseStaff` (+ `Sale` xem vị trí) |
| Khai loại khu, đổi tên/mã, bật/tắt khu | `Admin`, `OperationsManager` |
| Khai báo / sửa ô kệ | `Admin`, `OperationsManager`, `WarehouseStaff` |
| Xoá ô kệ | `Admin`, `OperationsManager` |
| Xếp kệ, chuyển ô, gợi ý vị trí | `WarehouseStaff`, `Admin`, `OperationsManager` |
| Tách / gộp kiện | `WarehouseStaff`, `Admin` |

---

## A. Thiết lập khu và ô kệ (Admin / Quản lý kho)

### A1. `GET /api/warehouses/{warehouseId}/zones` — danh sách khu

```json
{
  "message": "Lấy danh sách khu kho thành công.",
  "data": [
    {
      "zoneId": "guid",
      "warehouseId": "guid",
      "zoneCode": "KHA",
      "zoneName": "Khu A",
      "zoneType": "STORAGE",
      "zoneTypeText": "khu lưu kho",
      "status": "ACTIVE",
      "acceptsStorage": true,
      "shelfCount": 2,
      "binCount": 3,
      "activeBinCount": 3,
      "parcelCount": 21
    }
  ]
}
```

- `acceptsStorage = true` khi khu là `STORAGE` và `ACTIVE` — FE dùng để tô màu khu được xếp hàng.
- Danh sách xếp theo thứ tự loại: nhận → cách ly → lưu kho → xuất.

### A2. `PUT /api/warehouse-zones/{zoneId}` — khai loại khu / sửa khu

```json
{ "zoneType": "STORAGE", "status": "ACTIVE", "zoneName": "Khu A", "zoneCode": "KHA" }
```

- `zoneType` **bắt buộc**, một trong `RECEIVING`, `QUARANTINE`, `STORAGE`, `OUTBOUND`.
- `status` tuỳ chọn: `ACTIVE` / `INACTIVE` (bỏ trống giữ nguyên). `zoneName`, `zoneCode` tuỳ chọn, không trùng khu khác trong cùng kho.
- **Khu lưu kho đang chứa hàng không đổi sang loại khác và không ngừng dùng được** → 400 "Khu ... đang chứa N kiện ... Chuyển hàng sang ô khu lưu kho khác trước."

Response 200 `{ "message", "data": <khu như A1> }`.

### A3. Khai báo ô kệ

`GET /api/warehouses/{warehouseId}/locations?isActive=` → `{ "items": [ ... ] }`, mỗi ô:
`locationId (= binId), binCode, shelfId, shelfCode, zoneId, zoneName, zoneType, warehouseId, warehouseName, maxVolume, maxWeight, isActive, note`.

`POST /api/warehouses/{warehouseId}/locations` — tạo ô (tạo luôn khu/kệ nếu chưa có):

```json
{
  "zoneName": "Khu B",
  "zoneType": "STORAGE",
  "shelfCode": "SHELF-B1",
  "binCode": "BIN-B1-01",
  "maxVolume": 300000,
  "maxWeight": 100,
  "isActive": true,
  "note": ""
}
```

Response **201** là object ô kệ (không bọc `data`), có `zoneType`.

- `zoneName` là **khu mới** → bắt buộc `zoneType` (400 nếu thiếu).
- `zoneName` là khu **đã có** → bỏ trống `zoneType`. Gửi loại khác loại hiện tại → 400 (đổi loại qua A2).
- `maxVolume` tính bằng **cm³** (ô 100×60×50 cm = 300000), `maxWeight` bằng kg. `binCode` không trùng trong kho.

`PUT /api/warehouse-locations/{locationId}` — sửa ô, cùng body. Ô **đang chứa hàng** mà dời sang khu không phải khu lưu kho → 400. Ô đang chứa hàng không tắt được.

`DELETE /api/warehouse-locations/{locationId}` — xoá ô trống.

`GET /api/warehouses/{warehouseId}/locations/active` → `{ "items": [...] }` — chỉ ô đang dùng (dropdown chọn ô).

### A4. Sơ đồ kho

Sơ đồ là lưới toạ độ (hàng, cột) để vẽ kho; mỗi ô lưới gắn với một khu, kệ hoặc ô kệ.

| API | Role | Response |
|---|---|---|
| `GET /api/warehouses/{warehouseId}/layout/zones` | Admin, OM, WarehouseStaff | Cây khu → kệ → ô: `{ message, data: [ { zoneId, zoneCode, zoneName, zoneType, status, rowIndex, columnIndex, width, height, shelves: [ { shelfId, shelfCode, rowIndex, columnIndex, bins: [ { binId, binCode, rowIndex, columnIndex, status, currentVolume, maxVolume, currentWeight, maxWeight } ] } ] } ] }` |
| `GET /api/warehouses/{warehouseId}/layout` | Admin, OM, WarehouseStaff | Danh sách ô lưới: `{ message, data: [ { id, zoneId, shelfId, binId, rowIndex, columnIndex, displayLabel, layoutType, status, width, height, colorCode, zoneName, shelfCode, binCode } ] }` |
| `GET /api/warehouses/{warehouseId}/layout/status` | Admin, OM, WarehouseStaff | Mức lấp đầy từng ô lưới: `{ message, data: [ { layoutId, zoneId, shelfId, binId, displayLabel, rowIndex, columnIndex, status, currentItemCount, currentWeight, maxWeight, utilizationRate } ] }` — `status`: `AVAILABLE` · `OCCUPIED` · `FULL` · `LOCKED` · `INACTIVE` |
| `POST /api/warehouses/{warehouseId}/layout` | Admin, OperationsManager | 201 `{ message, data }` |
| `PUT /api/warehouses/{warehouseId}/layout/{layoutId}` | Admin, OperationsManager | `{ message, data }` |
| `DELETE /api/warehouses/{warehouseId}/layout/{layoutId}` | Admin, OperationsManager | `{ message }` |

```json
{ "layoutType": "BIN", "zoneId": "guid", "shelfId": "guid", "binId": "guid", "rowIndex": 2, "columnIndex": 3,
  "displayLabel": "BIN-A1-02", "status": "ACTIVE", "width": 1, "height": 1, "colorCode": "#4CAF50" }
```

- `layoutType`: `ZONE` · `SHELF` · `BIN`. `status`: `ACTIVE` · `LOCKED` · `INACTIVE` (ô lưới `LOCKED`/`INACTIVE` không được gợi ý xếp kệ).
- Toạ độ không trùng trong kho; khu/kệ/ô gắn vào phải đúng cây của kho.
- **Gợi ý vị trí (B2) chỉ xét ô kệ đã có ô lưới `BIN` trên sơ đồ.**

**Thử:** `demo.kho.cn` — `GET /api/warehouses/5F8A0B1C-2D3E-4F5A-6B7C-8D9E0F1A2B3C/zones`: "Khu A" là `STORAGE`, "Khu nhận" `RECEIVING`, "Khu xuất" `OUTBOUND`.

---

## B. Xếp kệ (Nhân viên kho)

### B1. `GET /api/parcels/pending-putaway?pageNumber=1&pageSize=20&search=&status=`

`{ "message", "data": { "items": [...], "totalCount", "pageNumber", "pageSize", "totalPages" } }`, mỗi kiện:
`id, packageCode, consignmentCode, customerName, actualWeight, length, width, height, chargeableWeight, packageStatus, checkInTime, orderItemId, productName, productType, quantity, packageConfiguration, services[]`.

Hiện kèm thùng gỗ (`packageConfiguration`) và dịch vụ (`services`) để kho biết kiện cần xử lý gì.

### B2. Gợi ý vị trí (tuỳ chọn)

`POST /api/ai/warehouse/putaway-suggestions`

```json
{ "parcelId": "guid", "warehouseId": "guid", "preferredZoneId": null }
```

```json
{
  "message": "Lấy danh sách gợi ý vị trí xếp kho thành công.",
  "data": {
    "suggestedLocations": [
      { "binId": "guid", "zoneId": "guid", "shelfId": "guid", "displayLabel": "BIN-A1-02", "confidenceScore": 0.85, "reason": "..." }
    ],
    "warnings": []
  }
}
```

- **Chỉ trả ô thuộc khu lưu kho đang dùng, còn đủ thể tích và tải trọng.** Ưu tiên ô cùng khách, hàng nặng tầng thấp, khu ưu tiên.
- `warnings` có câu "Kho chưa có ô kệ nào đang hoạt động thuộc khu lưu kho (STORAGE)" → báo quản lý kho khai loại khu (A2).
- Ô chỉ được gợi ý khi đã gắn vào sơ đồ layout của kho.

### B3. `PUT /api/parcels/{parcelId}/put-away` — xếp kệ

**Khuyên dùng — quét mã ô kệ:**

```json
{ "warehouseId": "guid", "binCode": "BIN-A1-02" }
```

Chọn theo ID (client cũ gửi đủ 4 ID vẫn chạy):

```json
{ "warehouseId": "guid", "binId": "guid", "zoneId": "guid", "shelfId": "guid" }
```

| Trường | Ghi chú |
|---|---|
| `warehouseId` | Bắt buộc, phải **đúng kho của phiếu nhập kho** |
| `binCode` | Mã ô quét được, tra trong kho `warehouseId`, không phân biệt hoa thường |
| `binId` | Dùng khi không quét mã. Gửi cả `binCode` và `binId` thì phải cùng một ô |
| `zoneId`, `shelfId` | Không bắt buộc. Gửi thì phải khớp khu/kệ của ô |

Server kiểm theo thứ tự:
1. Kiện đang `RECEIVED` (hoặc `RECEIVED_AT_DESTINATION`), chưa `STORED`.
2. Phiếu nhập kho cho phép xếp kệ (đã duyệt, không còn chênh lệch chờ xem).
3. Ô thuộc kho, ô đang dùng, khu đang dùng, **khu là `STORAGE`**.
4. Ô còn đủ thể tích và tải trọng (tính từ kiện đang nằm trong ô).

Response 200:

```json
{
  "message": "Nhập vị trí kho cho kiện hàng thành công.",
  "data": {
    "packageCode": "PCL-...",
    "warehouseName": "Kho Seoul (Hàn Quốc)",
    "zoneName": "Khu A",
    "zoneType": "STORAGE",
    "shelfCode": "SHELF-A1",
    "binCode": "BIN-A1-02",
    "packageStatus": "STORED",
    "putAwayTime": "2026-09-17T15:40:00Z"
  }
}
```

Sau khi xếp: kiện `STORED`, tạo tồn kho, ghi nhật ký nhập kho, đơn chuyển `WAREHOUSE_RECEIVED` (kho nguồn) hoặc `AT_DESTINATION_WAREHOUSE` (kho VN).

Chi tiết kiện `GET /api/parcels/{parcelId}` có `storageLocation: { warehouseName, zoneName, zoneType, shelfCode, binCode }`.

---

## C. Chuyển ô

`POST /api/inventories/{inventoryId}/relocate`

```json
{ "toBinCode": "BIN-A1-01", "reason": "Sắp xếp lại kệ" }
```

(hoặc `"toBinId": "guid"`)

- Lấy `inventoryId` từ `GET /api/inventories?warehouseId=` → `{ "items": [ { inventoryId, parcelId, binId, quantity, status, ... } ] }`.
- `reason` bắt buộc. Chỉ chuyển trong **cùng kho**; mã ô tra trong kho hiện tại của kiện.
- Ô đích phải là **ô khu lưu kho** đang dùng, còn đủ chỗ. Chuyển **từ** ô sai khu **về** khu lưu kho thì được.
- Tồn kho đang `PICKED` hoặc `RELEASED` không chuyển được (hàng đã soạn/xuất).
- **Kiện đang nằm trong một phiếu xuất kho còn hiệu lực không chuyển ô được** → 400 `Không điều chuyển được: kiện đang nằm trong phiếu xuất kho — PCL-... (phiếu WRO-...)`. Muốn đổi thì bỏ kiện khỏi phiếu trước ([`api-xuat-kho.md`](api-xuat-kho.md) mục E).

Response 200 `{ "message": "Điều chuyển vị trí kiện hàng thành công.", "data": <tồn kho> }`. Nhật ký: `GET /api/inventories/movements` (mục G).

**Thử:** `demo.kho.cn` — kiện của "Đơn 13" đang ở `BIN-A1-01`: chuyển sang `BIN-A1-02` (được) hoặc `BIN-OUT-01` (bị chặn).

---

## D. Kiện nằm sai khu

`GET /api/warehouses/{warehouseId}/misplaced-parcels`

```json
{
  "message": "Lấy danh sách kiện nằm sai khu thành công.",
  "data": [
    {
      "parcelId": "guid",
      "packageCode": "PCL-...",
      "packageStatus": "STORED",
      "consignmentCode": "VCL-...",
      "binId": "guid",
      "binCode": "BIN-OUT-01",
      "zoneId": "guid",
      "zoneName": "Khu xuất",
      "zoneType": "OUTBOUND",
      "reason": "Hàng đang nằm ở khu xuất"
    }
  ]
}
```

`reason` có thể là: "Hàng đang nằm ở khu nhận/khu xuất/khu cách ly", "Khu chưa phân loại", "Khu đã ngừng dùng", "Ô kệ đã ngừng dùng", "Ô kệ chưa gắn khu".

Cách xử lý (FE nên có màn riêng cho quản lý kho):
1. Lấy danh sách → với mỗi kiện, tìm tồn kho của kiện (`GET /api/inventories?warehouseId=`, lọc `parcelId`, lấy dòng **không** phải `RELEASED`/`PICKED`).
2. Chuyển về khu lưu kho qua mục C.
3. Kiện có tồn kho `PICKED` là đang trong phiếu xuất — để luồng xuất kho xử lý, không chuyển ô.

Hệ thống **không tự di chuyển** kiện.

> Trên production lúc deploy (17/09/2026) có 5 kiện `STORED` nằm ở ô `BIN-OUT-01` khu "Khu xuất" của Kho Seoul: 4 kiện chuyển được, 1 kiện đang `PICKED`.

---

## E. Tách / gộp kiện

Kiện đang nằm trong phiếu xuất kho còn hiệu lực **không tách, không gộp** → 400 kèm mã phiếu; bỏ kiện khỏi phiếu trước ([`api-xuat-kho.md`](api-xuat-kho.md) mục E).

### `POST /api/parcels/{parcelId}/split`

```json
{
  "children": [
    { "packageCode": "", "quantity": 2, "actualWeight": 0.6, "length": 20, "width": 15, "height": 10 },
    { "packageCode": "", "quantity": 1, "actualWeight": 0.4, "length": 15, "width": 15, "height": 10, "binId": "guid-o-khac" }
  ],
  "note": "Tách để xuất một phần"
}
```

- Ít nhất 2 kiện con; tổng `quantity` = số lượng tồn của kiện gốc; tổng cân không vượt kiện gốc.
- Kiện con không gửi `binId` → nằm lại ô của kiện gốc. Gửi `binId` → phải là **ô khu lưu kho cùng kho**, đủ chỗ.
- Kiện gốc **chưa lên kệ** thì kiện con không chọn ô được — tách xong xếp kệ từng kiện con (mục B).
- Kiện con giữ liên kết dòng hàng (`orderItemId`), thùng gỗ và dịch vụ của kiện gốc.

### `POST /api/parcels/merge`

```json
{ "sourceParcelIds": ["guid", "guid"], "packageCode": "", "actualWeight": 1.0, "length": 20, "width": 20, "height": 10, "binId": null }
```

- Các kiện cùng đơn, cùng kho, tồn kho đang `AVAILABLE`.
- `binId` bỏ trống → kiện gộp nằm ở ô của kiện đầu tiên. Ô (chọn hay mặc định) phải là **ô khu lưu kho**, đủ chỗ.

Response cả hai: `{ "message", "data": { "operation": "SPLIT" | "MERGE", "parent": {...}, "children": [ { "parcelId", "packageCode", ... } ] } }`.

**Thử:** `demo.kho.cn` — tách kiện "Áo vest" của "Đơn 13" (đang `STORED`).

---

## F. Luồng khác cũng theo luật khu

- **Xuất kho quốc tế:** bốc hàng chuyển kiện sang ô **khu `OUTBOUND`** — đây là ngoại lệ duy nhất của luật "hàng chỉ nằm ở khu lưu kho", và chỉ đi qua `POST /api/warehouse-release-requests/{id}/pick`. Kiện đã bốc theo phiếu **không** bị báo ở mục D; kiện nằm khu xuất mà không thuộc phiếu nào thì vẫn bị báo. Chi tiết: [`api-xuat-kho.md`](api-xuat-kho.md).
- **Nhận lại hàng hoàn:** `PUT /api/parcel-returns/{returnId}/receive` `{ "binId": "guid", "note": "" }` — có `binId` thì ô phải là ô khu lưu kho, đủ chỗ; bỏ trống `binId` thì kiện chưa lên kệ.
- **Duyệt phiếu nhập kho VN** (hàng về từ lô quốc tế): hệ thống tự xếp vào ô **khu lưu kho** đầu tiên còn chỗ của kho VN. Kho VN không có khu lưu kho còn chỗ → 400, khai thêm ô rồi duyệt lại.

---

## G. Tồn kho, nhật ký, kiểm kê

### Tồn kho — `GET /api/inventories?warehouseId=&customerId=&status=&regionCode=`

Đăng nhập (nhân viên). Response `{ "message", "items": [ ... ] }`, mỗi dòng:
`inventoryId, customerId, customerCode, customerName, customerPhone, orderId, orderItemId, consignmentCode, parcelId, packageCode, binId, binCode, warehouseId, warehouseName, warehouseRegion, quantity, status, createdAt, storedAt, actualWeight, actualVolume, storageDays, storageHours`.

`status`: `AVAILABLE` (trên kệ, khả dụng) · `RESERVED` (đang giữ cho phiếu xuất) · `PICKED` (đã soạn) · `RELEASED` (đã xuất khỏi kho). Màn tồn kho thường lọc bỏ `RELEASED`.

### Nhật ký di chuyển — `GET /api/inventories/movements?warehouseId=&parcelId=&limit=50`

`{ "message", "items": [ { id, inventoryId, parcelId, packageCode, fromBinId, fromBinCode, toBinId, toBinCode, type, reason, quantity, warehouseId, staffName, movedAt } ] }` — `type`: `INBOUND` (xếp kệ) · `RELOCATION` (chuyển ô) · `OUTBOUND` (xuất kho).

### Kiểm kê ô kệ

| API | Role | Ghi chú |
|---|---|---|
| `POST /api/inventories/cycle-counts` | Admin, WarehouseStaff | `{ "binId", "actualCount", "note" }` — số kiện hệ thống ghi nhận do server tự đếm. 201 `{ message, data }` |
| `GET /api/inventories/cycle-counts?warehouseId=&binId=&status=&limit=` | Nhân viên | `{ message, items }` |
| `PUT /api/inventories/cycle-counts/{cycleCountId}/resolve` | Admin, WarehouseStaff, OperationsManager | `{ "resolutionNote" }` (bắt buộc) — chỉ phiếu `DISCREPANCY`, chuyển `RESOLVED` |

Mỗi phiếu kiểm kê: `id, warehouseId, warehouseName, binId, binCode, expectedCount, actualCount, difference, status (MATCHED / DISCREPANCY / RESOLVED), note, staffName, countedAt, resolvedByName, ...`. Ô đang ngừng dùng không kiểm kê được.

### Báo cáo lưu kho

- `GET /api/inventories/long-stay-alerts?warehouseId=&customerId=&minDays=7&criticalDays=30&limit=100` → `{ message, items }` — dòng tồn kho nằm lâu, thêm `severity`.
- `GET /api/inventories/storage-report?warehouseId=&customerId=` → `{ message, items: [ { customerId, customerCode, customerName, customerPhone, warehouseRegion, parcelCount, totalQuantity, ... binCodes } ] }` — tồn kho gom theo khách.

**Thử:** `demo.kho.cn` — `GET /api/inventories?warehouseId=5F8A0B1C-2D3E-4F5A-6B7C-8D9E0F1A2B3C` có kiện "Đơn 13" ở `BIN-A1-01`; kiểm kê `BIN-A1-01`.

---

## Màn hình FE cần có / sửa

| Màn | Việc |
|---|---|
| Quản lý khu (Admin/OM) | Danh sách khu (A1), sửa loại/trạng thái (A2), tô màu `acceptsStorage` |
| Khai báo ô kệ | Thêm chọn `zoneType` khi nhập tên khu mới; hiện `zoneType` trong danh sách ô |
| Xếp kệ (app kho) | Quét mã ô → gửi `binCode`; hiện gợi ý (B2); hiện thùng gỗ + dịch vụ của kiện |
| Chuyển ô | Quét mã ô đích → `toBinCode` |
| Kiện nằm sai khu (OM) | Báo cáo (D) + nút chuyển về khu lưu kho |
| Tách/gộp kiện | Ô chọn cho kiện con/kiện gộp chỉ lấy từ ô khu lưu kho |

## Thay đổi so với trước (FE cần biết)

- Xếp kệ vào ô khu nhận / khu xuất / khu chưa phân loại **trước đây được, giờ bị chặn (400)**.
- `PUT /api/parcels/{id}/put-away`: thêm `binCode`; `zoneId`, `shelfId`, `binId` thành tuỳ chọn.
- `POST /api/inventories/{id}/relocate`: thêm `toBinCode`; `toBinId` thành tuỳ chọn. Ô đích phải thuộc khu lưu kho.
- `POST /api/warehouses/{id}/locations`: tạo khu mới bắt buộc `zoneType`.
- Tách kiện đã lên kệ trước đây lỗi hệ thống (500) — đã sửa.
- API mới: `GET /api/warehouses/{id}/zones`, `PUT /api/warehouse-zones/{zoneId}`, `GET /api/warehouses/{id}/misplaced-parcels`.
- Các khu có sẵn đã được khai loại theo tên: "Khu nhận"/"Receiving" → `RECEIVING`; "Khu xuất"/"Xuất hàng" → `OUTBOUND`; "Khu A"/"Khu Vực A"/"Khu Vực A - Updated" → `STORAGE`.

## Lỗi thường gặp

| Mã | Message (ví dụ) | Xử lý |
|---|---|---|
| 400 | Ô kệ BIN-OUT-01 thuộc khu xuất "Khu xuất". Hàng lưu kho chỉ được xếp vào khu loại STORAGE. | Quét ô khu lưu kho khác / dùng gợi ý |
| 400 | ... khu chưa phân loại ... nhờ quản lý kho khai loại cho khu này. | OM khai loại khu (A2) |
| 400 | Khu ... đang ngừng sử dụng. / Ô kệ ... đang ngừng sử dụng. | Chọn ô khác |
| 400 | Ô kệ ... không đủ sức chứa ... / không đủ tải trọng ... | Chọn ô khác, hoặc kiểm tra đơn vị `maxVolume` (cm³) |
| 400 | Ô kệ ... không thuộc kho ... / không thuộc khu vực kho đã chọn. | Kiểm tra kho đang thao tác |
| 404 | Không tìm thấy ô kệ mã ... trong kho đã chọn. | Quét lại mã |
| 400 | Kiện ... được duyệt nhận tại kho khác với kho đang xếp kệ. | Xếp đúng kho của phiếu |
| 400 | Phiếu tiếp nhận ... đang chờ quản lý kho duyệt ... / có chênh lệch ... | Chờ quản lý kho |
| 400 | Khu ... đang chứa N kiện nên không đổi loại hoặc ngừng dùng được. | Chuyển hết hàng ra trước |
| 400 | Không thể điều chuyển hàng tồn ở trạng thái PICKED/RELEASED. | Hàng đang/đã xuất, không chuyển ô |
| 403 | — | Sai role |
