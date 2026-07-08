# VCL Backend - Tài liệu tích hợp API cho Frontend

> Hệ thống: Vietnam Cross-Border Logistics System (VCL)
>
> Cập nhật: 18/06/2026 — Thêm API Admin quản lý danh mục hàng cấm/hạn chế (Restricted Items)
>
> Module hiện có: Authentication, User Management, Quotation, Order, Restricted Items

## 1. Thông tin chung

### Base URL Development

| Profile | URL |
|---|---|
| HTTP | `http://localhost:5209` |
| HTTPS | `https://localhost:7237` |

Swagger UI: `{BASE_URL}/swagger`

Tất cả request/response JSON sử dụng `camelCase`.

### Headers

```http
Content-Type: application/json
Authorization: Bearer {accessToken}
```

Header `Authorization` chỉ bắt buộc với API có ghi chú `Admin`.

## 2. JWT Authentication

Sau khi đăng nhập, FE lưu `token` và gắn vào các request được bảo vệ:

```typescript
config.headers.Authorization = `Bearer ${token}`;
```

JWT hiện có các claim:

| Claim | Nội dung |
|---|---|
| `nameid` | User ID |
| `name` | Họ tên |
| `email` | Email |
| `role` | Vai trò người dùng |

Token mặc định có hiệu lực 240 phút. Hệ thống chưa có refresh token.

| Status | Ý nghĩa |
|---|---|
| `401 Unauthorized` | Thiếu token, token sai/hết hạn hoặc đăng nhập thất bại |
| `403 Forbidden` | Token hợp lệ nhưng không có role được yêu cầu |

Trong Swagger, hộp **Authorize** chỉ cần nhập token, không nhập từ `Bearer`.

## 3. Tổng hợp endpoint

| Method | Endpoint | Quyền | Mô tả |
|---|---|---|---|
| `POST` | `/api/Auth/customer/register` | Public | Khởi tạo đăng ký khách hàng và gửi OTP |
| `POST` | `/api/Auth/customer/verify-otp` | Public | Xác thực OTP và kích hoạt tài khoản |
| `POST` | `/api/Auth/customer/resend-otp` | Public | Gửi lại OTP |
| `POST` | `/api/Auth/login` | Public | Đăng nhập và nhận JWT |
| `POST` | `/api/Auth/forgot-password` | Public | Gửi OTP 6 số qua email (hết hạn 5 phút) |
| `POST` | `/api/Auth/reset-password` | Public | Xác thực OTP và đặt lại mật khẩu |
| `POST` | `/api/Auth/admin/register` | Admin | Admin tạo tài khoản nhân viên |
| `PUT` | `/api/User/{id}/lock` | Admin | Khóa tài khoản |
| `PUT` | `/api/User/{id}/unlock` | Admin | Mở khóa tài khoản |
| `GET` | `/api/User/profile` | Authenticated | Lấy thông tin cá nhân của người dùng đang đăng nhập |
| `PUT` | `/api/User/profile` | Authenticated | Cập nhật thông tin cá nhân của người dùng đang đăng nhập |
| `POST` | `/api/orders/{orderId}/quotation/estimate` | Authenticated | Tạo báo giá tạm tính cho đơn ký gửi |
| `PUT` | `/api/quotations/{quotationId}/accept` | Authenticated | Khách hàng xác nhận báo giá tạm tính |
| `PUT` | `/api/orders/consignments/{orderId}/status` | Staff | Staff cập nhật trạng thái yêu cầu ký gửi |
| `GET` | `/api/orders/consignments` | Authenticated | Lấy danh sách yêu cầu ký gửi |
| `GET` | `/api/orders/consignments/{orderId}` | Authenticated | Xem chi tiết một yêu cầu ký gửi |
| `PUT` | `/api/orders/consignments/{orderId}/cancel` | Authenticated | Customer hủy yêu cầu ký gửi |
| `POST` | `/api/staff/consignments` | Sales | Sales tạo yêu cầu ký gửi thay cho Customer |
| `GET` | `/api/restricted-items` | Admin | Xem danh sách mặt hàng cấm/hạn chế |
| `GET` | `/api/restricted-items/{id}` | Admin | Lấy chi tiết mặt hàng cấm/hạn chế |
| `POST` | `/api/restricted-items` | Admin | Thêm mặt hàng cấm/hạn chế mới |
| `PUT` | `/api/restricted-items/{id}` | Admin | Cập nhật thông tin/vô hiệu hóa mặt hàng |
| `DELETE` | `/api/restricted-items/{id}` | Admin | Xóa mặt hàng cấm/hạn chế |
| `GET` | `/api/pricing-rules` | Public | Xem danh sách cấu hình bảng giá |
| `GET` | `/api/pricing-rules/{id}` | Public | Lấy chi tiết cấu hình giá theo ID |
| `POST` | `/api/pricing-rules` | Admin | Thêm cấu hình giá mới |
| `PUT` | `/api/pricing-rules/{id}` | Admin | Cập nhật cấu hình giá |
| `DELETE` | `/api/pricing-rules/{id}` | Admin | Xóa cấu hình giá |
| `GET` | `/api/service-pricings` | Public | Xem danh sách cấu hình bảng giá dịch vụ chính |
| `GET` | `/api/service-pricings/{id}` | Public | Lấy chi tiết bảng giá dịch vụ theo ID |
| `POST` | `/api/service-pricings` | Admin | Thêm cấu hình bảng giá dịch vụ mới |
| `PUT` | `/api/service-pricings/{id}` | Admin | Cập nhật cấu hình bảng giá dịch vụ |
| `DELETE` | `/api/service-pricings/{id}` | Admin | Xóa cấu hình bảng giá dịch vụ |
| `POST` | `/api/parcels/check-in` | Staff/WarehouseStaff | Check-in kiện hàng thực tế bằng mã gửi hàng |
| `GET` | `/api/parcels/pending-putaway` | Staff/WarehouseStaff | Lấy danh sách kiện chờ nhập vị trí kho |
| `PUT` | `/api/parcels/{parcelId}/put-away` | Staff/WarehouseStaff | Nhập kiện vào vị trí kho (Put-Away) |
| `POST` | `/api/inventories/from-parcel/{parcelId}` | Staff/WarehouseStaff | Ghi nhận tồn kho khả dụng sau khi put-away |
| `POST` | `/api/warehouse-release-requests` | Authenticated | Tạo yêu cầu xuất kho từ tồn kho |
| `GET` | `/api/warehouse-release-requests/{requestId}` | Authenticated | Xem chi tiết một yêu cầu xuất kho |
| `PUT` | `/api/warehouse-release-requests/{requestId}/status` | Staff | Staff cập nhật trạng thái yêu cầu xuất kho (duyệt/từ chối) |
| `POST` | `/api/warehouse-release-requests/{requestId}/picking-list` | Staff | Tạo phiếu picking cho yêu cầu xuất kho |
| `PUT` | `/api/picking-lists/{pickingListId}/confirm` | WarehouseStaff | Xác nhận picking hàng khỏi vị trí kho |
| `PUT` | `/api/warehouse-release-requests/{requestId}/complete` | WarehouseStaff | Xác nhận đóng gói và hoàn tất xuất kho |
| `POST` | `/api/consolidation` | Staff/WarehouseStaff/Admin/OperationsManager | Tạo mới một lô gom hàng (Consolidation) từ danh sách Order IDs |
| `GET` | `/api/consolidation` | Staff/WarehouseStaff/Admin/OperationsManager | Lấy danh sách các lô gom hàng (MasterBox) |
| `GET` | `/api/consolidation/{id}` | Staff/WarehouseStaff/Admin/OperationsManager | Lấy chi tiết thông tin lô gom hàng theo ID |
| `PUT` | `/api/consolidation/{id}` | Staff/WarehouseStaff/Admin/OperationsManager | Cập nhật thông tin/danh sách đơn hàng gom |
| `DELETE` | `/api/consolidation/{id}` | Staff/WarehouseStaff/Admin/OperationsManager | Hủy bỏ lô gom hàng (giải tán các kiện hàng trở lại trạng thái tự do) |
| `GET` | `/api/warehouses` | Admin | Lấy danh sách tất cả các kho vận hành |
| `POST` | `/api/warehouses` | Admin | Tạo mới một kho vận hành |
| `PUT` | `/api/warehouses/{id}` | Admin | Cập nhật thông tin kho vận hành |
| `DELETE` | `/api/warehouses/{id}` | Admin | Xóa kho vận hành |
| `GET` | `/api/warehouses/active` | Authenticated | Lấy danh sách các kho đang hoạt động |
| `GET` | `/api/warehouses/{warehouseId}/locations` | Admin | Lấy danh sách vị trí lưu trữ trong kho |
| `POST` | `/api/warehouses/{warehouseId}/locations` | Admin | Tạo mới vị trí lưu trữ trong kho |
| `PUT` | `/api/warehouse-locations/{locationId}` | Admin | Cập nhật thông tin vị trí lưu trữ |
| `DELETE` | `/api/warehouse-locations/{locationId}` | Admin | Xóa vị trí lưu trữ |
| `GET` | `/api/warehouses/{warehouseId}/locations/active` | Authenticated | Lấy danh sách vị trí lưu trữ đang hoạt động |
| `POST` | `/api/purchase-requests` | Customer | Customer tạo yêu cầu mua hộ hàng Trung Quốc |
| `GET` | `/api/purchase-requests` | Customer / Staff / Sales | Lấy danh sách yêu cầu mua hộ (phân trang, tìm kiếm, lọc trạng thái) |
| `GET` | `/api/purchase-requests/{requestId}` | Customer / Staff / Sales | Lấy chi tiết một yêu cầu mua hộ |
| `PUT` | `/api/purchase-requests/{requestId}/status` | Staff / Sales | Cập nhật trạng thái yêu cầu mua hộ (tiếp nhận/bổ sung/từ chối) |
| `POST` | `/api/purchase-requests/{requestId}/quotation` | Staff / Sales | Tạo báo giá cho yêu cầu mua hộ |
| `GET` | `/api/orders/consignments/routes` | Public | Danh sách tuyến hàng (route) cho dropdown FE |
| `GET` | `/api/orders/consignments/shipping-options` | Public | Danh sách phương án vận chuyển cho dropdown FE (lọc theo `?route=`) |
| `GET` | `/api/orders/consignments/statuses` | Public | Danh sách toàn bộ trạng thái yêu cầu ký gửi (code + label) |
| `GET` | `/api/product-types` | Public | Danh sách loại hàng (productType) cho dropdown FE |
| `POST` | `/api/international-shipments` | Staff / Operations | Tạo lô vận chuyển quốc tế từ các yêu cầu xuất kho đã RELEASED |
| `PUT` | `/api/international-shipments/{shipmentId}/status` | Staff / Operations | Cập nhật trạng thái lô vận chuyển quốc tế (MANIFESTED → IN_TRANSIT → ARRIVED_DESTINATION) |
| `PUT` | `/api/international-shipments/{shipmentId}/arrive` | Staff / Operations | Xác nhận lô đến kho đích (cập nhật lô + kiện sang ARRIVED_DESTINATION) |
| `PUT` | `/api/international-shipments/{shipmentId}/destination-receiving` | WarehouseStaff / Staff / Operations | Xác nhận nhận kiện tại kho đích (kiện sang RECEIVED_AT_DESTINATION) |
| `GET` | `/api/customer/parcels` | Customer | Customer tra cứu kiện hàng thuộc yêu cầu ký gửi của mình |
| `POST` | `/api/warehouse-receiving-notes` | Sale / Admin | Sales tạo phiếu tiếp nhận kho từ yêu cầu ký gửi đã APPROVED |
| `GET` | `/api/warehouse-receiving-notes/by-consignment/{orderId}` | Sale / Admin | Lấy phiếu tiếp nhận kho đang hiệu lực theo đơn ký gửi |
| `GET` | `/api/warehouse-receiving-notes/scan?code={code}` | WarehouseStaff / Admin | Warehouse Staff scan mã phiếu để nhận thông tin hàng dự kiến |
| `GET` | `/api/carriers` | Admin | Lấy danh sách tất cả các đơn vị vận chuyển |
| `GET` | `/api/carriers/active` | Authenticated | Lấy danh sách các đơn vị vận chuyển đang hoạt động |
| `POST` | `/api/carriers` | Admin | Tạo mới một đơn vị vận chuyển |
| `PUT` | `/api/carriers/{carrierId}` | Admin | Cập nhật thông tin đơn vị vận chuyển |
| `DELETE` | `/api/carriers/{carrierId}` | Admin | Xóa đơn vị vận chuyển |


## 4. Đăng ký khách hàng

### 4.1. Gửi thông tin đăng ký

```http
POST /api/Auth/customer/register
```

```json
{
  "fullName": "Nguyễn Văn A",
  "email": "customer@example.com",
  "password": "Password123",
  "phone": "0901234567",
  "country": "Vietnam",
  "address": "Ho Chi Minh City"
}
```

| Field | Ràng buộc |
|---|---|
| `fullName` | Bắt buộc, tối đa 255 ký tự |
| `email` | Bắt buộc, đúng định dạng email, tối đa 255 ký tự |
| `password` | Bắt buộc, từ 8 đến 100 ký tự |
| `phone` | Bắt buộc, đúng định dạng số điện thoại, tối đa 50 ký tự |
| `country` | Bắt buộc, tối đa 100 ký tự |
| `address` | Bắt buộc |

Response `200 OK`:

```json
{
  "message": "OTP da duoc gui den email cua ban."
}
```

Sau request này:

- `User` và `Customer` được tạo với status `PendingVerification`.
- OTP gồm 6 chữ số, có hiệu lực 5 phút.
- Không được yêu cầu gửi OTP mới trong vòng 60 giây.
- Tài khoản chưa thể đăng nhập trước khi xác thực OTP.

Response thường gặp:

| Status | Trường hợp |
|---|---|
| `400` | Gửi OTP lại quá sớm hoặc dữ liệu validation không hợp lệ |
| `409` | Email đã được sử dụng |

### 4.2. Xác thực OTP

```http
POST /api/Auth/customer/verify-otp
```

```json
{
  "email": "customer@example.com",
  "otp": "123456"
}
```

`otp` bắt buộc gồm đúng 6 chữ số.

Response `200 OK`:

```json
{
  "message": "Dang ky tai khoan thanh cong.",
  "customerId": "3fa85f64-5717-4562-b3fc-2c963f66afa6"
}
```

Khi thành công, status của `User` và `Customer` được chuyển thành `Active`.

Response `400 Bad Request` khi OTP sai, hết hạn, không tồn tại hoặc đã nhập sai quá 5 lần.

### 4.3. Gửi lại OTP

```http
POST /api/Auth/customer/resend-otp
```

```json
{
  "email": "customer@example.com"
}
```

Response `200 OK`:

```json
{
  "message": "OTP moi da duoc gui den email cua ban."
}
```

Chỉ được gửi lại sau 60 giây. OTP cũ hết hiệu lực khi OTP mới được tạo.

## 5. Đăng nhập

```http
POST /api/Auth/login
```

```json
{
  "email": "customer@example.com",
  "password": "Password123"
}
```

Response `200 OK`:

```json
{
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "expiresAt": "2026-06-12T04:00:00Z",
  "userId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "fullName": "Nguyễn Văn A",
  "role": "Customer"
}
```

Response `401 Unauthorized`:

```json
{
  "message": "Invalid credentials"
}
```

API không cho đăng nhập nếu tài khoản bị khóa, chưa `Active`, hoặc Customer chưa xác thực email.

## 6. Quản lý mật khẩu

### 6.1. Quên mật khẩu

```http
POST /api/Auth/forgot-password
```

```json
{
  "email": "customer@example.com"
}
```

Response `200 OK`:

```json
{
  "message": "Email đặt lại mật khẩu đã được gửi. Vui lòng kiểm tra hộp thư của bạn."
}
```

Backend sinh OTP 6 chữ số và gửi qua email. OTP có hiệu lực **5 phút**.

Lưu ý:

- FE nên disable nút "Gửi lại" trong **60 giây** sau mỗi lần gửi.
- OTP mới tạo sẽ vô hiệu hóa OTP cũ.

Response `400 Bad Request`:

| Trường hợp | Message |
|---|---|
| Email không tồn tại | `"Email không tồn tại trong hệ thống."` |
| Gửi lại quá sớm (< 60s) | `"Vui lòng chờ 60 giây trước khi gửi lại mã OTP."` |

### 6.2. Đặt lại mật khẩu

```http
POST /api/Auth/reset-password
```

```json
{
  "email": "customer@example.com",
  "otp": "847291",
  "newPassword": "NewPassword123"
}
```

| Field | Ràng buộc |
|---|---|
| `email` | Bắt buộc, đúng định dạng email |
| `otp` | Bắt buộc, đúng 6 chữ số |
| `newPassword` | Bắt buộc |

Response `200 OK`:

```json
{
  "message": "Mật khẩu đã được đặt lại thành công."
}
```

Response `400 Bad Request`:

| Trường hợp | Message |
|---|---|
| Email không tồn tại | `"Email không tồn tại trong hệ thống."` |
| OTP hết hạn | `"Mã OTP đã hết hạn. Vui lòng yêu cầu gửi lại."` |
| OTP không chính xác | `"Mã OTP không chính xác."` |
| Nhập sai quá 5 lần | `"Bạn đã nhập sai OTP quá số lần cho phép. Vui lòng yêu cầu gửi lại mã mới."` |

Khi nhập sai OTP, hệ thống đếm số lần thất bại. Sau **5 lần sai**, OTP bị khóa và phải yêu cầu gửi lại từ đầu.

## 7. Admin tạo tài khoản nhân viên

```http
POST /api/Auth/admin/register
Authorization: Bearer {adminToken}
```

Yêu cầu JWT có role chính xác là `Admin`.

```json
{
  "fullName": "SALE 01",
  "email": "sale01@employee.com",
  "password": "Employee123",
  "phone": "0999999999",
  "role": "Sale"
}
```

Response `201 Created`:

```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6"
}
```

Tài khoản được tạo với:

- `userType = "Employee"`
- `status = "Active"`
- `role` lấy từ request

Response:

| Status | Trường hợp |
|---|---|
| `401` | Thiếu/sai/hết hạn token |
| `403` | Token không có role `Admin` |
| `409` | Email đã được sử dụng |

Lưu ý: backend hiện chưa giới hạn danh sách role nhân viên. FE nên dùng đúng role do nghiệp vụ thống nhất, ví dụ `Sale`, `WarehouseStaff`, `OperationsManager`, `Delivery`.

## 8. Khóa và mở khóa tài khoản

### 8.1. Khóa tài khoản

```http
PUT /api/User/{id}/lock
Authorization: Bearer {adminToken}
```

Response `200 OK`:

```json
{
  "message": "Khóa tài khoản thành công."
}
```

### 8.2. Mở khóa tài khoản

```http
PUT /api/User/{id}/unlock
Authorization: Bearer {adminToken}
```

Response `200 OK`:

```json
{
  "message": "Mở khóa tài khoản thành công."
}
```

Hai endpoint yêu cầu role `Admin`. Response `400` nếu thao tác thất bại hoặc user không tồn tại.

### 8.3. Xem và cập nhật thông tin cá nhân (Profile)

#### 8.3.1. Xem thông tin cá nhân

```http
GET /api/User/profile
Authorization: Bearer {accessToken}
```

Response `200 OK`:

```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "fullName": "Nguyễn Văn A",
  "email": "customer@example.com",
  "phone": "0901234567",
  "role": "Customer",
  "userType": "Customer",
  "status": "Active",
  "createdAt": "2026-06-25T09:00:00Z",
  "customerCode": "CUS-20260625-ABCDEF",
  "customerType": "Individual",
  "country": "Vietnam",
  "address": "Ho Chi Minh City"
}
```

*Lưu ý:* Các trường `customerCode`, `customerType`, `country`, `address` chỉ trả về khi `userType` là `Customer` (các vai trò nhân viên/admin sẽ có giá trị `null` hoặc không có).

#### 8.3.2. Cập nhật thông tin cá nhân

```http
PUT /api/User/profile
Authorization: Bearer {accessToken}
```

Request Body:

```json
{
  "fullName": "Nguyễn Văn A",
  "phone": "0901234567",
  "country": "Vietnam",
  "address": "123 Đường ABC, Quận 1, TP.HCM"
}
```

Response `200 OK`:

```json
{
  "message": "Cập nhật thông tin cá nhân thành công."
}
```

## 9. Báo giá tạm tính (Quotation Estimate)

Tạo báo giá tạm tính cho yêu cầu ký gửi dựa trên thông tin Order và Order_Items. Đây chỉ là báo giá ước tính, chưa phải báo giá chốt sau khi kho cân đo thực tế.

```http
POST /api/orders/{orderId}/quotation/estimate
Authorization: Bearer {token}
```

**Path Parameters:**

| Parameter | Kiểu | Mô tả |
|---|---|---|
| `orderId` | `Guid` | ID của đơn hàng ký gửi |

