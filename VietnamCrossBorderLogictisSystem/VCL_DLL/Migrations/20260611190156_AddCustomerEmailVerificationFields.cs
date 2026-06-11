using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VCL_DLL.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerEmailVerificationFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "email_verification_failed_attempts",
                table: "USERS",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "email_verification_otp_expiry",
                table: "USERS",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "email_verification_otp_hash",
                table: "USERS",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_email_verified",
                table: "USERS",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "last_otp_sent_at",
                table: "USERS",
                type: "datetime2",
                nullable: true);

            migrationBuilder.Sql(
                "UPDATE USERS SET is_email_verified = 1 WHERE status = 'Active'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "email_verification_failed_attempts",
                table: "USERS");

            migrationBuilder.DropColumn(
                name: "email_verification_otp_expiry",
                table: "USERS");

            migrationBuilder.DropColumn(
                name: "email_verification_otp_hash",
                table: "USERS");

            migrationBuilder.DropColumn(
                name: "is_email_verified",
                table: "USERS");

            migrationBuilder.DropColumn(
                name: "last_otp_sent_at",
                table: "USERS");
        }
    }
}
