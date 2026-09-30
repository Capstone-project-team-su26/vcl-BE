# Ghép API luồng ký gửi — Phần 1: từ lúc khách đặt đơn tới lúc hàng vào kho

Tài liệu cho FE ghép API luồng ký gửi đang chạy trên production (cập nhật 17/09/2026 — thêm dịch vụ theo từng kiện).

- Base URL: `https://vcl.henrytech.cloud`
- Swagger: `https://vcl.henrytech.cloud/swagger/index.html`
- Mọi API (trừ chỗ ghi "không cần đăng nhập") gửi header `Authorization: Bearer <token>`.
- Lỗi luôn trả dạng `{ "message": "..." }`. FE hiển thị nguyên `message` cho người dùng (đã là tiếng Việt).
- **Tài khoản demo và đơn demo ở từng trạng thái:** xem [`README.md`](README.md). Mỗi bước dưới đây có dòng **Thử:** chỉ đơn demo + tài khoản dùng để thử.
- Danh mục (kho, bảng giá, dịch vụ, loại hàng, thùng gỗ): [`api-thiet-lap-danh-muc.md`](api-thiet-lap-danh-muc.md).

## Tổng quan luồng

```
Khách tạo đơn ký gửi                         → Đơn PENDING_REVIEW
   │  mỗi kiện: thùng gỗ (bắt buộc) + dịch vụ kèm kiện (kiểm hàng, bảo hiểm, đóng lại carton...)
   │  → hệ thống tính giá tạm tính, phí thùng + phí dịch vụ theo từng kiện
   ↓
Sale báo giá (không có bước "duyệt đơn")     → Đơn QUOTATION_SENT
   │  phí thùng + dịch vụ của từng kiện tự vào báo giá; Sale sửa phí 1 kiện → Admin duyệt
   ↓  (giá ngoài bảng giá → Admin duyệt giá trước)
Khách xác nhận báo giá + chọn cách trả cọc   → Đơn WAITING_DEPOSIT
   ↓
Tiền cọc về (payOS / SePay webhook, hoặc Admin xác nhận tay)
                                              → Đơn DEPOSIT_PAID
   ↓
Sale lập phiếu nhập kho                      → Phiếu PENDING_APPROVAL
   ↓
Quản lý kho duyệt                            → Phiếu ACTIVE, phát hành PDF có mã WRN-...
   ↓
Khách mang phiếu + hàng tới kho, kho quét mã WRN-
   │  kho thấy từng kiện cần thùng gỗ loại nào, kèm dịch vụ gì
   ├─ nhận đủ, khớp khai báo                 → Phiếu APPROVED (tự chốt, không duyệt lại), đơn CHECKED_IN
   └─ lệch số lượng / hàng ngoài khai báo    → Phiếu RECEIVED hoặc PARTIALLY_RECEIVED, chờ quản lý kho xem
                                               → quản lý kho APPROVED / REJECTED
   ↓
Kho xếp kiện lên kệ                           → Kiện STORED, hàng vào tồn kho
```

## Vai trò (role trong token)

| Vai trò | Làm gì trong luồng này |
|---|---|
| `Customer` | Tạo đơn, xem/từ chối/xác nhận báo giá, trả cọc, xem phiếu nhập kho của mình |
| `Sale` | Báo giá, lập phiếu nhập kho |
| `Admin` | Duyệt giá ngoại lệ, xác nhận tiền chuyển khoản tay, gán quản lý kho |
| Quản lý kho | Tài khoản được gán làm quản lý của kho (`managerId` của kho). Duyệt phiếu nhập kho và biên bản lệch. Kho chưa gán quản lý thì `OperationsManager`/`Admin` duyệt |
| `WarehouseStaff` | Quét phiếu, nhận hàng, xếp kệ |

Quy tắc chung: **người lập phiếu không được tự duyệt phiếu**, người kiểm đếm không tự duyệt biên bản của mình. Admin duyệt thay quản lý kho thì phải ghi lý do.

---

## Bước 0. Đăng ký, đăng nhập

### Khách tự đăng ký (không cần đăng nhập)

1. `POST /api/Auth/customer/register`

```json
{ "fullName": "Nguyễn Văn A", "email": "a@gmail.com", "password": "matkhau123", "phone": "0901234567", "country": "VN", "address": "12 Lê Lợi, Q1, TP.HCM" }
```

Tất cả bắt buộc; `password` ≥ 8 ký tự; `email`, `phone` đúng định dạng. Response `{ "message": "OTP da duoc gui den email cua ban." }`. Email đã đăng ký và xác thực → 400.

2. `POST /api/Auth/customer/verify-otp` `{ "email", "otp" }` → `{ "message": "Dang ky tai khoan thanh cong.", "customerId" }`. OTP hết hạn sau **5 phút**; nhập sai nhiều lần bị khoá, phải gửi lại.
3. Gửi lại OTP: `POST /api/Auth/customer/resend-otp` `{ "email" }` — cách lần gửi trước tối thiểu 60 giây.

Nhân viên **không tự đăng ký** — Admin tạo tài khoản (`POST /api/User`).

### Quên mật khẩu (không cần đăng nhập)

`POST /api/Auth/forgot-password` `{ "email" }` → gửi OTP qua email · `POST /api/Auth/reset-password` `{ "email", "otp", "newPassword" }`.

### `POST /api/Auth/login` — không cần đăng nhập

```json
{ "email": "demo.sale@vcl-demo.example", "password": "..." }
```

Tài khoản chưa xác thực OTP → 401 "Tai khoan chua duoc xac thuc."; bị khoá → 401.

### `POST /api/Auth/google` — không cần đăng nhập

```json
{ "idToken": "<Google ID token FE lấy từ Google>" }
```

Response 200 (cả hai API):

```json
{
  "token": "eyJhbGciOi...",
  "expiresAt": "2026-09-18T10:00:00Z",
  "userId": "guid",
  "fullName": "Nguyễn Văn A",
  "role": "Customer",
  "region": "CN"
}
```

FE dùng `role` để chọn màn hình. 401 `{ "message": "Invalid credentials" }` khi sai tài khoản.

---

## Bước 1. Khách tạo đơn ký gửi

### Dữ liệu đổ form (không cần đăng nhập)

| API | Dùng cho |
|---|---|
| `GET /api/orders/consignments/routes` | Dropdown tuyến hàng (`route`) |
| `GET /api/orders/consignments/shipping-options` | Dropdown phương án vận chuyển (`shippingOption`) |
| `GET /api/product-types` | Loại hàng (`productType`) |
| `GET /api/package-configurations` hoặc `/api/package-configurations/options` | Cấu hình thùng gỗ cho từng kiện (`packageConfigurationId`) |
| `GET /api/orders/consignments/item-services?route=&shippingOption=` | Dịch vụ chọn **theo từng kiện** (`items[].services[].pricingRuleId`): kiểm hàng, bảo hiểm, đóng lại carton... Truyền `route` + `shippingOption` để chỉ lấy dịch vụ áp cho phương án đó |

| `GET /api/orders/consignments/statuses` | Danh sách trạng thái đơn `[ { code, label } ]` cho bộ lọc |

