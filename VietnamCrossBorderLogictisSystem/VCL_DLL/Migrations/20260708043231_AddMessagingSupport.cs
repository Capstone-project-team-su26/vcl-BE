using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace VCL_DLL.Migrations
{
    /// <inheritdoc />
    public partial class AddMessagingSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CONVERSATIONS",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "(newid())"),
                    customer_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    sales_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    related_type = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    related_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(getutcdate())"),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(getutcdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CONVERSATIONS", x => x.id);
                    table.ForeignKey(
                        name: "FK_CONV_CUSTOMER",
                        column: x => x.customer_id,
                        principalTable: "CUSTOMERS",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_CONV_SALES",
                        column: x => x.sales_id,
                        principalTable: "USERS",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "MESSAGES",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "(newid())"),
                    conversation_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    sender_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    sender_role = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    content = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    attachment_url = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    is_read = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(getutcdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MESSAGES", x => x.id);
                    table.ForeignKey(
                        name: "FK_MSG_CONV",
                        column: x => x.conversation_id,
                        principalTable: "CONVERSATIONS",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CONVERSATIONS_customer_id",
                table: "CONVERSATIONS",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "IX_CONVERSATIONS_sales_id",
                table: "CONVERSATIONS",
                column: "sales_id");

            migrationBuilder.CreateIndex(
                name: "IX_MESSAGES_conversation_id",
                table: "MESSAGES",
                column: "conversation_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MESSAGES");

            migrationBuilder.DropTable(
                name: "CONVERSATIONS");
        }
    }
}
