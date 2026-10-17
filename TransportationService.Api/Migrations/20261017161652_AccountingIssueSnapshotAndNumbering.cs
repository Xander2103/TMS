using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class AccountingIssueSnapshotAndNumbering : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_invoices_TenantId_InvoiceNumber",
                table: "invoices");

            migrationBuilder.DropIndex(
                name: "IX_invoice_sequences_TenantId_LegalEntityId_Year_Month",
                table: "invoice_sequences");

            migrationBuilder.AddColumn<bool>(
                name: "FreeConfirmed",
                table: "transport_orders",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AlterColumn<string>(
                name: "InvoiceNumber",
                table: "invoices",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30);

            migrationBuilder.AddColumn<string>(
                name: "CreditReason",
                table: "invoices",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomerBuyerReferenceSnapshot",
                table: "invoices",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomerCitySnapshot",
                table: "invoices",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomerCompanyNumberSnapshot",
                table: "invoices",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomerCountryCodeSnapshot",
                table: "invoices",
                type: "character varying(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomerHouseNumberSnapshot",
                table: "invoices",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomerLegalNameSnapshot",
                table: "invoices",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomerNameSnapshot",
                table: "invoices",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomerPeppolIdSnapshot",
                table: "invoices",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomerPeppolSchemeSnapshot",
                table: "invoices",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomerPostalCodeSnapshot",
                table: "invoices",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomerStreetSnapshot",
                table: "invoices",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "DueDateIsManual",
                table: "invoices",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "IssuedAt",
                table: "invoices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PaidAt",
                table: "invoices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PaidByUserId",
                table: "invoices",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PaymentTermDays",
                table: "invoices",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SellerBic",
                table: "invoices",
                type: "character varying(11)",
                maxLength: 11,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SellerCity",
                table: "invoices",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SellerCompanyNumber",
                table: "invoices",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SellerCountryCode",
                table: "invoices",
                type: "character varying(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SellerHouseNumber",
                table: "invoices",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SellerInvoiceFooter",
                table: "invoices",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SellerLegalName",
                table: "invoices",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SellerPeppolId",
                table: "invoices",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SellerPeppolScheme",
                table: "invoices",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SellerPostalCode",
                table: "invoices",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SellerStreet",
                table: "invoices",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "invoice_sequences",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Invoice");

            migrationBuilder.AlterColumn<decimal>(
                name: "UnitPrice",
                table: "invoice_lines",
                type: "numeric(14,4)",
                precision: 14,
                scale: 4,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(12,2)",
                oldPrecision: 12,
                oldScale: 2);

            migrationBuilder.AlterColumn<decimal>(
                name: "Quantity",
                table: "invoice_lines",
                type: "numeric(14,3)",
                precision: 14,
                scale: 3,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(12,2)",
                oldPrecision: 12,
                oldScale: 2);

            migrationBuilder.AddColumn<string>(
                name: "UnitLabel",
                table: "invoice_lines",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_invoices_TenantId_InvoiceNumber",
                table: "invoices",
                columns: new[] { "TenantId", "InvoiceNumber" },
                unique: true,
                filter: "\"IsDeleted\" = false AND \"InvoiceNumber\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_invoice_sequences_TenantId_LegalEntityId_Kind_Year_Month",
                table: "invoice_sequences",
                columns: new[] { "TenantId", "LegalEntityId", "Kind", "Year", "Month" },
                unique: true,
                filter: "\"IsDeleted\" = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_invoices_TenantId_InvoiceNumber",
                table: "invoices");

            migrationBuilder.DropIndex(
                name: "IX_invoice_sequences_TenantId_LegalEntityId_Kind_Year_Month",
                table: "invoice_sequences");

            migrationBuilder.DropColumn(
                name: "FreeConfirmed",
                table: "transport_orders");

            migrationBuilder.DropColumn(
                name: "CreditReason",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "CustomerBuyerReferenceSnapshot",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "CustomerCitySnapshot",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "CustomerCompanyNumberSnapshot",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "CustomerCountryCodeSnapshot",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "CustomerHouseNumberSnapshot",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "CustomerLegalNameSnapshot",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "CustomerNameSnapshot",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "CustomerPeppolIdSnapshot",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "CustomerPeppolSchemeSnapshot",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "CustomerPostalCodeSnapshot",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "CustomerStreetSnapshot",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "DueDateIsManual",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "IssuedAt",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "PaidAt",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "PaidByUserId",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "PaymentTermDays",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "SellerBic",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "SellerCity",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "SellerCompanyNumber",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "SellerCountryCode",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "SellerHouseNumber",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "SellerInvoiceFooter",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "SellerLegalName",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "SellerPeppolId",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "SellerPeppolScheme",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "SellerPostalCode",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "SellerStreet",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "invoice_sequences");

            migrationBuilder.DropColumn(
                name: "UnitLabel",
                table: "invoice_lines");

            migrationBuilder.AlterColumn<string>(
                name: "InvoiceNumber",
                table: "invoices",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30,
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "UnitPrice",
                table: "invoice_lines",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(14,4)",
                oldPrecision: 14,
                oldScale: 4);

            migrationBuilder.AlterColumn<decimal>(
                name: "Quantity",
                table: "invoice_lines",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(14,3)",
                oldPrecision: 14,
                oldScale: 3);

            migrationBuilder.CreateIndex(
                name: "IX_invoices_TenantId_InvoiceNumber",
                table: "invoices",
                columns: new[] { "TenantId", "InvoiceNumber" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_invoice_sequences_TenantId_LegalEntityId_Year_Month",
                table: "invoice_sequences",
                columns: new[] { "TenantId", "LegalEntityId", "Year", "Month" },
                unique: true,
                filter: "\"IsDeleted\" = false");
        }
    }
}
