using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class PeppolDispatch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "NextAttemptAt",
                table: "peppol_transmissions",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NextAttemptAt",
                table: "peppol_transmissions");
        }
    }
}
