using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class InvoiceSnooze : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "InvoiceSnoozeReason",
                table: "transport_orders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "InvoiceSnoozeUntil",
                table: "transport_orders",
                type: "date",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InvoiceSnoozeReason",
                table: "transport_orders");

            migrationBuilder.DropColumn(
                name: "InvoiceSnoozeUntil",
                table: "transport_orders");
        }
    }
}
