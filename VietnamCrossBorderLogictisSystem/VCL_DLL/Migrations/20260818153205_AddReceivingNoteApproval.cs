using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VCL_DLL.Migrations
{
    /// <inheritdoc />
    public partial class AddReceivingNoteApproval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "approved_at",
                table: "WAREHOUSE_RECEIVING_NOTES",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "approved_by",
                table: "WAREHOUSE_RECEIVING_NOTES",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "rejection_reason",
                table: "WAREHOUSE_RECEIVING_NOTES",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            // Phiếu cũ đã ở RECEIVED được coi như đã duyệt. Không backfill thì mọi kiện đang chờ
            // xếp kệ tại kho gốc đều bị chốt mới chặn lại ngay sau khi deploy, mà không ai bấm
            // duyệt được vì phiếu đã rời trạng thái cho phép duyệt.
            migrationBuilder.Sql(@"
                UPDATE WAREHOUSE_RECEIVING_NOTES
                SET status = 'APPROVED', approved_at = GETUTCDATE()
                WHERE status = 'RECEIVED';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "approved_at",
                table: "WAREHOUSE_RECEIVING_NOTES");

            migrationBuilder.DropColumn(
                name: "approved_by",
                table: "WAREHOUSE_RECEIVING_NOTES");

            migrationBuilder.DropColumn(
                name: "rejection_reason",
                table: "WAREHOUSE_RECEIVING_NOTES");
        }
    }
}
