# VCL Backend — Tài liệu tích hợp API cho Frontend

> **Hệ thống:** Vietnam Cross-Border Logistics System (VCL)  
> **Cập nhật:** 10/06/2026  
> **Phiên bản API:** Đang phát triển (hiện có module **Authentication**)

---

## Mục lục

1. [Thông tin chung](#1-thông-tin-chung)
2. [Xác thực JWT](#2-xác-thực-jwt)
3. [Định dạng lỗi](#3-định-dạng-lỗi)
4. [API Authentication](#4-api-authentication)
5. [TypeScript types (tham khảo)](#5-typescript-types-tham-khảo)
6. [Ví dụ gọi API](#6-ví-dụ-gọi-api)
7. [Lưu ý cho FE](#7-lưu-ý-cho-fe)
8. [API dự kiến (chưa triển khai)](#8-api-dự-kiến-chưa-triển-khai)

---

## 1. Thông tin chung

### Base URL (Development)

| Profile | URL |
|---------|-----|
| HTTP | `http://localhost:5209` |
| HTTPS | `https://localhost:7237` |
| IIS Express | `http://localhost:54037` / `https://localhost:44395` |

**Prefix chung:** Tất cả endpoint đều nằm dưới `/api/...`

**Swagger UI (chỉ môi trường Development):** `{BASE_URL}/swagger`

### Headers mặc định

| Header | Giá trị | Bắt buộc |
|--------|---------|----------|
| `Content-Type` | `application/json` | Có (với POST có body) |
| `Accept` | `application/json` | Khuyến nghị |
| `Authorization` | `Bearer {accessToken}` | Có (với API yêu cầu đăng nhập) |

### Quy ước đặt tên JSON

- Backend ASP.NET Core mặc định serialize JSON theo **camelCase** (ví dụ: `fullName`, `userId`).
- Các field kiểu `DateTime` trả về dạng ISO 8601 UTC (ví dụ: `2026-06-10T08:30:00Z`).
- Field `Guid` trả về dạng chuỗi UUID (ví dụ: `"3fa85f64-5717-4562-b3fc-2c963f66afa6"`).

---

## 2. Xác thực JWT

Sau khi **đăng nhập thành công**, FE lưu `token` từ response và gửi kèm mọi request cần bảo vệ:

```
Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...
```

### Thông tin token

| Thuộc tính | Giá trị |
|------------|---------|
| Thuật toán | HS256 |
| Issuer | `vcl-system` |
| Audience | `vcl-clients` |
| Thời hạn | **240 phút** (4 giờ) — cấu hình trong `JWT:TokenValidityInMinutes` |

### Claims bên trong JWT

| Claim | Nội dung |
|-------|----------|
| `nameid` | `UserId` (Guid) |
| `unique_name` | Họ tên đầy đủ |
| `email` | Email đăng nhập |
| `role` | Vai trò (`User`, …) |

### Phản hồi khi token không hợp lệ / hết hạn

- HTTP **401 Unauthorized**
- FE nên redirect về trang đăng nhập hoặc gọi lại flow login.

> **Lưu ý:** Hiện chưa có API refresh token. Khi token hết hạn, người dùng cần đăng nhập lại.

---

## 3. Định dạng lỗi

Các endpoint Auth trả lỗi theo dạng object JSON:

```json
{
  "message": "Mô tả lỗi"
}
```

| HTTP Status | Ý nghĩa thường gặp |
|-------------|-------------------|
| `400 Bad Request` | Dữ liệu không hợp lệ / business rule (ví dụ: email không tồn tại) |
| `401 Unauthorized` | Sai email hoặc mật khẩu |
| `409 Conflict` | Email đã được đăng ký |
| `201 Created` | Đăng ký thành công |
| `200 OK` | Thao tác thành công |

---

## 4. API Authentication

**Controller:** `AuthController`  
**Base route:** `/api/auth`

---

### 4.1. Đăng ký tài khoản

```
POST /api/auth/register
```

**Auth:** Không cần (`AllowAnonymous`)

**Request body:**

```json
{
  "fullName": "Nguyễn Văn A",
  "email": "user@example.com",
  "password": "YourPassword123",
  "phone": "0901234567"
}
```

| Field | Type | Bắt buộc | Mô tả |
|-------|------|----------|-------|
| `fullName` | `string` | Có | Họ và tên |
| `email` | `string` | Có | Email (unique, không phân biệt hoa thường) |
| `password` | `string` | Có | Mật khẩu (được hash BCrypt phía server) |
| `phone` | `string` | Có | Số điện thoại |

**Response thành công — `201 Created`**

```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6"
}
```

**Response lỗi — `409 Conflict`**

```json
{
  "message": "Email already in use"
}
```

**Hành vi phía server khi đăng ký thành công:**
- `role` = `"User"`
- `userType` = `"Customer"`
- `status` = `"Active"`

---

### 4.2. Đăng nhập

```
POST /api/auth/login
```

**Auth:** Không cần (`AllowAnonymous`)

**Request body:**

```json
{
  "email": "user@example.com",
  "password": "YourPassword123"
}
```

| Field | Type | Bắt buộc | Mô tả |
|-------|------|----------|-------|
| `email` | `string` | Có | Email đăng nhập |
| `password` | `string` | Có | Mật khẩu |

**Response thành công — `200 OK`**

```json
{
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "expiresAt": "2026-06-10T12:30:00Z",
  "userId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "fullName": "Nguyễn Văn A",
  "role": "User"
}
```

| Field | Type | Mô tả |
|-------|------|-------|
| `token` | `string` | JWT access token — lưu vào localStorage / cookie / memory |
| `expiresAt` | `string` (ISO 8601) | Thời điểm token hết hạn (UTC) |
| `userId` | `string` (Guid) | ID người dùng |
| `fullName` | `string` | Họ tên |
| `role` | `string` | Vai trò trong hệ thống |

**Response lỗi — `401 Unauthorized`**

```json
{
  "message": "Invalid credentials"
}
```

---

### 4.3. Quên mật khẩu

```
POST /api/auth/forgot-password
```

**Auth:** Không cần (`AllowAnonymous`)

**Request body:**

```json
{
  "email": "user@example.com"
}
```

| Field | Type | Bắt buộc | Mô tả |
|-------|------|----------|-------|
| `email` | `string` | Có | Email tài khoản cần đặt lại mật khẩu |

**Response thành công — `200 OK`**

```json
{
  "message": "Email đặt lại mật khẩu đã được gửi. Vui lòng kiểm tra hộp thư của bạn."
}
```

**Response lỗi — `400 Bad Request`**

```json
{
  "message": "Email không tồn tại trong hệ thống."
}
```

**Flow phía FE cần biết:**
1. User nhập email → gọi API này.
2. Backend gửi email chứa **reset token** (URL-safe Base64, hiệu lực **1 giờ**).
3. Token được hiển thị trong email (chưa có deep-link URL — FE cần có trang nhập token + mật khẩu mới).

**Gợi ý UI FE:** Trang `/reset-password` với 2 field: `token` (copy từ email) và `newPassword`.

---

### 4.4. Đặt lại mật khẩu

```
POST /api/auth/reset-password
```

**Auth:** Không cần (`AllowAnonymous`)

**Request body:**

```json
{
  "token": "abc123-url-safe-base64-token",
  "newPassword": "NewSecurePassword456"
}
```

| Field | Type | Bắt buộc | Mô tả |
|-------|------|----------|-------|
| `token` | `string` | Có | Token nhận từ email (forgot-password) |
| `newPassword` | `string` | Có | Mật khẩu mới |

**Response thành công — `200 OK`**

```json
{
  "message": "Mật khẩu đã được đặt lại thành công."
}
```

**Response lỗi — `400 Bad Request`**

```json
{
  "message": "Token không hợp lệ hoặc đã hết hạn."
}
```

---

## 5. TypeScript types (tham khảo)

FE có thể copy các interface sau vào project:

```typescript
// ─── Request types ───────────────────────────────────────────

export interface RegisterRequest {
  fullName: string;
  email: string;
  password: string;
  phone: string;
}

export interface LoginRequest {
  email: string;
  password: string;
}

export interface ForgotPasswordRequest {
  email: string;
}

export interface ResetPasswordRequest {
  token: string;
  newPassword: string;
}

// ─── Response types ──────────────────────────────────────────

export interface AuthResponse {
  token: string;
  expiresAt: string;   // ISO 8601 UTC
  userId: string;      // Guid
  fullName: string;
  role: string;
}

export interface RegisterResponse {
  id: string;          // Guid
}

export interface MessageResponse {
  message: string;
}

export interface ApiError {
  message: string;
}
```

---

## 6. Ví dụ gọi API

### Axios (khuyến nghị)

```typescript
import axios from 'axios';

const api = axios.create({
  baseURL: import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5209',
  headers: { 'Content-Type': 'application/json' },
});

// Gắn token tự động
api.interceptors.request.use((config) => {
  const token = localStorage.getItem('accessToken');
  if (token) {
    config.headers.Authorization = `Bearer ${token}`;
  }
  return config;
});

// ─── Login ───────────────────────────────────────────────────
export async function login(email: string, password: string) {
  const { data } = await api.post<AuthResponse>('/api/auth/login', {
    email,
    password,
  });
  localStorage.setItem('accessToken', data.token);
  localStorage.setItem('tokenExpiresAt', data.expiresAt);
  return data;
}

// ─── Register ────────────────────────────────────────────────
export async function register(payload: RegisterRequest) {
  const { data } = await api.post<RegisterResponse>('/api/auth/register', payload);
  return data;
}

// ─── Forgot password ─────────────────────────────────────────
export async function forgotPassword(email: string) {
  const { data } = await api.post<MessageResponse>('/api/auth/forgot-password', { email });
  return data;
}

// ─── Reset password ──────────────────────────────────────────
export async function resetPassword(token: string, newPassword: string) {
  const { data } = await api.post<MessageResponse>('/api/auth/reset-password', {
    token,
    newPassword,
  });
  return data;
}
```

### Fetch API

```typescript
const BASE_URL = 'http://localhost:5209';

async function login(email: string, password: string) {
  const res = await fetch(`${BASE_URL}/api/auth/login`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email, password }),
  });

  if (!res.ok) {
    const err = await res.json();
    throw new Error(err.message ?? 'Login failed');
  }

  return res.json() as Promise<AuthResponse>;
}
```

---

## 7. Lưu ý cho FE

### CORS

Backend **chưa cấu hình CORS**. Khi FE chạy trên origin khác (ví dụ `http://localhost:5173`), cần một trong hai cách:

1. **BE bổ sung CORS** — liên hệ team BE để whitelist origin FE.
2. **Dùng proxy trong Vite/Webpack** — ví dụ Vite:

```typescript
// vite.config.ts
export default defineConfig({
  server: {
    proxy: {
      '/api': {
        target: 'http://localhost:5209',
        changeOrigin: true,
      },
    },
  },
});
```

Khi dùng proxy, `baseURL` của axios có thể để rỗng hoặc `/`.

### Biến môi trường FE gợi ý

```env
VITE_API_BASE_URL=http://localhost:5209
```

### Swagger

Trong Development, mở `{BASE_URL}/swagger` để xem schema OpenAPI và test trực tiếp (có hỗ trợ nhập Bearer token).

### Quản lý session FE

| Hành động | Gợi ý |
|-----------|-------|
| Sau login | Lưu `token`, `expiresAt`, `userId`, `fullName`, `role` |
| Trước mỗi request protected | Kiểm tra `expiresAt`; nếu hết hạn → logout |
| Logout | Xóa token khỏi storage, redirect `/login` |
| 401 từ API | Clear session + redirect login |

### Trang FE tương ứng (gợi ý routing)

| Trang | API liên quan |
|-------|---------------|
| `/register` | `POST /api/auth/register` |
| `/login` | `POST /api/auth/login` |
| `/forgot-password` | `POST /api/auth/forgot-password` |
| `/reset-password` | `POST /api/auth/reset-password` |

---

## 8. API dự kiến (chưa triển khai)

Database đã có các entity sau — team BE sẽ bổ sung controller tương ứng trong các sprint tiếp theo. FE có thể chuẩn bị module trước:

| Module | Entity liên quan |
|--------|------------------|
| Đơn hàng | `Order`, `OrderItem` |
| Vận chuyển | `Shipment`, `Parcel`, `Tracking`, `Carrier` |
| Kho | `Warehouse`, `WarehouseZone`, `Shelf`, `Bin`, `MasterBox` |
| Hải quan | `CustomsDeclaration`, `CustomsDocument`, `CustomsTaxis`, `RestrictedItem` |
| Thanh toán | `Payment`, `Invoice`, `InvoiceItem`, `Quotation`, `ServicePricing` |
| Hỗ trợ | `SupportTicket`, `Notification` |
| WRO | `WroRequest`, `ShipmentHandover` |
| Khách hàng / Công ty | `Customer`, `Company`, `User` |
| Vận đơn nội địa | `DomesticWaybill` |

> Khi có API mới, file này sẽ được cập nhật. FE có thể theo dõi qua Swagger hoặc PR từ team BE.

---

## Liên hệ

- **Repository BE:** `vcl-BE`
- **Project API:** `VietnamCrossBorderLogictisSystem/VCL_API`
- **Swagger (dev):** `http://localhost:5209/swagger`
