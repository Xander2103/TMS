using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class ExceptionManagementRemediation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "RedeliveryOfOrderId",
                table: "transport_orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RedeliverySourceIncidentId",
                table: "transport_orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomerMessage",
                table: "incidents",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FleetNotifiedAt",
                table: "incidents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ResolvedByUserId",
                table: "incidents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SourceExceptionId",
                table: "incidents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomerMessage",
                table: "execution_exceptions",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "EscalatedIncidentId",
                table: "execution_exceptions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FleetNotifiedAt",
                table: "execution_exceptions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SourceExceptionId",
                table: "damage_reports",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SourceIncidentId",
                table: "damage_reports",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_transport_orders_TenantId_RedeliveryOfOrderId",
                table: "transport_orders",
                columns: new[] { "TenantId", "RedeliveryOfOrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_incidents_TenantId_SourceExceptionId",
                table: "incidents",
                columns: new[] { "TenantId", "SourceExceptionId" },
                unique: true,
                filter: "\"SourceExceptionId\" IS NOT NULL AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_incidents_TenantId_TripId",
                table: "incidents",
                columns: new[] { "TenantId", "TripId" });

            migrationBuilder.CreateIndex(
                name: "IX_incidents_TenantId_VehicleId",
                table: "incidents",
                columns: new[] { "TenantId", "VehicleId" });

            migrationBuilder.CreateIndex(
                name: "IX_execution_exceptions_TenantId_TransportOrderId",
                table: "execution_exceptions",
                columns: new[] { "TenantId", "TransportOrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_damage_reports_TenantId_SourceExceptionId",
                table: "damage_reports",
                columns: new[] { "TenantId", "SourceExceptionId" },
                unique: true,
                filter: "\"SourceExceptionId\" IS NOT NULL AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_damage_reports_TenantId_SourceIncidentId",
                table: "damage_reports",
                columns: new[] { "TenantId", "SourceIncidentId" },
                unique: true,
                filter: "\"SourceIncidentId\" IS NOT NULL AND \"IsDeleted\" = false");

            // Legacy backfill: redeliveries created before this release only had the incident's forward
            // link — give them their backlink to the original order and the causing incident.
            migrationBuilder.Sql(@"
UPDATE transport_orders o
SET ""RedeliveryOfOrderId"" = i.""TransportOrderId"", ""RedeliverySourceIncidentId"" = i.""Id""
FROM incidents i
WHERE i.""LinkedRedeliveryOrderId"" = o.""Id"" AND i.""TenantId"" = o.""TenantId""
  AND o.""RedeliveryOfOrderId"" IS NULL AND i.""TransportOrderId"" IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_transport_orders_TenantId_RedeliveryOfOrderId",
                table: "transport_orders");

            migrationBuilder.DropIndex(
                name: "IX_incidents_TenantId_SourceExceptionId",
                table: "incidents");

            migrationBuilder.DropIndex(
                name: "IX_incidents_TenantId_TripId",
                table: "incidents");

            migrationBuilder.DropIndex(
                name: "IX_incidents_TenantId_VehicleId",
                table: "incidents");

            migrationBuilder.DropIndex(
                name: "IX_execution_exceptions_TenantId_TransportOrderId",
                table: "execution_exceptions");

            migrationBuilder.DropIndex(
                name: "IX_damage_reports_TenantId_SourceExceptionId",
                table: "damage_reports");

            migrationBuilder.DropIndex(
                name: "IX_damage_reports_TenantId_SourceIncidentId",
                table: "damage_reports");

            migrationBuilder.DropColumn(
                name: "RedeliveryOfOrderId",
                table: "transport_orders");

            migrationBuilder.DropColumn(
                name: "RedeliverySourceIncidentId",
                table: "transport_orders");

            migrationBuilder.DropColumn(
                name: "CustomerMessage",
                table: "incidents");

            migrationBuilder.DropColumn(
                name: "FleetNotifiedAt",
                table: "incidents");

            migrationBuilder.DropColumn(
                name: "ResolvedByUserId",
                table: "incidents");

            migrationBuilder.DropColumn(
                name: "SourceExceptionId",
                table: "incidents");

            migrationBuilder.DropColumn(
                name: "CustomerMessage",
                table: "execution_exceptions");

            migrationBuilder.DropColumn(
                name: "EscalatedIncidentId",
                table: "execution_exceptions");

            migrationBuilder.DropColumn(
                name: "FleetNotifiedAt",
                table: "execution_exceptions");

            migrationBuilder.DropColumn(
                name: "SourceExceptionId",
                table: "damage_reports");

            migrationBuilder.DropColumn(
                name: "SourceIncidentId",
                table: "damage_reports");
        }
    }
}
