# Source A — VCL backend (vcl-BE) đang chạy production

## Vai trò của agent mở ở thư mục này: CHỈ TRA CỨU

Chủ dự án dùng phiên này để **hỏi thông tin mới nhất** của source A. Việc sửa code, deploy, sửa DB do một phiên khác làm.

- **Không sửa gì**: không sửa file (local hay trên server), không build, không `docker compose up/down/restart`, không đổi cấu hình, không chạy SQL ghi (INSERT/UPDATE/DELETE/ALTER/CREATE/DROP), không chạy test tạo dữ liệu, không commit/push. Nếu câu hỏi dẫn tới cần sửa, chỉ báo lại cần sửa gì.
- **Luôn đọc trực tiếp trên server**, không dựa vào bản sao local (có thể cũ): `ssh root@72.61.114.184 '...'` với các lệnh đọc như `cat`, `sed -n`, `grep`, `find`, `git -c safe.directory=/root/back-end/vcl-BE log/status/diff/show`, `docker ps`, `docker logs --tail`, `docker inspect` (không in biến môi trường chứa secret), `curl` GET.
- Truy vấn DB chỉ `SELECT`, thêm `SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED` để không khoá bảng.
- Trả lời kèm nguồn: đường dẫn file + dòng, commit, hoặc câu truy vấn đã chạy; ghi rõ thời điểm kiểm tra.

Phần dưới là bối cảnh để tra cứu cho đúng chỗ.

Thư mục này là bản sao local của source A (chỉ để tham khảo cấu trúc). Source A thật nằm trên server.

## Truy cập

- Server: `ssh root@72.61.114.184` — đăng nhập bằng SSH key `~/.ssh/id_ed25519` của máy này (không dùng mật khẩu).
- Git trên server cần: `git -c safe.directory=/root/back-end/vcl-BE ...`
- Không in giá trị bí mật (connection string, JWT, SA password, khoá SePay/PayOS/GoShip) ra màn hình. SA password đọc tại chỗ: `docker inspect sqlserver --format '{{range .Config.Env}}{{println .}}{{end}}' | grep SA_PASSWORD`.

## Production

| Thành phần | Giá trị |
|---|---|
| Source A | `/root/back-end/vcl-BE` (repo `Capstone-project-team-su26/vcl-BE`, nhánh `dev`, HEAD `bdbbd52` + thay đổi luồng ký gửi **chưa commit**) |
| Compose | `/root/back-end/vcl-BE/VietnamCrossBorderLogictisSystem/docker-compose.yml` (chứa secret inline — không commit / không in ra) |
| Container | `vcl-api`, image `vcl-api:latest`, port **8085**, domain `vcl.henrytech.cloud` (và `api.fptuany.online`, `journeys.autopass.blog`) |
| DB | SQL Server container `sqlserver`, DB **`AppDb`**. API nối bằng `Server=sqlserver,1433` qua mạng Docker `sqlserver-docker_default` |
| Swagger | https://vcl.henrytech.cloud/swagger/index.html |

**Không** gọi SQL qua `72.61.114.184:14333` từ container: firewall server chặn container gọi vòng ra IP public của chính nó (container mới sẽ không kết nối được DB). `vcl-dev-vcl-api-1` (port 8086, cũng dùng `AppDb`) vẫn cấu hình kiểu cũ — restart là mất kết nối DB.

### Build & deploy

```sh
cd /root/back-end/vcl-BE/VietnamCrossBorderLogictisSystem
docker compose build           # không ảnh hưởng container đang chạy
docker compose up -d --no-build
curl -s -o /dev/null -w "%{http_code}\n" http://127.0.0.1:8085/api/orders/consignments/routes
```

Migration EF không tự chạy khi khởi động. Sinh script idempotent (`dotnet ef migrations script <from> <to> --idempotent`) rồi chạy bằng `sqlcmd -I` (bắt buộc `QUOTED_IDENTIFIER` cho filtered index). Luôn backup `AppDb` (`BACKUP ... WITH COPY_ONLY, CHECKSUM`) trước.

### Backup lần deploy 17/09/2026

- Image cũ: `vcl-api:backup-20260917-predeploy-kygui`
- Source cũ: `/root/backups/vcl-BE-20260917-predeploy-kygui.tar.gz`
- Compose cũ: `/root/backups/docker-compose.vcl-api.20260917-predeploy-kygui.yml`
- DB: `/var/opt/mssql/data/AppDb-20260917-predeploy-kygui.bak` (trong container `sqlserver`)

### Backup lần deploy xếp kệ theo khu (17/09/2026 tối)

- Image: `vcl-api:backup-20260917-predeploy-zones` · Source: `/root/backups/vcl-BE-20260917-predeploy-zones.tar.gz` · Compose: `/root/backups/docker-compose.vcl-api.20260917-predeploy-zones.yml` · DB: `/var/opt/mssql/data/AppDb-20260917-predeploy-zones.bak`

### Backup lần deploy dịch vụ theo kiện (17/09/2026 tối)

- Image: `vcl-api:backup-20260917-predeploy-itemsvc` · Source: `/root/backups/vcl-BE-20260917-predeploy-itemsvc.tar.gz` · Compose: `/root/backups/docker-compose.vcl-api.20260917-predeploy-itemsvc.yml` · DB: `/var/opt/mssql/data/AppDb-20260917-predeploy-itemsvc.bak`

Quay lại code cũ: `docker tag vcl-api:backup-20260917-predeploy-kygui vcl-api:latest && docker compose up -d --no-build` (giữ nguyên phần mạng `sqlnet` trong compose; schema mới chỉ thêm cột/bảng nên code cũ vẫn chạy).

