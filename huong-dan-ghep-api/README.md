# Hướng dẫn ghép API — VCL (source A)

Bộ tài liệu cho FE ghép API các luồng **đang chạy trên production**. Cập nhật 19/09/2026 (bổ sung luồng xuất kho quốc tế, hàng về Việt Nam, mua hộ).

| File | Nội dung |
|---|---|
| **README.md** (file này) | Bắt đầu ở đây: môi trường, quy ước chung, **tài khoản demo**, **đơn demo ở từng trạng thái**, ID danh mục |
| [`api-thiet-lap-danh-muc.md`](api-thiet-lap-danh-muc.md) | Admin thiết lập: kho, bảng giá, phụ phí/dịch vụ, loại hàng, cấu hình thùng gỗ |
| [`api-ky-gui-1.md`](api-ky-gui-1.md) | Luồng ký gửi: đăng ký/đăng nhập → tạo đơn (dịch vụ theo kiện) → báo giá → cọc → phiếu nhập kho → quản lý kho duyệt → kho nhận hàng → xếp kệ |
| [`api-kho-xep-ke-theo-khu.md`](api-kho-xep-ke-theo-khu.md) | Kho: loại khu, ô kệ, sơ đồ, xếp kệ, chuyển ô, kiện sai khu, tách/gộp kiện, tồn kho, kiểm kê |
| [`api-hang-ve-viet-nam.md`](api-hang-ve-viet-nam.md) | Hàng về Việt Nam: kho VN tiếp nhận + cân đo, sự cố hàng hoá, khách chọn giao ngay / gửi kho từng kiện, tất toán theo cân đo VN, nhập kho VN, giao nội địa, giao lại, bằng chứng giao, khiếu nại, đóng đơn |
| [`api-xuat-kho.md`](api-xuat-kho.md) | Xuất kho quốc tế: phiếu xuất kho → quản lý kho duyệt → bốc hàng sang khu xuất → lập lô → bàn giao → hành trình về VN; giấy tờ đính kèm, giữ hàng; **khách theo dõi tiến độ đơn**, **Sale theo dõi lô** |
| [`api-mua-ho.md`](api-mua-ho.md) | Mua hộ: yêu cầu → báo giá (trả trước / tạm tính) → khách trả trước → đơn mua NCC (giá chênh, Admin duyệt, đặt NCC, tiến độ NCC) → đơn kho + phiếu tiếp nhận, từ đó đi như ký gửi |

Phạm vi: **từ khách tạo đơn ký gửi tới khi hàng nằm trên kệ kho quốc tế**. Xuất kho quốc tế, vận chuyển, về VN, thanh toán cuối, lưu kho VN, giao hàng sẽ có tài liệu riêng khi các luồng đó chuẩn hoá xong.

Chưa có trong tài liệu (thuộc luồng sau, đang chuẩn hoá): phiếu xuất kho `/api/warehouse-release-requests`, lô vận chuyển `/api/international-shipments`, kiểm hàng tại VN `PUT /api/parcels/{id}/inspection`, thanh toán cuối / phí lưu kho `/api/orders/{id}/payments/final|storage-fee`, hoàn tất đơn `.../complete`, `.../customer-confirm`, `.../reject-unpaid`, giao hàng, hàng hoàn. Admin gửi thông báo tay: `POST /api/notifications`.

---

## 1. Môi trường

| | |
|---|---|
| Base URL | **`https://vcl.henrytech.cloud`** — chỉ dùng domain này. `api-vcl.zushin.io.vn` là một bản deploy khác chạy **code cũ**, không có các API mới (dịch vụ theo kiện, khu kho...) |
| Swagger | `https://vcl.henrytech.cloud/swagger/index.html` |
| Link PDF phiếu | `receiptPdfUrl` hiện server trả dạng `https://api-vcl.zushin.io.vn/api/public/receipts/{publicKey}` (cấu hình cũ). Mở được, nhưng nên đổi host sang `https://vcl.henrytech.cloud` giữ nguyên path — cùng một API, không cần token |

