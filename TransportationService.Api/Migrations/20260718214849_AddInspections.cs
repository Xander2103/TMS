using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddInspections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "inspections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VehicleId = table.Column<Guid>(type: "uuid", nullable: true),
                    TrailerId = table.Column<Guid>(type: "uuid", nullable: true),
                    InspectionType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    CustomTypeName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    CompletedDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Result = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    IntervalMonths = table.Column<int>(type: "integer", nullable: true),
                    WarningDays = table.Column<int>(type: "integer", nullable: true),
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
                    table.PrimaryKey("PK_inspections", x => x.Id);
                    table.CheckConstraint("CK_inspections_single_owner", "(\"VehicleId\" IS NULL) <> (\"TrailerId\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_inspections_trailers_TrailerId",
                        column: x => x.TrailerId,
                        principalTable: "trailers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_inspections_vehicles_VehicleId",
                        column: x => x.VehicleId,
                        principalTable: "vehicles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_inspections_TenantId_DueDate",
                table: "inspections",
                columns: new[] { "TenantId", "DueDate" });

            migrationBuilder.CreateIndex(
                name: "IX_inspections_TenantId_TrailerId",
                table: "inspections",
                columns: new[] { "TenantId", "TrailerId" });

            migrationBuilder.CreateIndex(
                name: "IX_inspections_TenantId_VehicleId",
                table: "inspections",
                columns: new[] { "TenantId", "VehicleId" });

            migrationBuilder.CreateIndex(
                name: "IX_inspections_TrailerId",
                table: "inspections",
                column: "TrailerId");

            migrationBuilder.CreateIndex(
                name: "IX_inspections_VehicleId",
                table: "inspections",
                column: "VehicleId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inspections");
        }
    }
}
