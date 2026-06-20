# VCL System - Hướng dẫn sử dụng Postman kiểm thử hệ thống

Tài liệu này hướng dẫn cách sử dụng hai file Postman Collection:
1. [Consignment_API.postman_collection.json](file:///c:/DO_AN/vcl-BE/docs/postman/Trong/Consignment_API.postman_collection.json) để kiểm thử luồng quản lý cấu hình bảng giá và tính năng tính báo giá ký gửi.
2. [Parcel_CheckIn_API.postman_collection.json](file:///c:/DO_AN/vcl-BE/docs/postman/Trong/Parcel_CheckIn_API.postman_collection.json) để kiểm thử quy trình check-in kiện hàng bằng mã gửi hàng.

---

## 1. Chuẩn bị môi trường kiểm thử

### Khởi động Server backend
Đảm bảo API server được chạy trên máy local của bạn.
- URL mặc định (HTTP): `http://localhost:5209` hoặc (HTTPS): `https://localhost:7237`.

### Import Collection vào Postman
1. Mở Postman.
2. Nhấn nút **Import** ở góc trên cùng bên trái.
3. Chọn các file `.postman_collection.json` mong muốn ở trên và chọn **Open**.

### Cấu hình biến môi trường (Environment Variables)
Tạo hoặc cấu hình một Environment trong Postman với các biến sau:

| Tên biến | Kiểu dữ liệu | Giá trị mẫu | Mô tả |
| :--- | :--- | :--- | :--- |
| `baseUrl` | String | `http://localhost:5209` | Địa chỉ chạy backend local |
| `adminToken` | Secret / String | *(Tự động điền sau khi login Admin)* | JWT Token của tài khoản Admin |
| `customerToken` | Secret / String | *(Tự động điền sau khi login Customer)* | JWT Token của tài khoản Khách hàng |
| `pricingRuleId1` | String | *(Tự động điền sau khi tạo Rule 1)* | ID của cấu hình giá thứ nhất |
| `pricingRuleId2` | String | *(Tự động điền sau khi tạo Rule 2)* | ID của cấu hình giá thứ hai |

---

## 2. Các luồng kiểm thử chi tiết

Mở thư mục **VCL - Consignment Pricing Rules Test** trong Postman và thực hiện tuần tự các API sau:

### Phần 1: Thiết lập & Đăng nhập (Setup & Authentication)

#### 1. Seed Admin
* **Method**: `GET`
* **URL**: `{{baseUrl}}/api/Test/seed-admin`
* **Mục đích**: Tạo sẵn tài khoản Admin thử nghiệm (`admin@test.com` / `Admin123`).

#### 2. Seed Customer
* **Method**: `GET`
* **URL**: `{{baseUrl}}/api/Test/seed-customer`
* **Mục đích**: Tạo sẵn tài khoản Khách hàng thử nghiệm (`customer@test.com` / `Customer123`).

#### 3. Login Admin
* **Method**: `POST`
* **URL**: `{{baseUrl}}/api/Auth/login`
* **Body (JSON)**:
  ```json
  {
      "email": "admin@test.com",
      "password": "Admin123"
  }
  ```
* **Mục đích**: Lấy Token Admin. Script Test của request này sẽ tự động lưu token vào biến môi trường `adminToken`.

#### 4. Login Customer
* **Method**: `POST`
* **URL**: `{{baseUrl}}/api/Auth/login`
* **Body (JSON)**:
  ```json
  {
      "email": "customer@test.com",
      "password": "Customer123"
  }
  ```
* **Mục đích**: Lấy Token Khách hàng để kiểm tra quyền và tạo ký gửi. Token được lưu vào biến `customerToken`.

---

### Phần 2: Quản lý cấu hình bảng giá (Pricing Rules CRUD)

Tất cả các API dưới đây (ngoại trừ API phân quyền) đều yêu cầu Header **Authorization: Bearer `{{adminToken}}`** (đã được cấu hình tự động trong Auth tab của folder).

#### 5. Tạo cấu hình giá 1 (Air - Express)
* **Method**: `POST`
* **URL**: `{{baseUrl}}/api/pricing-rules`
* **Body (JSON)**:
  ```json
  {
      "shippingServiceType": "Air",
      "consignmentType": "Express",
      "route": "Vietnam",
      "unitType": "kg",
      "pricePerWeight": 55000.00,
      "pricePerVolume": 120000.00,
      "serviceFee": 50000.00,
      "isActive": true
  }
  ```
* **Mục đích**: Thêm cấu hình giá vận chuyển đường bay, dịch vụ Express, tuyến Việt Nam. ID trả về được lưu vào `pricingRuleId1`.

#### 6. Tạo cấu hình giá 2 (Sea - Consolidation)
* **Method**: `POST`
* **URL**: `{{baseUrl}}/api/pricing-rules`
* **Body (JSON)**:
  ```json
  {
      "shippingServiceType": "Sea",
      "consignmentType": "Consolidation",
      "route": "Vietnam",
      "unitType": "kg",
      "pricePerWeight": 28000.00,
      "pricePerVolume": 80000.00,
      "serviceFee": 30000.00,
      "isActive": true
  }
  ```
* **Mục đích**: Thêm cấu hình giá đường biển, gom hàng (Consolidation), tuyến Việt Nam. ID trả về được lưu vào `pricingRuleId2`.

#### 7. Tạo trùng cấu hình (Duplicate Check) - Dự kiến Lỗi 400
* **Method**: `POST`
* **URL**: `{{baseUrl}}/api/pricing-rules`
* **Body**: Gửi lại chính xác body của Rule 1.
* **Kết quả mong đợi**: Trả về mã lỗi `400 Bad Request` cùng thông báo: *"Không cho tạo trùng cấu hình giá cùng loại dịch vụ, loại ký gửi và tuyến vận chuyển."*

#### 8. Xem danh sách cấu hình bảng giá
* **Method**: `GET`
* **URL**: `{{baseUrl}}/api/pricing-rules`
* **Mục đích**: Kiểm tra danh sách hiển thị đầy đủ các cấu hình vừa tạo.

#### 9. Lấy chi tiết cấu hình giá theo ID
* **Method**: `GET`
* **URL**: `{{baseUrl}}/api/pricing-rules/{{pricingRuleId1}}`
* **Mục đích**: Lấy thông tin chi tiết của Rule 1 bằng ID.

#### 10. Lấy cấu hình không tồn tại - Dự kiến Lỗi 404
* **Method**: `GET`
* **URL**: `{{baseUrl}}/api/pricing-rules/99999999-9999-9999-9999-999999999999`
* **Kết quả mong đợi**: Trả về mã lỗi `404 Not Found`.

#### 11. Cập nhật cấu hình giá (Air - Express)
* **Method**: `PUT`
* **URL**: `{{baseUrl}}/api/pricing-rules/{{pricingRuleId1}}`
* **Body (JSON)**:
  ```json
  {
      "shippingServiceType": "Air",
      "consignmentType": "Express",
      "route": "Vietnam",
      "unitType": "kg",
      "pricePerWeight": 58000.00,
      "pricePerVolume": 125000.00,
      "serviceFee": 52000.00,
      "isActive": true
  }
  ```
* **Mục đích**: Cập nhật giá theo cân nặng từ 55,000 -> 58,000 và service fee từ 50,000 -> 52,000.

#### 12. Cập nhật trùng lặp - Dự kiến Lỗi 400
* **Method**: `PUT`
* **URL**: `{{baseUrl}}/api/pricing-rules/{{pricingRuleId2}}`
* **Body (JSON)**: Gửi thông tin trùng hoàn toàn với Rule 1 (Air - Express - Vietnam).
* **Kết quả mong đợi**: Trả về mã lỗi `400 Bad Request` và thông báo lỗi trùng cấu hình.

#### 13. Khách hàng truy cập bị từ chối - Dự kiến Lỗi 403
* **Method**: `GET`
* **URL**: `{{baseUrl}}/api/pricing-rules`
* **Auth**: Sử dụng Bearer `{{customerToken}}` thay vì Admin.
* **Kết quả mong đợi**: Trả về `403 Forbidden` (Khách hàng không được quyền thao tác).

#### 14. Xóa cấu hình giá
* **Method**: `DELETE`
* **URL**: `{{baseUrl}}/api/pricing-rules/{{pricingRuleId1}}`
* **Mục đích**: Xóa cấu hình giá khỏi hệ thống.

---

### Phần 3: Check-in kiện hàng bằng mã gửi hàng (Parcel Check-in Flow)

Các API này hỗ trợ quy trình nhân viên kho (Warehouse Staff) quét hoặc nhập mã gửi hàng (Consignment Code) của yêu cầu ký gửi đã được duyệt để tạo Parcel (kiện hàng thực tế), cập nhật cân nặng, kích thước thực tế và lưu vào hệ thống.

#### 15. Seed Staff
* **Method**: `GET`
* **URL**: `{{baseUrl}}/api/Test/seed-staff`
* **Mục đích**: Tạo sẵn tài khoản nhân viên kho thử nghiệm (`staff@employee.com` / `Employee123`) có vai trò `WarehouseStaff`.

#### 16. Login Staff
* **Method**: `POST`
* **URL**: `{{baseUrl}}/api/auth/login`
* **Body (JSON)**:
  ```json
  {
      "email": "staff@employee.com",
      "password": "Employee123"
  }
  ```
* **Mục đích**: Lấy Token của Staff. Script Test của request này sẽ tự động lưu token vào biến môi trường `staffToken`.

#### 17. Staff Check-in kiện hàng (Happy Path)
* **Method**: `POST`
* **URL**: `{{baseUrl}}/api/parcels/check-in`
* **Auth**: Bearer `{{staffToken}}`
* **Body (JSON)**:
  ```json
  {
      "shippingCode": "{{consignmentCode}}",
      "actualWeight": 5.4,
      "length": 35.0,
      "width": 25.0,
      "height": 15.0
  }
  ```
* **Mục đích**: Tạo một `Parcel` mới cho yêu cầu ký gửi, tự động tính volumetricWeight và chargeableWeight, cập nhật trạng thái Parcel thành `RECEIVED` và Order thành `WAREHOUSE_RECEIVED`.

#### 18. Check-in lần 2 (Đã RECEIVED) - Dự kiến Lỗi 400
* **Method**: `POST`
* **URL**: `{{baseUrl}}/api/parcels/check-in`
* **Auth**: Bearer `{{staffToken}}`
* **Body (JSON)**: Gửi lại body của request Check-in trên.
* **Kết quả mong đợi**: Trả về `400 Bad Request` cùng thông báo: *"Yêu cầu ký gửi không ở trạng thái APPROVED."* (do đơn đã chuyển sang `WAREHOUSE_RECEIVED`).

#### 19. Check-in với mã sai định dạng - Dự kiến Lỗi 400
* **Method**: `POST`
* **URL**: `{{baseUrl}}/api/parcels/check-in`
* **Auth**: Bearer `{{staffToken}}`
* **Body (JSON)**:
  ```json
  {
      "shippingCode": "INVALID-FORMAT-123",
      "actualWeight": 5.4
  }
  ```
* **Kết quả mong đợi**: Trả về `400 Bad Request` cùng thông báo: *"Mã gửi hàng không hợp lệ."*

#### 20. Check-in với mã không tồn tại - Dự kiến Lỗi 404
* **Method**: `POST`
* **URL**: `{{baseUrl}}/api/parcels/check-in`
* **Auth**: Bearer `{{staffToken}}`
* **Body (JSON)**:
  ```json
  {
      "shippingCode": "VCL-00000000000000-000000",
      "actualWeight": 5.4
  }
  ```
* **Kết quả mong đợi**: Trả về `404 Not Found` cùng thông báo: *"Không tìm thấy yêu cầu ký gửi."*

#### 21. Khách hàng thực hiện Check-in - Dự kiến Lỗi 403
* **Method**: `POST`
* **URL**: `{{baseUrl}}/api/parcels/check-in`
* **Auth**: Bearer `{{customerToken}}`
* **Kết quả mong đợi**: Trả về `403 Forbidden` (Chỉ nhân viên được check-in).

---

## 3. Chạy kiểm thử tự động (Postman Runner)

Bạn có thể chạy toàn bộ kịch bản kiểm thử trên một cách tự động bằng tính năng **Collection Runner**:
1. Nhấp chuột phải vào tên collection **VCL - Consignment Pricing Rules Test**.
2. Chọn **Run collection**.
3. Chọn các test case, đảm bảo chọn đúng Environment chứa biến `baseUrl`.
4. Nhấn **Run VCL - Consignment Pricing Rules Test**.
5. Tất cả các bước kiểm thử viết bằng script JS trong Postman sẽ chạy tự động và hiển thị trạng thái `Passed`/`Failed` trực quan.