## 2. Quy ước chung

**Đăng nhập** `POST /api/Auth/login` `{ "email", "password" }` → `{ token, expiresAt, userId, fullName, role, region }`.
Mọi API (trừ chỗ ghi "không cần đăng nhập") gửi `Authorization: Bearer <token>`. FE dùng `role` để chọn màn hình.

**Response** — source A chưa thống nhất một kiểu bọc, FE xử lý theo từng API (tài liệu ghi rõ từng chỗ):

| Kiểu | Ví dụ |
|---|---|
| `{ "message", "data": ... }` | Đa số API nghiệp vụ |
| `{ "message", "data": { "items", "totalCount", "pageNumber", "pageSize", "totalPages" } }` | Danh sách phân trang |
| `{ "items": [...] }` hoặc `{ "message", "items": [...] }` | `/api/warehouses/active`, `/locations`, `/api/inventories` |
| Object trần | `login`, `/api/User/profile`, `/api/pricing-rules`, `/api/package-configurations`, tạo ô kệ (201) |
| `{ "success", "message", "data" }` | `/api/notifications` |

**Lỗi** luôn có `{ "message": "..." }` (tiếng Việt, hiển thị nguyên cho người dùng).

| Mã | Khi nào |
|---|---|
| 400 | Sai dữ liệu / sai bước nghiệp vụ |
| 401 | Chưa đăng nhập, token hết hạn — **và cả khi khách xem đơn không phải của mình** ở `GET /api/orders/consignments/{id}` |
| 403 | Không đủ quyền (role, tự duyệt phiếu mình lập, không phải quản lý kho...) |
| 404 | Không tìm thấy |
| 409 | Trùng (đơn đã có phiếu mở, dòng đã kiểm đếm) hoặc dữ liệu vừa bị người khác sửa → tải lại rồi thao tác lại |

**Thông báo trong app:** `GET /api/notifications` → `{ success, message, data: { items: [ { id, title, content, isRead, createdAt } ], totalCount, ... }, unreadCount }`; `PUT /api/notifications/{id}/read`; `PUT /api/notifications/read-all`.

**Thông tin tài khoản:** `GET /api/User/profile` → `{ id, fullName, email, phone, role, userType, status, region, customerCode, ... }`; `PUT /api/User/profile`.

## 3. Vai trò

| Role | Việc trong phạm vi tài liệu |
|---|---|
| `Customer` | Tạo đơn, chọn dịch vụ từng kiện, xem/xác nhận/từ chối báo giá, trả cọc, xem phiếu nhập kho, huỷ đơn chưa trả tiền, theo dõi hành trình đơn, giữ hàng tại kho nguồn, chọn giao ngay / gửi kho từng kiện, xem trước và trả tất toán, đặt giao, chọn cách xử lý sự cố, khiếu nại, xác nhận đã nhận |
| `Sale` | Xem đơn, yêu cầu bổ sung / từ chối, báo giá, lập phiếu nhập kho, theo dõi hàng đợi việc, **cập nhật hành trình lô sau khi hàng rời kho nguồn** (mỗi mốc tự báo khách) |
| `Admin` | Thiết lập danh mục, duyệt giá ngoại lệ, xác nhận tiền chuyển khoản, gán quản lý kho, ghi nhận đã chi bồi thường, làm thay (bắt buộc lý do) |
| `OperationsManager` | Quản lý kho: duyệt phiếu nhập kho, **duyệt phiếu xuất kho**, **quyết định sự cố hàng hoá**, duyệt biên bản lệch, khai loại khu (khi kho chưa gán quản lý riêng thì role này duyệt) |
| `WarehouseStaff` | Quét phiếu, check-in, kiểm đếm, xếp kệ, chuyển ô, tách/gộp kiện, kiểm kê; lập phiếu xuất kho, bốc hàng sang khu xuất, lập lô, bàn giao; tiếp nhận + cân đo ở kho VN, mở sự cố, nhận hàng hoàn. `region` cho biết kho phụ trách (CN/KR/JP/VN) — thao tác kho ngoài vùng bị 403 |
| `Delivery` | Giao hàng nội địa (ngoài phạm vi tài liệu này) |

