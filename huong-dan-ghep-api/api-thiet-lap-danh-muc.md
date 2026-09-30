# Ghép API — Thiết lập danh mục (Admin)

Các danh mục phải có trước khi chạy luồng ký gửi. Quy ước chung, tài khoản demo: [`README.md`](README.md).
Thử bằng `demo.admin@vcl-demo.example`. **Sửa danh mục trên production ảnh hưởng mọi đơn thật** — thử thì tạo bản ghi mới rồi xoá/ngưng, đừng sửa bản ghi đang dùng.

| Danh mục | Dùng ở đâu |
|---|---|
| Kho + loại kho | Báo giá chọn kho `ORIGIN`; phiếu nhập kho; xếp kệ |
| Khu / kệ / ô kệ | Xếp kệ — xem [`api-kho-xep-ke-theo-khu.md`](api-kho-xep-ke-theo-khu.md) mục A |
| Quản lý kho | Ai duyệt phiếu nhập kho — [`api-ky-gui-1.md`](api-ky-gui-1.md) bước 7 |
| Bảng giá vận chuyển | Cước quốc tế theo phương án + tuyến |
| Quy định phí (pricing rules) | Dịch vụ theo kiện (kiểm hàng, bảo hiểm...), phí nội địa, VAT, hệ số quy đổi |
| Loại hàng | `productType` của từng kiện, thuế nhập khẩu |
| Cấu hình thùng gỗ | `packageConfigurationId` bắt buộc cho mỗi kiện |

---

## 1. Kho

| API | Role | Ghi chú |
|---|---|---|
| `GET /api/warehouses?isActive=&search=&regionCode=` | Admin | Tất cả kho |
| `GET /api/warehouses/active` | mọi nhân viên + Customer | `{ "items": [ { id, name, code, address, contactPhone, region, regionCode, warehouseType, isActive } ] }` |
| `POST /api/warehouses` | Admin | 201, trả object kho |
| `PUT /api/warehouses/{id}` | Admin | Trả object kho |
| `DELETE /api/warehouses/{id}` | Admin | Chặn nếu kho đã chứa kiện hoặc có lịch sử tồn kho |

```json
{ "name": "Kho Quảng Châu (Trung Quốc)", "code": "CN_WH", "address": "...", "contactPhone": "+86 20 8888 6666",
  "region": "CN", "warehouseType": "ORIGIN", "isActive": true }
```

- `warehouseType`: `ORIGIN` (kho nhận hàng ở nước ngoài — báo giá và phiếu nhập kho chỉ dùng loại này), `DESTINATION` (kho VN), `DOMESTIC`. Bỏ trống = `DESTINATION`.
- `region`: mã quốc gia ISO (`CN`, `KR`, `JP`, `VN`...).
- Tắt kho (`isActive: false`) bị chặn khi kho còn hàng, còn phiếu xuất đang xử lý hoặc phiếu tiếp nhận chưa xong.

**Quản lý kho:** `GET /api/warehouses/{id}/manager`, `PUT /api/warehouses/{id}/manager` `{ "managerId": "guid" | null, "reason": "..." }` (Admin, bắt buộc lý do).

## 2. Bảng giá vận chuyển — `/api/service-pricings`

| API | Role |
|---|---|
| `GET /api/service-pricings` | Không cần đăng nhập — danh sách (bỏ bản đã xoá), kèm các quy định phí gắn theo |
| `GET /api/service-pricings/{id}` | Không cần đăng nhập |
| `POST /api/service-pricings` | Admin — 201, trả object |
| `PUT /api/service-pricings/{id}` | Admin — `{ message }` |
| `DELETE /api/service-pricings/{id}` | Admin — xoá mềm |

```json
{ "carrierId": null, "serviceType": "Express", "originCountry": "CN", "destinationCountry": "VN",
  "unitType": "KG", "price": 10000, "currency": "VND", "effectiveDate": "2026-09-01T00:00:00Z" }
```

- `serviceType` chính là `shippingOption` khách chọn (`Express`, `Standard`). Dòng `serviceType = STORAGE` là phí lưu kho, không hiện ở danh sách phương án vận chuyển.
- Cặp `originCountry`–`destinationCountry` là tuyến (`CN`–`VN` ↔ `route: "CN-VN"`).
- Báo giá chính thức **bắt buộc** có dòng giá khớp phương án + tuyến, `price > 0` và đã tới `effectiveDate`; không có thì Sale không báo giá được.

## 3. Quy định phí — `/api/pricing-rules`

| API | Role |
|---|---|
| `GET /api/pricing-rules?orderType=CONSIGNMENT` | Không cần đăng nhập — trả **mảng trần**. `orderType=CONSIGNMENT` bỏ các phí chỉ của mua hộ |
| `GET /api/pricing-rules/{id}` | Không cần đăng nhập |
| `POST /api/pricing-rules` | Admin — 201, trả object |
| `PUT /api/pricing-rules/{id}` | Admin — `{ message }` |
| `DELETE /api/pricing-rules/{id}` | Admin |

```json
{
  "servicePricingId": null,
  "ruleName": "Phụ phí chống sốc",
  "ruleCode": "SUR_BUBBLE_WRAP",
  "ruleType": "PACKING",
  "conditionType": null,
  "conditionValue": null,
  "calculationType": "FIXED",
  "value": 15000,
  "minAmount": null,
  "maxAmount": null,
  "isRequired": false,
  "status": "ACTIVE",
  "description": "Quấn chống sốc cho kiện dễ vỡ"
}
```

