using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class IssuedTransportDocumentReplacement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ReplacedAt",
                table: "issued_transport_documents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReplacedByDocumentId",
                table: "issued_transport_documents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReplacementReason",
                table: "issued_transport_documents",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            // M9: one ACTIVE document per order and kind from now on. Orders that already hold
            // several (issued before the rule) keep every document; each older one is marked as
            // replaced by the next one issued after it, so only the newest stays active.
            migrationBuilder.Sql("""
                UPDATE issued_transport_documents AS d
                SET "ReplacedByDocumentId" = chain.next_id,
                    "ReplacedAt" = chain.next_issued_at,
                    "ReplacementReason" = 'Automatisch: vervangen door een later uitgegeven document (vóór de regel één actief document per opdracht).'
                FROM (
                    SELECT "Id",
                           LEAD("Id") OVER w AS next_id,
                           LEAD("IssuedAt") OVER w AS next_issued_at
                    FROM issued_transport_documents
                    WHERE "IsDeleted" = false
                    WINDOW w AS (PARTITION BY "TenantId", "TransportOrderId", "Kind" ORDER BY "IssuedAt", "DocumentNumber")
                ) AS chain
                WHERE d."Id" = chain."Id" AND chain.next_id IS NOT NULL AND d."ReplacedByDocumentId" IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReplacedAt",
                table: "issued_transport_documents");

            migrationBuilder.DropColumn(
                name: "ReplacedByDocumentId",
                table: "issued_transport_documents");

            migrationBuilder.DropColumn(
                name: "ReplacementReason",
                table: "issued_transport_documents");
        }
    }
}
