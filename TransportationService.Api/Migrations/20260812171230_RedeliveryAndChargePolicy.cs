using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class RedeliveryAndChargePolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RedeliveryMode",
                table: "tenant_settings",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "RedeliverySuggested",
                table: "incidents",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "SourceStopId",
                table: "incidents",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "incident_charge_policies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: true),
                    IncidentType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    Mode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DefaultAmount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    DefaultDescription = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_incident_charge_policies", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_incidents_TenantId_SourceStopId",
                table: "incidents",
                columns: new[] { "TenantId", "SourceStopId" },
                unique: true,
                filter: "\"SourceStopId\" IS NOT NULL AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_incident_charge_policies_TenantId_CustomerId_IncidentType",
                table: "incident_charge_policies",
                columns: new[] { "TenantId", "CustomerId", "IncidentType" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "incident_charge_policies");

            migrationBuilder.DropIndex(
                name: "IX_incidents_TenantId_SourceStopId",
                table: "incidents");

            migrationBuilder.DropColumn(
                name: "RedeliveryMode",
                table: "tenant_settings");

            migrationBuilder.DropColumn(
                name: "RedeliverySuggested",
                table: "incidents");

            migrationBuilder.DropColumn(
                name: "SourceStopId",
                table: "incidents");
        }
    }
}
