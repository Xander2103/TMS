using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <summary>
    /// C-03 data repair — ONE-OFF re-encoding of the stop time windows.
    /// <para>
    /// Until the "one transport-time convention" fix, the web client sent a typed wall clock with
    /// a <c>Z</c> suffix (<c>2026-08-30T08:00:00Z</c> for a dispatcher's 08:00), so the API stored
    /// the wall clock AS IF it were UTC. The client now converts wall clock ↔ instant through the
    /// tenant zone, which makes every historical row read back one or two hours late.
    /// </para>
    /// <para>
    /// Fix: re-interpret each stored naive wall clock in the tenant's own zone —
    /// <c>((col AT TIME ZONE 'UTC') AT TIME ZONE &lt;tenant zone&gt;)</c>.
    /// </para>
    /// <para>
    /// <b>Origin matters.</b> Only rows written by a client that stamped a wall clock as UTC may be
    /// converted. Rows created from an inbound EDI message are excluded: a partner sends a real
    /// instant, so those rows are either already correct or do not exist. Every other writer of
    /// these columns wrote the wall-clock-as-UTC encoding. See the task report for the full
    /// classification and the evidence per origin.
    /// </para>
    /// <para>
    /// Scope is otherwise narrow: only the eight client-written window columns of
    /// <c>transport_order_stops</c>. Execution, POD, ETA, scan, audit and status-history stamps
    /// come from the server clock, are true UTC instants, and are NOT touched. Neither are
    /// <c>dock_appointments</c>: that screen still writes AND reads the raw wall clock, so it is
    /// internally consistent today and must be re-encoded together with its own client fix.
    /// </para>
    /// <para>
    /// Runs exactly once — <c>__EFMigrationsHistory</c> is the marker; EF never replays an applied
    /// migration and inserts the history row in this migration's own transaction, so a failure
    /// rolls everything back together. It must be applied in the SAME release as the client-side
    /// convention fix, with no order saved in between (the deploy script already stops the old app,
    /// migrates, then starts the new one). The expression is NOT idempotent if the raw SQL is run a
    /// second time by hand — running it twice shifts by 2× the offset.
    /// </para>
    /// <para>
    /// Every count is written to <c>audit_logs</c>, one row per tenant: PostgreSQL
    /// <c>RAISE NOTICE</c> output is invisible under <c>dotnet ef database update</c>, which is how
    /// <c>scripts/deploy-transportationservice.sh</c> applies migrations. Query them afterwards
    /// with the statements in <c>docs/delivery/operations.md</c>.
    /// </para>
    /// </summary>
    public partial class StopWindowTenantZoneReencoding : Migration
    {
        private const string MigrationId = "20260830133955_StopWindowTenantZoneReencoding";
        private const string MigrationName = "StopWindowTenantZoneReencoding";

        /// <summary>A stop row that carries at least one window; rows with none are never rewritten.</summary>
        private const string HasAnyWindow = """
            (s."PlannedFrom" IS NOT NULL OR s."PlannedTo" IS NOT NULL
                                 OR s."RequestedFrom" IS NOT NULL OR s."RequestedTo" IS NOT NULL
                                 OR s."ConfirmedFrom" IS NOT NULL OR s."ConfirmedTo" IS NOT NULL
                                 OR s."EarliestAllowed" IS NOT NULL OR s."LatestAllowed" IS NOT NULL)
            """;

        /// <summary>
        /// True when this stop's order was created from an inbound EDI message. <c>EdiService</c>
        /// sets <c>Status = Processed</c> and the two result columns in the same block, so the
        /// three conditions together identify exactly the EDI-created orders.
        /// </summary>
        private const string IsEdiSourced = """
            EXISTS (SELECT 1 FROM edi_messages m
                                          WHERE m."TenantId" = s."TenantId"
                                            AND m."IsDeleted" = false
                                            AND m."Direction" = 'Inbound'
                                            AND m."Status" = 'Processed'
                                            AND m."ResultEntityType" = 'TransportOrder'
                                            AND m."ResultEntityId" = s."TransportOrderId"::text)
            """;

        /// <summary>Collapses a window that DST inverted (see the <c>inverted</c> counter).</summary>
        private static string CollapseInverted(string from, string to) => $"""
            UPDATE transport_order_stops s
                           SET "{to}" = s."{from}"
                         WHERE s."TenantId" = r.tenant_id
                           AND s."{to}" < s."{from}"
                           AND NOT {IsEdiSourced};
                        GET DIAGNOSTICS n = ROW_COUNT;
                        inverted := inverted + n;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"""
                DO $$
                DECLARE
                    r RECORD;
                    zone text;
                    zone_source text;
                    candidates bigint;
                    edi_excluded bigint;
                    converted bigint;
                    inverted bigint;
                    n bigint;
                BEGIN
                    -- LEFT JOIN, not an inner join: a tenant whose settings row does not exist yet
                    -- (CompanySettingsService creates it lazily) must be treated exactly as the C#
                    -- does — TenantTimeZone.Resolve(null) = Europe/Amsterdam. An inner join would
                    -- skip that tenant here while the API and the web client kept reading its rows
                    -- as Amsterdam time, leaving it permanently 1–2 h out with no signal.
                    FOR r IN
                        SELECT t."Id" AS tenant_id, ts."Timezone" AS raw_zone
                          FROM tenants t
                          LEFT JOIN tenant_settings ts ON ts."TenantId" = t."Id"
                    LOOP
                        -- The setting is unvalidated free text, and PostgreSQL aborts on an unknown
                        -- zone name, so an unusable value degrades to the tenant default instead of
                        -- taking down the migration. Mirrors TenantTimeZone.Resolve exactly.
                        IF r.raw_zone IS NOT NULL
                           AND EXISTS (SELECT 1 FROM pg_timezone_names z
                                        WHERE lower(z.name) = lower(btrim(r.raw_zone))) THEN
                            zone := btrim(r.raw_zone);
                            zone_source := 'tenant_settings';
                        ELSIF r.raw_zone IS NULL THEN
                            zone := 'Europe/Amsterdam';
                            zone_source := 'default (geen tenant_settings-rij)';
                        ELSE
                            zone := 'Europe/Amsterdam';
                            zone_source := 'default (onbruikbare tijdzone: ' || r.raw_zone || ')';
                        END IF;

                        SELECT count(*) FILTER (WHERE NOT q.is_edi),
                               count(*) FILTER (WHERE q.is_edi)
                          INTO candidates, edi_excluded
                          FROM (SELECT {IsEdiSourced} AS is_edi
                                  FROM transport_order_stops s
                                 WHERE s."TenantId" = r.tenant_id
                                   AND {HasAnyWindow}) q;

                        converted := 0;
                        inverted := 0;

                        -- A UTC tenant needs no rewrite: the expression is an identity there.
                        IF lower(zone) NOT IN ('utc', 'etc/utc', 'universal', 'zulu') THEN
                            -- NULL AT TIME ZONE ... is NULL, so empty windows stay empty.
                            -- Soft-deleted stops are included on purpose: they are still referenced
                            -- by packages/executions (C-01) and must not diverge from their live twin.
                            UPDATE transport_order_stops s
                               SET "PlannedFrom"     = ((s."PlannedFrom"     AT TIME ZONE 'UTC') AT TIME ZONE zone::text),
                                   "PlannedTo"       = ((s."PlannedTo"       AT TIME ZONE 'UTC') AT TIME ZONE zone::text),
                                   "RequestedFrom"   = ((s."RequestedFrom"   AT TIME ZONE 'UTC') AT TIME ZONE zone::text),
                                   "RequestedTo"     = ((s."RequestedTo"     AT TIME ZONE 'UTC') AT TIME ZONE zone::text),
                                   "ConfirmedFrom"   = ((s."ConfirmedFrom"   AT TIME ZONE 'UTC') AT TIME ZONE zone::text),
                                   "ConfirmedTo"     = ((s."ConfirmedTo"     AT TIME ZONE 'UTC') AT TIME ZONE zone::text),
                                   "EarliestAllowed" = ((s."EarliestAllowed" AT TIME ZONE 'UTC') AT TIME ZONE zone::text),
                                   "LatestAllowed"   = ((s."LatestAllowed"   AT TIME ZONE 'UTC') AT TIME ZONE zone::text)
                             WHERE s."TenantId" = r.tenant_id
                               AND {HasAnyWindow}
                               AND NOT {IsEdiSourced};
                            GET DIAGNOSTICS converted = ROW_COUNT;

                            -- Both bounds shift by the offset AT THEIR OWN INSTANT, so a window
                            -- straddling the spring-forward gap can come out inverted — and the
                            -- order then refuses to save (TransportOrderService validates all four
                            -- pairs). Collapse those to a point rather than leave them unsaveable.
                            {CollapseInverted("PlannedFrom", "PlannedTo")}
                            {CollapseInverted("RequestedFrom", "RequestedTo")}
                            {CollapseInverted("ConfirmedFrom", "ConfirmedTo")}
                            {CollapseInverted("EarliestAllowed", "LatestAllowed")}
                        END IF;

                        INSERT INTO audit_logs
                            ("Id", "TenantId", "UserId", "EntityType", "EntityId", "Action",
                             "OldValuesJson", "NewValuesJson", "Timestamp", "IpAddress", "CorrelationId")
                        VALUES (
                            gen_random_uuid(), r.tenant_id, NULL, 'DataMigration',
                            '{MigrationId}', '{MigrationName}',
                            json_build_object(
                                'timezoneSetting', r.raw_zone,
                                'candidateStopRows', candidates,
                                'ediStopRows', edi_excluded)::text,
                            json_build_object(
                                'timezoneUsed', zone,
                                'timezoneSource', zone_source,
                                'converted', converted,
                                'skippedEdi', edi_excluded,
                                'invertedWindowsCollapsed', inverted)::text,
                            now(), NULL, NULL);
                    END LOOP;
                END $$;
                """);
        }

        /// <summary>
        /// Exact inverse: read each instant as tenant wall clock and store that wall clock as UTC
        /// again, under the same zone resolution and the same EDI exclusion. Rolling this back means
        /// rolling the application back to the client that produced the old encoding, so restoring
        /// it is the correct behaviour. Not bit-exact for an instant whose tenant wall clock falls
        /// in a DST gap or repeated hour, and the collapsed inverted windows are not un-collapsed —
        /// both are recorded in the task report.
        /// </summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"""
                DO $$
                DECLARE
                    r RECORD;
                    zone text;
                    reverted bigint;
                BEGIN
                    FOR r IN
                        SELECT t."Id" AS tenant_id, ts."Timezone" AS raw_zone
                          FROM tenants t
                          LEFT JOIN tenant_settings ts ON ts."TenantId" = t."Id"
                    LOOP
                        IF r.raw_zone IS NOT NULL
                           AND EXISTS (SELECT 1 FROM pg_timezone_names z
                                        WHERE lower(z.name) = lower(btrim(r.raw_zone))) THEN
                            zone := btrim(r.raw_zone);
                        ELSE
                            zone := 'Europe/Amsterdam';
                        END IF;

                        CONTINUE WHEN lower(zone) IN ('utc', 'etc/utc', 'universal', 'zulu');

                        UPDATE transport_order_stops s
                           SET "PlannedFrom"     = ((s."PlannedFrom"     AT TIME ZONE zone::text) AT TIME ZONE 'UTC'),
                               "PlannedTo"       = ((s."PlannedTo"       AT TIME ZONE zone::text) AT TIME ZONE 'UTC'),
                               "RequestedFrom"   = ((s."RequestedFrom"   AT TIME ZONE zone::text) AT TIME ZONE 'UTC'),
                               "RequestedTo"     = ((s."RequestedTo"     AT TIME ZONE zone::text) AT TIME ZONE 'UTC'),
                               "ConfirmedFrom"   = ((s."ConfirmedFrom"   AT TIME ZONE zone::text) AT TIME ZONE 'UTC'),
                               "ConfirmedTo"     = ((s."ConfirmedTo"     AT TIME ZONE zone::text) AT TIME ZONE 'UTC'),
                               "EarliestAllowed" = ((s."EarliestAllowed" AT TIME ZONE zone::text) AT TIME ZONE 'UTC'),
                               "LatestAllowed"   = ((s."LatestAllowed"   AT TIME ZONE zone::text) AT TIME ZONE 'UTC')
                         WHERE s."TenantId" = r.tenant_id
                           AND {HasAnyWindow}
                           AND NOT {IsEdiSourced};
                        GET DIAGNOSTICS reverted = ROW_COUNT;

                        INSERT INTO audit_logs
                            ("Id", "TenantId", "UserId", "EntityType", "EntityId", "Action",
                             "OldValuesJson", "NewValuesJson", "Timestamp", "IpAddress", "CorrelationId")
                        VALUES (
                            gen_random_uuid(), r.tenant_id, NULL, 'DataMigration',
                            '{MigrationId}', '{MigrationName}.Down',
                            NULL,
                            json_build_object('timezoneUsed', zone, 'reverted', reverted)::text,
                            now(), NULL, NULL);
                    END LOOP;
                END $$;
                """);
        }
    }
}
