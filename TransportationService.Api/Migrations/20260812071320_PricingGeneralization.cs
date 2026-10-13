using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class PricingGeneralization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "DistanceKm",
                table: "transport_orders",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "LoadingMeters",
                table: "transport_orders",
                type: "numeric(8,2)",
                precision: 8,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OriginZoneId",
                table: "price_rules",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "tenant_holidays",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
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
                    table.PrimaryKey("PK_tenant_holidays", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_price_rules_OriginZoneId",
                table: "price_rules",
                column: "OriginZoneId");

            migrationBuilder.CreateIndex(
                name: "IX_tenant_holidays_TenantId_Date",
                table: "tenant_holidays",
                columns: new[] { "TenantId", "Date" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.AddForeignKey(
                name: "FK_price_rules_pricing_zones_OriginZoneId",
                table: "price_rules",
                column: "OriginZoneId",
                principalTable: "pricing_zones",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_price_rules_pricing_zones_OriginZoneId",
                table: "price_rules");

            migrationBuilder.DropTable(
                name: "tenant_holidays");

            migrationBuilder.DropIndex(
                name: "IX_price_rules_OriginZoneId",
                table: "price_rules");

            migrationBuilder.DropColumn(
                name: "DistanceKm",
                table: "transport_orders");

            migrationBuilder.DropColumn(
                name: "LoadingMeters",
                table: "transport_orders");

            migrationBuilder.DropColumn(
                name: "OriginZoneId",
                table: "price_rules");
        }
    }
}
