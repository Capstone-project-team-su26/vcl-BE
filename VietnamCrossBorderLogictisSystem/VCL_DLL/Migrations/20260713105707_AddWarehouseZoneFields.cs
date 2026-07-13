using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VCL_DLL.Migrations
{
    /// <inheritdoc />
    public partial class AddWarehouseZoneFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "status",
                table: "WAREHOUSE_ZONES",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true,
                defaultValue: "Active");

            migrationBuilder.AddColumn<string>(
                name: "zone_code",
                table: "WAREHOUSE_ZONES",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "zone_type",
                table: "WAREHOUSE_ZONES",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "status",
                table: "WAREHOUSE_ZONES");

            migrationBuilder.DropColumn(
                name: "zone_code",
                table: "WAREHOUSE_ZONES");

            migrationBuilder.DropColumn(
                name: "zone_type",
                table: "WAREHOUSE_ZONES");
        }
    }
}
