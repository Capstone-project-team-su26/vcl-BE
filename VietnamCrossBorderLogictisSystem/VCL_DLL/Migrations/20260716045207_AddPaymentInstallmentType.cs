using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VCL_DLL.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentInstallmentType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "installment_type",
                table: "PAYMENTS",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            // Seed cấu hình tỷ lệ cọc mặc định (Admin chỉnh qua API additional-service-fees).
            migrationBuilder.Sql(@"
IF NOT EXISTS (SELECT 1 FROM [ADDITIONAL_SERVICE_FEES] WHERE [fee_code] = 'DEPOSIT_RATE')
BEGIN
    INSERT INTO [ADDITIONAL_SERVICE_FEES] ([id], [fee_name], [fee_code], [calculation_type], [value], [unit], [is_active], [description], [created_at])
    VALUES (NEWID(), N'Tỷ lệ cọc đơn ký gửi', 'DEPOSIT_RATE', 'PERCENTAGE', 50, '%', 1, N'Tỷ lệ % tiền cọc khách thanh toán khi xác nhận báo giá ký gửi (đợt 1).', GETUTCDATE());
END");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "installment_type",
                table: "PAYMENTS");
        }
    }
}
