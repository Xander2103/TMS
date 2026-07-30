using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <summary>
    /// C3 data hygiene. Historic portal-invite outbox rows persisted the raw activation link in
    /// their rendered body (and a raw-token prefix in the idempotency key), so every activation
    /// token that was ever mailed must be treated as exposed-at-rest:
    ///
    /// 1. scrub the body of every portal-invite outbox row (the row itself stays, as the audit
    ///    trail of the send);
    /// 2. revoke every still-open activation token — a fresh invite mints a fresh token, and
    ///    from now on the dispatcher scrubs the link right after delivery is decided.
    ///
    /// Data-only fix over existing rows, written in portable SQL (runs on PostgreSQL and on the
    /// SQLite test harness alike). Irreversible by design: Down cannot restore scrubbed links,
    /// and un-revoking security tokens would be a hole, not a rollback.
    /// </summary>
    public partial class TokenPersistenceHygiene : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE outbox_messages
                SET "Body" = '[inhoud met eenmalige link verwijderd na afhandeling]'
                WHERE "Kind" = 'portal_user_invited';
                """);

            migrationBuilder.Sql("""
                UPDATE user_security_tokens
                SET "RevokedAt" = CURRENT_TIMESTAMP
                WHERE "Kind" = 'Activation' AND "UsedAt" IS NULL AND "RevokedAt" IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally empty: scrubbed links are gone and revoked tokens stay revoked.
        }
    }
}
