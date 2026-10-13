using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class InvoiceNumbering : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "InvoicePeriodMonth",
                table: "invoices",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "InvoicePeriodYear",
                table: "invoices",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "LegalEntityId",
                table: "invoices",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "NumberIsManual",
                table: "invoices",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Backfill the invoice period of historic rows from their invoice date.
            migrationBuilder.Sql(
                """
                UPDATE invoices
                SET "InvoicePeriodYear" = EXTRACT(YEAR FROM "InvoiceDate")::int,
                    "InvoicePeriodMonth" = EXTRACT(MONTH FROM "InvoiceDate")::int
                WHERE "InvoicePeriodYear" = 0;
                """);

            migrationBuilder.CreateTable(
                name: "invoice_sequences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LegalEntityId = table.Column<Guid>(type: "uuid", nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    Month = table.Column<int>(type: "integer", nullable: false),
                    NextValue = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("PK_invoice_sequences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_invoice_sequences_legal_entities_LegalEntityId",
                        column: x => x.LegalEntityId,
                        principalTable: "legal_entities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_invoices_LegalEntityId",
                table: "invoices",
                column: "LegalEntityId");

            migrationBuilder.CreateIndex(
                name: "IX_invoices_TenantId_LegalEntityId_InvoicePeriodYear_InvoicePe~",
                table: "invoices",
                columns: new[] { "TenantId", "LegalEntityId", "InvoicePeriodYear", "InvoicePeriodMonth" });

            migrationBuilder.CreateIndex(
                name: "IX_invoice_sequences_LegalEntityId",
                table: "invoice_sequences",
                column: "LegalEntityId");

            migrationBuilder.CreateIndex(
                name: "IX_invoice_sequences_TenantId_LegalEntityId_Year_Month",
                table: "invoice_sequences",
                columns: new[] { "TenantId", "LegalEntityId", "Year", "Month" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.AddForeignKey(
                name: "FK_invoices_legal_entities_LegalEntityId",
                table: "invoices",
                column: "LegalEntityId",
                principalTable: "legal_entities",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_invoices_legal_entities_LegalEntityId",
                table: "invoices");

            migrationBuilder.DropTable(
                name: "invoice_sequences");

            migrationBuilder.DropIndex(
                name: "IX_invoices_LegalEntityId",
                table: "invoices");

            migrationBuilder.DropIndex(
                name: "IX_invoices_TenantId_LegalEntityId_InvoicePeriodYear_InvoicePe~",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "InvoicePeriodMonth",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "InvoicePeriodYear",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "LegalEntityId",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "NumberIsManual",
                table: "invoices");
        }
    }
}
