using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddTrailers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TrailerNumberNextValue",
                table: "tenant_settings",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "TrailerNumberPrefix",
                table: "tenant_settings",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "trailers",
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
                    CapacityKg = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    LengthMeters = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: true),
                    WidthMeters = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: true),
                    HeightMeters = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: true),
                    VolumeM3 = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: true),
                    HasRefrigeration = table.Column<bool>(type: "boolean", nullable: false),
                    AdrSuitable = table.Column<bool>(type: "boolean", nullable: false),
                    OwnershipType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    OperationalStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("PK_trailers", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_drivers_DefaultTrailerId",
                table: "drivers",
                column: "DefaultTrailerId");

            migrationBuilder.CreateIndex(
                name: "IX_trailers_TenantId",
                table: "trailers",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_trailers_TenantId_InternalNumber",
                table: "trailers",
                columns: new[] { "TenantId", "InternalNumber" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_trailers_TenantId_IsActive",
                table: "trailers",
                columns: new[] { "TenantId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_trailers_TenantId_LicensePlate",
                table: "trailers",
                columns: new[] { "TenantId", "LicensePlate" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_trailers_TenantId_OperationalStatus",
                table: "trailers",
                columns: new[] { "TenantId", "OperationalStatus" });

            migrationBuilder.AddForeignKey(
                name: "FK_drivers_trailers_DefaultTrailerId",
                table: "drivers",
                column: "DefaultTrailerId",
                principalTable: "trailers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_drivers_trailers_DefaultTrailerId",
                table: "drivers");

            migrationBuilder.DropTable(
                name: "trailers");

            migrationBuilder.DropIndex(
                name: "IX_drivers_DefaultTrailerId",
                table: "drivers");

            migrationBuilder.DropColumn(
                name: "TrailerNumberNextValue",
                table: "tenant_settings");

            migrationBuilder.DropColumn(
                name: "TrailerNumberPrefix",
                table: "tenant_settings");
        }
    }
}
