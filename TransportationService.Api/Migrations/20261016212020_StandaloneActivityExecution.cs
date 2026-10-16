using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class StandaloneActivityExecution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CancellationReason",
                table: "dossier_activities",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CancelledAt",
                table: "dossier_activities",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CancelledByUserId",
                table: "dossier_activities",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CompletedAt",
                table: "dossier_activities",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CompletedByUserId",
                table: "dossier_activities",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DriverId",
                table: "dossier_activities",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExecutionStatus",
                table: "dossier_activities",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Planned");

            migrationBuilder.AddColumn<string>(
                name: "SiteAddress",
                table: "dossier_activities",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "StartTime",
                table: "dossier_activities",
                type: "time without time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "StartedAt",
                table: "dossier_activities",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "VehicleId",
                table: "dossier_activities",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WorkDescription",
                table: "dossier_activities",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RequiresCrane",
                table: "activity_types",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // T-H7 backfill. Before this migration a planning-relevant standalone activity counted as
            // executed once its planned date was today or earlier (DossierLifecycleService); those rows
            // become Completed on that date, so auto-confirmation, the dossier counts and invoicing keep
            // reading them as executed. Every standalone activity of an already CONFIRMED dossier is
            // executed by definition. Everything else starts Planned (the column default).
            migrationBuilder.Sql("""
                UPDATE dossier_activities AS a
                SET "ExecutionStatus" = 'Completed',
                    "CompletedAt" = COALESCE(a."PlannedDate"::timestamp, d."ClosedAt", a."UpdatedAt")
                FROM activity_types AS t, transport_dossiers AS d
                WHERE t."Id" = a."ActivityTypeId"
                  AND d."Id" = a."DossierId"
                  AND a."LinkedTransportOrderId" IS NULL
                  AND t."HasStops" = false
                  AND (
                        (t."PlanningRelevant" = true AND a."PlannedDate" IS NOT NULL AND a."PlannedDate" <= CURRENT_DATE)
                     OR d."Status" = 'Closed'
                  );
                """);

            // Mirrors the seeder default for tenants seeded before the flag existed (the seeder is the
            // only other place that knows this code; domain logic reads the flag).
            migrationBuilder.Sql("""UPDATE activity_types SET "RequiresCrane" = true WHERE "Code" = 'KRAANWERK';""");

            migrationBuilder.CreateIndex(
                name: "IX_dossier_activities_DriverId",
                table: "dossier_activities",
                column: "DriverId");

            migrationBuilder.CreateIndex(
                name: "IX_dossier_activities_VehicleId",
                table: "dossier_activities",
                column: "VehicleId");

            migrationBuilder.AddForeignKey(
                name: "FK_dossier_activities_drivers_DriverId",
                table: "dossier_activities",
                column: "DriverId",
                principalTable: "drivers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_dossier_activities_vehicles_VehicleId",
                table: "dossier_activities",
                column: "VehicleId",
                principalTable: "vehicles",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_dossier_activities_drivers_DriverId",
                table: "dossier_activities");

            migrationBuilder.DropForeignKey(
                name: "FK_dossier_activities_vehicles_VehicleId",
                table: "dossier_activities");

            migrationBuilder.DropIndex(
                name: "IX_dossier_activities_DriverId",
                table: "dossier_activities");

            migrationBuilder.DropIndex(
                name: "IX_dossier_activities_VehicleId",
                table: "dossier_activities");

            migrationBuilder.DropColumn(
                name: "CancellationReason",
                table: "dossier_activities");

            migrationBuilder.DropColumn(
                name: "CancelledAt",
                table: "dossier_activities");

            migrationBuilder.DropColumn(
                name: "CancelledByUserId",
                table: "dossier_activities");

            migrationBuilder.DropColumn(
                name: "CompletedAt",
                table: "dossier_activities");

            migrationBuilder.DropColumn(
                name: "CompletedByUserId",
                table: "dossier_activities");

            migrationBuilder.DropColumn(
                name: "DriverId",
                table: "dossier_activities");

            migrationBuilder.DropColumn(
                name: "ExecutionStatus",
                table: "dossier_activities");

            migrationBuilder.DropColumn(
                name: "SiteAddress",
                table: "dossier_activities");

            migrationBuilder.DropColumn(
                name: "StartTime",
                table: "dossier_activities");

            migrationBuilder.DropColumn(
                name: "StartedAt",
                table: "dossier_activities");

            migrationBuilder.DropColumn(
                name: "VehicleId",
                table: "dossier_activities");

            migrationBuilder.DropColumn(
                name: "WorkDescription",
                table: "dossier_activities");

            migrationBuilder.DropColumn(
                name: "RequiresCrane",
                table: "activity_types");
        }
    }
}
