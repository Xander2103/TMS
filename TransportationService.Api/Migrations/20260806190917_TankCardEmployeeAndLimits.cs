using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class TankCardEmployeeAndLimits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CostCenter",
                table: "tank_cards",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DailyLimit",
                table: "tank_cards",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "EmployeeId",
                table: "tank_cards",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FuelType",
                table: "tank_cards",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InternalName",
                table: "tank_cards",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MonthlyLimit",
                table: "tank_cards",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "WeeklyLimit",
                table: "tank_cards",
                type: "numeric",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_tank_cards_EmployeeId",
                table: "tank_cards",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_tank_cards_TenantId_EmployeeId",
                table: "tank_cards",
                columns: new[] { "TenantId", "EmployeeId" });

            migrationBuilder.AddForeignKey(
                name: "FK_tank_cards_employees_EmployeeId",
                table: "tank_cards",
                column: "EmployeeId",
                principalTable: "employees",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            if (migrationBuilder.ActiveProvider?.Contains("Npgsql") != true)
            {
                return;
            }

            // Backfill the new canonical EmployeeId from the legacy DriverId link, matching what
            // TankCardService.ResolveEmployeeAndDriverAsync would have derived on write. Only fills
            // rows that don't already have an EmployeeId (none can yet, but keep this idempotent).
            migrationBuilder.Sql(@"
                UPDATE tank_cards tc SET
                    ""EmployeeId"" = d.""EmployeeId""
                FROM drivers d
                WHERE tc.""DriverId"" = d.""Id"" AND tc.""EmployeeId"" IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_tank_cards_employees_EmployeeId",
                table: "tank_cards");

            migrationBuilder.DropIndex(
                name: "IX_tank_cards_EmployeeId",
                table: "tank_cards");

            migrationBuilder.DropIndex(
                name: "IX_tank_cards_TenantId_EmployeeId",
                table: "tank_cards");

            migrationBuilder.DropColumn(
                name: "CostCenter",
                table: "tank_cards");

            migrationBuilder.DropColumn(
                name: "DailyLimit",
                table: "tank_cards");

            migrationBuilder.DropColumn(
                name: "EmployeeId",
                table: "tank_cards");

            migrationBuilder.DropColumn(
                name: "FuelType",
                table: "tank_cards");

            migrationBuilder.DropColumn(
                name: "InternalName",
                table: "tank_cards");

            migrationBuilder.DropColumn(
                name: "MonthlyLimit",
                table: "tank_cards");

            migrationBuilder.DropColumn(
                name: "WeeklyLimit",
                table: "tank_cards");
        }
    }
}
