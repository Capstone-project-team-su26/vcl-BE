# API luồng mua hộ

Cập nhật 19/09/2026. Thiết kế nghiệp vụ: `docs/migrate-source-cu/08-luong-mua-ho.md` mục 6 (bản chốt).
Quy ước chung (đăng nhập, kiểu response, mã lỗi): xem [README.md](README.md).

```
① Khách tạo yêu cầu (Sale tạo thay được)                    PENDING_REVIEW
② Sale xem xét: bổ sung / từ chối                             NEED_MORE_INFO / REJECTED
③ Sale báo giá: phần TRẢ TRƯỚC = tiền hàng + phí mua + ship nội địa (+ VAT phần phí)
               phần TẠM TÍNH  = cước quốc tế + VAT cước + thuế NK (thu ở VN theo cân đo thật)   QUOTED
④ Khách chấp nhận + trả 100% phần trả trước                  WAITING_PAYMENT → PAID
⑤ Sale lập đơn mua NCC (NCC trong danh mục, kho nhận, SL, giá thực)                (đơn mua) DRAFT
   giá thực vượt giá báo > 5% → khách xem + trả phần chênh     AWAITING_CUSTOMER → AWAITING_CUSTOMER_PAYMENT
⑥ Admin duyệt đơn mua                                         PENDING_APPROVAL → APPROVED / REJECTED
⑦ Sale đặt NCC: mã đơn NCC + ảnh bằng chứng                    ORDERED     (yêu cầu → PURCHASING)
   → hệ thống sinh ĐƠN KHO PURCHASE mã PUR-xxx-n + PHIẾU TIẾP NHẬN đã mở (ACTIVE)
⑨ Sale cập nhật tiến độ NCC (tự báo khách)                     SUPPLIER_CONFIRMED → SUPPLIER_SHIPPED
⑩ Kho nhận hàng theo phiếu — y hệt ký gửi (api-ky-gui-1.md mục kho nhận hàng)
⑪ Từ đây như ký gửi: xếp kệ → xuất kho → về VN → tất toán theo cân đo VN → giao → đóng đơn
   (api-kho-xep-ke-theo-khu.md, api-xuat-kho.md, api-hang-ve-viet-nam.md — dùng orderId của đơn kho)
```

**Đã bỏ** (trả 404): `PUT /api/purchase-requests/{id}/confirm-purchase` (popup bấm tay tiến độ), `POST .../approve-store` (duyệt nhập kho trước khi kho nhận), `POST .../final-payment` (thu đợt cuối ở kho nguồn). Endpoint cũ `POST .../purchase-order` và `PUT /api/purchase-orders/{id}/status` được thay bằng mục D.

## A. Yêu cầu mua hộ

| Method | Path | Ai | Ghi chú |
|---|---|---|---|
| POST | `/api/purchase-requests` | Customer | Tạo yêu cầu |
| POST | `/api/staff/purchase-requests` | Sale | Tạo thay khách: body như trên + `customerId` hoặc `customerPhone` / `customerEmail` |
| GET | `/api/purchase-requests?pageNumber&pageSize&status&searchKeyword` | Customer (của mình) / nhân viên | |
| GET | `/api/purchase-requests/{id}` | Chủ yêu cầu / nhân viên | `items[].itemId` là id sản phẩm dùng ở báo giá và đơn mua |
| PUT | `/api/purchase-requests/{id}/status` | Sale | `{ status: IN_REVIEW \| NEED_MORE_INFO \| REJECTED \| PENDING_REVIEW, reason }` — chỉ khi yêu cầu còn ở giai đoạn xem xét; `NEED_MORE_INFO`/`REJECTED` bắt buộc `reason` |
| GET | `/api/purchase-requests/{id}/history` | Chủ yêu cầu / nhân viên | Nhật ký đổi trạng thái |

Tạo yêu cầu:

```json
{
  "route": "KR-VN", "shippingOption": "Express",
  "receiverName": "Nguyễn A", "receiverPhone": "0900000000", "receiverAddress": "HCM",
  "items": [
    { "productLink": "https://...", "productType": "<id loại hàng>", "productName": "Tai nghe", "quantity": 2,
      "attributes": "Màu đen", "note": "", "imageUrls": ["https://..."] }
  ]
}
```

