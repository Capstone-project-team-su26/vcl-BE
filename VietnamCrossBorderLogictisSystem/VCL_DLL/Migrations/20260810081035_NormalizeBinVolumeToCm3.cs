using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VCL_DLL.Migrations
{
    /// <summary>
    /// Quy đổi sức chứa ô kệ cũ từ lít sang cm³.
    /// <para>
    /// Toàn hệ thống tính thể tích bằng cm³: kích thước kiện nhập theo cm rồi nhân trực tiếp
    /// (<c>InventoryService.RelocateAsync</c>, put-away), và <c>WarehouseService.ValidateBinCapacity</c>
    /// chặn thẳng ô kệ dưới 1.000 cm³ với thông báo "nhiều khả năng đang nhập theo lít hoặc m³".
    /// </para>
    /// <para>
    /// Một số ô kệ tạo trước khi có ràng buộc đó đang giữ số theo lít (80, 100, 200). Kiện
    /// 30×20×5 = 3.000 cm³ đem so với 200 thì luôn báo "ô kệ không đủ sức chứa", dù ô kệ thực tế
    /// rộng 200 lít = 200.000 cm³ — thừa sức chứa. Put-away và điều chuyển vì thế tắc hoàn toàn.
    /// </para>
    /// <para>
    /// Sửa ở tầng dữ liệu chứ không chia thể tích kiện trong code: chia đi thì mâu thuẫn với
    /// ValidateBinCapacity, và những ô kệ đã đúng đơn vị (mặc định 500.000 cm³) sẽ bị hiểu thành
    /// 500.000 lít = 500 m³, nhận mọi kiện hàng kể cả khi thực tế không nhét vừa.
    /// </para>
    /// Ngưỡng 1.000 lấy đúng theo <c>MinBinVolumeCm3</c>, nên chạy lại nhiều lần cũng không nhân hai lần.
    /// </summary>
    public partial class NormalizeBinVolumeToCm3 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
UPDATE BINS
SET max_volume = max_volume * 1000
WHERE max_volume IS NOT NULL AND max_volume > 0 AND max_volume < 1000;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Trả lại đơn vị lít cho đúng những ô vừa quy đổi (1.000 – 999.999 cm³).
            migrationBuilder.Sql(@"
UPDATE BINS
SET max_volume = max_volume / 1000
WHERE max_volume IS NOT NULL AND max_volume >= 1000 AND max_volume < 1000000;
");
        }
    }
}
