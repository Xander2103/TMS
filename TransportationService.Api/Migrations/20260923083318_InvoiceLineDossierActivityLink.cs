using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class InvoiceLineDossierActivityLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DossierActivityId",
                table: "invoice_lines",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_invoice_lines_DossierActivityId",
                table: "invoice_lines",
                column: "DossierActivityId");

            migrationBuilder.CreateIndex(
                name: "IX_invoice_lines_TenantId_DossierActivityId",
                table: "invoice_lines",
                columns: new[] { "TenantId", "DossierActivityId" });

            migrationBuilder.AddForeignKey(
                name: "FK_invoice_lines_dossier_activities_DossierActivityId",
                table: "invoice_lines",
                column: "DossierActivityId",
                principalTable: "dossier_activities",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_invoice_lines_dossier_activities_DossierActivityId",
                table: "invoice_lines");

            migrationBuilder.DropIndex(
                name: "IX_invoice_lines_DossierActivityId",
                table: "invoice_lines");

            migrationBuilder.DropIndex(
                name: "IX_invoice_lines_TenantId_DossierActivityId",
                table: "invoice_lines");

            migrationBuilder.DropColumn(
                name: "DossierActivityId",
                table: "invoice_lines");
        }
    }
}