Trạng thái yêu cầu: `PENDING_REVIEW` · `IN_REVIEW` · `NEED_MORE_INFO` · `REJECTED` · `QUOTED` · `QUOTATION_REJECTED` · `WAITING_PAYMENT` · `PAID` (đã trả trước, chờ đặt mua) · `PURCHASING` (đã đặt ít nhất một đơn NCC) · `COMPLETED` (mọi sản phẩm đã đặt NCC và mọi đơn kho đã đóng) · `CANCELLED`. Dữ liệu cũ còn có `DEPOSIT_PAID`, `PURCHASED`, `SELLER_SHIPPED`, `ARRIVED_ORIGIN_WAREHOUSE`, `WAITING_STORED`, `STORED` — chỉ hiển thị.

## B. Báo giá (Sale)

**Gợi ý phí** `POST /api/purchase-requests/{id}/calculate-fees`

```json
{ "items": [ { "purchaseRequestItemId": "<itemId>", "unitPrice": 500000, "quantity": 2 } ] }
```
→ `data`: `productsSubtotal`, `calculatedPurchaseFee`, `appliedPurchaseFeePercent`, `domesticShippingFee`, `freightRatePerKg` (bảng giá khớp tuyến + loại dịch vụ), `estimatedWeight` (0,2 kg/món, tối thiểu 1 kg), `servicePricingId`, `estimatedShippingFee`, `vatRate`, `estimatedPrepayAmount`, `totalEstimatedCost`, `note`. Chỉ để điền sẵn form, không lưu gì.

**Lập báo giá** `POST /api/purchase-requests/{id}/quotation` (yêu cầu ở `PENDING_REVIEW` / `IN_REVIEW` / `NEED_MORE_INFO`)

```json
{
  "currency": "KRW",
  "items": [ { "purchaseRequestItemId": "<itemId>", "unitPrice": 45000 } ],
  "purchaseFee": 80000,
  "domesticShippingFee": 30000,
  "freightRatePerKg": 120000,
  "estimatedWeight": 2,
  "servicePricingId": null,
  "additionalFees": [ { "pricingRuleId": null, "feeName": "Kiểm hàng", "feeType": "SURCHARGE", "amount": 20000, "note": "" } ],
  "note": ""
}
```

- `items[].unitPrice` theo `currency` (bỏ trống = VND); hệ thống quy đổi và **khoá tỷ giá** vào báo giá. Phí luôn nhập VND.
- `freightRatePerKg` bỏ trống thì lấy theo bảng giá khớp tuyến / `servicePricingId`; không có bảng giá thì 400 yêu cầu nhập tay. `estimatedWeight` bỏ trống = 0,2 kg/món (tối thiểu 1 kg). `shippingFee` cũ bị bỏ qua.
- Đơn giá /kg này được mang sang đơn kho: tất toán ở VN tính cước = cân đo thật × đúng đơn giá này.

Response (`GET /api/purchase-requests/{id}/quotation` trả cùng dạng, bản mới nhất):

| Trường | Nghĩa |
|---|---|
| `productsSubtotal` | Tiền hàng (VND) |
| `purchaseFee`, `domesticShippingFee` | Phí mua hộ, ship nội địa NCC → kho |
| `shippingFee` | Cước quốc tế **tạm tính** = `estimatedWeight × freightRatePerKg` |
| `vatRate`, `vat` | % VAT và tổng VAT (phần phí + phần cước) |
| `importTax` | Thuế NK **tạm tính** theo loại hàng |
| `prepayAmount` | **Khách trả khi chấp nhận** = hàng + phí mua + ship nội địa + phụ phí + VAT phần phí |
| `estimatedLaterAmount` | Tạm tính thu ở VN = cước + VAT cước + thuế NK |
| `totalAmount` | `prepayAmount + estimatedLaterAmount` |
| `isLegacy` | Báo giá đời cũ (thu cọc theo tỷ lệ) — không chấp nhận được nữa, Sale lập lại |
| `items[]`, `additionalFees[]`, `currency`, `exchangeRate`, `status` | |

