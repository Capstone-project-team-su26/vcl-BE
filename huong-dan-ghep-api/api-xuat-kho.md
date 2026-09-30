# Ghép API — Xuất kho quốc tế

Tài liệu cho FE ghép luồng đưa hàng rời kho nguồn (Quảng Châu / Seoul / Tokyo) lên chuyến về Việt Nam.
Nối tiếp [`api-kho-xep-ke-theo-khu.md`](api-kho-xep-ke-theo-khu.md) (từ lúc kiện đã `STORED` ở ô kệ khu lưu kho).

- Base URL: `https://vcl.henrytech.cloud`
- Swagger: `https://vcl.henrytech.cloud/swagger/index.html`
- Mọi API gửi `Authorization: Bearer <token>`. Lỗi trả `{ "message": "..." }` — hiển thị nguyên `message`.
- Tài khoản demo, đơn demo, quy ước response: [`README.md`](README.md).

**Luồng một dòng:** lập phiếu → quản lý kho duyệt → bốc hàng sang khu xuất → lập lô → bàn giao → Sale ghi hành trình về VN → khách theo dõi và nhận thông báo từng mốc.

| Bước | Ai làm |
|---|---|
| Xem kiện đủ điều kiện, lập / sửa / gửi duyệt / rút / huỷ phiếu | `WarehouseStaff` đúng vùng kho nguồn (hoặc `Admin`) |
| Duyệt / từ chối phiếu | Quản lý kho nguồn; kho chưa gán quản lý → `OperationsManager`; `Admin` làm thay **phải ghi lý do** |
| Bốc hàng, bỏ kiện, tách phiếu, lập lô, gỡ phiếu, huỷ lô, bàn giao | `WarehouseStaff` đúng vùng (hoặc `Admin`) |
| Ghi mốc hành trình sau bàn giao, up tờ khai nhập / biên bản sự cố | `Sale`, `OperationsManager`, `Admin` |
| Bật/tắt giữ hàng | `Customer` (đơn của mình), nhân viên nội bộ |
| Theo dõi tiến độ đơn, nhận thông báo từng mốc | `Customer` (đơn của mình) — mục L |
| Hàng đợi lô cần cập nhật, dòng thời gian lô | `Sale` — mục M |

`OperationsManager` **không** thao tác kho (không bốc hàng, không lập lô, không bàn giao) — người duyệt tách khỏi người làm.

---

## Trạng thái

### Phiếu xuất kho (WRO) — `status` / `statusText`

```
DRAFT ─submit─► PENDING_APPROVAL ─decide APPROVED─► APPROVED ─pick kiện đầu─► PICKING ─pick đủ─► READY
  │  ▲                │   │                                                                        │ vào lô
  │  └──withdraw──────┘   └─decide REJECTED─► REJECTED                                             ▼
  └─cancel─► CANCELLED                                                    IN_SHIPMENT ─bàn giao─► HANDED_OVER
                                                                               └─gỡ khỏi lô─► READY
```

| `status` | `statusText` (server trả sẵn) |
|---|---|
| `DRAFT` | Nháp |
| `PENDING_APPROVAL` | Chờ quản lý kho duyệt |
| `APPROVED` | Đã duyệt, chờ bốc hàng |
| `PICKING` | Đang bốc hàng sang khu xuất |
| `READY` | Đã bốc xong, chờ vào lô |
| `IN_SHIPMENT` | Đã vào lô vận chuyển |
| `HANDED_OVER` | Đã bàn giao cho hãng vận chuyển |
| `REJECTED` | Bị từ chối |
| `CANCELLED` | Đã huỷ |

Bỏ hết kiện khỏi phiếu → phiếu tự về `CANCELLED`.

### Kiện trong phiếu — `parcels[].status`

| Giá trị | Nghĩa | Kiện đang nằm đâu |
|---|---|---|
| `RESERVED` | Đã khoá hàng, chờ bốc | Ô kệ khu lưu kho (`sourceBinCode`) |
| `PICKED` | Đã bốc sang khu xuất | Ô khu xuất (`stagingBinCode`) |
| `HANDED_OVER` | Đã bàn giao, rời kho | Không còn ô (`binId = null`) |
| `REMOVED` | Bị bỏ khỏi phiếu | Đã trả về ô khu lưu kho |

Tồn kho đi kèm: `AVAILABLE` → `RESERVED` (gửi duyệt) → `PICKED` (bốc) → `RELEASED` (bàn giao).
`PARCELS.packageStatus` vẫn là `STORED` tới lúc bàn giao (rồi thành `IN_TRANSIT`) — màn kho, sức chứa ô, kiểm kê không đổi cách hiểu.

### Lô vận chuyển — `status`

```
DRAFT ─handover─► HANDED_OVER ─► DEPARTED ─► IN_TRANSIT ⇄ DELAYED / ON_HOLD ─► CUSTOMS_CLEARED ─► ARRIVED_VN ─► ARRIVED_DESTINATION
  └─cancel─► CANCELLED
```

| `status` | Nghĩa |
|---|---|
| `DRAFT` | Lô nháp, còn thêm/gỡ phiếu được |
| `HANDED_OVER` | Đã bàn giao cho hãng, hàng rời kho |
| `DEPARTED` | Đã rời kho nguồn |
| `IN_TRANSIT` | Đang trên đường |
| `DELAYED` | Trễ lịch |
| `ON_HOLD` | Bị giữ / có sự cố |
| `CUSTOMS_CLEARED` | Đã thông quan nhập |
| `ARRIVED_VN` | Đã về Việt Nam |
| `ARRIVED_DESTINATION` | Đã tới kho đích → luồng hàng về Việt Nam ([`api-hang-ve-viet-nam.md`](api-hang-ve-viet-nam.md)) |
| `CANCELLED` | Lô nháp đã huỷ |

`GET /api/international-shipments` trả thêm `statusTab` để FE chia tab: `PREPARING` (DRAFT) · `IN_TRANSIT` (HANDED_OVER, DEPARTED, IN_TRANSIT, CUSTOMS_CLEARED) · `ARRIVED` (ARRIVED_VN, ARRIVED_DESTINATION) · `ISSUE` (DELAYED, ON_HOLD) · `CANCELLED`.

### Kiện không xuất được — 10 mã lý do

Xem bảng đầy đủ ở mục **A**.

---

## Chuẩn bị dữ liệu trước khi ghép

| Cần | Lấy ở đâu |
|---|---|
| `shippingRouteId` (tuyến) | `GET /api/shipping-routes?isActive=true` → `{ message, data: [ { id, routeCode, routeName, originWarehouseId, destinationWarehouseId, transportMode, isActive, ... } ] }`. Tuyến **phải có cả kho nguồn và kho đích**, nếu không API lập phiếu trả 400. Kho nguồn + kho đích của phiếu lấy tự động từ tuyến, FE không gửi |
| Ô ở **khu xuất** để bốc hàng vào | `GET /api/warehouses/{warehouseId}/locations/active` → lọc `zoneType = "OUTBOUND"`. Kho Quảng Châu / Seoul / Tokyo đều đã có `BIN-OUT-01`, `BIN-OUT-02` |
| `carrierId` (hãng vận chuyển, dùng lúc bàn giao) | `GET /api/carriers/active` |

---

## A. Kiện đủ điều kiện xuất

### `GET /api/warehouse-release-requests/eligible-parcels?shippingRouteId=&search=&includeBlocked=false`

| Query | Ghi chú |
|---|---|
| `shippingRouteId` | **Bắt buộc**. Server lấy kho nguồn từ tuyến rồi xét toàn bộ kiện đang nằm ở kho đó |
| `search` | Lọc theo mã kiện, mã đơn, tên khách, tên hàng, mã ô |
| `includeBlocked` | `false` (mặc định): chỉ kiện đủ điều kiện. `true`: trả cả kiện vướng kèm `reasons` — màn kho nên bật `true` để nhân viên biết vướng gì |

Response `{ "message", "data": [ ... ] }`:

```json
{
  "message": "Lấy danh sách kiện đủ điều kiện xuất thành công.",
  "data": [
    {
      "parcelId": "guid",
      "packageCode": "PCL-...",
      "orderId": "guid",
      "consignmentCode": "VCL-...",
      "customerName": "Nguyễn Văn A",
      "productName": "Áo vest",
      "weight": 1.2, "length": 40, "width": 30, "height": 20,
      "binCode": "BIN-A1-01", "shelfCode": "SHELF-A1", "zoneName": "Khu A",
      "isEligible": true,
      "reasons": []
    },
    {
      "parcelId": "guid",
      "packageCode": "PCL-...",
      "isEligible": false,
      "reasons": [ { "code": "EXPORT_HOLD", "message": "Đơn đang bật giữ hàng tại kho nguồn." } ]
    }
  ]
}
```

Danh sách đã sắp: kiện đủ điều kiện lên trước, rồi theo `packageCode`.

### 10 mã lý do (`reasons[].code`)

