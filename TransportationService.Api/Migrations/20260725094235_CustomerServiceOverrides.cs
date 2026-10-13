using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class CustomerServiceOverrides : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "Value",
                table: "customer_service_option_prices",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(12,2)",
                oldPrecision: 12,
                oldScale: 2);

            migrationBuilder.AddColumn<bool>(
                name: "Disabled",
                table: "customer_service_option_prices",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateOnly>(
                name: "EffectiveFrom",
                table: "customer_service_option_prices",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "EffectiveUntil",
                table: "customer_service_option_prices",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InvoiceDescription",
                table: "customer_service_option_prices",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MinimumAmount",
                table: "customer_service_option_prices",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Disabled",
                table: "customer_service_option_prices");

            migrationBuilder.DropColumn(
                name: "EffectiveFrom",
                table: "customer_service_option_prices");

            migrationBuilder.DropColumn(
                name: "EffectiveUntil",
                table: "customer_service_option_prices");

            migrationBuilder.DropColumn(
                name: "InvoiceDescription",
                table: "customer_service_option_prices");

            migrationBuilder.DropColumn(
                name: "MinimumAmount",
                table: "customer_service_option_prices");

            migrationBuilder.AlterColumn<decimal>(
                name: "Value",
                table: "customer_service_option_prices",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "numeric(12,2)",
                oldPrecision: 12,
                oldScale: 2,
                oldNullable: true);
        }
    }
}
