using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class StorageStays : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "storage_stays",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PackageId = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseLocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    InAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    OutAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    InPackageEventId = table.Column<Guid>(type: "uuid", nullable: true),
                    OutPackageEventId = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("PK_storage_stays", x => x.Id);
                    table.ForeignKey(
                        name: "FK_storage_stays_warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_storage_stays_TenantId_WarehouseId_OutAt",
                table: "storage_stays",
                columns: new[] { "TenantId", "WarehouseId", "OutAt" });

            migrationBuilder.CreateIndex(
                name: "IX_storage_stays_WarehouseId",
                table: "storage_stays",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "UX_storage_stays_open_per_package",
                table: "storage_stays",
                columns: new[] { "TenantId", "PackageId" },
                unique: true,
                filter: "\"OutAt\" IS NULL AND \"IsDeleted\" = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "storage_stays");
        }
    }
}
