using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class StopTimeRequirements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TimeRequirement",
                table: "transport_order_stops",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<TimeOnly>(
                name: "TimeRequirementFrom",
                table: "transport_order_stops",
                type: "time without time zone",
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "TimeRequirementTo",
                table: "transport_order_stops",
                type: "time without time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TimeRequirement",
                table: "transport_order_stops");

            migrationBuilder.DropColumn(
                name: "TimeRequirementFrom",
                table: "transport_order_stops");

            migrationBuilder.DropColumn(
                name: "TimeRequirementTo",
                table: "transport_order_stops");
        }
    }
}
