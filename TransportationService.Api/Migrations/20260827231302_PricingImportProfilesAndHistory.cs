using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class PricingImportProfilesAndHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "pricing_import_profiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    MappingJson = table.Column<string>(type: "text", nullable: false),
                    HeaderRow = table.Column<int>(type: "integer", nullable: false),
                    SheetName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
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
                    table.PrimaryKey("PK_pricing_import_profiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "pricing_import_runs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AgreementId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetAgreementId = table.Column<Guid>(type: "uuid", nullable: false),
                    FileName = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    Checksum = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProfileId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProfileName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    RowsRead = table.Column<int>(type: "integer", nullable: false),
                    RowsValid = table.Column<int>(type: "integer", nullable: false),
                    Created = table.Column<int>(type: "integer", nullable: false),
                    Updated = table.Column<int>(type: "integer", nullable: false),
                    Removed = table.Column<int>(type: "integer", nullable: false),
                    Failed = table.Column<int>(type: "integer", nullable: false),
                    Mode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
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
                    table.PrimaryKey("PK_pricing_import_runs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_pricing_import_profiles_TenantId",
                table: "pricing_import_profiles",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_pricing_import_profiles_TenantId_Name",
                table: "pricing_import_profiles",
                columns: new[] { "TenantId", "Name" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_pricing_import_runs_TenantId",
                table: "pricing_import_runs",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_pricing_import_runs_TenantId_AgreementId",
                table: "pricing_import_runs",
                columns: new[] { "TenantId", "AgreementId" });

            migrationBuilder.CreateIndex(
                name: "IX_pricing_import_runs_TenantId_Checksum",
                table: "pricing_import_runs",
                columns: new[] { "TenantId", "Checksum" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pricing_import_profiles");

            migrationBuilder.DropTable(
                name: "pricing_import_runs");
        }
    }
}