**Kiểm tra hàng cấm trước khi gửi (tuỳ chọn):** `POST /api/orders/consignments/validate-items` `{ "items": [ ...giống items của đơn... ] }` → `{ message, data: { canCreate, results: [ { productName, level, matchedItem, reason } ] } }`. `level = BANNED` là hàng cấm (tạo đơn sẽ bị chặn); các mức khác là cảnh báo, vẫn tạo được.

**Gợi ý thùng theo kích thước (tuỳ chọn):** `POST /api/package-configurations/suggest` `{ length, width, height, weight }` (đăng nhập).

**Ảnh sản phẩm** (đưa URL vào `items[].referenceUrls`):
- `POST /api/uploads/image` — `multipart/form-data`, field `file` → `{ "message", "url" }`
- `POST /api/uploads/images` — field `files` (nhiều file) → `{ "message", "urls": [...] }`
- Đăng nhập; chỉ nhận **jpg, png, webp**, tối đa **5MB** mỗi ảnh; link trả về là link công khai.

### `POST /api/orders/consignments` — role `Customer`

```json
{
  "route": "CN-VN",
  "shippingOption": "ROAD",
  "receiverName": "Nguyễn Văn B",
  "receiverPhone": "0900000000",
  "receiverAddress": "12 Lê Lợi, Q1, TP.HCM",
  "defaultDestinationHandling": "DIRECT_DELIVERY",
  "note": "Hàng dễ vỡ",
  "items": [
    {
      "productName": "Bình gốm",
      "productType": "CERAMIC",
      "quantity": 2,
      "weight": 5.5,
      "length": 40, "width": 30, "height": 30,
      "declaredValue": 1500000,
      "referenceUrls": ["https://..."],
      "domesticTrackingCode": "SF123456789",
      "packageConfigurationId": "guid-thung-go",
      "services": [
        { "pricingRuleId": "guid-kiem-hang" },
        { "pricingRuleId": "guid-bao-hiem" }
      ]
    },
    {
      "productName": "Áo khoác",
      "productType": "CLOTHES",
      "quantity": 1,
      "weight": 1.2,
      "declaredValue": 300000,
      "packageConfigurationId": "guid-thung-go",
      "services": []
    }
  ]
}
```

Bắt buộc: `route`, `shippingOption`, `items` (ít nhất 1 dòng), mỗi dòng có `productName`, `productType`, `quantity >= 1`.
Mỗi dòng `items` là **một kiện** — kho sẽ nhận theo từng dòng này, và **dịch vụ đi theo từng kiện**:

- **Thùng gỗ bắt buộc cho mọi kiện**: dòng nào cũng phải có `packageConfigurationId` (lấy từ `GET /api/package-configurations`). Phí thùng tính theo cấu hình đó.
- **Dịch vụ kèm kiện** `services[]` (tuỳ chọn): mỗi phần tử `{ "pricingRuleId" }` lấy từ `GET /api/orders/consignments/item-services`. Kiện nào không cần thì gửi `[]` hoặc bỏ trống.
  - Phí tính riêng cho kiện đó: `FIXED` = 1 lần/kiện; `PERCENTAGE` = % giá trị khai báo của kiện (có mức tối thiểu/tối đa); `PER_KG` theo cân của kiện.
  - Dịch vụ `PERCENTAGE` (bảo hiểm) thì kiện **phải có `declaredValue` > 0**.
  - Không chọn trùng một dịch vụ trong cùng kiện. Không chọn rule thùng gỗ/thuế/cước nội địa ở đây (400).
- **Đã bỏ** các trường cấp đơn `pricingRuleIds`, `requiresInspection`, `requiresPacking`, `requiresWoodenCrate`, `requiresInsurance`. Gửi lên sẽ bị bỏ qua (không tính phí).

Response **201**:

```json
{
  "message": "Tạo yêu cầu ký gửi thành công.",
  "data": {
    "orderId": "guid",
    "consignmentCode": "VCL-...",
    "status": "PENDING_REVIEW",
    "consignmentType": "ROAD",
    "route": "CN-VN",
    "itemCount": 3,
    "requiresInspection": true,
    "pricingRuleIds": ["guid-kiem-hang", "guid-bao-hiem"],
    "quotation": {
      "quotationId": "guid",
      "status": "DRAFT",
      "totalEstimatedCost": 2350000,
      "items": [
        {
          "id": "guid-dong-hang-1",
          "productName": "Bình gốm",
          "packageConfigurationId": "guid-thung-go",
          "packageConfiguration": { "id": "guid", "configCode": "MEDIUM", "configName": "Medium Box", "length": 30, "width": 20, "height": 15, "packageFee": 25000 },
          "services": [
            { "pricingRuleId": "guid-kiem-hang", "code": "SUR_INSPECTION", "name": "Phụ phí kiểm hàng", "calculationType": "FIXED", "value": 10000 },
            { "pricingRuleId": "guid-bao-hiem", "code": "SUR_INSURANCE_3PERCENT", "name": "Phụ phí bảo hiểm", "calculationType": "PERCENTAGE", "value": 3, "minAmount": 10000 }
          ]
        }
      ],
      "additionalFees": [
        { "feeType": "SURCHARGE", "code": "SUR_INSPECTION", "pricingRuleId": "guid-kiem-hang", "orderItemId": "guid-dong-hang-1", "itemName": "Bình gốm", "amount": 10000 },
        { "feeType": "SURCHARGE", "code": "SUR_INSURANCE_3PERCENT", "pricingRuleId": "guid-bao-hiem", "orderItemId": "guid-dong-hang-1", "itemName": "Bình gốm", "amount": 45000 },
        { "feeType": "PACKING_FEE", "orderItemId": "guid-dong-hang-1", "itemName": "Bình gốm", "amount": 25000 }
      ]
    },
    "warnings": []
  }
}
```

`quotation` ở đây là **giá tạm tính** (`DRAFT`), chỉ để hiển thị tham khảo — khách **không** xác nhận được bản này. Phải chờ Sale gửi báo giá chính thức.

Trong `additionalFees`, dòng có `orderItemId` là phí của riêng kiện đó (`itemName` là tên kiện) — FE nhóm theo kiện để hiển thị. Dòng không có `orderItemId` là phí cả đơn.

Lỗi 400 thường gặp: kiện thiếu/sai cấu hình thùng gỗ, dịch vụ không hợp lệ hoặc chọn trùng, bảo hiểm mà kiện không khai giá trị, vượt giới hạn cân nặng/kích thước/giá trị, hàng thuộc danh mục cấm.

> Sale tạo đơn thay khách: `POST /api/staff/consignments` (role `Sale`), cùng cấu trúc `items[].packageConfigurationId` + `items[].services`. Sale để trống thùng thì hệ thống gợi ý theo kích thước. Đơn tạo ra ở trạng thái `APPROVED` và báo giá như bình thường.

Chi tiết đơn `GET /api/orders/consignments/{orderId}` trả `items[].packageConfiguration` và `items[].services` của từng kiện.

**Thử:** đăng nhập `demo.khach1` tạo đơn mới; đơn mẫu "Đơn 01" có sẵn dịch vụ theo kiện.

### Khách xem đơn

- `GET /api/orders/consignments?pageNumber=1&pageSize=10&status=&searchCode=&orderType=CONSIGNMENT` — khách chỉ thấy đơn của mình.
- `GET /api/orders/consignments/{orderId}` — chi tiết đơn.
- `PUT /api/orders/consignments/{orderId}/cancel` — khách huỷ đơn khi chưa có khoản tiền nào đã trả.

