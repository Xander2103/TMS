using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class AccountingLedgerAndSalesCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "LedgerAccountId",
                table: "invoice_lines",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LedgerAccountNameSnapshot",
                table: "invoice_lines",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LedgerAccountNumberSnapshot",
                table: "invoice_lines",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SalesCategoryId",
                table: "invoice_lines",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SalesCategoryNameSnapshot",
                table: "invoice_lines",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ledger_accounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountNumber = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ExternalCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("PK_ledger_accounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "sales_categories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SystemRole = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    LedgerAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("PK_sales_categories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_sales_categories_ledger_accounts_LedgerAccountId",
                        column: x => x.LedgerAccountId,
                        principalTable: "ledger_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_invoice_lines_LedgerAccountId",
                table: "invoice_lines",
                column: "LedgerAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_invoice_lines_SalesCategoryId",
                table: "invoice_lines",
                column: "SalesCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_invoice_lines_TenantId_LedgerAccountId",
                table: "invoice_lines",
                columns: new[] { "TenantId", "LedgerAccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_ledger_accounts_TenantId_AccountNumber",
                table: "ledger_accounts",
                columns: new[] { "TenantId", "AccountNumber" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_sales_categories_LedgerAccountId",
                table: "sales_categories",
                column: "LedgerAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_sales_categories_TenantId",
                table: "sales_categories",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_sales_categories_TenantId_Code",
                table: "sales_categories",
                columns: new[] { "TenantId", "Code" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.AddForeignKey(
                name: "FK_invoice_lines_ledger_accounts_LedgerAccountId",
                table: "invoice_lines",
                column: "LedgerAccountId",
                principalTable: "ledger_accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_invoice_lines_sales_categories_SalesCategoryId",
                table: "invoice_lines",
                column: "SalesCategoryId",
                principalTable: "sales_categories",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_invoice_lines_ledger_accounts_LedgerAccountId",
                table: "invoice_lines");

            migrationBuilder.DropForeignKey(
                name: "FK_invoice_lines_sales_categories_SalesCategoryId",
                table: "invoice_lines");

            migrationBuilder.DropTable(
                name: "sales_categories");

            migrationBuilder.DropTable(
                name: "ledger_accounts");

            migrationBuilder.DropIndex(
                name: "IX_invoice_lines_LedgerAccountId",
                table: "invoice_lines");

            migrationBuilder.DropIndex(
                name: "IX_invoice_lines_SalesCategoryId",
                table: "invoice_lines");

            migrationBuilder.DropIndex(
                name: "IX_invoice_lines_TenantId_LedgerAccountId",
                table: "invoice_lines");

            migrationBuilder.DropColumn(
                name: "LedgerAccountId",
                table: "invoice_lines");

            migrationBuilder.DropColumn(
                name: "LedgerAccountNameSnapshot",
                table: "invoice_lines");

            migrationBuilder.DropColumn(
                name: "LedgerAccountNumberSnapshot",
                table: "invoice_lines");

            migrationBuilder.DropColumn(
                name: "SalesCategoryId",
                table: "invoice_lines");

            migrationBuilder.DropColumn(
                name: "SalesCategoryNameSnapshot",
                table: "invoice_lines");
        }
    }
}
