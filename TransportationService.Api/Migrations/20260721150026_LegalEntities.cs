using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class LegalEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "legal_entities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LegalName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TradingName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CompanyNumber = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    VatNumber = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    PeppolId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    PeppolScheme = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    Street = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    HouseNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    PostalCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    City = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CountryCode = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    Email = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    PhoneNumber = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    Website = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    Iban = table.Column<string>(type: "character varying(34)", maxLength: 34, nullable: true),
                    Bic = table.Column<string>(type: "character varying(11)", maxLength: 11, nullable: true),
                    BankName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    DefaultCurrency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    PaymentTermDays = table.Column<int>(type: "integer", nullable: false),
                    InvoiceNumberFormat = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    InvoiceSequencePadding = table.Column<int>(type: "integer", nullable: false),
                    InvoicePrefix = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    InvoiceFooter = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    LogoStorageKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    LogoFileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    LogoContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("PK_legal_entities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "user_legal_entity_selections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    LegalEntityId = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("PK_user_legal_entity_selections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_user_legal_entity_selections_legal_entities_LegalEntityId",
                        column: x => x.LegalEntityId,
                        principalTable: "legal_entities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_legal_entities_tenant_default",
                table: "legal_entities",
                column: "TenantId",
                unique: true,
                filter: "\"IsDefault\" = true AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_legal_entities_TenantId_IsActive",
                table: "legal_entities",
                columns: new[] { "TenantId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_user_legal_entity_selections_LegalEntityId",
                table: "user_legal_entity_selections",
                column: "LegalEntityId");

            migrationBuilder.CreateIndex(
                name: "IX_user_legal_entity_selections_TenantId_UserId",
                table: "user_legal_entity_selections",
                columns: new[] { "TenantId", "UserId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_legal_entity_selections");

            migrationBuilder.DropTable(
                name: "legal_entities");
        }
    }
}
