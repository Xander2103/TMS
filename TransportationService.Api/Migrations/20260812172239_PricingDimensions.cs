using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class PricingDimensions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsReturnMovement",
                table: "transport_orders",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "MoffettRequired",
                table: "transport_orders",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "PlateauRequired",
                table: "transport_orders",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "ActivityTypeId",
                table: "service_option_conditions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ActivityTypeId",
                table: "price_rules",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsReturnMovement",
                table: "transport_orders");

            migrationBuilder.DropColumn(
                name: "MoffettRequired",
                table: "transport_orders");

            migrationBuilder.DropColumn(
                name: "PlateauRequired",
                table: "transport_orders");

            migrationBuilder.DropColumn(
                name: "ActivityTypeId",
                table: "service_option_conditions");

            migrationBuilder.DropColumn(
                name: "ActivityTypeId",
                table: "price_rules");
        }
    }
}
