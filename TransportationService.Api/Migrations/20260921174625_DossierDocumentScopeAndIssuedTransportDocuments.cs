using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class DossierDocumentScopeAndIssuedTransportDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "TransportOrderId",
                table: "order_documents",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "DossierId",
                table: "order_documents",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "issued_transport_documents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TransportOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    DossierId = table.Column<Guid>(type: "uuid", nullable: true),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DocumentNumber = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ExternalNumber = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    IssuedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
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
                    table.PrimaryKey("PK_issued_transport_documents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_issued_transport_documents_transport_dossiers_DossierId",
                        column: x => x.DossierId,
                        principalTable: "transport_dossiers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_issued_transport_documents_transport_orders_TransportOrderId",
                        column: x => x.TransportOrderId,
                        principalTable: "transport_orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "transport_document_sequences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("PK_transport_document_sequences", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_order_documents_DossierId",
                table: "order_documents",
                column: "DossierId");

            migrationBuilder.CreateIndex(
                name: "IX_order_documents_TenantId_DossierId",
                table: "order_documents",
                columns: new[] { "TenantId", "DossierId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_order_documents_order_or_dossier",
                table: "order_documents",
                sql: "\"TransportOrderId\" IS NOT NULL OR \"DossierId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_issued_transport_documents_DossierId",
                table: "issued_transport_documents",
                column: "DossierId");

            migrationBuilder.CreateIndex(
                name: "IX_issued_transport_documents_TenantId_DocumentNumber",
                table: "issued_transport_documents",
                columns: new[] { "TenantId", "DocumentNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_issued_transport_documents_TenantId_DossierId",
                table: "issued_transport_documents",
                columns: new[] { "TenantId", "DossierId" });

            migrationBuilder.CreateIndex(
                name: "IX_issued_transport_documents_TenantId_RequestId",
                table: "issued_transport_documents",
                columns: new[] { "TenantId", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_issued_transport_documents_TenantId_TransportOrderId",
                table: "issued_transport_documents",
                columns: new[] { "TenantId", "TransportOrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_issued_transport_documents_TransportOrderId",
                table: "issued_transport_documents",
                column: "TransportOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_transport_document_sequences_TenantId_Kind_Year",
                table: "transport_document_sequences",
                columns: new[] { "TenantId", "Kind", "Year" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.AddForeignKey(
                name: "FK_order_documents_transport_dossiers_DossierId",
                table: "order_documents",
                column: "DossierId",
                principalTable: "transport_dossiers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // The data step is plain SQL for the production provider; SQLite test databases
            // are created from the model and run the same statement from their own test.
            if (migrationBuilder.ActiveProvider?.Contains("Npgsql") != true)
            {
                return;
            }

            // D6: every existing order document gets the OWNING dossier of its order (wrapper first,
            // else the oldest active link — the OwningDossierResolver precedence). Orders without a
            // dossier keep NULL. Only this one column is written: nothing is moved or deleted, no
            // file is touched. Idempotent (fills NULLs only). TransportOrderId merely lost its
            // NOT NULL above — no row was rewritten for that.
            migrationBuilder.Sql(Modules.Orders.Services.OrderDocumentDossierBackfill.Sql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_order_documents_transport_dossiers_DossierId",
                table: "order_documents");

            migrationBuilder.DropTable(
                name: "issued_transport_documents");

            migrationBuilder.DropTable(
                name: "transport_document_sequences");

            migrationBuilder.DropIndex(
                name: "IX_order_documents_DossierId",
                table: "order_documents");

            migrationBuilder.DropIndex(
                name: "IX_order_documents_TenantId_DossierId",
                table: "order_documents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_order_documents_order_or_dossier",
                table: "order_documents");

            migrationBuilder.DropColumn(
                name: "DossierId",
                table: "order_documents");

            migrationBuilder.AlterColumn<Guid>(
                name: "TransportOrderId",
                table: "order_documents",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
