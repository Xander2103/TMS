using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class SynchronisedFleetAssignment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The fixed driver↔vehicle relationship moves to a single source of truth on the
            // vehicle (Vehicle.FixedDriverId). Copy the driver-side preference over first —
            // only into vehicles whose slot is free and for drivers not already fixed elsewhere.
            migrationBuilder.Sql("""
                UPDATE vehicles v
                SET "FixedDriverId" = d."Id"
                FROM drivers d
                WHERE d."DefaultVehicleId" = v."Id"
                  AND d."TenantId" = v."TenantId"
                  AND d."IsDeleted" = false
                  AND v."IsDeleted" = false
                  AND v."FixedDriverId" IS NULL
                  AND NOT EXISTS (
                      SELECT 1 FROM vehicles v2
                      WHERE v2."TenantId" = v."TenantId"
                        AND v2."FixedDriverId" = d."Id"
                        AND v2."IsDeleted" = false);
                """);

            // Defensive de-duplication so the new one-vehicle-per-driver unique indexes can
            // always be created; the first vehicle (by id) keeps the assignment.
            migrationBuilder.Sql("""
                UPDATE vehicles v SET "FixedDriverId" = NULL
                WHERE v."FixedDriverId" IS NOT NULL AND v."IsDeleted" = false AND EXISTS (
                    SELECT 1 FROM vehicles v2
                    WHERE v2."TenantId" = v."TenantId"
                      AND v2."FixedDriverId" = v."FixedDriverId"
                      AND v2."IsDeleted" = false
                      AND v2."Id" < v."Id");
                """);
            migrationBuilder.Sql("""
                UPDATE vehicles v SET "CurrentDriverId" = NULL
                WHERE v."CurrentDriverId" IS NOT NULL AND v."IsDeleted" = false AND EXISTS (
                    SELECT 1 FROM vehicles v2
                    WHERE v2."TenantId" = v."TenantId"
                      AND v2."CurrentDriverId" = v."CurrentDriverId"
                      AND v2."IsDeleted" = false
                      AND v2."Id" < v."Id");
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_drivers_trailers_DefaultTrailerId",
                table: "drivers");

            migrationBuilder.DropForeignKey(
                name: "FK_drivers_vehicles_DefaultVehicleId",
                table: "drivers");

            migrationBuilder.DropForeignKey(
                name: "FK_drivers_vehicles_PreferredVehicleId",
                table: "drivers");

            migrationBuilder.DropIndex(
                name: "IX_drivers_DefaultVehicleId",
                table: "drivers");

            migrationBuilder.DropIndex(
                name: "IX_drivers_PreferredVehicleId",
                table: "drivers");

            // The trailer preference keeps its data under a clearer name (DefaultTrailerId →
            // FixedTrailerId); the vehicle-side columns disappear entirely.
            migrationBuilder.RenameColumn(
                name: "DefaultTrailerId",
                table: "drivers",
                newName: "FixedTrailerId");

            migrationBuilder.RenameIndex(
                name: "IX_drivers_DefaultTrailerId",
                table: "drivers",
                newName: "IX_drivers_FixedTrailerId");

            migrationBuilder.DropColumn(
                name: "DefaultVehicleId",
                table: "drivers");

            migrationBuilder.DropColumn(
                name: "PreferredVehicleId",
                table: "drivers");

            migrationBuilder.DropColumn(
                name: "FixedVehiclePreference",
                table: "drivers");

            migrationBuilder.CreateIndex(
                name: "IX_vehicles_TenantId_CurrentDriverId",
                table: "vehicles",
                columns: new[] { "TenantId", "CurrentDriverId" },
                unique: true,
                filter: "\"CurrentDriverId\" IS NOT NULL AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_vehicles_TenantId_FixedDriverId",
                table: "vehicles",
                columns: new[] { "TenantId", "FixedDriverId" },
                unique: true,
                filter: "\"FixedDriverId\" IS NOT NULL AND \"IsDeleted\" = false");

            migrationBuilder.AddForeignKey(
                name: "FK_drivers_trailers_FixedTrailerId",
                table: "drivers",
                column: "FixedTrailerId",
                principalTable: "trailers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_drivers_trailers_FixedTrailerId",
                table: "drivers");

            migrationBuilder.DropIndex(
                name: "IX_vehicles_TenantId_CurrentDriverId",
                table: "vehicles");

            migrationBuilder.DropIndex(
                name: "IX_vehicles_TenantId_FixedDriverId",
                table: "vehicles");

            migrationBuilder.RenameColumn(
                name: "FixedTrailerId",
                table: "drivers",
                newName: "DefaultTrailerId");

            migrationBuilder.RenameIndex(
                name: "IX_drivers_FixedTrailerId",
                table: "drivers",
                newName: "IX_drivers_DefaultTrailerId");

            migrationBuilder.AddColumn<Guid>(
                name: "DefaultVehicleId",
                table: "drivers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PreferredVehicleId",
                table: "drivers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "FixedVehiclePreference",
                table: "drivers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Best-effort reverse data copy: vehicle-side fixed driver back to the driver row.
            migrationBuilder.Sql("""
                UPDATE drivers d
                SET "DefaultVehicleId" = v."Id"
                FROM vehicles v
                WHERE v."FixedDriverId" = d."Id" AND v."TenantId" = d."TenantId";
                """);

            migrationBuilder.CreateIndex(
                name: "IX_drivers_DefaultVehicleId",
                table: "drivers",
                column: "DefaultVehicleId");

            migrationBuilder.CreateIndex(
                name: "IX_drivers_PreferredVehicleId",
                table: "drivers",
                column: "PreferredVehicleId");

            migrationBuilder.AddForeignKey(
                name: "FK_drivers_trailers_DefaultTrailerId",
                table: "drivers",
                column: "DefaultTrailerId",
                principalTable: "trailers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_drivers_vehicles_DefaultVehicleId",
                table: "drivers",
                column: "DefaultVehicleId",
                principalTable: "vehicles",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_drivers_vehicles_PreferredVehicleId",
                table: "drivers",
                column: "PreferredVehicleId",
                principalTable: "vehicles",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