| Mã | `message` server trả | FE gợi ý xử lý |
|---|---|---|
| `NOT_STORED` | Kiện chưa ở trạng thái đã lưu kho. | Kiện chưa xếp kệ — xem màn chờ xếp kệ |
| `NOT_IN_STORAGE_ZONE` | Kiện không nằm ở ô kệ thuộc khu lưu kho đang dùng. | Chuyển về khu `STORAGE` (báo cáo kiện sai khu) |
| `WRONG_WAREHOUSE` | Kiện không nằm ở kho nguồn của tuyến. | Chọn đúng tuyến |
| `ORDER_CLOSED` | Đơn đã đóng (huỷ / từ chối / hoàn tất). | Không xuất được |
| `DEPOSIT_NOT_CONFIRMED` | Đơn chưa có khoản cọc được xác nhận. | Báo Sale thu cọc. Đơn mua hộ (`orderType = PURCHASE`) được bỏ qua điều kiện này |
| `RECEIVING_NOT_SETTLED` | Phiếu nhập kho của đơn chưa được quản lý kho duyệt xong. | Chờ quản lý kho duyệt biên bản lệch |
| `EXPORT_HOLD` | Đơn đang bật giữ hàng tại kho nguồn. | Khách/Sale tắt giữ hàng (mục K) |
| `RESTRICTED_NO_PERMIT` | Đơn có hàng hạn chế nhưng chưa đính kèm giấy phép. | Up `PERMIT` vào `entityType=ORDER` (mục J) |
| `ALREADY_IN_EXPORT` | Kiện đã nằm trong một phiếu xuất khác. | Mở phiếu kia ra xử lý |
| `INVENTORY_NOT_AVAILABLE` | Kiện không có dòng tồn kho sẵn sàng (đang giữ hoặc sai sổ). | Kiện đang bị khoá cho phiếu khác, hoặc tồn kho sai sổ |

Một kiện có thể vướng nhiều lý do cùng lúc — `reasons` là mảng, hiện hết.

**Thử:** `demo.kho.cn` — lấy `shippingRouteId` của tuyến có `originWarehouseId = 5F8A0B1C-2D3E-4F5A-6B7C-8D9E0F1A2B3C` (Kho Quảng Châu), rồi gọi với `includeBlocked=true`. Kiện của "Đơn 13" (đang `STORED` ở `BIN-A1-01`) phải có `isEligible = true`.

---

## B. Lập / sửa / gửi duyệt / rút / huỷ phiếu

| API | Role | Ghi chú |
|---|---|---|
| `POST /api/warehouse-release-requests` | WarehouseStaff, Admin | **201** — tạo phiếu `DRAFT` |
| `PUT /api/warehouse-release-requests/{id}` | Người lập, Admin | Chỉ khi `DRAFT`. Gửi lại **toàn bộ** danh sách kiện |
| `POST /api/warehouse-release-requests/{id}/submit` | Người lập, Admin | `DRAFT` → `PENDING_APPROVAL`, **khoá hàng** |
| `POST /api/warehouse-release-requests/{id}/withdraw` | Người lập, Admin | `PENDING_APPROVAL` → `DRAFT`, nhả hàng |
| `POST /api/warehouse-release-requests/{id}/cancel` | Người lập, Admin | Chỉ khi `DRAFT` → `CANCELLED` |
| `GET /api/warehouse-release-requests?status=&warehouseId=&search=&pageNumber=1&pageSize=20` | Nhân viên nội bộ (khách → 403) | Danh sách phân trang |
| `GET /api/warehouse-release-requests/{id}` | Nhân viên nội bộ (khách → 403) | Chi tiết: kiện + vị trí + giấy tờ |

### B1. Lập phiếu — `POST /api/warehouse-release-requests`

```json
{ "shippingRouteId": "guid", "parcelIds": ["guid", "guid"], "note": "Chuyến thứ 5 tháng 9" }
```

- `parcelIds` ít nhất 1 kiện; **mọi kiện phải đủ điều kiện** (mục A), nếu không → 400 kèm danh sách từng kiện vướng gì.
- Không gửi kho nguồn/kho đích — server lấy từ tuyến. Tuyến `isActive = false` → 400.
- Chưa khoá hàng ở bước này; kiện chỉ bị "giữ chỗ" (phiếu khác lập sẽ báo `ALREADY_IN_EXPORT`).

Response **201** `{ "message", "data": <chi tiết phiếu như B3> }`.

### B2. Sửa phiếu nháp — `PUT /api/warehouse-release-requests/{id}`

Cùng body với B1. Danh sách gửi lên là danh sách cuối cùng: kiện bị bỏ ra khỏi phiếu nháp **xoá hẳn dòng**, không ghi là "bỏ kiện" (bỏ kiện là sự cố kho, xem mục E).

### B3. Chi tiết phiếu — `GET /api/warehouse-release-requests/{id}`

```json
{
  "message": "Lấy chi tiết phiếu xuất kho thành công.",
  "data": {
    "id": "guid",
    "wroCode": "WRO-20260918-0001",
    "status": "PICKING",
    "statusText": "Đang bốc hàng sang khu xuất",
    "originWarehouseId": "guid", "originWarehouseName": "Kho Quảng Châu",
    "destinationWarehouseId": "guid", "destinationWarehouseName": "Kho HCM - Tân Bình",
    "shippingRouteId": "guid", "shippingRouteCode": "CN_VN_AIR",
    "parcelCount": 3, "pickedCount": 1, "totalWeight": 12.5,
    "createdAt": "2026-09-18T02:10:00Z", "createdByName": "NV Kho Quảng Châu",
    "submittedAt": "2026-09-18T02:20:00Z",
    "decidedAt": "2026-09-18T02:30:00Z", "decidedByName": "Quản lý kho", "decisionNote": "Đủ điều kiện",
    "shipmentId": null, "shipmentCode": null,
    "isLegacy": false,
    "note": "Chuyến thứ 5 tháng 9",
    "splitFromId": null, "splitFromCode": null,
    "parcels": [
      {
        "id": "guid-DÒNG-KIỆN",
        "parcelId": "guid",
        "packageCode": "PCL-...",
        "status": "PICKED",
        "orderId": "guid", "consignmentCode": "VCL-...", "customerName": "Nguyễn Văn A",
        "productName": "Áo vest",
        "weight": 1.2, "length": 40, "width": 30, "height": 20,
        "sourceBinCode": "BIN-A1-01",
        "stagingBinCode": "BIN-OUT-01",
        "pickedAt": "2026-09-18T02:40:00Z", "pickedByName": "NV Kho Quảng Châu",
        "removedAt": null, "removedReason": null
      }
    ],
    "attachments": []
  }
}
```

> **`parcels[].id` ≠ `parcels[].parcelId`.** `id` là **id dòng kiện trong phiếu** (dùng làm `entityId` khi đính kèm ảnh sự cố, mục E); `parcelId` là id kiện hàng (dùng trong URL `pick` / `remove` / `split`).

`parcels` ở chi tiết trả **cả dòng `REMOVED`** (để xem lịch sử); `parcelCount` / `pickedCount` chỉ đếm dòng còn hiệu lực.
`attachments` chỉ trả cho nhân viên nội bộ (khách gọi thì mảng rỗng).

### B4. Danh sách phiếu — `GET /api/warehouse-release-requests`

| Query | Ghi chú |
|---|---|
| `status` | Một hoặc nhiều, **ngăn bằng dấu phẩy**: `status=APPROVED,PICKING` |
| `warehouseId` | Lọc theo kho nguồn |
| `search` | Mã phiếu hoặc mã kiện trong phiếu |
| `pageNumber`, `pageSize` | Mặc định 1 / 20, tối đa 100 |

Response `{ "message", "data": { "items": [ <như B3 nhưng không có parcels/attachments/note> ], "totalCount", "pageNumber", "pageSize", "totalPages" } }`, sắp xếp mới nhất trước.

### B5. Gửi duyệt — `POST /{id}/submit`

Không có body. Server **xét lại toàn bộ điều kiện mục A ngay trước khi khoá hàng** (từ lúc lập tới giờ đơn có thể bị huỷ, bị giữ hàng, kiện bị chuyển chỗ) → nếu vướng trả 400 kèm tên kiện. Thành công: mỗi kiện có một dòng tồn `AVAILABLE` → `RESERVED`, gửi thông báo cho quản lý kho nguồn (chưa gán quản lý thì báo cho `OperationsManager` / `Admin`).

### B6. Rút / huỷ

- `POST /{id}/withdraw`: chỉ khi `PENDING_APPROVAL`; tồn `RESERVED` → `AVAILABLE`. Phiếu về `DRAFT` nhưng **vẫn giữ chỗ kiện** (phiếu khác vẫn thấy `ALREADY_IN_EXPORT`).
- `POST /{id}/cancel`: chỉ khi `DRAFT`; xoá các dòng kiện, phiếu → `CANCELLED`.

