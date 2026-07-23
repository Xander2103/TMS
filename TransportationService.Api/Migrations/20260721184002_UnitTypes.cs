using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class UnitTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "QuantityUnitCode",
                table: "transport_orders",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QuantityUnitCode",
                table: "order_cargo_items",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "unit_types",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_unit_types", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_unit_types_TenantId",
                table: "unit_types",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_unit_types_TenantId_Code",
                table: "unit_types",
                columns: new[] { "TenantId", "Code" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_unit_types_TenantId_IsActive",
                table: "unit_types",
                columns: new[] { "TenantId", "IsActive" });

            // Map existing free-text QuantityUnit values onto managed codes (case-insensitive).
            // Unmapped values keep their free text (QuantityUnitCode stays null) and still display.
            migrationBuilder.Sql(
                """
                UPDATE transport_orders SET "QuantityUnitCode" = CASE lower(btrim("QuantityUnit"))
                    WHEN 'colli' THEN 'COLLI'
                    WHEN 'europallet' THEN 'EUROPALLET' WHEN 'europalletten' THEN 'EUROPALLET' WHEN 'euro pallet' THEN 'EUROPALLET'
                    WHEN 'pallet' THEN 'PALLET' WHEN 'palletten' THEN 'PALLET' WHEN 'blokpallet' THEN 'BLOCKPALLET'
                    WHEN 'container' THEN 'CONTAINER' WHEN 'krat' THEN 'CRATE' WHEN 'kratten' THEN 'CRATE'
                    WHEN 'doos' THEN 'BOX' WHEN 'dozen' THEN 'BOX' WHEN 'rolcontainer' THEN 'ROLLCONTAINER'
                    WHEN 'stuks' THEN 'PIECE' WHEN 'stuk' THEN 'PIECE' WHEN 'pcs' THEN 'PIECE'
                    WHEN 'ldm' THEN 'LOADINGMETER' WHEN 'laadmeter' THEN 'LOADINGMETER' WHEN 'laadmeters' THEN 'LOADINGMETER'
                    WHEN 'kg' THEN 'KG' WHEN 'kilogram' THEN 'KG' WHEN 'ton' THEN 'TON' WHEN 'tonnes' THEN 'TON' WHEN 'ton(nen)' THEN 'TON'
                    WHEN 'vat' THEN 'DRUM' WHEN 'drum' THEN 'DRUM' WHEN 'document' THEN 'DOCUMENT' WHEN 'pakket' THEN 'PARCEL'
                    ELSE NULL END
                WHERE "QuantityUnit" IS NOT NULL AND "QuantityUnitCode" IS NULL;
                """);
            migrationBuilder.Sql(
                """
                UPDATE order_cargo_items SET "QuantityUnitCode" = CASE lower(btrim("QuantityUnit"))
                    WHEN 'colli' THEN 'COLLI'
                    WHEN 'europallet' THEN 'EUROPALLET' WHEN 'pallet' THEN 'PALLET' WHEN 'blokpallet' THEN 'BLOCKPALLET'
                    WHEN 'container' THEN 'CONTAINER' WHEN 'krat' THEN 'CRATE' WHEN 'doos' THEN 'BOX'
                    WHEN 'stuks' THEN 'PIECE' WHEN 'ldm' THEN 'LOADINGMETER' WHEN 'laadmeter' THEN 'LOADINGMETER'
                    WHEN 'kg' THEN 'KG' WHEN 'ton' THEN 'TON'
                    ELSE NULL END
                WHERE "QuantityUnit" IS NOT NULL AND "QuantityUnitCode" IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "unit_types");

            migrationBuilder.DropColumn(
                name: "QuantityUnitCode",
                table: "transport_orders");

            migrationBuilder.DropColumn(
                name: "QuantityUnitCode",
                table: "order_cargo_items");
        }
    }
}
