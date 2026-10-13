using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class ScheduledPriceAdjustments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "scheduled_price_adjustments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    EffectiveDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Percent = table.Column<decimal>(type: "numeric(7,3)", precision: 7, scale: 3, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
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
                    table.PrimaryKey("PK_scheduled_price_adjustments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "scheduled_price_adjustment_rules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AdjustmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourcePriceRuleId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedPriceRuleId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceOriginalEffectiveUntil = table.Column<DateOnly>(type: "date", nullable: true),
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
                    table.PrimaryKey("PK_scheduled_price_adjustment_rules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_scheduled_price_adjustment_rules_scheduled_price_adjustment~",
                        column: x => x.AdjustmentId,
                        principalTable: "scheduled_price_adjustments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_scheduled_price_adjustment_rules_AdjustmentId",
                table: "scheduled_price_adjustment_rules",
                column: "AdjustmentId");

            migrationBuilder.CreateIndex(
                name: "IX_scheduled_price_adjustment_rules_TenantId_AdjustmentId",
                table: "scheduled_price_adjustment_rules",
                columns: new[] { "TenantId", "AdjustmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_scheduled_price_adjustments_TenantId_CustomerId_EffectiveDa~",
                table: "scheduled_price_adjustments",
                columns: new[] { "TenantId", "CustomerId", "EffectiveDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "scheduled_price_adjustment_rules");

            migrationBuilder.DropTable(
                name: "scheduled_price_adjustments");
        }
    }
}