**Thử:** `demo.kho.cn` — lập phiếu với kiện của "Đơn 13" → `submit` → gọi lại `eligible-parcels`, kiện đó phải có thêm lý do `INVENTORY_NOT_AVAILABLE` (đã khoá hàng) → `withdraw` → lý do đó biến mất.

---

## C. Duyệt / từ chối phiếu

### `POST /api/warehouse-release-requests/{id}/decide`

```json
{ "decision": "APPROVED", "reason": "Đủ điều kiện" }
```

```json
{ "decision": "REJECTED", "reason": "Hàng chưa đủ giấy phép" }
```

| Trường | Ghi chú |
|---|---|
| `decision` | **Bắt buộc**, chỉ nhận `APPROVED` hoặc `REJECTED` (khác → 400) |
| `reason` | Bắt buộc khi `REJECTED`; bắt buộc khi **Admin duyệt thay** quản lý kho; tuỳ chọn khi quản lý kho duyệt. Lưu vào `decisionNote` |

Luật người duyệt:
- Phiếu phải đang `PENDING_APPROVAL`.
- **Người gửi duyệt không tự duyệt được** → 403 "Người lập phiếu xuất kho không được tự duyệt...".
- Kho đã gán quản lý (`WAREHOUSES.manager_id`) → chỉ người đó duyệt (hoặc Admin + lý do) → người khác 403.
- Kho chưa gán quản lý → `OperationsManager` / `Admin` duyệt.

Duyệt (`APPROVED`): server xét lại điều kiện lần cuối (bỏ qua `INVENTORY_NOT_AVAILABLE` và `ALREADY_IN_EXPORT` vì chính phiếu này đang giữ hàng) → phiếu `APPROVED`, phiếu xuất kho PDF sẵn sàng tải (mục G), báo người lập.
Từ chối (`REJECTED`): nhả hàng `RESERVED` → `AVAILABLE`, phiếu `REJECTED`, báo người lập.

**Thử:** `demo.quanlykho` (kho Quảng Châu chưa gán quản lý riêng) duyệt phiếu do `demo.kho.cn` gửi. Thử gọi bằng chính `demo.kho.cn` → 403.

---

## D. Bốc hàng sang khu xuất (pick)

### `POST /api/warehouse-release-requests/{id}/pick`

Quét **mã kiện** rồi quét **mã ô ở khu xuất** — mỗi lần gọi là một kiện.

```json
{ "packageCode": "PCL-20260918-0001", "stagingBinCode": "BIN-OUT-01" }
```

| Trường | Ghi chú |
|---|---|
| `packageCode` | Mã kiện quét được, không phân biệt hoa thường |
| `parcelId` | Dùng thay `packageCode` khi chọn từ danh sách. Gửi cả hai thì `parcelId` được ưu tiên |
| `stagingBinCode` | Mã ô quét được, tra trong **kho nguồn của phiếu** |
| `stagingBinId` | Dùng khi không quét mã. Gửi kèm `stagingBinCode` thì phải cùng một ô |

Server kiểm theo thứ tự:
1. Phiếu đang `APPROVED` hoặc `PICKING` (khác → 400 kèm trạng thái hiện tại).
2. Tài khoản đúng vùng kho nguồn.
3. Kiện có trong phiếu (quét kiện lạ → 400 "Kiện ... không nằm trong phiếu ..."), dòng kiện đang `RESERVED` (bốc lại → 400).
4. Ô thuộc kho nguồn, ô đang dùng, khu đang dùng, **khu phải là `OUTBOUND`**, còn đủ thể tích/tải trọng.

Thành công: `PARCELS.bin_id` chuyển sang ô khu xuất, ô kệ lưu kho cũ được trả chỗ, tồn `RESERVED` → `PICKED` (đổi luôn `binId`), ghi nhật ký `RELOCATION` với lý do "Bốc hàng sang khu xuất theo phiếu WRO-...". Dòng kiện ghi `sourceBinCode` (ô cũ) và `stagingBinCode` (ô khu xuất).

Phiếu chuyển `PICKING`; khi **không còn dòng nào `RESERVED`** thì tự chuyển `READY` và báo Sale "phiếu sẵn sàng đi".

Response 200 `{ "message": "Đã bốc kiện sang khu xuất.", "data": <chi tiết phiếu như B3> }` — FE đọc `data.status` để biết đã `READY` chưa.

Ghi chú quan trọng cho các màn kho khác:
- Kiện đang ở khu xuất **theo phiếu** thì **không** hiện trong `GET /api/warehouses/{id}/misplaced-parcels`. Kiện nằm khu xuất mà không thuộc phiếu nào thì vẫn bị báo sai khu.
- Kiện đang trong phiếu xuất còn hiệu lực **không tách / không gộp / không chuyển ô** bằng API điều chuyển → 400 "Không ... được: kiện đang nằm trong phiếu xuất kho — PCL-... (phiếu WRO-...)". Muốn đổi thì bỏ kiện khỏi phiếu trước (mục E).

**Thử:** `demo.kho.cn` — `stagingBinCode: "BIN-A1-02"` (ô khu lưu kho) → 400 có chữ `OUTBOUND`; `stagingBinCode: "BIN-OUT-01"` → 200, kiện `PICKED`.

---

## E. Bỏ kiện khỏi phiếu (thiếu hàng / hàng hỏng)

### `POST /api/warehouse-release-requests/{id}/parcels/{parcelId}/remove`

**Bắt buộc đính kèm ảnh/biên bản trước khi gọi.** Thứ tự đúng:

1. Lấy chi tiết phiếu, tìm dòng kiện → lấy **`parcels[].id`** (id **dòng kiện trong phiếu**, KHÔNG phải `parcelId`).
2. `POST /api/attachments` (multipart) với `entityType = WRO_PARCEL`, `entityId = <parcels[].id>`, `documentType = PICKING_ISSUE`.
3. Gọi API bỏ kiện, `{parcelId}` trên URL là **id kiện hàng**.

```json
{ "reason": "Kiện bị rách, không đi chuyến này", "returnBinCode": "BIN-A1-01", "attachmentIds": [] }
```

| Trường | Ghi chú |
|---|---|
| `reason` | **Bắt buộc**, thiếu → 400 "Bỏ kiện khỏi phiếu phải ghi lý do." |
| `returnBinCode` | **Bắt buộc khi kiện đã `PICKED`** — quét ô **khu lưu kho** để trả hàng về kệ. Kiện còn `RESERVED` thì bỏ trống (hàng vẫn ở chỗ cũ) |
| `returnBinId` | Thay cho `returnBinCode` khi chọn từ danh sách |
| `attachmentIds` | Tuỳ chọn, để tham chiếu; điều kiện thật sự là **đã có ít nhất một file `PICKING_ISSUE`** gắn vào dòng kiện |

Thiếu ảnh → 400 kèm nguyên văn hướng dẫn: `Phải đính kèm ảnh/biên bản (documentType = PICKING_ISSUE, entityType = WRO_PARCEL, entityId = <id dòng kiện>) trước khi bỏ kiện khỏi phiếu.`
Kiện đã bốc mà thiếu `returnBinCode` → 400 "Kiện đã bốc sang khu xuất: phải quét ô kệ khu lưu kho (returnBinCode) để trả hàng về."

Không bỏ kiện được khi phiếu đang `IN_SHIPMENT`, `HANDED_OVER`, `CANCELLED`, `REJECTED`.

Sau khi bỏ: dòng kiện `REMOVED` (ghi `removedAt`, `removedReason`), tồn về `AVAILABLE`, kiện đã bốc thì nằm lại ô khu lưu kho vừa quét (ghi nhật ký `RELOCATION`), báo Sale của đơn + quản lý kho. Phiếu tự tính lại: hết kiện → `CANCELLED`; còn kiện và tất cả đã bốc → `READY`; còn kiện chưa bốc → `PICKING`.

**Thử:** `demo.kho.cn` — gọi remove khi chưa up ảnh (400, message có `PICKING_ISSUE`) → up file PDF/JPG với `entityType=WRO_PARCEL` → gọi lại (200).

---

## F. Tách phiếu (container đầy)

### `POST /api/warehouse-release-requests/{id}/split`

```json
{ "parcelIds": ["guid", "guid"] }
```

- Các kiện chọn chuyển sang **phiếu con mới**, giữ nguyên trạng thái, người lập, người duyệt, tuyến, kho — **vẫn khoá hàng, không phải duyệt lại**. Phiếu con có `splitFromId` / `splitFromCode` trỏ về phiếu gốc.
- Không tách được khi phiếu đang `IN_SHIPMENT`, `HANDED_OVER`, `CANCELLED`, `REJECTED`.
- **Không tách hết kiện** — phiếu gốc phải còn ít nhất một kiện → 400.
- Kiện không thuộc phiếu thì bị bỏ qua; không kiện nào thuộc phiếu → 400.
- Sau khi tách, mỗi phiếu tự tính lại trạng thái theo kiện của mình (`APPROVED` / `PICKING` / `READY`).