## C. Khách chấp nhận và trả trước

| Method | Path | Ai | Body |
|---|---|---|---|
| PUT | `/api/purchase-requests/{id}/quotation/confirm-and-pay` | Customer | `{ paymentMethod: "PAYOS" \| "SEPAY", returnUrl }` |
| PUT | `/api/purchase-requests/{id}/quotation/reject` | Customer / Sale | `{ rejectionReason }` |
| GET | `/api/purchase-requests/{id}/payments` | Chủ yêu cầu / nhân viên | |

`confirm-and-pay` → `data`: `paymentId`, `orderCode`, `amount` (= `depositAmount` = `prepayAmount`), `checkoutUrl`, `depositDescription`, `remainingAmount` (= phần tạm tính thu ở VN). Khoản thu có `paymentType = PREPAYMENT`. Trả xong (webhook / Admin đối soát tay) → yêu cầu `PAID`.

`payments` → `data`: `totalBillAmount` (phần trả trước + phần chênh đã phát sinh), `totalPaid`, `outstanding`, `depositAmount`, `estimatedLaterAmount`, `isLegacy`, `payments[] { paymentId, orderCode, paymentType (PREPAYMENT | PRICE_DIFFERENCE | đời cũ DEPOSIT/FULL_PAYMENT/FINAL_PAYMENT), purchaseOrderId, amount, status, checkoutUrl, paidAt }`.

## D. Đơn mua NCC

| Method | Path | Ai | Khi nào |
|---|---|---|---|
| POST | `/api/purchase-requests/{id}/purchase-orders` | Sale | Yêu cầu đã trả trước → tạo `DRAFT` |
| GET | `/api/purchase-requests/{id}/purchase-orders` | Chủ yêu cầu / nhân viên | Khách không thấy đơn nháp |
| GET | `/api/purchase-orders?status=A,B&search=&pageNumber&pageSize` | Nhân viên | Phân trang; `search` theo mã đơn mua / mã đơn NCC / tên NCC |
| GET | `/api/purchase-orders/{id}` | Chủ yêu cầu / nhân viên | |
| PUT | `/api/purchase-orders/{id}` | Sale | Sửa khi `DRAFT` / `REJECTED` (về lại `DRAFT`) |
| POST | `/api/purchase-orders/{id}/submit` | Sale | `DRAFT`/`REJECTED` → `PENDING_APPROVAL`, hoặc `AWAITING_CUSTOMER` nếu vượt ngưỡng |
| POST | `/api/purchase-orders/{id}/customer-decision` | Customer (chủ) | `AWAITING_CUSTOMER` |
| POST | `/api/purchase-orders/{id}/decide` | **Admin** | `PENDING_APPROVAL` |
| POST | `/api/purchase-orders/{id}/place` | Sale | `APPROVED` → `ORDERED` |
| PUT | `/api/purchase-orders/{id}/progress` | Sale | `ORDERED` → `SUPPLIER_CONFIRMED` → `SUPPLIER_SHIPPED` |
| POST | `/api/purchase-orders/{id}/cancel` | Sale (chưa đặt NCC) / Admin (đã đặt, kho chưa nhận kiện) | |

Tạo / sửa:

```json
{
  "supplierId": "<id NCC trong GET /api/suppliers?isActive=true>",
  "warehouseId": "<kho nguồn ORIGIN; bỏ trống = kho dự kiến của yêu cầu>",
  "currency": "CNY",
  "purchaseNote": "",
  "items": [ { "purchaseRequestItemId": "<itemId>", "quantity": 2, "unitPrice": 250 } ]
}
```

Luật: NCC phải có trong danh mục và đang hoạt động; kho phải là kho nguồn đang hoạt động; SL từ 1 tới SL khách đặt; mỗi sản phẩm chỉ nằm trong **một** đơn mua còn hiệu lực (một yêu cầu có thể tách nhiều đơn mua / nhiều NCC); sản phẩm phải có trong báo giá đã chấp nhận.

