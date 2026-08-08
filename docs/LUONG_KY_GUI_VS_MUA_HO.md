# Ký gửi vs Mua hộ — luồng đi, endpoint, và những chỗ hai luồng khác nhau

Tài liệu này dựng từ một lần chạy thật đầu-cuối cả hai luồng (08/08/2026), ghi lại đúng
endpoint đã gọi, payload đã gửi và response nhận về. Mục đích: FE biết gắn cái gì, BE biết
chỗ nào hai luồng lệch nhau để không sửa bên này làm hỏng bên kia.

## 1. Mô hình: hai đầu vào, một kho

```
KÝ GỬI:  Khách gửi hàng có sẵn  ──> ORDERS (order_type=CONSIGNMENT) ──┐
                                                                      ├──> KHO: check-in → put-away → tồn kho → WRO → về VN
MUA HỘ:  Khách nhờ mua hộ ──> PURCHASE_REQUESTS ──(duyệt nhập kho)──> ORDERS (order_type=PURCHASE) ──┘
```

Điểm mấu chốt: **mua hộ có thêm một vòng đời riêng ở trước**. Đơn mua hộ sống trong bảng
`PURCHASE_REQUESTS` suốt quá trình đặt hàng; chỉ khi Ops duyệt nhập kho nó mới sinh ra một
bản ghi `ORDERS` để đi tiếp bằng đúng luồng kho của ký gửi. Ký gửi thì có `ORDERS` ngay từ đầu.

Vì hai luồng dùng chung bảng `ORDERS` và chung toàn bộ bước kho, nên **mọi API kho đều trả về
cả hai loại đơn**. Muốn tách thì lọc bằng `orderType`.

## 2. Quy ước mã — đọc kỹ phần này

| Field | Ký gửi | Mua hộ | Ý nghĩa |
|---|---|---|---|
| `orderCode` | `VCL-…` | `PUR-…` | **Mã đơn kho. Dùng field này cho mọi trường hợp.** |
| `consignmentCode` | `VCL-…` | `null` | Mã ký gửi. Đơn mua hộ không có mã ký gửi. |
| `purchaseCode` | `null` | `PUR-…` | Mã yêu cầu mua hộ. |

Quy tắc cho FE: **luôn hiển thị `orderCode`**, khỏi if/else theo loại đơn. Chỉ đụng tới
`consignmentCode` / `purchaseCode` khi thật sự cần phân biệt nguồn gốc đơn.

Cột DB `ORDERS.consignment_code` giữ mã cho **cả hai** loại (tên cột có từ thời hệ thống mới
chỉ chạy ký gửi). Đừng suy ra ý nghĩa từ tên cột đó.

## 3. Vai trò — ai làm được gì

Khai báo tập trung tại `VCL_BLL/Helpers/StaffRoleHelper.cs`. Bốn tập tách riêng theo ý định:

| Tập | Gồm | Dùng cho |
|---|---|---|
| `IsInternalStaff` | Admin, Sale, Staff, **WarehouseStaff**, OperationsManager, Delivery | Xem dữ liệu của mọi khách (thay vì chỉ đơn của mình) |
| `CanUpdatePurchaseProgress` | Admin, Sale, Staff, OperationsManager | Đẩy tiến độ mua hàng |
| `CanApproveWarehouseStore` | Admin, OperationsManager, Manager | Duyệt kiểm kê nhập kho (Sale **không** có) |
| `CanHandlePayments` | Admin, Sale, Staff, OperationsManager | Thao tác/xem thanh toán (kho **không** có) |

Tách như vậy vì trước đây một mảng vai trò bị dùng lẫn cho cả "ai xem được" lẫn "ai thao tác
được" — thêm vai trò để sửa lỗi hiển thị là vô tình cấp luôn quyền thao tác.

## 4. Luồng A — KÝ GỬI

### A1. Dropdown (gọi trước khi mở form)

| Endpoint | Quyền | Trả về |
|---|---|---|
| `GET /api/orders/consignments/routes` | công khai | `["Trung quốc --> Việt Nam", …]` |
| `GET /api/orders/consignments/shipping-options?route=CN-VN` | công khai | `["Express", "Standard"]` |
| `GET /api/package-configurations` | đăng nhập | mảng thùng: `id`, `configCode`, `configName`, `packageFee` |
| `GET /api/warehouses/active` | đăng nhập | `{ items: [...] }` — **không** dùng `/api/warehouses` (chỉ Admin, Sale gọi bị 403) |