Response 200 `{ "message": "Đã tách phiếu xuất kho.", "data": <chi tiết **phiếu con**> }` — FE điều hướng sang phiếu con.

---

## G. PDF của phiếu

| API | Role | Trả về |
|---|---|---|
| `GET /api/warehouse-release-requests/{id}/release-note` | Nhân viên nội bộ (khách → 403) | File `application/pdf` — phiếu xuất kho (sinh khi duyệt) |
| `GET /api/warehouse-release-requests/{id}/picking-sheet` | Nhân viên nội bộ (khách → 403) | File `application/pdf` — danh sách soạn hàng để kho cầm đi bốc |

Hai API này trả **file nhị phân**, không bọc `{ message, data }`. FE gọi kèm `Authorization` rồi mở blob (không dùng link trần). Không tìm thấy phiếu → 404 `{ "message": "..." }`.

---

## H. Lô vận chuyển

| API | Role | Ghi chú |
|---|---|---|
| `GET /api/international-shipments/ready-wro-requests?originWarehouseId=&search=` | Nhân viên nội bộ (khách → 403) | Phiếu `READY` chờ gom lô |
| `POST /api/international-shipments` | WarehouseStaff, Admin | **201** — lô `DRAFT` |
| `POST /api/international-shipments/{id}/wro-requests` | WarehouseStaff, Admin | Thêm phiếu vào lô nháp |
| `DELETE /api/international-shipments/{id}/wro-requests/{wroRequestId}` | WarehouseStaff, Admin | Gỡ phiếu khỏi lô nháp |
| `POST /api/international-shipments/{id}/cancel` | WarehouseStaff, Admin | Huỷ lô nháp |
| `POST /api/international-shipments/{id}/handover` | WarehouseStaff, Admin | Bàn giao cho hãng |
| `GET /api/international-shipments/{id}/manifest` | Nhân viên nội bộ (khách → 403) | Manifest PDF |
| `GET /api/international-shipments?pageNumber=&pageSize=&statusTab=&status=&originWarehouseId=&search=` | Nhân viên nội bộ (khách → 403) | Danh sách lô |
| `GET /api/international-shipments/{id}` | Nhân viên nội bộ (khách → 403) | Chi tiết lô |
| `GET /api/international-shipments/tracking-queue?originWarehouseId=&attentionOnly=` | Nhân viên nội bộ (khách → 403) | Hàng đợi theo dõi lô của Sale (mục M) |
| `GET /api/international-shipments/{id}/timeline` | Nhân viên nội bộ (khách → 403) | Dòng thời gian lô cho Sale (mục M) |

### H1. Phiếu chờ gom lô — `GET /api/international-shipments/ready-wro-requests`

```json
{
  "message": "Lấy danh sách phiếu xuất kho chờ gom lô thành công.",
  "data": [
    {
      "wroRequestId": "guid",
      "wroCode": "WRO-20260918-0001",
      "status": "READY",
      "originWarehouseId": "guid", "originWarehouseName": "Kho Quảng Châu",
      "destinationWarehouseId": "guid", "destinationWarehouseName": "Kho HCM - Tân Bình",
      "shippingRouteId": "guid", "shippingRouteCode": "CN_VN_AIR",
      "parcelCount": 2, "totalWeight": 8.4,
      "decidedAt": "2026-09-18T02:30:00Z",
      "isLegacy": false
    }
  ]
}
```

Chỉ trả phiếu đang `READY`. FE nhóm theo `originWarehouseId` + `destinationWarehouseId` vì một lô không trộn hai cặp kho.

### H2. Lập lô — `POST /api/international-shipments`

```json
{ "wroRequestIds": ["guid", "guid"], "shippingMethod": "AIR", "carrierId": null, "note": "" }
```

- `wroRequestIds` ít nhất 1; mọi phiếu phải đang `READY` (khác → 400 kèm trạng thái hiện tại) và **cùng kho nguồn + cùng kho đích** (khác → 400).
- `shippingMethod`, `carrierId`, `note` tuỳ chọn — hãng vận chuyển thật sự chốt ở bước bàn giao.
- Người lập phải đúng vùng kho nguồn.

Response **201** `{ "message", "data": <chi tiết lô> }`:

```json
{
  "shipmentId": "guid",
  "shipmentCode": "SHIP-20260918-001",
  "status": "DRAFT",
  "statusTab": "PREPARING",
  "originWarehouseId": "guid", "originWarehouseName": "Kho Quảng Châu",
  "destinationWarehouseId": "guid", "destinationWarehouseName": "Kho HCM - Tân Bình",
  "carrierId": null, "carrierName": null,
  "shippingMethod": "AIR",
  "shippingRouteId": "guid", "shippingRouteName": "...",
  "totalPackages": 0, "totalWeight": 0,
  "carrierTrackingCode": null,
  "shippedAt": null, "deliveredAt": null,
  "pdfUrls": [], "exportImageUrls": [], "customsDocUrls": [],
  "wroRequests": [ { "wroRequestId": "guid", "wroCode": "WRO-20260918-0001" } ],
  "parcels": [ { "parcelId": "guid", "packageCode": "PCL-...", "weight": 1.2, "orderId": "guid", "orderCode": "VCL-...", "customerId": "guid", "customerName": "...", "packageStatus": "STORED", ... } ]
}
```

> Khoá chính của lô trả về là **`shipmentId`** (không phải `id`).

Phiếu vào lô chuyển `READY` → `IN_SHIPMENT`.

### H3. Thêm / gỡ phiếu, huỷ lô

- `POST /{id}/wro-requests` `{ "wroRequestIds": ["guid"] }` — chỉ khi lô `DRAFT`; phiếu phải `READY` và **cùng kho nguồn/kho đích với lô** (khác → 400 "Phiếu ... khác kho nguồn/kho đích của lô ...").
- `DELETE /{id}/wro-requests/{wroRequestId}` — chỉ khi lô `DRAFT`; phiếu về `READY`, hàng vẫn nằm ở khu xuất. Phiếu không nằm trong lô → 404.
- `POST /{id}/cancel` — chỉ khi lô `DRAFT`; mọi phiếu về `READY`, lô → `CANCELLED`.

Cả ba trả `{ "message", "data": <chi tiết lô> }`.

### H4. Bàn giao — `POST /api/international-shipments/{id}/handover`

**Trước khi gọi phải đính kèm giấy tờ vào lô** (mục J): `HANDOVER_RECORD` + `WAYBILL`, và `CUSTOMS_EXPORT` nếu tuyến bật cờ yêu cầu tờ khai xuất.

```json
{
  "carrierId": "guid",
  "recipientName": "Anh Kim",
  "recipientPhone": "010-1234-5678",
  "waybillCode": "WB-20260918-01",
  "vehicleNumber": "51C-123.45",
  "sealNumber": "SEAL-001",
  "handoverLocation": "Kho Quảng Châu - cổng B",
  "note": ""
}
```

| Trường | Bắt buộc |
|---|---|
| `carrierId` | **Có** — không tìm thấy hãng → 404 |
| `recipientName` | **Có** — thiếu → 400 "Phải ghi tên người nhận bên hãng vận chuyển." |
| `waybillCode` | **Có** — thiếu → 400 "Phải nhập mã vận đơn." |
| `recipientPhone`, `vehicleNumber`, `sealNumber`, `handoverLocation`, `note` | Không |

Server kiểm trước khi trừ tồn:
1. Lô đang `DRAFT` và có ít nhất một phiếu.
2. Mọi phiếu trong lô đang `IN_SHIPMENT`.
3. Mọi kiện còn lại của từng phiếu đã `PICKED` → còn kiện chưa bốc thì 400 kèm **danh sách mã kiện**.
4. Mọi đơn trong lô **còn cọc đã xác nhận và không bật giữ hàng** → vướng thì 400 kèm danh sách `VCL-...: đang bật giữ hàng` / `VCL-...: không còn cọc đã xác nhận`, kho gỡ phiếu chứa kiện của đơn đó ra khỏi lô. (Đơn mua hộ không xét cọc.)
5. Đủ giấy tờ bàn giao → thiếu thì 400 `Thiếu giấy tờ bàn giao (HANDOVER_RECORD, WAYBILL). Đính kèm vào lô qua POST /api/attachments với entityType = SHIPMENT, entityId = <id lô>.`

Thành công (một transaction): mỗi kiện — tồn `PICKED` → `RELEASED` + nhật ký `OUTBOUND`, `binId = null`, `packageStatus = IN_TRANSIT`, dòng kiện → `HANDED_OVER`; ô khu xuất được trả chỗ; phiếu → `HANDED_OVER` (ghi `carrierId`, mã vận đơn, thời điểm bàn giao); lô → `HANDED_OVER` với `shippedAt`, `totalPackages`, `totalWeight`; lưu biên bản bàn giao; ghi mốc `HANDED_OVER`; báo khách từng đơn "hàng đã xuất kho".

