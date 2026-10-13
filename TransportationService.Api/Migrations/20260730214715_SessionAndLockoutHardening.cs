using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class SessionAndLockoutHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FailedLoginCount",
                table: "users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "LockoutEndsAt",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FamilyId",
                table: "refresh_tokens",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_refresh_tokens_UserId_FamilyId",
                table: "refresh_tokens",
                columns: new[] { "UserId", "FamilyId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_refresh_tokens_UserId_FamilyId",
                table: "refresh_tokens");

            migrationBuilder.DropColumn(
                name: "FailedLoginCount",
                table: "users");

            migrationBuilder.DropColumn(
                name: "LockoutEndsAt",
                table: "users");

            migrationBuilder.DropColumn(
                name: "FamilyId",
                table: "refresh_tokens");
        }
    }
}
