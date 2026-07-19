using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class PersonnelPlanningSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "trip_planning_entries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TripId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    DriverId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    TripNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    PlannedStart = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PlannedEnd = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ActualStart = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ActualEnd = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    VehicleSummary = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    RouteSummary = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
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
                    table.PrimaryKey("PK_trip_planning_entries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_trip_planning_entries_TenantId_Date",
                table: "trip_planning_entries",
                columns: new[] { "TenantId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_trip_planning_entries_TenantId_EmployeeId_Date",
                table: "trip_planning_entries",
                columns: new[] { "TenantId", "EmployeeId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_trip_planning_entries_TenantId_TripId",
                table: "trip_planning_entries",
                columns: new[] { "TenantId", "TripId" },
                unique: true,
                filter: "\"IsDeleted\" = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "trip_planning_entries");
        }
    }
}
