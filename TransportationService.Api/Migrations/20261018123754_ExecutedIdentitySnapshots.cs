using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class ExecutedIdentitySnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExecutedDriverName",
                table: "trips",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExecutedTrailerNumber",
                table: "trips",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExecutedTrailerPlate",
                table: "trips",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExecutedVehicleNumber",
                table: "trips",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExecutedVehiclePlate",
                table: "trips",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ExecutionSnapshotAt",
                table: "trips",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomerNameAtCompletion",
                table: "transport_orders",
                type: "text",
                nullable: true);

            // MD-M6 legacy policy: trips / orders completed BEFORE this release freeze their identity
            // ONCE from the master data as it stands at deployment — the only source left (no older
            // plate or name can be reconstructed). From then on they never change again.
            migrationBuilder.Sql("""
                UPDATE trips t SET
                    "ExecutedVehicleNumber" = v."InternalNumber", "ExecutedVehiclePlate" = v."LicensePlate"
                FROM vehicles v
                WHERE t."Status" = 'Completed' AND t."ExecutionSnapshotAt" IS NULL AND v."Id" = t."VehicleId";
                UPDATE trips t SET
                    "ExecutedTrailerNumber" = tr."InternalNumber", "ExecutedTrailerPlate" = tr."LicensePlate"
                FROM trailers tr
                WHERE t."Status" = 'Completed' AND t."ExecutionSnapshotAt" IS NULL AND tr."Id" = t."TrailerId";
                UPDATE trips t SET "ExecutedDriverName" = btrim(e."FirstName" || ' ' || e."LastName")
                FROM drivers d JOIN employees e ON e."Id" = d."EmployeeId"
                WHERE t."Status" = 'Completed' AND t."ExecutionSnapshotAt" IS NULL AND d."Id" = t."DriverId";
                UPDATE trips SET "ExecutionSnapshotAt" = now() WHERE "Status" = 'Completed' AND "ExecutionSnapshotAt" IS NULL;
                UPDATE transport_orders o SET "CustomerNameAtCompletion" = c."Name"
                FROM customers c
                WHERE o."Status" IN ('Completed', 'Invoiced') AND o."CustomerNameAtCompletion" IS NULL AND c."Id" = o."CustomerId";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExecutedDriverName",
                table: "trips");

            migrationBuilder.DropColumn(
                name: "ExecutedTrailerNumber",
                table: "trips");

            migrationBuilder.DropColumn(
                name: "ExecutedTrailerPlate",
                table: "trips");

            migrationBuilder.DropColumn(
                name: "ExecutedVehicleNumber",
                table: "trips");

            migrationBuilder.DropColumn(
                name: "ExecutedVehiclePlate",
                table: "trips");

            migrationBuilder.DropColumn(
                name: "ExecutionSnapshotAt",
                table: "trips");

            migrationBuilder.DropColumn(
                name: "CustomerNameAtCompletion",
                table: "transport_orders");
        }
    }
}
