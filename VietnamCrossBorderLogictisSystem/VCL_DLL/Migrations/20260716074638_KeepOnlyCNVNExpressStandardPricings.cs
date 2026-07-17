using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VCL_DLL.Migrations
{
    /// <inheritdoc />
    public partial class KeepOnlyCNVNExpressStandardPricings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE SERVICE_PRICINGS SET is_deleted = 1 WHERE NOT (origin_country = 'CN' AND destination_country = 'VN' AND (service_type = 'Express' OR service_type = 'Standard'));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE SERVICE_PRICINGS SET is_deleted = 0;");
        }
    }
}
