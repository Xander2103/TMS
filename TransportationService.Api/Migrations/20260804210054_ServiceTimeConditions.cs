using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class ServiceTimeConditions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AllowStacking",
                table: "service_option_conditions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "Priority",
                table: "service_option_conditions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "StopScope",
                table: "service_option_conditions",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<TimeOnly>(
                name: "TimeOfDay",
                table: "service_option_conditions",
                type: "time without time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AllowStacking",
                table: "service_option_conditions");

            migrationBuilder.DropColumn(
                name: "Priority",
                table: "service_option_conditions");

            migrationBuilder.DropColumn(
                name: "StopScope",
                table: "service_option_conditions");

            migrationBuilder.DropColumn(
                name: "TimeOfDay",
                table: "service_option_conditions");
        }
    }
}
