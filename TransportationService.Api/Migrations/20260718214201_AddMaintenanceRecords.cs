using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddMaintenanceRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "maintenance_records",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VehicleId = table.Column<Guid>(type: "uuid", nullable: true),
                    TrailerId = table.Column<Guid>(type: "uuid", nullable: true),
                    MaintenanceType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    CustomTypeName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ScheduledDate = table.Column<DateOnly>(type: "date", nullable: true),
                    OdometerTriggerKm = table.Column<int>(type: "integer", nullable: true),
                    CompletedDate = table.Column<DateOnly>(type: "date", nullable: true),
                    CompletedOdometerKm = table.Column<int>(type: "integer", nullable: true),
                    WorkPerformed = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Provider = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Cost = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    NextServiceDate = table.Column<DateOnly>(type: "date", nullable: true),
                    NextServiceOdometerKm = table.Column<int>(type: "integer", nullable: true),
                    IntervalMonths = table.Column<int>(type: "integer", nullable: true),
                    IntervalKm = table.Column<int>(type: "integer", nullable: true),
                    AttachmentPath = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
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
                    table.PrimaryKey("PK_maintenance_records", x => x.Id);
                    table.CheckConstraint("CK_maintenance_records_single_owner", "(\"VehicleId\" IS NULL) <> (\"TrailerId\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_maintenance_records_trailers_TrailerId",
                        column: x => x.TrailerId,
                        principalTable: "trailers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_maintenance_records_vehicles_VehicleId",
                        column: x => x.VehicleId,
                        principalTable: "vehicles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_records_TenantId_Status_ScheduledDate",
                table: "maintenance_records",
                columns: new[] { "TenantId", "Status", "ScheduledDate" });

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_records_TenantId_TrailerId",
                table: "maintenance_records",
                columns: new[] { "TenantId", "TrailerId" });

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_records_TenantId_VehicleId",
                table: "maintenance_records",
                columns: new[] { "TenantId", "VehicleId" });

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_records_TrailerId",
                table: "maintenance_records",
                column: "TrailerId");

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_records_VehicleId",
                table: "maintenance_records",
                column: "VehicleId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "maintenance_records");
        }
    }
}
