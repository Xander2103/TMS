using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class ContractTypeRequiresEndDate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "RequiresEndDate",
                table: "contract_types",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Backfill: existing BEP/UITZ/STUD rows (fixed-term, temp agency, student) require an
            // employment end date going forward; everything else keeps the column's false default.
            migrationBuilder.Sql(
                "UPDATE contract_types SET \"RequiresEndDate\" = true WHERE \"Code\" IN ('BEP','UITZ','STUD')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RequiresEndDate",
                table: "contract_types");
        }
    }
}