Dòng tồn khác của cùng kiện (dữ liệu cũ sai sổ) **không bị đụng tới** — kho tự kiểm ở màn tồn kho.

### H5. Manifest — `GET /api/international-shipments/{id}/manifest`

Trả file `application/pdf` (không bọc JSON). Sinh được ngay từ lúc lô còn nháp.

**Thử:** `demo.kho.cn` — sau khi phiếu `READY`: `ready-wro-requests?originWarehouseId=5F8A0B1C-2D3E-4F5A-6B7C-8D9E0F1A2B3C` → `POST /api/international-shipments` → gọi handover khi chưa up giấy tờ (400, message có `HANDOVER_RECORD`) → up `HANDOVER_RECORD` + `WAYBILL` → handover lại (200). `carrierId` lấy từ `GET /api/carriers/active`.

---

## I. Hành trình sau bàn giao

### `PUT /api/international-shipments/{id}/status`

```json
{
  "status": "DEPARTED",
  "note": "Đã gọi hãng xác nhận chuyến bay",
  "customerMessage": "Hàng đã lên chuyến bay KE-123, dự kiến hạ cánh sáng mai.",
  "location": "Sân bay Incheon",
  "carrierTrackingCode": "KE-123-998877"
}
```

| Trường | Ghi chú |
|---|---|
| `status` | **Bắt buộc** — mốc muốn ghi (bảng dưới) |
| `note` | Ghi chú **nội bộ** — khách không bao giờ thấy. Bắt buộc ở `DELAYED`, `ON_HOLD` |
| `customerMessage` | Lời nhắn **hiện cho khách** (trong thông báo và màn theo dõi đơn). Bỏ trống thì khách nhận câu mặc định của mốc |
| `location` | Hàng đang ở đâu (cảng, sân bay, cửa khẩu) — khách thấy |
| `carrierTrackingCode` | Mã tra cứu của hãng — gửi thì ghi đè, khách thấy để tự tra |

**Mỗi mốc đều gửi thông báo cho chủ của từng đơn trong lô** (một tin mỗi đơn): tiêu đề `Đơn VCL-...: <tên mốc>`, nội dung là `customerMessage` hoặc câu mặc định. FE màn Sale nên có ô "Lời nhắn cho khách" riêng với ô "Ghi chú nội bộ".

Câu mặc định khách nhận khi Sale để trống `customerMessage`:

| Mốc | Khách thấy (tiêu đề) | Nội dung mặc định |
|---|---|---|
| `HANDED_OVER` (kho bàn giao) | Hàng đã xuất kho | Đơn … đã rời kho nguồn và được bàn giao cho đơn vị vận chuyển. Hãng: …, mã vận đơn … |
| `DEPARTED` | Hàng đã khởi hành | Hàng của đơn … đã khởi hành về Việt Nam. |
| `IN_TRANSIT` | Đang vận chuyển về Việt Nam | Hàng của đơn … đang trên đường về Việt Nam. |
| `DELAYED` | Hàng bị chậm so với dự kiến | … Nhân viên sẽ cập nhật thời gian mới sớm nhất. |
| `ON_HOLD` | Hàng đang tạm giữ để xử lý | … Nhân viên sẽ liên hệ với bạn. |
| `CUSTOMS_CLEARED` | Đã thông quan | Hàng của đơn … đã hoàn tất thông quan nhập khẩu. |
| `ARRIVED_VN` | Hàng đã về Việt Nam | … đang được chuyển về kho. |
| `ARRIVED_DESTINATION` | Hàng đã về kho Việt Nam | … kho đang kiểm đếm. |

`PUT /api/international-shipments/{id}/arrive` (đường cũ của luồng tiếp nhận VN) cũng nhận `customerMessage` và cũng báo khách — hai đường vào mốc về kho không đường nào quên báo.

Role: `Sale`, `OperationsManager`, `Admin`, `Staff` — **nhân viên kho gọi thì 403** ("Chỉ Sale, Operations hoặc Admin cập nhật được hành trình lô."). Bấm nút nào được thì đọc `nextMilestones` ở mục M thay vì tự suy.

| Mốc `status` | Chỉ đi lên từ | Bắt buộc kèm |
|---|---|---|
| `DEPARTED` | `HANDED_OVER` | — |
| `IN_TRANSIT` | `HANDED_OVER`, `DEPARTED`, `DELAYED`, `ON_HOLD` | — |
| `DELAYED` | `DEPARTED`, `IN_TRANSIT`, `ON_HOLD` | **`note`** (thiếu → 400) |
| `ON_HOLD` | `DEPARTED`, `IN_TRANSIT`, `DELAYED` | **`note`** + đính kèm **`INCIDENT`** vào lô (thiếu → 400) |
| `CUSTOMS_CLEARED` | `IN_TRANSIT`, `DELAYED`, `ON_HOLD` | đính kèm **`CUSTOMS_IMPORT`** (tờ khai nhập) vào lô (thiếu → 400) |
| `ARRIVED_VN` | `CUSTOMS_CLEARED`, `IN_TRANSIT`, `DELAYED`, `ON_HOLD` | — (tuyến không phải làm thủ tục nhập thì đi thẳng từ `IN_TRANSIT`) |
| `ARRIVED_DESTINATION` | `ARRIVED_VN` | — |

- `carrierTrackingCode` tuỳ chọn, gửi thì ghi đè mã tra cứu của hãng.
- Nhảy cóc mốc → 400 `Lô đang ở ..., không đặt được mốc .... Chỉ đi từ: ...`. Mốc lạ → 400 kèm danh sách mốc hợp lệ.
- **Không lùi trạng thái.** Sai thì ghi thêm mốc đúng kèm ghi chú — mọi lần gọi đều lưu một dòng vào lịch sử theo dõi.
- `ARRIVED_VN`: báo khách từng đơn "đã về Việt Nam". `ARRIVED_DESTINATION`: lô + mọi kiện chuyển `ARRIVED_DESTINATION`, mở đường cho kho VN tiếp nhận (`PUT /api/international-shipments/{id}/destination-receiving`, `PUT /api/parcels/{id}/inspection` — xem [`api-hang-ve-viet-nam.md`](api-hang-ve-viet-nam.md)). Xác nhận lần hai → 400 "Lô vận chuyển này đã được xác nhận đến kho đích trước đó."

Response 200 `{ "message": "Cập nhật hành trình lô thành công.", "data": <chi tiết lô> }`.

**Thử:** `demo.sale` — gọi `ARRIVED_VN` ngay sau bàn giao → 400; `DEPARTED` → 200; `IN_TRANSIT` → 200; `CUSTOMS_CLEARED` khi chưa có tờ khai → 400 (message có `CUSTOMS_IMPORT`); up `CUSTOMS_IMPORT` rồi gọi lại → 200. `demo.kho.cn` gọi API này → 403.

---

## J. Giấy tờ đính kèm

| API | Role | Ghi chú |
|---|---|---|
| `POST /api/attachments` | Nhân viên nội bộ; Khách chỉ `PERMIT` trên đơn của mình | **multipart/form-data** |
| `GET /api/attachments?entityType=&entityId=` | Nhân viên nội bộ; Khách chỉ `entityType=ORDER` của đơn mình | `{ message, data: [...] }` |
| `GET /api/attachments/{id}/download` | Như trên | Trả file, luôn kiểm quyền |

### J1. Tải lên — `POST /api/attachments`

Gửi `multipart/form-data` với các phần:

| Phần | Bắt buộc | Giá trị |
|---|---|---|
| `file` | Có | File thật. Rỗng → 400 "Chưa chọn file để tải lên." |
| `entityType` | Có | `ORDER` · `WRO` · `WRO_PARCEL` · `SHIPMENT` |
| `entityId` | Có | GUID của đối tượng tương ứng |
| `documentType` | Có | Xem bảng J3 |
| `note` | Không | Ghi chú |

Giới hạn: **PDF, JPG, PNG, WEBP; tối đa 10 MB**. Sai định dạng → 400 "Chỉ nhận file PDF, JPG, PNG hoặc WEBP."; quá dung lượng → 400 "File vượt quá 10 MB."
Gửi đúng `Content-Type` cho phần `file` (`application/pdf`, `image/jpeg`, `image/png`, `image/webp`) — server xét theo đây, không xét đuôi tên.

Response 200:

```json
{
  "message": "Tải giấy tờ lên thành công.",
  "data": {
    "id": "guid",
    "entityType": "SHIPMENT", "entityId": "guid",
    "documentType": "HANDOVER_RECORD",
    "fileName": "bien-ban-ban-giao.pdf",
    "contentType": "application/pdf",
    "size": 128374,
    "note": "",
    "uploadedById": "guid", "uploadedByName": "NV Kho Quảng Châu",
    "uploadedAt": "2026-09-18T03:00:00Z",
    "downloadUrl": "/api/attachments/{id}/download"
  }
}
```

`downloadUrl` là **đường dẫn tương đối**, FE ghép base URL và gọi kèm `Authorization`. File nằm riêng tư trên máy chủ, **không có link công khai**.