---

## Bước 2. Sale xem đơn mới

- `GET /api/orders/consignments?status=PENDING_REVIEW&orderType=CONSIGNMENT` — **luôn truyền `orderType=CONSIGNMENT`**, không thì đơn mua hộ (PUR-) lẫn vào.
- `GET /api/orders/consignments/{orderId}` — chi tiết.

**Không còn bước "Duyệt đơn".** Sale xem xong báo giá luôn (bước 3). API đổi trạng thái chỉ dùng khi không báo giá được:

### `PUT /api/orders/consignments/{orderId}/status` — role `Sale`, `Admin`

```json
{ "status": "NEED_MORE_INFO", "rejectionReason": "Thiếu ảnh sản phẩm" }
```

- `status` chỉ nhận `REJECTED` hoặc `NEED_MORE_INFO`, **bắt buộc** `rejectionReason`.
- Gửi `APPROVED` sẽ bị 400 — FE bỏ nút "Duyệt đơn".
- Đơn đã có báo giá khách chấp nhận thì không đổi được.

Response 200: `{ "message", "status", "consignmentCode", "receiptPdfUrl" }`.

---

## Bước 3. Sale báo giá

### Dữ liệu cho form báo giá

- `GET /api/warehouses/active` — chọn kho nhận hàng. **Chỉ chọn kho loại `ORIGIN`** (kho nhận hàng quốc tế).
- `GET /api/service-pricings` — bảng giá (lọc theo `serviceType` và tuyến của đơn).
- `GET /api/pricing-rules?orderType=CONSIGNMENT` — các loại phụ phí cả đơn (dùng làm `feeId`).
- Dịch vụ theo kiện khách đã chọn và phí thùng gỗ **hệ thống tự đưa vào báo giá**, Sale không cần nhập lại.

### `POST /api/orders/{orderId}/quotation/send` — role `Sale`, `Admin`

```json
{
  "warehouseId": "guid-kho-ORIGIN",
  "servicePricingId": "guid",
  "serviceType": "ROAD",
  "salesNote": "Báo giá theo cân nặng khai báo",
  "overrideReason": null,
  "quotation": {
    "additionalFees": [
      { "feeId": "guid-pricing-rule", "label": "Phụ phí hàng cồng kềnh", "amount": 50000, "enabled": true },
      { "feeId": "guid-kiem-hang", "orderItemId": "guid-dong-hang-1", "amount": 5000, "enabled": true }
    ],
    "vat": null,
    "importTax": null
  }
}
```

- `warehouseId` **bắt buộc**, phải là kho ORIGIN đang hoạt động. Kho này là kho khách sẽ mang hàng tới.
- Đơn phải đang ở `PENDING_REVIEW`, `NEED_MORE_INFO`, `APPROVED`, `QUOTATION_SENT` hoặc `QUOTATION_REJECTED`.
- Không truyền `servicePricingId` thì hệ thống tự lấy dòng giá theo `serviceType` + tuyến. Chưa có bảng giá → 400 "Admin cần cấu hình giá trước khi báo giá".
- **Giá ngoài bảng giá:** nếu Sale sửa cước quốc tế, phí dịch vụ, phụ phí hoặc thuế khác bảng giá (lệch > 1đ) thì bắt buộc có `overrideReason` (hoặc `salesNote`), và báo giá phải chờ Admin duyệt.
- `additionalFees`:
  - Không có `orderItemId` → phụ phí **cả đơn** Sale thêm. Nếu `feeId` là dịch vụ khách đã chọn theo kiện thì dòng đó bị bỏ qua (đã tự tính, không cộng hai lần) — FE gửi lại nguyên danh sách phí cũ cũng không sao.
  - Có `orderItemId` + `feeId` → **sửa số tiền dịch vụ của đúng kiện đó**. `enabled: false` = miễn phí dịch vụ đó. Kiện không chọn dịch vụ này → 400. Số khác bảng giá → giá ngoại lệ, cần `overrideReason` và Admin duyệt.
- Gửi báo giá mới thì báo giá cũ chưa được chấp nhận tự bị thay (`SUPERSEDED`).

Response 200:

```json
{
  "message": "...",
  "status": "QUOTATION_SENT",
  "consignment": { "...": "thông tin đơn" }
}
```

| `status` trả về | Ý nghĩa cho FE |
|---|---|
| `QUOTATION_SENT` | Khách đã thấy báo giá |
| `PENDING_PRICE_APPROVAL` | Có giá ngoài bảng giá, chờ Admin duyệt; khách **chưa** thấy bản này |

### `PUT /api/quotations/{quotationId}/price-approval` — role `Admin`

```json
{ "decision": "APPROVED", "note": "Đồng ý giảm giá cho khách quen" }
```

- `decision`: `APPROVED` hoặc `REJECTED`. Từ chối bắt buộc `note`.
- Admin lập báo giá thì không tự duyệt được (403).
- Duyệt xong báo giá mới tới tay khách. Từ chối thì Sale gửi lại báo giá khác.

---

## Bước 4. Khách xem, từ chối hoặc xác nhận báo giá và trả cọc

### `GET /api/orders/{orderId}/quotation`

Response 200 `{ "message", "data": QuotationDetail }`. Các trường FE cần:

| Trường | Dùng làm gì |
|---|---|
| `quotationId` | Gọi các API bên dưới |
| `status` | `PENDING` = báo giá chính thức đang chờ khách; `ACCEPTED` = đã xác nhận; `DRAFT` = tạm tính |
| **`canCustomerAccept`** | **Chỉ hiện nút "Xác nhận & thanh toán" khi `true`** |
| `totalEstimatedCost`, `estimatedFreightCharge`, `serviceFee`, `domesticShippingFee`, `vat`, `importTax`, `additionalFees[]`, `parcels[]` | Hiển thị bảng giá. `additionalFees[]` có `orderItemId`/`itemName` là phí thùng + dịch vụ của từng kiện |
| `expiredAt` | Hạn báo giá |
| `salesNote`, `warehouseId` | Ghi chú Sale, kho nhận hàng |
| `priceApprovalStatus`, `overrideReason`, `acceptedAt` | Thông tin duyệt giá / thời điểm khách chấp nhận |

Hoặc lấy theo id: `GET /api/quotations/{quotationId}`.

### `PUT /api/quotations/{quotationId}/reject` — khách từ chối

```json
{ "rejectionReason": "Giá cao quá" }
```

Đơn chuyển `QUOTATION_REJECTED`, Sale có thể báo giá lại. (Tương đương: `POST /api/orders/{orderId}/quotation/reject`.)

### `PUT /api/quotations/{quotationId}/confirm-and-pay` — khách xác nhận + tạo khoản cọc

```json
{
  "paymentMethod": "PAYOS",
  "returnUrl": "https://logictic.site/history/consignment",
  "cancelUrl": "https://logictic.site/history/consignment"
}
```

`paymentMethod`: `PAYOS` (mặc định khi bỏ trống), `SEPAY` hoặc `OFFLINE` (chuyển khoản tay).

Response 200:

