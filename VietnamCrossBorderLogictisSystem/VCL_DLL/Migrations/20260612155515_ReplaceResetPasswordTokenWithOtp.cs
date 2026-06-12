using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VCL_DLL.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceResetPasswordTokenWithOtp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ResetPasswordToken",
                table: "USERS",
                newName: "ResetPasswordOtpHash");

            migrationBuilder.RenameColumn(
                name: "ResetPasswordExpiry",
                table: "USERS",
                newName: "ResetPasswordOtpExpiry");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastResetOtpSentAt",
                table: "USERS",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ResetPasswordFailedAttempts",
                table: "USERS",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastResetOtpSentAt",
                table: "USERS");

            migrationBuilder.DropColumn(
                name: "ResetPasswordFailedAttempts",
                table: "USERS");

            migrationBuilder.RenameColumn(
                name: "ResetPasswordOtpHash",
                table: "USERS",
                newName: "ResetPasswordToken");

            migrationBuilder.RenameColumn(
                name: "ResetPasswordOtpExpiry",
                table: "USERS",
                newName: "ResetPasswordExpiry");
        }
    }
}