### J2. Danh sách / tải về

`GET /api/attachments?entityType=WRO&entityId={wroId}` → `{ "message": "Lấy danh sách giấy tờ thành công.", "data": [ <như J1> ] }`, sắp theo thời gian tải lên.
`GET /api/attachments/{id}/download` → file nhị phân đúng `contentType` và tên gốc. File đã mất trên đĩa → 404.

### J3. Loại giấy tờ

| `documentType` | Gắn vào (`entityType`) | Ai up | Bắt buộc ở bước |
|---|---|---|---|
| `PERMIT` | `ORDER` (id đơn) | Khách, nhân viên | Kiện của đơn có hàng hạn chế mới xuất được (`RESTRICTED_NO_PERMIT`) |
| `PICKING_ISSUE` | `WRO_PARCEL` (**id dòng kiện** = `parcels[].id`) | NV kho | **Bỏ kiện khỏi phiếu** (mục E) |
| `PARCEL_PHOTO` | `WRO_PARCEL` | NV kho | Không bắt buộc — ảnh kiện lúc bốc |
| `HANDOVER_RECORD` | `SHIPMENT` (id lô) | NV kho | **Bàn giao lô** |
| `WAYBILL` | `SHIPMENT` | NV kho | **Bàn giao lô** |
| `CUSTOMS_EXPORT` | `SHIPMENT` | NV kho | Bàn giao, khi tuyến yêu cầu tờ khai xuất |
| `COMMERCIAL_INVOICE` | `SHIPMENT` | NV kho | Không bắt buộc |
| `CUSTOMS_IMPORT` | `SHIPMENT` | Sale | **Mốc `CUSTOMS_CLEARED`** |
| `TAX_RECEIPT` | `SHIPMENT` | Sale | Không bắt buộc |
| `INCIDENT` | `SHIPMENT` | Sale | **Mốc `ON_HOLD`** |
| `OTHER` | Bất kỳ | Người có quyền trên đối tượng | — |

`entityType` hoặc `documentType` ngoài danh sách → 400 kèm nguyên danh sách hợp lệ.

### J4. Quyền xem

- **Nhân viên nội bộ** (`Admin`, `Sale`, `Staff`, `WarehouseStaff`, `OperationsManager`, `Operations`, `Delivery`): xem và tải mọi giấy tờ, đính kèm mọi loại.
- **Khách**: chỉ thao tác trên `entityType = ORDER` của **đơn mình** — tải lên chỉ được `PERMIT`; gọi với `entityType = WRO` / `WRO_PARCEL` / `SHIPMENT` → **403** "Chỉ nhân viên mới xem được giấy tờ của phiếu xuất và lô vận chuyển."; gọi đơn của người khác → 403 "Bạn chỉ xem được giấy tờ của đơn mình."

Giấy tờ **không có API xoá** — sai thì up bổ sung bản đúng.

Hệ thống tự sinh, không up qua API này: phiếu xuất kho PDF, danh sách soạn hàng, manifest lô (mục G, H5).

---

## K. Giữ hàng

### `PUT /api/orders/consignments/{orderId}/export-hold`

```json
{ "hold": true, "reason": "Khách muốn gộp chuyến sau" }
```

```json
{ "hold": false }
```

- Role: `Customer` (chỉ đơn của mình, khác → 403), nhân viên nội bộ.
- Bật mà thiếu `reason` → 400 "Bật giữ hàng phải ghi lý do để kho biết vì sao hàng không lên chuyến." Tắt thì không cần lý do.

Response 200 `{ "message": "Đã bật giữ hàng." | "Đã tắt giữ hàng.", "data": { ... } }`:

```json
{
  "orderId": "guid",
  "consignmentCode": "VCL-...",
  "exportHold": true,
  "exportHoldReason": "Khách muốn gộp chuyến sau",
  "exportHoldAt": "2026-09-18T03:10:00Z",
  "parcelsAlreadyInApprovedRelease": ["PCL-..."]
}
```

- Bật giữ hàng → kiện của đơn bị chặn `EXPORT_HOLD` ở mục A và **chặn bàn giao** lô chứa đơn đó.
- `parcelsAlreadyInApprovedRelease` liệt kê kiện **đã nằm trong phiếu đã duyệt** (`APPROVED` / `PICKING` / `READY` / `IN_SHIPMENT`) — cờ giữ hàng chỉ chặn phiếu lập sau, kiện này vẫn đi trừ khi kho chủ động bỏ kiện khỏi phiếu. **FE phải hiện danh sách này cho khách**, đừng để khách tưởng đã dừng được hàng.
- Bật giữ hàng còn báo cho Sale, nhân viên kho và quản lý kho.

**Thử:** `demo.khach1` — bật giữ hàng cho "Đơn 13" (không `reason` → 400, có `reason` → 200) → `demo.kho.cn` gọi `eligible-parcels?includeBlocked=true`, kiện của đơn có `EXPORT_HOLD`. Tắt lại trước khi thử tiếp.

---

## L. Khách theo dõi tiến độ đơn

Khách **không** gọi được API phiếu xuất / lô (403) — mọi thứ khách cần nằm ở hai API dưới, đã lọc sẵn: chỉ đơn của mình, chỉ lời nhắn dành cho khách, không lộ mã phiếu nội bộ, ghi chú nội bộ hay giấy tờ.

| API | Role | Ghi chú |
|---|---|---|
| `GET /api/orders/consignments/tracking?stage=&search=&includeArrived=false&pageNumber=1&pageSize=20` | Customer (đơn của mình), nhân viên (mọi đơn) | Danh sách đơn đang có hàng ở kho / trên đường |
| `GET /api/orders/consignments/{orderId}/tracking` | Customer (đơn của mình, khác → **403**), nhân viên | Hành trình chi tiết một đơn |

### Chặng của đơn (`currentStage`, `parcels[].stage`)

| Giá trị | `currentStageText` khách thấy |
|---|---|
| `NOT_RECEIVED` | Kho chưa nhận hàng |
| `AT_ORIGIN_WAREHOUSE` | Hàng đang lưu tại Kho …, chờ xếp chuyến |
| `PREPARING_EXPORT` | Đang chuẩn bị xuất kho (phiếu xuất đã lập / lô còn nháp) |
| `HANDED_OVER` · `DEPARTED` · `IN_TRANSIT` · `DELAYED` · `ON_HOLD` · `CUSTOMS_CLEARED` · `ARRIVED_VN` · `ARRIVED_DESTINATION` | Tiêu đề mốc ở bảng mục I |

Đơn nhiều kiện đi tách chuyến thì `currentStage` là chặng của **kiện chậm nhất** (khách chỉ nhận đủ hàng khi kiện cuối về) và `isSplitAcrossStages = true` — FE hiện thêm từng kiện ở `parcels`.

### L1. Danh sách — `GET /api/orders/consignments/tracking`

- Chỉ đơn **đã có hàng ở kho**. Mặc định **bỏ đơn đã về kho VN** (`ARRIVED_DESTINATION`); `includeArrived=true` để lấy cả.
- `stage` lọc theo chặng, nhiều giá trị ngăn bởi dấu phẩy: `stage=IN_TRANSIT,DELAYED`.
- `search`: mã đơn, tên khách, mã chuyến.
- Sắp theo sự kiện mới nhất lên đầu.

```json
{
  "message": "Lấy danh sách đơn đang theo dõi thành công.",
  "data": {
    "items": [
      {
        "orderId": "guid", "consignmentCode": "VCL-...", "customerName": "Nguyễn Văn A",
        "currentStage": "IN_TRANSIT", "currentStageText": "Đang vận chuyển về Việt Nam",
        "parcelCount": 2, "shipmentCodes": ["SHIP-20260918-001"],
        "lastEventAt": "2026-09-18T05:00:00Z", "lastEventTitle": "Đang vận chuyển về Việt Nam",
        "estimatedArrivalDate": "2026-09-23T05:00:00Z"
      }
    ],
    "totalCount": 1, "pageNumber": 1, "pageSize": 20, "totalPages": 1
  }
}
```

### L2. Chi tiết — `GET /api/orders/consignments/{orderId}/tracking`