Không cần request body — hệ thống tự lấy thông tin từ Order và OrderItems.

### 9.1. Công thức tính phí

| Thành phần | Công thức |
|---|---|
| Total Weight | `Σ OrderItem.Weight` (kg) |
| Total Volume | `Σ (Length × Width × Height)` (cm³) |
| Volumetric Weight | `TotalVolume / 5000` (tiêu chuẩn IATA) |
| Chargeable Weight | `MAX(TotalWeight, VolumetricWeight)` |
| Freight Charge | `ChargeableWeight × Rate` (từ bảng ServicePricing) |
| Service Fee | Express: 50,000 VND \| Consolidation: 30,000 VND |
| Tax & Duty | `10% × Σ DeclaredValue` |
| **Total** | **FreightCharge + ServiceFee + TaxAndDuty** |

Nếu không có bảng giá trong DB, hệ thống dùng giá mặc định: Express = 50,000 VND/kg, Consolidation = 30,000 VND/kg.

### 9.2. Response thành công

Response `200 OK`:

```json
{
  "message": "Tạo báo giá tạm tính thành công.",
  "data": {
    "quotationId": "a1b2c3d4-...",
    "orderId": "e5f6g7h8-...",
    "consignmentType": "Express",
    "quoteType": "ESTIMATE",
    "status": "DRAFT",
    "totalWeight": 2.50,
    "totalVolume": 15000.00,
    "volumetricWeight": 3.00,
    "chargeableWeight": 3.00,
    "estimatedFreightCharge": 150000.00,
    "serviceFee": 50000.00,
    "taxAndDuty": 50000.00,
    "totalEstimatedCost": 250000.00,
    "createdAt": "2026-06-15T12:00:00Z",
    "expiredAt": "2026-06-22T12:00:00Z",
    "items": [
      {
        "id": "i1j2k3l4-...",
        "productName": "iPhone 15",
        "weight": 0.50,
        "length": 15.00,
        "width": 8.00,
        "height": 5.00,
        "declaredValue": 500000.00,
        "itemVolume": 600.00
      }
    ]
  }
}
```

### 9.3. Response lỗi

| Status | Trường hợp | Message |
|---|---|---|
| `400` | Order chưa có sản phẩm | `"Đơn hàng chưa có sản phẩm nào. Vui lòng thêm sản phẩm trước khi tạo báo giá."` |
| `401` | Chưa đăng nhập | Tự động bởi middleware JWT |
| `404` | Order không tồn tại | `"Không tìm thấy đơn hàng với ID: {orderId}"` |

### 9.4. Lưu ý

- Báo giá tạm tính có hiệu lực **7 ngày** (`expiredAt`).
- `quoteType` luôn là `"ESTIMATE"`, `status` luôn là `"DRAFT"`.
- `consignmentType` lấy từ Order: `"Express"` hoặc `"Consolidation"`.
- Cập nhật Quotation → `ACCEPTED`, Order → `WAITING_FOR_PARCEL`.
- Mỗi lần gọi API sẽ tạo mới một Quotation record (không cập nhật cái cũ).
- API cũng cập nhật `totalWeight` và `totalVolume` trên Order.

## 10. Xác nhận báo giá tạm tính (Accept Quotation)

Khách hàng xác nhận đồng ý với báo giá tạm tính. Sau khi xác nhận:
- Trạng thái Quotation chuyển sang `ACCEPTED`.
- Trạng thái Order chuyển sang `WAITING_FOR_PARCEL` (chờ kho nhận hàng).

```http
PUT /api/quotations/{quotationId}/accept
Authorization: Bearer {token}
```

**Path Parameters:**

| Parameter | Kiểu | Mô tả |
|---|---|---|
| `quotationId` | `Guid` | ID của báo giá cần xác nhận |

### 10.1. Response thành công

Response `200 OK`:

```json
{
  "message": "Xác nhận báo giá thành công.",
  "data": {
    "quotationId": "a1b2c3d4-...",
    "orderId": "e5f6g7h8-...",
    "quotationStatus": "ACCEPTED",
    "orderStatus": "WAITING_FOR_PARCEL",
    "acceptedAt": "2026-06-15T12:00:00Z"
  }
}
```

### 10.2. Response lỗi

| Status | Trường hợp | Message |
|---|---|---|
| `400` | Báo giá đã được xác nhận | `"Báo giá này đã được xác nhận trước đó."` |
| `400` | Báo giá đã bị hủy | `"Báo giá này đã bị hủy, không thể xác nhận."` |
| `400` | Trạng thái không hợp lệ | `"Không thể xác nhận báo giá ở trạng thái: {status}"` |
| `400` | Báo giá đã hết hạn | `"Báo giá đã hết hạn. Vui lòng tạo báo giá mới."` |
| `401` | Chưa đăng nhập hoặc sai user | Tự động, hoặc `"Bạn không có quyền xác nhận báo giá này."` |
| `404` | Quotation không tồn tại | `"Không tìm thấy báo giá với ID: {quotationId}"` |

## 11. Staff cập nhật trạng thái yêu cầu ký gửi

Staff kiểm tra thông tin và cập nhật trạng thái cho yêu cầu ký gửi. Nếu từ chối, bắt buộc phải kèm theo lý do để khách hàng có thể xem lại.

```http
PUT /api/orders/consignments/{orderId}/status
Authorization: Bearer {staffToken}
```

**Path Parameters:**

| Parameter | Kiểu | Mô tả |
|---|---|---|
| `orderId` | `Guid` | ID của yêu cầu ký gửi |

**Request Body:**

```json
{
  "status": "REJECTED",
  "rejectionReason": "Hàng hóa chứa vật phẩm cấm bay"
}
```

| Field | Ràng buộc |
| `status` | Bắt buộc. Các giá trị hợp lệ: `PENDING_REVIEW`, `APPROVED`, `REJECTED` |
| `rejectionReason` | Bắt buộc khi `status` là `REJECTED`. Lưu vào trường `Note` của yêu cầu. |

### 11.1 Response thành công

Response `200 OK`:

```json
{
  "message": "Cập nhật trạng thái thành công.",
  "status": "APPROVED",
  "consignmentCode": "VCL-20260616235959-123456"
}
```

*Lưu ý:*
- Khi duyệt thành công trạng thái là `APPROVED` và đơn ký gửi chưa có mã gửi hàng, hệ thống sẽ tự động phát sinh một mã gửi hàng duy nhất và trả về trong trường `consignmentCode`.
- Nếu đơn ký gửi đã có mã gửi hàng từ trước, hệ thống giữ nguyên mã cũ.
- Khi trạng thái là `PENDING_REVIEW` hoặc `REJECTED`, hệ thống không phát sinh mã gửi hàng mới.
- Response trả về trạng thái mới cập nhật (`status`) và mã gửi hàng (`consignmentCode`).

### 11.2 Response lỗi

| Status | Trường hợp | Message |
|---|---|---|
| `400` | Trạng thái không hợp lệ | `"Trạng thái không hợp lệ. Các trạng thái hợp lệ: PENDING_REVIEW, APPROVED, REJECTED."` |
| `400` | Thiếu lý do từ chối | `"Vui lòng cung cấp lý do từ chối (RejectionReason)."` |
| `400` | Yêu cầu đã hủy hoặc nhập kho | `"Không thể cập nhật trạng thái yêu cầu khi yêu cầu đang ở trạng thái {status}"` |
| `401` | Chưa đăng nhập hoặc token sai | Tự động bởi middleware JWT |
| `403` | Không phải Staff | Tự động bởi middleware JWT |
| `404` | Không tìm thấy yêu cầu ký gửi | `"Không tìm thấy yêu cầu ký gửi với ID {orderId}"` |

## 12. Lấy danh sách yêu cầu ký gửi

Hệ thống cung cấp API lấy danh sách yêu cầu ký gửi có phân trang. Customer chỉ thấy yêu cầu của chính mình, Admin và các tài khoản thuộc nhóm Staff (Sale, WarehouseStaff, OperationsManager, Delivery) có thể xem toàn bộ yêu cầu trong hệ thống.

```http
GET /api/orders/consignments
Authorization: Bearer {token}
```

**Query Parameters:**

| Parameter | Kiểu | Mô tả |
|---|---|---|
| `pageNumber` | `int` | Trang hiện tại (mặc định: 1) |
| `pageSize` | `int` | Số lượng trên mỗi trang (mặc định: 10) |
| `status` | `string` | Lọc theo trạng thái yêu cầu (tuỳ chọn) |
| `searchCode` | `string` | Tìm kiếm theo mã yêu cầu ký gửi (tuỳ chọn) |

### 12.1 Response thành công

Response `200 OK`:

```json
{
  "message": "Lấy danh sách yêu cầu ký gửi thành công.",
  "data": {
    "items": [
      {
        "orderId": "e5f6g7h8-...",
        "consignmentCode": "VCL-001",
        "customerName": "Nguyễn Văn A",
        "consignmentType": "Express",
        "status": "WAITING_FOR_PARCEL",
        "totalWeight": 2.50,
        "totalVolume": 15000.00,
        "createdAt": "2026-06-16T12:00:00Z"
      }
    ],
    "totalCount": 1,
    "pageNumber": 1,
    "pageSize": 10,
    "totalPages": 1
  }
}
```

### 12.2 Response lỗi

| Status | Trường hợp | Message |
|---|---|---|
| `401` | Chưa đăng nhập hoặc token không hợp lệ / Người dùng không hợp lệ | Tự động bởi middleware JWT hoặc `"Người dùng không hợp lệ."` |

### 12.3 Xem chi tiết một yêu cầu ký gửi

Xem thông tin chi tiết của một yêu cầu ký gửi cụ thể bằng ID. Customer chỉ được xem đơn hàng của mình; các tài khoản thuộc nhóm Staff được xem mọi đơn hàng.

```http
GET /api/orders/consignments/{orderId}
Authorization: Bearer {token}
```

**Path Parameters:**

| Parameter | Kiểu | Mô tả |
|---|---|---|
| `orderId` | `Guid` | ID của yêu cầu ký gửi |

**Response thành công (200 OK):**

```json
{
  "message": "Lấy chi tiết yêu cầu ký gửi thành công.",
  "data": {
    "orderId": "e5f6g7h8-...",
    "consignmentCode": "VCL-20260616120000-123456",
    "consignmentType": "Express",
    "status": "WAITING_FOR_PARCEL",
    "orderType": "CONSIGNMENT",
    "totalWeight": 2.50,
    "totalVolume": 15000.00,
    "note": "Hàng dễ vỡ, xin nhẹ tay",
    "route": "TQ-VN",
    "receiverName": "Nguyễn Văn B",
    "receiverPhone": "0987654321",
    "receiverAddress": "123 Đường Lê Lợi, Quận 1, TP. HCM",
    "requiresInspection": true,
    "createdAt": "2026-06-16T12:00:00Z",
    "customer": {
      "customerId": "81a7b4c9-...",
      "fullName": "Nguyễn Văn A",
      "email": "customer@example.com",
      "phone": "0901234567"
    },
    "items": [
      {
        "id": "item-guid-...",
        "productName": "Điện thoại di động",
        "productType": "Electronics",
        "quantity": 2,
        "weight": 1.20,
        "width": 10.00,
        "height": 5.00,
        "length": 15.00,
        "declaredValue": 500.00,
        "referenceUrl": "http://example.com/item",
        "domesticTrackingCode": "TRACK123456"
      }
    ],
    "quotation": {
      "quotationId": "quote-guid-...",
      "quoteType": "ESTIMATE",
      "status": "APPROVED",
      "estimatedFreightCharge": 15.00,
      "serviceFee": 5.00,
      "taxAndDuty": 2.00,
      "totalVolume": 15000.00,
      "totalEstimatedCost": 22.00,
      "createdAt": "2026-06-16T12:05:00Z",
      "expiredAt": "2026-06-23T12:05:00Z"
    }
  }
}
```

**Response lỗi:**

| Status | Trường hợp | Message |
|---|---|---|
| `401` | Chưa đăng nhập hoặc không phải chủ đơn (nếu là Customer) | `"Bạn không có quyền xem yêu cầu ký gửi này."` |
| `404` | Không tìm thấy yêu cầu ký gửi | `"Không tìm thấy yêu cầu ký gửi với ID {orderId}"` |

## 13. Customer hủy yêu cầu ký gửi (Cancel Consignment)

Customer hủy yêu cầu ký gửi của mình. Chỉ được phép hủy khi trạng thái đơn hàng khác `CANCELLED` hoặc `WAREHOUSE_RECEIVED`.

```http
PUT /api/orders/consignments/{orderId}/cancel
Authorization: Bearer {customerToken}
```

**Path Parameters:**

| Parameter | Kiểu | Mô tả |
|---|---|---|
| `orderId` | `Guid` | ID của yêu cầu ký gửi cần hủy |

**Request Body:**

```json
{
  "cancelReason": "Thay đổi kế hoạch gửi hàng"
}
```

| Field | Ràng buộc |
|---|---|
| `cancelReason` | Không bắt buộc. Lý do hủy đơn. |

### 13.1 Response thành công

Response `200 OK`:

```json
{
  "message": "Hủy yêu cầu ký gửi thành công.",
  "status": "CANCELLED",
  "consignmentCode": "VCL-20260616235959-123456"
}
```

### 13.2 Response lỗi

| Status | Trường hợp | Message |
|---|---|---|
| `400` | Trạng thái không hợp lệ (đã hủy hoặc đã nhận kho) | `"Không thể hủy yêu cầu ký gửi khi yêu cầu đang ở trạng thái {status}"` |
| `401` | Chưa đăng nhập hoặc token sai | Tự động bởi middleware JWT |
| `401` | Không có quyền hủy đơn hàng này (không phải chủ sở hữu) | `"Bạn không có quyền hủy yêu cầu ký gửi này."` |
| `404` | Không tìm thấy yêu cầu ký gửi | `"Không tìm thấy yêu cầu ký gửi với ID {orderId}"` |

## 14. Quản lý danh mục hàng cấm/hạn chế (Restricted Items)

Admin quản lý danh sách các mặt hàng bị cấm (BANNED), hạn chế (RESTRICTED), hoặc cảnh báo (WARNING). Dữ liệu này được đối chiếu tự động khi Khách hàng gửi yêu cầu ký gửi.

### 14.1. Lấy danh sách hàng cấm/hạn chế

```http
GET /api/restricted-items
Authorization: Bearer {adminToken}
```

Response `200 OK`:
```json
[
  {
    "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "itemName": "Pin Lithium Test",
    "country": "Vietnam",
    "restrictionType": "BANNED",
    "note": "Nguy hiểm cháy nổ, cấm vận chuyển bay",
    "isActive": true
  }
]
```

### 14.2. Lấy chi tiết theo ID

```http
GET /api/restricted-items/{id}
Authorization: Bearer {adminToken}
```

Response `200 OK` (chi tiết mặt hàng). Trả về lỗi `404 Not Found` nếu không tìm thấy ID.

### 14.3. Thêm mặt hàng mới

```http
POST /api/restricted-items
Authorization: Bearer {adminToken}
```

Request Body:
```json
{
  "itemName": "Pin Lithium Test",
  "country": "Vietnam",
  "restrictionType": "BANNED",
  "note": "Nguy hiểm cháy nổ, cấm vận chuyển bay",
  "isActive": true
}
```

| Field | Ràng buộc |
|---|---|
| `itemName` | Bắt buộc, tối đa 255 ký tự |
| `country` | Không bắt buộc, tối đa 100 ký tự |
| `restrictionType` | Bắt buộc, chỉ nhận một trong ba giá trị: `BANNED`, `RESTRICTED`, `WARNING` |
| `note` | Không bắt buộc |
| `isActive` | Không bắt buộc, mặc định là `true` |

Response `201 Created` trả về thông tin mặt hàng vừa tạo kèm ID.
Response `400 Bad Request` khi dữ liệu lỗi hoặc bị trùng (cùng tên, cùng quốc gia áp dụng và cùng loại hạn chế).

### 14.4. Cập nhật thông tin mặt hàng

Sử dụng để chỉnh sửa chi tiết hoặc bật/tắt trạng thái hoạt động (`isActive: false` để vô hiệu hóa mặt hàng).

```http
PUT /api/restricted-items/{id}
Authorization: Bearer {adminToken}
```

Request Body:
```json
{
  "itemName": "Pin Lithium Test",
  "country": "Vietnam",
  "restrictionType": "BANNED",
  "note": "Đã sửa đổi ghi chú",
  "isActive": false
}
```

Response `200 OK`:
```json
{
  "message": "Cập nhật mặt hàng thành công."
}
```

Response `400` nếu thông tin trùng lặp với mặt hàng khác hoặc không hợp lệ. Trả về `404` nếu ID không tồn tại.

### 14.5. Xóa mặt hàng cấm/hạn chế

```http
DELETE /api/restricted-items/{id}
Authorization: Bearer {adminToken}
```

Response `200 OK`:
```json
{
  "message": "Xóa mặt hàng thành công."
}
```

Trả về `404 Not Found` nếu không tìm thấy ID.

## 15. Quản lý cấu hình bảng giá (Pricing Rules)

Admin quản lý cấu hình bảng giá dùng để tính báo giá ký gửi. Bảng giá này được hệ thống sử dụng khi tạo báo giá tạm tính (estimate) và báo giá chốt cho yêu cầu ký gửi.

### 15.1. Xem danh sách cấu hình bảng giá

```http
GET /api/pricing-rules
```

Response `200 OK`:
```json
[
  {
    "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "shippingServiceType": "Air",
    "consignmentType": "Express",
    "route": "Vietnam",
    "unitType": "kg",
    "pricePerWeight": 50000.00,
    "pricePerVolume": 100000.00,
    "serviceFee": 50000.00,
    "isActive": true,
    "createdAt": "2026-06-19T12:00:00Z"
  }
]
```

### 15.2. Lấy chi tiết cấu hình giá theo ID

```http
GET /api/pricing-rules/{id}
```

Response `200 OK` (chi tiết cấu hình giá). Trả về `404 Not Found` nếu không tìm thấy ID.

### 15.3. Thêm cấu hình giá mới

```http
POST /api/pricing-rules
Authorization: Bearer {adminToken}
```

Request Body:
```json
{
  "shippingServiceType": "Air",
  "consignmentType": "Express",
  "route": "Vietnam",
  "unitType": "kg",
  "pricePerWeight": 50000.00,
  "pricePerVolume": 100000.00,
  "serviceFee": 50000.00,
  "isActive": true
}
```

| Field | Ràng buộc |
|---|---|
| `shippingServiceType` | Bắt buộc, tối đa 100 ký tự |
| `consignmentType` | Bắt buộc, tối đa 100 ký tự (thường là `Express` hoặc `Consolidation`) |
| `route` | Không bắt buộc, tối đa 255 ký tự (Tên quốc gia hoặc tuyến đường, ví dụ: `Vietnam`) |
| `unitType` | Không bắt buộc, tối đa 50 ký tự |
| `pricePerWeight` | Bắt buộc, >= 0 |
| `pricePerVolume` | Không bắt buộc, >= 0 |
| `serviceFee` | Bắt buộc, >= 0 |
| `isActive` | Không bắt buộc, mặc định là `true` |

Response `201 Created` trả về thông tin cấu hình giá vừa tạo kèm ID.
Response `400 Bad Request` khi dữ liệu lỗi hoặc bị trùng (cùng loại dịch vụ vận chuyển, loại ký gửi, và tuyến vận chuyển).

### 15.4. Cập nhật cấu hình giá

Sử dụng để chỉnh sửa thông tin cấu hình giá hoặc bật/tắt trạng thái hoạt động (`isActive: false` để vô hiệu hóa cấu hình giá).

```http
PUT /api/pricing-rules/{id}
Authorization: Bearer {adminToken}
```

Request Body:
```json
{
  "shippingServiceType": "Air",
  "consignmentType": "Express",
  "route": "Vietnam",
  "unitType": "kg",
  "pricePerWeight": 55000.00,
  "pricePerVolume": 120000.00,
  "serviceFee": 50000.00,
  "isActive": false
}
```

Response `200 OK`:
```json
{
  "message": "Cập nhật cấu hình giá thành công."
}
```

Response `400` nếu thông tin bị trùng hoặc không hợp lệ. Trả về `404` nếu không tìm thấy ID.

### 15.5. Xóa cấu hình giá

```http
DELETE /api/pricing-rules/{id}
Authorization: Bearer {adminToken}
```

Response `200 OK`:
```json
{
  "message": "Xóa cấu hình giá thành công."
}
```

Trả về `404 Not Found` nếu không tìm thấy ID.

## 16. Check-in kiện hàng bằng mã gửi hàng

Quy trình dành cho nhân viên kho (Warehouse Staff) quét hoặc nhập mã gửi hàng (Consignment Code) để đối chiếu, cân, đo kích thước thực tế của kiện hàng và tạo Parcel.

```http
POST /api/parcels/check-in
Authorization: Bearer {staffToken}
```

**Request Body:**

