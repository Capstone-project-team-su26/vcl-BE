using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VCL_DLL.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CARRIERS",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "(newid())"),
                    carrier_name = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    carrier_type = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    api_url = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    contact_email = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__CARRIERS__3213E83FEE9EF3D6", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "RESTRICTED_ITEMS",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "(newid())"),
                    country = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    item_name = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    restriction_type = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    note = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__RESTRICT__3213E83FBA73EC49", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "USERS",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "(newid())"),
                    full_name = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    email = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    phone = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    password = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    role = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    user_type = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: true, defaultValueSql: "(getutcdate())"),
                    ResetPasswordOtpHash = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ResetPasswordOtpExpiry = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ResetPasswordFailedAttempts = table.Column<int>(type: "int", nullable: false),
                    LastResetOtpSentAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    is_email_verified = table.Column<bool>(type: "bit", nullable: false),
                    email_verification_otp_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    email_verification_otp_expiry = table.Column<DateTime>(type: "datetime2", nullable: true),
                    email_verification_failed_attempts = table.Column<int>(type: "int", nullable: false),
                    last_otp_sent_at = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__USERS__3213E83F9DEBD1C3", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "WAREHOUSES",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "(newid())"),
                    warehouse_name = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    country = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    city = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    address = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    warehouse_type = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: true, defaultValueSql: "(getutcdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__WAREHOUS__3213E83F27FFA333", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "SERVICE_PRICINGS",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "(newid())"),
                    carrier_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    service_type = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    origin_country = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    destination_country = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    unit_type = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    price = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    currency = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    effective_date = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__SERVICE___3213E83F302461BA", x => x.id);
                    table.ForeignKey(
                        name: "FK_PRICING_CARRIER",
                        column: x => x.carrier_id,
                        principalTable: "CARRIERS",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "COMPANIES",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "(newid())"),
                    owner_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    company_name = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    tax_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    country = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    address = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: true, defaultValueSql: "(getutcdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__COMPANIE__3213E83FBC5B7014", x => x.id);
                    table.ForeignKey(
                        name: "FK_COMP_USER",
                        column: x => x.owner_id,
                        principalTable: "USERS",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "NOTIFICATIONS",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "(newid())"),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    title = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    content = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    is_read = table.Column<bool>(type: "bit", nullable: true, defaultValue: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: true, defaultValueSql: "(getutcdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__NOTIFICA__3213E83F4F6AF9FD", x => x.id);
                    table.ForeignKey(
                        name: "FK_NOTIF_USER",
                        column: x => x.user_id,
                        principalTable: "USERS",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "WAREHOUSE_ZONES",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "(newid())"),
                    warehouse_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    zone_name = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__WAREHOUS__3213E83F216F8248", x => x.id);
                    table.ForeignKey(
                        name: "FK_ZONE_WH",
                        column: x => x.warehouse_id,
                        principalTable: "WAREHOUSES",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "CUSTOMERS",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "(newid())"),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    customer_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    full_name = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    email = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    phone = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    country = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    address = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    customer_type = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: true, defaultValueSql: "(getutcdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__CUSTOMER__3213E83FA05355C1", x => x.id);
                    table.ForeignKey(
                        name: "FK_CUSTOMER_COMPANY",
                        column: x => x.company_id,
                        principalTable: "COMPANIES",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_CUSTOMER_USER",
                        column: x => x.user_id,
                        principalTable: "USERS",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "SHELVES",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "(newid())"),
                    zone_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    shelf_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__SHELVES__3213E83F1F58091A", x => x.id);
                    table.ForeignKey(
                        name: "FK_SHELF_ZONE",
                        column: x => x.zone_id,
                        principalTable: "WAREHOUSE_ZONES",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "ORDERS",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "(newid())"),
                    customer_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    consignment_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    consignment_type = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    total_weight = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    total_volume = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    note = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    order_type = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: true, defaultValueSql: "(getutcdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__ORDERS__3213E83F039F5424", x => x.id);
                    table.ForeignKey(
                        name: "FK_ORDER_COMP",
                        column: x => x.company_id,
                        principalTable: "COMPANIES",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_ORDER_CUSTOMER",
                        column: x => x.customer_id,
                        principalTable: "CUSTOMERS",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "WRO_REQUESTS",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "(newid())"),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    customer_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    shelf_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    wro_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    receiver_name = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    receiver_phone = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    delivery_address = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: true, defaultValueSql: "(getutcdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__WRO_REQU__3213E83F1D9751C6", x => x.id);
                    table.ForeignKey(
                        name: "FK_WRO_CUSTOMER",
                        column: x => x.customer_id,
                        principalTable: "CUSTOMERS",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_WRO_USER",
                        column: x => x.user_id,
                        principalTable: "USERS",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "ORDER_ITEMS",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "(newid())"),
                    order_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    sku = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    product_name = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    weight = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    width = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    height = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    length = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    unit = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    product_type = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    declared_value = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: true, defaultValueSql: "(getutcdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__ORDER_IT__3213E83F13F43A7F", x => x.id);
                    table.ForeignKey(
                        name: "FK_ITEM_COMP",
                        column: x => x.company_id,
                        principalTable: "COMPANIES",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_ITEM_ORDER",
                        column: x => x.order_id,
                        principalTable: "ORDERS",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "SUPPORT_TICKETS",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "(newid())"),
                    customer_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    consignment_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    subject = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: true, defaultValueSql: "(getutcdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__SUPPORT___3213E83FC640A23B", x => x.id);
                    table.ForeignKey(
                        name: "FK_TICKET_CUSTOMER",
                        column: x => x.customer_id,
                        principalTable: "CUSTOMERS",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_TICKET_ORDER",
                        column: x => x.consignment_id,
                        principalTable: "ORDERS",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "QUOTATIONS",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "(newid())"),
                    order_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    wro_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    quote_type = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    estimated_freight_charge = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    tax_and_duty = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    total_volume = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    service_fee = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: true, defaultValueSql: "(getutcdate())"),
                    expired_at = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__QUOTATIO__3213E83FFEFC40E8", x => x.id);
                    table.ForeignKey(
                        name: "FK_QUOTE_ORDER",
                        column: x => x.order_id,
                        principalTable: "ORDERS",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_QUOTE_WRO",
                        column: x => x.wro_id,
                        principalTable: "WRO_REQUESTS",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "BINS",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "(newid())"),
                    product_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    warehouse_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    shelf_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    bin_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    max_volume = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    current_volume = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    max_weight = table.Column<decimal>(type: "decimal(18,2)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__BINS__3213E83F5D6097FF", x => x.id);
                    table.ForeignKey(
                        name: "FK_BIN_PRODUCT",
                        column: x => x.product_id,
                        principalTable: "ORDER_ITEMS",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_BIN_SHELF",
                        column: x => x.shelf_id,
                        principalTable: "SHELVES",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_BIN_WH",
                        column: x => x.warehouse_id,
                        principalTable: "WAREHOUSES",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "INVOICES",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "(newid())"),
                    quotation_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    invoice_no = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    total_amount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    currency = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    invoice_status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    issued_at = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__INVOICES__3213E83F6A731E25", x => x.id);
                    table.ForeignKey(
                        name: "FK_INV_QUOTE",
                        column: x => x.quotation_id,
                        principalTable: "QUOTATIONS",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "INVOICE_ITEMS",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "(newid())"),
                    invoice_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    fee_type = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    reference_type = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    reference_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    amount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    currency = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    description = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__INVOICE___3213E83F973152D5", x => x.id);
                    table.ForeignKey(
                        name: "FK_INVITEM_INV",
                        column: x => x.invoice_id,
                        principalTable: "INVOICES",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "PAYMENTS",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "(newid())"),
                    invoice_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    amount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    payment_method = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    payment_status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    transaction_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    paid_at = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__PAYMENTS__3213E83FA1AFBB3B", x => x.id);
                    table.ForeignKey(
                        name: "FK_PAY_INV",
                        column: x => x.invoice_id,
                        principalTable: "INVOICES",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "CUSTOMS_DECLARATIONS",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "(newid())"),
                    shipment_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    hs_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    declared_value = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    clearance_status = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    declared_at = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__CUSTOMS___3213E83FAD31662A", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "CUSTOMS_DOCUMENTS",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "(newid())"),
                    customs_declaration_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    document_type = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    file_url = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    uploaded_at = table.Column<DateTime>(type: "datetime2", nullable: true, defaultValueSql: "(getutcdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__CUSTOMS___3213E83F0ECEAE66", x => x.id);
                    table.ForeignKey(
                        name: "FK_DOC_CUST",
                        column: x => x.customs_declaration_id,
                        principalTable: "CUSTOMS_DECLARATIONS",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "CUSTOMS_TAXES",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "(newid())"),
                    customs_declaration_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tax_type = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    tax_rate = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    amount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    currency = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__CUSTOMS___3213E83F508B2662", x => x.id);
                    table.ForeignKey(
                        name: "FK_TAX_CUST",
                        column: x => x.customs_declaration_id,
                        principalTable: "CUSTOMS_DECLARATIONS",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "DOMESTIC_WAYBILLS",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "(newid())"),
                    parcel_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    carrier_name = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    actual_weight = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    tracking_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    delivery_fee = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: true, defaultValueSql: "(getutcdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__DOMESTIC__3213E83FA3DA4C35", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "MASTER_BOXES",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "(newid())"),
                    shipment_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    master_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    total_weight = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    total_volume = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__MASTER_B__3213E83F9A589884", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "PARCELS",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "(newid())"),
                    consignment_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    master_box_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    package_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    actual_weight = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    volumetric_weight = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    chargeable_weight = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    width = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    height = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    length = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    package_status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__PARCELS__3213E83FD79A8B69", x => x.id);
                    table.ForeignKey(
                        name: "FK_PARCEL_MASTER",
                        column: x => x.master_box_id,
                        principalTable: "MASTER_BOXES",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_PARCEL_ORDER",
                        column: x => x.consignment_id,
                        principalTable: "ORDERS",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "SHIPMENTS",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "(newid())"),
                    consignment_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    origin_warehouse_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    destination_warehouse_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    carrier_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    master_box_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    shipment_type = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    shipment_stage = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    shipped_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    delivered_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    shipment_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__SHIPMENT__3213E83FAF3C517F", x => x.id);
                    table.ForeignKey(
                        name: "FK_SHIP_CARRIER",
                        column: x => x.carrier_id,
                        principalTable: "CARRIERS",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_SHIP_DEST_WH",
                        column: x => x.destination_warehouse_id,
                        principalTable: "WAREHOUSES",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_SHIP_MASTER",
                        column: x => x.master_box_id,
                        principalTable: "MASTER_BOXES",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_SHIP_ORDER",
                        column: x => x.consignment_id,
                        principalTable: "ORDERS",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_SHIP_ORIGIN_WH",
                        column: x => x.origin_warehouse_id,
                        principalTable: "WAREHOUSES",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "SHIPMENT_HANDOVERS",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "(newid())"),
                    shipper_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    shipment_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    from_carrier_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    to_carrier_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    handover_location = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    handover_time = table.Column<DateTime>(type: "datetime2", nullable: true),
                    status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__SHIPMENT__3213E83F916BF1B6", x => x.id);
                    table.ForeignKey(
                        name: "FK_HO_FROM_C",
                        column: x => x.from_carrier_id,
                        principalTable: "CARRIERS",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_HO_SHIP",
                        column: x => x.shipment_id,
                        principalTable: "SHIPMENTS",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_HO_SHIPPER",
                        column: x => x.shipper_id,
                        principalTable: "USERS",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_HO_TO_C",
                        column: x => x.to_carrier_id,
                        principalTable: "CARRIERS",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "TRACKINGS",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "(newid())"),
                    shipment_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tracking_status = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    location = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    tracking_stage = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    note = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    tracking_time = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__TRACKING__3213E83F4170C98A", x => x.id);
                    table.ForeignKey(
                        name: "FK_TRACK_SHIP",
                        column: x => x.shipment_id,
                        principalTable: "SHIPMENTS",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_BINS_product_id",
                table: "BINS",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "IX_BINS_shelf_id",
                table: "BINS",
                column: "shelf_id");

            migrationBuilder.CreateIndex(
                name: "IX_BINS_warehouse_id",
                table: "BINS",
                column: "warehouse_id");

            migrationBuilder.CreateIndex(
                name: "IX_COMPANIES_owner_id",
                table: "COMPANIES",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "IX_CUSTOMERS_company_id",
                table: "CUSTOMERS",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_CUSTOMERS_user_id",
                table: "CUSTOMERS",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_CUSTOMS_DECLARATIONS_shipment_id",
                table: "CUSTOMS_DECLARATIONS",
                column: "shipment_id");

            migrationBuilder.CreateIndex(
                name: "IX_CUSTOMS_DOCUMENTS_customs_declaration_id",
                table: "CUSTOMS_DOCUMENTS",
                column: "customs_declaration_id");

            migrationBuilder.CreateIndex(
                name: "IX_CUSTOMS_TAXES_customs_declaration_id",
                table: "CUSTOMS_TAXES",
                column: "customs_declaration_id");

            migrationBuilder.CreateIndex(
                name: "IX_DOMESTIC_WAYBILLS_parcel_id",
                table: "DOMESTIC_WAYBILLS",
                column: "parcel_id");

            migrationBuilder.CreateIndex(
                name: "IX_INVOICE_ITEMS_invoice_id",
                table: "INVOICE_ITEMS",
                column: "invoice_id");

            migrationBuilder.CreateIndex(
                name: "IX_INVOICES_quotation_id",
                table: "INVOICES",
                column: "quotation_id");

            migrationBuilder.CreateIndex(
                name: "IX_MASTER_BOXES_shipment_id",
                table: "MASTER_BOXES",
                column: "shipment_id");

            migrationBuilder.CreateIndex(
                name: "IX_NOTIFICATIONS_user_id",
                table: "NOTIFICATIONS",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_ORDER_ITEMS_company_id",
                table: "ORDER_ITEMS",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_ORDER_ITEMS_order_id",
                table: "ORDER_ITEMS",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "IX_ORDERS_company_id",
                table: "ORDERS",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_ORDERS_customer_id",
                table: "ORDERS",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "IX_PARCELS_consignment_id",
                table: "PARCELS",
                column: "consignment_id");

            migrationBuilder.CreateIndex(
                name: "IX_PARCELS_master_box_id",
                table: "PARCELS",
                column: "master_box_id");

            migrationBuilder.CreateIndex(
                name: "IX_PAYMENTS_invoice_id",
                table: "PAYMENTS",
                column: "invoice_id");

            migrationBuilder.CreateIndex(
                name: "IX_QUOTATIONS_order_id",
                table: "QUOTATIONS",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "IX_QUOTATIONS_wro_id",
                table: "QUOTATIONS",
                column: "wro_id");

            migrationBuilder.CreateIndex(
                name: "IX_SERVICE_PRICINGS_carrier_id",
                table: "SERVICE_PRICINGS",
                column: "carrier_id");

            migrationBuilder.CreateIndex(
                name: "IX_SHELVES_zone_id",
                table: "SHELVES",
                column: "zone_id");

            migrationBuilder.CreateIndex(
                name: "IX_SHIPMENT_HANDOVERS_from_carrier_id",
                table: "SHIPMENT_HANDOVERS",
                column: "from_carrier_id");

            migrationBuilder.CreateIndex(
                name: "IX_SHIPMENT_HANDOVERS_shipment_id",
                table: "SHIPMENT_HANDOVERS",
                column: "shipment_id");

            migrationBuilder.CreateIndex(
                name: "IX_SHIPMENT_HANDOVERS_shipper_id",
                table: "SHIPMENT_HANDOVERS",
                column: "shipper_id");

            migrationBuilder.CreateIndex(
                name: "IX_SHIPMENT_HANDOVERS_to_carrier_id",
                table: "SHIPMENT_HANDOVERS",
                column: "to_carrier_id");

            migrationBuilder.CreateIndex(
                name: "IX_SHIPMENTS_carrier_id",
                table: "SHIPMENTS",
                column: "carrier_id");

            migrationBuilder.CreateIndex(
                name: "IX_SHIPMENTS_consignment_id",
                table: "SHIPMENTS",
                column: "consignment_id");

            migrationBuilder.CreateIndex(
                name: "IX_SHIPMENTS_destination_warehouse_id",
                table: "SHIPMENTS",
                column: "destination_warehouse_id");

            migrationBuilder.CreateIndex(
                name: "IX_SHIPMENTS_master_box_id",
                table: "SHIPMENTS",
                column: "master_box_id");

            migrationBuilder.CreateIndex(
                name: "IX_SHIPMENTS_origin_warehouse_id",
                table: "SHIPMENTS",
                column: "origin_warehouse_id");

            migrationBuilder.CreateIndex(
                name: "IX_SUPPORT_TICKETS_consignment_id",
                table: "SUPPORT_TICKETS",
                column: "consignment_id");

            migrationBuilder.CreateIndex(
                name: "IX_SUPPORT_TICKETS_customer_id",
                table: "SUPPORT_TICKETS",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "IX_TRACKINGS_shipment_id",
                table: "TRACKINGS",
                column: "shipment_id");

            migrationBuilder.CreateIndex(
                name: "IX_WAREHOUSE_ZONES_warehouse_id",
                table: "WAREHOUSE_ZONES",
                column: "warehouse_id");

            migrationBuilder.CreateIndex(
                name: "IX_WRO_REQUESTS_customer_id",
                table: "WRO_REQUESTS",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "IX_WRO_REQUESTS_user_id",
                table: "WRO_REQUESTS",
                column: "user_id");

            migrationBuilder.AddForeignKey(
                name: "FK_CUST_SHIP",
                table: "CUSTOMS_DECLARATIONS",
                column: "shipment_id",
                principalTable: "SHIPMENTS",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "FK_WAYBILL_PARCEL",
                table: "DOMESTIC_WAYBILLS",
                column: "parcel_id",
                principalTable: "PARCELS",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "FK_MASTER_SHIP",
                table: "MASTER_BOXES",
                column: "shipment_id",
                principalTable: "SHIPMENTS",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SHIP_DEST_WH",
                table: "SHIPMENTS");

            migrationBuilder.DropForeignKey(
                name: "FK_SHIP_ORIGIN_WH",
                table: "SHIPMENTS");

            migrationBuilder.DropForeignKey(
                name: "FK_COMP_USER",
                table: "COMPANIES");

            migrationBuilder.DropForeignKey(
                name: "FK_CUSTOMER_USER",
                table: "CUSTOMERS");

            migrationBuilder.DropForeignKey(
                name: "FK_CUSTOMER_COMPANY",
                table: "CUSTOMERS");

            migrationBuilder.DropForeignKey(
                name: "FK_ORDER_COMP",
                table: "ORDERS");

            migrationBuilder.DropForeignKey(
                name: "FK_MASTER_SHIP",
                table: "MASTER_BOXES");

            migrationBuilder.DropTable(
                name: "BINS");

            migrationBuilder.DropTable(
                name: "CUSTOMS_DOCUMENTS");

            migrationBuilder.DropTable(
                name: "CUSTOMS_TAXES");

            migrationBuilder.DropTable(
                name: "DOMESTIC_WAYBILLS");

            migrationBuilder.DropTable(
                name: "INVOICE_ITEMS");

            migrationBuilder.DropTable(
                name: "NOTIFICATIONS");

            migrationBuilder.DropTable(
                name: "PAYMENTS");

            migrationBuilder.DropTable(
                name: "RESTRICTED_ITEMS");

            migrationBuilder.DropTable(
                name: "SERVICE_PRICINGS");

            migrationBuilder.DropTable(
                name: "SHIPMENT_HANDOVERS");

            migrationBuilder.DropTable(
                name: "SUPPORT_TICKETS");

            migrationBuilder.DropTable(
                name: "TRACKINGS");

            migrationBuilder.DropTable(
                name: "ORDER_ITEMS");

            migrationBuilder.DropTable(
                name: "SHELVES");

            migrationBuilder.DropTable(
                name: "CUSTOMS_DECLARATIONS");

            migrationBuilder.DropTable(
                name: "PARCELS");

            migrationBuilder.DropTable(
                name: "INVOICES");

            migrationBuilder.DropTable(
                name: "WAREHOUSE_ZONES");

            migrationBuilder.DropTable(
                name: "QUOTATIONS");

            migrationBuilder.DropTable(
                name: "WRO_REQUESTS");

            migrationBuilder.DropTable(
                name: "WAREHOUSES");

            migrationBuilder.DropTable(
                name: "USERS");

            migrationBuilder.DropTable(
                name: "COMPANIES");

            migrationBuilder.DropTable(
                name: "SHIPMENTS");

            migrationBuilder.DropTable(
                name: "CARRIERS");

            migrationBuilder.DropTable(
                name: "MASTER_BOXES");

            migrationBuilder.DropTable(
                name: "ORDERS");

            migrationBuilder.DropTable(
                name: "CUSTOMERS");
        }
    }
}
