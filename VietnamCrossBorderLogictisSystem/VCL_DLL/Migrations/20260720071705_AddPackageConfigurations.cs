using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VCL_DLL.Migrations
{
    /// <inheritdoc />
    public partial class AddPackageConfigurations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "package_configuration_id",
                table: "PARCELS",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "package_configuration_id",
                table: "ORDER_ITEMS",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PACKAGE_CONFIGURATIONS",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "(newid())"),
                    config_code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    config_name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    length = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    width = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    height = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    max_weight = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    package_fee = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true, defaultValue: "ACTIVE")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PACKAGE_CONFIGURATIONS", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PARCELS_package_configuration_id",
                table: "PARCELS",
                column: "package_configuration_id");

            migrationBuilder.CreateIndex(
                name: "IX_ORDER_ITEMS_package_configuration_id",
                table: "ORDER_ITEMS",
                column: "package_configuration_id");

            migrationBuilder.AddForeignKey(
                name: "FK_ORDER_ITEMS_PACKAGE_CONFIG",
                table: "ORDER_ITEMS",
                column: "package_configuration_id",
                principalTable: "PACKAGE_CONFIGURATIONS",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "FK_PARCELS_PACKAGE_CONFIG",
                table: "PARCELS",
                column: "package_configuration_id",
                principalTable: "PACKAGE_CONFIGURATIONS",
                principalColumn: "id");

            migrationBuilder.InsertData(
                table: "PACKAGE_CONFIGURATIONS",
                columns: new[] { "id", "config_code", "config_name", "length", "width", "height", "max_weight", "package_fee", "status" },
                values: new object[,]
                {
                    { new Guid("99999999-1111-1111-1111-111111111111"), "SMALL", "Small Box", 18m, 12m, 8m, 1.0m, 10000m, "ACTIVE" },
                    { new Guid("99999999-2222-2222-2222-222222222222"), "MEDIUM", "Medium Box", 30m, 20m, 15m, 5.0m, 25000m, "ACTIVE" },
                    { new Guid("99999999-3333-3333-3333-333333333333"), "LARGE", "Large Box", 55m, 40m, 35m, 15.0m, 50000m, "ACTIVE" },
                    { new Guid("99999999-4444-4444-4444-444444444444"), "CUSTOM", "Custom Box", 9999m, 9999m, 9999m, 9999m, 0m, "ACTIVE" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ORDER_ITEMS_PACKAGE_CONFIG",
                table: "ORDER_ITEMS");

            migrationBuilder.DropForeignKey(
                name: "FK_PARCELS_PACKAGE_CONFIG",
                table: "PARCELS");

            migrationBuilder.DropTable(
                name: "PACKAGE_CONFIGURATIONS");

            migrationBuilder.DropIndex(
                name: "IX_PARCELS_package_configuration_id",
                table: "PARCELS");

            migrationBuilder.DropIndex(
                name: "IX_ORDER_ITEMS_package_configuration_id",
                table: "ORDER_ITEMS");

            migrationBuilder.DropColumn(
                name: "package_configuration_id",
                table: "PARCELS");

            migrationBuilder.DropColumn(
                name: "package_configuration_id",
                table: "ORDER_ITEMS");
        }
    }
}
