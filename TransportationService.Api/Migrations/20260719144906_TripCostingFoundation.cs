using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class TripCostingFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ConsumptionLPer100Km",
                table: "vehicles",
                type: "numeric(6,1)",
                precision: 6,
                scale: 1,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ActualDistanceKm",
                table: "trips",
                type: "numeric(8,1)",
                precision: 8,
                scale: 1,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ActualEmptyKm",
                table: "trips",
                type: "numeric(8,1)",
                precision: 8,
                scale: 1,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PlannedDistanceKm",
                table: "trips",
                type: "numeric(8,1)",
                precision: 8,
                scale: 1,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PlannedEmptyKm",
                table: "trips",
                type: "numeric(8,1)",
                precision: 8,
                scale: 1,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "cost_rate_sets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    FuelPricePerLitre = table.Column<decimal>(type: "numeric(8,3)", precision: 8, scale: 3, nullable: false),
                    DefaultConsumptionLPer100Km = table.Column<decimal>(type: "numeric(6,1)", precision: 6, scale: 1, nullable: false),
                    VehicleCostPerKm = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false),
                    VehicleCostPerHour = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false),
                    DriverCostPerHour = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false),
                    EmployerCostMultiplier = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    MaintenanceCostPerKm = table.Column<decimal>(type: "numeric(8,3)", precision: 8, scale: 3, nullable: false),
                    DepreciationPerDay = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    TrailerCostPerDay = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    EquipmentCostPerDay = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    DefaultTollPerTrip = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    OvertimeThresholdMinutesPerDay = table.Column<int>(type: "integer", nullable: false),
                    OvertimeRateMultiplier = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    WaitingTimeCostPerHour = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false),
                    Co2KgPerLitreDiesel = table.Column<decimal>(type: "numeric(6,3)", precision: 6, scale: 3, nullable: false),
                    Co2KgPerLitreOther = table.Column<decimal>(type: "numeric(6,3)", precision: 6, scale: 3, nullable: false),
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
                    table.PrimaryKey("PK_cost_rate_sets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "trip_cost_lines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TripId = table.Column<Guid>(type: "uuid", nullable: false),
                    Phase = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CostType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: false),
                    Unit = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    UnitRate = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    Source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    IsManualOverride = table.Column<bool>(type: "boolean", nullable: false),
                    OverrideReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CalculatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
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
                    table.PrimaryKey("PK_trip_cost_lines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_trip_cost_lines_trips_TripId",
                        column: x => x.TripId,
                        principalTable: "trips",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "trip_cost_summaries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TripId = table.Column<Guid>(type: "uuid", nullable: false),
                    EstimatedTotal = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    ActualTotal = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    ProjectedTotal = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    Revenue = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    IsFinalized = table.Column<bool>(type: "boolean", nullable: false),
                    FinalCost = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    FinalRevenue = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    FinalizedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FinalizedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("PK_trip_cost_summaries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_trip_cost_summaries_trips_TripId",
                        column: x => x.TripId,
                        principalTable: "trips",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_cost_rate_sets_TenantId_EffectiveFrom",
                table: "cost_rate_sets",
                columns: new[] { "TenantId", "EffectiveFrom" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_trip_cost_lines_TenantId_TripId_Phase",
                table: "trip_cost_lines",
                columns: new[] { "TenantId", "TripId", "Phase" });

            migrationBuilder.CreateIndex(
                name: "IX_trip_cost_lines_TripId",
                table: "trip_cost_lines",
                column: "TripId");

            migrationBuilder.CreateIndex(
                name: "IX_trip_cost_summaries_TenantId_TripId",
                table: "trip_cost_summaries",
                columns: new[] { "TenantId", "TripId" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_trip_cost_summaries_TripId",
                table: "trip_cost_summaries",
                column: "TripId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cost_rate_sets");

            migrationBuilder.DropTable(
                name: "trip_cost_lines");

            migrationBuilder.DropTable(
                name: "trip_cost_summaries");

            migrationBuilder.DropColumn(
                name: "ConsumptionLPer100Km",
                table: "vehicles");

            migrationBuilder.DropColumn(
                name: "ActualDistanceKm",
                table: "trips");

            migrationBuilder.DropColumn(
                name: "ActualEmptyKm",
                table: "trips");

            migrationBuilder.DropColumn(
                name: "PlannedDistanceKm",
                table: "trips");

            migrationBuilder.DropColumn(
                name: "PlannedEmptyKm",
                table: "trips");
        }
    }
}
