using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <summary>
    /// Makes the audit trail append-only by CONSTRUCTION rather than by convention: a PostgreSQL
    /// trigger rejects every UPDATE and DELETE on audit_logs, so neither a future code change nor
    /// anyone holding the application's connection string can quietly rewrite or erase history.
    ///
    /// Inserts stay unrestricted. Retention/archival deletes must therefore run as a maintenance
    /// role that temporarily disables the trigger (documented in the operational checklist) — a
    /// deliberate, visible action instead of an ambient capability of the running application.
    ///
    /// SQLite (used by the test harness) does not support this syntax, so the statements are
    /// applied only on PostgreSQL.
    /// </summary>
    public partial class AuditAppendOnly : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider?.Contains("Npgsql") != true)
            {
                return;
            }

            migrationBuilder.Sql(@"
                CREATE OR REPLACE FUNCTION audit_logs_append_only() RETURNS trigger AS $$
                BEGIN
                    RAISE EXCEPTION 'audit_logs is append-only: % is not permitted', TG_OP
                        USING ERRCODE = 'insufficient_privilege';
                END;
                $$ LANGUAGE plpgsql;");

            migrationBuilder.Sql(@"
                DROP TRIGGER IF EXISTS trg_audit_logs_append_only ON audit_logs;
                CREATE TRIGGER trg_audit_logs_append_only
                    BEFORE UPDATE OR DELETE ON audit_logs
                    FOR EACH ROW EXECUTE FUNCTION audit_logs_append_only();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider?.Contains("Npgsql") != true)
            {
                return;
            }

            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_audit_logs_append_only ON audit_logs;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS audit_logs_append_only();");
        }
    }
}