```json
{
  "shippingCode": "VCL-20260616235959-123456",
  "actualWeight": 5.40,
  "length": 35.00,
  "width": 25.00,
  "height": 15.00
}
```

| Field | Ràng buộc | Mô tả |
|---|---|---|
| `shippingCode` | Bắt buộc. Định dạng bắt đầu bằng `VCL-` | Mã gửi hàng của yêu cầu ký gửi |
| `actualWeight` | Bắt buộc. Phải lớn hơn 0 | Cân nặng thực tế (kg) |
| `length` | Không bắt buộc | Chiều dài thực tế (cm) |
| `width` | Không bắt buộc | Chiều rộng thực tế (cm) |
| `height` | Không bắt buộc | Chiều cao thực tế (cm) |

### 16.1. Logic tính toán và lưu trữ
- **Volumetric Weight**: Nếu có đủ 3 kích thước `length`, `width`, `height` và đều lớn hơn 0, hệ thống tự động tính:
  `volumetricWeight = (length * width * height) / 5000`
- **Chargeable Weight**: Được tính bằng `MAX(actualWeight, volumetricWeight ?? 0)`.
- **Order Status**: Trạng thái của Order (yêu cầu ký gửi) tương ứng sẽ tự động chuyển sang `"WAREHOUSE_RECEIVED"`.
- **Parcel Status**: Trạng thái của Parcel mới tạo sẽ là `"RECEIVED"`.

### 16.2. Response thành công

Response `200 OK`:

```json
{
  "message": "Check-in kiện hàng thành công.",
  "data": {
    "shippingCode": "VCL-20260616235959-123456",
    "consignmentId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "consignmentCode": "VCL-20260616235959-123456",
    "customerName": "Nguyễn Văn A",
    "customerCode": "CUS-20260616-A1B2C3",
    "packageCode": "PCL-20260620170000-987654",
    "actualWeight": 5.40,
    "length": 35.00,
    "width": 25.00,
    "height": 15.00,
    "volumetricWeight": 2.63,
    "chargeableWeight": 5.40,
    "packageStatus": "RECEIVED",
    "declaredItems": [
      {
        "productName": "Sách tin học",
        "productType": "Book",
        "quantity": 2,
        "weight": 1.20,
        "length": 20.00,
        "width": 15.00,
        "height": 10.00,
        "declaredValue": 200000.00
      }
    ]
  }
}
```

### 16.3. Response lỗi

| Status | Trường hợp | Message |
|---|---|---|
| `400` | Mã gửi hàng sai định dạng | `"Mã gửi hàng không hợp lệ."` |
| `400` | Cân nặng thực tế <= 0 | `"Cân nặng thực tế phải lớn hơn 0."` |
| `400` | Yêu cầu ký gửi không ở trạng thái APPROVED | `"Yêu cầu ký gửi không ở trạng thái APPROVED."` |
| `401` | Chưa đăng nhập hoặc token không hợp lệ | Tự động bởi middleware JWT |
| `403` | Không có quyền thao tác (không phải Staff/WarehouseStaff) | Tự động bởi middleware JWT |
| `404` | Không tìm thấy yêu cầu ký gửi tương ứng | `"Không tìm thấy yêu cầu ký gửi."` |

---

## 17. Lấy danh sách kiện chờ nhập vị trí kho (Pending Putaway)

Quy trình dành cho nhân viên kho (Warehouse Staff / Staff) xem danh sách các kiện hàng đã được nhận tại kho nhưng chưa được sắp xếp/gán vào ô kệ (Bin) cụ thể (BinId = null) để tiến hành put-away.

> **Cập nhật (VCL-120):** khi không truyền `status`, danh sách bao gồm kiện ở **cả hai** trạng thái sẵn sàng put-away:
> - `RECEIVED` — kiện check-in trực tiếp tại kho/điểm tập kết
> - `RECEIVED_AT_DESTINATION` — kiện nhận từ lô vận chuyển quốc tế tại kho đích (xem mục 31.3)

```http
GET /api/parcels/pending-putaway?pageNumber=1&pageSize=10
Authorization: Bearer {staffToken}
```

**Query Parameters:**

| Parameter | Type | Required | Default | Description |
|---|---|---|---|---|
| `pageNumber` | `number` | No | 1 | Số thứ tự trang cần lấy |
| `pageSize` | `number` | No | 10 | Số lượng kiện hàng tối đa hiển thị trên mỗi trang |
| `search` | `string` | No | - | Từ khóa tìm kiếm theo mã kiện hàng (`PCL-...`) hoặc mã yêu cầu ký gửi (`VCL-...`) |
| `status` | `string` | No | - | Lọc theo trạng thái kiện hàng (ví dụ: `RECEIVED`) |

### 17.1. Response thành công

Response `200 OK`:

```json
{
  "message": "Lấy danh sách kiện chờ nhập vị trí kho thành công.",
  "data": {
    "items": [
      {
        "id": "7fa85f64-5717-4562-b3fc-2c963f66afa7",
        "packageCode": "PCL-20260620170000-987654",
        "consignmentCode": "VCL-20260616235959-123456",
        "customerName": "Nguyễn Văn A",
        "actualWeight": 5.40,
        "length": 35.00,
        "width": 25.00,
        "height": 15.00,
        "volumetricWeight": 2.63,
        "chargeableWeight": 5.40,
        "packageStatus": "RECEIVED",
        "checkInTime": "2026-06-20T17:00:00Z"
      }
    ],
    "totalCount": 1,
    "pageNumber": 1,
    "pageSize": 10,
    "totalPages": 1
  }
}
```

### 17.2. Response lỗi

| Status | Trường hợp | Message |
|---|---|---|
| `401` | Chưa đăng nhập hoặc token không hợp lệ | Tự động bởi middleware JWT |
| `403` | Không có quyền thao tác (không phải Staff/WarehouseStaff) | Tự động bởi middleware JWT |

---

## 18. Nhập vị trí kho cho kiện hàng (Put-away)

Quy trình dành cho nhân viên kho (Warehouse Staff / Staff) xếp kiện hàng vào một ô kệ (`Bin`) cụ thể trong kho. Trạng thái kiện hàng sau khi nhập vị trí thành công sẽ cập nhật thành `STORED`.

> **Cập nhật (VCL-120):** cho phép put-away kiện ở trạng thái `RECEIVED` **hoặc** `RECEIVED_AT_DESTINATION` (kiện nhận từ lô vận chuyển quốc tế). Kiện chưa được nhận tại kho hoặc đã `STORED` → `400`. Sau put-away, `POST /api/inventories/from-parcel/{parcelId}` ghi nhận tồn kho `AVAILABLE` theo đúng kho hiện tại của kiện (không đổi, dùng chung cho cả 2 luồng).

```http
PUT /api/parcels/{parcelId}/put-away
Authorization: Bearer {staffToken}
```

**Request Body:**

```json
{
  "warehouseId": "a987c88b-1111-2222-3333-444455556666",
  "zoneId": "b987c88b-1111-2222-3333-444455556666",
  "shelfId": "c987c88b-1111-2222-3333-444455556666",
  "binId": "d987c88b-1111-2222-3333-444455556666"
}
```

### 18.1. Response thành công

Response `200 OK`:

```json
{
  "message": "Nhập vị trí kho cho kiện hàng thành công.",
  "data": {
    "packageCode": "PCL-20260620170000-987654",
    "warehouseName": "Kho Sài Gòn",
    "zoneName": "Khu A",
    "shelfCode": "SHF-A1",
    "binCode": "BIN-A1-01",
    "packageStatus": "STORED",
    "putAwayTime": "2026-06-21T01:38:49Z"
  }
}
```

### 18.2. Response lỗi

| Status | Trường hợp | Message |
|---|---|---|
| `400` | Thiếu thông tin hoặc ô kệ không hợp lệ/không đủ sức chứa | `"Ô kệ không đủ sức chứa thể tích."` hoặc `"Ô kệ không đủ sức chứa tải trọng."` |
| `401` | Chưa đăng nhập hoặc token không hợp lệ | Tự động bởi middleware JWT |
| `403` | Không có quyền thao tác (không phải Staff/WarehouseStaff) | Tự động bởi middleware JWT |
| `404` | Không tìm thấy kiện hàng hoặc vị trí kho tương ứng | `"Không tìm thấy kiện hàng."` hoặc `"Không tìm thấy ô kệ (Bin)."` |

## 19. Sales tạo yêu cầu ký gửi thay cho khách hàng (Sales Create Consignment)

Sales tạo yêu cầu ký gửi thay cho Customer khi khách hàng liên hệ trực tiếp qua điện thoại, chat hoặc tại quầy.

```http
POST /api/staff/consignments
Authorization: Bearer {salesToken}
```

**Request Body:**

```json
{
  "customerId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "customerPhone": "0901234567",
  "customerEmail": "customer@example.com",
  "route": "TQ-VN",
  "shippingOption": "Sea - Consolidation",
  "receiverName": "Nguyen Van B",
  "receiverPhone": "0987654321",
  "receiverAddress": "123 Nguyen Trai, Q5, HCMC",
  "requiresInspection": true,
  "note": "Khách ký gửi tại quầy",
  "items": [
    {
      "productName": "Áo khoác nam",
      "productType": "Quần áo",
      "quantity": 10,
      "weight": 0.5,
      "width": 30,
      "height": 20,
      "length": 10,
      "declaredValue": 1000000,
      "referenceUrl": "http://taobao.com/item/123",
      "domesticTrackingCode": "SF123456789"
    }
  ]
}
```

| Field | Ràng buộc | Mô tả |
|---|---|---|
| `customerId` | Chọn 1 trong 3: `customerId` / `customerPhone` / `customerEmail`. | ID của khách hàng có sẵn trong hệ thống |
| `customerPhone` | Chọn 1 trong 3: `customerId` / `customerPhone` / `customerEmail`. | Số điện thoại khách hàng để tìm kiếm |
| `customerEmail` | Chọn 1 trong 3: `customerId` / `customerPhone` / `customerEmail`. | Email khách hàng để tìm kiếm |
| `route` | Bắt buộc. | Tuyến đường vận chuyển (ví dụ: `TQ-VN`) |
| `shippingOption` | Bắt buộc. | Phương thức vận chuyển (khớp với ServicePricing, ví dụ: `Air - Express`, `Sea - Consolidation`) |
| `items` | Bắt buộc. Tối thiểu 1 phần tử. | Danh sách hàng hóa ký gửi |
| `items[].productName` | Bắt buộc. | Tên mặt hàng ký gửi |
| `items[].productType` | Bắt buộc. | Loại mặt hàng (ví dụ: `Quần áo`, `Điện tử`) |
| `items[].quantity` | Bắt buộc. Phải > 0. | Số lượng mặt hàng |

### 19.1 Response thành công

Response `201 Created`:

```json
{
  "message": "Tạo yêu cầu ký gửi thay khách hàng thành công.",
  "data": {
    "orderId": "e5f6g7h8-...",
    "customerId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "userId": "a1b2c3d4-...",
    "status": "APPROVED",
    "consignmentType": "Sea - Consolidation",
    "route": "TQ-VN",
    "requiresInspection": true,
    "itemCount": 1,
    "quotation": {
      "quotationId": "9f8e7d6c-...",
      "quoteType": "ESTIMATE",
      "status": "DRAFT",
      "estimatedFreightCharge": 150000,
      "serviceFee": 50000,
      "taxAndDuty": 20000,
      "totalVolume": 6000,
      "totalEstimatedCost": 220000,
      "createdAt": "2026-06-29T08:00:00Z",
      "expiredAt": "2026-07-04T08:00:00Z"
    },
    "warnings": []
  }
}
```

*Lưu ý:*
- Trạng thái ban đầu của yêu cầu được cấu hình thông qua `ConsignmentSettings:InitialStatusByStaff` (mặc định là `APPROVED`).
- Nếu trạng thái là `APPROVED`, hệ thống tự sinh mã gửi hàng (`consignmentCode`) giống luồng duyệt của Staff.
- Trả về báo giá tạm tính tương tự như khi khách hàng tự tạo yêu cầu.

### 19.2 Response lỗi

| Status | Trường hợp | Message |
|---|---|---|
| `400` | Thiếu thông tin bắt buộc | `"Vui lòng chọn tuyến hàng."` hoặc `"Vui lòng chọn phương thức vận chuyển."` |
| `400` | Mặt hàng chứa hàng cấm | `"Không thể tạo ký gửi. Các mặt hàng sau thuộc danh mục cấm: {bannedItemNames}"` |
| `401` | Chưa đăng nhập hoặc token không hợp lệ / không phải Sales | Tự động bởi middleware JWT |
| `404` | Không tìm thấy Customer | `"Không tìm thấy khách hàng với ID {customerId}"` |

---

## 20. TypeScript types

```typescript
export interface CustomerRegisterRequest {
  fullName: string;
  email: string;
  password: string;
  phone: string;
  country: string;
  address: string;
}

export interface VerifyCustomerOtpRequest {
  email: string;
  otp: string;
}

export interface ResendCustomerOtpRequest {
  email: string;
}

export interface LoginRequest {
  email: string;
  password: string;
}

export interface AdminRegisterRequest {
  fullName: string;
  email: string;
  password: string;
  phone: string;
  role: string;
}

export interface ForgotPasswordRequest {
  email: string;
}

export interface ResetPasswordRequest {
  email: string;
  otp: string;
  newPassword: string;
}

export interface AuthResponse {
  token: string;
  expiresAt: string;
  userId: string;
  fullName: string;
  role: string;
}

export interface MessageResponse {
  message: string;
}

export interface VerifyOtpResponse extends MessageResponse {
  customerId: string;
}

export interface CreatedUserResponse {
  id: string;
}

export interface ApiError {
  message?: string;
  error?: string;
  status?: number;
}
export interface EstimateQuotationResponse {
  quotationId: string;
  orderId: string;
  consignmentType: string;
  quoteType: string;
  status: string;
  totalWeight: number;
  totalVolume: number;
  volumetricWeight: number;
  chargeableWeight: number;
  estimatedFreightCharge: number;
  serviceFee: number;
  taxAndDuty: number;
  totalEstimatedCost: number;
  createdAt: string;
  expiredAt: string | null;
  items: OrderItemDetail[];
}

export interface OrderItemDetail {
  id: string;
  productName: string;
  weight: number | null;
  length: number | null;
  width: number | null;
  height: number | null;
  declaredValue: number | null;
  itemVolume: number;
}

export interface QuotationDataResponse extends MessageResponse {
  data: EstimateQuotationResponse;
}

export interface AcceptQuotationResponse {
  quotationId: string;
  orderId: string;
  quotationStatus: string;
  orderStatus: string;
  acceptedAt: string;
}

export interface AcceptQuotationDataResponse extends MessageResponse {
  data: AcceptQuotationResponse;
}

export interface UpdateConsignmentStatusRequest {
  status: string;
  rejectionReason?: string;
}

export interface UpdateConsignmentStatusResponse extends MessageResponse {
  status: string;
  consignmentCode: string | null;
}

export interface ConsignmentResponseDto {
  orderId: string;
  consignmentCode: string;
  customerName: string;
  consignmentType: string;
  status: string;
  totalWeight: number | null;
  totalVolume: number | null;
  route: string | null;
  receiverName: string | null;
  receiverPhone: string | null;
  receiverAddress: string | null;
  requiresInspection: boolean;
  createdAt: string | null;
}

export interface ConsignmentCustomerInfo {
  customerId: string | null;
  fullName: string;
  email: string | null;
  phone: string | null;
}

export interface ConsignmentItemDetail {
  id: string;
  productName: string | null;
  productType: string | null;
  quantity: number | null;
  weight: number | null;
  width: number | null;
  height: number | null;
  length: number | null;
  declaredValue: number | null;
  referenceUrl: string | null;
  domesticTrackingCode: string | null;
}

export interface ConsignmentQuotationSummary {
  quotationId: string;
  quoteType: string | null;
  status: string | null;
  estimatedFreightCharge: number | null;
  serviceFee: number | null;
  taxAndDuty: number | null;
  totalVolume: number | null;
  totalEstimatedCost: number;
  createdAt: string | null;
  expiredAt: string | null;
}

export interface ConsignmentDetailResponse {
  orderId: string;
  consignmentCode: string | null;
  consignmentType: string;
  status: string;
  orderType: string | null;
  totalWeight: number | null;
  totalVolume: number | null;
  note: string | null;
  route: string | null;
  receiverName: string | null;
  receiverPhone: string | null;
  receiverAddress: string | null;
  requiresInspection: boolean;
  createdAt: string | null;
  customer: ConsignmentCustomerInfo;
  items: ConsignmentItemDetail[];
  quotation: ConsignmentQuotationSummary | null;
}

export interface ConsignmentDetailDataResponse extends MessageResponse {
  data: ConsignmentDetailResponse;
}

export interface PaginatedResponse<T> {
  items: T[];
  totalCount: number;
  pageNumber: number;
  pageSize: number;
  totalPages: number;
}

export interface ConsignmentPaginatedResponse extends MessageResponse {
  data: PaginatedResponse<ConsignmentResponseDto>;
}

export interface CancelConsignmentRequest {
  cancelReason?: string;
}

export interface CancelConsignmentResponse extends MessageResponse {
  status: string;
  consignmentCode: string | null;
}

export interface RestrictedItemDto {
  id: string;
  itemName: string;
  country: string | null;
  restrictionType: 'BANNED' | 'RESTRICTED' | 'WARNING';
  note: string | null;
  isActive: boolean;
}

export interface CreateRestrictedItemRequest {
  itemName: string;
  country?: string;
  restrictionType: 'BANNED' | 'RESTRICTED' | 'WARNING';
  note?: string;
  isActive?: boolean;
}

export interface UpdateRestrictedItemRequest {
  itemName: string;
  country?: string;
  restrictionType: 'BANNED' | 'RESTRICTED' | 'WARNING';
  note?: string;
  isActive: boolean;
}

export interface PricingRuleDto {
  id: string;
  servicePricingId: string | null;
  ruleName: string;
  ruleCode: string;
  ruleType: string;
  conditionType: string | null;
  conditionValue: string | null;
  calculationType: string;
  value: number;
  minAmount: number | null;
  maxAmount: number | null;
  isRequired: boolean;
  status: string;
  description: string | null;
  createdAt: string;
  updatedAt: string | null;
}

export interface CreatePricingRuleRequest {
  servicePricingId?: string;
  ruleName: string;
  ruleCode: string;
  ruleType: string;
  conditionType?: string;
  conditionValue?: string;
  calculationType: string;
  value: number;
  minAmount?: number;
  maxAmount?: number;
  isRequired?: boolean;
  status?: string;
  description?: string;
}

export interface UpdatePricingRuleRequest {
  servicePricingId?: string;
  ruleName: string;
  ruleCode: string;
  ruleType: string;
  conditionType?: string;
  conditionValue?: string;
  calculationType: string;
  value: number;
  minAmount?: number;
  maxAmount?: number;
  isRequired: boolean;
  status: string;
  description?: string;
}

export interface ParcelCheckInRequest {
  shippingCode: string;
  actualWeight: number;
  length?: number;
  width?: number;
  height?: number;
}

export interface DeclaredItemDto {
  productName: string;
  productType: string;
  quantity: number;
  weight: number | null;
  length: number | null;
  width: number | null;
  height: number | null;
  declaredValue: number | null;
}

export interface ParcelCheckInResponse {
  shippingCode: string;
  consignmentId: string;
  consignmentCode: string;
  customerName: string;
  customerCode: string;
  packageCode: string;
  actualWeight: number;
  length: number | null;
  width: number | null;
  height: number | null;
  volumetricWeight: number | null;
  chargeableWeight: number;
  packageStatus: string;
  declaredItems: DeclaredItemDto[];
}

export interface ParcelCheckInDataResponse extends MessageResponse {
  data: ParcelCheckInResponse;
}

export interface PendingPutawayParcelDto {
  id: string;
  packageCode: string;
  consignmentCode: string;
  customerName: string;
  actualWeight: number | null;
  length: number | null;
  width: number | null;
  height: number | null;
  volumetricWeight: number | null;
  chargeableWeight: number | null;
  packageStatus: string;
  checkInTime: string | null;
}

export interface PendingPutawayParcelsResponse {
  items: PendingPutawayParcelDto[];
  totalCount: number;
  pageNumber: number;
  pageSize: number;
  totalPages: number;
}

export interface PendingPutawayParcelsDataResponse extends MessageResponse {
  data: PendingPutawayParcelsResponse;
}

export interface ConsignmentItemRequest {
  productName: string;
  productType: string;
  quantity: number;
  weight?: number;
  width?: number;
  height?: number;
  length?: number;
  declaredValue?: number;
  referenceUrl?: string;
  domesticTrackingCode?: string;
}

export interface CreateConsignmentByStaffRequest {
  customerId?: string;
  customerPhone?: string;
  customerEmail?: string;
  route: string;
  shippingOption: string;
  receiverName?: string;
  receiverPhone?: string;
  receiverAddress?: string;
  requiresInspection: boolean;
  note?: string;
  items: ConsignmentItemRequest[];
}

export interface RestrictedItemWarning {
  productName: string;
  matchedItem: string;
  restrictionType: string;
  note: string | null;
}

export interface CreateConsignmentResponse {
  orderId: string;
  customerId: string | null;
  userId: string | null;
  status: string;
  consignmentType: string;
  route: string;
  requiresInspection: boolean;
  itemCount: number;
  quotation: EstimateQuotationResponse;
  warnings: RestrictedItemWarning[];
}

export interface CreateConsignmentDataResponse extends MessageResponse {
  data: CreateConsignmentResponse;
}

export interface InventoryResponse {
  inventoryId: string;
  customerId: string;
  orderId: string;
  orderItemId: string | null;
  parcelId: string;
  binId: string;
  quantity: number;
  status: string;
  createdAt: string;
}

export interface InventoryDataResponse {
  message: string;
  data: InventoryResponse;
}

export interface CreateWroRequestItem {
  inventoryId: string;
  quantity: number;
}

export interface CreateWroRequest {
  receiverName: string;
  receiverPhone: string;
  deliveryAddress: string;
  shelfCode?: string;
  items: CreateWroRequestItem[];
}

export interface WroItemResponse {
  itemId: string;
  inventoryId: string;
  quantity: number;
  packageCode: string;
}

export interface WroResponse {
  wroId: string;
  wroCode: string;
  customerId: string | null;
  receiverName: string;
  receiverPhone: string;
  deliveryAddress: string;
  status: string;
  shelfCode: string | null;
  createdAt: string;
  items: WroItemResponse[];
}

export interface WroDataResponse {
  message: string;
  data: WroResponse;
}

export interface WroDetailItem {
  itemId: string;
  inventoryId: string;
  quantity: number;
  packageCode: string;
  productName: string;
  binCode: string;
}

export interface WroDetailResponse {
  wroId: string;
  wroCode: string;
  createdByName: string;
  createdByUserRole: string;
  warehouseName: string;
  customerId: string | null;
  customerName: string;
  receiverName: string;
  receiverPhone: string;
  deliveryAddress: string;
  status: string;
  shelfCode: string | null;
  note: string | null;
  createdAt: string;
  totalQuantity: number;
  items: WroDetailItem[];
}

export interface WroDetailDataResponse {
  message: string;
  data: WroDetailResponse;
}

export interface UpdateWroStatusRequest {
  status: string;
  rejectionReason?: string;
}

export interface UpdateWroStatusResponse {
  wroId: string;
  wroCode: string;
  status: string;
}

export interface UpdateWroStatusDataResponse extends MessageResponse {
  data: UpdateWroStatusResponse;
}

export interface PickingItemResponse {
  itemId: string;
  inventoryId: string;
  packageCode: string;
  productName: string;
  binCode: string;
  shelfCode: string;
  quantity: number;
}

export interface PickingListResponse {
  pickingListId: string;
  wroRequestId: string;
  wroCode: string;
  customerId: string | null;
  customerName: string;
  status: string;
  createdBy: string;
  createdByName: string;
  createdAt: string;
  items: PickingItemResponse[];
}

export interface PickingListDataResponse extends MessageResponse {
  data: PickingListResponse;
}

export interface ConfirmPickingItem {
  inventoryId: string;
  quantity: number;
}

export interface ConfirmPickingRequest {
  items: ConfirmPickingItem[];
}

export interface ConfirmPickingItemResponse {
  inventoryId: string;
  quantity: number;
  binId: string;
  binCode: string;
  shelfCode: string;
}

export interface ConfirmPickingResponse {
  pickingListId: string;
  wroRequestId: string;
  status: string;
  confirmedBy: string | null;
  confirmedByName: string;
  confirmedAt: string | null;
  items: ConfirmPickingItemResponse[];
}

export interface ConfirmPickingDataResponse extends MessageResponse {
  data: ConfirmPickingResponse;
}

export interface CompleteWroItem {
  inventoryId: string;
  quantity: number;
}

export interface CompleteWroRequest {
  items: CompleteWroItem[];
}

export interface CompleteWroItemResponse {
  inventoryId: string;
  quantity: number;
  status: string;
}

export interface CompleteWroResponse {
  wroRequestId: string;
  wroCode: string;
  status: string;
  releasedBy: string | null;
  releasedByName: string;
  releasedAt: string | null;
  warehouseId: string | null;
  warehouseName: string;
  items: CompleteWroItemResponse[];
}

export interface CompleteWroDataResponse extends MessageResponse {
  data: CompleteWroResponse;
}

export interface WarehouseResponse {
  id: string;
  name: string;
  code: string;
  address: string;
  warehouseType: string;
  isActive: boolean;
}

export interface WarehouseListResponse {
  items: WarehouseResponse[];
}

export interface WarehouseLocationResponse {
  locationId: string;
  binCode: string;
  shelfId: string;
  shelfCode: string;
  zoneId: string;
  zoneName: string;
  warehouseId: string;
  warehouseName: string;
  maxVolume: number | null;
  maxWeight: number | null;
  isActive: boolean;
  note: string;
}

export interface WarehouseLocationListResponse {
  items: WarehouseLocationResponse[];
}

export interface CreateWarehouseRequest {
  name: string;
  code: string;
  address: string;
  warehouseType?: string;
  isActive?: boolean;
}

export interface CreateLocationRequest {
  zoneName: string;
  shelfCode: string;
  binCode: string;
  maxVolume?: number;
  maxWeight?: number;
  isActive?: boolean;
  note?: string;
}
```

