using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class InvoiceEntityValidationAndAttachments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "LegalEntityId",
                table: "transport_orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomerVatNumberSnapshot",
                table: "invoices",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomerVatTreatment",
                table: "invoices",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SellerAddressLine",
                table: "invoices",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SellerIban",
                table: "invoices",
                type: "character varying(34)",
                maxLength: 34,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SellerName",
                table: "invoices",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SellerVatNumber",
                table: "invoices",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VatLegalText",
                table: "invoices",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DefaultLegalEntityId",
                table: "customers",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "invoice_attachments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    FileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    StorageKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    IncludeWhenSending = table.Column<bool>(type: "boolean", nullable: false),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_invoice_attachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_invoice_attachments_invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_customers_DefaultLegalEntityId",
                table: "customers",
                column: "DefaultLegalEntityId");

            migrationBuilder.CreateIndex(
                name: "IX_invoice_attachments_InvoiceId",
                table: "invoice_attachments",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_invoice_attachments_TenantId_InvoiceId",
                table: "invoice_attachments",
                columns: new[] { "TenantId", "InvoiceId" });

            migrationBuilder.AddForeignKey(
                name: "FK_customers_legal_entities_DefaultLegalEntityId",
                table: "customers",
                column: "DefaultLegalEntityId",
                principalTable: "legal_entities",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_customers_legal_entities_DefaultLegalEntityId",
                table: "customers");

            migrationBuilder.DropTable(
                name: "invoice_attachments");

            migrationBuilder.DropIndex(
                name: "IX_customers_DefaultLegalEntityId",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "LegalEntityId",
                table: "transport_orders");

            migrationBuilder.DropColumn(
                name: "CustomerVatNumberSnapshot",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "CustomerVatTreatment",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "SellerAddressLine",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "SellerIban",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "SellerName",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "SellerVatNumber",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "VatLegalText",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "DefaultLegalEntityId",
                table: "customers");
        }
    }
}
