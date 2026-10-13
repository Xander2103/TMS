using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class ProblemsResponsibilityCharge : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ChargeAmount",
                table: "incidents",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ChargeDecidedAt",
                table: "incidents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ChargeDecidedByUserId",
                table: "incidents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ChargeDecision",
                table: "incidents",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ChargeDescription",
                table: "incidents",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LinkedRedeliveryOrderId",
                table: "incidents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResponsibilityNotes",
                table: "incidents",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResponsibleParty",
                table: "incidents",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ChargeAmount",
                table: "incidents");

            migrationBuilder.DropColumn(
                name: "ChargeDecidedAt",
                table: "incidents");

            migrationBuilder.DropColumn(
                name: "ChargeDecidedByUserId",
                table: "incidents");

            migrationBuilder.DropColumn(
                name: "ChargeDecision",
                table: "incidents");

            migrationBuilder.DropColumn(
                name: "ChargeDescription",
                table: "incidents");

            migrationBuilder.DropColumn(
                name: "LinkedRedeliveryOrderId",
                table: "incidents");

            migrationBuilder.DropColumn(
                name: "ResponsibilityNotes",
                table: "incidents");

            migrationBuilder.DropColumn(
                name: "ResponsibleParty",
                table: "incidents");
        }
    }
}
