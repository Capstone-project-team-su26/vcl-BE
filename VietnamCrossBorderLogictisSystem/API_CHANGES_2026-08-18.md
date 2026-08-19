# Thay đổi API — chuẩn hoá luồng đơn ký gửi (18/08/2026)

> Doc này **thay thế phần trạng thái** của `wro_fe_api_contract.md` và `vn_arrival_fe_api_contract.md`.
> Các phần khác của hai doc đó (payload, ví dụ response) vẫn đúng.

## Vì sao đổi

Trước đây phiếu xuất kho (WRO) và lô vận chuyển (Shipment) **cùng có** các mốc `IN_TRANSIT`,
`ARRIVED_IN_VN`, `COMPLETED`, mà không chỗ nào đồng bộ hai bên. Sale bấm "Đã về VN" ở màn phiếu
thì chỉ đổi một cột trong bảng phiếu — kiện hàng và lô đứng nguyên, kho VN không có gì để đối
soát, Sale không tất toán được. Thêm nữa `ARRIVED_DESTINATION` bị chặn trên `/status` nhưng lại
nằm trong danh sách trạng thái kế tiếp mà FE hiển thị, nên bấm vào chỉ nhận 400.

Nay: **phiếu duyệt một lần rồi hết việc, mọi mốc sau đó thuộc về lô.**

---

## 1. Vòng đời mới

### Phiếu tiếp nhận kho — `WAREHOUSE_RECEIVING_NOTES` (MỚI có bước duyệt)

```
ACTIVE ──(kho check-in / confirm-receipt)──> PARTIALLY_RECEIVED ──> RECEIVED ──(OM)──> APPROVED
                                                                              ↘──────> REJECTED
```

Phiếu **tự sinh** khi đơn ký gửi thu được tiền — không ai phải bấm tạo nữa.

Sinh ở **cả 5 đường tiền vào**: webhook PayOS · webhook SePay · Sale xác nhận thanh toán ·
cọc 0đ · **Admin/Sale duyệt tay khoản chuyển khoản** (`AdminFinanceService`). Đường cuối là
chỗ dễ sót nhất vì nằm ở service khác — thiếu nó thì đơn đối soát thủ công không có phiếu.

### Phiếu xuất kho — `WRO`

```
RELEASE_PENDING ──(OM duyệt)──> RELEASE_APPROVED ──> PICKING ──(tick đủ kiện)──> PICKED  ■ hết
                ↘ RELEASE_REJECTED
```

`IN_TRANSIT` · `ARRIVED_IN_VN` · `ARRIVED_VN` · `COMPLETED` · `DELIVERED` **không còn đặt được
trên phiếu** → API trả 400 kèm câu chỉ đường sang lô.

### Lô vận chuyển — `SHIPMENTS`

```
CREATED ──(kho)──> READY_TO_SHIP ──(kho: "Đối tác đã lấy hàng")──> IN_TRANSIT
   ──(Sale: "Đã về VN, chờ chuyển về kho")──> ARRIVED_VN
   ──(Sale: "Đã về tới kho VN")──> ARRIVED_DESTINATION ──(kho VN đối soát)──> …
```

Bỏ `MANIFESTED`, `CUSTOMS_EXPORT_PENDING`, `CUSTOMS_IMPORT_PENDING`, `CUSTOMS_REJECTED`.
Lô kẹt cửa khẩu thì dùng `HOLD` / `ISSUE`. Chứng từ thông quan đính lúc OM duyệt phiếu xuất kho.

`statusTab` còn 4 giá trị: `PREPARING` · `IN_TRANSIT` · `ARRIVED` · `ISSUE` (tab `CUSTOMS` bỏ).

---

## 2. Endpoint mới

### `GET /api/warehouse-receiving-notes`

Danh sách phiếu — trước đó chỉ tra được từng phiếu theo id hoặc theo đơn, không có cách nào
dựng hàng đợi. Quyền: Admin · Sale · Staff · WarehouseStaff · OM · Manager.

Query: `status` · `warehouseId` · `search` (mã phiếu, mã đơn, tên/SĐT khách) · `pageNumber` · `pageSize`.

Trả `PaginatedResponse`, phiếu **chờ duyệt luôn xếp đầu**. Mỗi dòng có sẵn thứ màn hình cần mà
không phải gọi thêm: `statusText`, `checkedItemCount`/`declaredItemCount`, `hasDiscrepancy`,
`parcelCount`, `awaitingApproval`, `approvedByName`, `rejectionReason`.

### `GET /api/warehouse-receiving-notes/my-order/{consignmentOrderId}`

Khách tự tra phiếu tiếp nhận của **đơn mình**. Quyền: mọi user đã đăng nhập, nhưng BE kiểm
`order.CustomerId` khớp người gọi — hỏi đơn người khác trả **403**.

Trả `data: null` khi đơn chưa có phiếu (chưa thanh toán, hoặc đơn mua hộ). Ghi chú nội bộ của
kho và lý do OM từ chối bị lược khỏi response — đó là việc nội bộ.