## 4. Tài khoản demo (production)

Đã tạo sẵn trên production để thử. **Mật khẩu không ghi trong repo — xin chủ dự án.**

| Email | Role | Vùng | Dùng để |
|---|---|---|---|
| `demo.admin@vcl-demo.example` | Admin | — | Duyệt giá ngoại lệ, xác nhận tiền, thiết lập danh mục |
| `demo.sale@vcl-demo.example` | Sale | — | Báo giá, lập phiếu nhập kho |
| `demo.quanlykho@vcl-demo.example` | OperationsManager | — | Duyệt phiếu nhập kho, duyệt biên bản lệch, khai loại khu |
| `demo.kho.cn@vcl-demo.example` | WarehouseStaff | CN | Kho Quảng Châu: quét phiếu, check-in, xếp kệ |
| `demo.kho.vn@vcl-demo.example` | WarehouseStaff | VN | Kho HCM (luồng về VN — tài liệu sau) |
| `demo.giaohang@vcl-demo.example` | Delivery | — | Giao hàng (tài liệu sau) |
| `demo.khach1@vcl-demo.example` | Customer | — | Khách có 14 đơn mẫu ở mọi trạng thái |
| `demo.khach2@vcl-demo.example` | Customer | — | Khách thứ hai — thử kiểm quyền (khách 1 không xem được đơn của khách 2) |

Đuôi `vcl-demo.example` là tên miền dành riêng, email thông báo không gửi tới ai.

## 5. Đơn demo — mỗi đơn dừng ở một bước

Tất cả tuyến **CN → VN**, phương án `Express`, kho nhận **Kho Quảng Châu**. Tên đơn nằm ở `receiverName` và `note` (bắt đầu `[DEMO]`) — tìm nhanh bằng `GET /api/orders/consignments?pageSize=100` rồi lọc `receiverName`.

