using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <summary>
    /// Phase 7 (master-data wave 2026-08-05): order stops get a frozen snapshot of their master
    /// location. Adds the snapshot columns and backfills existing master-location stops with the
    /// CURRENT location values — exactly what the live join rendered until now, so the change is
    /// display-identical — after which master edits no longer leak into historical orders.
    ///
    /// Notes on the backfill:
    /// - The address quintet and instruction fields are only filled where the stop's own value
    ///   IS NULL (free-address data and user-entered instructions always win).
    /// - AppointmentRequired absorbs the location flag (the read path used to OR it in live).
    /// - OpeningHoursSummary is intentionally left NULL: summaries regenerate on the next
    ///   snapshot (create/location change/expliciet opnieuw overnemen); until then the UI simply
    ///   shows no hours line, matching the legacy free-text-only behaviour.
    /// - Raw SQL is PostgreSQL-only, guarded like AuditAppendOnly (the SQLite test harness uses
    ///   EnsureCreated and never runs migrations).
    /// </summary>
    public partial class StopLocationSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AccessCode",
                table: "transport_order_stops",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContactEmail",
                table: "transport_order_stops",
                type: "character varying(250)",
                maxLength: 250,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContactMobile",
                table: "transport_order_stops",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContactName",
                table: "transport_order_stops",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContactPhone",
                table: "transport_order_stops",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DefaultLoadingMinutes",
                table: "transport_order_stops",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DefaultUnloadingMinutes",
                table: "transport_order_stops",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Dock",
                table: "transport_order_stops",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Gate",
                table: "transport_order_stops",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OpeningHoursSummary",
                table: "transport_order_stops",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RouteDescription",
                table: "transport_order_stops",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SnapshotAt",
                table: "transport_order_stops",
                type: "timestamp with time zone",
                nullable: true);

            if (migrationBuilder.ActiveProvider?.Contains("Npgsql") != true)
            {
                return;
            }

            migrationBuilder.Sql(@"
                UPDATE transport_order_stops s SET
                    ""ContactName"" = l.""ContactName"",
                    ""ContactPhone"" = l.""ContactPhone"",
                    ""ContactMobile"" = l.""ContactMobile"",
                    ""ContactEmail"" = l.""ContactEmail"",
                    ""Gate"" = l.""Gate"",
                    ""AccessCode"" = l.""AccessCode"",
                    ""Dock"" = l.""Dock"",
                    ""RouteDescription"" = l.""RouteDescription"",
                    ""DefaultLoadingMinutes"" = l.""DefaultLoadingMinutes"",
                    ""DefaultUnloadingMinutes"" = l.""DefaultUnloadingMinutes"",
                    ""SnapshotAt"" = now() AT TIME ZONE 'utc',
                    ""LocationName"" = COALESCE(s.""LocationName"", l.""Name""),
                    ""Address"" = COALESCE(s.""Address"",
                        NULLIF(TRIM(CONCAT(COALESCE(l.""Street"", ''), ' ', COALESCE(l.""HouseNumber"", ''))), '')),
                    ""PostalCode"" = COALESCE(s.""PostalCode"", l.""PostalCode""),
                    ""City"" = COALESCE(s.""City"", l.""City""),
                    ""CountryCode"" = COALESCE(s.""CountryCode"", l.""CountryCode""),
                    ""Instructions"" = COALESCE(s.""Instructions"", l.""DriverInstructions""),
                    ""AccessInstructions"" = COALESCE(s.""AccessInstructions"", l.""AccessInstructions""),
                    ""LoadingInstructions"" = COALESCE(s.""LoadingInstructions"", l.""LoadingInstructions""),
                    ""UnloadingInstructions"" = COALESCE(s.""UnloadingInstructions"", l.""UnloadingInstructions""),
                    ""AppointmentRequired"" = s.""AppointmentRequired"" OR l.""AppointmentRequired""
                FROM locations l
                WHERE s.""LocationId"" = l.""Id"" AND s.""TenantId"" = l.""TenantId"";");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AccessCode",
                table: "transport_order_stops");

            migrationBuilder.DropColumn(
                name: "ContactEmail",
                table: "transport_order_stops");

            migrationBuilder.DropColumn(
                name: "ContactMobile",
                table: "transport_order_stops");

            migrationBuilder.DropColumn(
                name: "ContactName",
                table: "transport_order_stops");

            migrationBuilder.DropColumn(
                name: "ContactPhone",
                table: "transport_order_stops");

            migrationBuilder.DropColumn(
                name: "DefaultLoadingMinutes",
                table: "transport_order_stops");

            migrationBuilder.DropColumn(
                name: "DefaultUnloadingMinutes",
                table: "transport_order_stops");

            migrationBuilder.DropColumn(
                name: "Dock",
                table: "transport_order_stops");

            migrationBuilder.DropColumn(
                name: "Gate",
                table: "transport_order_stops");

            migrationBuilder.DropColumn(
                name: "OpeningHoursSummary",
                table: "transport_order_stops");

            migrationBuilder.DropColumn(
                name: "RouteDescription",
                table: "transport_order_stops");

            migrationBuilder.DropColumn(
                name: "SnapshotAt",
                table: "transport_order_stops");
        }
    }
}