**Ngưỡng lệch giá**: `submit` so tiền hàng thực (`totalAmount`) với tiền hàng theo giá đã báo cho đúng các dòng đó (`quotedGoodsAmount`). Vượt quá `priceToleranceRate`% (mặc định 5, Admin đổi qua phụ phí mã `PURCHASE_PRICE_TOLERANCE_RATE`) → `priceDifferenceAmount = totalAmount − quotedGoodsAmount`, đơn sang `AWAITING_CUSTOMER` và khách được báo. Trong ngưỡng: công ty chịu, đi thẳng `PENDING_APPROVAL`.

`customer-decision`:
```json
{ "accept": true, "paymentMethod": "SEPAY", "returnUrl": "" }      // → AWAITING_CUSTOMER_PAYMENT, sinh khoản PRICE_DIFFERENCE
{ "accept": false, "reason": "Giá cao quá" }                         // → DRAFT, Sale sửa lại hoặc huỷ
```
Gọi lại `accept: true` khi đang `AWAITING_CUSTOMER_PAYMENT` để lấy link mới (link cũ bị huỷ). Khách trả xong → đơn **tự** sang `PENDING_APPROVAL`.

`decide`: `{ "approve": true, "note": "" }` hoặc `{ "approve": false, "note": "lý do (bắt buộc)" }`. Duyệt bị chặn nếu khách chưa trả phần trả trước hoặc chưa trả phần chênh.

`place`: trước tiên tải ảnh bằng chứng `POST /api/attachments` (multipart) với `entityType=PURCHASE_ORDER`, `entityId=<purchaseOrderId>`, `documentType=PURCHASE_PROOF`; rồi gọi `{ "supplierOrderCode": "TB123456", "note": "" }`. Hệ thống sinh:
- Đơn kho `OrderType = PURCHASE`, mã `{mã yêu cầu}-{sequenceNo}`, trạng thái `DEPOSIT_PAID`, các dòng hàng theo đơn mua (giá trị khai báo = giá mua thật).
- Phiếu tiếp nhận `WRN-xxxxxxxx` đã mở cho kho nhận (`ACTIVE`, không qua bước OM duyệt phiếu).
- Hoá đơn chặng quốc tế của đơn kho: cước tạm tính (cân ước tính chia theo SL × đơn giá /kg đã báo), VAT cước, thuế NK tính trên giá mua thật. Đây là số tất toán ở VN sẽ điều chỉnh theo cân đo thật.

`progress`: `{ "status": "SUPPLIER_CONFIRMED" }` hoặc `{ "status": "SUPPLIER_SHIPPED", "domesticTrackingCode": "CJ123" (bắt buộc), "domesticCarrier": "CJ", "note": "" }`. Mã vận đơn nội địa được ghi vào từng dòng hàng của đơn kho để kho đối chiếu khi nhận.

`cancel`: `{ "reason": "..." }` (bắt buộc). Đơn đã đặt NCC: chỉ Admin, và chỉ khi kho chưa nhận kiện nào — đơn kho và phiếu tiếp nhận chuyển `CANCELLED`. Tiền đã thu (phần chênh đã trả) hoàn ngoài hệ thống.

Response một đơn mua (`data`):

| Trường | Ghi chú |
|---|---|
| `purchaseOrderId`, `purchaseOrderCode` (`PO-{mã yêu cầu}-{n}`), `sequenceNo`, `purchaseRequestId`, `purchaseCode`, `customerName` | |
| `supplierId`, `supplierName`, `currency`, `exchangeRate`, `purchaseNote`, `supplierOrderCode`, `decisionNote`, `receivingNoteCode`, `receivingNoteStatus`, `proofCount` | **null / 0 với khách** |
| `warehouseId`, `warehouseName` | Kho nhận |
| `totalAmount`, `quotedGoodsAmount`, `priceDifferenceAmount`, `priceToleranceRate` | VND |
| `status`, `statusText` | Nhãn tiếng Việt |
| `customerRejectReason`, `customerDecisionAt`, `submittedAt`, `decidedAt`, `orderedAt`, `supplierConfirmedAt`, `supplierShippedAt`, `domesticCarrier`, `domesticTrackingCode`, `cancelledAt`, `cancelReason` | |
| `priceDifferencePaymentId`, `priceDifferenceCheckoutUrl`, `priceDifferencePaymentStatus` | Khoản chênh gần nhất |
| `warehouseOrderId`, `warehouseOrderCode`, `warehouseOrderStatus` | Đơn kho — từ đây theo dõi bằng API ký gửi |
| `items[] { orderItemId, purchaseRequestItemId, productName, requestedQuantity, quantity, unitPrice (VND), unitPriceOriginal (null với khách), quotedUnitPrice, lineTotal }` | |

