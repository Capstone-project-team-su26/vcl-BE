using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VCL_DLL.Migrations
{
    /// <inheritdoc />
    public partial class ExpandPurchaseRequestFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "receiver_address",
                table: "PURCHASE_REQUESTS",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "receiver_name",
                table: "PURCHASE_REQUESTS",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "receiver_phone",
                table: "PURCHASE_REQUESTS",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "requires_inspection",
                table: "PURCHASE_REQUESTS",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "requires_quantity_check",
                table: "PURCHASE_REQUESTS",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "route",
                table: "PURCHASE_REQUESTS",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "product_type",
                table: "PURCHASE_REQUEST_ITEMS",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source_website",
                table: "PURCHASE_REQUEST_ITEMS",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "receiver_address",
                table: "PURCHASE_REQUESTS");

            migrationBuilder.DropColumn(
                name: "receiver_name",
                table: "PURCHASE_REQUESTS");

            migrationBuilder.DropColumn(
                name: "receiver_phone",
                table: "PURCHASE_REQUESTS");

            migrationBuilder.DropColumn(
                name: "requires_inspection",
                table: "PURCHASE_REQUESTS");

            migrationBuilder.DropColumn(
                name: "requires_quantity_check",
                table: "PURCHASE_REQUESTS");

            migrationBuilder.DropColumn(
                name: "route",
                table: "PURCHASE_REQUESTS");

            migrationBuilder.DropColumn(
                name: "product_type",
                table: "PURCHASE_REQUEST_ITEMS");

            migrationBuilder.DropColumn(
                name: "source_website",
                table: "PURCHASE_REQUEST_ITEMS");
        }
    }
}
