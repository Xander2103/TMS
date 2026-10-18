using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <inheritdoc />
    public partial class IdentityNormalization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_users_TenantId_Email",
                table: "users");

            migrationBuilder.AddColumn<string>(
                name: "NormalizedEmail",
                table: "users",
                type: "character varying(250)",
                maxLength: 250,
                nullable: false,
                defaultValue: "");

            // MD-H11: deterministic backfill; accounts are NEVER merged or renamed silently — when two
            // accounts of a tenant only differ in letter case the migration stops and names them.
            migrationBuilder.Sql("""UPDATE users SET "NormalizedEmail" = lower(btrim("Email"));""");
            migrationBuilder.Sql("""
                DO $$
                DECLARE conflicts text;
                BEGIN
                    SELECT string_agg(format('tenant %s: %s (%s accounts)', "TenantId", "NormalizedEmail", n), '; ')
                      INTO conflicts
                      FROM (SELECT "TenantId", "NormalizedEmail", count(*) AS n FROM users GROUP BY 1, 2 HAVING count(*) > 1) d;
                    IF conflicts IS NOT NULL THEN
                        RAISE EXCEPTION 'Gebruikersaccounts verschillen enkel in hoofdletters van het e-mailadres: %. Voeg ze manueel samen of wijzig een adres en voer de migratie opnieuw uit.', conflicts;
                    END IF;
                END $$;
                """);

            // MD-M7: role names are unique per tenant regardless of letter case.
            migrationBuilder.Sql("""
                DO $$
                DECLARE conflicts text;
                BEGIN
                    SELECT string_agg(format('tenant %s: %s (%s rollen)', "TenantId", lname, n), '; ')
                      INTO conflicts
                      FROM (SELECT "TenantId", lower("Name") AS lname, count(*) AS n FROM roles GROUP BY 1, 2 HAVING count(*) > 1) d;
                    IF conflicts IS NOT NULL THEN
                        RAISE EXCEPTION 'Rolnamen verschillen enkel in hoofdletters: %. Hernoem een van beide rollen en voer de migratie opnieuw uit.', conflicts;
                    END IF;
                END $$;
                """);
            migrationBuilder.Sql("""CREATE UNIQUE INDEX "IX_roles_TenantId_LowerName" ON roles ("TenantId", lower("Name"));""");

            migrationBuilder.CreateIndex(
                name: "IX_users_NormalizedEmail",
                table: "users",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "IX_users_TenantId_NormalizedEmail",
                table: "users",
                columns: new[] { "TenantId", "NormalizedEmail" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_roles_TenantId_LowerName";""");

            migrationBuilder.DropIndex(
                name: "IX_users_NormalizedEmail",
                table: "users");

            migrationBuilder.DropIndex(
                name: "IX_users_TenantId_NormalizedEmail",
                table: "users");

            migrationBuilder.DropColumn(
                name: "NormalizedEmail",
                table: "users");

            migrationBuilder.CreateIndex(
                name: "IX_users_TenantId_Email",
                table: "users",
                columns: new[] { "TenantId", "Email" },
                unique: true);
        }
    }
}
