# VCL Backend - Tài liệu tích hợp API cho Frontend

> Hệ thống: Vietnam Cross-Border Logistics System (VCL)
>
> Cập nhật: 16/06/2026 — Thêm API Lấy danh sách yêu cầu ký gửi
>
> Module hiện có: Authentication, User Management, Quotation, Order

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
| `GET` | `/api/orders/consignments` | Authenticated | Lấy danh sách yêu cầu ký gửi |

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

## 11. Lấy danh sách yêu cầu ký gửi

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

### 11.1 Response thành công

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

### 11.2 Response lỗi

| Status | Trường hợp | Message |
|---|---|---|
| `401` | Chưa đăng nhập hoặc token sai | Tự động bởi middleware JWT |

## 12. TypeScript types

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
```

## 13. Axios integration

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

export async function getConsignments(params?: { pageNumber?: number, pageSize?: number, status?: string, searchCode?: string }) {
  return api.get<ConsignmentPaginatedResponse>('/api/orders/consignments', { params });
}
```

## 14. Lưu ý tích hợp FE

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
| `/orders/consignments` | `GET /api/orders/consignments` |
