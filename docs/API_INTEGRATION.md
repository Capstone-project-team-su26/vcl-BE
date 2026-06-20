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
| `POST` | `/api/orders/{orderId}/quotation/estimate` | Authenticated | Tạo báo giá tạm tính cho đơn ký gửi |
| `PUT` | `/api/quotations/{quotationId}/accept` | Authenticated | Khách hàng xác nhận báo giá tạm tính |
| `PUT` | `/api/orders/consignments/{orderId}/status` | Staff | Staff cập nhật trạng thái yêu cầu ký gửi |
| `GET` | `/api/orders/consignments` | Authenticated | Lấy danh sách yêu cầu ký gửi |
| `PUT` | `/api/orders/consignments/{orderId}/cancel` | Authenticated | Customer hủy yêu cầu ký gửi |
| `GET` | `/api/restricted-items` | Admin | Xem danh sách mặt hàng cấm/hạn chế |
| `GET` | `/api/restricted-items/{id}` | Admin | Lấy chi tiết mặt hàng cấm/hạn chế |
| `POST` | `/api/restricted-items` | Admin | Thêm mặt hàng cấm/hạn chế mới |
| `PUT` | `/api/restricted-items/{id}` | Admin | Cập nhật thông tin/vô hiệu hóa mặt hàng |
| `DELETE` | `/api/restricted-items/{id}` | Admin | Xóa mặt hàng cấm/hạn chế |
| `POST` | `/api/parcels/check-in` | Staff/WarehouseStaff | Check-in kiện hàng thực tế bằng mã gửi hàng |
| `GET` | `/api/parcels/pending-putaway` | Staff/WarehouseStaff | Lấy danh sách kiện chờ nhập vị trí kho |

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

Hệ thống cung cấp API lấy danh sách yêu cầu ký gửi có phân trang. Customer chỉ thấy yêu cầu của chính mình, Admin có thể xem toàn bộ yêu cầu trong hệ thống.

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
Authorization: Bearer {adminToken}
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
Authorization: Bearer {adminToken}
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

Quy trình dành cho nhân viên kho (Warehouse Staff / Staff) xem danh sách các kiện hàng đã được check-in vào hệ thống thành công (PackageStatus = "RECEIVED") nhưng chưa được sắp xếp/gán vào ô kệ (Bin) cụ thể trong kho (BinId = null) để tiến hành put-away.

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

## 18. TypeScript types

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
  createdAt: string | null;
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
  shippingServiceType: string;
  consignmentType: string;
  route: string | null;
  unitType: string | null;
  pricePerWeight: number;
  pricePerVolume: number | null;
  serviceFee: number;
  isActive: boolean;
  createdAt: string;
}

export interface CreatePricingRuleRequest {
  shippingServiceType: string;
  consignmentType: string;
  route?: string;
  unitType?: string;
  pricePerWeight: number;
  pricePerVolume?: number;
  serviceFee: number;
  isActive?: boolean;
}

export interface UpdatePricingRuleRequest {
  shippingServiceType: string;
  consignmentType: string;
  route?: string;
  unitType?: string;
  pricePerWeight: number;
  pricePerVolume?: number;
  serviceFee: number;
  isActive: boolean;
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
```

## 19. Axios integration

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
```

## 20. Lưu ý tích hợp FE

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
| `/admin/restricted-items` | `GET /api/restricted-items` |
| `/admin/restricted-items/create` | `POST /api/restricted-items` |
| `/admin/restricted-items/{id}/edit` | `PUT /api/restricted-items/{id}` |
| `/admin/pricing-rules` | `GET /api/pricing-rules` |
| `/admin/pricing-rules/create` | `POST /api/pricing-rules` |
| `/admin/pricing-rules/{id}/edit` | `PUT /api/pricing-rules/{id}` |
| `/warehouse/check-in` | `POST /api/parcels/check-in` |
| `/warehouse/pending-putaway` | `GET /api/parcels/pending-putaway` |

