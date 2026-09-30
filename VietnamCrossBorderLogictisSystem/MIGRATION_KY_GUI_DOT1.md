# Migrate luồng ký gửi — Đợt 1: từ đặt đơn đến nhập kho quốc tế

Nhánh: `migration/ky-gui` (tách từ `dev` @ `bdbbd52`). Môi trường thử: container `vcl-migration-api`, port **8088**, DB `AppDb_migration` (bản sao `AppDb` ngày 17/09/2026).

Đợt này áp cho phần ký gửi từ lúc khách đặt đơn tới lúc hàng nằm trên kệ kho quốc tế. Xuất kho, vận chuyển, về VN, giao hàng làm ở đợt sau. Luồng cố định trong code, không có cờ cấu hình.

## 1. Luồng

```text
1. Khách đặt đơn (API cũ)                         → PENDING_REVIEW
     báo giá tạm tính sinh kèm chỉ để khách xem, không xác nhận được
2. Sale báo giá (không cần bấm duyệt đơn trước)    → QUOTATION_SENT
     bắt buộc kho nhận hàng (ORIGIN) + bảng giá hợp lệ
     giá ngoài bảng giá → PENDING_PRICE_APPROVAL → Admin (khác người lập) duyệt → tới khách
     cần thì Sale từ chối / yêu cầu bổ sung (NEED_MORE_INFO), bắt buộc lý do
3. Khách xác nhận báo giá + thanh toán cọc          → WAITING_DEPOSIT
4. Thanh toán xong hệ thống cập nhật                → DEPOSIT_PAID
     webhook SePay/payOS đã xác thực; chuyển khoản tay thì Admin xác nhận số tiền thực nhận
     hệ thống báo Sale lập phiếu nhập kho
5. Sale lập phiếu nhập kho                         → phiếu PENDING_APPROVAL
6. Quản lý kho duyệt (khác người lập)               → phiếu ACTIVE
     phát hành phiếu PDF (in mã phiếu WRN-), báo khách mang phiếu + hàng tới kho
7. Khách mang phiếu + hàng tới; kho quét mã WRN- (hoặc mã đơn VCL-), kiểm hàng
     khớp → nhập, xếp kệ luôn; không lập phiếu mới, không duyệt lại
     thiếu / thừa / sai hàng → quản lý kho xem biên bản → APPROVED hoặc REJECTED
```

Mọi bước ghi lịch sử, xem ở `GET /api/orders/consignments/{id}/timeline`.

## 2. Thay đổi FE phải xử lý

### API bỏ

| API | Lý do | FE làm gì |
|---|---|---|
| `PUT /api/quotations/{id}/accept` | Khách xác nhận là phải thanh toán luôn; xác nhận riêng làm đơn kẹt (không thanh toán tiếp được) | Dùng `PUT /api/quotations/{id}/confirm-and-pay` |
| `PUT /api/orders/{id}/payment-confirmation` | Thanh toán xong hệ thống tự cập nhật; chuyển khoản tay do Admin xác nhận | Bỏ nút "Xác nhận thanh toán" của Sale |
| Trạng thái `APPROVED`, `PENDING_REVIEW` ở `PUT /api/orders/consignments/{id}/status` | Sale báo giá luôn, không duyệt đơn trước | Bỏ nút "Duyệt đơn". Endpoint chỉ còn `REJECTED` / `NEED_MORE_INFO` |

### API đổi hành vi

