using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class PricingAgreements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AgreementId",
                table: "price_rules",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "BaseAmount",
                table: "price_rules",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "OversizeBillableFactor",
                table: "price_rules",
                type: "numeric(8,2)",
                precision: 8,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "OversizeLengthCm",
                table: "price_rules",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "OversizeWidthCm",
                table: "price_rules",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Priority",
                table: "price_rules",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "pricing_agreements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveUntil = table.Column<DateOnly>(type: "date", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    MinimumAmount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    LegacyRateCardId = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("PK_pricing_agreements", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "pricing_agreement_surcharges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AgreementId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Value = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
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
                    table.PrimaryKey("PK_pricing_agreement_surcharges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_pricing_agreement_surcharges_pricing_agreements_AgreementId",
                        column: x => x.AgreementId,
                        principalTable: "pricing_agreements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_price_rules_AgreementId",
                table: "price_rules",
                column: "AgreementId");

            migrationBuilder.CreateIndex(
                name: "IX_price_rules_TenantId_AgreementId",
                table: "price_rules",
                columns: new[] { "TenantId", "AgreementId" });

            migrationBuilder.CreateIndex(
                name: "IX_pricing_agreement_surcharges_AgreementId",
                table: "pricing_agreement_surcharges",
                column: "AgreementId");

            migrationBuilder.CreateIndex(
                name: "IX_pricing_agreement_surcharges_TenantId_AgreementId",
                table: "pricing_agreement_surcharges",
                columns: new[] { "TenantId", "AgreementId" });

            migrationBuilder.CreateIndex(
                name: "IX_pricing_agreements_TenantId_CustomerId_EffectiveFrom",
                table: "pricing_agreements",
                columns: new[] { "TenantId", "CustomerId", "EffectiveFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_pricing_agreements_TenantId_LegacyRateCardId",
                table: "pricing_agreements",
                columns: new[] { "TenantId", "LegacyRateCardId" });

            migrationBuilder.AddForeignKey(
                name: "FK_price_rules_pricing_agreements_AgreementId",
                table: "price_rules",
                column: "AgreementId",
                principalTable: "pricing_agreements",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_price_rules_pricing_agreements_AgreementId",
                table: "price_rules");

            migrationBuilder.DropTable(
                name: "pricing_agreement_surcharges");

            migrationBuilder.DropTable(
                name: "pricing_agreements");

            migrationBuilder.DropIndex(
                name: "IX_price_rules_AgreementId",
                table: "price_rules");

            migrationBuilder.DropIndex(
                name: "IX_price_rules_TenantId_AgreementId",
                table: "price_rules");

            migrationBuilder.DropColumn(
                name: "AgreementId",
                table: "price_rules");

            migrationBuilder.DropColumn(
                name: "BaseAmount",
                table: "price_rules");

            migrationBuilder.DropColumn(
                name: "OversizeBillableFactor",
                table: "price_rules");

            migrationBuilder.DropColumn(
                name: "OversizeLengthCm",
                table: "price_rules");

            migrationBuilder.DropColumn(
                name: "OversizeWidthCm",
                table: "price_rules");

            migrationBuilder.DropColumn(
                name: "Priority",
                table: "price_rules");
        }
    }
}