`route` nhận **cả mã lẫn tên hiển thị**: gửi `"CN-VN"` hay `"Trung quốc --> Việt Nam"` đều được.

### A2. Khách tạo yêu cầu — `POST /api/orders/consignments` (role `Customer`)

```json
{
  "route": "CN-VN",
  "shippingOption": "STANDARD",
  "receiverName": "…", "receiverPhone": "…", "receiverAddress": "…",
  "requiresWoodenCrate": true,
  "items": [{
    "productName": "…", "productType": "Quần áo", "quantity": 1,
    "weight": 2, "length": 30, "width": 20, "height": 15,
    "declaredValue": 400000,
    "packageConfigurationId": "99999999-1111-1111-1111-111111111111"
  }]
}
```

→ `201`, trả `orderId`, `consignmentCode` (**mã `VCL-` sinh ngay tại bước này**), `status: "PENDING_REVIEW"`,
và một `quotation` báo giá tạm tính kèm theo.

**Bẫy:** `requiresWoodenCrate: true` và `packageConfigurationId` ở **từng item** là bắt buộc trên
web khách. Thiếu một trong hai → `400 "Đóng thùng gỗ là dịch vụ bắt buộc…"`. Luồng Sale tạo hộ
(`POST /api/staff/consignments`) không bị ràng buộc này.

### A3. Sale duyệt — `PUT /api/orders/consignments/{orderId}/status`

```json
{ "status": "APPROVED" }
```

→ trả `status`, `consignmentCode`, `receiptPdfUrl`. Đây là mốc **phát hành phiếu PDF** và là điều
kiện để kho được check-in. Sinh phiếu lỗi thì `receiptPdfUrl: null` nhưng đơn vẫn `APPROVED`.

## 5. Luồng B — MUA HỘ

### B1. Khách tạo yêu cầu — `POST /api/purchase-requests` (role `Customer`)

```json
{
  "route": "CN-VN",
  "shippingOption": "STANDARD",
  "receiverName": "…", "receiverPhone": "…", "receiverAddress": "…",
  "items": [{
    "productLink": "https://…",
    "productType": "Quần áo",
    "productName": "…",
    "quantity": 1
  }]
}
```

→ trả `purchaseRequestId`, `purchaseCode` (`PUR-…`), `status: "PENDING_REVIEW"`.

**Khác ký gửi:** item là **link sản phẩm**, không có cân nặng/kích thước (chưa ai cầm hàng),
và **không cần** `packageConfigurationId` / `requiresWoodenCrate`.

### B2. Đẩy tiến độ — `PUT /api/purchase-requests/{id}/confirm-purchase`

```json
{ "status": "PAID", "warehouseId": "5f8a0b1c-…" }
```

Thứ tự trạng thái:

```
PENDING_REVIEW → DEPOSIT_PAID → PAID → PURCHASED → SELLER_SHIPPED
   → ARRIVED_ORIGIN_WAREHOUSE → WAITING_STORED → STORED
```

Sale/Staff đẩy được tới `WAITING_STORED`. Bước `STORED` chỉ Ops/Admin (Sale gọi → `403`).

### B3. Nhập kho — cùng endpoint, `status: "STORED"` (role `OperationsManager`)

Chỉ đi được từ `WAITING_STORED` hoặc `ARRIVED_ORIGIN_WAREHOUSE`, nhảy cóc từ đầu luồng → `400`.

Response:

```json
{
  "purchaseCode": "PUR-20260808214719-581890",
  "status": "STORED",
  "statusDisplayName": "Đã nhập kho",
  "orderCode": "PUR-20260808214719-581890",
  "receiptPdfUrl": "https://api-vcl.zushin.io.vn/api/public/receipts/…",
  "warehouseId": "…", "warehouseName": "Kho Quảng Châu (Trung Quốc)"
}
```

Đây là bước **sinh `ORDERS` + `ORDER_ITEMS` + phiếu PDF**. Trước bước này kho không có gì để check-in.
`POST /api/purchase-requests/{id}/approve-store` làm đúng việc tương đương; dùng cái nào cũng được.

## 6. Điểm hợp nhất — kho check-in

Từ đây trở đi **hai luồng dùng chung endpoint, chung payload, chung response**.

`POST /api/parcels/check-in` (role `WarehouseStaff`):