| API | Trước | Sau |
|---|---|---|
| `PUT /api/orders/consignments/{id}/status` | Admin, Sale, WarehouseStaff, OM, Delivery; đổi được cả đơn đã cọc | Chỉ Admin/Sale; bắt buộc `rejectionReason`; chặn khi đã có báo giá được chấp nhận |
| `POST /api/orders/{id}/quotation/send` | Mọi tài khoản; `warehouseId` tuỳ chọn; thiếu bảng giá dùng 50.000/30.000đ/kg | Chỉ Admin/Sale; **bắt buộc `warehouseId`** (kho ORIGIN); thiếu bảng giá → 400; gửi được từ đơn mới |
| Báo giá có phụ phí / VAT / thuế nhập tay khác bảng giá | Gửi khách ngay | Bắt buộc `overrideReason` (hoặc `salesNote`); `status = PENDING_PRICE_APPROVAL`, khách chưa thấy tới khi Admin duyệt |
| `GET` báo giá, từ chối báo giá | Không kiểm chủ đơn | Khách chỉ thao tác đơn của mình (403); chỉ từ chối được báo giá đang chờ, bắt buộc lý do |
| `PUT /api/quotations/{id}/confirm-and-pay` | Xác nhận được cả báo giá tạm tính | Chỉ báo giá Sale đã gửi, còn hạn, không chờ duyệt giá. Dùng `canCustomerAccept` để bật/tắt nút |
| `PUT /api/admin/finance/transactions/{id}/approve` | Chỉ `transactionCode` | Thêm `receivedAmount` (thiếu so với số phải thu → 400); một mã giao dịch không dùng cho hai khoản |
| `POST /api/warehouse-receiving-notes` | Hệ thống tự lập khi nhận tiền; kho ORIGIN hoặc DESTINATION | **Sale lập**, chỉ đơn `DEPOSIT_PAID`, chỉ kho ORIGIN; phiếu `PENDING_APPROVAL`; response trả đầy đủ chi tiết phiếu |
| `PUT /api/warehouse-receiving-notes/{id}/status` | OM duyệt biên bản sau khi đếm | Phiếu `PENDING_APPROVAL`: duyệt cho nhận hàng. Phiếu `RECEIVED` / `PARTIALLY_RECEIVED` có chênh lệch: xem biên bản. Người lập / người kiểm đếm không tự duyệt. Kho đã gán quản lý thì chỉ quản lý đó duyệt, Admin duyệt thay phải có `reason`; kho chưa gán quản lý thì OM/Admin duyệt |
| `POST /api/parcels/check-in` | Nhận cả đơn chưa cọc; mã VCL-/PUR- | Bắt buộc có cọc đã xác nhận + phiếu đã duyệt; `shippingCode` nhận thêm **mã phiếu WRN-** |
| `POST .../confirm-receipt` | Gọi lại được trên phiếu đã chốt; lỗi tạo kiện bị nuốt | Chỉ khi phiếu `ACTIVE` / `PARTIALLY_RECEIVED`; dòng đã kiểm không kiểm lại (409); lỗi kiện → 400, không lưu gì |
| `PUT /api/parcels/{id}/put-away` | Cần phiếu APPROVED; đơn không phiếu thì bỏ qua | Xếp kệ khi kiểm đếm khớp phiếu đã duyệt (kể cả đang nhận một phần); đúng kho của phiếu; ô kệ đang hoạt động |
| `GET /api/orders/action-queue` (Sale) | Dòng "Chờ kho nhận hàng", không bấm được | Đơn `DEPOSIT_PAID` chưa có phiếu: `actionState = AWAITING_RECEIVING_NOTE`, `nextAction = CREATE_RECEIVING_NOTE`, `canAct = true` |
| Phiếu PDF ký gửi | Sinh khi Sale duyệt đơn | Sinh khi quản lý kho duyệt phiếu nhập kho; in thêm **mã phiếu nhập kho**; lấy đúng báo giá đã chấp nhận |
| Khách tự huỷ đơn | PENDING_REVIEW, APPROVED | Mọi trạng thái trước khi có tiền thực nhận; huỷ luôn link thanh toán đang treo |
| Webhook SePay thiếu khoá | Bỏ qua xác thực | 401 |

### API mới

| API | Ai | Nội dung |
|---|---|---|
| `PUT /api/quotations/{id}/price-approval` | Admin (khác người lập) | `{ "decision": "APPROVED" \| "REJECTED", "note": "..." }` — REJECTED bắt buộc `note` |
| `GET /api/warehouses/{id}/manager` | Nội bộ | Quản lý hiện tại + lịch sử |
| `PUT /api/warehouses/{id}/manager` | Admin | `{ "managerId": "<userId> \| null", "reason": "..." }` |
| `GET /api/orders/consignments/{id}/timeline` | Nội bộ, khách chủ đơn | `[{ event, fromStatus, toStatus, note, actorName, at }]` |

### Field mới (chỉ thêm)

- Báo giá: `priceApprovalStatus`, `overrideReason`, `acceptedAt`, `canCustomerAccept`.
- Chi tiết phiếu nhập kho: `createdByName`, `receiveApprovedByName`, `receiveApprovedAt`, `approvalNote`, `receivedByName`, `receivedAt`, `requiresReview`, `receiptPdfUrl`.
- Danh sách phiếu: `approvalStage` = `RECEIVE` (chờ duyệt nhận hàng) · `DISCREPANCY` (chờ xem chênh lệch).
- Khoản thu: trạng thái `RECEIVED_UNALLOCATED` — tiền về khi đơn đã huỷ/đóng hoặc khoản thu đã huỷ; không tính vào đơn, Admin nhận thông báo.

## 3. Lỗi của bản cũ đã sửa

| Lỗi | Sửa |
|---|---|
| Tồn mỗi kiện = tổng lượng cả đơn (đơn nhiều kiện bị nhân tồn) | Tồn = lượng dòng hàng của kiện |
| Trả phí lưu kho qua payOS làm đơn + hoá đơn thành `PAID` | Mọi đường xác nhận tiền đi qua `PaymentConfirmationService` |
| Tiền về sau khi huỷ làm đơn "sống lại" | Khoản thu `RECEIVED_UNALLOCATED`, đơn giữ nguyên |
| Webhook SePay không xác thực khi thiếu khoá | Từ chối |
| Báo giá tính cước hai lần cho kiện `MERGED` | Loại kiện SPLIT/MERGED |
| Xác nhận báo giá riêng (không thanh toán) làm đơn kẹt | Bỏ endpoint |
| Kiện đợt 2 bị chặn sau khi đơn sang `WAREHOUSE_RECEIVED` | Điều kiện tiền đọc từ khoản cọc thật |
| Nhiều lần SaveChanges, lỗi giữa chừng để lại dữ liệu dở | `DbTransactionFilter`: mỗi request ghi là một transaction, lỗi thì rollback |
| Không chống ghi đè đồng thời | `row_version` trên ORDERS, PAYMENTS, QUOTATIONS, WAREHOUSE_RECEIVING_NOTES, INVENTORIES → 409; unique `PAYMENTS.order_code` |