## 22. Ghi nhận tồn kho khả dụng từ kiện hàng (Warehouse Inventory)

Staff/WarehouseStaff ghi nhận tồn kho khả dụng sau khi Parcel đã được nhập vào vị trí kho thành công để chuẩn bị cho quy trình xuất kho.

```http
POST /api/inventories/from-parcel/{parcelId}
Authorization: Bearer {staffToken}
```

**Parameters:**

| Name | Type | Description |
|---|---|---|
| `parcelId` | `string (Guid)` | ID của kiện hàng đã được nhập vị trí kho (status `STORED`) |

**Response thành công (201 Created):**

```json
{
  "message": "Ghi nhận tồn kho khả dụng thành công.",
  "data": {
    "inventoryId": "9b1deb4d-3b7d-4bad-9bdd-2b152d12ebcd",
    "customerId": "81a7b4c9-...",
    "orderId": "64b734c1-...",
    "orderItemId": "24b784a0-...",
    "parcelId": "517ac767-...",
    "binId": "e14b21a7-...",
    "quantity": 5,
    "status": "AVAILABLE",
    "createdAt": "2026-06-29T08:45:00Z"
  }
}
```

**Response lỗi:**

| Status | Trường hợp | Message |
|---|---|---|
| `400` | Kiện hàng chưa được put-away | `"Kiện hàng chưa được nhập vị trí kho (chưa put-away)."` |
| `400` | Ghi nhận trùng cho cùng một Parcel | `"Kiện hàng đã được ghi nhận tồn kho trước đó."` |
| `401` | Chưa đăng nhập hoặc token không hợp lệ | Tự động bởi middleware JWT |
| `403` | Không có quyền thao tác (không phải Staff/WarehouseStaff) | Tự động bởi middleware JWT |
| `404` | Không tìm thấy Parcel | `"Không tìm thấy kiện hàng với ID: {parcelId}"` |

---

## 23. Axios integration

```typescript
import axios from 'axios';

export const api = axios.create({
  baseURL: import.meta.env.VITE_API_BASE_URL ?? 'https://localhost:7237',
  headers: {
    'Content-Type': 'application/json',
  },
});

api.interceptors.request.use((config) => {
  const token = localStorage.getItem('accessToken');
  if (token) {
    config.headers.Authorization = `Bearer ${token}`;
  }
  return config;
});

api.interceptors.response.use(
  (response) => response,
  (error) => {
    if (error.response?.status === 401) {
      localStorage.removeItem('accessToken');
    }
    return Promise.reject(error);
  },
);
```

Ví dụ flow đăng ký khách hàng:

```typescript
export async function registerCustomer(payload: CustomerRegisterRequest) {
  return api.post<MessageResponse>('/api/Auth/customer/register', payload);
}

export async function verifyCustomerOtp(payload: VerifyCustomerOtpRequest) {
  return api.post<VerifyOtpResponse>('/api/Auth/customer/verify-otp', payload);
}

export async function resendCustomerOtp(email: string) {
  return api.post<MessageResponse>('/api/Auth/customer/resend-otp', { email });
}

export async function forgotPassword(email: string) {
  return api.post<MessageResponse>('/api/Auth/forgot-password', { email });
}

export async function resetPassword(payload: ResetPasswordRequest) {
  return api.post<MessageResponse>('/api/Auth/reset-password', payload);
}
export async function createEstimateQuotation(orderId: string) {
  return api.post<QuotationDataResponse>(`/api/orders/${orderId}/quotation/estimate`);
}

export async function acceptQuotation(quotationId: string) {
  return api.put<AcceptQuotationDataResponse>(`/api/quotations/${quotationId}/accept`);
}

export async function updateConsignmentStatus(orderId: string, payload: UpdateConsignmentStatusRequest) {
  return api.put<UpdateConsignmentStatusResponse>(`/api/orders/consignments/${orderId}/status`, payload);
}

export async function getConsignments(params?: { pageNumber?: number, pageSize?: number, status?: string, searchCode?: string }) {
  return api.get<ConsignmentPaginatedResponse>('/api/orders/consignments', { params });
}

export async function getConsignmentDetail(orderId: string) {
  return api.get<ConsignmentDetailDataResponse>(`/api/orders/consignments/${orderId}`);
}

export async function cancelConsignment(orderId: string, payload: CancelConsignmentRequest) {
  return api.put<CancelConsignmentResponse>(`/api/orders/consignments/${orderId}/cancel`, payload);
}

export async function getRestrictedItems() {
  return api.get<RestrictedItemDto[]>('/api/restricted-items');
}

export async function getRestrictedItemById(id: string) {
  return api.get<RestrictedItemDto>(`/api/restricted-items/${id}`);
}

export async function createRestrictedItem(payload: CreateRestrictedItemRequest) {
  return api.post<RestrictedItemDto>('/api/restricted-items', payload);
}

export async function updateRestrictedItem(id: string, payload: UpdateRestrictedItemRequest) {
  return api.put<MessageResponse>(`/api/restricted-items/${id}`, payload);
}

export async function deleteRestrictedItem(id: string) {
  return api.delete<MessageResponse>(`/api/restricted-items/${id}`);
}

export async function getPricingRules() {
  return api.get<PricingRuleDto[]>('/api/pricing-rules');
}

export async function getPricingRuleById(id: string) {
  return api.get<PricingRuleDto>(`/api/pricing-rules/${id}`);
}

export async function createPricingRule(payload: CreatePricingRuleRequest) {
  return api.post<PricingRuleDto>('/api/pricing-rules', payload);
}

export async function updatePricingRule(id: string, payload: UpdatePricingRuleRequest) {
  return api.put<MessageResponse>(`/api/pricing-rules/${id}`, payload);
}

export async function deletePricingRule(id: string) {
  return api.delete<MessageResponse>(`/api/pricing-rules/${id}`);
}

export async function checkInParcel(payload: ParcelCheckInRequest) {
  return api.post<ParcelCheckInDataResponse>('/api/parcels/check-in', payload);
}

export async function getPendingPutawayParcels(
  pageNumber: number = 1,
  pageSize: number = 10,
  search?: string,
  status?: string
) {
  return api.get<PendingPutawayParcelsDataResponse>('/api/parcels/pending-putaway', {
    params: { pageNumber, pageSize, search, status }
  });
}

export interface ParcelPutAwayRequest {
  warehouseId: string;
  zoneId: string;
  shelfId: string;
  binId: string;
}

export interface ParcelPutAwayResponse {
  packageCode: string;
  warehouseName: string;
  zoneName: string;
  shelfCode: string;
  binCode: string;
  packageStatus: string;
  putAwayTime: string;
}

export interface ParcelPutAwayDataResponse {
  message: string;
  data: ParcelPutAwayResponse;
}

export async function putAwayParcel(parcelId: string, payload: ParcelPutAwayRequest) {
  return api.put<ParcelPutAwayDataResponse>(`/api/parcels/${parcelId}/put-away`, payload);
}

export async function createConsignmentByStaff(payload: CreateConsignmentByStaffRequest) {
  return api.post<CreateConsignmentDataResponse>('/api/staff/consignments', payload);
}

export async function recordInventoryFromParcel(parcelId: string) {
  return api.post<InventoryDataResponse>(`/api/inventories/from-parcel/${parcelId}`);
}

export async function createWroRequest(payload: CreateWroRequest) {
  return api.post<WroDataResponse>('/api/warehouse-release-requests', payload);
}

export async function getWroRequestDetail(requestId: string) {
  return api.get<WroDetailDataResponse>(`/api/warehouse-release-requests/${requestId}`);
}

export async function updateWroStatus(requestId: string, payload: UpdateWroStatusRequest) {
  return api.put<UpdateWroStatusDataResponse>(`/api/warehouse-release-requests/${requestId}/status`, payload);
}

export async function createPickingList(requestId: string) {
  return api.post<PickingListDataResponse>(`/api/warehouse-release-requests/${requestId}/picking-list`);
}

export async function confirmPickingList(pickingListId: string, payload: ConfirmPickingRequest) {
  return api.put<ConfirmPickingDataResponse>(`/api/picking-lists/${pickingListId}/confirm`, payload);
}

export async function completeWroRequest(requestId: string, payload: CompleteWroRequest) {
  return api.put<CompleteWroDataResponse>(`/api/warehouse-release-requests/${requestId}/complete`, payload);
}

export async function getWarehouses(isActive?: boolean, search?: string) {
  return api.get<WarehouseListResponse>('/api/warehouses', { params: { isActive, search } });
}

export async function getActiveWarehouses() {
  return api.get<WarehouseListResponse>('/api/warehouses/active');
}

export async function createWarehouse(payload: CreateWarehouseRequest) {
  return api.post<WarehouseResponse>('/api/warehouses', payload);
}

export async function updateWarehouse(id: string, payload: CreateWarehouseRequest) {
  return api.put<WarehouseResponse>(`/api/warehouses/${id}`, payload);
}

export async function deleteWarehouse(id: string) {
  return api.delete<MessageResponse>(`/api/warehouses/${id}`);
}

export async function getWarehouseLocations(warehouseId: string, isActive?: boolean) {
  return api.get<WarehouseLocationListResponse>(`/api/warehouses/${warehouseId}/locations`, { params: { isActive } });
}

export async function createWarehouseLocation(warehouseId: string, payload: CreateLocationRequest) {
  return api.post<WarehouseLocationResponse>(`/api/warehouses/${warehouseId}/locations`, payload);
}

export async function updateWarehouseLocation(locationId: string, payload: CreateLocationRequest) {
  return api.put<WarehouseLocationResponse>(`/api/warehouse-locations/${locationId}`, payload);
}

export async function deleteWarehouseLocation(locationId: string) {
  return api.delete<MessageResponse>(`/api/warehouse-locations/${locationId}`);
}

export async function getActiveWarehouseLocations(warehouseId: string) {
  return api.get<WarehouseLocationListResponse>(`/api/warehouses/${warehouseId}/locations/active`);
}
```

## 24. Tạo yêu cầu xuất kho từ tồn kho (Warehouse Release Request - WRO)

Người dùng (Customer/Staff) tạo yêu cầu xuất kho từ hàng đã có tồn kho khả dụng (`AVAILABLE`).

```http
POST /api/warehouse-release-requests
Authorization: Bearer {accessToken}
Content-Type: application/json

{
  "receiverName": "Nguyen Van A",
  "receiverPhone": "0987654321",
  "deliveryAddress": "123 Duong Le Loi, Q1, HCM",
  "shelfCode": "SHELF-01",
  "items": [
    {
      "inventoryId": "9b1deb4d-3b7d-4bad-9bdd-2b152d12ebcd",
      "quantity": 2
    }
  ]
}
```

**Response thành công (201 Created):**

```json
{
  "message": "Tạo yêu cầu xuất kho thành công.",
  "data": {
    "wroId": "e14b21a7-...",
    "wroCode": "WRO-20260629-8439",
    "customerId": "81a7b4c9-...",
    "receiverName": "Nguyen Van A",
    "receiverPhone": "0987654321",
    "deliveryAddress": "123 Duong Le Loi, Q1, HCM",
    "status": "PENDING_REVIEW",
    "shelfCode": "SHELF-01",
    "createdAt": "2026-06-29T09:21:00Z",
    "items": [
      {
        "itemId": "a1b2c3d4-...",
        "inventoryId": "d5e243d7-...", 
        "quantity": 2,
        "packageCode": "PCL-20260620103028-258201"
      }
    ]
  }
}
```

**Lưu ý nghiệp vụ:**
- Trạng thái yêu cầu ban đầu là `"PENDING_REVIEW"`.
- Số lượng hàng được xuất của tồn kho gốc bị trừ đi, và tạo ra bản ghi tồn kho mới có trạng thái `"RESERVED"` để giữ hàng cho yêu cầu này.
- **Customer** chỉ được xuất tồn kho thuộc sở hữu của mình tại **kho đích (Vietnam)**. Nếu cố xuất hàng kho quốc tế hoặc của khách khác, hệ thống trả `403 Forbidden`.
- **Staff/WarehouseStaff/Operations** có quyền xuất kho bất kỳ (kể cả kho quốc tế để điều phối hàng).

**Response lỗi:**

| Status | Trường hợp | Message |
|---|---|---|
| `400` | Số lượng xuất kho không hợp lệ | `"Số lượng xuất kho phải lớn hơn 0."` hoặc `"Số lượng xuất ({quantity}) vượt quá số lượng tồn kho khả dụng ({available})."` |
| `400` | Tồn kho không ở trạng thái AVAILABLE | `"Hàng tồn kho với ID {id} không khả dụng để xuất (Trạng thái hiện tại: RESERVED)."` |
| `403` | Khách hàng xuất hàng ở kho quốc tế | `"Khách hàng chỉ được phép tạo yêu cầu xuất kho tại kho đích (Vietnam). Kho hiện tại: {name}."` |
| `403` | Xuất hàng không thuộc sở hữu của mình | `"Bạn không có quyền tạo yêu cầu xuất kho cho tồn kho {id} do không thuộc sở hữu của bạn."` |
| `404` | Không tìm thấy Inventory ID | `"Không tìm thấy tồn kho với ID: {id}"` |

### 24.1. Xem chi tiết yêu cầu xuất kho

Backend cung cấp API để lấy chi tiết của một yêu cầu xuất kho (WRO). Staff/Operations xem được mọi yêu cầu, Customer chỉ xem được yêu cầu của chính mình.

```http
GET /api/warehouse-release-requests/{requestId}
Authorization: Bearer {accessToken}
```

**Parameters:**

| Name | Type | Description |
|---|---|---|
| `requestId` | `string (Guid)` | ID của yêu cầu xuất kho |

**Response thành công (200 OK):**

```json
{
  "message": "Lấy chi tiết yêu cầu xuất kho thành công.",
  "data": {
    "wroId": "e14b21a7-...",
    "wroCode": "WRO-20260629-8439",
    "createdByName": "Nguyen Van A",
    "createdByUserRole": "Customer",
    "warehouseName": "Kho Việt Nam HCMC",
    "customerId": "81a7b4c9-...",
    "customerName": "Nguyen Van A",
    "receiverName": "Nguyen Van A",
    "receiverPhone": "0987654321",
    "deliveryAddress": "123 Duong Le Loi, Q1, HCM",
    "status": "PENDING_REVIEW",
    "shelfCode": "SHELF-01",
    "note": null,
    "createdAt": "2026-06-29T09:21:00Z",
    "totalQuantity": 2,
    "items": [
      {
        "itemId": "a1b2c3d4-...",
        "inventoryId": "d5e243d7-...", 
        "quantity": 2,
        "packageCode": "PCL-20260620103028-258201",
        "productName": "Giày thể thao Nam",
        "binCode": "BIN-A-01"
      }
    ]
  }
}
```

**Response lỗi:**

| Status | Trường hợp | Message |
|---|---|---|
| `401` | Chưa đăng nhập hoặc token không hợp lệ | Tự động bởi middleware JWT |
| `403` | Không có quyền xem yêu cầu xuất kho này (Customer xem của người khác) | `"Bạn không có quyền xem yêu cầu xuất kho này."` |
| `404` | Không tìm thấy yêu cầu xuất kho | `"Không tìm thấy yêu cầu xuất kho với ID {requestId}"` |

### 24.2. Staff duyệt hoặc từ chối yêu cầu xuất kho

Nhân viên (Staff/Operations) duyệt (`APPROVED`) hoặc từ chối (`REJECTED`) yêu cầu xuất kho. Khi từ chối, bắt buộc phải cung cấp lý do từ chối.

```http
PUT /api/warehouse-release-requests/{requestId}/status
Authorization: Bearer {accessToken}
Content-Type: application/json
```

