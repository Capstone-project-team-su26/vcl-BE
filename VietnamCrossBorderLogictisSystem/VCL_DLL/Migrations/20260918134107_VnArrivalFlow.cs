using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VCL_DLL.Migrations
{
    /// <inheritdoc />
    public partial class VnArrivalFlow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "height",
                table: "PARCEL_INSPECTIONS",
                type: "decimal(10,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "length",
                table: "PARCEL_INSPECTIONS",
                type: "decimal(10,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "width",
                table: "PARCEL_INSPECTIONS",
                type: "decimal(10,2)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "completed_automatically",
                table: "ORDERS",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "proof_at",
                table: "DELIVERY_REQUESTS",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "proof_by",
                table: "DELIVERY_REQUESTS",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "proof_received_by",
                table: "DELIVERY_REQUESTS",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "redelivery_fee",
                table: "DELIVERY_REQUESTS",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "redelivery_fee_payment_id",
                table: "DELIVERY_REQUESTS",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "returned_at",
                table: "DELIVERY_REQUESTS",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "returned_by",
                table: "DELIVERY_REQUESTS",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "source_delivery_request_id",
                table: "DELIVERY_REQUESTS",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PARCEL_INCIDENTS",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "(newid())"),
                    incident_code = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    parcel_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    order_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    incident_type = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    stage = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    reported_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    reported_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(getutcdate())"),
                    customer_choice = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    customer_note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    customer_responded_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    resolution = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    resolution_note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    compensation_amount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    resolved_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    resolved_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    compensation_paid_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    compensation_paid_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    compensation_reference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    row_version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PARCEL_INCIDENTS", x => x.id);
                    table.ForeignKey(
                        name: "FK_PARCEL_INCIDENTS_ORDER",
                        column: x => x.order_id,
                        principalTable: "ORDERS",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PARCEL_INCIDENTS_PARCEL",
                        column: x => x.parcel_id,
                        principalTable: "PARCELS",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PARCEL_INCIDENTS_ORDER_STATUS",
                table: "PARCEL_INCIDENTS",
                columns: new[] { "order_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_PARCEL_INCIDENTS_PARCEL",
                table: "PARCEL_INCIDENTS",
                column: "parcel_id");

            migrationBuilder.CreateIndex(
                name: "UX_PARCEL_INCIDENTS_CODE",
                table: "PARCEL_INCIDENTS",
                column: "incident_code",
                unique: true,
                filter: "[incident_code] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PARCEL_INCIDENTS");

            migrationBuilder.DropColumn(
                name: "height",
                table: "PARCEL_INSPECTIONS");

            migrationBuilder.DropColumn(
                name: "length",
                table: "PARCEL_INSPECTIONS");

            migrationBuilder.DropColumn(
                name: "width",
                table: "PARCEL_INSPECTIONS");

            migrationBuilder.DropColumn(
                name: "completed_automatically",
                table: "ORDERS");

            migrationBuilder.DropColumn(
                name: "proof_at",
                table: "DELIVERY_REQUESTS");

            migrationBuilder.DropColumn(
                name: "proof_by",
                table: "DELIVERY_REQUESTS");

            migrationBuilder.DropColumn(
                name: "proof_received_by",
                table: "DELIVERY_REQUESTS");

            migrationBuilder.DropColumn(
                name: "redelivery_fee",
                table: "DELIVERY_REQUESTS");

            migrationBuilder.DropColumn(
                name: "redelivery_fee_payment_id",
                table: "DELIVERY_REQUESTS");

            migrationBuilder.DropColumn(
                name: "returned_at",
                table: "DELIVERY_REQUESTS");

            migrationBuilder.DropColumn(
                name: "returned_by",
                table: "DELIVERY_REQUESTS");

            migrationBuilder.DropColumn(
                name: "source_delivery_request_id",
                table: "DELIVERY_REQUESTS");
        }
    }
}
