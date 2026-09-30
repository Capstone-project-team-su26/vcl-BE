using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VCL_DLL.Migrations
{
    /// <summary>
    /// Đưa dữ liệu xuất kho cũ sang vòng đời mới (docs/migrate-source-cu/06-luong-xuat-kho-quoc-te.md
    /// mục 10.2): phiếu và lô đổi trạng thái, và dựng lại WRO_PARCELS từ WRO_ITEMS → INVENTORIES.
    /// <para>
    /// Phiếu/lô cũ được đánh dấu <c>is_legacy = 1</c> để các chốt giấy tờ mới không chặn hàng đang
    /// dở dang — nếu chặn thì hàng đã nằm trên tàu cũng không bàn giao được nữa.
    /// </para>
    /// </summary>
    public partial class ExportFlowLegacyData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
-- 1. Kho nguồn / kho đích của phiếu: lấy theo tuyến đã gắn.
UPDATE w
SET w.origin_warehouse_id = r.origin_warehouse_id,
    w.destination_warehouse_id = r.destination_warehouse_id
FROM WRO_REQUESTS w
JOIN SHIPPING_ROUTES r ON r.id = w.shipping_route_id
WHERE w.origin_warehouse_id IS NULL;

-- Phiếu không có tuyến: suy kho từ ô kệ mà kiện của phiếu đang nằm.
;WITH parcel_wh AS (
    SELECT wi.wro_request_id, z.warehouse_id,
           ROW_NUMBER() OVER (PARTITION BY wi.wro_request_id ORDER BY COUNT(*) DESC) AS rn
    FROM WRO_ITEMS wi
    JOIN INVENTORIES i ON i.id = wi.inventory_id
    JOIN PARCELS p ON p.id = i.parcel_id
    JOIN BINS b ON b.id = p.bin_id
    JOIN SHELVES s ON s.id = b.shelf_id
    JOIN WAREHOUSE_ZONES z ON z.id = s.zone_id
    GROUP BY wi.wro_request_id, z.warehouse_id
)
UPDATE w SET w.origin_warehouse_id = pw.warehouse_id
FROM WRO_REQUESTS w
JOIN parcel_wh pw ON pw.wro_request_id = w.id AND pw.rn = 1
WHERE w.origin_warehouse_id IS NULL;

-- 2. Dựng WRO_PARCELS từ dữ liệu cũ. Một kiện chỉ được thuộc một phiếu còn hiệu lực, nên khi
--    kiện xuất hiện ở nhiều phiếu thì giữ phiếu mới nhất và bỏ qua phần còn lại.
;WITH ranked AS (
    SELECT wi.wro_request_id, i.parcel_id, w.status AS wro_status, i.status AS inv_status, p.bin_id,
           ROW_NUMBER() OVER (PARTITION BY i.parcel_id ORDER BY w.created_at DESC) AS rn
    FROM WRO_ITEMS wi
    JOIN WRO_REQUESTS w ON w.id = wi.wro_request_id
    JOIN INVENTORIES i ON i.id = wi.inventory_id
    JOIN PARCELS p ON p.id = i.parcel_id
    WHERE UPPER(LTRIM(RTRIM(w.status))) NOT IN (N'RELEASE_REJECTED', N'CANCELLED', N'REJECTED')
)
INSERT INTO WRO_PARCELS (id, wro_request_id, parcel_id, status, source_bin_id, created_at)
SELECT NEWID(), r.wro_request_id, r.parcel_id,
       CASE
           WHEN UPPER(LTRIM(RTRIM(r.wro_status))) IN (N'HANDED_OVER', N'IN_TRANSIT', N'ARRIVED_IN_VN', N'RELEASED')
                THEN N'HANDED_OVER'
           WHEN UPPER(LTRIM(RTRIM(r.wro_status))) IN (N'PICKED', N'PACKING', N'PACKED')
                OR UPPER(LTRIM(RTRIM(r.inv_status))) = N'PICKED'
                THEN N'PICKED'
           ELSE N'RESERVED'
       END,
       r.bin_id,
       GETUTCDATE()
FROM ranked r
WHERE r.rn = 1
  AND NOT EXISTS (SELECT 1 FROM WRO_PARCELS wp WHERE wp.parcel_id = r.parcel_id AND wp.status <> N'REMOVED');

-- 3. Trạng thái phiếu theo vòng đời mới.
UPDATE WRO_REQUESTS SET is_legacy = 1;

UPDATE WRO_REQUESTS SET status = N'PENDING_APPROVAL' WHERE UPPER(LTRIM(RTRIM(status))) = N'RELEASE_PENDING';
UPDATE WRO_REQUESTS SET status = N'APPROVED'         WHERE UPPER(LTRIM(RTRIM(status))) = N'RELEASE_APPROVED';
UPDATE WRO_REQUESTS SET status = N'REJECTED'         WHERE UPPER(LTRIM(RTRIM(status))) = N'RELEASE_REJECTED';
UPDATE WRO_REQUESTS SET status = N'READY'            WHERE UPPER(LTRIM(RTRIM(status))) IN (N'PICKED', N'PACKING', N'PACKED');
UPDATE WRO_REQUESTS SET status = N'HANDED_OVER'      WHERE UPPER(LTRIM(RTRIM(status))) IN (N'IN_TRANSIT', N'ARRIVED_IN_VN', N'RELEASED');

-- Phiếu đã nằm trong một lô thì trạng thái phải nói đúng chuyện đó.
UPDATE w SET w.status = N'IN_SHIPMENT'
FROM WRO_REQUESTS w
JOIN SHIPMENT_WRO_REQUESTS swr ON swr.wro_request_id = w.id
WHERE UPPER(LTRIM(RTRIM(w.status))) = N'READY';

-- 4. Trạng thái lô theo vòng đời mới.
UPDATE SHIPMENTS SET status = N'DRAFT'
WHERE UPPER(LTRIM(RTRIM(status))) IN (N'CREATED', N'MANIFESTED', N'LOT_CREATED', N'PREPARING', N'READY_TO_SHIP', N'CUSTOMS_EXPORT_PENDING');

UPDATE SHIPMENTS SET status = N'IN_TRANSIT' WHERE UPPER(LTRIM(RTRIM(status))) = N'CUSTOMS_IMPORT_PENDING';
UPDATE SHIPMENTS SET status = N'ON_HOLD'    WHERE UPPER(LTRIM(RTRIM(status))) IN (N'HOLD', N'ISSUE');

-- Lô đang đi thì thời điểm bàn giao lấy theo lúc rời kho đã ghi trước đây.
UPDATE SHIPMENTS SET handed_over_at = shipped_at
WHERE handed_over_at IS NULL AND shipped_at IS NOT NULL
  AND UPPER(LTRIM(RTRIM(status))) IN (N'HANDED_OVER', N'DEPARTED', N'IN_TRANSIT', N'ARRIVED_VN', N'ARRIVED_DESTINATION');
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Không khôi phục được tên trạng thái cũ cho từng phiếu (nhiều giá trị cũ gộp về một
            // giá trị mới), nên bước lùi chỉ xoá phần dữ liệu do migration này sinh ra.
            migrationBuilder.Sql("DELETE FROM WRO_PARCELS;");
        }
    }
}
