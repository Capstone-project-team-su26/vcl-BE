using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VCL_DLL.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchaseRequestServiceFlags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "requires_insurance",
                table: "PURCHASE_REQUESTS",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "requires_packing",
                table: "PURCHASE_REQUESTS",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "requires_wooden_crate",
                table: "PURCHASE_REQUESTS",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "requires_insurance",
                table: "PURCHASE_REQUESTS");

            migrationBuilder.DropColumn(
                name: "requires_packing",
                table: "PURCHASE_REQUESTS");

            migrationBuilder.DropColumn(
                name: "requires_wooden_crate",
                table: "PURCHASE_REQUESTS");
        }
    }
}