```json
{
  "message": "Xác nhận báo giá và tạo thanh toán thành công.",
  "data": {
    "quotationId": "guid",
    "quotationStatus": "ACCEPTED",
    "orderId": "guid",
    "orderStatus": "WAITING_DEPOSIT",
    "consignmentCode": "VCL-...",
    "invoiceNo": "INV-20260917-1234",
    "paymentId": "guid",
    "orderCode": 1726561234,
    "amount": 705000,
    "installmentType": "DEPOSIT",
    "totalBillAmount": 2350000,
    "depositRate": 30,
    "paymentMethod": "PAYOS",
    "paymentStatus": "PENDING",
    "checkoutUrl": "https://pay.payos.vn/...",
    "paymentLinkId": "..."
  }
}
```

| `paymentMethod` | FE làm gì |
|---|---|
| `PAYOS` / `SEPAY` | Mở `checkoutUrl` cho khách trả tiền, sau đó poll trạng thái (dưới) |
| `OFFLINE` | `checkoutUrl = null`. Hiện thông tin chuyển khoản kèm nội dung có `consignmentCode`; chờ Admin xác nhận (bước 5) |

Trường hợp tỷ lệ cọc cấu hình 0%: `amount = 0`, `paymentStatus = "PAID"`, đơn sang `DEPOSIT_PAID` ngay.

Lỗi 400 thường gặp: "Báo giá này đã được xác nhận trước đó", "Báo giá đã hết hạn", "đang chờ Admin duyệt", "đã được thay bằng báo giá mới". 403 khi không phải chủ đơn.

### `GET /api/payments/status/{orderCode}` — không cần đăng nhập, dùng để poll

```json
{ "orderCode": 1726561234, "amount": 705000, "status": "PAID", "paymentMethod": "PAYOS" }
```

`status`: `PENDING` → `PAID`. Poll mỗi 3–5 giây sau khi khách quay lại từ trang thanh toán, rồi tải lại chi tiết đơn.

Khi webhook payOS bị chậm: `GET /api/payments/{orderCode}/sync-status` (đăng nhập) hỏi thẳng payOS → `{ message, data: { orderCode, status, isPaid } }`.

SePay: `checkoutUrl` trỏ tới `GET /api/payments/sepay/checkout/{orderCode}` — trang HTML có mã QR, mở trong tab mới.

### Xem các khoản thanh toán của đơn

- `GET /api/orders/{orderId}/payments` → `{ message, data: { orderId, consignmentCode, orderStatus, totalBillAmount, totalPaid, remaining, payments: [ { paymentId, invoiceId, installmentType, amount, paymentMethod, paymentStatus, orderCode, transactionCode, checkoutUrl, paidAt } ] } }`
- `GET /api/orders/{orderId}/payments/history` → như trên, thêm `customer`, `quotation { quotationId, quoteType, status, totalAmount }`, mỗi khoản có `createdAt`, `failureReason`.
- `installmentType`: `DEPOSIT` (cọc) · `FINAL` (thanh toán cuối) · `STORAGE_FEE` (phí lưu kho).

**Thử:** `demo.khach1` — "Đơn 03" (xác nhận & trả cọc), "Đơn 06" (đã chọn chuyển khoản, xem khoản chờ). `demo.khach2` — "Đơn 15".

---

## Bước 5. Hệ thống xác nhận tiền cọc

FE **không** gọi API nào để đổi trạng thái đơn sau khi trả tiền. Tiền được xác nhận ở một chỗ duy nhất:

- payOS: webhook `POST /api/payments/payos/webhook` (server gọi).
- SePay: webhook `POST /api/payments/sepay-webhook` (server gọi). **Hiện production chưa cấu hình khoá SePay nên webhook bị từ chối — tiền SePay cần Admin xác nhận tay như OFFLINE.**
- Chuyển khoản tay / SePay: Admin xác nhận (dưới).

Khi cọc được xác nhận: khoản thu `PAID`, đơn sang **`DEPOSIT_PAID`**, Sale nhận thông báo "cần lập phiếu nhập kho".

### Màn Admin đối soát

`GET /api/admin/finance/transactions/pending-approval` — role `Admin`. Response `{ "message", "data": [...] }`, mỗi dòng:
`paymentId, consignmentCode, customerName, amount, paymentMethod, status, installmentType, orderStatus, orderCode, createdAt, waitingDays`.

`PUT /api/admin/finance/transactions/{paymentId}/approve`

```json
{ "transactionCode": "FT26260123456", "receivedAmount": 705000, "note": "Đã thấy trong sao kê VCB" }
```

- `transactionCode`: mã giao dịch ngân hàng. Mã đã dùng cho khoản khác → 400.
- `receivedAmount`: số tiền thực nhận. **Nhỏ hơn số cần trả → 400** (không xác nhận thiếu).
- Response `{ "message", "data": { paymentId, paymentStatus, orderStatusBefore, orderStatusAfter, amount, transactionCode, paidAt, ... } }`.
- `paymentStatus = "RECEIVED_UNALLOCATED"`: tiền về cho đơn đã đóng/khoản đã huỷ — tiền được ghi nhận nhưng đơn không đổi, Admin xử lý tiếp.

`PUT /api/admin/finance/transactions/{paymentId}/reject` — `{ "reason": "Không thấy tiền về" }` (bắt buộc lý do).

### Tổng quan tiền (Admin)

| API | Response |
|---|---|
| `GET /api/admin/finance/summary?from=&to=` | `{ message, data: { totalBillAmount, totalPaid, remaining, orderCount, paidCount, partialCount, unpaidCount } }` |
| `GET /api/admin/finance/transactions?PageNumber=&PageSize=&From=&To=` | Giao dịch: `data.items[ { paymentId, orderId, consignmentCode, amount, paymentMethod, status, paidAt } ]` + phân trang |
| `GET /api/admin/finance/orders?PageNumber=&PageSize=&PaymentStatus=&From=&To=&Search=` | Công nợ theo đơn: `data.items[ { orderId, consignmentCode, customerName, customerCode, orderStatus, totalBillAmount, totalPaid, remaining, paymentStatus, lastPaidAt } ]` |

**Thử:** `demo.admin` — "Đơn 06" nằm trong `pending-approval`.

---

## Bước 6. Sale lập phiếu nhập kho

### Tìm đơn cần lập phiếu

`GET /api/orders/action-queue` — role `Sale`, `Admin`, `OperationsManager`. Response `{ "message", "data": { "items": [...] } }`.

Đơn đã cọc chưa có phiếu hiện ra với:

```json
{
  "orderId": "guid",
  "orderCode": "VCL-...",
  "status": "DEPOSIT_PAID",
  "customerName": "...",
  "actionState": "AWAITING_RECEIVING_NOTE",
  "nextAction": "CREATE_RECEIVING_NOTE",
  "canAct": true
}
```

Hoặc lọc danh sách: `GET /api/orders/consignments?status=DEPOSIT_PAID&orderType=CONSIGNMENT`.

**Thử:** `demo.sale` — "Đơn 07" hiện trong `action-queue` với `CREATE_RECEIVING_NOTE`.

> Không dùng `PUT /api/orders/consignments/{orderId}/notify-warehouse` cho bước này — API đó thuộc luồng gửi kho sau tất toán, không phải phiếu nhập kho ký gửi.

