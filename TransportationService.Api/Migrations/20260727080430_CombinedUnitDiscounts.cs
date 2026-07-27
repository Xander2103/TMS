using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class CombinedUnitDiscounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "combined_unit_discounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: true),
                    AgreementId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Scope = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveUntil = table.Column<DateOnly>(type: "date", nullable: true),
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
                    table.PrimaryKey("PK_combined_unit_discounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "combined_unit_discount_tiers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DiscountId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromCount = table.Column<decimal>(type: "numeric(7,3)", precision: 7, scale: 3, nullable: false),
                    ToCount = table.Column<decimal>(type: "numeric(7,3)", precision: 7, scale: 3, nullable: true),
                    Percent = table.Column<decimal>(type: "numeric(7,3)", precision: 7, scale: 3, nullable: false),
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
                    table.PrimaryKey("PK_combined_unit_discount_tiers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_combined_unit_discount_tiers_combined_unit_discounts_Discou~",
                        column: x => x.DiscountId,
                        principalTable: "combined_unit_discounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "combined_unit_discount_units",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DiscountId = table.Column<Guid>(type: "uuid", nullable: false),
                    UnitTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    EquivalentFactor = table.Column<decimal>(type: "numeric(7,3)", precision: 7, scale: 3, nullable: false),
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
                    table.PrimaryKey("PK_combined_unit_discount_units", x => x.Id);
                    table.ForeignKey(
                        name: "FK_combined_unit_discount_units_combined_unit_discounts_Discou~",
                        column: x => x.DiscountId,
                        principalTable: "combined_unit_discounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_combined_unit_discount_tiers_DiscountId",
                table: "combined_unit_discount_tiers",
                column: "DiscountId");

            migrationBuilder.CreateIndex(
                name: "IX_combined_unit_discount_tiers_TenantId_DiscountId",
                table: "combined_unit_discount_tiers",
                columns: new[] { "TenantId", "DiscountId" });

            migrationBuilder.CreateIndex(
                name: "IX_combined_unit_discount_units_DiscountId",
                table: "combined_unit_discount_units",
                column: "DiscountId");

            migrationBuilder.CreateIndex(
                name: "IX_combined_unit_discount_units_TenantId_DiscountId",
                table: "combined_unit_discount_units",
                columns: new[] { "TenantId", "DiscountId" });

            migrationBuilder.CreateIndex(
                name: "IX_combined_unit_discounts_TenantId_AgreementId",
                table: "combined_unit_discounts",
                columns: new[] { "TenantId", "AgreementId" });

            migrationBuilder.CreateIndex(
                name: "IX_combined_unit_discounts_TenantId_CustomerId",
                table: "combined_unit_discounts",
                columns: new[] { "TenantId", "CustomerId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "combined_unit_discount_tiers");

            migrationBuilder.DropTable(
                name: "combined_unit_discount_units");

            migrationBuilder.DropTable(
                name: "combined_unit_discounts");
        }
    }
}
