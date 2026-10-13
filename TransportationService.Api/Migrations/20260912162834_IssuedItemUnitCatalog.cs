using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <summary>
    /// Data-only migration (HR wave 2026-09-12 §8): issued-item templates move from free-text /
    /// UnitType names to the fixed catalogue in <c>Modules/Employees/IssuedItemUnits.cs</c>.
    /// Known legacy names map to their code, NULL/blank becomes the default "piece", anything
    /// unrecognised becomes "other". Nothing is deleted and the column keeps its type, so the
    /// change is backward compatible; Down is intentionally a no-op (codes are valid values in
    /// the previous schema too).
    /// </summary>
    public partial class IssuedItemUnitCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE issued_item_templates SET "Unit" = CASE
                    WHEN "Unit" IS NULL OR btrim("Unit") = '' THEN 'piece'
                    WHEN lower(btrim("Unit")) IN ('piece','pieces','pcs','stuk','stuks','st') THEN 'piece'
                    WHEN lower(btrim("Unit")) IN ('pair','paar','paire') THEN 'pair'
                    WHEN lower(btrim("Unit")) IN ('set','sets') THEN 'set'
                    WHEN lower(btrim("Unit")) IN ('box','boxes','doos','dozen','boîte') THEN 'box'
                    WHEN lower(btrim("Unit")) IN ('pack','pak','pakken','paquet') THEN 'pack'
                    WHEN lower(btrim("Unit")) IN ('roll','rol','rollen','rouleau') THEN 'roll'
                    WHEN lower(btrim("Unit")) IN ('meter','metre','mètre','m') THEN 'meter'
                    WHEN lower(btrim("Unit")) IN ('liter','litre','l') THEN 'liter'
                    WHEN lower(btrim("Unit")) IN ('kilogram','kg','kilo') THEN 'kilogram'
                    ELSE 'other'
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally empty: the mapped codes remain valid values for the previous schema.
        }
    }
}
