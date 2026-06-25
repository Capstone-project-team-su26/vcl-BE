using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VCL_DLL.Migrations
{
    /// <inheritdoc />
    public partial class AddConsignmentReceiverAndRoute : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "receiver_address",
                table: "ORDERS",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "receiver_name",
                table: "ORDERS",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "receiver_phone",
                table: "ORDERS",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "requires_inspection",
                table: "ORDERS",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "route",
                table: "ORDERS",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "receiver_address",
                table: "ORDERS");

            migrationBuilder.DropColumn(
                name: "receiver_name",
                table: "ORDERS");

            migrationBuilder.DropColumn(
                name: "receiver_phone",
                table: "ORDERS");

            migrationBuilder.DropColumn(
                name: "requires_inspection",
                table: "ORDERS");

            migrationBuilder.DropColumn(
                name: "route",
                table: "ORDERS");
        }
    }
}
