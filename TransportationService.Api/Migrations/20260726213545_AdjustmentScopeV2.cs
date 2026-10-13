using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class AdjustmentScopeV2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "Percent",
                table: "scheduled_price_adjustments",
                type: "numeric(7,3)",
                precision: 7,
                scale: 3,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(7,3)",
                oldPrecision: 7,
                oldScale: 3);

            migrationBuilder.AlterColumn<Guid>(
                name: "CustomerId",
                table: "scheduled_price_adjustments",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "AgreementId",
                table: "scheduled_price_adjustments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AmountDelta",
                table: "scheduled_price_adjustments",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BasisFilter",
                table: "scheduled_price_adjustments",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "RoundingStep",
                table: "scheduled_price_adjustments",
                type: "numeric(4,2)",
                precision: 4,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UnitTypeIdFilter",
                table: "scheduled_price_adjustments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_scheduled_price_adjustments_TenantId_AgreementId_EffectiveD~",
                table: "scheduled_price_adjustments",
                columns: new[] { "TenantId", "AgreementId", "EffectiveDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_scheduled_price_adjustments_TenantId_AgreementId_EffectiveD~",
                table: "scheduled_price_adjustments");

            migrationBuilder.DropColumn(
                name: "AgreementId",
                table: "scheduled_price_adjustments");

            migrationBuilder.DropColumn(
                name: "AmountDelta",
                table: "scheduled_price_adjustments");

            migrationBuilder.DropColumn(
                name: "BasisFilter",
                table: "scheduled_price_adjustments");

            migrationBuilder.DropColumn(
                name: "RoundingStep",
                table: "scheduled_price_adjustments");

            migrationBuilder.DropColumn(
                name: "UnitTypeIdFilter",
                table: "scheduled_price_adjustments");

            migrationBuilder.AlterColumn<decimal>(
                name: "Percent",
                table: "scheduled_price_adjustments",
                type: "numeric(7,3)",
                precision: 7,
                scale: 3,
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "numeric(7,3)",
                oldPrecision: 7,
                oldScale: 3,
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "CustomerId",
                table: "scheduled_price_adjustments",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