## 4. Code

| Thành phần | Vai trò |
|---|---|
| `VCL_API/Infrastructure/DbTransactionFilter.cs` | Transaction cho mọi request ghi |
| `VCL_BLL/Services/PaymentConfirmationService.cs` | Một chỗ ghi nhận tiền thực nhận (payOS, SePay, Admin) |
| `VCL_BLL/Helpers/QuotationAcceptanceRules.cs` | Trạng thái báo giá, điều kiện khách xác nhận |
| `VCL_BLL/Helpers/WarehouseApprovalPolicy.cs` | Ai được duyệt chứng từ của kho |
| `VCL_BLL/Helpers/DepositQuery.cs` | "Đơn đã có cọc" đọc từ khoản thu thật |
| `VCL_BLL/Helpers/ParcelQuantityResolver.cs` | Lượng hàng của một kiện |
| `VCL_BLL/Helpers/OrderHistoryWriter.cs` | Ghi lịch sử đơn |
| `VCL_BLL/Services/WarehouseManagerService.cs` | Gán quản lý kho |
| `VCL_DLL/Models/Controls/ControlColumns.cs`, `VCL_DLL/DBContext/VCLDBContext.Controls.cs` | Cột/bảng mới, tách khỏi file sinh tự động |

Đã xoá: tự lập phiếu khi nhận tiền (`EnsureReceivingNoteForPaidOrderAsync`), Sale xác nhận thanh toán (`ConfirmOrderPaymentAsync`), xác nhận báo giá riêng (`AcceptQuotationAsync`), giá cứng mặc định, bỏ qua xác thực SePay, sinh PDF khi Sale duyệt đơn, cấu hình trạng thái đơn do Sale tạo (`ConsignmentSettings:InitialStatusByStaff`).

## 5. Triển khai

1. Backup `AppDb` (`COPY_ONLY`).
2. Chạy `VCL_DLL/Migrations/Sql/20260917_ConsignmentControlsWave1.sql` bằng `sqlcmd -I` (bắt buộc `QUOTED_IDENTIFIER` cho filtered index). Script idempotent, chỉ thêm cột/bảng/index và đánh dấu dữ liệu cũ: phiếu cũ coi như đã duyệt nhận hàng, khoản đã thu ghi `LEGACY`, báo giá cũ không cần duyệt giá.
3. Cấu hình `SePay__ApiKey` hoặc `SePay__WebhookSecret` trước khi deploy — thiếu thì webhook SePay bị từ chối.
4. Gán quản lý cho từng kho (`PUT /api/warehouses/{id}/manager`). Kho chưa gán thì OM/Admin duyệt.
5. Đơn cũ đang `APPROVED` / `PAYMENT_CONFIRMED` (nếu có) xử lý tay: đơn `APPROVED` Sale báo giá bình thường; đơn `PAYMENT_CONFIRMED` cần Admin kiểm khoản cọc rồi đưa về `DEPOSIT_PAID`.
6. Container API phải nối SQL Server qua mạng Docker `sqlserver-docker_default` với `Server=sqlserver,1433` — firewall server chặn container mới gọi vòng ra `72.61.114.184:14333`.
7. Deploy image mới.

**Đã triển khai production 17/09/2026** (`vcl-api`, `AppDb`). Backup: image `vcl-api:backup-20260917-predeploy-kygui`, `/root/backups/vcl-BE-20260917-predeploy-kygui.tar.gz`, `/root/backups/docker-compose.vcl-api.20260917-predeploy-kygui.yml`, `AppDb-20260917-predeploy-kygui.bak`.

## 6. Kiểm thử

`tests/wave1_consignment_flow.py` — 62 bước qua HTTP thật trên môi trường thử, gồm cả các thao tác sai phải bị chặn. Token ký bằng khoá JWT của môi trường thử, không dùng mật khẩu.

Kết quả 17/09/2026: **62 passed, 0 failed**.

## 7. Chưa làm

- Sổ nghĩa vụ theo dòng hàng (`CHARGES` / `CHARGE_ALLOCATIONS`).
- Xuất kho quốc tế, vận chuyển, tiếp nhận VN, báo giá theo số đo thực tế, lưu kho VN, giao hàng, giao lại, sự cố, huỷ/hoàn tiền.
- `Idempotency-Key`, audit log, outbox thông báo sau commit.
- Mua hộ vẫn theo luồng cũ (có hưởng transaction và luật xác nhận tiền chung).
