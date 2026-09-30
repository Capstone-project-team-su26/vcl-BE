using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VCL_DLL.Migrations
{
    /// <inheritdoc />
    public partial class ExportFlowWroParcels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "decided_at",
                table: "WRO_REQUESTS",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "decided_by",
                table: "WRO_REQUESTS",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "decision_note",
                table: "WRO_REQUESTS",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "destination_warehouse_id",
                table: "WRO_REQUESTS",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_legacy",
                table: "WRO_REQUESTS",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "origin_warehouse_id",
                table: "WRO_REQUESTS",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "row_version",
                table: "WRO_REQUESTS",
                type: "rowversion",
                rowVersion: true,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "split_from_id",
                table: "WRO_REQUESTS",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "submitted_at",
                table: "WRO_REQUESTS",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "submitted_by",
                table: "WRO_REQUESTS",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "requires_customs_export",
                table: "SHIPPING_ROUTES",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "created_by",
                table: "SHIPMENTS",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "handed_over_at",
                table: "SHIPMENTS",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "row_version",
                table: "SHIPMENTS",
                type: "rowversion",
                rowVersion: true,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "handed_over_by",
                table: "SHIPMENT_HANDOVERS",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "note",
                table: "SHIPMENT_HANDOVERS",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "recipient_name",
                table: "SHIPMENT_HANDOVERS",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "recipient_phone",
                table: "SHIPMENT_HANDOVERS",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "seal_number",
                table: "SHIPMENT_HANDOVERS",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "vehicle_number",
                table: "SHIPMENT_HANDOVERS",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "waybill_code",
                table: "SHIPMENT_HANDOVERS",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "export_hold",
                table: "ORDERS",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "export_hold_at",
                table: "ORDERS",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "export_hold_by",
                table: "ORDERS",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "export_hold_reason",
                table: "ORDERS",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ATTACHMENTS",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "(newid())"),
                    entity_type = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    entity_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    document_type = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    file_name = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    content_type = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    size = table.Column<long>(type: "bigint", nullable: false),
                    sha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    storage_key = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    uploaded_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    uploaded_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(getutcdate())"),
                    note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ATTACHMENTS", x => x.id);
                    table.ForeignKey(
                        name: "FK_ATTACHMENTS_UPLOADER",
                        column: x => x.uploaded_by,
                        principalTable: "USERS",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WRO_PARCELS",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "(newid())"),
                    wro_request_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    parcel_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    source_bin_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    staging_bin_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    picked_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    picked_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    removed_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    removed_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    removed_reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(getutcdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WRO_PARCELS", x => x.id);
                    table.ForeignKey(
                        name: "FK_WRO_PARCELS_PARCEL",
                        column: x => x.parcel_id,
                        principalTable: "PARCELS",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WRO_PARCELS_REQUEST",
                        column: x => x.wro_request_id,
                        principalTable: "WRO_REQUESTS",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WRO_REQUESTS_destination_warehouse_id",
                table: "WRO_REQUESTS",
                column: "destination_warehouse_id");

            migrationBuilder.CreateIndex(
                name: "IX_WRO_REQUESTS_ORIGIN_WAREHOUSE",
                table: "WRO_REQUESTS",
                column: "origin_warehouse_id");

            migrationBuilder.CreateIndex(
                name: "IX_ATTACHMENTS_ENTITY",
                table: "ATTACHMENTS",
                columns: new[] { "entity_type", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "IX_ATTACHMENTS_uploaded_by",
                table: "ATTACHMENTS",
                column: "uploaded_by");

            migrationBuilder.CreateIndex(
                name: "IX_WRO_PARCELS_REQUEST",
                table: "WRO_PARCELS",
                column: "wro_request_id");

            migrationBuilder.CreateIndex(
                name: "UX_WRO_PARCELS_ACTIVE_PARCEL",
                table: "WRO_PARCELS",
                column: "parcel_id",
                unique: true,
                filter: "[status] <> 'REMOVED'");

            migrationBuilder.AddForeignKey(
                name: "FK_WRO_DESTINATION_WAREHOUSE",
                table: "WRO_REQUESTS",
                column: "destination_warehouse_id",
                principalTable: "WAREHOUSES",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_WRO_ORIGIN_WAREHOUSE",
                table: "WRO_REQUESTS",
                column: "origin_warehouse_id",
                principalTable: "WAREHOUSES",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WRO_DESTINATION_WAREHOUSE",
                table: "WRO_REQUESTS");

            migrationBuilder.DropForeignKey(
                name: "FK_WRO_ORIGIN_WAREHOUSE",
                table: "WRO_REQUESTS");

            migrationBuilder.DropTable(
                name: "ATTACHMENTS");

            migrationBuilder.DropTable(
                name: "WRO_PARCELS");

            migrationBuilder.DropIndex(
                name: "IX_WRO_REQUESTS_destination_warehouse_id",
                table: "WRO_REQUESTS");

            migrationBuilder.DropIndex(
                name: "IX_WRO_REQUESTS_ORIGIN_WAREHOUSE",
                table: "WRO_REQUESTS");

            migrationBuilder.DropColumn(
                name: "decided_at",
                table: "WRO_REQUESTS");

            migrationBuilder.DropColumn(
                name: "decided_by",
                table: "WRO_REQUESTS");

            migrationBuilder.DropColumn(
                name: "decision_note",
                table: "WRO_REQUESTS");

            migrationBuilder.DropColumn(
                name: "destination_warehouse_id",
                table: "WRO_REQUESTS");

            migrationBuilder.DropColumn(
                name: "is_legacy",
                table: "WRO_REQUESTS");

            migrationBuilder.DropColumn(
                name: "origin_warehouse_id",
                table: "WRO_REQUESTS");

            migrationBuilder.DropColumn(
                name: "row_version",
                table: "WRO_REQUESTS");

            migrationBuilder.DropColumn(
                name: "split_from_id",
                table: "WRO_REQUESTS");

            migrationBuilder.DropColumn(
                name: "submitted_at",
                table: "WRO_REQUESTS");

            migrationBuilder.DropColumn(
                name: "submitted_by",
                table: "WRO_REQUESTS");

            migrationBuilder.DropColumn(
                name: "requires_customs_export",
                table: "SHIPPING_ROUTES");

            migrationBuilder.DropColumn(
                name: "created_by",
                table: "SHIPMENTS");

            migrationBuilder.DropColumn(
                name: "handed_over_at",
                table: "SHIPMENTS");

            migrationBuilder.DropColumn(
                name: "row_version",
                table: "SHIPMENTS");

            migrationBuilder.DropColumn(
                name: "handed_over_by",
                table: "SHIPMENT_HANDOVERS");

            migrationBuilder.DropColumn(
                name: "note",
                table: "SHIPMENT_HANDOVERS");

            migrationBuilder.DropColumn(
                name: "recipient_name",
                table: "SHIPMENT_HANDOVERS");

            migrationBuilder.DropColumn(
                name: "recipient_phone",
                table: "SHIPMENT_HANDOVERS");

            migrationBuilder.DropColumn(
                name: "seal_number",
                table: "SHIPMENT_HANDOVERS");

            migrationBuilder.DropColumn(
                name: "vehicle_number",
                table: "SHIPMENT_HANDOVERS");

            migrationBuilder.DropColumn(
                name: "waybill_code",
                table: "SHIPMENT_HANDOVERS");

            migrationBuilder.DropColumn(
                name: "export_hold",
                table: "ORDERS");

            migrationBuilder.DropColumn(
                name: "export_hold_at",
                table: "ORDERS");

            migrationBuilder.DropColumn(
                name: "export_hold_by",
                table: "ORDERS");

            migrationBuilder.DropColumn(
                name: "export_hold_reason",
                table: "ORDERS");
        }
    }
}
