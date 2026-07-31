using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class NotificationLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AcknowledgedAt",
                table: "notifications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DedupeKey",
                table: "notifications",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiresAt",
                table: "notifications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RequiresAcknowledgement",
                table: "notifications",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "ResolvedAt",
                table: "notifications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_notifications_TenantId_DedupeKey",
                table: "notifications",
                columns: new[] { "TenantId", "DedupeKey" });

            migrationBuilder.CreateIndex(
                name: "IX_notifications_TenantId_ExpiresAt",
                table: "notifications",
                columns: new[] { "TenantId", "ExpiresAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_notifications_TenantId_DedupeKey",
                table: "notifications");

            migrationBuilder.DropIndex(
                name: "IX_notifications_TenantId_ExpiresAt",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "AcknowledgedAt",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "DedupeKey",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "ExpiresAt",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "RequiresAcknowledgement",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "ResolvedAt",
                table: "notifications");
        }
    }
}
