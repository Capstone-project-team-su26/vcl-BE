using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VCL_DLL.Migrations
{
    /// <summary>
    /// Nạp tỷ giá quy đổi sang VND cho các tuyến đang khai thác.
    /// <para>
    /// Bảng EXCHANGE_RATES tạo ở migration 20260807160342 nhưng không kèm seed, nên dữ liệu tỷ giá
    /// chỉ tồn tại nhờ nhập tay qua API. Ngày 09/08/2026 bảng bị drop rồi create lại khi có người
    /// hạ migration về mốc cũ rồi chạy tiến lại — toàn bộ tỷ giá mất trắng và không khôi phục được
    /// từ đâu (migration không seed, không báo giá nào từng dùng ngoại tệ để suy ngược).
    /// Seed này để lần sau bảng có bị tạo lại thì dữ liệu vẫn tự về.
    /// </para>
    /// <para>
    /// Viết bằng SQL có <c>IF NOT EXISTS</c> thay vì <c>InsertData</c>: chạy lại nhiều lần không lỗi,
    /// và không đụng tỷ giá Admin đã chỉnh. Dùng <c>InsertData</c> với GUID cố định là cách đã làm
    /// kẹt migration SeedCleanCarriers vì trùng khoá chính.
    /// </para>
    /// Số liệu là <b>tỷ giá tham khảo</b> để hệ thống có cái mà chạy — Admin cập nhật số thật
    /// qua <c>PUT /api/exchange-rates/{id}</c>.
    /// </summary>
    public partial class SeedExchangeRates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF NOT EXISTS (SELECT 1 FROM EXCHANGE_RATES WHERE currency_code = 'CNY')
    INSERT INTO EXCHANGE_RATES (id, currency_code, currency_name, rate_to_vnd, is_active, note, created_at)
    VALUES ('a1c00001-0000-4000-8000-000000000001', 'CNY', N'Nhân dân tệ (Trung Quốc)', 3600.0000,
            1, N'Tỷ giá tham khảo, Admin cập nhật lại theo thực tế.', GETUTCDATE());

IF NOT EXISTS (SELECT 1 FROM EXCHANGE_RATES WHERE currency_code = 'KRW')
    INSERT INTO EXCHANGE_RATES (id, currency_code, currency_name, rate_to_vnd, is_active, note, created_at)
    VALUES ('a1c00001-0000-4000-8000-000000000002', 'KRW', N'Won (Hàn Quốc)', 19.0000,
            1, N'Tỷ giá tham khảo, Admin cập nhật lại theo thực tế.', GETUTCDATE());

IF NOT EXISTS (SELECT 1 FROM EXCHANGE_RATES WHERE currency_code = 'JPY')
    INSERT INTO EXCHANGE_RATES (id, currency_code, currency_name, rate_to_vnd, is_active, note, created_at)
    VALUES ('a1c00001-0000-4000-8000-000000000003', 'JPY', N'Yên (Nhật Bản)', 175.0000,
            1, N'Tỷ giá tham khảo, Admin cập nhật lại theo thực tế.', GETUTCDATE());

IF NOT EXISTS (SELECT 1 FROM EXCHANGE_RATES WHERE currency_code = 'USD')
    INSERT INTO EXCHANGE_RATES (id, currency_code, currency_name, rate_to_vnd, is_active, note, created_at)
    VALUES ('a1c00001-0000-4000-8000-000000000004', 'USD', N'Đô la Mỹ', 26000.0000,
            1, N'Tỷ giá tham khảo, Admin cập nhật lại theo thực tế.', GETUTCDATE());
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Chỉ gỡ đúng 4 dòng seed theo id, không đụng ngoại tệ Admin tự thêm.
            migrationBuilder.Sql(@"
DELETE FROM EXCHANGE_RATES
WHERE id IN (
    'a1c00001-0000-4000-8000-000000000001',
    'a1c00001-0000-4000-8000-000000000002',
    'a1c00001-0000-4000-8000-000000000003',
    'a1c00001-0000-4000-8000-000000000004'
);
");
        }
    }
}
