using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace VCL_DLL.Migrations
{
    /// <inheritdoc />
    public partial class SeedCleanCarriers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "CARRIERS",
                columns: new[] { "id", "api_url", "carrier_code", "carrier_name", "carrier_type", "contact_email", "contact_phone", "internal_notes", "is_active", "supported_regions", "supported_shipping_methods" },
                values: new object[,]
                {
                    { new Guid("41bbc694-4590-49c7-aa30-6ad2f01a5c37"), "https://online-gateway.ghn.vn/shiip/public-api/v2/", "GHN", "Giao Hàng Nhanh (GHN)", "Domestic", "cskh@ghn.vn", "1900 636677", "Đối tác chuyển phát nhanh nội địa GHN.", true, "Toàn quốc Việt Nam", "Giao nhanh, Giao tiết kiệm" },
                    { new Guid("5ded98a2-dc10-4a36-b5d6-3b15c5e373d2"), "https://sandbox.goship.io/api/v2/", "GOSHIP", "GoShip Aggregator", "Domestic", "support@goship.io", "1900 6789", "Cổng kết nối đa hãng GoShip Sandbox cho giao hàng chặng cuối tại Việt Nam.", true, "Toàn quốc (63 tỉnh thành Việt Nam)", "GHTK, GHN, Viettel Post, NJV, JT Express" },
                    { new Guid("7b1a2c3d-4e5f-6a7b-8c9d-0e1f2a3b4c5d"), null, "COSCO_SEA", "COSCO Shipping Lines", "CrossBorder_Sea", "booking@coscoshipping.com", "+84 28 3823 0000", "Hãng tàu đối tác chặng đường biển Cảng Trung Quốc - Cảng Việt Nam.", true, "Thượng Hải, Thâm Quyến, Hải Phòng, Cát Lái", "Vận tải đường biển FCL/LCL" },
                    { new Guid("8a2e1d4b-7c3f-4e5a-9b8c-1d2e3f4a5b6c"), null, "HOPPHAT_TRUCK", "Nhà Xe Vận Tải Hợp Phát", "CrossBorder_Road", "lienhe@hopphatlogistics.com", "0903 123 456", "Đơn vị đối tác liên hệ ngoài chặng xe tải đường bộ Trung - Việt.", true, "Bằng Tường, Lạng Sơn, Hà Nội, TP.HCM", "Xe container, Xe tải đường bộ TQ-VN" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "CARRIERS",
                keyColumn: "id",
                keyValue: new Guid("41bbc694-4590-49c7-aa30-6ad2f01a5c37"));

            migrationBuilder.DeleteData(
                table: "CARRIERS",
                keyColumn: "id",
                keyValue: new Guid("5ded98a2-dc10-4a36-b5d6-3b15c5e373d2"));

            migrationBuilder.DeleteData(
                table: "CARRIERS",
                keyColumn: "id",
                keyValue: new Guid("7b1a2c3d-4e5f-6a7b-8c9d-0e1f2a3b4c5d"));

            migrationBuilder.DeleteData(
                table: "CARRIERS",
                keyColumn: "id",
                keyValue: new Guid("8a2e1d4b-7c3f-4e5a-9b8c-1d2e3f4a5b6c"));
        }
    }
}
