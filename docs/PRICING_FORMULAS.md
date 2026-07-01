# Hướng dẫn Công thức Tính giá & Cấu hình Bảng giá VCL

Tài liệu này tổng hợp chi tiết công thức tính toán báo giá chặng quốc tế và chặng nội địa cho hệ thống **Vietnam Cross-Border Logistics (VCL)**, phục vụ hoạt động pair-programming và tích hợp giữa các thành viên trong đội ngũ phát triển (Frontend, Backend và Nghiệp vụ).

---

## 1. Phân loại loại hình đơn hàng

Hệ thống hỗ trợ 2 loại hình tính giá khác nhau dựa trên trường `OrderType` của Đơn hàng:

| Loại đơn hàng (`OrderType`) | Các thành phần chi phí áp dụng | Mô tả |
| :--- | :--- | :--- |
| **`CONSIGNMENT`** (Hàng ký gửi) | `Cước quốc tế + Phí vận chuyển nội địa` | Chỉ tính phí chặng quốc tế (theo Net Weight) và phí ship chặng cuối tại Việt Nam. Các phụ phí, phí dịch vụ cố định và thuế nhập khẩu bằng **`0`**. |
| **Các loại khác** (Mua hộ, Standard...) | `Cước quốc tế + Phí dịch vụ cố định + Tổng phụ phí + Thuế & Thuế nhập khẩu` | Tính toán đầy đủ phí dịch vụ, phụ phí tùy chọn (kiểm đếm, đóng gỗ, bảo hiểm...) và thuế nhập khẩu dựa trên giá trị khai báo. |

---

## 2. Công thức xác định Cân nặng tính cước (Chargeable Weight / Net Weight)

Cân nặng dùng để nhân với đơn giá chặng quốc tế (**Net Weight**) là giá trị lớn nhất trong 3 loại cân nặng dưới đây:

$$\text{Net Weight} = \max(\text{Cân thực tế}, \text{Cân quy đổi thể tích}, \text{Cân sàn tối thiểu})$$

### 2.1. Cân thực tế (`totalWeight` / `actualWeight`)
* Là tổng trọng lượng thực tế của hàng hóa được khai báo (khi báo giá tạm tính) hoặc được cân đo trực tiếp tại kho (khi hàng về kho chặng đầu).
* Đơn vị tính: **Kilogram (kg)**.

### 2.2. Cân quy đổi thể tích (`volumetricWeight` / DIM)
Quy đổi kích thước ba chiều của hàng hóa sang cân nặng theo tiêu chuẩn IATA Air Express chặng Châu Á:

$$\text{Cân thể tích (kg)} = \frac{\text{Dài (cm)} \times \text{Rộng (cm)} \times \text{Cao (cm)}}{5000}$$

> [!NOTE]
> **Quy đổi đơn vị thể tích trong Database:**
> * Trong Database và các API request truyền lên, thể tích tổng được lưu dưới dạng **mét khối ($m^3$)** để đồng bộ.
> * Khi tính toán, thể tích $m^3$ sẽ được quy đổi tương đương:

$$\text{Cân thể tích (kg)} = \text{Thể tích (m³)} \times 200$$

### 2.3. Cân sàn tối thiểu (`minimumWeight`)
* Là mức cân nặng tối thiểu bắt buộc phải tính phí cho mỗi tuyến đường chặng quốc tế (ví dụ: tối thiểu là 1.0kg kể cả khi hàng chỉ nặng 0.2kg).
* Giá trị này được Admin cấu hình động trong bảng `PricingRule` (Rule dạng `MIN_WEIGHT` hoặc `ConditionType == "MIN_WEIGHT"`).
* **Giá trị mặc định:** Nếu hệ thống chưa cấu hình, cân tối thiểu mặc định sẽ là **`1.0 kg`**.

---

## 3. Công thức tính chi phí chi tiết

### 3.1. Cước chặng quốc tế (International Freight Charge)
Đơn giá cước được tra cứu động từ bảng cấu hình giá `ServicePricing` dựa trên:
* Tuyến đường (Route: VN - INDO, INDO - VN,...)
* Loại dịch vụ (ConsignmentType: Express hoặc Consolidation)
* Ngày hiệu lực của bảng giá (`EffectiveDate`)

$$\text{Cước quốc tế} = \text{Net Weight} \times \text{Đơn giá vận chuyển quốc tế}$$

---

### 3.2. Phí vận chuyển nội địa (Domestic Shipping Fee)
Áp dụng cho đơn hàng **Ký gửi (`CONSIGNMENT`)** và được lấy theo 2 luồng:

#### A. Khi tạo báo giá tạm tính (Estimate Quotation)
* Lấy giá trị ước tính từ bảng `PricingRule` được cấu hình động theo tuyến đường.
* Rule tương ứng là các cấu hình có `RuleType`/`RuleCode`/`ConditionType` là `"DOMESTIC_FEE"` hoặc tên rule chứa chữ `"nội địa"`.
* Nếu không tìm thấy cấu hình tạm tính, mặc định là **`0 VND`**.

#### B. Khi gửi báo giá chính thức (Send/Official Quotation)
* Khi hàng đã về kho Việt Nam và được tạo mã vận đơn chặng cuối, hệ thống sẽ gộp chính xác cước phí thực tế:

$$\text{Phí chặng nội địa thực tế} = \sum \left( \text{Phí chuyển phát DeliveryFee từ các DomesticWaybill của kiện hàng} \right)$$

* Nếu đơn hàng chưa được nhập kiện tại kho, hệ thống tự động fallback về phí ước tính từ `PricingRule`.

---

### 3.3. Các loại phí khác (Chỉ áp dụng cho Đơn mua hộ / Standard)
* **Phí dịch vụ cố định (Base Service Fee):**
  * Express: `50,000 VND` (mặc định)
  * Consolidation: `30,000 VND` (mặc định)
* **Thuế & Thuế nhập khẩu (Tax and Duty):**

$$\text{Thuế} = \text{Tổng giá trị khai báo của hàng hóa} \times 10\% \text{ (Thuế suất mặc định)}$$

* **Các phụ phí phát sinh (Surcharges):**
  * Tra cứu từ `PricingRule` hoạt động (`Status == "ACTIVE"`), bao gồm: đóng thùng gỗ (`WOODEN_BOX`), kiểm đếm (`INSPECTION`), bảo hiểm hàng hóa (`INSURANCE`)...

---

## 4. Cách cấu hình trên Database (cho Quản trị viên/Admin)

Để điều chỉnh các mức cước và quy tắc tính phí động mà không cần can thiệp mã nguồn, Admin sử dụng 2 thực thể sau:

### 4.1. Bảng `ServicePricing` (Đơn giá vận chuyển quốc tế)
Admin thêm/sửa đơn giá chặng cho từng loại hình vận chuyển qua API `/api/service-pricings`:
* **`serviceType`**: `"Express"` hoặc `"Consolidation"`.
* **`originCountry`** & **`destinationCountry`**: Ví dụ `"INDO"` và `"VN"`.
* **`price`**: Đơn giá (VND/kg hoặc VND/CBM).
* **`effectiveDate`**: Ngày bắt đầu có hiệu lực của mức giá này.

### 4.2. Bảng `PricingRule` (Cấu hình cân tối thiểu, phí nội địa và phụ phí)
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
