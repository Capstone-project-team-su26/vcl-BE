using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VCL_DLL.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchasePaymentType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "payment_type",
                table: "PURCHASE_PAYMENTS",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true,
                defaultValue: "FULL_PAYMENT");

            // Mọi khoản thanh toán mua hộ trước đây đều là đợt trả toàn bộ giá trị báo giá.
            // Cột nullable nên SQL Server không tự điền default cho các dòng đã có -> backfill tay.
            migrationBuilder.Sql(
                "UPDATE [PURCHASE_PAYMENTS] SET [payment_type] = 'FULL_PAYMENT' WHERE [payment_type] IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "payment_type",
                table: "PURCHASE_PAYMENTS");
        }
    }
}
