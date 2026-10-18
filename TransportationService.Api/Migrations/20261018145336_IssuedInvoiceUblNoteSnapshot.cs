using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class IssuedInvoiceUblNoteSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // MD-H3 follow-up: the UBL note as issued. New issues freeze it in the issue save; rows
            // issued before this column are filled ONCE at startup by InvoicePartySnapshotBackfill
            // from the deployment state (documented legacy policy) — no data step here.
            migrationBuilder.AddColumn<string>(
                name: "UblNoteSnapshot",
                table: "invoices",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UblNoteSnapshot",
                table: "invoices");
        }
    }
}