```json
{
  "shippingCode": "PUR-20260808214719-581890",
  "packages": [{ "actualWeight": 1, "length": 20, "width": 15, "height": 10 }]
}
```

**Bẫy tên field:** là `packages[].actualWeight`, **không phải** `parcels[].weight`.
Gửi sai → `400 "Cân nặng thực tế phải lớn hơn 0."` (thông báo này dễ gây hiểu nhầm là thiếu số liệu).

`shippingCode` nhận **cả `VCL-` lẫn `PUR-`**. Response phân biệt loại đơn giúp FE:

| | Ký gửi | Mua hộ |
|---|---|---|
| `orderCode` | `VCL-…` | `PUR-…` |
| `consignmentCode` | `VCL-…` | `null` |
| `purchaseCode` | `null` | `PUR-…` |
| `orderType` | `CONSIGNMENT` | `PURCHASE` |

## 7. Danh sách đơn — chỗ FE hay gắn sai

`GET /api/orders/consignments` trả về **cả hai loại đơn**, vì màn kho cần thấy đủ.

```
GET /api/orders/consignments?orderType=CONSIGNMENT   → chỉ ký gửi (dùng cho màn ký gửi)
GET /api/orders/consignments?orderType=PURCHASE      → chỉ mua hộ
GET /api/orders/consignments                          → cả hai (dùng cho màn kho)
```

Mỗi dòng có field `orderType` để lọc phía client nếu cần. **Không truyền `orderType` ở màn ký gửi
là nguyên nhân của hiện tượng "màn ký gửi hiện mã PUR-"** — mã không hề sinh sai, chỉ là danh sách lẫn loại.

## 8. Hai bộ từ vựng trạng thái — không phải lỗi, nhưng phải biết

Cùng một lô hàng mua hộ, hai API trả hai trạng thái khác nhau:

| API | Trạng thái | Nghĩa |
|---|---|---|
| `GET /api/purchase-requests/{id}` | `STORED` | Vòng đời **mua hàng** đã xong |
| `GET /api/orders/consignments?searchCode=PUR-…` | `APPROVED` | Vòng đời **kho** vừa bắt đầu |

Đây là hai giai đoạn nối tiếp nhau, không mâu thuẫn. `ORDERS.status = APPROVED` chính là điều kiện
để kho được check-in; sau khi check-in nó chuyển `CHECKED_IN` như đơn ký gửi.

Quy tắc hiển thị cho FE:
- Màn **mua hộ** → dùng `status` + `statusDisplayName` của `PURCHASE_REQUESTS`.
- Màn **kho** → dùng `status` của `ORDERS`.
- Đừng trộn hai bộ này vào cùng một cột.

## 9. Bảng response envelope — không đồng nhất

Ba dạng khác nhau, đây là nguồn lỗi parse phổ biến:

| Dạng | Endpoint ví dụ |
|---|---|
| `{ message, data }` | phần lớn: tạo đơn, chi tiết, confirm-purchase |
| `{ items, totalCount, pageNumber, pageSize }` | `GET /api/warehouses/active` |
| mảng trần `[...]` | `GET /api/package-configurations` |

Danh sách phân trang trong `data` lại lồng thêm một tầng: `data.items`.

## 10. Những lỗi đã sửa trong đợt này

| # | Triệu chứng | Nguyên nhân |
|---|---|---|
| 1 | `confirm-purchase` với `STORED` trả `orderCode`/`receiptPdfUrl` = null, đơn kẹt vĩnh viễn | Chỉ `approve-store` mới sinh đơn kho; đường `confirm-purchase` chỉ đổi cột trạng thái |
| 2 | Field tên `consignmentCode` trong response mua hộ | Lộ tên cột DB cũ ra API — đã đổi thành `orderCode` |
| 3 | Màn ký gửi hiện đơn mã `PUR-` | `GET /api/orders/consignments` không lọc theo loại đơn, DTO cũng không có `orderType` |
| 4 | Kho gọi `GET /api/purchase-requests` ra danh sách rỗng, xem chi tiết bị `403` | `WarehouseStaff` thiếu trong mảng vai trò của service mua hộ, dù có trong service ký gửi |
| 5 | Sửa vai trò chỗ này lại nới quyền chỗ khác | Một mảng vai trò dùng lẫn cho cả "ai xem được" và "ai thao tác được" |
