using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class DerivedPricingAgreements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "BaseAgreementId",
                table: "pricing_agreements",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "pricing_agreement_modifiers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AgreementId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CountryCode = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    ZoneId = table.Column<Guid>(type: "uuid", nullable: true),
                    Percent = table.Column<decimal>(type: "numeric(7,3)", precision: 7, scale: 3, nullable: true),
                    FixedAmount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
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
                    table.PrimaryKey("PK_pricing_agreement_modifiers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_pricing_agreement_modifiers_pricing_agreements_AgreementId",
                        column: x => x.AgreementId,
                        principalTable: "pricing_agreements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_pricing_agreements_BaseAgreementId",
                table: "pricing_agreements",
                column: "BaseAgreementId");

            migrationBuilder.CreateIndex(
                name: "IX_pricing_agreements_TenantId_BaseAgreementId",
                table: "pricing_agreements",
                columns: new[] { "TenantId", "BaseAgreementId" });

            migrationBuilder.CreateIndex(
                name: "IX_pricing_agreement_modifiers_AgreementId",
                table: "pricing_agreement_modifiers",
                column: "AgreementId");

            migrationBuilder.CreateIndex(
                name: "IX_pricing_agreement_modifiers_TenantId_AgreementId",
                table: "pricing_agreement_modifiers",
                columns: new[] { "TenantId", "AgreementId" });

            migrationBuilder.AddForeignKey(
                name: "FK_pricing_agreements_pricing_agreements_BaseAgreementId",
                table: "pricing_agreements",
                column: "BaseAgreementId",
                principalTable: "pricing_agreements",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_pricing_agreements_pricing_agreements_BaseAgreementId",
                table: "pricing_agreements");

            migrationBuilder.DropTable(
                name: "pricing_agreement_modifiers");

            migrationBuilder.DropIndex(
                name: "IX_pricing_agreements_BaseAgreementId",
                table: "pricing_agreements");

            migrationBuilder.DropIndex(
                name: "IX_pricing_agreements_TenantId_BaseAgreementId",
                table: "pricing_agreements");

            migrationBuilder.DropColumn(
                name: "BaseAgreementId",
                table: "pricing_agreements");
        }
    }
}
