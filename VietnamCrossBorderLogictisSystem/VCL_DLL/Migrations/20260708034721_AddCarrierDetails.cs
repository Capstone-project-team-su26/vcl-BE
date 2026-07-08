using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VCL_DLL.Migrations
{
    /// <inheritdoc />
    public partial class AddCarrierDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "carrier_code",
                table: "CARRIERS",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "contact_phone",
                table: "CARRIERS",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "internal_notes",
                table: "CARRIERS",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_active",
                table: "CARRIERS",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "supported_regions",
                table: "CARRIERS",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "supported_shipping_methods",
                table: "CARRIERS",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.Sql("UPDATE CARRIERS SET carrier_code = 'CARRIER_' + CONVERT(VARCHAR(36), id) WHERE carrier_code = '' OR carrier_code IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CARRIERS_carrier_code",
                table: "CARRIERS",
                column: "carrier_code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CARRIERS_carrier_code",
                table: "CARRIERS");

            migrationBuilder.DropColumn(
                name: "carrier_code",
                table: "CARRIERS");

            migrationBuilder.DropColumn(
                name: "contact_phone",
                table: "CARRIERS");

            migrationBuilder.DropColumn(
                name: "internal_notes",
                table: "CARRIERS");

            migrationBuilder.DropColumn(
                name: "is_active",
                table: "CARRIERS");

            migrationBuilder.DropColumn(
                name: "supported_regions",
                table: "CARRIERS");

            migrationBuilder.DropColumn(
                name: "supported_shipping_methods",
                table: "CARRIERS");
        }
    }
}
