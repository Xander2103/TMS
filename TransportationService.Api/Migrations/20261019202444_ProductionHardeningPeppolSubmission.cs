using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <summary>
    /// Production hardening H3/EXT-03 (additive, nullable, metadata-only on PostgreSQL): the
    /// Submitting claim (IdempotencyKey + SubmissionClaimedAt, persisted before the provider call)
    /// and NextPollAt for starvation-free status polling. The new "Submitting" status value needs no
    /// schema change (string column, 30 chars). Down drops the columns; run it only after no row is
    /// left in Submitting.
    /// </summary>
    public partial class ProductionHardeningPeppolSubmission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "IdempotencyKey",
                table: "peppol_transmissions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "NextPollAt",
                table: "peppol_transmissions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SubmissionClaimedAt",
                table: "peppol_transmissions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_peppol_transmissions_status_next_poll",
                table: "peppol_transmissions",
                columns: new[] { "Status", "NextPollAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_peppol_transmissions_status_next_poll",
                table: "peppol_transmissions");

            migrationBuilder.DropColumn(
                name: "IdempotencyKey",
                table: "peppol_transmissions");

            migrationBuilder.DropColumn(
                name: "NextPollAt",
                table: "peppol_transmissions");

            migrationBuilder.DropColumn(
                name: "SubmissionClaimedAt",
                table: "peppol_transmissions");
        }
    }
}
