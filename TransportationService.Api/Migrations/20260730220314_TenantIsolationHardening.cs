using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class TenantIsolationHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_legal_entities_peppol_participant",
                table: "legal_entities",
                columns: new[] { "PeppolScheme", "PeppolId" },
                unique: true,
                filter: "\"PeppolScheme\" IS NOT NULL AND \"PeppolId\" IS NOT NULL AND \"IsActive\" = true AND \"IsDeleted\" = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_legal_entities_peppol_participant",
                table: "legal_entities");
        }
    }
}
