using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddDamageReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "damage_reports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VehicleId = table.Column<Guid>(type: "uuid", nullable: true),
                    TrailerId = table.Column<Guid>(type: "uuid", nullable: true),
                    DriverId = table.Column<Guid>(type: "uuid", nullable: true),
                    IncidentDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Location = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Severity = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    InsuranceReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    RepairCost = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    DowntimeDays = table.Column<int>(type: "integer", nullable: true),
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
                    table.PrimaryKey("PK_damage_reports", x => x.Id);
                    table.CheckConstraint("CK_damage_reports_single_owner", "(\"VehicleId\" IS NULL) <> (\"TrailerId\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_damage_reports_drivers_DriverId",
                        column: x => x.DriverId,
                        principalTable: "drivers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_damage_reports_trailers_TrailerId",
                        column: x => x.TrailerId,
                        principalTable: "trailers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_damage_reports_vehicles_VehicleId",
                        column: x => x.VehicleId,
                        principalTable: "vehicles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_damage_reports_DriverId",
                table: "damage_reports",
                column: "DriverId");

            migrationBuilder.CreateIndex(
                name: "IX_damage_reports_TenantId_IncidentDate",
                table: "damage_reports",
                columns: new[] { "TenantId", "IncidentDate" });

            migrationBuilder.CreateIndex(
                name: "IX_damage_reports_TenantId_TrailerId",
                table: "damage_reports",
                columns: new[] { "TenantId", "TrailerId" });

            migrationBuilder.CreateIndex(
                name: "IX_damage_reports_TenantId_VehicleId",
                table: "damage_reports",
                columns: new[] { "TenantId", "VehicleId" });

            migrationBuilder.CreateIndex(
                name: "IX_damage_reports_TrailerId",
                table: "damage_reports",
                column: "TrailerId");

            migrationBuilder.CreateIndex(
                name: "IX_damage_reports_VehicleId",
                table: "damage_reports",
                column: "VehicleId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "damage_reports");
        }
    }
}