### `POST /api/warehouse-receiving-notes` — role `Sale`, `Admin`

```json
{
  "consignmentOrderId": "guid-đơn",
  "warehouseId": "guid-kho-ORIGIN",
  "warehouseNote": "Khách hẹn mang hàng tới sáng thứ 6"
}
```

- Đơn phải đang **`DEPOSIT_PAID`**; không phải đơn mua hộ.
- `warehouseId` phải là kho `ORIGIN` đang hoạt động (nên mặc định là `warehouseId` trong báo giá).
- Mỗi đơn chỉ có 1 phiếu đang mở. Đã có → **409**.

Response **201** (lưu ý key là `receivingNote`, không phải `data`):

```json
{
  "message": "Gửi phiếu tiếp nhận kho thành công.",
  "receivingNote": {
    "id": "guid",
    "receivingNoteCode": "WRN-...",
    "consignmentOrderId": "guid",
    "warehouseId": "guid",
    "warehouseName": "Kho Quảng Châu",
    "status": "PENDING_APPROVAL",
    "createdByName": "Sale A",
    "expectedItems": [ { "orderItemId": "guid", "productName": "Bình gốm", "quantity": 2, "weight": 5.5 } ],
    "...": "các trường chi tiết khác (xem bước 7)"
  }
}
```

Quản lý kho (hoặc OM/Admin nếu kho chưa có quản lý) nhận thông báo có phiếu chờ duyệt.

---

## Bước 7. Quản lý kho duyệt phiếu nhập kho

### Danh sách phiếu chờ duyệt

`GET /api/warehouse-receiving-notes?status=PENDING_APPROVAL&warehouseId=&search=&pageNumber=1&pageSize=20`

Response `{ "message", "data": { "items": [...], "totalCount", "pageNumber", "pageSize", "totalPages" } }`, mỗi phần tử `items`:

| Trường | Ghi chú |
|---|---|
| `id`, `receivingNoteCode`, `consignmentOrderId`, `consignmentCode` | |
| `warehouseId`, `warehouseName`, `customerName`, `customerPhone` | |
| `status`, `statusText` | |
| `awaitingApproval` | `true` = đang chờ quyết định |
| **`approvalStage`** | **`RECEIVE`** = duyệt cho khách mang hàng tới (bước này); **`DISCREPANCY`** = xem biên bản lệch (bước 8b) |
| `declaredItemCount`, `checkedItemCount`, `hasDiscrepancy`, `parcelCount` | |

Phiếu chờ duyệt luôn nằm đầu danh sách. Bỏ `status` để lấy tất cả.

### Chi tiết phiếu

`GET /api/warehouse-receiving-notes/{receivingNoteId}` — role `Admin`, `Sale`, `WarehouseStaff`, `OperationsManager`. Response `{ "message", "data": ReceivingNoteDetail }`:

| Nhóm | Trường |
|---|---|
| Phiếu | `id, receivingNoteCode, status, warehouseId, warehouseName, warehouseNote, createdAt` |
| Đơn / khách | `consignmentOrderId, consignmentCode, customerName, customerCode, customerPhone, route` |
| Hàng khai báo | `expectedItems[]: orderItemId, productName, productType, quantity, weight, declaredValue, packageConfiguration, services[]` |
| Người lập / duyệt | `createdByName`, `receiveApprovedByName`, `receiveApprovedAt`, `approvalNote` |
| Nhận hàng | `receivedByName, receivedAt`, `items[]` (biên bản đối chiếu), `parcels[]` (kiện đã nhận) |
| Cờ cho FE | `requiresReview` (có chênh lệch cần duyệt), `hasDiscrepancy`, `canPutAway` (được xếp kệ chưa) |
| Kết quả | `approvedByName, approvedAt, rejectionReason`, **`receiptPdfUrl`** |

Theo đơn: `GET /api/warehouse-receiving-notes/by-consignment/{consignmentOrderId}` (trả thẳng object, không bọc `data`; 404 khi chưa có phiếu đang hoạt động).

### `PUT /api/warehouse-receiving-notes/{receivingNoteId}/status` — quản lý kho quyết định

Duyệt:

```json
{ "status": "APPROVED", "reason": "Đã hẹn khách" }
```

Từ chối:

```json
{ "status": "REJECTED", "rejectionReason": "Sai kho nhận, lập lại phiếu tại kho Đông Hưng" }
```

- `status` chỉ nhận `APPROVED` / `REJECTED`. Từ chối bắt buộc `rejectionReason`.
- `reason`: ghi chú khi duyệt. **Admin duyệt thay quản lý kho thì bắt buộc `reason`.**
- Hệ thống kiểm lại cọc lúc duyệt: đơn bị huỷ/khoản cọc không còn → 400.

Ai được bấm (FE nên ẩn nút với người không đủ quyền, server vẫn chặn):

| Tình huống | Kết quả |
|---|---|
| Người đang đăng nhập là quản lý của kho (`managerId`) và không phải người lập phiếu | Được |
| Admin, kho đã có quản lý | Được nếu có `reason` |
| Kho chưa gán quản lý | `OperationsManager` / `Admin` được |
| Người lập phiếu tự duyệt | **403** |
| Người khác | **403** "Chỉ quản lý kho ... được quyết định phiếu tiếp nhận." |

Response 200 `{ "message", "data": ReceivingNoteDetail }`:

- Duyệt → `status: "ACTIVE"`, `receiveApprovedByName/At` có giá trị, **`receiptPdfUrl`** là link PDF phiếu nhập kho (có mã WRN-). Khách nhận thông báo kèm mã phiếu và link.
- Từ chối → `status: "REJECTED"`, người lập nhận thông báo. Sale lập phiếu mới (phiếu bị từ chối không chặn lập lại).

### Gán quản lý kho (Admin làm một lần cho mỗi kho)

- `GET /api/warehouses/{warehouseId}/manager` → `{ warehouseId, warehouseName, managerId, managerName, history[] }`
- `PUT /api/warehouses/{warehouseId}/manager` — role `Admin`

```json
{ "managerId": "guid-user", "reason": "Phân công quản lý kho Quảng Châu" }
```

`managerId: null` để bỏ gán (khi đó OM/Admin duyệt).

### Phía khách xem phiếu

`GET /api/warehouse-receiving-notes/my-order/{orderId}` — khách đăng nhập.

- `data: null` + message "Đơn hàng chưa có phiếu tiếp nhận kho." khi chưa có phiếu.
- Có phiếu: cùng cấu trúc chi tiết như trên. Khi `status = "ACTIVE"` hiện nút **"Tải phiếu nhập kho"** dùng `receiptPdfUrl`, kèm hướng dẫn mang phiếu + hàng tới kho `warehouseName`.
- 403 khi xem đơn người khác.

Nhân viên tải lại PDF: `GET /api/orders/consignments/{orderId}/receipt` (role `Admin`, `Sale`, `WarehouseStaff`, `OperationsManager`, trả file PDF).

`receiptPdfUrl` hiện có dạng `https://api-vcl.zushin.io.vn/api/public/receipts/{publicKey}` (host lấy từ cấu hình cũ, trỏ sang bản deploy code cũ) — FE nên gọi `https://vcl.henrytech.cloud/api/public/receipts/{publicKey}`. **Không cần đăng nhập**, trả `application/pdf`; thêm `?download=true` để tải về thay vì xem.