## E. Sau khi đặt NCC — dùng chung API ký gửi

Dùng `warehouseOrderId` như `orderId` của đơn ký gửi:
- Kho: quét `WRN-...` (`GET /api/warehouse-receiving-notes/scan?code=`), check-in `POST /api/parcels/check-in` với `packages[]` theo `orderItemId`, lệch SL thì quản lý kho duyệt biên bản — y hệt ký gửi. Quét mã `PUR-xxx` chỉ chạy khi yêu cầu có đúng một đơn kho; nhiều đơn thì quét `WRN-` hoặc mã `PUR-xxx-n`.
- Khách theo dõi: `GET /api/orders/consignments/tracking` và `/{orderId}/tracking` (đơn mua hộ nằm chung danh sách).
- Xuất kho, về VN, tất toán (`GET /api/orders/{orderId}/settlement-preview`, `POST /api/orders/{orderId}/payments/final`), giao, đóng đơn: như ký gửi. Tất toán không đòi cọc (đơn kho có khoản cọc 0đ đánh dấu đã trả trước ở luồng mua).
- Yêu cầu mua hộ tự sang `COMPLETED` khi mọi sản phẩm đã đặt NCC và mọi đơn kho đã `COMPLETED`.

## F. Dữ liệu cũ

- Yêu cầu đã trả **cọc theo tỷ lệ** (đời cũ, trạng thái `PAID`/`DEPOSIT_PAID`/...) mà chưa có đơn kho: Sale lập đơn mua như mục D. Phần tiền cọc đã thu dư / thiếu so với "tiền hàng + phí mua" được cộng/trừ vào hoá đơn chặng VN của đơn kho đầu tiên (dòng `PREPAID_ADJUSTMENT`).
- Yêu cầu đã có đơn kho theo quy trình cũ (`STORED` / `COMPLETED`): giữ nguyên, không lập đơn mua mới.

## G. Lỗi thường gặp

| Tình huống | Mã | message (rút gọn) |
|---|---|---|
| Lập đơn mua khi khách chưa trả trước | 400 | "Chỉ lập đơn mua khi khách đã thanh toán phần trả trước..." |
| Không chọn NCC / NCC ngừng hoạt động | 400 | "Phải chọn nhà cung cấp có trong danh mục." |
| SL vượt SL khách đặt | 400 | "Số lượng mua '...' phải từ 1 đến N" |
| Sản phẩm đã nằm ở đơn mua khác | 400 | "... đã nằm trong một đơn mua khác còn hiệu lực." |
| Sale gọi `decide` | 403 | "Chỉ Admin được duyệt đơn mua NCC." |
| Duyệt khi khách chưa trả phần chênh | 400 | "Khách chưa trả phần chênh giá..." |
| `place` chưa có ảnh bằng chứng | 400 | "Phải tải ảnh bằng chứng đặt hàng (...PURCHASE_PROOF)..." |
| `SUPPLIER_SHIPPED` thiếu mã vận đơn | 400 | "Phải nhập mã vận đơn nội địa khi NCC phát hàng." |
| Huỷ đơn đã đặt NCC khi kho đã nhận kiện | 400 | "Kho đã nhận kiện của đơn ... — không huỷ được" |
| Chấp nhận báo giá đời cũ | 400 | "Báo giá này lập theo cách tính cũ..." |
