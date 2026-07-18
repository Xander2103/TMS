using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddVehicles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "VehicleNumberNextValue",
                table: "tenant_settings",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "VehicleNumberPrefix",
                table: "tenant_settings",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "vehicles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InternalNumber = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    LicensePlate = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Vin = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: true),
                    Brand = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Year = table.Column<int>(type: "integer", nullable: true),
                    FirstRegistrationDate = table.Column<DateOnly>(type: "date", nullable: true),
                    FuelType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    EmissionClass = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    GrossVehicleWeightKg = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    PayloadKg = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    LengthMeters = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: true),
                    WidthMeters = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: true),
                    HeightMeters = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: true),
                    VolumeM3 = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: true),
                    OdometerKm = table.Column<int>(type: "integer", nullable: false),
                    HasCrane = table.Column<bool>(type: "boolean", nullable: false),
                    HasRefrigeration = table.Column<bool>(type: "boolean", nullable: false),
                    HasTailLift = table.Column<bool>(type: "boolean", nullable: false),
                    AdrSuitable = table.Column<bool>(type: "boolean", nullable: false),
                    OwnershipType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    OperationalStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    FixedDriverId = table.Column<Guid>(type: "uuid", nullable: true),
                    CurrentDriverId = table.Column<Guid>(type: "uuid", nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
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
                    table.PrimaryKey("PK_vehicles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_vehicles_drivers_CurrentDriverId",
                        column: x => x.CurrentDriverId,
                        principalTable: "drivers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_vehicles_drivers_FixedDriverId",
                        column: x => x.FixedDriverId,
                        principalTable: "drivers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_drivers_DefaultVehicleId",
                table: "drivers",
                column: "DefaultVehicleId");

            migrationBuilder.CreateIndex(
                name: "IX_drivers_PreferredVehicleId",
                table: "drivers",
                column: "PreferredVehicleId");

            migrationBuilder.CreateIndex(
                name: "IX_vehicles_CurrentDriverId",
                table: "vehicles",
                column: "CurrentDriverId");

            migrationBuilder.CreateIndex(
                name: "IX_vehicles_FixedDriverId",
                table: "vehicles",
                column: "FixedDriverId");

            migrationBuilder.CreateIndex(
                name: "IX_vehicles_TenantId",
                table: "vehicles",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_vehicles_TenantId_InternalNumber",
                table: "vehicles",
                columns: new[] { "TenantId", "InternalNumber" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_vehicles_TenantId_IsActive",
                table: "vehicles",
                columns: new[] { "TenantId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_vehicles_TenantId_LicensePlate",
                table: "vehicles",
                columns: new[] { "TenantId", "LicensePlate" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_vehicles_TenantId_OperationalStatus",
                table: "vehicles",
                columns: new[] { "TenantId", "OperationalStatus" });

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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_drivers_vehicles_DefaultVehicleId",
                table: "drivers");

            migrationBuilder.DropForeignKey(
                name: "FK_drivers_vehicles_PreferredVehicleId",
                table: "drivers");

            migrationBuilder.DropTable(
                name: "vehicles");

            migrationBuilder.DropIndex(
                name: "IX_drivers_DefaultVehicleId",
                table: "drivers");

            migrationBuilder.DropIndex(
                name: "IX_drivers_PreferredVehicleId",
                table: "drivers");

            migrationBuilder.DropColumn(
                name: "VehicleNumberNextValue",
                table: "tenant_settings");

            migrationBuilder.DropColumn(
                name: "VehicleNumberPrefix",
                table: "tenant_settings");
        }
    }
}
