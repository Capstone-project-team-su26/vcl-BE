using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VCL_DLL.Migrations
{
    /// <summary>
    /// Đơn giá phí lưu kho tại kho đích: 20.000đ mỗi kiện mỗi ngày, miễn 7 ngày đầu.
    /// <para>
    /// Seed trong migration chứ không nhập tay trên DB: bảng cấu hình nào không có seed thì
    /// chỉ một lần hạ migration là mất vĩnh viễn — đúng chuyện đã xảy ra với bảng tỷ giá 09/08.
    /// </para>
    /// <para>
    /// <c>service_type = 'STORAGE'</c> KHÔNG phải một phương án vận chuyển. Bảng này kiêm luôn
    /// nguồn cho ô chọn phương án và ô chọn tuyến của khách, nên phía code có
    /// <c>ServicePricingHelper.IsShippingOption</c> lọc dòng này ra khỏi hai ô đó.
    /// </para>
    /// </summary>
    public partial class SeedStorageServicePricing : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF NOT EXISTS (SELECT 1 FROM SERVICE_PRICINGS WHERE service_type = 'STORAGE' AND is_deleted = 0)
BEGIN
    INSERT INTO SERVICE_PRICINGS
        (id, carrier_id, service_type, origin_country, destination_country,
         unit_type, price, currency, effective_date, is_deleted)
    VALUES
        (NEWID(), NULL, 'STORAGE', 'VN', 'VN',
         'PARCEL_DAY', 20000, 'VND', GETUTCDATE(), 0);
END
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Xoá mềm thay vì DELETE: dòng giá này có thể đã được hoá đơn tham chiếu tới.
            migrationBuilder.Sql(@"
UPDATE SERVICE_PRICINGS SET is_deleted = 1 WHERE service_type = 'STORAGE';
");
        }
    }
}