## Môi trường thử (không phải production)

| Thành phần | Giá trị |
|---|---|
| Source | `/root/vcl-migration` (nhánh `migration/ky-gui`) |
| Deploy | `/root/vcl-migration-deploy` (`docker-compose.yml`, `app.env` quyền 600 với JWT riêng, mail/PayOS tắt; `dotnet.sh` chạy .NET SDK trong container) |
| Container / DB | `vcl-migration-api` port **8088**, DB `AppDb_migration` |
| Test | `VietnamCrossBorderLogictisSystem/tests/wave1_consignment_flow.py` (tự ký JWT bằng khoá môi trường thử, id người dùng trong `/root/vcl-migration-deploy/test_ids.json`) — 62/62 pass |

## Nghiệp vụ

- Chuẩn nghiệp vụ tham chiếu: source B ở `/Users/henry/Downloads/vcl-platform` (`docs/business-rules.md`, `docs/danh-sach-luong.md`). Kế hoạch migrate và phân tích chênh lệch: `vcl-platform/docs/migrate-source-cu/`.
- Luồng ký gửi đã chốt với chủ dự án và **đã lên production 17/09/2026** (chi tiết `VietnamCrossBorderLogictisSystem/MIGRATION_KY_GUI_DOT1.md`):
  khách đặt → Sale báo giá (không bấm duyệt đơn) → khách xác nhận + cọc → webhook/Admin xác nhận tiền (`DEPOSIT_PAID`) → **Sale lập phiếu nhập kho** → quản lý kho duyệt (phát hành PDF, mã WRN-) → khách mang phiếu tới, kho quét WRN-, khớp thì nhập kho luôn; lệch thì quản lý kho xem lại.
- **Dịch vụ theo từng kiện — đã lên production 17/09/2026 (tối)**: `items[].services[]` (danh mục `GET /api/orders/consignments/item-services`) + thùng gỗ bắt buộc `items[].packageConfigurationId`; bỏ `pricingRuleIds`/`requires*` cấp đơn (luồng mua hộ vẫn dùng cờ). Bảng `ORDER_ITEM_SERVICES`, cột `QUOTATION_FEES.order_item_id` (migration `20260917141349_ItemServicesPerParcel`). Sale sửa phí dịch vụ một kiện qua `additionalFees[].orderItemId`. Test `tests/item_services_flow.py` 38/38.
- **Xếp kệ theo loại khu — đã lên production 17/09/2026 (tối)**: khu `RECEIVING`/`QUARANTINE`/`STORAGE`/`OUTBOUND`; hàng lưu kho chỉ nằm ở khu `STORAGE` đang dùng, kiểm chung ở `VCL_BLL/Helpers/BinPlacementPolicy.cs` (put-away nhận `binCode`, relocate `toBinCode`, split/merge, nhận hàng hoàn, tự xếp phiếu nhập kho VN, gợi ý AI). API `GET /api/warehouses/{id}/zones`, `PUT /api/warehouse-zones/{id}`, `GET /api/warehouses/{id}/misplaced-parcels`. Migration dữ liệu `20260917151720_WarehouseZoneTypes` (khai loại theo tên khu). Hướng dẫn FE: `huong-dan-ghep-api/api-kho-xep-ke-theo-khu.md`. Test `tests/warehouse_zones_flow.py` 28/28. Lúc deploy có 5 kiện STORED nằm ở Kho Seoul `BIN-OUT-01` (khu xuất), 1 kiện đang PICKED.
- Hướng dẫn ghép API cho FE: `huong-dan-ghep-api/api-ky-gui-1.md` (từ tạo đơn tới khách mang phiếu tới kho, quản lý kho duyệt, xếp kệ).
- **Tài khoản + dữ liệu demo trên production (17/09/2026):** 8 tài khoản `demo.*@vcl-demo.example` (admin, sale, quanlykho, kho.cn, kho.vn, giaohang, khach1, khach2) và 15 đơn mẫu `[DEMO]` ở mọi bước ký gửi → xếp kệ (Kho Quảng Châu). Danh sách đơn: `huong-dan-ghep-api/README.md`. Script tạo: `VietnamCrossBorderLogictisSystem/tests/seed_demo_data.py` (mật khẩu đọc từ stdin, không lưu ở đâu — chỉ chủ dự án giữ). Đừng xoá/sửa các đơn demo trừ khi được yêu cầu.
- Hướng dẫn FE: `huong-dan-ghep-api/README.md` (bắt đầu), `api-thiet-lap-danh-muc.md`, `api-ky-gui-1.md`, `api-kho-xep-ke-theo-khu.md`.
- Chưa làm theo chuẩn: xuất kho quốc tế, lô vận chuyển, về VN, lưu kho VN, giao hàng, huỷ/hoàn tiền.

## Quy tắc làm việc

- Chủ dự án không muốn đụng các hệ thống khác trên server (nhiều dự án khác cùng chạy). Chỉ sửa đúng phần được yêu cầu; hỏi trước khi thay container/DB production.
- Không thêm cờ cấu hình để bật/tắt quy tắc — chủ dự án muốn luồng cố định trong code, bỏ code thừa.
- Không dùng `rsync --delete` từ local lên server: sẽ xoá file chỉ sinh trên server (ví dụ script SQL).
- Không commit/push khi chưa được yêu cầu.
- Webhook SePay trên production cần `SePay__ApiKey` hoặc `SePay__WebhookSecret`; hiện **chưa cấu hình** nên webhook bị từ chối (401) — tiền SePay phải Admin xác nhận tay tới khi chủ dự án điền khoá.
