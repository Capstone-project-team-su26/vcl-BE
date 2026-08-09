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
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM [CARRIERS] WHERE [id] = '41bbc694-4590-49c7-aa30-6ad2f01a5c37')
                BEGIN
                    INSERT INTO [CARRIERS] ([id], [api_url], [carrier_code], [carrier_name], [carrier_type], [contact_email], [contact_phone], [internal_notes], [is_active], [supported_regions], [supported_shipping_methods])
                    VALUES ('41bbc694-4590-49c7-aa30-6ad2f01a5c37', N'https://online-gateway.ghn.vn/shiip/public-api/v2/', N'GHN', N'Giao Hàng Nhanh (GHN)', N'Domestic', N'cskh@ghn.vn', N'1900 636677', N'Đối tác chuyển phát nhanh nội địa GHN.', CAST(1 AS bit), N'Toàn quốc Việt Nam', N'Giao nhanh, Giao tiết kiệm');
                END
                IF NOT EXISTS (SELECT 1 FROM [CARRIERS] WHERE [id] = '5ded98a2-dc10-4a36-b5d6-3b15c5e373d2')
                BEGIN
                    INSERT INTO [CARRIERS] ([id], [api_url], [carrier_code], [carrier_name], [carrier_type], [contact_email], [contact_phone], [internal_notes], [is_active], [supported_regions], [supported_shipping_methods])
                    VALUES ('5ded98a2-dc10-4a36-b5d6-3b15c5e373d2', N'https://sandbox.goship.io/api/v2/', N'GOSHIP', N'GoShip Aggregator', N'Domestic', N'support@goship.io', N'1900 6789', N'Cổng kết nối đa hãng GoShip Sandbox cho giao hàng chặng cuối tại Việt Nam.', CAST(1 AS bit), N'Toàn quốc (63 tỉnh thành Việt Nam)', N'GHTK, GHN, Viettel Post, NJV, JT Express');
                END
                IF NOT EXISTS (SELECT 1 FROM [CARRIERS] WHERE [id] = '7b1a2c3d-4e5f-6a7b-8c9d-0e1f2a3b4c5d')
                BEGIN
                    INSERT INTO [CARRIERS] ([id], [api_url], [carrier_code], [carrier_name], [carrier_type], [contact_email], [contact_phone], [internal_notes], [is_active], [supported_regions], [supported_shipping_methods])
                    VALUES ('7b1a2c3d-4e5f-6a7b-8c9d-0e1f2a3b4c5d', NULL, N'COSCO_SEA', N'COSCO Shipping Lines', N'CrossBorder_Sea', N'booking@coscoshipping.com', N'+84 28 3823 0000', N'Hãng tàu đối tác chặng đường biển Cảng Trung Quốc - Cảng Việt Nam.', CAST(1 AS bit), N'Thượng Hải, Thâm Quyến, Hải Phòng, Cát Lái', N'Vận tải đường biển FCL/LCL');
                END
                IF NOT EXISTS (SELECT 1 FROM [CARRIERS] WHERE [id] = '8a2e1d4b-7c3f-4e5a-9b8c-1d2e3f4a5b6c')
                BEGIN
                    INSERT INTO [CARRIERS] ([id], [api_url], [carrier_code], [carrier_name], [carrier_type], [contact_email], [contact_phone], [internal_notes], [is_active], [supported_regions], [supported_shipping_methods])
                    VALUES ('8a2e1d4b-7c3f-4e5a-9b8c-1d2e3f4a5b6c', NULL, N'HOPPHAT_TRUCK', N'Nhà Xe Vận Tải Hợp Phát', N'CrossBorder_Road', N'lienhe@hopphatlogistics.com', N'0903 123 456', N'Đơn vị đối tác liên hệ ngoài chặng xe tải đường bộ Trung - Việt.', CAST(1 AS bit), N'Bằng Tường, Lạng Sơn, Hà Nội, TP.HCM', N'Xe container, Xe tải đường bộ TQ-VN');
                END
            ");
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
