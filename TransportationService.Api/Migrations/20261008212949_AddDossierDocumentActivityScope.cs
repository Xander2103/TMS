using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddDossierDocumentActivityScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DossierActivityId",
                table: "order_documents",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_order_documents_DossierActivityId",
                table: "order_documents",
                column: "DossierActivityId");

            migrationBuilder.CreateIndex(
                name: "IX_order_documents_TenantId_DossierActivityId",
                table: "order_documents",
                columns: new[] { "TenantId", "DossierActivityId" },
                filter: "\"DossierActivityId\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_order_documents_activity_requires_dossier",
                table: "order_documents",
                sql: "\"DossierActivityId\" IS NULL OR (\"TransportOrderId\" IS NULL AND \"DossierId\" IS NOT NULL)");

            migrationBuilder.AddForeignKey(
                name: "FK_order_documents_dossier_activities_DossierActivityId",
                table: "order_documents",
                column: "DossierActivityId",
                principalTable: "dossier_activities",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_order_documents_dossier_activities_DossierActivityId",
                table: "order_documents");

            migrationBuilder.DropIndex(
                name: "IX_order_documents_DossierActivityId",
                table: "order_documents");

            migrationBuilder.DropIndex(
                name: "IX_order_documents_TenantId_DossierActivityId",
                table: "order_documents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_order_documents_activity_requires_dossier",
                table: "order_documents");

            migrationBuilder.DropColumn(
                name: "DossierActivityId",
                table: "order_documents");
        }
    }
}
