# Failover giữa hai server

Hai server là hai bản sao nóng của cùng một hệ thống, dùng chung một database. App server chết thì đổi sang máy còn lại; database chết thì không có failover.

| | zushin | henrytech |
|---|---|---|
| Domain API | `https://api-vcl.zushin.io.vn` | `https://vcl.henrytech.cloud` |
| Reverse proxy | Traefik (chỉ stack `main`) | port publish trực tiếp |
| Cổng host `main` | 8085 | 8085 |
| Cổng host `dev` | 8086 | 8086 |

Mỗi branch là một stack riêng: thư mục `/root/vcl-deploy/<branch>`, compose project `vcl-<branch>`. Nhờ vậy deploy `main` và `dev` không ghi đè nhau, và cron (chỉ chạy trên `main`) không kéo server về code cũ của branch khác.

Cả hai máy chạy cùng một image từ GHCR và cùng một file `app.env` sinh từ cùng một bộ GitHub secret, nên máy dự bị luôn giống máy chính. Workflow `Deploy` chạy lại mỗi 15 phút, nên máy nào đang chết lúc push thì sống lại sẽ tự bắt kịp trong vòng 15 phút.

## Khi một máy chết

1. **Frontend**: đổi base URL API sang domain máy còn lại, deploy lại FE.
2. **SePay**: vào dashboard SePay đổi webhook URL sang `https://<domain máy mới>/api/payments/sepay-webhook`.
3. **GHN**: gọi `POST /api/ghn/register-webhook` trên máy mới (không cần truyền tham số — nó tự lấy `GHNSettings:WebhookUrl` của chính máy đó).
4. **CORS**: kiểm tra domain frontend có trong `Cors:AllowedOrigins` ở `appsettings.json`.

Bỏ bước 2 hoặc 3 thì thanh toán và cập nhật vận đơn sẽ chết im lặng dù API vẫn xanh.

## Deploy thủ công một máy

Actions → `Deploy` → `Run workflow`, chọn `target` là `zushin`, `henrytech` hoặc `both`. Deploy là thao tác reconcile: nếu image đang chạy đã đúng và `.env` không đổi thì job bỏ qua, chạy lại nhiều lần vô hại.

## Đổi một biến cấu hình

Nếu là giá trị bí mật: sửa secret tương ứng trong repo settings. Nếu là giá trị không bí mật (`ApiUrl`, `ShopId`, `ReturnUrl`...): sửa trực tiếp trong bước `Build .env cho server` của `.github/workflows/deploy.yml`. Cả hai cách đều không cần chạm vào server.

Sau đó chạy `Deploy` (hoặc chờ cron ≤ 15 phút): job thấy `app.env` khác nội dung cũ nên sẽ `up -d` lại container dù image không đổi.

Hai file cấu hình trên server tách riêng có lý do: `app.env` là env của container và compose không interpolate nó, nên password chứa `$` vẫn nguyên vẹn; `.env` chỉ giữ `IMAGE`, `API_PORT`, `TRAEFIK_ROUTER_RULE` cho compose interpolate.

Health check fail thì job tự rollback về image trước đó (`vcl-api:previous-vcl-<branch>`) rồi báo đỏ.

## Secrets cần có trong repo

Dùng lại nguyên các secret đã có: `CONNECTIONSTRING`, `JWT_SECRET_KEY`, `PAYOS_CLIENT_ID`, `PAYOS_API_KEY`, `PAYOS_CHECKSUM_KEY`, `SEPAY_WEBHOOK_SECRET`, `HOST`, `USER_NAME`, `PASS_WORD`.

Cần tạo thêm:

| Secret | Dùng cho |
|---|---|
| `MAIL_APP_PASSWORD` | Gmail app password, dùng cho cả `MailSettings` và `SmtpSettings` |
| `CLOUDINARY_API_KEY`, `CLOUDINARY_API_SECRET` | Cloudinary |
| `GHN_TOKEN` | GHN API token |
| `GOSHIP_CLIENT_SECRET`, `GOSHIP_TOKEN` | Goship |
| `ZUSHIN_HOST`, `ZUSHIN_USER_NAME`, `ZUSHIN_PASS_WORD` | SSH vào zushin |

Xoá được sau khi bỏ workflow cũ: `APP_PORT`, `DOCKER_PORT`, `DOCKER_IMAGE_NAME` — không còn chỗ nào dùng.

Thiếu bất kỳ secret nào ở trên thì job dừng ngay từ bước dựng `app.env` và in ra tên secret còn thiếu, chứ không deploy ra container thiếu config.

Image GHCR pull bằng `GITHUB_TOKEN` của chính workflow run, không cần secret riêng.

## Việc phải làm bằng tay trên GitHub

- **Branch protection**: chỉ để job `build` là required status check. Hai job `deploy` không được là required — một máy chết không được chặn merge của cả team.
- **Rotate credentials**: connection string production, `JWT__SecretKey`, Gmail app password, Cloudinary `ApiSecret`, GHN token, Goship `ClientSecret`/`Token` đều đã từng commit lên git nên vẫn còn trong history. Đổi file là chưa đủ, phải cấp lại giá trị mới rồi cập nhật vào `APP_ENV_FILE`.