**Path Parameters:**

| Parameter | Kiểu | Mô tả |
|---|---|---|
| `requestId` | `Guid` | ID của yêu cầu xuất kho cần duyệt |

**Request Body:**

```json
{
  "status": "REJECTED",
  "rejectionReason": "Hàng hóa bị hư hỏng không đủ xuất kho"
}
```

| Field | Ràng buộc | Mô tả |
|---|---|---|
| `status` | Bắt buộc. Phải là `APPROVED` hoặc `REJECTED`. | Trạng thái mới của yêu cầu xuất kho |
| `rejectionReason` | Bắt buộc nếu trạng thái là `REJECTED`. | Lý do từ chối yêu cầu |

**Response thành công (200 OK):**

```json
{
  "message": "Cập nhật trạng thái yêu cầu xuất kho thành công.",
  "data": {
    "wroId": "e14b21a7-...",
    "wroCode": "WRO-20260629-8439",
    "status": "REJECTED"
  }
}
```

**Response lỗi:**

| Status | Trường hợp | Message |
|---|---|---|
| `400` | Trạng thái mới không hợp lệ hoặc thiếu lý do từ chối | `"Trạng thái mới không hợp lệ..."` hoặc `"Vui lòng cung cấp lý do từ chối (RejectionReason)."` |
| `400` | Yêu cầu đã duyệt/từ chối trước đó (không ở trạng thái PENDING_REVIEW) | `"Không thể cập nhật trạng thái yêu cầu xuất kho do yêu cầu đã ở trạng thái {status}"` |
| `401` | Chưa đăng nhập hoặc token không hợp lệ | Tự động bởi middleware JWT |
| `403` | Khách hàng thực hiện thao tác (chỉ Staff/Ops được thao tác) | `"Bạn không có quyền cập nhật trạng thái yêu cầu xuất kho."` |
| `404` | Không tìm thấy yêu cầu xuất kho | `"Không tìm thấy yêu cầu xuất kho với ID {requestId}"` |

### 24.3. Tạo phiếu picking cho yêu cầu xuất kho

Nhân viên (Staff/Operations) tạo phiếu picking từ yêu cầu xuất kho đã được duyệt (`APPROVED`). Trạng thái của yêu cầu xuất kho sẽ chuyển sang `PICKING`, và trạng thái ban đầu của phiếu picking là `PENDING`.

```http
POST /api/warehouse-release-requests/{requestId}/picking-list
Authorization: Bearer {accessToken}
```

**Path Parameters:**

| Parameter | Kiểu | Mô tả |
|---|---|---|
| `requestId` | `Guid` | ID của yêu cầu xuất kho cần tạo phiếu picking |

**Response thành công (201 Created):**

```json
{
  "message": "Tạo phiếu picking thành công.",
  "data": {
    "pickingListId": "b1a7d6c5-...",
    "wroRequestId": "e14b21a7-...",
    "wroCode": "WRO-20260629-8439",
    "customerId": "81a7b4c9-...",
    "customerName": "Nguyen Van Customer",
    "status": "PENDING",
    "createdBy": "d5e243d7-...",
    "createdByName": "Nguyen Van Staff",
    "createdAt": "2026-06-30T09:30:00Z",
    "items": [
      {
        "itemId": "c2b3a4d5-...",
        "inventoryId": "d5e243d7-...",
        "packageCode": "PCL-20260620103028-258201",
        "productName": "Giày thể thao Nam",
        "binCode": "BIN-A-01",
        "shelfCode": "SHELF-01",
        "quantity": 2
      }
    ]
  }
}
```

**Response lỗi:**

| Status | Trường hợp | Message |
|---|---|---|
| `400` | Yêu cầu xuất kho chưa được duyệt (không ở trạng thái `APPROVED`) | `"Chỉ có thể tạo phiếu picking cho yêu cầu xuất kho đã được duyệt (APPROVED)."` |
| `400` | Đã tồn tại phiếu picking cho yêu cầu này | `"Phiếu picking cho yêu cầu xuất kho này đã tồn tại."` |
| `401` | Chưa đăng nhập hoặc token không hợp lệ | Tự động bởi middleware JWT |
| `403` | Khách hàng thực hiện thao tác (chỉ Staff/Ops được thao tác) | `"Bạn không có quyền tạo phiếu picking."` |
| `404` | Không tìm thấy yêu cầu xuất kho | `"Không tìm thấy yêu cầu xuất kho với ID {requestId}"` |

---

### 24.4. Xác nhận picking hàng khỏi vị trí kho

Nhân viên kho (WarehouseStaff/Staff/Operations) xác nhận đã lấy hàng khỏi vị trí kho theo phiếu picking. Trạng thái của phiếu picking sẽ chuyển thành `PICKED`, và trạng thái của yêu cầu xuất kho tương ứng chuyển sang `PACKING`. Đồng thời, trạng thái của tồn kho được picking sẽ cập nhật sang `PICKED`.

```http
PUT /api/picking-lists/{pickingListId}/confirm
Authorization: Bearer {accessToken}
Content-Type: application/json

{
  "items": [
    {
      "inventoryId": "d5e243d7-...",
      "quantity": 2
    }
  ]
}
```

**Path Parameters:**

| Parameter | Kiểu | Mô tả |
|---|---|---|
| `pickingListId` | `Guid` | ID của phiếu picking cần xác nhận |

**Request Body (`ConfirmPickingRequest`):**

| Field | Kiểu | Mô tả |
|---|---|---|
| `items` | `ConfirmPickingItem[]` | Danh sách tồn kho đã thực tế picking |

**Response thành công (200 OK):**

```json
{
  "message": "Xác nhận picking thành công.",
  "data": {
    "pickingListId": "b1a7d6c5-...",
    "wroRequestId": "e14b21a7-...",
    "status": "PICKED",
    "confirmedBy": "d5e243d7-...",
    "confirmedByName": "Nguyen Van Warehouse Staff",
    "confirmedAt": "2026-07-02T13:30:00Z",
    "items": [
      {
        "inventoryId": "d5e243d7-...",
        "quantity": 2,
        "binId": "f3b4c5d6-...",
        "binCode": "BIN-A-01",
        "shelfCode": "SHELF-01"
      }
    ]
  }
}
```

**Response lỗi:**

| Status | Trường hợp | Message |
|---|---|---|
| `400` | Phiếu picking không ở trạng thái `PENDING` | `"Chỉ có thể xác nhận phiếu picking ở trạng thái PENDING. Trạng thái hiện tại: {status}"` |
| `400` | Số lượng picking không hợp lệ (âm hoặc vượt quá số lượng `RESERVED`) | `"Số lượng picking không hợp lệ..."` hoặc `"Số lượng picking ({qPick}) vượt quá số lượng đã RESERVED..."` |
| `400` | Tồn kho ID trong request không thuộc danh sách picking | `"Tồn kho ID {inventoryId} không thuộc danh sách hàng cần picking của phiếu này."` |
| `401` | Chưa đăng nhập hoặc token không hợp lệ | Tự động bởi middleware JWT |
| `403` | Khách hàng thực hiện thao tác (chỉ WarehouseStaff/Staff/Ops được thao tác) | `"Bạn không có quyền xác nhận phiếu picking."` |
| `404` | Không tìm thấy phiếu picking | `"Không tìm thấy phiếu picking với ID {pickingListId}"` |

---

### 24.5. Xác nhận đóng gói và hoàn tất xuất kho

Nhân viên kho (WarehouseStaff/Staff/Operations) xác nhận đóng gói và hoàn tất xuất kho sau khi hàng đã được picking. Trạng thái của phiếu picking tương ứng sẽ chuyển thành `RELEASED` hoặc `OUTBOUND` tùy loại hàng, và trạng thái của yêu cầu xuất kho tương ứng chuyển sang `RELEASED`.

```http
PUT /api/warehouse-release-requests/{requestId}/complete
Authorization: Bearer {accessToken}
Content-Type: application/json

{
  "items": [
    {
      "inventoryId": "d5e243d7-...",
      "quantity": 2
    }
  ]
}
```

**Path Parameters:**

| Parameter | Kiểu | Mô tả |
|---|---|---|
| `requestId` | `Guid` | ID của yêu cầu xuất kho cần hoàn tất |

**Request Body (`CompleteWroRequest`):**

| Field | Kiểu | Mô tả |
|---|---|---|
| `items` | `CompleteWroItem[]` | Danh sách hàng đã được đóng gói và xuất kho thực tế |

**Response thành công (200 OK):**

```json
{
  "message": "Hoàn tất xuất kho thành công.",
  "data": {
    "wroRequestId": "e14b21a7-...",
    "wroCode": "WRO-20260629-8439",
    "status": "RELEASED",
    "releasedBy": "d5e243d7-...",
    "releasedByName": "Nguyen Van Warehouse Staff",
    "releasedAt": "2026-07-02T13:50:00Z",
    "warehouseId": "a1b2c3d4-...",
    "warehouseName": "Kho Ha Noi",
    "items": [
      {
        "inventoryId": "d5e243d7-...",
        "quantity": 2,
        "status": "RELEASED"
      }
    ]
  }
}
```

**Response lỗi:**

| Status | Trường hợp | Message |
|---|---|---|
| `400` | Yêu cầu xuất kho đã hoàn tất trước đó | `"Yêu cầu xuất kho này đã hoàn tất xuất kho trước đó."` |
| `400` | Yêu cầu xuất kho chưa được picking (không ở trạng thái `PICKED`/`PACKING`) | `"Yêu cầu xuất kho chưa đủ điều kiện xuất kho. Trạng thái hiện tại: {status}"` |
| `400` | Số lượng đóng gói không hợp lệ hoặc vượt quá số lượng đã picking | `"Số lượng xuất kho không được nhỏ hơn 0."` hoặc `"Số lượng xuất ({qShipped}) vượt quá số lượng đã picking..."` |
| `400` | Tồn kho ID trong request không thuộc danh sách đã picking | `"Tồn kho ID {inventoryId} không nằm trong danh sách đã picking của yêu cầu xuất kho này."` |
| `401` | Chưa đăng nhập hoặc token không hợp lệ | Tự động bởi middleware JWT |
| `403` | Khách hàng thực hiện thao tác (chỉ WarehouseStaff/Staff/Ops được thao tác) | `"Bạn không có quyền hoàn tất xuất kho."` |
| `404` | Không tìm thấy yêu cầu xuất kho | `"Không tìm thấy yêu cầu xuất kho với ID {requestId}"` |

---

## 25. Quản lý bảng giá dịch vụ chính (Service Pricing)

Admin hệ thống quản lý bảng giá cước vận chuyển chính chặng quốc tế cho các tuyến đường (route).

### 25.1. Xem danh sách bảng giá dịch vụ chính

```http
GET /api/service-pricings
```

**Response thành công (200 OK):**

```json
[
  {
    "id": "e3b0c442-...",
    "carrierId": "f4c1d2e3-...",
    "serviceType": "Consolidation",
    "originCountry": "INDO",
    "destinationCountry": "VN",
    "unitType": "KG",
    "price": 45000.0,
    "currency": "VND",
    "effectiveDate": "2026-06-01T00:00:00Z"
  }
]
```

### 25.2. Lấy chi tiết bảng giá dịch vụ theo ID

```http
GET /api/service-pricings/{id}
```

**Response thành công (200 OK):**

```json
{
  "id": "e3b0c442-...",
  "carrierId": "f4c1d2e3-...",
  "serviceType": "Consolidation",
  "originCountry": "INDO",
  "destinationCountry": "VN",
  "unitType": "KG",
  "price": 45000.0,
  "currency": "VND",
  "effectiveDate": "2026-06-01T00:00:00Z"
}
```

### 25.3. Thêm cấu hình bảng giá dịch vụ mới (Admin)

```http
POST /api/service-pricings
Authorization: Bearer {accessToken}
```

**Body Request:**

```json
{
  "carrierId": "f4c1d2e3-...",
  "serviceType": "Consolidation",
  "originCountry": "INDO",
  "destinationCountry": "VN",
  "unitType": "KG",
  "price": 45000.0,
  "currency": "VND",
  "effectiveDate": "2026-06-01T00:00:00Z"
}
```

**Response thành công (201 Created):** Trả về đối tượng vừa tạo kèm header `Location`.

### 25.4. Cập nhật cấu hình bảng giá dịch vụ (Admin)

```http
PUT /api/service-pricings/{id}
Authorization: Bearer {accessToken}
```

**Body Request:**

```json
{
  "carrierId": "f4c1d2e3-...",
  "serviceType": "Consolidation",
  "originCountry": "INDO",
  "destinationCountry": "VN",
  "unitType": "KG",
  "price": 48000.0,
  "currency": "VND",
  "effectiveDate": "2026-07-01T00:00:00Z"
}
```

**Response thành công (200 OK):**

```json
{
  "message": "Cập nhật bảng giá dịch vụ thành công."
}
```

### 25.5. Xóa cấu hình bảng giá dịch vụ (Admin)

```http
DELETE /api/service-pricings/{id}
Authorization: Bearer {accessToken}
```

**Response thành công (200 OK):**

```json
{
  "message": "Xóa bảng giá dịch vụ thành công."
}
```

---

## 26. Quản lý đơn vị vận chuyển (Carrier Management)

Admin quản lý danh sách các đơn vị vận chuyển hoặc forwarder được sử dụng trong hệ thống. Dữ liệu này dùng cho Staff hoặc Operations chọn khi tạo lô vận chuyển quốc tế, cập nhật trạng thái vận chuyển hoặc ghi nhận thông tin vận chuyển.

### 26.1. Lấy danh sách tất cả các đơn vị vận chuyển (Admin only)

```http
GET /api/carriers
Authorization: Bearer {accessToken}
```

**Response thành công (200 OK):**

```json
[
  {
    "id": "f4c1d2e3-5717-4562-b3fc-2c963f66afa6",
    "carrierName": "Viettel Post",
    "carrierCode": "VIETTEL_POST",
    "carrierType": "COURIER",
    "apiUrl": "https://api.viettelpost.com.vn",
    "contactEmail": "support@viettelpost.com.vn",
    "contactPhone": "19008095",
    "supportedShippingMethods": "Hỏa tốc, Tiết kiệm, Đường bộ",
    "supportedRegions": "Vietnam, Domestic",
    "isActive": true,
    "internalNotes": "Đối tác vận chuyển chính nội địa"
  }
]
```

### 26.2. Lấy danh sách các đơn vị vận chuyển đang hoạt động (Nghiệp vụ)

Tất cả các tài khoản nhân viên (Admin, Staff, Operations, WarehouseStaff) đều có thể gọi để chọn đơn vị vận chuyển khi tạo lô hoặc cập nhật trạng thái.

```http
GET /api/carriers/active
Authorization: Bearer {accessToken}
```

**Response tương tự như GET /api/carriers nhưng chỉ lọc các bản ghi có `isActive = true`.**

### 26.3. Lấy thông tin chi tiết một đơn vị vận chuyển theo ID (Admin only)

```http
GET /api/carriers/{id}
Authorization: Bearer {accessToken}
```

### 26.4. Tạo mới một đơn vị vận chuyển (Admin only)

```http
POST /api/carriers
Authorization: Bearer {accessToken}
Content-Type: application/json

{
  "carrierName": "DHL Express",
  "carrierCode": "DHL_EXPRESS",
  "carrierType": "AIR",
  "apiUrl": "https://api.dhl.com",
  "contactEmail": "contact@dhl.com",
  "contactPhone": "18001530",
  "supportedShippingMethods": "Express, Air Cargo",
  "supportedRegions": "Global, Vietnam, Japan, Korea",
  "isActive": true,
  "internalNotes": "Vận chuyển quốc tế đường hàng không"
}
```

**Ràng buộc:**
- `carrierName`, `carrierCode`, `carrierType` là bắt buộc.
- `carrierCode` không được chứa ký tự đặc biệt ngoài chữ, số, gạch dưới và gạch ngang. Nếu trùng lặp với `carrierCode` hiện có sẽ trả về `400 Bad Request`.

**Response thành công (201 Created):** Trả về đối tượng vừa tạo kèm ID và Header `Location`.

### 26.5. Cập nhật thông tin đơn vị vận chuyển (Admin only)

```http
PUT /api/carriers/{id}
Authorization: Bearer {accessToken}
Content-Type: application/json

{
  "carrierName": "DHL Express VN",
  "carrierCode": "DHL_EXPRESS",
  "carrierType": "AIR",
  "apiUrl": "https://api.dhl.com.vn",
  "contactEmail": "support.vn@dhl.com",
  "contactPhone": "18001530",
  "supportedShippingMethods": "Express, Air Cargo",
  "supportedRegions": "Global, Vietnam, Japan, Korea",
  "isActive": false,
  "internalNotes": "Tạm dừng hoạt động để bảo trì hệ thống API"
}
```

**Response thành công (200 OK):**

```json
{
  "message": "Cập nhật đơn vị vận chuyển thành công."
}
```

### 26.6. Xóa đơn vị vận chuyển (Admin only)

Xóa hoàn toàn một đơn vị vận chuyển khỏi hệ thống nếu không có ràng buộc dữ liệu lịch sử.

```http
DELETE /api/carriers/{id}
Authorization: Bearer {accessToken}
```

**Response thành công (200 OK):**

```json
{
  "message": "Xóa đơn vị vận chuyển thành công."
}
```

#### TypeScript Interfaces:

```typescript
interface CarrierDto {
  id: string;
  carrierName: string;
  carrierCode: string;
  carrierType: string;
  apiUrl?: string | null;
  contactEmail?: string | null;
  contactPhone?: string | null;
  supportedShippingMethods?: string | null;
  supportedRegions?: string | null;
  isActive: boolean;
  internalNotes?: string | null;
}

interface CreateCarrierRequestDto {
  carrierName: string;
  carrierCode: string;
  carrierType: string;
  apiUrl?: string | null;
  contactEmail?: string | null;
  contactPhone?: string | null;
  supportedShippingMethods?: string | null;
  supportedRegions?: string | null;
  isActive: boolean;
  internalNotes?: string | null;
}

interface UpdateCarrierRequestDto {
  carrierName: string;
  carrierCode: string;
  carrierType: string;
  apiUrl?: string | null;
  contactEmail?: string | null;
  contactPhone?: string | null;
  supportedShippingMethods?: string | null;
  supportedRegions?: string | null;
  isActive: boolean;
  internalNotes?: string | null;
}
```

---

## 27. Quản lý kho và vị trí lưu trữ (Warehouse & Location Management)

Admin quản lý các kho vận hành, thiết lập Zone/Shelf/Bin trong kho. Cung cấp API lấy danh sách kho/vị trí hoạt động (active) cho các nghiệp vụ check-in, put-away, picking, xuất kho và tạo lô vận chuyển.

### 27.1. Lấy danh sách tất cả các kho vận hành (Admin)

```http
GET /api/warehouses
Authorization: Bearer {adminToken}
```

**Response thành công (200 OK):**

```json
{
  "items": [
    {
      "id": "a1b2c3d4-...",
      "name": "Kho Hà Nội",
      "code": "WH-HAN",
      "address": "Số 1 Đại Cồ Việt, Hà Nội",
      "warehouseType": "DOMESTIC",
      "isActive": true
    }
  ]
}
```

### 27.2. Lấy danh sách kho đang hoạt động (Nghiệp vụ)

Tất cả các tài khoản nhân viên (Admin, Sale, Staff, WarehouseStaff, Operations) đều có thể gọi để chọn kho đi, kho đến.

```http
GET /api/warehouses/active
Authorization: Bearer {accessToken}
```

### 27.3. Tạo kho mới (Admin)

```http
POST /api/warehouses
Authorization: Bearer {adminToken}
Content-Type: application/json

{
  "name": "Kho Hải Phòng",
  "code": "WH-HPH",
  "address": "Cảng Hải Phòng",
  "warehouseType": "DOMESTIC",
  "isActive": true
}
```

**Ràng buộc:** Không cho tạo trùng mã kho (`code`). Nếu trùng trả về `400 Bad Request`.

### 27.4. Cập nhật thông tin kho (Admin)

```http
PUT /api/warehouses/{warehouseId}
Authorization: Bearer {adminToken}
Content-Type: application/json

{
  "name": "Kho Hải Phòng Cập Nhật",
  "code": "WH-HPH",
  "address": "Cảng Hải Phòng mới",
  "warehouseType": "DOMESTIC",
  "isActive": false
}
```

**Ràng buộc:**
- Không cho cập nhật trùng mã kho của kho khác.
- Không cho vô hiệu hóa (`isActive = false`) nếu kho đó đang chứa hàng hóa lưu trữ có số lượng > 0, hoặc có yêu cầu xuất kho đang xử lý, hoặc có phiếu tiếp nhận hàng chưa hoàn tất. Trả về `400 Bad Request`.

### 27.5. Xóa kho (Admin)

```http
DELETE /api/warehouses/{warehouseId}
Authorization: Bearer {adminToken}
```

**Ràng buộc:** Không cho xóa kho đã có lịch sử kiện hàng hoặc lịch sử tồn kho. Trả về `400 Bad Request`.