**Thử:** `demo.quanlykho` — "Đơn 08" (phiếu chờ duyệt). `demo.khach1` — "Đơn 09" xem phiếu `ACTIVE` và mở PDF.

---

## Bước 8. Khách mang phiếu + hàng tới kho — kho kiểm và nhận

Kho **không lập phiếu mới** ở bước này. Nhận hàng theo đúng phiếu đã duyệt.

### 8.1 Quét mã phiếu

`GET /api/warehouse-receiving-notes/scan?code=WRN-...` — role `WarehouseStaff`, `Admin`.

```json
{
  "message": "Quét phiếu tiếp nhận kho thành công.",
  "data": {
    "receivingNoteId": "guid",
    "receivingNoteCode": "WRN-...",
    "status": "ACTIVE",
    "consignmentOrderId": "guid",
    "consignmentCode": "VCL-...",
    "customerName": "...", "customerCode": "...",
    "warehouseId": "guid", "warehouseName": "...",
    "warehouseNote": "...",
    "expectedItems": [
      {
        "orderItemId": "guid", "productName": "Bình gốm", "productType": "CERAMIC", "quantity": 2, "weight": 5.5,
        "packageConfiguration": { "configCode": "MEDIUM", "configName": "Medium Box", "length": 30, "width": 20, "height": 15 },
        "services": [ { "code": "SUR_INSPECTION", "name": "Phụ phí kiểm hàng" }, { "code": "SUR_INSURANCE_3PERCENT", "name": "Phụ phí bảo hiểm" } ]
      }
    ]
  }
}
```

FE kiểm `status`:

| `status` | Hiển thị |
|---|---|
| `PENDING_APPROVAL` | "Phiếu chưa được quản lý kho duyệt" — không cho nhận |
| `ACTIVE`, `PARTIALLY_RECEIVED` | Cho nhận hàng |
| `RECEIVED`, `APPROVED` | Đã chốt kiểm đếm |
| `REJECTED` | Phiếu bị từ chối |

Kiểm thêm `warehouseId` trùng kho nhân viên đang đứng.

**Thử:** `demo.kho.cn` quét `WRN-15984094` ("Đơn 09", 2 kiện chưa nhận) hoặc `WRN-65978595` ("Đơn 10", còn 1 kiện).

### 8.2 Nhận hàng — chọn **một** trong hai cách

#### Cách A (khuyên dùng khi hàng khớp): cân đo từng kiện — `POST /api/parcels/check-in`

Role `WarehouseStaff`, `Admin`, `OperationsManager`.

```json
{
  "shippingCode": "WRN-...",
  "packages": [
    { "orderItemId": "guid-dòng-hàng-1", "actualWeight": 5.6, "length": 41, "width": 30, "height": 31 },
    { "orderItemId": "guid-dòng-hàng-2", "actualWeight": 3.2, "length": 30, "width": 20, "height": 20 }
  ]
}
```

- `shippingCode` nhận mã phiếu `WRN-...` (hoặc mã đơn `VCL-...`).
- Mỗi phần tử `packages` là một kiện, `orderItemId` lấy từ `expectedItems`. Có thể nhận nhiều đợt.
- `actualWeight > 0`; kích thước nếu gửi phải > 0 và không vượt ngưỡng.
- Điều kiện server kiểm: đơn chưa đóng, **đã có cọc được xác nhận**, phiếu **đã được quản lý kho duyệt** và đang `ACTIVE`/`PARTIALLY_RECEIVED`.

Response 200 `{ "message": "Check-in kiện hàng thành công.", "data": ParcelCheckIn }`, trường chính:

| Trường | Ý nghĩa |
|---|---|
| `parcels[]` | Các kiện vừa tạo: `parcelId, packageCode, actualWeight, chargeableWeight, packageStatus = RECEIVED, weightDifference, warnings[]` |
| `declaredPackageCount`, `receivedPackageCount`, `remainingPackageCount` | Tiến độ |
| `isFullyReceived` | Đã nhận đủ kiện chưa |
| `packageProgress[]` | `orderItemId, productName, quantity, isReceived, packageCode, actualWeight, packageConfiguration, services[]` — vẽ checklist, hiện thùng gỗ + dịch vụ kho phải làm cho từng kiện |
| `orderStatus` | `CHECKED_IN` khi đủ kiện |
| `warnings[]` | Cảnh báo lệch cân nặng so với khai báo (chỉ cảnh báo, không chặn) |

Kết quả với phiếu:
- Nhận **đủ** kiện → phiếu tự chuyển **`APPROVED`** (khớp phiếu đã duyệt, không duyệt lại). Đi tiếp bước 9.
- Chưa đủ → phiếu `PARTIALLY_RECEIVED`, khách mang tiếp đợt sau, quét lại mã WRN-.

Lỗi 400 thường gặp: "chưa có cọc được xác nhận", "Phiếu ... chưa được quản lý kho duyệt", "đã chốt kiểm đếm", "Kiện ... đã được check-in trước đó".

#### Cách B (khi cần ghi biên bản số lượng, có lệch): `POST /api/warehouse-receiving-notes/{receivingNoteId}/confirm-receipt`

Role `WarehouseStaff`, `Admin`.

```json
{
  "warehouseNote": "Khách mang thiếu 1 bình",
  "items": [
    { "orderItemId": "guid-dòng-hàng-1", "actualQuantity": 1, "actualWeight": 2.8, "note": "Thiếu 1" },
    { "orderItemId": null, "productName": "Hộp quà", "productType": "OTHER", "actualQuantity": 1, "note": "Hàng ngoài khai báo" }
  ],
  "parcels": [
    { "orderItemId": "guid-dòng-hàng-1", "actualWeight": 2.8, "length": 40, "width": 30, "height": 30 }
  ]
}
```

- `items` bắt buộc ít nhất 1 dòng. `actualQuantity >= 0`.
- Dòng có `orderItemId` = đối chiếu với hàng khai báo; `orderItemId: null` = hàng phát sinh ngoài khai báo (bắt buộc `productName`).
- `parcels` (tuỳ chọn): cân đo kiện ngay trong lần xác nhận, hệ thống tạo kiện như cách A.
- Mỗi dòng khai báo chỉ kiểm đếm một lần (lần 2 → 409).

Response 200 `{ "message": "Xác nhận nhập kho thành công.", "data": ReceivingNoteDetail }`. Phiếu sau kiểm đếm:

| Kết quả kiểm đếm | `status` phiếu | FE làm gì |
|---|---|---|
| Đếm đủ mọi dòng, không lệch | `APPROVED` | Cho xếp kệ (`canPutAway = true`) |
| Chưa đếm hết dòng, không lệch | `PARTIALLY_RECEIVED` | Chờ đợt sau |
| Có dòng lệch số lượng hoặc hàng ngoài khai báo | `RECEIVED` / `PARTIALLY_RECEIVED`, `requiresReview = true` | Hiện "Chờ quản lý kho xem chênh lệch", khoá nút xếp kệ |

Khi có lệch, quản lý kho nhận thông báo.

### 8.3 Quản lý kho xem chênh lệch

