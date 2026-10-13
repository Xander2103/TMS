using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class PeppolFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BuyerReference",
                table: "customers",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PeppolDeliveryPreference",
                table: "customers",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Peppol");

            migrationBuilder.AddColumn<bool>(
                name: "PeppolEnabled",
                table: "customers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "PeppolValidatedAt",
                table: "customers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PeppolValidationReference",
                table: "customers",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PeppolValidationStatus",
                table: "customers",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Unknown");

            migrationBuilder.CreateTable(
                name: "peppol_incoming_documents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentKind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SupplierParticipant = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    SupplierName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    DocumentNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DocumentDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    PayloadStorageKey = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    PayloadHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ProviderMessageId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ReviewNote = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
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
                    table.PrimaryKey("PK_peppol_incoming_documents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "peppol_settings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LegalEntityId = table.Column<Guid>(type: "uuid", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    Environment = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ProviderKey = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    InvoiceEmailFallback = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    DefaultInvoiceNote = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
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
                    table.PrimaryKey("PK_peppol_settings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_peppol_settings_legal_entities_LegalEntityId",
                        column: x => x.LegalEntityId,
                        principalTable: "legal_entities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "peppol_transmissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentKind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Environment = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ProviderKey = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ProviderMessageId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SellerParticipant = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    BuyerParticipant = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    PayloadStorageKey = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    PayloadHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    PayloadVersion = table.Column<int>(type: "integer", nullable: false),
                    ErrorDetail = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ResponseCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    RetryCount = table.Column<int>(type: "integer", nullable: false),
                    CorrelationId = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("PK_peppol_transmissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_peppol_transmissions_invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "peppol_transmission_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TransmissionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Detail = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
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
                    table.PrimaryKey("PK_peppol_transmission_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_peppol_transmission_events_peppol_transmissions_Transmissio~",
                        column: x => x.TransmissionId,
                        principalTable: "peppol_transmissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_peppol_incoming_documents_TenantId_ProviderMessageId",
                table: "peppol_incoming_documents",
                columns: new[] { "TenantId", "ProviderMessageId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_peppol_incoming_documents_TenantId_Status",
                table: "peppol_incoming_documents",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_peppol_settings_LegalEntityId",
                table: "peppol_settings",
                column: "LegalEntityId");

            migrationBuilder.CreateIndex(
                name: "IX_peppol_settings_TenantId_LegalEntityId",
                table: "peppol_settings",
                columns: new[] { "TenantId", "LegalEntityId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_peppol_transmission_events_TenantId_TransmissionId",
                table: "peppol_transmission_events",
                columns: new[] { "TenantId", "TransmissionId" });

            migrationBuilder.CreateIndex(
                name: "IX_peppol_transmission_events_TransmissionId",
                table: "peppol_transmission_events",
                column: "TransmissionId");

            migrationBuilder.CreateIndex(
                name: "IX_peppol_transmissions_InvoiceId",
                table: "peppol_transmissions",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_peppol_transmissions_one_active_per_invoice",
                table: "peppol_transmissions",
                columns: new[] { "TenantId", "InvoiceId" },
                unique: true,
                filter: "\"Status\" NOT IN ('Failed','Rejected','Cancelled')");

            migrationBuilder.CreateIndex(
                name: "IX_peppol_transmissions_tenant_invoice",
                table: "peppol_transmissions",
                columns: new[] { "TenantId", "InvoiceId" });

            migrationBuilder.CreateIndex(
                name: "IX_peppol_transmissions_tenant_status",
                table: "peppol_transmissions",
                columns: new[] { "TenantId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "peppol_incoming_documents");

            migrationBuilder.DropTable(
                name: "peppol_settings");

            migrationBuilder.DropTable(
                name: "peppol_transmission_events");

            migrationBuilder.DropTable(
                name: "peppol_transmissions");

            migrationBuilder.DropColumn(
                name: "BuyerReference",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "PeppolDeliveryPreference",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "PeppolEnabled",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "PeppolValidatedAt",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "PeppolValidationReference",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "PeppolValidationStatus",
                table: "customers");
        }
    }
}
