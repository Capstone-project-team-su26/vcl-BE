# vcl-BE

Backend cho **Vietnam Cross-Border Logistics System (VCL)**.

Solution chính: `VietnamCrossBorderLogictisSystem/`

## Database seeding (Development)

Khi chạy API ở môi trường **Development**, hệ thống tự:

1. Chạy EF Core migrations (`Database.MigrateAsync`)
2. Seed tài khoản mẫu nếu email chưa tồn tại

Logic seed nằm tại `VietnamCrossBorderLogictisSystem/VCL_BLL/Seeding/VCLDatabaseSeeder.cs` và được gọi từ `VCL_API/Program.cs`.

### Bật / tắt seed

| File | `SeedData:Enabled` |
|---|---|
| `VCL_API/appsettings.json` | `false` (mặc định) |
| `VCL_API/appsettings.Development.json` | `true` |

Seeding chỉ chạy khi **đồng thời** thỏa:

- `ASPNETCORE_ENVIRONMENT=Development`
- `SeedData:Enabled = true`

### Chạy local

1. Cấu hình connection string SQL Server trong `VCL_API/appsettings.json` hoặc User Secrets (`ConnectionStrings:DefaultConnection`).
2. Cấu hình `JWT:SecretKey` (bắt buộc cho auth).
3. Chạy API:

```bash
dotnet run --project VietnamCrossBorderLogictisSystem/VCL_API
```

Swagger: `http://localhost:5209/swagger`

### Tài khoản seed

Chỉ dùng cho môi trường dev/test. Mật khẩu được hash bằng `PasswordHelper` giống flow đăng ký thật.

| Vai trò | Email | Mật khẩu | Ghi chú |
|---|---|---|---|
| Admin | `admin@vcl.com` | `Admin123!` | `userType=Employee`, `status=Active` |
| Sale | `sale01@employee.com` | `Employee123` | |
| WarehouseStaff | `warehouse@vcl.com` | `Employee123` | |
| OperationsManager | `ops@vcl.com` | `Employee123` | |
| Delivery | `delivery@vcl.com` | `Employee123` | |
| Customer | `customer@example.com` | `Password123` | Có bản ghi `Customer`, email đã verify, `status=Active` |

Đăng nhập qua `POST /api/Auth/login`:

```json
{
  "email": "admin@vcl.com",
  "password": "Admin123!"
}
```

### Lưu ý

- Seeder **idempotent**: chạy lại app không tạo trùng email đã có.
- Production mặc định **không** seed (`SeedData:Enabled = false`).
- Muốn thêm/sửa tài khoản seed: chỉnh mảng trong `VCLDatabaseSeeder.cs`.

## Tài liệu API

Xem thêm: [`docs/API_INTEGRATION.md`](docs/API_INTEGRATION.md)
