using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class PricingAgreementAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsShared",
                table: "pricing_agreements",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "MaximumAmount",
                table: "pricing_agreements",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "pricing_agreement_assignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AgreementId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    PercentAdjustment = table.Column<decimal>(type: "numeric(7,3)", precision: 7, scale: 3, nullable: true),
                    FixedAdjustment = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: true),
                    EffectiveUntil = table.Column<DateOnly>(type: "date", nullable: true),
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
                    table.PrimaryKey("PK_pricing_agreement_assignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_pricing_agreement_assignments_pricing_agreements_AgreementId",
                        column: x => x.AgreementId,
                        principalTable: "pricing_agreements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_pricing_agreement_assignments_AgreementId",
                table: "pricing_agreement_assignments",
                column: "AgreementId");

            migrationBuilder.CreateIndex(
                name: "IX_pricing_agreement_assignments_TenantId_AgreementId",
                table: "pricing_agreement_assignments",
                columns: new[] { "TenantId", "AgreementId" });

            migrationBuilder.CreateIndex(
                name: "IX_pricing_agreement_assignments_TenantId_CustomerId",
                table: "pricing_agreement_assignments",
                columns: new[] { "TenantId", "CustomerId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pricing_agreement_assignments");

            migrationBuilder.DropColumn(
                name: "IsShared",
                table: "pricing_agreements");

            migrationBuilder.DropColumn(
                name: "MaximumAmount",
                table: "pricing_agreements");
        }
    }
}
