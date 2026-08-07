using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VCL_DLL.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchaseRequestHistoryAndProofImages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "proof_image_urls",
                table: "PURCHASE_REQUESTS",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PURCHASE_REQUEST_HISTORY",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "(newid())"),
                    purchase_request_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    from_status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    to_status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    proof_image_urls = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    changed_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    changed_by_name = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: true, defaultValueSql: "(getutcdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PURCHASE_REQUEST_HISTORY", x => x.id);
                    table.ForeignKey(
                        name: "FK_PURCHASE_REQUEST_HISTORY_REQUEST",
                        column: x => x.purchase_request_id,
                        principalTable: "PURCHASE_REQUESTS",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PURCHASE_REQUEST_HISTORY_REQUEST",
                table: "PURCHASE_REQUEST_HISTORY",
                column: "purchase_request_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PURCHASE_REQUEST_HISTORY");

            migrationBuilder.DropColumn(
                name: "proof_image_urls",
                table: "PURCHASE_REQUESTS");
        }
    }
}