| Đơn (`receiverName`) | Mã đơn | Trạng thái | Đăng nhập | Thử bước tiếp theo | Tài liệu |
|---|---|---|---|---|---|
| Đơn 01 · vừa tạo | VCL-20260917161921-540135 | `PENDING_REVIEW` | demo.sale | Báo giá / yêu cầu bổ sung. Có dịch vụ theo kiện (kiểm hàng + bảo hiểm, đóng lại carton) | ký gửi B2–B3 |
| Đơn 02 · cần bổ sung | VCL-20260917161922-247332 | `NEED_MORE_INFO` | demo.sale | Báo giá sau khi khách bổ sung | B2–B3 |
| Đơn 03 · chờ khách xác nhận báo giá | VCL-20260917161922-926264 | `QUOTATION_SENT` | demo.khach1 | Xem báo giá, `confirm-and-pay` hoặc từ chối | B4 |
| Đơn 04 · giá ngoại lệ chờ Admin | VCL-20260917161922-218636 | `PENDING_REVIEW` + báo giá `PENDING_PRICE_APPROVAL` | demo.admin | Duyệt / từ chối giá ngoại lệ (Sale giảm phí kiểm hàng 1 kiện) | B3 |
| Đơn 05 · khách từ chối báo giá | VCL-20260917161922-939086 | `QUOTATION_REJECTED` | demo.sale | Báo giá lại | B3 |
| Đơn 06 · chờ Admin xác nhận tiền cọc | VCL-20260917161923-693859 | `WAITING_DEPOSIT` | demo.admin | `pending-approval` → approve / reject | B5 |
| Đơn 07 · đã cọc, chờ lập phiếu nhập kho | VCL-20260917161923-937737 | `DEPOSIT_PAID` | demo.sale | `action-queue` → lập phiếu | B6 |
| Đơn 08 · phiếu chờ quản lý kho duyệt | VCL-20260917161923-114710 | `DEPOSIT_PAID`, phiếu `PENDING_APPROVAL` | demo.quanlykho | Duyệt / từ chối phiếu | B7 |
| Đơn 09 · phiếu đã duyệt, chờ khách mang hàng | VCL-20260917161924-468682 | phiếu `ACTIVE` **WRN-15984094** | demo.kho.cn (khách: demo.khach1 xem PDF) | Quét `WRN-15984094`, check-in | B8 |
| Đơn 10 · kho mới nhận 1/2 kiện | VCL-20260917161925-568935 | phiếu `PARTIALLY_RECEIVED` **WRN-65978595** | demo.kho.cn | Check-in kiện còn lại | B8 |
| Đơn 11 · nhận lệch, chờ quản lý kho xem | VCL-20260917161926-286936 | `CHECKED_IN`, phiếu `RECEIVED` + `requiresReview` **WRN-73021520** | demo.quanlykho | Duyệt / từ chối biên bản lệch | B8.3 |
| Đơn 12 · nhận đủ, chờ xếp kệ | VCL-20260917161928-486864 | `CHECKED_IN`, kiện `RECEIVED` **WRN-38872624** | demo.kho.cn | Xếp kệ: quét `BIN-A1-01` hoặc `BIN-A1-02` | B9, kho B |
| Đơn 13 · đã xếp kệ | VCL-20260917161929-805650 | `WAREHOUSE_RECEIVED`, kiện `STORED` ở `BIN-A1-01` | demo.kho.cn | Tồn kho, chuyển ô, tách/gộp kiện; **điểm bắt đầu của luồng xuất kho**: lập phiếu xuất → duyệt → bốc sang `BIN-OUT-01` | kho C–E, G; xuất kho A–H |
| Đơn 14 · khách đã huỷ | VCL-20260917161930-481825 | `CANCELLED` | demo.khach1 | Chỉ xem | B1 |
| Đơn 15 · của khách 2 | VCL-20260917161931-264171 | `QUOTATION_SENT` | demo.khach2 | Khách 2 xác nhận; khách 1 gọi chi tiết → 401 | B4 |

Thao tác thử sẽ làm đơn đổi trạng thái. Cần bộ đơn mới thì nhờ backend chạy lại `tests/seed_demo_data.py --force`.

Ô kệ Kho Quảng Châu để thử luật khu: `BIN-A1-01`, `BIN-A1-02`, `BIN-A2-01`, `BIN-001` (khu lưu kho — xếp kệ được); `BIN-RCV-01` (khu nhận), `BIN-OUT-01`, `BIN-OUT-02` (khu xuất) — xếp kệ vào sẽ bị 400.
Hai ô `BIN-OUT-*` chỉ dùng ở **bước bốc hàng** của luồng xuất kho ([`api-xuat-kho.md`](api-xuat-kho.md) mục D), không dùng cho xếp kệ.

**Chuẩn bị cho luồng xuất kho:** 4 kho đều chưa gán quản lý riêng nên `demo.quanlykho` (OperationsManager) duyệt phiếu xuất. Production hiện chỉ khai **một tuyến `CN_VN_ROAD_GZ_HN`** (Kho Quảng Châu → Kho HCM) — muốn thử xuất từ Seoul/Tokyo thì Admin khai tuyến trước ([`api-thiet-lap-danh-muc.md`](api-thiet-lap-danh-muc.md)).

## 6. ID danh mục hay dùng (production)