| Trường | Giá trị |
|---|---|
| `calculationType` | `FIXED` (mỗi kiện) · `PERCENTAGE` (% giá trị khai báo của kiện, áp `minAmount`/`maxAmount`) · `PER_KG` · `PER_CBM` · `PER_PRODUCT` |
| `isRequired` | `true` = phí hệ thống tự áp (VAT, thuế nhập khẩu...). `false` = dịch vụ khách tự chọn |
| `servicePricingId` | Gắn với một dòng giá thì chỉ áp cho phương án đó; `null` = áp mọi phương án |
| `ruleCode` | Duy nhất |

**Rule nào thành "dịch vụ theo kiện"** (hiện ở `GET /api/orders/consignments/item-services`): `status = ACTIVE`, `isRequired = false`, và `ruleType`/`ruleCode` **không** chứa `DOMESTIC_FEE`, `PURCHASE`, `VAT`, `IMPORT_TAX`, `VOLUMETRIC_DIVISOR`, `MIN_WEIGHT`, `WOOD`, `CRATE`. Thùng gỗ không phải rule — là cấu hình thùng (mục 5).

Rule hệ thống đang dùng (không xoá): `DOMESTIC_FEE` (phí nội địa), `VAT`, `IMPORT_TAX`, `VOLUMETRIC_DIVISOR` (hệ số quy đổi thể tích, `value` = 5000).

## 4. Loại hàng — `/api/product-types`

| API | Role | Response |
|---|---|---|
| `GET /api/product-types` | Không cần đăng nhập | `{ message, data: [ { id, name } ] }` — chỉ loại đang dùng (`/all` và `/{id}` trả thêm thuế, trạng thái) |
| `GET /api/product-types/all` | Admin | Cả loại đã ngừng |
| `GET /api/product-types/{id}` | Không cần đăng nhập | `{ message, data }` |
| `POST /api/product-types` | Admin | 201 `{ message, data }` |
| `PUT /api/product-types/{id}` | Admin | `{ message }` |
| `DELETE /api/product-types/{id}` | Admin | Ngừng sử dụng |

```json
{ "name": "Đồ gốm", "importTaxRate": 0.1, "isActive": true }
```

`importTaxRate` là tỷ lệ (0.1 = 10%). Báo giá **chặn** kiện có loại hàng chưa cấu hình thuế. Tên không trùng.

## 5. Cấu hình thùng gỗ — `/api/package-configurations`

| API | Role | Response |
|---|---|---|
| `GET /api/package-configurations` | Không cần đăng nhập | Mảng trần `[ { id, configCode, configName, length, width, height, maxWeight, packageFee, status } ]` |
| `GET /api/package-configurations/options` | Không cần đăng nhập | Mảng rút gọn cho dropdown `[ { code, name, category, fee, description } ]` |
| `POST /api/package-configurations/suggest` | Đăng nhập | Gợi ý thùng theo kích thước: `{ length, width, height, weight }` → cấu hình kèm `estimatedFee` |
| `GET /api/package-configurations/{id}` | Đăng nhập | Object |
| `POST /api/package-configurations` | Admin | 201 `{ message, data }` |
| `PUT /api/package-configurations/{id}` | Admin | `{ message, data }` |
| `DELETE /api/package-configurations/{id}` | Admin | Chuyển `INACTIVE` (không xoá) |

```json
{ "configCode": "XLARGE", "configName": "Thùng gỗ XL", "length": 80, "width": 60, "height": 50,
  "maxWeight": 30, "packageFee": 90000, "status": "ACTIVE" }
```

- Thùng tiêu chuẩn: `packageFee` là phí cố định mỗi thùng.
- Thùng `CUSTOM`: `packageFee` là **đơn giá mỗi 1.000 cm³** (làm tròn lên) — kiện chọn CUSTOM bắt buộc nhập đủ dài/rộng/cao.
- Khách tạo đơn chỉ chọn được thùng `ACTIVE`.

## 6. Tài khoản nhân viên — `/api/User`

| API | Role | Ghi chú |
|---|---|---|
| `POST /api/User` | Admin | Tạo nhân viên → 201 `{ id }`. Không tạo được role `Admin` |
| `GET /api/User` | Admin, Sale | Danh sách tài khoản |
| `GET /api/User/{id}` | Admin, Sale | Chi tiết |
| `PUT /api/User/{id}/role` | Admin | Đổi role |
| `PUT /api/User/{id}/lock` · `PUT /api/User/{id}/unlock` | Admin | Khoá / mở khoá (khoá thì đăng nhập 401) |
| `GET /api/User/profile` · `PUT /api/User/profile` | Mọi tài khoản | Hồ sơ của chính mình |

```json
{ "fullName": "Nhân viên kho Quảng Châu", "email": "kho.cn@vcl.vn", "password": "matkhau123", "phone": "0900000000", "role": "WarehouseStaff", "region": "CN" }
```

- `role`: `Sale`, `OperationsManager`, `WarehouseStaff`, `Delivery` (chấp nhận cả "Warehouse Staff", "Operations Manager"). Viết sai tên role thì tài khoản tạo được nhưng **không dùng được API nào** — FE nên cho chọn từ danh sách cố định.
- `region` (mã quốc gia) bắt buộc với nhân viên kho: cho biết kho phụ trách (`CN`, `KR`, `JP`, `VN`).
- Khách hàng tự đăng ký qua `POST /api/Auth/customer/register` ([`api-ky-gui-1.md`](api-ky-gui-1.md) bước 0).