### 27.6. Lấy danh sách vị trí lưu trữ trong kho (Admin)

```http
GET /api/warehouses/{warehouseId}/locations
Authorization: Bearer {adminToken}
```

**Response thành công (200 OK):**

```json
{
  "items": [
    {
      "locationId": "b1c2d3e4-...",
      "binCode": "BIN-A-01",
      "shelfId": "c1d2e3f4-...",
      "shelfCode": "SHELF-A",
      "zoneId": "d1e2f3a4-...",
      "zoneName": "Zone A",
      "warehouseId": "a1b2c3d4-...",
      "warehouseName": "Kho Hà Nội",
      "maxVolume": 100.00,
      "maxWeight": 500.00,
      "isActive": true,
      "note": "Vị trí hàng thường"
    }
  ]
}
```

### 27.7. Tạo vị trí lưu trữ mới (Admin)

```http
POST /api/warehouses/{warehouseId}/locations
Authorization: Bearer {adminToken}
Content-Type: application/json

{
  "zoneName": "Zone A",
  "shelfCode": "SHELF-A",
  "binCode": "BIN-A-01",
  "maxVolume": 100,
  "maxWeight": 500,
  "isActive": true,
  "note": "Kệ A1 hàng dễ vỡ"
}
```

**Ràng buộc:**
- Tự động tìm hoặc tạo Zone/Shelf theo tên/mã gửi lên.
- Không cho tạo trùng mã vị trí lưu trữ (`binCode`) trong cùng một kho. Nếu trùng trả về `400 Bad Request`.

### 27.8. Cập nhật vị trí lưu trữ (Admin)

```http
PUT /api/warehouse-locations/{locationId}
Authorization: Bearer {adminToken}
Content-Type: application/json

{
  "zoneName": "Zone A",
  "shelfCode": "SHELF-A",
  "binCode": "BIN-A-01-EDITED",
  "maxVolume": 150,
  "maxWeight": 600,
  "isActive": false,
  "note": "Đang bảo trì"
}
```

**Ràng buộc:**
- Không cho cập nhật trùng mã vị trí lưu trữ trong cùng một kho.
- Không cho vô hiệu hóa (`isActive = false`) nếu vị trí đó đang chứa hàng hóa khả dụng (`Inventory.Quantity > 0`) hoặc đang liên kết với kiện hàng. Trả về `400 Bad Request`.

### 27.9. Xóa vị trí lưu trữ (Admin)

```http
DELETE /api/warehouse-locations/{locationId}
Authorization: Bearer {adminToken}
```

**Ràng buộc:** Không cho xóa vị trí đang chứa hàng hoặc liên kết với kiện hàng. Trả về `400 Bad Request`.

### 27.10. Lấy danh sách vị trí đang hoạt động trong kho (Nghiệp vụ)

Tất cả các tài khoản nhân viên đều có thể gọi khi thực hiện put-away hoặc picking.

```http
GET /api/warehouses/{warehouseId}/locations/active
Authorization: Bearer {accessToken}
```

---

## 28. Gom hàng và đóng bao (Consolidation - MasterBox)

Tài khoản nhân viên (Staff, WarehouseStaff, Admin, OperationsManager) sử dụng các API này để thực hiện quy trình gom nhiều đơn hàng ký gửi của khách hàng vào một MasterBox (mã bao hàng gom chung) trước khi làm thủ tục xuất khẩu hoặc vận chuyển xuyên biên giới.

### 28.1. Tạo lô gom hàng mới

Gom hàng bằng cách truyền lên danh sách các Order ID của các đơn hàng. Hệ thống sẽ tự động tìm kiếm các kiện hàng (Parcels) tương ứng của các đơn hàng này, kiểm tra tính hợp lệ và tự động sinh ra một mã `MasterCode` duy nhất định dạng `MBX-yyyyMMddHHmmss-XXXXXX`.

```http
POST /api/consolidation
Authorization: Bearer {accessToken}
Content-Type: application/json

{
  "orderIds": [
    "c5b2a472-87db-4ce5-be8d-192a4e21a37c",
    "b8c3d9a1-02cf-4b72-a1f9-90bde6ef76a8"
  ],
  "status": "CONSOLIDATED"
}
```

**Ràng buộc:**
- Danh sách `orderIds` phải chứa ít nhất 1 Order ID hợp lệ.
- Tất cả các đơn hàng được chọn phải tồn tại và có chứa ít nhất 1 kiện hàng (`Parcel`).
- Không cho phép gom những kiện hàng đã được gom ở một MasterBox khác (tránh trùng lặp). Nếu phát hiện trùng sẽ trả về `400 Bad Request`.
- Hệ thống tự động sinh `MasterCode` duy nhất định dạng `MBX-yyyyMMddHHmmss-XXXXXX`.

**Response thành công (201 Created):**
Trả về chi tiết MasterBox đã tạo cùng danh sách đơn hàng và kiện hàng được liên kết:

```json
{
  "id": "e2a3b4c5-5717-4562-b3fc-2c963f66afa6",
  "masterCode": "MBX-SPECIAL-001",
  "totalWeight": 15.50,
  "totalVolume": 0.085000,
  "status": "CONSOLIDATED",
  "shipmentId": null,
  "orders": [
    {
      "id": "c5b2a472-87db-4ce5-be8d-192a4e21a37c",
      "consignmentCode": "VCL-20260703-982132",
      "status": "APPROVED",
      "route": "TQ-VN",
      "parcels": [
        {
          "id": "a1b2c3d4-e5f6-7a8b-9c0d-1e2f3a4b5c6d",
          "packageCode": "PKG0001",
          "actualWeight": 7.50,
          "length": 50.00,
          "width": 30.00,
          "height": 20.00,
          "volumetricWeight": 6.00,
          "chargeableWeight": 7.50,
          "packageStatus": "CHECKED_IN"
        }
      ]
    }
  ]
}
```

### 28.2. Lấy chi tiết lô gom hàng theo ID

```http
GET /api/consolidation/{id}
Authorization: Bearer {accessToken}
```

### 28.3. Lấy danh sách các lô gom hàng

```http
GET /api/consolidation?status=CONSOLIDATED&search=MBX-SPECIAL
Authorization: Bearer {accessToken}
```

**Query Parameters:**
- `status`: Lọc theo trạng thái của MasterBox (ví dụ: `CONSOLIDATED`, `IN_TRANSIT`, v.v.).
- `search`: Tìm kiếm tương đối theo mã `MasterCode`.

### 28.4. Cập nhật lô gom hàng

Cho phép thay đổi mã bao hàng, trạng thái vận chuyển, hoặc cập nhật lại danh sách các đơn hàng nằm trong bao gom hàng đó. Khi cập nhật danh sách đơn hàng mới, các kiện hàng cũ không nằm trong danh sách mới sẽ được gỡ bỏ khỏi MasterBox (`masterBoxId = null`).

```http
PUT /api/consolidation/{id}
Authorization: Bearer {accessToken}
Content-Type: application/json

{
  "masterCode": "MBX-SPECIAL-001-EDITED",
  "status": "IN_TRANSIT",
  "shipmentId": "d1e2f3a4-5717-4562-b3fc-2c963f66afa6",
  "orderIds": [
    "c5b2a472-87db-4ce5-be8d-192a4e21a37c"
  ]
}
```

### 28.5. Hủy gom hàng (Xóa MasterBox)

Khi thực hiện xóa MasterBox, hệ thống sẽ tự động giải tán tất cả các kiện hàng đang liên kết với nó (đặt `MasterBoxId = null`) để trở lại trạng thái tự do chờ gom lô mới.

```http
DELETE /api/consolidation/{id}
Authorization: Bearer {accessToken}
```

---

## 29. Tạo yêu cầu mua hộ (Purchase Request)

Customer tạo yêu cầu mua hộ hàng Trung Quốc. Khách nhập thông tin nhận hàng + danh sách sản phẩm cần mua; hệ thống ghi nhận yêu cầu ở trạng thái `PENDING_REVIEW` để Staff/Sales tiếp nhận và xử lý báo giá ở bước sau.

> **Về 2 loại ghi chú:** `generalNote` là ghi chú chung cho cả yêu cầu; `items[].note` là ghi chú riêng cho từng sản phẩm — hai trường khác mục đích, không trùng nhau.

```http
POST /api/purchase-requests
Authorization: Bearer {accessToken}
Content-Type: application/json

{
  "route": "TQ-VN",
  "receiverName": "Nguyễn Văn A",
  "receiverPhone": "0901234567",
  "receiverAddress": "123 Lê Lợi, Q1, TP.HCM",
  "requiresInspection": true,
  "requiresQuantityCheck": true,
  "generalNote": "Mua giúp mình đợt sale, ưu tiên shop uy tín",
  "items": [
    {
      "productLink": "https://item.taobao.com/item.htm?id=123456",
      "sourceWebsite": "Taobao",
      "productType": "Electronics",
      "productName": "Áo khoác nam",
      "quantity": 2,
      "attributes": "Size L, màu đen",
      "note": "Chọn loại vải dày",
      "imageUrl": "https://res.cloudinary.com/.../ref.jpg"
    }
  ]
}
```

**Ràng buộc dữ liệu:**

| Trường | Bắt buộc | Mô tả |
|---|---|---|
| `route` | Không | Tuyến hàng (dropdown `/api/orders/consignments/routes`) |
| `receiverName` | Không | Tên người nhận |
| `receiverPhone` | Không | SĐT người nhận |
| `receiverAddress` | Không | Địa chỉ nhận hàng |
| `requiresInspection` | Không | Yêu cầu kiểm tra hàng (mặc định `false`) |
| `requiresQuantityCheck` | Không | Yêu cầu kiểm tra số lượng (mặc định `false`) |
| `generalNote` | Không | Ghi chú mua hộ chung cho cả yêu cầu |
| `items` | **Có** | Danh sách sản phẩm, tối thiểu 1 phần tử |
| `items[].productLink` | **Có** | Link sản phẩm cần mua |
| `items[].sourceWebsite` | Không | Website/sàn nguồn (Taobao, 1688, Tmall...) |
| `items[].productType` | Không | Loại hàng (dropdown `/api/product-types`) |
| `items[].productName` | Không | Tên sản phẩm (nếu có) |
| `items[].quantity` | **Có** | Số lượng, phải `>= 1` |
| `items[].attributes` | Không | Thuộc tính như size, màu, mẫu mã |
| `items[].note` | Không | Ghi chú riêng cho từng sản phẩm |
| `items[].imageUrl` | Không | Hình ảnh tham khảo |

**Response thành công (201 Created):**

```json
{
  "message": "Tạo yêu cầu mua hộ thành công.",
  "data": {
    "purchaseRequestId": "e14b21a7-...",
    "purchaseCode": "PUR-20260704-8439",
    "customerId": "81a7b4c9-...",
    "route": "TQ-VN",
    "receiverName": "Nguyễn Văn A",
    "receiverPhone": "0901234567",
    "receiverAddress": "123 Lê Lợi, Q1, TP.HCM",
    "requiresInspection": true,
    "requiresQuantityCheck": true,
    "generalNote": "Mua giúp mình đợt sale, ưu tiên shop uy tín",
    "status": "PENDING_REVIEW",
    "createdAt": "2026-07-04T09:21:00Z",
    "items": [
      {
        "itemId": "a1b2c3d4-...",
        "productLink": "https://item.taobao.com/item.htm?id=123456",
        "sourceWebsite": "Taobao",
        "productType": "Electronics",
        "productName": "Áo khoác nam",
        "quantity": 2,
        "attributes": "Size L, màu đen",
        "note": "Chọn loại vải dày",
        "imageUrl": "https://res.cloudinary.com/.../ref.jpg"
      }
    ]
  }
}
```

**Lưu ý nghiệp vụ:**
- Trạng thái yêu cầu ban đầu luôn là `"PENDING_REVIEW"`.
- Mã yêu cầu `purchaseCode` được sinh tự động theo định dạng `PUR-yyyyMMdd-xxxx`.
- Chỉ tài khoản **Customer** mới được tạo yêu cầu mua hộ. Không xử lý bước nhà cung cấp chuẩn bị hàng ở API này.

**Response lỗi:**

| Status | Trường hợp | Message |
|---|---|---|
| `400` | Thiếu link sản phẩm | `"Link sản phẩm không được để trống."` |
| `400` | Số lượng không hợp lệ | `"Số lượng sản phẩm phải lớn hơn 0."` |
| `400` | Danh sách sản phẩm rỗng | `"Danh sách sản phẩm cần mua hộ phải có ít nhất một sản phẩm."` |
| `401` | Chưa đăng nhập | (JWT thiếu/không hợp lệ) |
| `403` | Không phải Customer | `"Chỉ khách hàng mới được tạo yêu cầu mua hộ."` |

