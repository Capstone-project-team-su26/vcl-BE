# TICKET — Đồng bộ BE theo code mới của Phong (07/08/2026)

**Nguồn:** `vcl-customer-web` commits `1908b8ee..a41ecbc0` (06–07/08, tác giả Phong).

> Khi mở lại diff: bỏ qua `logictic-customer/.agents/**` — Phong lỡ commit ~13.400 file skill rác.
> Code thật chỉ 18 file trong `logictic-customer/src`.

## TRẠNG THÁI — đã code 07/08, `dotnet build` 0 error

| # | Việc | Kết quả |
|---|---|---|
| 1 | `quotationCreatedAt` cho detail | ✅ `ConsignmentDtos.cs`, `OrderService.Queries.cs` |
| 2 | Lọc pricing rule theo `?orderType=` | ✅ `PricingRuleController.cs`, `PricingRuleService.cs` — **cần Phong bỏ GUID hardcode** |
| 3 | Validate giới hạn kiện/đơn | ✅ `OrderService.Create.cs` |
| 4 | Đóng thùng gỗ bắt buộc | ✅ `OrderService.Create.cs` (chỉ luồng khách tự tạo) |
| 5 | Bỏ thanh toán OFFLINE | ⛔ cố ý chưa sửa — xem cuối file |

### 1. `quotationCreatedAt` ở detail
List đã trả sẵn từ trước; detail (`ConsignmentDetailResponse`) thiếu → đã thêm,
lấy từ `latestQuotation?.CreatedAt`, khớp cách list làm.

### 2. Lọc pricing rule theo loại đơn
`GET /api/pricing-rules?orderType=CONSIGNMENT` → loại mọi rule có code/type chứa
`PURCHASE`, `VAT`, `IMPORT_TAX`. `?orderType=PURCHASE` → chỉ lấy đúng nhóm đó.
Bỏ trống = trả toàn bộ (màn quản trị bảng giá không đổi hành vi).

Nhận diện theo **mã/loại rule, không theo Id** — vì Id rule lệch giữa DB chung và DB dựng mới
(đúng vết xe đổ `CARTON_REPACK`: `4829CEDB…` vs `7f8dcbac…`).

→ **Việc của Phong:** trong `PackageOptionalServices.jsx` + `PackageOptionalServicesS1.jsx`,
gọi API kèm `?orderType=CONSIGNMENT` rồi **xoá `HIDDEN_RULE_IDS`** (đang hardcode
`35905dcf-4e55-4cc7-ab9a-2453e012fe74` và `0385131b-214c-49b8-9de2-116d62f27111`).

### 3. Giới hạn kiện/đơn
Mặc định khớp đúng `validatePackage` / `validateConsignmentForm` của FE:

| Cấp | Giới hạn |
|---|---|
| Kiện | ≤ 3 kg · dài ≤ 100 cm · rộng ≤ 200 cm · cao ≤ 50 cm |
| Kiện | số lượng ≤ 5 · giá trị khai báo ≤ 6.000.000 đ |
| Đơn | tổng ≤ 5 kg · tổng giá trị ≤ 10.000.000 đ |

Không hardcode chết: override được qua `appsettings.json`, đổi hạn mức lúc demo
không phải deploy lại.

```json
"ConsignmentSettings": {
  "Limits": {
    "MaxItemWeightKg": 3,
    "MaxItemLengthCm": 100,
    "MaxItemWidthCm": 200,
    "MaxItemHeightCm": 50,
    "MaxItemQuantity": 5,
    "MaxItemDeclaredValue": 6000000,
    "MaxOrderWeightKg": 5,
    "MaxOrderDeclaredValue": 10000000
  }
}
```

Áp cho **cả 2 luồng** tạo đơn (khách tự tạo + Sale tạo hộ). Cân nặng/kích thước chỉ kiểm
khi client có gửi, vì luồng Sale và mobile không bắt buộc nhập đủ các trường này.

### 4. Đóng thùng gỗ bắt buộc
`CreateConsignmentAsync` (khách tự tạo) giờ đòi `requiresWoodenCrate = true` **và**
mọi item phải có `packageConfigurationId`. Đã đối chiếu payload FE
(`ConsignmentOrder.jsx:2471`) — Phong có gửi đủ, không vỡ.

`CreateConsignmentByStaffAsync` **không** áp ràng buộc này, giữ nguyên
`SuggestPackageConfigurationIdAsync` làm lưới an toàn cho Sale.

## ❓ VẪN CẦN PHONG XÁC NHẬN

Code đã chạy đúng theo số của FE rồi, nhưng mấy con số nhìn không nhất quán.
Nếu Phong trả lời khác thì **chỉ sửa `appsettings.json`**, không đụng code:

1. **rộng ≤ 200 cm > dài ≤ 100 cm** — thường dài là cạnh lớn nhất. Có nhầm không?
2. **tổng đơn ≤ 5 kg mà 1 kiện đã ≤ 3 kg** → 1 đơn tối đa ~2 kiện. Đúng ý hay số tạm để test?

## ⛔ CỐ Ý CHƯA SỬA — thanh toán OFFLINE

FE khách hàng bỏ hẳn lựa chọn offline, chỉ còn SePay
(`QuotationPaymentConfirmDialog.jsx`). BE vẫn nhận `"OFFLINE"`
(`ConsignmentPaymentService.cs:34`).

**Chưa chặn vì:** web nội bộ `HeThongLogictic` (Sale/Ops) có thể vẫn dùng đường này để
đối soát chuyển khoản tay. Chặn ở BE là gãy app khác, không phải chỉ gãy web khách.
→ Cần @Đạt xác nhận trước khi bỏ.

---
Liên quan: `docs/API_INTEGRATION.md`, `docs/CONSIGNMENT_RECEIPT_CHECKIN_API.md`.
