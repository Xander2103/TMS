using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class DossierConfirmationLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CancellationReason",
                table: "transport_dossiers",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CancelledAt",
                table: "transport_dossiers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CancelledByUserId",
                table: "transport_dossiers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConfirmationReason",
                table: "transport_dossiers",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConfirmationSource",
                table: "transport_dossiers",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ConfirmedByUserId",
                table: "transport_dossiers",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_transport_dossiers_TenantId_ClosedAt",
                table: "transport_dossiers",
                columns: new[] { "TenantId", "ClosedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_transport_dossiers_TenantId_CreatedAt",
                table: "transport_dossiers",
                columns: new[] { "TenantId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_transport_dossiers_TenantId_DossierDate",
                table: "transport_dossiers",
                columns: new[] { "TenantId", "DossierDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_transport_dossiers_TenantId_ClosedAt",
                table: "transport_dossiers");

            migrationBuilder.DropIndex(
                name: "IX_transport_dossiers_TenantId_CreatedAt",
                table: "transport_dossiers");

            migrationBuilder.DropIndex(
                name: "IX_transport_dossiers_TenantId_DossierDate",
                table: "transport_dossiers");

            migrationBuilder.DropColumn(
                name: "CancellationReason",
                table: "transport_dossiers");

            migrationBuilder.DropColumn(
                name: "CancelledAt",
                table: "transport_dossiers");

            migrationBuilder.DropColumn(
                name: "CancelledByUserId",
                table: "transport_dossiers");

            migrationBuilder.DropColumn(
                name: "ConfirmationReason",
                table: "transport_dossiers");

            migrationBuilder.DropColumn(
                name: "ConfirmationSource",
                table: "transport_dossiers");

            migrationBuilder.DropColumn(
                name: "ConfirmedByUserId",
                table: "transport_dossiers");
        }
    }
}