**TypeScript interface (khớp DTO C#):**

```typescript
interface CreatePurchaseRequestItemDto {
  productLink: string;
  sourceWebsite?: string | null;
  productType?: string | null;
  productName?: string | null;
  quantity: number;
  attributes?: string | null;
  note?: string | null;
  imageUrl?: string | null;
}

interface CreatePurchaseRequestDto {
  route?: string | null;
  receiverName?: string | null;
  receiverPhone?: string | null;
  receiverAddress?: string | null;
  requiresInspection: boolean;
  requiresQuantityCheck: boolean;
  generalNote?: string | null;
  items: CreatePurchaseRequestItemDto[];
}

interface PurchaseRequestItemResponseDto {
  itemId: string;
  productLink: string;
  sourceWebsite?: string | null;
  productType?: string | null;
  productName?: string | null;
  quantity: number;
  attributes?: string | null;
  note?: string | null;
  imageUrl?: string | null;
}

interface PurchaseRequestResponseDto {
  purchaseRequestId: string;
  purchaseCode: string;
  customerId?: string | null;
  route?: string | null;
  receiverName?: string | null;
  receiverPhone?: string | null;
  receiverAddress?: string | null;
  requiresInspection: boolean;
  requiresQuantityCheck: boolean;
  generalNote?: string | null;
  status: string;
  createdAt: string;
  items: PurchaseRequestItemResponseDto[];
}
```

### 29.1. Lấy danh sách yêu cầu mua hộ

Lấy danh sách yêu cầu mua hộ có phân trang. **Customer** chỉ thấy yêu cầu của chính mình; **Staff/Sales/Admin/Operations** thấy toàn bộ yêu cầu cần xử lý. Yêu cầu ở trạng thái `PENDING_REVIEW` được ưu tiên hiển thị lên đầu.

```http
GET /api/purchase-requests?pageNumber=1&pageSize=10&status=PENDING_REVIEW&searchKeyword=PUR-2026
Authorization: Bearer {accessToken}
```

**Query params (đều tùy chọn):**

| Name | Type | Default | Mô tả |
|---|---|---|---|
| `pageNumber` | `number` | `1` | Trang hiện tại (min 1) |
| `pageSize` | `number` | `10` | Số bản ghi mỗi trang (min 1) |
| `status` | `string` | — | Lọc theo trạng thái, ví dụ `PENDING_REVIEW` |
| `searchKeyword` | `string` | — | Tìm theo mã yêu cầu (`purchaseCode`) hoặc tên Customer |

**Response thành công (200 OK):**

```json
{
  "message": "Lấy danh sách yêu cầu mua hộ thành công.",
  "data": {
    "items": [
      {
        "purchaseRequestId": "b037ab62-...",
        "purchaseCode": "PUR-20260704-7582",
        "customerId": "1b53ba9c-...",
        "receiverName": "Nguyễn Văn A",
        "itemCount": 2,
        "status": "PENDING_REVIEW",
        "generalNote": "Mua giúp mình đợt sale",
        "createdAt": "2026-07-04T04:18:52Z",
        "items": [
          { "productName": "Áo khoác nam", "quantity": 2 },
          { "productName": null, "quantity": 5 }
        ]
      }
    ],
    "totalCount": 3,
    "pageNumber": 1,
    "pageSize": 10,
    "totalPages": 1
  }
}
```

**Lưu ý nghiệp vụ:**
- Ưu tiên hiển thị yêu cầu `PENDING_REVIEW` lên đầu, sau đó sắp xếp theo `createdAt` mới nhất.
- Trả về `items: []` với `totalCount: 0` nếu chưa có yêu cầu nào (không phải lỗi).
- Trả `401` nếu chưa đăng nhập.

**TypeScript interface (khớp DTO C#):**

```typescript
interface PurchaseRequestListProductDto {
  productName?: string | null;
  quantity: number;
}

interface PurchaseRequestListItemDto {
  purchaseRequestId: string;
  purchaseCode: string;
  customerId?: string | null;
  receiverName: string;
  itemCount: number;
  status: string;
  generalNote?: string | null;
  createdAt: string;
  items: PurchaseRequestListProductDto[];
}

interface PaginatedResponse<T> {
  items: T[];
  totalCount: number;
  pageNumber: number;
  pageSize: number;
  totalPages: number;
}
```

### 29.2. Lấy chi tiết yêu cầu mua hộ

Lấy chi tiết đầy đủ một yêu cầu mua hộ theo `requestId`. **Customer** chỉ xem được yêu cầu của chính mình; **Staff/Sales/Admin/Operations** xem được mọi yêu cầu.

```http
GET /api/purchase-requests/{requestId}
Authorization: Bearer {accessToken}
```

**Response thành công (200 OK):**

```json
{
  "message": "Lấy chi tiết yêu cầu mua hộ thành công.",
  "data": {
    "purchaseRequestId": "97e4fd25-...",
    "purchaseCode": "PUR-20260704-3113",
    "customerId": "1b53ba9c-...",
    "customerName": "Bùi Đình Trọng",
    "createdByName": "Bùi Đình Trọng",
    "route": "TQ-VN",
    "receiverName": "Nguyễn Văn A",
    "receiverPhone": "0901234567",
    "receiverAddress": "123 Lê Lợi, Q1, TP.HCM",
    "requiresInspection": true,
    "requiresQuantityCheck": true,
    "generalNote": "Mua giúp mình đợt sale",
    "status": "PENDING_REVIEW",
    "reason": null,
    "createdAt": "2026-07-04T04:18:52Z",
    "totalQuantity": 7,
    "items": [
      {
        "itemId": "80d01761-...",
        "productLink": "https://item.taobao.com/item.htm?id=123456",
        "sourceWebsite": "Taobao",
        "productType": "Electronics",
        "productName": "Áo khoác nam",
        "quantity": 2,
        "attributes": "Size L, màu đen",
        "note": "Chọn loại vải dày",
        "imageUrl": "https://res.cloudinary.com/.../ref.jpg"
      }
    ]
  }
}
```

**Response lỗi:**

| Status | Trường hợp | Message |
|---|---|---|
| `401` | Chưa đăng nhập | (JWT thiếu/không hợp lệ) |
| `403` | Customer xem yêu cầu của người khác | `"Bạn không có quyền xem yêu cầu mua hộ này."` |
| `404` | Không tìm thấy yêu cầu | `"Không tìm thấy yêu cầu mua hộ với ID {requestId}"` |

**TypeScript interface (khớp DTO C#):**

```typescript
interface PurchaseRequestDetailResponseDto {
  purchaseRequestId: string;
  purchaseCode: string;
  customerId?: string | null;
  customerName: string;
  createdByName: string;
  route?: string | null;
  receiverName?: string | null;
  receiverPhone?: string | null;
  receiverAddress?: string | null;
  requiresInspection: boolean;
  requiresQuantityCheck: boolean;
  generalNote?: string | null;
  status: string;
  reason?: string | null;
  createdAt: string;
  totalQuantity: number;
  items: PurchaseRequestItemResponseDto[];
}
```

### 29.3. Staff cập nhật trạng thái yêu cầu mua hộ

Staff/Sales tiếp nhận và cập nhật trạng thái một yêu cầu mua hộ. Dùng để chuyển sang đang xử lý, yêu cầu khách bổ sung thông tin, hoặc từ chối.

```http
PUT /api/purchase-requests/{requestId}/status
Authorization: Bearer {accessToken}
Content-Type: application/json

{
  "status": "NEED_MORE_INFO",
  "reason": "Thiếu ảnh sản phẩm, vui lòng bổ sung"
}
```

**Ràng buộc:**

| Trường | Bắt buộc | Mô tả |
|---|---|---|
| `status` | **Có** | Một trong: `PENDING_REVIEW`, `IN_REVIEW`, `NEED_MORE_INFO`, `REJECTED` |
| `reason` | **Có** khi status là `NEED_MORE_INFO` hoặc `REJECTED` | Lý do để Customer xem lại |

**Response thành công (200 OK):**

```json
{
  "message": "Cập nhật trạng thái yêu cầu mua hộ thành công.",
  "data": {
    "purchaseRequestId": "b037ab62-...",
    "purchaseCode": "PUR-20260704-7582",
    "status": "NEED_MORE_INFO",
    "reason": "Thiếu ảnh sản phẩm, vui lòng bổ sung"
  }
}
```

**Lưu ý nghiệp vụ:**
- Chỉ **Staff/Sales** (Admin/Sale/Staff/Operations) được thao tác — Customer gọi trả `403`.
- **Không** cho cập nhật nếu yêu cầu đã ở `QUOTATION_SENT` (đã sang bước báo giá) hoặc `CANCELLED` (đã hủy).
- `reason` chỉ được lưu khi status là `NEED_MORE_INFO`/`REJECTED`; các trạng thái khác reason sẽ được xoá.
- Trường `reason` này trả về trong API chi tiết (mục 29.2) để Customer xem lại.

**Response lỗi:**

| Status | Trường hợp | Message |
|---|---|---|
| `400` | Trạng thái không hợp lệ | `"Trạng thái không hợp lệ. Hợp lệ: PENDING_REVIEW, IN_REVIEW, NEED_MORE_INFO, REJECTED."` |
| `400` | Thiếu lý do khi NEED_MORE_INFO/REJECTED | `"Vui lòng cung cấp lý do khi yêu cầu bổ sung thông tin hoặc từ chối."` |
| `400` | Yêu cầu đã sang báo giá/đã hủy | `"Không thể cập nhật trạng thái do yêu cầu đã ở trạng thái {status}."` |
| `403` | Không phải Staff/Sales | `"Chỉ Staff hoặc Sales được cập nhật trạng thái yêu cầu mua hộ."` |
| `404` | Không tìm thấy yêu cầu | `"Không tìm thấy yêu cầu mua hộ với ID {requestId}"` |

**TypeScript interface:**

```typescript
interface UpdatePurchaseRequestStatusRequestDto {
  status: "PENDING_REVIEW" | "IN_REVIEW" | "NEED_MORE_INFO" | "REJECTED";
  reason?: string | null;
}

interface UpdatePurchaseRequestStatusResponseDto {
  purchaseRequestId: string;
  purchaseCode: string;
  status: string;
  reason?: string | null;
}
```

### 29.4. Staff tạo báo giá mua hộ

Staff/Sales tạo báo giá cho một yêu cầu mua hộ. Mỗi sản phẩm nhập đơn giá (`unitPrice`), số lượng lấy theo yêu cầu; hệ thống tính `lineTotal = unitPrice × quantity`, `productsSubtotal = Σ lineTotal`, và `totalAmount = productsSubtotal + purchaseFee + shippingFee`.

```http
POST /api/purchase-requests/{requestId}/quotation
Authorization: Bearer {accessToken}
Content-Type: application/json

{
  "purchaseFee": 50000,
  "shippingFee": 120000,
  "note": "Báo giá đợt 1",
  "items": [
    { "purchaseRequestItemId": "0ebcd2a8-...", "unitPrice": 200000 },
    { "purchaseRequestItemId": "f7743c57-...", "unitPrice": 150000 }
  ]
}
```

**Ràng buộc:**

| Trường | Bắt buộc | Mô tả |
|---|---|---|
| `purchaseFee` | **Có** | Phí mua hộ (>= 0) |
| `shippingFee` | **Có** | Phí vận chuyển dự kiến (>= 0, có thể 0) |
| `note` | Không | Ghi chú báo giá |
| `items` | **Có** | Tối thiểu 1 sản phẩm |
| `items[].purchaseRequestItemId` | **Có** | Phải là item thuộc yêu cầu này |
| `items[].unitPrice` | **Có** | Đơn giá sản phẩm (>= 0) |

**Response thành công (201 Created):**

```json
{
  "message": "Tạo báo giá mua hộ thành công.",
  "data": {
    "quotationId": "9c1f...",
    "purchaseRequestId": "a5816037-...",
    "purchaseCode": "PUR-20260708-1234",
    "productsSubtotal": 900000,
    "purchaseFee": 50000,
    "shippingFee": 120000,
    "totalAmount": 1070000,
    "note": "Báo giá đợt 1",
    "status": "PENDING_CUSTOMER_CONFIRMATION",
    "createdAt": "2026-07-08T07:20:00Z",
    "items": [
      { "quotationItemId": "...", "purchaseRequestItemId": "0ebcd2a8-...", "productName": "Tai nghe", "unitPrice": 200000, "quantity": 3, "lineTotal": 600000 },
      { "quotationItemId": "...", "purchaseRequestItemId": "f7743c57-...", "productName": "Chuột", "unitPrice": 150000, "quantity": 2, "lineTotal": 300000 }
    ]
  }
}
```

**Lưu ý nghiệp vụ:**
- Chỉ tạo báo giá khi yêu cầu đang ở `PENDING_REVIEW`, `IN_REVIEW` hoặc `NEED_MORE_INFO`.
- Sau khi tạo: báo giá có status `PENDING_CUSTOMER_CONFIRMATION`, và yêu cầu mua hộ chuyển sang `QUOTED`.
- Không cho tạo trùng báo giá đang hiệu lực (một yêu cầu chỉ có một báo giá chờ khách xác nhận).
- Số lượng (`quantity`) luôn lấy theo item của yêu cầu, FE không cần gửi.

**Response lỗi:**

| Status | Trường hợp | Message |
|---|---|---|
| `400` | Trạng thái yêu cầu không cho báo giá / đã QUOTED | `"Không thể tạo báo giá do yêu cầu đang ở trạng thái {status}."` |
| `400` | Đã có báo giá chờ xác nhận | `"Yêu cầu này đã có báo giá đang chờ khách xác nhận."` |
| `400` | Item không thuộc yêu cầu | `"Sản phẩm {id} không thuộc yêu cầu mua hộ này."` |
| `403` | Không phải Staff/Sales | `"Chỉ Staff hoặc Sales được tạo báo giá mua hộ."` |
| `404` | Không tìm thấy yêu cầu | `"Không tìm thấy yêu cầu mua hộ với ID {requestId}"` |

**TypeScript interface:**

```typescript
interface CreatePurchaseQuotationItemDto {
  purchaseRequestItemId: string;
  unitPrice: number;
}

interface CreatePurchaseQuotationDto {
  purchaseFee: number;
  shippingFee: number;
  note?: string | null;
  items: CreatePurchaseQuotationItemDto[];
}

interface PurchaseQuotationItemResponseDto {
  quotationItemId: string;
  purchaseRequestItemId: string;
  productName?: string | null;
  unitPrice: number;
  quantity: number;
  lineTotal: number;
}

interface PurchaseQuotationResponseDto {
  quotationId: string;
  purchaseRequestId: string;
  purchaseCode: string;
  productsSubtotal: number;
  purchaseFee: number;
  shippingFee: number;
  totalAmount: number;
  note?: string | null;
  status: string;
  createdAt: string;
  items: PurchaseQuotationItemResponseDto[];
}
```

---

## 30. Dropdown dữ liệu cho form ký gửi (Consignment dropdowns)

Nhóm API `GET` cung cấp dữ liệu đổ dropdown cho form tạo yêu cầu ký gửi (`route`, `shippingOption`, `productType`). Tất cả đều **public** (không cần token) và trả về `data` là mảng.

### 30.1. Danh sách tuyến hàng (route)

```http
GET /api/orders/consignments/routes
```

```json
{
  "message": "Lấy danh sách tuyến hàng thành công.",
  "data": ["VIETNAM-JAPAN", "Vietnam-Korea", "VIETNAM-USA"]
}
```

Giá trị route lấy từ `ServicePricing` theo định dạng `{OriginCountry}-{DestinationCountry}`, đã `distinct` và sắp xếp A→Z.

### 30.2. Danh sách phương án vận chuyển (shippingOption)

```http
GET /api/orders/consignments/shipping-options
GET /api/orders/consignments/shipping-options?route=Vietnam-Korea
```

```json
{
  "message": "Lấy danh sách phương án vận chuyển thành công.",
  "data": ["CONSOLIDATION", "EXPRESS", "Standard"]
}
```

- Không truyền `route` → trả về toàn bộ `ServiceType` distinct.
- Truyền `route` → chỉ trả về các phương án khả dụng cho tuyến đó (rỗng `[]` nếu route không có).

### 30.3. Danh sách loại hàng (productType)

```http
GET /api/product-types
```

```json
{
  "message": "Lấy danh sách loại hàng thành công.",
  "data": [
    { "id": "11111111-0000-0000-0000-000000000004", "name": "Accessories" },
    { "id": "11111111-0000-0000-0000-000000000002", "name": "Clothing" }
  ]
}
```

Lấy từ bảng `PRODUCT_TYPES` (chỉ các loại `isActive = true`), sắp xếp theo tên. Admin có thể mở rộng danh sách này về sau.

### 30.4. Danh sách trạng thái yêu cầu ký gửi (statuses)

```http
GET /api/orders/consignments/statuses
```

```json
{
  "message": "Lấy danh sách trạng thái yêu cầu ký gửi thành công.",
  "data": [
    { "code": "PENDING_REVIEW", "label": "Chờ duyệt" },
    { "code": "QUOTATION_SENT", "label": "Đã gửi báo giá" },
    { "code": "APPROVED", "label": "Đã duyệt" },
    { "code": "REJECTED", "label": "Đã từ chối" },
    { "code": "CANCELLED", "label": "Đã hủy" },
    { "code": "COMPLETED", "label": "Hoàn tất" }
  ]
}
```

Dùng cho dropdown lọc trạng thái ở màn danh sách ký gửi. `code` gửi lại BE khi lọc (`GET /api/orders/consignments?status=`), `label` để hiển thị.

**TypeScript interface:**

```typescript
// routes & shipping-options: data là string[]
interface DropdownStringResponse {
  message: string;
  data: string[];
}

interface ProductTypeResponse {
  id: string;
  name: string;
}

interface ConsignmentStatusDto {
  code: string;
  label: string;
}
```

> **Lưu ý về `consignmentCode`:** mã ký gửi do **BE tự sinh** ngay khi khách tạo yêu cầu (định dạng `VCL-{yyyyMMddHHmmss}-{6 số}`). FE **không cần** tự tạo hay gửi mã này lên.

---

## 31. Hỗ trợ trao đổi giữa Customer và Sales (SignalR Chat)

Chức năng nhắn tin trực tuyến thời gian thực tích hợp giữa REST API lưu trữ dữ liệu và SignalR Hub để truyền tin tức thời.

### 31.1. Tạo cuộc trao đổi mới (Customer only)

Khách hàng bắt đầu một cuộc hội thoại mới, có thể gắn với một Consignment (Yêu cầu ký gửi), Purchase Request (Yêu cầu mua hộ) hoặc Quotation (Báo giá).

```http
POST /api/conversations
Authorization: Bearer {accessToken}
Content-Type: application/json
```

**Request body:**

```json
{
  "relatedType": "CONSIGNMENT", // Hoặc "PURCHASE_REQUEST", "QUOTATION", hoặc null
  "relatedId": "b037ab62-23c2-4fa1-8439-c1155fb9ebac", // Hoặc null
  "message": "Tôi muốn hỏi thêm về đơn hàng này.",
  "attachmentUrl": "https://cloudinary.com/..." // Hoặc null
}
```

- `relatedType` phải thuộc các giá trị: `CONSIGNMENT`, `PURCHASE_REQUEST`, `QUOTATION`.
- `message` bắt buộc từ 1 đến 2000 ký tự.

**Response thành công (201 Created):**

```json
{
  "id": "e8d77a28-bc4a-4e20-96f3-3c97db39a1ab",
  "customerId": "8a3da14d-03ee-fad5-0eb1-1f7881ddce19",
  "customerName": "Nguyễn Văn Khách",
  "customerCode": "KH-12345",
  "salesId": null,
  "salesName": null,
  "relatedType": "CONSIGNMENT",
  "relatedId": "b037ab62-23c2-4fa1-8439-c1155fb9ebac",
  "relatedCode": "VCL-20260708-000001",
  "status": "OPEN",
  "createdAt": "2026-07-08T04:24:00Z",
  "updatedAt": "2026-07-08T04:24:00Z",
  "unreadCount": 0
}
```

### 31.2. Lấy danh sách cuộc trao đổi

- **Customer:** Chỉ lấy danh sách cuộc trao đổi của chính mình.
- **Sales/Admin:** Lấy danh sách các cuộc trao đổi chưa được phân công (`salesId = null`) hoặc cuộc trao đổi do chính mình đảm nhận (`salesId = currentUserId`).

```http
GET /api/conversations
Authorization: Bearer {accessToken}
```

**Response thành công (200 OK):**

```json
[
  {
    "id": "e8d77a28-bc4a-4e20-96f3-3c97db39a1ab",
    "customerId": "8a3da14d-03ee-fad5-0eb1-1f7881ddce19",
    "customerName": "Nguyễn Văn Khách",
    "customerCode": "KH-12345",
    "salesId": "7cc4a1f8-0cf2-4467-93aa-bf482d8c97dd",
    "salesName": "Trần Văn Sales",
    "relatedType": "CONSIGNMENT",
    "relatedId": "b037ab62-23c2-4fa1-8439-c1155fb9ebac",
    "relatedCode": "VCL-20260708-000001",
    "status": "OPEN",
    "createdAt": "2026-07-08T04:24:00Z",
    "updatedAt": "2026-07-08T04:25:30Z",
    "unreadCount": 2
  }
]
```

### 31.3. Lấy chi tiết lịch sử tin nhắn cuộc trao đổi

Truy cập chi tiết một cuộc trao đổi và danh sách tin nhắn. Chỉ cho phép các bên liên quan truy cập (Customer của chat hoặc Sales được phân công / chưa phân công).

```http
GET /api/conversations/{conversationId}
Authorization: Bearer {accessToken}
```

**Response thành công (200 OK):**

```json
{
  "id": "e8d77a28-bc4a-4e20-96f3-3c97db39a1ab",
  "customerId": "8a3da14d-03ee-fad5-0eb1-1f7881ddce19",
  "customerName": "Nguyễn Văn Khách",
  "customerCode": "KH-12345",
  "salesId": "7cc4a1f8-0cf2-4467-93aa-bf482d8c97dd",
  "salesName": "Trần Văn Sales",
  "relatedType": "CONSIGNMENT",
  "relatedId": "b037ab62-23c2-4fa1-8439-c1155fb9ebac",
  "relatedCode": "VCL-20260708-000001",
  "status": "OPEN",
  "createdAt": "2026-07-08T04:24:00Z",
  "updatedAt": "2026-07-08T04:25:30Z",
  "unreadCount": 0,
  "messages": [
    {
      "id": "d13bf2a9-...",
      "conversationId": "e8d77a28-bc4a-4e20-96f3-3c97db39a1ab",
      "senderId": "4c94fd11-...",
      "senderRole": "Customer",
      "content": "Tôi muốn hỏi thêm về đơn hàng này.",
      "attachmentUrl": null,
      "isRead": true,
      "createdAt": "2026-07-08T04:24:00Z"
    },
    {
      "id": "f58bc449-...",
      "conversationId": "e8d77a28-bc4a-4e20-96f3-3c97db39a1ab",
      "senderId": "7cc4a1f8-...",
      "senderRole": "Sale",
      "content": "Chào anh, tôi có thể giúp gì ạ?",
      "attachmentUrl": null,
      "isRead": true,
      "createdAt": "2026-07-08T04:25:30Z"
    }
  ]
}
```

### 31.4. Gửi tin nhắn mới

Gửi tin nhắn vào cuộc trao đổi hiện tại. 
- Nếu Sales gửi tin nhắn vào một cuộc trao đổi chưa được phân công (`salesId` rỗng), hệ thống tự động gán cuộc trao đổi này cho Sales đó.

```http
POST /api/conversations/{conversationId}/messages
Authorization: Bearer {accessToken}
Content-Type: application/json
```

**Request body:**

```json
{
  "content": "Tôi gửi ảnh hóa đơn nhé.",
  "attachmentUrl": "https://cloudinary.com/..." // Hoặc null
}
```

- `content` bắt buộc từ 1 đến 2000 ký tự.

**Response thành công (200 OK):**

```json
{
  "id": "3bb87c12-...",
  "conversationId": "e8d77a28-bc4a-4e20-96f3-3c97db39a1ab",
  "senderId": "4c94fd11-...",
  "senderRole": "Customer",
  "content": "Tôi gửi ảnh hóa đơn nhé.",
  "attachmentUrl": "https://cloudinary.com/...",
  "isRead": false,
  "createdAt": "2026-07-08T04:26:00Z"
}
```

### 31.5. Đánh dấu đã đọc các tin nhắn

Đánh dấu tất cả tin nhắn do đối phương gửi đến trong cuộc hội thoại là đã đọc.

```http
PUT /api/conversations/{conversationId}/read
Authorization: Bearer {accessToken}
```

**Response thành công (200 OK):**

```json
{
  "message": "Đã đánh dấu đọc tin nhắn."
}
```

---

### 31.6. Kết nối SignalR Hub (Real-time WebSockets)

- **Hub Endpoint:** `/hubs/chat`
- **Giao thức bắt tay (Handshake & Auth):** Vì trình duyệt không hỗ trợ custom header trong WebSocket handshake, FE phải truyền JWT token qua query parameter:
  - Ví dụ: `wss://domain.com/hubs/chat?access_token={accessToken}`

#### Các Hub Method mà FE gọi lên (Invoke):

1. **`JoinConversation(conversationId: string)`**:
   - Tham gia vào nhóm hội thoại để nhận tin nhắn real-time.
   - Hệ thống tự động kiểm tra quyền truy cập. Nếu không có quyền, Hub ném lỗi `HubException`.
2. **`LeaveConversation(conversationId: string)`**:
   - Rời nhóm hội thoại khi người dùng thoát cửa sổ chat.

#### Các Sự kiện thời gian thực mà FE lắng nghe từ Hub (On):

1. **`ReceiveMessage(message: MessageDto)`**:
   - Nhận tin nhắn mới tức thời gửi bởi đối phương trong phòng hội thoại đang tham gia.
2. **`ConversationRead(data: { conversationId: string, readerId: string })`**:
   - Nhận thông báo khi đối phương vừa mở xem cuộc hội thoại và đánh dấu đọc.
3. **`NewConversationCreated(conversation: ConversationDto)`**:
   - Nhận thông báo khi có một khách hàng vừa tạo một cuộc trao đổi mới (Dành cho Sales/Admin ở màn hình chờ chat).

---

## 31. Tạo lô vận chuyển quốc tế (International Shipment)

Staff/Operations gom các yêu cầu xuất kho (WRO) đã `RELEASED` vào một lô vận chuyển quốc tế. Hệ thống gom danh sách kiện (Parcel) từ các WRO, tính tổng số kiện + tổng cân nặng, và tạo lô ở trạng thái `CREATED`.

```http
POST /api/international-shipments
Authorization: Bearer {accessToken}
Content-Type: application/json

{
  "wroRequestIds": ["e14b21a7-...", "a5816037-..."],
  "originWarehouseId": "11111111-...",
  "destinationWarehouseId": "22222222-...",
  "carrierId": "33333333-...",
  "shippingMethod": "Sea"
}
```

**Ràng buộc:**

| Trường | Bắt buộc | Mô tả |
|---|---|---|
| `wroRequestIds` | **Có** | Danh sách WRO (tối thiểu 1), tất cả phải ở trạng thái `RELEASED` |
| `originWarehouseId` | **Có** | Kho đi |
| `destinationWarehouseId` | **Có** | Kho đến |
| `carrierId` | Không | Đơn vị vận chuyển |
| `shippingMethod` | Không | Phương thức vận chuyển (Sea, Air...) |

**Response thành công (201 Created):**

```json
{
  "message": "Tạo lô vận chuyển quốc tế thành công.",
  "data": {
    "shipmentId": "9c1f...",
    "shipmentCode": "INTL-20260708080000-1234",
    "originWarehouseId": "11111111-...",
    "originWarehouseName": "Kho Trung Quốc",
    "destinationWarehouseId": "22222222-...",
    "destinationWarehouseName": "Kho Việt Nam HCM",
    "carrierId": "33333333-...",
    "carrierName": "GHTK",
    "shippingMethod": "Sea",
    "totalPackages": 5,
    "totalWeight": 12.5,
    "status": "CREATED",
    "wroRequests": [
      { "wroRequestId": "e14b21a7-...", "wroCode": "WRO-20260701-1234" }
    ],
    "parcels": [
      { "parcelId": "d5e243d7-...", "packageCode": "PCL-...", "weight": 2.5 }
    ]
  }
}
```

**Lưu ý nghiệp vụ:**
- Chỉ nhận WRO đã `RELEASED`; WRO chưa đủ điều kiện → `400`.
- Một WRO chỉ được thuộc **một** lô vận chuyển (ràng buộc unique) — gán lại vào lô khác trả `400`.
- Danh sách kiện được gom tự động từ các WRO (WroItem → Inventory → Parcel), distinct.
- `totalPackages` = số kiện; `totalWeight` = tổng cân nặng thực tế (ActualWeight, fallback ChargeableWeight).
- Trạng thái lô ban đầu luôn là `CREATED`.

**Response lỗi:**

| Status | Trường hợp | Message |
|---|---|---|
| `400` | Danh sách WRO rỗng | `"Phải chọn ít nhất một yêu cầu xuất kho."` |
| `400` | WRO chưa RELEASED | `"Yêu cầu xuất kho {code} chưa đủ điều kiện (phải ở trạng thái RELEASED)."` |
| `400` | WRO đã thuộc lô khác | `"Yêu cầu xuất kho {code} đã thuộc một lô vận chuyển khác."` |
| `403` | Không phải Staff/Operations | `"Chỉ Staff hoặc Operations được tạo lô vận chuyển quốc tế."` |
| `404` | Không tìm thấy WRO/kho/carrier | `"Không tìm thấy yêu cầu xuất kho với ID {id}"` |

**TypeScript interface:**

```typescript
interface CreateInternationalShipmentDto {
  wroRequestIds: string[];
  originWarehouseId: string;
  destinationWarehouseId: string;
  carrierId?: string | null;
  shippingMethod?: string | null;
}

interface InternationalShipmentResponseDto {
  shipmentId: string;
  shipmentCode: string;
  originWarehouseId?: string | null;
  originWarehouseName: string;
  destinationWarehouseId?: string | null;
  destinationWarehouseName: string;
  carrierId?: string | null;
  carrierName?: string | null;
  shippingMethod?: string | null;
  totalPackages: number;
  totalWeight: number;
  status: string;
  wroRequests: { wroRequestId: string; wroCode: string }[];
  parcels: { parcelId: string; packageCode: string; weight?: number | null }[];
}
```

---

## 32. Customer tra cứu kiện hàng (Customer Parcels)

Customer xem danh sách kiện hàng thuộc yêu cầu ký gửi của mình sau khi kho đã check-in (`RECEIVED`) hoặc nhập vị trí (`STORED`). Chỉ trả về kiện thuộc Customer đang đăng nhập.

```http
GET /api/customer/parcels?consignmentCode=VCL-2026...&searchCode=PCL-2026
Authorization: Bearer {accessToken}
```

**Query params (đều tùy chọn):**

| Name | Type | Mô tả |
|---|---|---|
| `consignmentCode` | `string` | Lọc theo mã yêu cầu ký gửi |
| `searchCode` | `string` | Tìm theo mã kiện hàng (`packageCode`) |

**Response thành công (200 OK):**

```json
{
  "message": "Lấy danh sách kiện hàng thành công.",
  "data": [
    {
      "parcelId": "d5e243d7-...",
      "packageCode": "PCL-20260701-1234",
      "consignmentId": "e14b21a7-...",
      "consignmentCode": "VCL-20260701-5678",
      "packageStatus": "STORED",
      "actualWeight": 1.5,
      "length": 30,
      "width": 20,
      "height": 15,
      "volumetricWeight": 1.8,
      "chargeableWeight": 1.8,
      "checkInTime": "2026-07-01T09:00:00Z",
      "putAwayTime": "2026-07-01T10:30:00Z",
      "saleNote": "Hàng dễ vỡ",
      "items": [
        { "productName": "Loa Bluetooth", "quantity": 4, "declaredValue": 800000 }
      ]
    }
  ]
}
```

**Lưu ý nghiệp vụ:**
- Chỉ trả về kiện đã check-in (`RECEIVED`) hoặc đã nhập vị trí (`STORED`); `putAwayTime` chỉ có khi đã `STORED`.
- Trả về `data: []` nếu Customer chưa có kiện nào được check-in (không phải lỗi).
- Nếu lọc bằng `consignmentCode` của yêu cầu **không thuộc** Customer → trả `403`.
- `saleNote` lấy từ ghi chú của yêu cầu ký gửi; `items` là danh sách sản phẩm khai báo của yêu cầu (tên, số lượng, giá trị).

**TypeScript interface:**

```typescript
interface CustomerParcelItemDto {
  productName: string;
  quantity: number;
  declaredValue?: number | null;
}

interface CustomerParcelDto {
  parcelId: string;
  packageCode: string;
  consignmentId?: string | null;
  consignmentCode: string;
  packageStatus: string;
  actualWeight?: number | null;
  length?: number | null;
  width?: number | null;
  height?: number | null;
  volumetricWeight?: number | null;
  chargeableWeight?: number | null;
  checkInTime?: string | null;
  putAwayTime?: string | null;
  saleNote?: string | null;
  items: CustomerParcelItemDto[];
}
```

---

### 31.1. Cập nhật trạng thái lô vận chuyển quốc tế

Staff/Operations cập nhật trạng thái lô theo vòng đời vận chuyển. Mỗi lần cập nhật lưu 1 bản ghi lịch sử (bảng `TRACKINGS`).

```http
PUT /api/international-shipments/{shipmentId}/status
Authorization: Bearer {accessToken}
Content-Type: application/json

{
  "status": "IN_TRANSIT",
  "note": "Đã rời cảng Busan"
}
```

**Vòng đời trạng thái (đúng thứ tự):**

```
CREATED → MANIFESTED → IN_TRANSIT → ARRIVED_DESTINATION
```

| Trường | Bắt buộc | Mô tả |
|---|---|---|
| `status` | **Có** | Một trong: `MANIFESTED`, `IN_TRANSIT`, `ARRIVED_DESTINATION` |
| `note` | Không | Ghi chú vận chuyển |

**Response thành công (200 OK):**

```json
{
  "message": "Cập nhật trạng thái lô vận chuyển thành công.",
  "data": {
    "shipmentId": "9c1f...",
    "shipmentCode": "INTL-20260708080000-1234",
    "status": "IN_TRANSIT",
    "note": "Đã rời cảng Busan",
    "updatedAt": "2026-07-08T09:00:00Z",
    "updatedByName": "Nguyễn Văn Staff"
  }
}
```

**Lưu ý nghiệp vụ:**
- Chỉ chuyển đúng thứ tự: `MANIFESTED` (từ `CREATED`) → `IN_TRANSIT` (từ `MANIFESTED`) → `ARRIVED_DESTINATION` (từ `IN_TRANSIT`).
- Chuyển sai thứ tự → `400`.
- Mỗi lần cập nhật lưu lịch sử vào `TRACKINGS` (status + note + thời gian).
- `IN_TRANSIT` set `shippedAt`, `ARRIVED_DESTINATION` set `deliveredAt` trên lô.

**Response lỗi:**

| Status | Trường hợp | Message |
|---|---|---|
| `400` | Trạng thái không hợp lệ | `"Trạng thái không hợp lệ. Hợp lệ: MANIFESTED, IN_TRANSIT, ARRIVED_DESTINATION."` |
| `400` | Sai thứ tự chuyển | `"Không thể chuyển sang {target} khi lô đang ở trạng thái {current}. Chỉ chuyển được từ {required}."` |
| `403` | Không phải Staff/Operations | `"Chỉ Staff hoặc Operations được cập nhật trạng thái lô vận chuyển."` |
| `404` | Không tìm thấy lô | `"Không tìm thấy lô vận chuyển với ID {shipmentId}"` |

**TypeScript interface:**

```typescript
interface UpdateInternationalShipmentStatusDto {
  status: "MANIFESTED" | "IN_TRANSIT" | "ARRIVED_DESTINATION";
  note?: string | null;
}

interface UpdateInternationalShipmentStatusResponseDto {
  shipmentId: string;
  shipmentCode: string;
  status: string;
  note?: string | null;
  updatedAt: string;
  updatedByName: string;
}
```

---

### 31.2. Xác nhận lô đến kho đích

Staff/Operations xác nhận lô vận chuyển đã đến kho đích. Kết thúc bước vận chuyển quốc tế và chuẩn bị dữ liệu kiện cho quy trình nhập kho tại kho đích.

```http
PUT /api/international-shipments/{shipmentId}/arrive
Authorization: Bearer {accessToken}
Content-Type: application/json

{
  "note": "Lô đã cập cảng Cát Lái"
}
```

**Response thành công (200 OK):**

```json
{
  "message": "Xác nhận lô đến kho đích thành công.",
  "data": {
    "shipmentId": "9c1f...",
    "shipmentCode": "INTL-20260708080000-1234",
    "destinationWarehouseId": "22222222-...",
    "destinationWarehouseName": "Kho Việt Nam HCM",
    "status": "ARRIVED_DESTINATION",
    "arrivedAt": "2026-07-08T12:00:00Z",
    "confirmedByName": "Nguyễn Văn Staff",
    "note": "Lô đã cập cảng Cát Lái",
    "totalPackages": 5,
    "parcels": [
      { "parcelId": "d5e243d7-...", "packageCode": "PCL-...", "weight": 2.5 }
    ]
  }
}
```

**Lưu ý nghiệp vụ:**
- Chỉ xác nhận khi lô đang `IN_TRANSIT`; lô đã `ARRIVED_DESTINATION` → `400` (không xác nhận 2 lần).
- Cập nhật lô → `ARRIVED_DESTINATION` + `deliveredAt`, và **tất cả Parcel trong lô** → `PackageStatus = ARRIVED_DESTINATION` (sẵn sàng cho nhập kho VCL-118/120).
- Lưu lịch sử vào `TRACKINGS` (status + note + thời gian).

**Response lỗi:**

| Status | Trường hợp | Message |
|---|---|---|
| `400` | Lô đã xác nhận đến trước đó | `"Lô vận chuyển này đã được xác nhận đến kho đích trước đó."` |
| `400` | Lô chưa IN_TRANSIT | `"Lô chưa đủ điều kiện xác nhận đến kho đích (phải đang IN_TRANSIT, hiện tại: {status})."` |
| `403` | Không phải Staff/Operations | `"Chỉ Staff hoặc Operations được xác nhận lô đến kho đích."` |
| `404` | Không tìm thấy lô | `"Không tìm thấy lô vận chuyển với ID {shipmentId}"` |

**TypeScript interface:**

```typescript
interface ConfirmShipmentArrivalDto {
  note?: string | null;
}

interface ConfirmShipmentArrivalResponseDto {
  shipmentId: string;
  shipmentCode: string;
  destinationWarehouseId?: string | null;
  destinationWarehouseName: string;
  status: string;
  arrivedAt: string;
  confirmedByName: string;
  note?: string | null;
  totalPackages: number;
  parcels: { parcelId: string; packageCode: string; weight?: number | null }[];
}
```

---

### 31.3. Xác nhận nhận kiện tại kho đích

Warehouse Staff (hoặc Staff/Operations) xác nhận các kiện trong lô đã được nhận tại kho đích, sau khi lô đã `ARRIVED_DESTINATION`. Sau bước này kiện chuyển `RECEIVED_AT_DESTINATION` và sẵn sàng cho put-away.

```http
PUT /api/international-shipments/{shipmentId}/destination-receiving
Authorization: Bearer {accessToken}
Content-Type: application/json

{
  "parcelIds": ["d5e243d7-..."],
  "note": "Nhận đủ 5 kiện"
}
```

**Ràng buộc:**

| Trường | Bắt buộc | Mô tả |
|---|---|---|
| `parcelIds` | Không | Danh sách kiện cần nhận; **bỏ trống = nhận tất cả** kiện trong lô chưa nhận |
| `note` | Không | Ghi chú nhận kiện |

**Response thành công (200 OK):**

```json
{
  "message": "Xác nhận nhận kiện tại kho đích thành công.",
  "data": {
    "shipmentId": "9c1f...",
    "shipmentCode": "INTL-20260708080000-1234",
    "destinationWarehouseId": "22222222-...",
    "destinationWarehouseName": "Kho Việt Nam HCM",
    "receivedAt": "2026-07-08T13:00:00Z",
    "receivedByName": "Warehouse Staff A",
    "note": "Nhận đủ 5 kiện",
    "receivedCount": 5,
    "parcels": [
      { "parcelId": "d5e243d7-...", "packageCode": "PCL-...", "packageStatus": "RECEIVED_AT_DESTINATION" }
    ]
  }
}
```

**Lưu ý nghiệp vụ:**
- Chỉ nhận kiện khi lô đang `ARRIVED_DESTINATION` (chưa đến → `400`).
- Cập nhật kiện → `PackageStatus = RECEIVED_AT_DESTINATION` + set `checkInTime`, sẵn sàng cho put-away (VCL-120).
- Không cho nhận trùng: kiện đã `RECEIVED_AT_DESTINATION` → `400` (nếu truyền `parcelIds`), hoặc bỏ qua nếu nhận tất cả; nhận hết rồi mà gọi lại → `400`.
- Lưu lịch sử vào `TRACKINGS` (stage `DESTINATION_RECEIVING`).

**Response lỗi:**

| Status | Trường hợp | Message |
|---|---|---|
| `400` | Lô chưa đến kho đích | `"Lô chưa đến kho đích (phải ở trạng thái ARRIVED_DESTINATION, hiện tại: {status})."` |
| `400` | Parcel không thuộc lô | `"Parcel {id} không thuộc lô vận chuyển này."` |
| `400` | Kiện đã nhận trước đó | `"Parcel {code} đã được xác nhận nhận tại kho đích trước đó."` |
| `403` | Không đúng quyền | `"Chỉ Warehouse Staff, Staff hoặc Operations được xác nhận nhận kiện tại kho đích."` |
| `404` | Không tìm thấy lô | `"Không tìm thấy lô vận chuyển với ID {shipmentId}"` |

**TypeScript interface:**

```typescript
interface DestinationReceivingDto {
  parcelIds?: string[] | null;
  note?: string | null;
}

interface DestinationReceivingResponseDto {
  shipmentId: string;
  shipmentCode: string;
  destinationWarehouseId?: string | null;
  destinationWarehouseName: string;
  receivedAt: string;
  receivedByName: string;
  note?: string | null;
  receivedCount: number;
  parcels: { parcelId: string; packageCode: string; packageStatus: string }[];
}
```

---

## 33. Phiếu tiếp nhận kho (Warehouse Receiving Note)

Sales tạo phiếu tiếp nhận kho từ yêu cầu ký gửi đã `APPROVED`; phiếu có mã riêng (`WRN-xxxxxxxx`) để Warehouse Staff **scan trên mobile** và nhận hàng. Không thay thế API check-in — chỉ bổ sung bước lấy thông tin phiếu trước khi check-in.

### 33.1. Sales tạo phiếu tiếp nhận kho

```http
POST /api/warehouse-receiving-notes
Authorization: Bearer {saleToken}
Content-Type: application/json

{
  "consignmentOrderId": "5c94b9d5-...",
  "warehouseId": "df34760c-...",
  "warehouseNote": "Giao trước 5h chiều"
}
```

**Response thành công (201):** trả `receivingNote` gồm `receivingNoteCode`, `status: ACTIVE`, kho, ghi chú...

**Lưu ý / lỗi:**
- Chỉ tạo khi yêu cầu ký gửi đã `APPROVED` → nếu chưa: `400`.
- Mỗi đơn chỉ có **1 phiếu đang hiệu lực** → tạo trùng: `409`.
- Không tìm thấy đơn/kho: `404`. Chỉ **Sale/Admin** được tạo.

### 33.2. Warehouse Staff scan phiếu

```http
GET /api/warehouse-receiving-notes/scan?code=WRN-63670531
Authorization: Bearer {warehouseStaffToken}
```

**Response thành công (200 OK):**

```json
{
  "message": "Quét phiếu tiếp nhận kho thành công.",
  "data": {
    "receivingNoteId": "...",
    "receivingNoteCode": "WRN-63670531",
    "status": "ACTIVE",
    "consignmentOrderId": "5c94b9d5-...",
    "consignmentCode": "VCL-20260708184739-692376",
    "customerId": "1b53ba9c-...",
    "customerName": "Bùi Đình Trọng",
    "customerCode": "...",
    "warehouseId": "df34760c-...",
    "warehouseName": "Kho HCM - Tân Bình",
    "warehouseNote": "Giao trước 5h chiều",
    "expectedItems": [
      { "productName": "Loa", "productType": "Điện tử", "quantity": 2, "declaredValue": 800000 }
    ]
  }
}
```

**Lưu ý nghiệp vụ:**
- Chỉ **WarehouseStaff/Admin** được scan (`403` nếu sai quyền); mã không tồn tại → `404`.
- Sau khi kho xác nhận nhận hàng, **reuse** `POST /api/parcels/check-in` (mục 16) để tạo Parcel. Check-in thành công sẽ tự cập nhật phiếu sang `RECEIVED`.
- Không ảnh hưởng flow check-in bằng mã gửi hàng đã có.

**TypeScript interface (scan):**

```typescript
interface WarehouseReceivingNoteExpectedItemDto {
  productName: string;
  productType: string;
  quantity: number;
  declaredValue?: number | null;
}

interface WarehouseReceivingNoteScanResponseDto {
  receivingNoteId: string;
  receivingNoteCode: string;
  status: string;
  consignmentOrderId: string;
  consignmentCode: string;
  customerId?: string | null;
  customerName: string;
  customerCode: string;
  warehouseId: string;
  warehouseName: string;
  warehouseNote: string;
  expectedItems: WarehouseReceivingNoteExpectedItemDto[];
}
```

---

## 26. Lưu ý tích hợp FE

- Backend hiện chưa cấu hình CORS. FE chạy khác origin có thể cần proxy hoặc BE whitelist origin.
- Email OTP đặt lại mật khẩu có thể bị Gmail đưa vào Spam trong môi trường test.
- OTP đặt lại mật khẩu hết hạn sau **5 phút** (khác với OTP đăng ký cũng 5 phút).
- FE nên disable nút gửi lại OTP đặt lại mật khẩu trong 60 giây (giống luồng đăng ký).
- Sau 5 lần nhập sai OTP đặt lại mật khẩu, user phải bấm "Gửi lại" để nhận OTP mới.
- FE nên disable nút gửi lại OTP trong 60 giây.
- OTP hết hạn sau 5 phút.
- Không cho user truy cập portal trước khi verify OTP và login thành công.
- Khi API trả `401`, xóa session và chuyển về trang login.
- Khi API trả `403`, hiển thị thông báo không đủ quyền.
- API hiện chưa hỗ trợ refresh token.

### Routing FE gợi ý

| Trang | API |
|---|---|
| `/register` | `POST /api/Auth/customer/register` |
| `/verify-email` | `POST /api/Auth/customer/verify-otp`, `resend-otp` |
| `/login` | `POST /api/Auth/login` |
| `/forgot-password` | `POST /api/Auth/forgot-password` |
| `/reset-password` | `POST /api/Auth/reset-password` |
| `/admin/users/create` | `POST /api/Auth/admin/register` |
| `/orders/{id}/quotation` | `POST /api/orders/{orderId}/quotation/estimate` |
| `/quotations/{id}/accept` | `PUT /api/quotations/{quotationId}/accept` |
| `/orders/{id}/status` | `PUT /api/orders/consignments/{orderId}/status` |
| `/orders/consignments` | `GET /api/orders/consignments` |
| `/orders/{id}/cancel` | `PUT /api/orders/consignments/{orderId}/cancel` |
| `/staff/consignments/create` | `POST /api/staff/consignments` |
| `/admin/restricted-items` | `GET /api/restricted-items` |
| `/admin/restricted-items/create` | `POST /api/restricted-items` |
| `/admin/restricted-items/{id}/edit` | `PUT /api/restricted-items/{id}` |
| `/admin/pricing-rules` | `GET /api/pricing-rules` |
| `/admin/pricing-rules/create` | `POST /api/pricing-rules` |
| `/admin/pricing-rules/{id}/edit` | `PUT /api/pricing-rules/{id}` |
| `/admin/service-pricings` | `GET /api/service-pricings` |
| `/admin/service-pricings/create` | `POST /api/service-pricings` |
| `/admin/service-pricings/{id}/edit` | `PUT /api/service-pricings/{id}` |
| `/warehouse/check-in` | `POST /api/parcels/check-in` |
| `/warehouse/pending-putaway` | `GET /api/parcels/pending-putaway` |
| `/warehouse/put-away` | `PUT /api/parcels/{parcelId}/put-away` |
| `/warehouse/inventory/record` | `POST /api/inventories/from-parcel/{parcelId}` |
| `/warehouse/release-requests/create` | `POST /api/warehouse-release-requests` |
| `/warehouse/release-requests/{id}` | `GET /api/warehouse-release-requests/{requestId}` |
| `/warehouse/release-requests/{id}/status` | `PUT /api/warehouse-release-requests/{requestId}/status` |
| `/warehouse/release-requests/{id}/picking-list` | `POST /api/warehouse-release-requests/{requestId}/picking-list` |
| `/admin/carriers` | `GET /api/carriers` |
| `/admin/carriers/create` | `POST /api/carriers` |
| `/admin/carriers/{id}/edit` | `PUT /api/carriers/{id}` |
| `/customer/chat` | `GET /api/conversations`, `POST /api/conversations` |
| `/customer/chat/{id}` | `GET /api/conversations/{id}`, `POST /api/conversations/{id}/messages`, `PUT /api/conversations/{id}/read` |
| `/sales/chat` | `GET /api/conversations` |
| `/sales/chat/{id}` | `GET /api/conversations/{id}`, `POST /api/conversations/{id}/messages`, `PUT /api/conversations/{id}/read` |


