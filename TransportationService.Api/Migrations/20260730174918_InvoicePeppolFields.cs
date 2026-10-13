using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class InvoicePeppolFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CreditNotePrefix",
                table: "legal_entities",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CreditedInvoiceId",
                table: "invoices",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "invoices",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Invoice");

            migrationBuilder.AddColumn<string>(
                name: "PaymentReference",
                table: "invoices",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UnitCode",
                table: "invoice_lines",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "C62");

            migrationBuilder.AddColumn<string>(
                name: "VatCategoryCode",
                table: "invoice_lines",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_invoices_CreditedInvoiceId",
                table: "invoices",
                column: "CreditedInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_invoices_one_live_credit_note",
                table: "invoices",
                columns: new[] { "TenantId", "CreditedInvoiceId" },
                unique: true,
                filter: "\"CreditedInvoiceId\" IS NOT NULL AND \"Status\" <> 'Cancelled' AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_invoices_TenantId_CreditedInvoiceId",
                table: "invoices",
                columns: new[] { "TenantId", "CreditedInvoiceId" });

            migrationBuilder.AddForeignKey(
                name: "FK_invoices_invoices_CreditedInvoiceId",
                table: "invoices",
                column: "CreditedInvoiceId",
                principalTable: "invoices",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_invoices_invoices_CreditedInvoiceId",
                table: "invoices");

            migrationBuilder.DropIndex(
                name: "IX_invoices_CreditedInvoiceId",
                table: "invoices");

            migrationBuilder.DropIndex(
                name: "IX_invoices_one_live_credit_note",
                table: "invoices");

            migrationBuilder.DropIndex(
                name: "IX_invoices_TenantId_CreditedInvoiceId",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "CreditNotePrefix",
                table: "legal_entities");

            migrationBuilder.DropColumn(
                name: "CreditedInvoiceId",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "PaymentReference",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "UnitCode",
                table: "invoice_lines");

            migrationBuilder.DropColumn(
                name: "VatCategoryCode",
                table: "invoice_lines");
        }
    }
}
