using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class MaintenancePolicies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "maintenance_policies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AssetKind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: true),
                    VehicleId = table.Column<Guid>(type: "uuid", nullable: true),
                    TrailerId = table.Column<Guid>(type: "uuid", nullable: true),
                    IntervalMonths = table.Column<int>(type: "integer", nullable: true),
                    IntervalKm = table.Column<int>(type: "integer", nullable: true),
                    WarningDays = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("PK_maintenance_policies", x => x.Id);
                    table.CheckConstraint("CK_maintenance_policies_single_level", "(CASE WHEN \"VehicleId\" IS NOT NULL THEN 1 ELSE 0 END + CASE WHEN \"TrailerId\" IS NOT NULL THEN 1 ELSE 0 END + CASE WHEN \"CategoryId\" IS NOT NULL THEN 1 ELSE 0 END) <= 1");
                });

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_policies_TenantId_AssetKind_Kind",
                table: "maintenance_policies",
                columns: new[] { "TenantId", "AssetKind", "Kind" });

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_policies_TenantId_TrailerId",
                table: "maintenance_policies",
                columns: new[] { "TenantId", "TrailerId" });

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_policies_TenantId_VehicleId",
                table: "maintenance_policies",
                columns: new[] { "TenantId", "VehicleId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "maintenance_policies");
        }
    }
}