| Danh mục | Giá trị |
|---|---|
| Kho Quảng Châu (ORIGIN, CN) | `5F8A0B1C-2D3E-4F5A-6B7C-8D9E0F1A2B3C` |
| Kho Seoul (ORIGIN, KR) | `7F0A0B1C-2D3E-4F5A-6B7C-8D9E0F1A2B3C` |
| Kho Tokyo (ORIGIN, JP) | `6F9A0B1C-2D3E-4F5A-6B7C-8D9E0F1A2B3C` |
| Kho HCM - Tân Bình (DESTINATION, VN) | `DF34760C-0C3E-48AD-BDD6-3A30A4EC402A` |
| Loại hàng | `11111111-0000-0000-0000-000000000001` Điện tử · `…0002` Quần áo · `…0003` Mỹ phẩm · `…0004` Phụ kiện · `…0005` Thực phẩm (lấy đủ qua `GET /api/product-types`) |
| Thùng gỗ | `99999999-1111-1111-1111-111111111111` SMALL · `…2222…` MEDIUM · `…3333…` LARGE · `…4444…` CUSTOM |
| Dịch vụ theo kiện | Lấy qua `GET /api/orders/consignments/item-services` (mã `SUR_INSPECTION`, `SUR_INSURANCE_3PERCENT`, `CARTON_REPACK`) |
| Tuyến / phương án | `route`: `CN-VN`, `KR-VN`, `JP-VN`; `shippingOption`: `Express`, `Standard` |

ID có thể khác nếu dựng DB mới — luôn lấy từ API danh mục khi chạy thật.

## 7. Màn hình theo role (checklist ghép)

| Role | Màn hình | API chính |
|---|---|---|
| Khách | Đăng ký / đăng nhập, tạo đơn (thùng gỗ + dịch vụ từng kiện, upload ảnh), danh sách + chi tiết đơn, báo giá, thanh toán cọc, phiếu nhập kho (PDF), giữ hàng tại kho nguồn, **theo dõi hành trình đơn** (chặng, mã vận đơn, ngày dự kiến, dòng thời gian), lịch sử đơn, thông báo; chọn giao ngay / gửi kho, xem trước + trả tất toán, đặt giao, sự cố + khiếu nại, xác nhận đã nhận | ký gửi B0–B4, B7 (phía khách), xuất kho K–L, hàng về VN B, C, D2, E, F |
| Sale | Danh sách đơn theo trạng thái, hàng đợi việc, báo giá (sửa phí theo kiện), lập phiếu nhập kho, tạo đơn hộ khách, **hàng đợi lô cần cập nhật**, dòng thời gian lô, ghi mốc hành trình kèm lời nhắn cho khách, up tờ khai nhập / biên bản sự cố; phát hành tất toán theo cân đo VN, báo phí giao lại, ghi bằng chứng giao | B1–B3, B6, xuất kho I–J, L–M, hàng về VN C, D |
| Admin | Danh mục, duyệt giá ngoại lệ, giao dịch chờ duyệt + tổng quan tiền, gán quản lý kho, ghi nhận chi bồi thường | thiết lập, B3, B5, B7, hàng về VN E |
| Quản lý kho | Phiếu chờ duyệt (`approvalStage` RECEIVE / DISCREPANCY), **phiếu xuất kho chờ duyệt**, **sự cố hàng hoá chờ quyết**, phiếu nhập kho VN + yêu cầu giao chờ duyệt, khu kho, kiện sai khu | B7, B8.3, kho A, D, xuất kho C, hàng về VN D, E |
| Nhân viên kho | Quét phiếu, check-in / kiểm đếm, danh sách chờ xếp kệ, quét mã ô xếp kệ, chuyển ô, tách/gộp, tồn kho, kiểm kê; kiện đủ điều kiện xuất, phiếu xuất kho, màn bốc hàng (quét kiện + quét ô khu xuất), lập lô và bàn giao; tiếp nhận lô ở kho VN (cân đo từng kiện), nhận hàng hoàn | B8–B9, kho B–G, xuất kho A–H, hàng về VN A, D |
