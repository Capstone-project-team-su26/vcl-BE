# Hướng dẫn Công thức Tính giá & Cấu hình Bảng giá VCL

Tài liệu này tổng hợp chi tiết công thức tính toán báo giá chặng quốc tế và chặng nội địa cho hệ thống **Vietnam Cross-Border Logistics (VCL)**, phục vụ hoạt động pair-programming và tích hợp giữa các thành viên trong đội ngũ phát triển (Frontend, Backend và Nghiệp vụ).

---

## 1. Phân loại loại hình đơn hàng

Hệ thống hỗ trợ 2 loại hình tính giá khác nhau dựa trên trường `OrderType` của Đơn hàng:

| Loại đơn hàng (`OrderType`) | Các thành phần chi phí áp dụng | Mô tả |
| :--- | :--- | :--- |
| **`CONSIGNMENT`** (Hàng ký gửi) | `Cước quốc tế + Phí vận chuyển nội địa` | Chỉ tính phí chặng quốc tế (theo Net Weight) và phí ship chặng cuối tại Việt Nam. Các phụ phí, phí dịch vụ cố định và thuế nhập khẩu bằng **`0`**. |
| **Các loại khác** (Mua hộ...) | `Cước quốc tế + Phí dịch vụ cố định + Tổng phụ phí + Thuế & Thuế nhập khẩu` | Tính toán đầy đủ phí dịch vụ, phụ phí tùy chọn (kiểm đếm, đóng gỗ, bảo hiểm...) và thuế nhập khẩu dựa trên giá trị khai báo. |

---

## 2. Công thức tính Cân nặng quy đổi thể tích (DIM / Volumetric Weight)

### 2.1. Khi tính từ Danh sách hàng hóa (`OrderItems`)

Trọng lượng thể tích được nhân với **số lượng (`Quantity`)** của từng dòng sản phẩm để ra cân tổng thể tích chính xác:

$$\text{Cân quy đổi thể tích (kg)} = \sum_{item} \left( \frac{\text{Dài (cm)} \times \text{Rộng (cm)} \times \text{Cao (cm)}}{5000} \times \text{Quantity} \right)$$

### 2.2. Khi báo giá thủ công bằng Thể tích tổng (`VolumeM3` tính bằng $m^3$)

Hệ thống tự động quy đổi đơn vị mét khối ($m^3$) sang centimet khối ($cm^3$) trước khi chia cho hệ số 5000:

$$\text{Cân quy đổi thể tích (kg)} = \frac{\text{VolumeM3} \times 1.000.000}{5000} = \text{VolumeM3} \times 200$$

---

## 3. Công thức tính Cân tính cước (Net Weight / Chargeable Weight)

Cân nặng cuối cùng dùng để tính phí vận chuyển quốc tế là giá trị lớn nhất trong 3 yếu tố:

$$\text{Net Weight} = \max(\text{Cân thực tế tổng},\ \text{Cân quy đổi thể tích tổng},\ \text{Cân sàn tối thiểu})$$

Trong đó:
* **Cân thực tế tổng:**

$$\text{Cân thực tế tổng} = \sum (\text{Cân nặng thực tế từng sản phẩm} \times \text{Quantity})$$

* **Cân sàn tối thiểu (`minimumWeight`):** Lấy từ quy tắc `PricingRule` có mã/loại `"MIN_WEIGHT"`. Nếu Admin không thiết lập cấu hình riêng chặng, mặc định là **`1.0 kg`**.

---

## 4. Công thức tính Phí vận chuyển chặng nội địa (Domestic Shipping Fee)

Được cấu hình động trong `PricingRule` cho hàng ký gửi (`CONSIGNMENT`):

**Tạm tính (Estimate):** Lấy đơn giá cố định (`Value`) từ bảng `PricingRule` có mã/loại `"DOMESTIC_FEE"` (nếu không có mặc định là `0`).

**Chính thức (Official):** Quét thực tế chặng cuối khi hàng về kho:

$$\text{Phí chặng nội địa thực tế} = \sum \left( \text{Phí chuyển phát } \textit{DeliveryFee} \text{ từ các } \textit{DomesticWaybill} \text{ liên kết với các kiện } \textit{Parcel} \right)$$

---

## 5. Tổng hợp công thức tính Tổng tiền báo giá

### A. Đối với đơn hàng Ký gửi (`CONSIGNMENT`)

Chỉ tính cước quốc tế chặng biên giới và phí ship chặng cuối. Toàn bộ các phí phụ thu khác và thuế nhập khẩu bằng `0`:

$$\text{Tổng tiền ký gửi} = \left( \text{Net Weight} \times \text{Đơn cước quốc tế từ ServicePricing} \right) + \text{Phí vận chuyển nội địa}$$

### B. Đối với đơn hàng Mua hộ / Khác

$$\text{Tổng tiền} = \left( \text{Net Weight} \times \text{Đơn cước quốc tế} \right) + \text{Phí dịch vụ cố định} + \sum(\text{Phụ phí bổ sung}) + \text{Thuế nhập khẩu}$$

Trong đó:
* **Phí dịch vụ cố định** = `50,000 VND` nếu chặng đi nhanh `Express`, hoặc `30,000 VND` nếu chặng thường `Standard`
* **Thuế nhập khẩu** = Tổng giá trị khai báo × 10% (mặc định)

---

## 6. Cách cấu hình trên Database (cho Quản trị viên/Admin)

Để điều chỉnh các mức cước và quy tắc tính phí động mà không cần can thiệp mã nguồn, Admin sử dụng 2 thực thể sau:

### 6.1. Bảng `ServicePricing` (Đơn giá vận chuyển quốc tế)
Admin thêm/sửa đơn giá chặng cho từng loại hình vận chuyển qua API `/api/service-pricings`:
* **`serviceType`**: `"Express"` hoặc `"Standard"`.
* **`originCountry`** & **`destinationCountry`**: Ví dụ `"INDO"` và `"VN"`.
* **`price`**: Đơn giá (VND/kg).
* **`effectiveDate`**: Ngày bắt đầu có hiệu lực của mức giá này.

### 6.2. Bảng `PricingRule` (Cấu hình cân tối thiểu, phí nội địa và phụ phí)
Admin cấu hình qua API `/api/pricing-rules`:
* **Cân sàn tối thiểu**:
  * `RuleCode`: `"MIN_WEIGHT"`
  * `Value`: Số cân sàn (ví dụ: `1.5` -> tối thiểu tính phí 1.5kg).
* **Ước tính phí nội địa**:
  * `RuleCode`: `"DOMESTIC_FEE"`
  * `Value`: Số tiền chặng cuối tạm tính (ví dụ: `35000` -> tạm tính phí ship nội địa 35,000 VND).
* **Phụ phí đóng gỗ**:
  * `RuleCode`: `"WOODEN_BOX"`
  * `CalculationType`: `"FIXED"` hoặc `"PER_KG"` hoặc `"PERCENTAGE"`
  * `Value`: Đơn giá phụ thu.
* **Liên kết**: Trường `servicePricingId` (Khóa ngoại đến bảng `ServicePricing`) dùng để áp dụng rule này riêng cho chặng bay/chặng xe cụ thể đó. Nếu để trống (`null`), rule sẽ được áp dụng chung trên toàn hệ thống.