```json
{
  "message": "Lấy hành trình đơn hàng thành công.",
  "data": {
    "orderId": "guid", "consignmentCode": "VCL-...", "orderStatus": "WAREHOUSE_RECEIVED",
    "currentStage": "DEPARTED", "currentStageText": "Hàng đã khởi hành",
    "isSplitAcrossStages": false,
    "originWarehouseName": "Kho Seoul (Hàn Quốc)", "destinationWarehouseName": "Kho HCM - Tân Bình",
    "estimatedArrivalDate": "2026-09-23T05:00:00Z",
    "exportHold": false, "exportHoldReason": null,
    "parcels": [
      { "parcelId": "guid", "packageCode": "PCL-...", "productName": "Áo vest",
        "stage": "DEPARTED", "stageText": "Hàng đã khởi hành",
        "packageStatus": "IN_TRANSIT", "shipmentCode": "SHIP-20260918-001" }
    ],
    "shipments": [
      { "shipmentCode": "SHIP-20260918-001", "status": "DEPARTED", "statusText": "Hàng đã khởi hành",
        "carrierName": "Korean Air Cargo", "carrierTrackingCode": "KE-123-998877",
        "handedOverAt": "2026-09-18T03:00:00Z", "estimatedArrivalDate": "2026-09-23T03:00:00Z",
        "packageCodes": ["PCL-..."] }
    ],
    "events": [
      { "time": "2026-09-17T08:00:00Z", "stage": "AT_ORIGIN_WAREHOUSE", "title": "Kho đã nhận hàng",
        "message": "Kho Seoul (Hàn Quốc) đã nhận hàng của đơn VCL-....", "location": null, "shipmentCode": null },
      { "time": "2026-09-18T01:00:00Z", "stage": "PREPARING_EXPORT", "title": "Đang chuẩn bị xuất kho", "message": "..." },
      { "time": "2026-09-18T03:00:00Z", "stage": "HANDED_OVER", "title": "Hàng đã xuất kho", "message": "...", "location": "Kho Seoul", "shipmentCode": "SHIP-20260918-001" },
      { "time": "2026-09-18T05:00:00Z", "stage": "DEPARTED", "title": "Hàng đã khởi hành",
        "message": "Hàng đã lên chuyến bay KE-123, dự kiến hạ cánh sáng mai.", "location": "Sân bay Incheon", "shipmentCode": "SHIP-20260918-001" }
    ]
  }
}
```

- `events` sắp **cũ → mới** — màn dòng thời gian đảo ngược nếu muốn mới nhất lên đầu.
- `message` là `customerMessage` Sale nhập, hoặc câu mặc định của mốc.
- `estimatedArrivalDate` = lúc bàn giao + số ngày vận chuyển của tuyến (`estimatedTransitDays`); tuyến chưa khai số ngày thì `null`.
- `carrierTrackingCode` + `carrierName`: khách tự tra trên trang của hãng.
- `exportHold = true`: hiện trạng thái "đang giữ hàng" và nút tắt (mục K).
- Sau `ARRIVED_DESTINATION`, đọc `parcels[].packageStatus` cho các bước tiếp ở VN (kiểm đếm, giao hàng).
- Thông báo từng mốc đọc ở `GET /api/notifications` như các thông báo khác.

**Thử:** `demo.khach1` — `GET /api/orders/consignments/tracking?includeArrived=true` rồi mở chi tiết "Đơn 13" (chặng `AT_ORIGIN_WAREHOUSE`). `demo.khach2` mở cùng đơn → 403.

---

## M. Sale theo dõi lô

| API | Role | Ghi chú |
|---|---|---|
| `GET /api/international-shipments/tracking-queue?originWarehouseId=&attentionOnly=false` | Nhân viên nội bộ | Lô đã bàn giao mà chưa về kho đích |
| `GET /api/international-shipments/{id}/timeline` | Nhân viên nội bộ | Mọi mốc + bước tiếp theo + đơn bị ảnh hưởng + giấy tờ |
| `PUT /api/international-shipments/{id}/status` | Sale, OM, Admin | Ghi mốc (mục I) |
| `POST /api/attachments` (`entityType=SHIPMENT`) | Nhân viên nội bộ | Up tờ khai nhập, biên bản sự cố (mục J) |

### M1. Hàng đợi — `GET /api/international-shipments/tracking-queue`

Màn làm việc hằng ngày của Sale. Lô **quá ngày dự kiến** lên đầu, rồi lô **lâu không cập nhật**, rồi theo lần cập nhật cũ nhất. `attentionOnly=true` chỉ lấy lô cần xử lý (quá hạn, ≥ 3 ngày chưa có mốc mới, đang `DELAYED`/`ON_HOLD`).

```json
{
  "message": "Lấy hàng đợi theo dõi lô thành công.",
  "data": [
    {
      "shipmentId": "guid", "shipmentCode": "SHIP-20260918-001",
      "status": "IN_TRANSIT", "statusText": "Đang vận chuyển",
      "originWarehouseName": "Kho Seoul (Hàn Quốc)", "destinationWarehouseName": "Kho HCM - Tân Bình",
      "carrierName": "Korean Air Cargo", "carrierTrackingCode": "KE-123-998877",
      "handedOverAt": "2026-09-18T03:00:00Z", "estimatedArrivalDate": "2026-09-23T03:00:00Z",
      "lastUpdateAt": "2026-09-18T05:00:00Z", "daysSinceLastUpdate": 0,
      "isOverdue": false, "needsUpdate": false,
      "orderCount": 3, "parcelCount": 7,
      "nextMilestones": [
        { "status": "DELAYED", "text": "Trễ lịch", "requirements": ["NOTE"], "missingDocuments": [] },
        { "status": "ON_HOLD", "text": "Tạm giữ / sự cố", "requirements": ["NOTE", "INCIDENT"], "missingDocuments": ["INCIDENT"] },
        { "status": "CUSTOMS_CLEARED", "text": "Đã thông quan nhập", "requirements": ["CUSTOMS_IMPORT"], "missingDocuments": ["CUSTOMS_IMPORT"] },
        { "status": "ARRIVED_VN", "text": "Đã về Việt Nam", "requirements": [], "missingDocuments": [] }
      ]
    }
  ]
}
```

| Trường | Dùng để |
|---|---|
| `nextMilestones` | **Nút được bấm tiếp** — FE chỉ hiện đúng các nút này, theo thứ tự trả về |
| `requirements` | `NOTE` → bắt nhập ghi chú; mã giấy tờ → phải có trên lô |
| `missingDocuments` | Giấy tờ còn thiếu — khoá nút, hiện nút "Tải lên" ngay cạnh |
| `isOverdue` | Quá `estimatedArrivalDate` mà chưa về Việt Nam — tô đỏ |
| `needsUpdate` | ≥ 3 ngày không có mốc mới — nhắc Sale hỏi hãng |

### M2. Dòng thời gian lô — `GET /api/international-shipments/{id}/timeline`

Trả mọi trường của M1, thêm:

```json
{
  "events": [
    { "id": "guid", "time": "2026-09-18T05:00:00Z", "status": "DEPARTED", "statusText": "Đã khởi hành",
      "location": "Sân bay Incheon", "note": "Đã gọi hãng xác nhận chuyến bay",
      "customerMessage": "Hàng đã lên chuyến bay KE-123...", "createdByName": "Nguyễn Sale" }
  ],
  "orders": [ { "orderId": "guid", "consignmentCode": "VCL-...", "customerName": "Nguyễn Văn A", "parcelCount": 2 } ],
  "attachments": [ { "id": "guid", "documentType": "WAYBILL", "fileName": "van-don.pdf", "downloadUrl": "/api/attachments/{id}/download", "...": "..." } ]
}
```

- `note` là ghi chú nội bộ, `customerMessage` là thứ khách đã nhận (`null` = khách nhận câu mặc định).
- `orders`: các đơn bị ảnh hưởng — Sale gọi khách khi lô trễ/tạm giữ.

**Thử:** `demo.sale` — sau khi `demo.kho.cn` bàn giao một lô: `tracking-queue` thấy lô với `nextMilestones = [DEPARTED, IN_TRANSIT]` → ghi `DEPARTED` kèm `customerMessage` → `demo.khach1` mở `GET /api/orders/consignments/{orderId}/tracking` thấy đúng lời nhắn, và có thông báo mới ở `GET /api/notifications`.

---

## N. API đã bỏ so với bản cũ (FE gỡ khỏi code)

| Endpoint cũ | Thay bằng |
|---|---|
| `POST /api/warehouse-release-requests/single` | `POST /api/warehouse-release-requests` |
| `POST /api/warehouse-release-requests/batch` | `POST /api/warehouse-release-requests` |
| `PUT /api/warehouse-release-requests/{id}/status` | `submit` / `withdraw` / `cancel` / `decide` |
| `POST /api/warehouse-release-requests/{id}/picking-list` | `POST /{id}/pick` (mỗi kiện một lần quét) |
| `PUT /api/warehouse-release-requests/{id}/confirm-picking` | Phiếu tự `READY` khi bốc đủ |
| `PUT /api/warehouse-release-requests/{id}/shipping-route` | Tuyến chọn lúc lập/sửa phiếu (`shippingRouteId`) |
| `PUT /api/warehouse-release-requests/{id}/packing` | **Bỏ hẳn bước đóng gói** — hàng đã đóng thùng gỗ từ lúc nhập kho |
| `PUT /api/warehouse-release-requests/{id}/complete` | `POST /api/international-shipments/{id}/handover` |
| `POST /api/warehouse-release-requests/{id}/handover` | `POST /api/international-shipments/{id}/handover` (bàn giao theo **lô**, không theo phiếu) |
| `POST /api/warehouse-release-requests/{id}/notify-customer` | Hệ thống tự báo khách ở mỗi mốc |
| `POST /api/picking-lists/{id}/scan-item` | `POST /api/warehouse-release-requests/{id}/pick` |
| `PUT /api/picking-lists/{id}/confirm` | Không còn (`GET /api/picking-lists/{id}` giữ lại để xem dữ liệu cũ) |
| `GET /api/international-shipments/available-wro-requests` | `GET /api/international-shipments/ready-wro-requests` |
| Tạo lô bằng `parcelIds` | Tạo lô bằng `wroRequestIds` |
| Đặt tay trạng thái lô `READY_TO_SHIP`, `IN_TRANSIT` trước bàn giao | Bàn giao mới sinh `HANDED_OVER`, rồi mới ghi mốc |

