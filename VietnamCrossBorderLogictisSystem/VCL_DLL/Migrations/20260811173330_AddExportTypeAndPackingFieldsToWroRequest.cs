using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VCL_DLL.Migrations
{
    /// <inheritdoc />
    public partial class AddExportTypeAndPackingFieldsToWroRequest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExportType",
                table: "WRO_REQUESTS",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PackagingPhotos",
                table: "WRO_REQUESTS",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PackingNotes",
                table: "WRO_REQUESTS",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExportType",
                table: "WRO_REQUESTS");

            migrationBuilder.DropColumn(
                name: "PackagingPhotos",
                table: "WRO_REQUESTS");

            migrationBuilder.DropColumn(
                name: "PackingNotes",
                table: "WRO_REQUESTS");
        }
    }
}
