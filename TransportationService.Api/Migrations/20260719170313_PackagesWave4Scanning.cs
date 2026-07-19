using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class PackagesWave4Scanning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PackageDepartureRule",
                table: "tenant_settings",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "AllowWithWarning");

            migrationBuilder.AddColumn<Guid>(
                name: "ClientEventId",
                table: "scan_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PackageId",
                table: "scan_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PackageOutcome",
                table: "scan_events",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PackageId",
                table: "execution_exceptions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_scan_events_PackageId",
                table: "scan_events",
                column: "PackageId");

            migrationBuilder.CreateIndex(
                name: "IX_scan_events_TenantId_ClientEventId",
                table: "scan_events",
                columns: new[] { "TenantId", "ClientEventId" },
                unique: true,
                filter: "\"ClientEventId\" IS NOT NULL AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_execution_exceptions_PackageId",
                table: "execution_exceptions",
                column: "PackageId");

            migrationBuilder.AddForeignKey(
                name: "FK_execution_exceptions_packages_PackageId",
                table: "execution_exceptions",
                column: "PackageId",
                principalTable: "packages",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_scan_events_packages_PackageId",
                table: "scan_events",
                column: "PackageId",
                principalTable: "packages",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_execution_exceptions_packages_PackageId",
                table: "execution_exceptions");

            migrationBuilder.DropForeignKey(
                name: "FK_scan_events_packages_PackageId",
                table: "scan_events");

            migrationBuilder.DropIndex(
                name: "IX_scan_events_PackageId",
                table: "scan_events");

            migrationBuilder.DropIndex(
                name: "IX_scan_events_TenantId_ClientEventId",
                table: "scan_events");

            migrationBuilder.DropIndex(
                name: "IX_execution_exceptions_PackageId",
                table: "execution_exceptions");

            migrationBuilder.DropColumn(
                name: "PackageDepartureRule",
                table: "tenant_settings");

            migrationBuilder.DropColumn(
                name: "ClientEventId",
                table: "scan_events");

            migrationBuilder.DropColumn(
                name: "PackageId",
                table: "scan_events");

            migrationBuilder.DropColumn(
                name: "PackageOutcome",
                table: "scan_events");

            migrationBuilder.DropColumn(
                name: "PackageId",
                table: "execution_exceptions");
        }
    }
}
