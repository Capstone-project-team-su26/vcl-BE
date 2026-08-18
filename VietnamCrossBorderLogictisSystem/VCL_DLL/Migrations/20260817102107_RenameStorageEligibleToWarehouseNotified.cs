using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VCL_DLL.Migrations
{
    /// <inheritdoc />
    public partial class RenameStorageEligibleToWarehouseNotified : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "storage_eligible_note",
                table: "ORDERS",
                newName: "warehouse_notified_note");

            migrationBuilder.RenameColumn(
                name: "storage_eligible_by_id",
                table: "ORDERS",
                newName: "warehouse_notified_by_id");

            migrationBuilder.RenameColumn(
                name: "storage_eligible_at",
                table: "ORDERS",
                newName: "warehouse_notified_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "warehouse_notified_note",
                table: "ORDERS",
                newName: "storage_eligible_note");

            migrationBuilder.RenameColumn(
                name: "warehouse_notified_by_id",
                table: "ORDERS",
                newName: "storage_eligible_by_id");

            migrationBuilder.RenameColumn(
                name: "warehouse_notified_at",
                table: "ORDERS",
                newName: "storage_eligible_at");
        }
    }
}
