using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class ServiceCalculationBases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AutoApply",
                table: "service_options",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "OnlyForAdr",
                table: "service_options",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "UnitTypeId",
                table: "service_options",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AutoApplyOverride",
                table: "customer_service_option_prices",
                type: "boolean",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AutoApply",
                table: "service_options");

            migrationBuilder.DropColumn(
                name: "OnlyForAdr",
                table: "service_options");

            migrationBuilder.DropColumn(
                name: "UnitTypeId",
                table: "service_options");

            migrationBuilder.DropColumn(
                name: "AutoApplyOverride",
                table: "customer_service_option_prices");
        }
    }
}