**Thử:** `demo.quanlykho` — "Đơn 11" (`WRN-73021520`, khai 3 nhận 2, `approvalStage = DISCREPANCY`). Người kiểm đếm (`demo.kho.cn`) không tự duyệt được.

Danh sách: `GET /api/warehouse-receiving-notes?status=RECEIVED` (hoặc `PARTIALLY_RECEIVED`), lọc dòng `approvalStage = "DISCREPANCY"`.

Chi tiết: `GET /api/warehouse-receiving-notes/{id}` — hiển thị `items[]`:
`productName, declaredQuantity, actualQuantity, quantityDifference, declaredWeight, actualWeight, weightDifference, note`.

Quyết định: cùng API bước 7

```json
{ "status": "APPROVED", "reason": "Khách xác nhận chỉ gửi 1 bình" }
```

```json
{ "status": "REJECTED", "rejectionReason": "Hàng không khớp, trả lại khách" }
```

- Quy tắc quyền giống bước 7, thêm: **người đã kiểm đếm phiếu không tự duyệt biên bản** (403).
- `APPROVED` → cho xếp kệ. `REJECTED` → kho nhận thông báo, kiện không được xếp kệ.

---

## Bước 9. Xếp kiện lên kệ (hàng chính thức vào kho)

**Thử:** `demo.kho.cn` — "Đơn 12" có 2 kiện chờ xếp; quét `BIN-A1-02` (khu lưu kho, được) hoặc `BIN-OUT-01` (khu xuất, bị chặn).

> Chi tiết đầy đủ về loại khu, xếp kệ, chuyển ô, kiện sai khu, tách/gộp kiện: [`api-kho-xep-ke-theo-khu.md`](api-kho-xep-ke-theo-khu.md).

### `GET /api/parcels/pending-putaway?pageNumber=1&pageSize=10&search=&status=`

Response `{ "message", "data": { "items": [...], "totalCount", "pageNumber", "pageSize", "totalPages" } }`, mỗi phần tử `items`: `id, packageCode, consignmentCode, customerName, actualWeight, length, width, height, chargeableWeight, packageStatus, checkInTime, orderItemId, productName, productType, quantity, packageConfiguration, services[]`.

Chi tiết một kiện: `GET /api/parcels/{parcelId}` — cũng trả `packageConfiguration` và `services[]`.

### Loại khu trong kho

Mỗi khu có một loại. **Hàng lưu kho chỉ được xếp vào ô thuộc khu `STORAGE` đang dùng** — server chặn mọi luồng xếp kệ, chuyển ô, tách/gộp kiện, nhận hàng hoàn vào ô khu khác.

| `zoneType` | Khu | Xếp hàng lưu kho? |
|---|---|---|
| `RECEIVING` | Khu nhận — hàng vừa check-in | Không |
| `QUARANTINE` | Khu cách ly — hàng lệch/hỏng chờ quản lý kho | Không |
| `STORAGE` | Khu lưu kho | **Có** |
| `OUTBOUND` | Khu xuất — hàng đã soạn chờ bàn giao | Không |
| (trống) | Khu chưa phân loại | Không |

- `GET /api/warehouses/{warehouseId}/zones` — danh sách khu: `zoneId, zoneCode, zoneName, zoneType, zoneTypeText, status (ACTIVE/INACTIVE), acceptsStorage, shelfCount, binCount, activeBinCount, parcelCount`.
- `PUT /api/warehouse-zones/{zoneId}` — role `Admin`, `OperationsManager`: `{ "zoneType": "STORAGE", "status": "ACTIVE", "zoneName": "Khu A", "zoneCode": "KHA" }`. Khu đang chứa hàng không đổi sang loại khác hay ngừng dùng được (400).
- Khai báo ô kệ `POST /api/warehouses/{warehouseId}/locations`: `zoneName` là khu **mới** thì bắt buộc gửi `zoneType`; khu đã có thì bỏ trống (gửi loại khác → 400, đổi loại qua API khu ở trên). Response có thêm `zoneType`.

### Chọn vị trí

- **Khuyên dùng:** nhân viên **quét mã ô kệ** rồi gửi `binCode` — không cần chọn khu/kệ.
- Gợi ý vị trí: `POST /api/ai/warehouse/putaway-suggestions` `{ "parcelId", "warehouseId", "preferredZoneId"? }` — chỉ trả ô thuộc khu lưu kho còn đủ chỗ.
- Sơ đồ: `GET /api/warehouses/{warehouseId}/layout/zones` (mỗi khu có `zoneType`, `status`).

### `PUT /api/parcels/{parcelId}/put-away` — role `WarehouseStaff`, `Admin`, `OperationsManager`

Quét mã ô:

```json
{ "warehouseId": "guid", "binCode": "BIN-A1-02" }
```

Hoặc chọn theo ID (client cũ gửi đủ 4 ID vẫn chạy; `zoneId`/`shelfId` nếu gửi thì phải khớp ô):

```json
{ "warehouseId": "guid", "binId": "guid", "zoneId": "guid", "shelfId": "guid" }
```

- `warehouseId` phải **đúng kho của phiếu nhập kho**. `binCode` tra trong kho này (không phân biệt hoa thường).
- Kiện phải `RECEIVED`; phiếu phải cho phép xếp kệ (`APPROVED`, hoặc đã nhận không lệch).
- Ô kệ: **thuộc khu `STORAGE` đang dùng**, ô đang dùng, còn đủ thể tích (cm³) và tải trọng (kg). Sức chứa tính từ kiện đang thật sự nằm trong ô.

Response 200:

```json
{
  "message": "Nhập vị trí kho cho kiện hàng thành công.",
  "data": { "packageCode": "...", "warehouseName": "...", "zoneName": "Khu A", "zoneType": "STORAGE", "shelfCode": "...", "binCode": "BIN-A1-02", "packageStatus": "STORED", "putAwayTime": "..." }
}
```

Lỗi thường gặp:
- 400 "Ô kệ BIN-OUT-01 thuộc khu xuất "Khu xuất". Hàng lưu kho chỉ được xếp vào khu loại STORAGE."
- 400 "... khu chưa phân loại ... nhờ quản lý kho khai loại cho khu này."
- 404 "Không tìm thấy ô kệ mã ... trong kho đã chọn."
- 400 "Phiếu tiếp nhận ... đang chờ quản lý kho duyệt cho nhận hàng." / "Biên bản phiếu ... có chênh lệch ..." / "Phiếu tiếp nhận ... đã bị từ chối: ..."
- 400 "Kiện ... được duyệt nhận tại kho khác với kho đang xếp kệ."
- 400 "Ô kệ ... không đủ sức chứa / không đủ tải trọng / đang ngừng sử dụng." / "Khu ... đang ngừng sử dụng."

### Chuyển ô, kiện nằm sai khu

- `POST /api/inventories/{inventoryId}/relocate` `{ "toBinCode": "BIN-A1-01", "reason": "Sắp xếp lại" }` (hoặc `toBinId`). Ô đích phải là ô khu lưu kho cùng kho. Chuyển **từ** ô sai khu về khu lưu kho thì được.
- `GET /api/warehouses/{warehouseId}/misplaced-parcels` — kiện đang nằm ở ô không hợp lệ cho hàng lưu kho (khu nhận/xuất/cách ly, khu chưa phân loại, khu/ô ngừng dùng), kèm `reason`. Kho dùng để chuyển hàng về khu lưu kho; hệ thống không tự di chuyển.
- Tách kiện `POST /api/parcels/{parcelId}/split` / gộp kiện `POST /api/parcels/merge`: `binId` của kiện con/kiện gộp cũng phải là ô khu lưu kho. Kiện chưa lên kệ thì tách xong mới xếp kệ từng kiện con.

