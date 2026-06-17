using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VCL_DLL.Migrations
{
    /// <inheritdoc />
    public partial class AddConsignmentItemFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "domestic_tracking_code",
                table: "ORDER_ITEMS",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "quantity",
                table: "ORDER_ITEMS",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reference_url",
                table: "ORDER_ITEMS",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "domestic_tracking_code",
                table: "ORDER_ITEMS");

            migrationBuilder.DropColumn(
                name: "quantity",
                table: "ORDER_ITEMS");

            migrationBuilder.DropColumn(
                name: "reference_url",
                table: "ORDER_ITEMS");
        }
    }
}