---

## Màn hình FE cần có / sửa

| Màn | Việc |
|---|---|
| Chọn kiện xuất (app kho) | Chọn tuyến → `eligible-parcels?includeBlocked=true`, hiện `reasons` cho kiện vướng |
| Phiếu xuất kho | Danh sách theo `status`, chi tiết + nút submit/withdraw/cancel, tải 2 PDF |
| Duyệt phiếu (quản lý kho) | Tab `PENDING_APPROVAL`, nút duyệt/từ chối (từ chối bắt buộc lý do) |
| Bốc hàng (app kho) | Quét mã kiện → quét mã ô `BIN-OUT-*`, đếm `pickedCount/parcelCount`, tự chuyển màn khi `READY` |
| Bỏ kiện | Chụp ảnh → up `PICKING_ISSUE` theo **id dòng kiện** → nhập lý do → quét ô trả về nếu đã bốc |
| Tách phiếu | Chọn kiện đi chuyến sau → mở phiếu con |
| Lập lô | `ready-wro-requests` nhóm theo cặp kho, thêm/gỡ phiếu, manifest |
| Bàn giao | Form hãng + người nhận + vận đơn, **bắt up biên bản và vận đơn trước khi bật nút** |
| Hành trình (Sale) | Nút theo đúng thứ tự mốc, ép ghi chú ở `DELAYED`/`ON_HOLD`, ép tờ khai ở `CUSTOMS_CLEARED` |
| Giữ hàng (khách) | Công tắc + lý do, cảnh báo `parcelsAlreadyInApprovedRelease` |
| Đơn đang vận chuyển (khách) | `GET /api/orders/consignments/tracking`, lọc theo chặng, bấm vào mở chi tiết |
| Theo dõi đơn (khách) | `GET /api/orders/consignments/{orderId}/tracking` — thanh chặng, dòng thời gian, mã vận đơn + hãng, ngày dự kiến, từng kiện |
| Hàng đợi lô (Sale) | `tracking-queue` — tô đỏ `isOverdue`, nhắc `needsUpdate`, lọc `attentionOnly` |
| Chi tiết lô (Sale) | `timeline` — dòng thời gian, nút theo `nextMilestones`, ô "ghi chú nội bộ" + ô "lời nhắn cho khách" + vị trí, tải giấy tờ còn thiếu, danh sách đơn bị ảnh hưởng |

---

## O. Lỗi thường gặp

| Mã | Message (ví dụ) | Nguyên nhân / xử lý |
|---|---|---|
| 403 | Bạn phụ trách vùng KR, không thao tác được tại kho Kho Quảng Châu (CN). | Sai vùng — tài khoản kho chỉ thao tác kho cùng `region` |
| 403 | Tài khoản chưa khai vùng phụ trách nên không thao tác được kho... | Admin điền `region` cho tài khoản |
| 403 | Chỉ nhân viên kho (hoặc Admin) mới thao tác được phiếu xuất kho / lô vận chuyển. | `OperationsManager` cố bốc hàng / lập lô — sai vai trò |
| 403 | Người lập phiếu xuất kho không được tự duyệt... | Đổi tài khoản quản lý kho |
| 403 | Chỉ quản lý kho ... được quyết định phiếu xuất kho. | Kho đã gán quản lý riêng |
| 403 | Chỉ người lập phiếu ... hoặc Admin mới sửa được phiếu. | Sửa/gửi/rút/huỷ phiếu của người khác |
| 403 | Chỉ Sale, Operations hoặc Admin cập nhật được hành trình lô. | Kho gọi `PUT /{id}/status` |
| 403 | Chỉ nhân viên mới xem được phiếu xuất kho. / Chỉ nhân viên mới xem được lô vận chuyển. | Khách gọi API phiếu/lô — dùng mục L thay thế |
| 403 | Bạn chỉ theo dõi được đơn của mình. | Khách mở hành trình đơn của người khác |
| 403 | Chỉ nhân viên mới xem được giấy tờ của phiếu xuất và lô vận chuyển. | Khách gọi `GET /api/attachments` với `entityType` khác `ORDER` |
| 400 | Có kiện chưa đủ điều kiện xuất — PCL-...: Kiện đã nằm trong một phiếu xuất khác. | Kiện đang trong phiếu khác — xử lý phiếu kia trước |
| 400 | Ô kệ BIN-A1-02 thuộc khu lưu kho "Khu A". Kiện đã bốc chỉ được xếp vào khu loại OUTBOUND. | Bốc hàng phải quét ô **khu xuất** |
| 400 | Ô kệ BIN-OUT-01 thuộc khu xuất "Khu xuất". Hàng lưu kho chỉ được xếp vào khu loại STORAGE. | Trả kiện về kệ (`returnBinCode`) phải là ô **khu lưu kho** |
| 404 | Không tìm thấy ô kệ mã ... trong kho đã chọn. | Quét lại mã ô, ô phải thuộc kho nguồn của phiếu |
| 400 | Kiện ... không nằm trong phiếu WRO-... | Quét nhầm kiện |
| 400 | Kiện ... đã được bốc sang khu xuất rồi. | Quét trùng |
| 400 | Phải đính kèm ảnh/biên bản (documentType = PICKING_ISSUE, entityType = WRO_PARCEL, entityId = ...) trước khi bỏ kiện khỏi phiếu. | Up ảnh theo **id dòng kiện**, không phải `parcelId` |
| 400 | Kiện đã bốc sang khu xuất: phải quét ô kệ khu lưu kho (returnBinCode) để trả hàng về. | Thiếu `returnBinCode` khi bỏ kiện đã bốc |
| 400 | Không tách được toàn bộ kiện — phiếu gốc phải còn ít nhất một kiện. | Bỏ bớt kiện khỏi danh sách tách |
| 400 | Các phiếu trong cùng một lô phải cùng kho nguồn và cùng kho đích. | Lọc phiếu theo cặp kho trước khi lập lô |
| 400 | Phiếu ... đang ở ... — chỉ gom được phiếu đã bốc xong (READY). | Phiếu chưa bốc đủ hoặc đã vào lô khác |
| 400 | Phiếu ... còn kiện chưa bốc sang khu xuất: PCL-... | Bốc nốt hoặc bỏ kiện khỏi phiếu rồi bàn giao |
| 400 | Thiếu giấy tờ bàn giao (HANDOVER_RECORD, WAYBILL). Đính kèm vào lô qua POST /api/attachments với entityType = SHIPMENT, entityId = ... | Up đủ giấy tờ trước khi bàn giao |
| 400 | Không bàn giao được, các đơn sau đang vướng — gỡ phiếu chứa kiện của đơn ra khỏi lô: VCL-...: đang bật giữ hàng | Tắt giữ hàng, hoặc gỡ phiếu ra khỏi lô |
| 400 | Lô đang ở HANDED_OVER, không đặt được mốc ARRIVED_VN. Chỉ đi từ: CUSTOMS_CLEARED, IN_TRANSIT, DELAYED, ON_HOLD. | Nhảy cóc mốc — ghi mốc trung gian trước |
| 400 | Mốc trễ / tạm giữ phải ghi chú lý do. | `DELAYED` / `ON_HOLD` thiếu `note` |
| 400 | Phải đính kèm tờ khai nhập (CUSTOMS_IMPORT) trước khi ghi mốc thông quan. | Up tờ khai vào lô |
| 400 | Không điều chuyển / tách / gộp được: kiện đang nằm trong phiếu xuất kho — PCL-... (phiếu WRO-...). | Bỏ kiện khỏi phiếu trước khi đụng vào kiện |
| 400 | Lô ... đang ở ..., chỉ sửa được khi còn nháp. | Thêm/gỡ phiếu, huỷ lô chỉ làm khi lô `DRAFT` |
| 400 | Chỉ nhận file PDF, JPG, PNG hoặc WEBP. / File vượt quá 10 MB. | Kiểm định dạng và dung lượng trước khi gửi |
| 404 | Không tìm thấy đơn vị vận chuyển ... | `carrierId` sai — lấy từ `GET /api/carriers/active` |
| 404 | Không tìm thấy phiếu xuất kho ... / lô vận chuyển ... | Sai id |
