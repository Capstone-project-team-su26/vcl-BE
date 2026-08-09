using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VCL_DLL.Migrations
{
    /// <inheritdoc />
    public partial class CleanupNullCapacityBins : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                -- Gỡ liên kết sơ đồ kho cho các ô kệ rác (max_weight & max_volume bị NULL và không có hàng)
                UPDATE WAREHOUSE_LOCATION_LAYOUTS 
                SET bin_id = NULL 
                WHERE bin_id IN (
                    SELECT b.id 
                    FROM BINS b
                    LEFT JOIN INVENTORIES i ON b.id = i.bin_id AND i.quantity > 0
                    LEFT JOIN PARCELS p ON b.id = p.bin_id
                    WHERE b.max_weight IS NULL AND b.max_volume IS NULL AND i.id IS NULL AND p.id IS NULL
                );

                -- Xóa các ô kệ rác rỗng chưa cấu hình kích thước
                DELETE FROM BINS 
                WHERE id IN (
                    SELECT b.id 
                    FROM BINS b
                    LEFT JOIN INVENTORIES i ON b.id = i.bin_id AND i.quantity > 0
                    LEFT JOIN PARCELS p ON b.id = p.bin_id
                    WHERE b.max_weight IS NULL AND b.max_volume IS NULL AND i.id IS NULL AND p.id IS NULL
                );

                -- Cập nhật giá trị mặc định chuẩn kho (max_volume = 500,000 cm³ ~ 0.5 m³, max_weight = 100 kg) cho các ô kệ còn lại nếu bị NULL
                UPDATE BINS 
                SET max_volume = ISNULL(max_volume, 500000),
                    max_weight = ISNULL(max_weight, 100)
                WHERE max_volume IS NULL OR max_weight IS NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}