### `PUT /api/warehouse-receiving-notes/{receivingNoteId}/status`

OM duyệt hoặc từ chối biên bản đối chiếu. Quyền: Admin / OperationsManager / Manager.

```json
{ "status": "APPROVED" }
{ "status": "REJECTED", "rejectionReason": "Thiếu 2 thùng so với khai báo" }
```

- Chỉ đi được từ `RECEIVED` hoặc `PARTIALLY_RECEIVED`; đã xử lý rồi → 400.
- `REJECTED` mà không có lý do → 400.
- Response là `WarehouseReceivingNoteDetailDto`, thêm 4 field: `approvedByName`, `approvedAt`,
  `rejectionReason`, `canPutAway`.

---

## 3. Hành vi thay đổi

| Endpoint | Trước | Nay |
|---|---|---|
| `PUT /api/international-shipments/{id}/status` | Chặn `ARRIVED_DESTINATION` | **Nhận cả 4 mốc**, kể cả `ARRIVED_DESTINATION` (chạy đúng thân xử lý của `/arrive`) |
| `PUT /api/international-shipments/{id}/arrive` | Đường duy nhất để về kho | Vẫn chạy, là alias — dùng đường nào cũng được |
| `PUT /api/parcels/{parcelId}/put-away` | Kiện `RECEIVED` xếp kệ tự do | **Đòi phiếu tiếp nhận đã `APPROVED`** (kiện `RECEIVED_AT_DESTINATION` vẫn đòi phiếu nhập kho VN như cũ) |
| `PUT /api/warehouse-release-requests/{id}/status` | Nhận 7 trạng thái | Chỉ `RELEASE_APPROVED` / `RELEASE_REJECTED` |
| `POST /api/warehouse-release-requests/{id}/notify-customer` | Đẩy phiếu sang `ARRIVED_IN_VN` | **400** — khách được báo tự động khi lô sang `ARRIVED_VN` |
| `POST /api/warehouse-release-requests/{id}/complete` | Hoàn tất xuất kho | **400** — dùng lô `IN_TRANSIT` |
| `POST /api/warehouse-release-requests/{id}/handover` | Bàn giao cho nhà xe | **400** — dùng lô `IN_TRANSIT` |
| `POST /api/international-shipments` | WRO phải `RELEASED`/`HANDED_OVER`/`IN_TRANSIT` | WRO phải `PICKED` (vẫn nhận 4 giá trị cũ cho dữ liệu đang dở) |

### Field bị bỏ khỏi response WRO

`isCustomsCleared` · `customsStatus` · `customsStatusText` — ba field này suy từ `status` chứ
không phải tin từ hải quan, và luôn hiện "Đã thông quan" ngay khi Sale bấm báo khách. FE bỏ khỏi
màn phiếu; chứng từ thật vẫn ở `customsDocumentUrls`.

### Side-effect dời chỗ

Nhả ô kệ + `Inventory → RELEASED` + nhật ký `OUTBOUND` nay xảy ra **một lần tại lô `IN_TRANSIT`**,
không còn ở bước bàn giao của từng phiếu. Kiện chuyển `PackageStatus = IN_TRANSIT`.

---

## 4. Ai bấm mốc nào

| Mốc | Vai trò |
|---|---|
| Lô `READY_TO_SHIP`, `IN_TRANSIT` | WarehouseStaff · Staff · OM · Admin |
| Lô `ARRIVED_VN`, `ARRIVED_DESTINATION` | Sale · Staff · OM · Admin |
| Lô `HOLD`, `ISSUE` | Sale · Staff · OM · Admin |
| Phiếu tiếp nhận `APPROVED`/`REJECTED` | OM · Admin · Manager |
| Phiếu xuất kho `RELEASE_APPROVED` | như cũ |

---

## 5. Migration

`20260818153205_AddReceivingNoteApproval` — thêm `approved_by`, `approved_at`,
`rejection_reason` vào `WAREHOUSE_RECEIVING_NOTES`, kèm backfill
`status = 'RECEIVED' → 'APPROVED'` để hàng đang chờ xếp kệ không bị chốt mới chặn lại.

---

## 6. Việc của FE ở đợt UI

1. Màn Vận chuyển của OM: dropdown trạng thái đọc theo bảng mới, bỏ tab `CUSTOMS`; nút
   "Đã đến kho đích" giờ bấm được (gọi thẳng `/status`).
2. Màn kho: thêm nút `READY_TO_SHIP` và "Đối tác đã lấy hàng" trên lô.
3. Màn WRO của Sale: bỏ mục "Đã về VN (Báo khách & Chuyển thông quan)" và cột trạng thái thông quan.
4. Màn OM: thêm hàng đợi duyệt phiếu tiếp nhận kho gốc.
5. Màn kho gốc: nút xếp kệ chỉ bật khi `canPutAway = true`.
