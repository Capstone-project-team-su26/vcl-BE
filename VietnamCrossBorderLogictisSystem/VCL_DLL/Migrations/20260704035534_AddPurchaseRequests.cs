using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VCL_DLL.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchaseRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PURCHASE_REQUESTS",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "(newid())"),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    customer_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    purchase_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: true, defaultValueSql: "(getutcdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PURCHASE_REQUESTS", x => x.id);
                    table.ForeignKey(
                        name: "FK_PURCHASE_REQ_CUSTOMER",
                        column: x => x.customer_id,
                        principalTable: "CUSTOMERS",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_PURCHASE_REQ_USER",
                        column: x => x.user_id,
                        principalTable: "USERS",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "PURCHASE_REQUEST_ITEMS",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "(newid())"),
                    purchase_request_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    product_link = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    product_name = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    quantity = table.Column<int>(type: "int", nullable: false),
                    attributes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    image_url = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PURCHASE_REQUEST_ITEMS", x => x.id);
                    table.ForeignKey(
                        name: "FK_PURCHASE_REQ_ITEM_REQUEST",
                        column: x => x.purchase_request_id,
                        principalTable: "PURCHASE_REQUESTS",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PURCHASE_REQUEST_ITEMS_purchase_request_id",
                table: "PURCHASE_REQUEST_ITEMS",
                column: "purchase_request_id");

            migrationBuilder.CreateIndex(
                name: "IX_PURCHASE_REQUESTS_customer_id",
                table: "PURCHASE_REQUESTS",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "IX_PURCHASE_REQUESTS_user_id",
                table: "PURCHASE_REQUESTS",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PURCHASE_REQUEST_ITEMS");

            migrationBuilder.DropTable(
                name: "PURCHASE_REQUESTS");
        }
    }
}
