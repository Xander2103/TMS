using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class PackagesWave9Warehouse : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AssignedToUserId",
                table: "execution_exceptions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_execution_exceptions_TenantId_AssignedToUserId",
                table: "execution_exceptions",
                columns: new[] { "TenantId", "AssignedToUserId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_execution_exceptions_TenantId_AssignedToUserId",
                table: "execution_exceptions");

            migrationBuilder.DropColumn(
                name: "AssignedToUserId",
                table: "execution_exceptions");
        }
    }
}
