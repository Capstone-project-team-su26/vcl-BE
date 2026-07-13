using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VCL_DLL.Migrations
{
    /// <inheritdoc />
    public partial class UpdateQuotationFeeWithParcelDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "chargeable_weight",
                table: "QUOTATION_FEES",
                type: "decimal(18,4)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "parcel_id",
                table: "QUOTATION_FEES",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "shipping_fee",
                table: "QUOTATION_FEES",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "total_amount",
                table: "QUOTATION_FEES",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_QUOTATION_FEES_parcel_id",
                table: "QUOTATION_FEES",
                column: "parcel_id");

            migrationBuilder.AddForeignKey(
                name: "FK_QF_PARCEL",
                table: "QUOTATION_FEES",
                column: "parcel_id",
                principalTable: "PARCELS",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_QF_PARCEL",
                table: "QUOTATION_FEES");

            migrationBuilder.DropIndex(
                name: "IX_QUOTATION_FEES_parcel_id",
                table: "QUOTATION_FEES");

            migrationBuilder.DropColumn(
                name: "chargeable_weight",
                table: "QUOTATION_FEES");

            migrationBuilder.DropColumn(
                name: "parcel_id",
                table: "QUOTATION_FEES");

            migrationBuilder.DropColumn(
                name: "shipping_fee",
                table: "QUOTATION_FEES");

            migrationBuilder.DropColumn(
                name: "total_amount",
                table: "QUOTATION_FEES");
        }
    }
}