---

## Theo dõi lịch sử đơn (mọi bước)

`GET /api/orders/consignments/{orderId}/timeline` — khách (đơn của mình) và nhân viên.

```json
{
  "message": "Lấy lịch sử đơn thành công.",
  "data": [
    { "event": "PAYMENT_CONFIRMED_DEPOSIT", "fromStatus": "WAITING_DEPOSIT", "toStatus": "DEPOSIT_PAID", "note": "...", "actorName": "Hệ thống", "at": "2026-09-17T08:00:00Z" },
    { "event": "RECEIVING_NOTE_APPROVED_TO_RECEIVE", "fromStatus": "DEPOSIT_PAID", "toStatus": "DEPOSIT_PAID", "note": "Phiếu WRN-... tại kho ...", "actorName": "Quản lý kho", "at": "..." }
  ]
}
```

Các `event` trong phần này: `QUOTATION_SENT`, `QUOTATION_SUBMITTED_FOR_PRICE_APPROVAL`, `QUOTATION_PRICE_APPROVED`, `QUOTATION_PRICE_REJECTED`, `QUOTATION_REJECTED`, `QUOTATION_ACCEPTED`, `PAYMENT_CONFIRMED_DEPOSIT`, `PAYMENT_RECEIVED_UNALLOCATED`, `RECEIVING_NOTE_CREATED`, `RECEIVING_NOTE_APPROVED_TO_RECEIVE`, `RECEIVING_NOTE_REJECTED`, `RECEIVING_NOTE_COUNTED`, `RECEIVING_COUNT_APPROVED`, `RECEIVING_COUNT_REJECTED`, `ORDER_CANCELLED_BY_CUSTOMER`.

---

## Bảng trạng thái

### Đơn ký gửi (trong phần này)

| Trạng thái | Ý nghĩa | Ai làm bước tiếp |
|---|---|---|
| `PENDING_REVIEW` | Khách vừa tạo | Sale báo giá |
| `APPROVED` | Đơn do Sale tạo thay khách | Sale báo giá |
| `NEED_MORE_INFO` | Sale cần khách bổ sung | Khách / Sale |
| `REJECTED` | Sale từ chối nhận | — |
| `QUOTATION_SENT` | Đã gửi báo giá chính thức | Khách |
| `QUOTATION_REJECTED` | Khách từ chối báo giá | Sale báo giá lại |
| `WAITING_DEPOSIT` | Khách đã xác nhận, chờ tiền cọc | Khách trả / Admin xác nhận |
| `DEPOSIT_PAID` | Cọc đã xác nhận | Sale lập phiếu nhập kho |
| `CHECKED_IN` | Kho đã nhận đủ kiện | Kho xếp kệ |
| `WAREHOUSE_RECEIVED` | Kiện đã lên kệ | (phần 2) |
| `CANCELLED` | Đã huỷ | — |

### Báo giá

| Trạng thái | Khách thấy? | Ý nghĩa |
|---|---|---|
| `DRAFT` | Có (tham khảo) | Giá tạm tính lúc tạo đơn, không xác nhận được |
| `PENDING` | Có | Báo giá chính thức chờ khách |
| `PENDING_PRICE_APPROVAL` | Không | Giá ngoài bảng giá, chờ Admin |
| `PRICE_REJECTED` | Không | Admin từ chối giá ngoại lệ |
| `ACCEPTED` | Có | Khách đã xác nhận |
| `REJECTED` | Có | Khách từ chối |
| `SUPERSEDED` | Không | Bị báo giá mới thay |

### Phiếu nhập kho

| Trạng thái | Ý nghĩa | Bước tiếp |
|---|---|---|
| `PENDING_APPROVAL` | Sale vừa lập | Quản lý kho duyệt (`approvalStage = RECEIVE`) |
| `ACTIVE` | Được duyệt, đã có PDF | Khách mang hàng tới |
| `PARTIALLY_RECEIVED` | Đã nhận một phần | Nhận tiếp / quản lý xem nếu `requiresReview` |
| `RECEIVED` | Đếm đủ nhưng có chênh lệch | Quản lý kho xem (`approvalStage = DISCREPANCY`) |
| `APPROVED` | Chốt nhận hàng | Xếp kệ |
| `REJECTED` | Bị từ chối (trước hoặc sau kiểm đếm) | Sale lập lại / xử lý hàng |

### Khoản thanh toán

| Trạng thái | Ý nghĩa |
|---|---|
| `PENDING` | Chờ tiền về |
| `PAID` | Đã xác nhận |
| `RECEIVED_UNALLOCATED` | Tiền về nhưng đơn đã đóng / khoản đã huỷ — Admin xử lý |
| `CANCELLED` | Đã huỷ |

## Mã lỗi HTTP

| Mã | Khi nào | FE xử lý |
|---|---|---|
| 400 | Sai dữ liệu hoặc sai bước nghiệp vụ | Hiện `message` |
| 401 | Chưa đăng nhập / token hết hạn | Về trang đăng nhập |
| 403 | Không đủ quyền (không phải chủ đơn, tự duyệt phiếu của mình, không phải quản lý kho) | Hiện `message`, ẩn nút |
| 404 | Không tìm thấy | Hiện `message` |
| 409 | Trùng (đơn đã có phiếu đang mở, dòng đã kiểm đếm) hoặc dữ liệu vừa bị người khác sửa | Tải lại dữ liệu rồi thao tác lại |

## Các API cũ đã bỏ / đổi (FE cần gỡ)

- Nút **"Duyệt đơn"** (`PUT .../status` với `APPROVED`) — bỏ, Sale báo giá luôn.
- `PUT /api/quotations/{id}/accept` — đã gỡ (404). Khách xác nhận là thanh toán luôn: `PUT /api/quotations/{id}/confirm-and-pay`.
- `PUT /api/orders/{id}/payment-confirmation` — đã gỡ (404). Bỏ nút "Xác nhận thanh toán" của Sale; tiền do webhook hoặc Admin xác nhận.
- Kho **không lập phiếu nhập kho** nữa và không check-in đơn chưa có phiếu được duyệt — phiếu do Sale lập, quản lý kho duyệt.
- `POST /api/orders/{orderId}/quotation/send` giờ **bắt buộc `warehouseId`**.
- Tạo đơn ký gửi: **bỏ** `pricingRuleIds`, `requiresInspection`, `requiresPacking`, `requiresWoodenCrate`, `requiresInsurance` ở cấp đơn. Dịch vụ chọn theo từng kiện `items[].services[]`, thùng gỗ theo `items[].packageConfigurationId`. Form tạo đơn đổi từ "tick dịch vụ cho cả đơn" sang "tick dịch vụ trong từng kiện".

Chi tiết thay đổi phía backend: `VietnamCrossBorderLogictisSystem/MIGRATION_KY_GUI_DOT1.md`.
