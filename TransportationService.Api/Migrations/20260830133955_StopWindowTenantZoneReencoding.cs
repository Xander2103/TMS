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
    /// <b>Origin matters, per ROW.</b> Only values written by a client that stamped a wall clock as
    /// UTC may be converted. A window that arrived WITH an inbound EDI message is a real instant and
    /// is excluded — but only that: a window later typed into the web form on an EDI-created order
    /// carries the client encoding and IS converted. The two are told apart by comparing the stop's
    /// <c>CreatedAt</c> with the message's processing time; see the <c>EdiWrittenRow</c> predicate.
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
    /// Every count — including the ids of the windows a DST gap inverted — is written to
    /// <c>audit_logs</c>, one row per tenant: PostgreSQL <c>RAISE NOTICE</c> output is invisible
    /// under <c>dotnet ef database update</c>, which is how
    /// <c>scripts/deploy-transportationservice.sh</c> applies migrations. Query them afterwards
    /// with the statements in <c>docs/delivery/operations.md</c> §1.2b.
    /// </para>
    /// </summary>
    public partial class StopWindowTenantZoneReencoding : Migration
    {
        private const string MigrationId = "20260830133955_StopWindowTenantZoneReencoding";
        private const string MigrationName = "StopWindowTenantZoneReencoding";

        /// <summary>PL/pgSQL guard: both migrations call <c>gen_random_uuid()</c> (built in from 13).</summary>
        private static string RequirePostgres13(string migrationName) => $"""
            IF current_setting('server_version_num')::int < 130000 THEN
                        RAISE EXCEPTION '{migrationName}: PostgreSQL 13 of hoger vereist (gen_random_uuid); deze server is %.', current_setting('server_version');
                    END IF;
            """;

        /// <summary>A stop row that carries at least one window; rows with none are never rewritten.</summary>
        private const string HasAnyWindow = """
            (s."PlannedFrom" IS NOT NULL OR s."PlannedTo" IS NOT NULL
                                 OR s."RequestedFrom" IS NOT NULL OR s."RequestedTo" IS NOT NULL
                                 OR s."ConfirmedFrom" IS NOT NULL OR s."ConfirmedTo" IS NOT NULL
                                 OR s."EarliestAllowed" IS NOT NULL OR s."LatestAllowed" IS NOT NULL)
            """;

        /// <summary>
        /// PER-ROW EDI test: this stop already existed when the inbound message that created its
        /// order finished processing, so its window came with the message and is a true instant.
        /// <para>
        /// Order-scoped exclusion is not enough. A pre-Task-3 EDI import that carried a window threw
        /// at Npgsql (only <c>Kind = Utc</c> is accepted on <c>timestamptz</c>), so EDI-created
        /// orders in production have NULL windows — and a dispatcher could then open such an order
        /// in the web form and type one, in the wall-clock-as-UTC encoding this migration exists to
        /// repair. Excluding by order would leave exactly those rows wrong forever while reporting
        /// them as "already correct".
        /// </para>
        /// <para>
        /// Before stop identity became stable (C-01) an order edit deleted and recreated every stop,
        /// so a client-written stop always post-dates the message. <c>EdiService</c> creates the
        /// order and only then stamps <c>ProcessedAt</c> from the same clock, so an EDI-written stop
        /// always satisfies <c>CreatedAt &lt;= ProcessedAt</c>.
        /// </para>
        /// </summary>
        private const string EdiWrittenRow = """
            EXISTS (SELECT 1 FROM edi_messages m
                                          WHERE m."TenantId" = s."TenantId"
                                            AND m."IsDeleted" = false
                                            AND m."Direction" = 'Inbound'
                                            AND m."Status" = 'Processed'
                                            AND m."ResultEntityType" = 'TransportOrder'
                                            AND m."ResultEntityId" = s."TransportOrderId"::text
                                            AND s."CreatedAt" <= coalesce(m."ProcessedAt", m."CreatedAt"))
            """;

        /// <summary>The converted value of one column.</summary>
        private static string Conv(string column) =>
            $"""((s."{column}" AT TIME ZONE 'UTC') AT TIME ZONE zone::text)""";

        /// <summary>
        /// True when the pair was correctly ordered BEFORE the conversion and comes out inverted
        /// after it. Both bounds shift by the offset at their own instant, so a window straddling
        /// the spring-forward gap can flip — and TransportOrderService validates all four pairs,
        /// which would leave the order unsaveable.
        /// </summary>
        private static string DstInverts(string from, string to) => $"""
            (s."{to}" IS NOT NULL AND s."{from}" IS NOT NULL
                                          AND s."{to}" >= s."{from}" AND {Conv(to)} < {Conv(from)})
            """;

        /// <summary>Already inverted before the migration ran — not our damage, left untouched.</summary>
        private static string AlreadyInverted(string from, string to) =>
            $"""(s."{to}" IS NOT NULL AND s."{from}" IS NOT NULL AND s."{to}" < s."{from}")""";

        /// <summary>
        /// The upper bound of a pair, converted — and, when the conversion would invert it, rebuilt
        /// as "converted lower bound + the ORIGINAL width". The width is always derivable here (both
        /// bounds are non-null in every inverting case) and is read from the pre-update values on
        /// the right-hand side of the same statement, so the window keeps its length instead of
        /// collapsing to a point.
        /// </summary>
        private static string ConvertedUpperBound(string from, string to) => $"""
            CASE WHEN {DstInverts(from, to)}
                                        THEN {Conv(from)} + (s."{to}" - s."{from}")
                                        ELSE {Conv(to)}
                                   END
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
                    dst_ids uuid[];
                    preinverted_ids uuid[];
                BEGIN
                    {RequirePostgres13(MigrationName)}

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
                          FROM (SELECT {EdiWrittenRow} AS is_edi
                                  FROM transport_order_stops s
                                 WHERE s."TenantId" = r.tenant_id
                                   AND {HasAnyWindow}) q;

                        converted := 0;
                        dst_ids := ARRAY[]::uuid[];
                        preinverted_ids := ARRAY[]::uuid[];

                        -- A UTC tenant needs no rewrite: the expression is an identity there.
                        IF lower(zone) NOT IN ('utc', 'etc/utc', 'universal', 'zulu') THEN
                            -- Recorded BEFORE the update, because afterwards the evidence is gone and
                            -- audit_logs is append-only: which windows the DST gap flips (repaired,
                            -- keeping their original width) and which were already inverted before
                            -- this ran (NOT touched — that is not this migration's damage to fix).
                            SELECT coalesce(array_agg(s."Id") FILTER (
                                       WHERE {DstInverts("PlannedFrom", "PlannedTo")}
                                          OR {DstInverts("RequestedFrom", "RequestedTo")}
                                          OR {DstInverts("ConfirmedFrom", "ConfirmedTo")}
                                          OR {DstInverts("EarliestAllowed", "LatestAllowed")}), ARRAY[]::uuid[]),
                                   coalesce(array_agg(s."Id") FILTER (
                                       WHERE {AlreadyInverted("PlannedFrom", "PlannedTo")}
                                          OR {AlreadyInverted("RequestedFrom", "RequestedTo")}
                                          OR {AlreadyInverted("ConfirmedFrom", "ConfirmedTo")}
                                          OR {AlreadyInverted("EarliestAllowed", "LatestAllowed")}), ARRAY[]::uuid[])
                              INTO dst_ids, preinverted_ids
                              FROM transport_order_stops s
                             WHERE s."TenantId" = r.tenant_id
                               AND {HasAnyWindow}
                               AND NOT {EdiWrittenRow};

                            -- ONE statement for the whole tenant: NULL AT TIME ZONE ... is NULL, so
                            -- empty windows stay empty. Soft-deleted stops are included on purpose:
                            -- they are still referenced by packages/executions (C-01) and must not
                            -- diverge from their live twin.
                            UPDATE transport_order_stops s
                               SET "PlannedFrom"     = {Conv("PlannedFrom")},
                                   "PlannedTo"       = {ConvertedUpperBound("PlannedFrom", "PlannedTo")},
                                   "RequestedFrom"   = {Conv("RequestedFrom")},
                                   "RequestedTo"     = {ConvertedUpperBound("RequestedFrom", "RequestedTo")},
                                   "ConfirmedFrom"   = {Conv("ConfirmedFrom")},
                                   "ConfirmedTo"     = {ConvertedUpperBound("ConfirmedFrom", "ConfirmedTo")},
                                   "EarliestAllowed" = {Conv("EarliestAllowed")},
                                   "LatestAllowed"   = {ConvertedUpperBound("EarliestAllowed", "LatestAllowed")}
                             WHERE s."TenantId" = r.tenant_id
                               AND {HasAnyWindow}
                               AND NOT {EdiWrittenRow};
                            GET DIAGNOSTICS converted = ROW_COUNT;
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
                                'ediWrittenStopRows', edi_excluded)::text,
                            json_build_object(
                                'timezoneUsed', zone,
                                'timezoneSource', zone_source,
                                'converted', converted,
                                'skippedEdi', edi_excluded,
                                'dstInvertedWindowsRepaired', cardinality(dst_ids),
                                'dstInvertedStopIds', dst_ids[1:500],
                                'dstInvertedStopIdsTruncated', cardinality(dst_ids) > 500,
                                'alreadyInvertedLeftUntouched', cardinality(preinverted_ids),
                                'alreadyInvertedStopIds', preinverted_ids[1:500])::text,
                            now(), NULL, NULL);
                    END LOOP;
                END $$;
                """);
        }

        /// <summary>
        /// Exact inverse: read each instant as tenant wall clock and store that wall clock as UTC
        /// again, under the same zone resolution and the same per-row EDI exclusion. Rolling this
        /// back means rolling the application back to the client that produced the old encoding, so
        /// restoring it is the correct behaviour. Two documented asymmetries: it is not bit-exact
        /// for an instant whose tenant wall clock falls in a DST gap or repeated hour, and a window
        /// whose upper bound was rebuilt by <c>ConvertedUpperBound</c> is not un-rebuilt — the
        /// affected ids are in the <c>Up</c> audit row. A row is written for EVERY tenant, including
        /// UTC ones, so the rollback record lines up one-to-one with the <c>Up</c> record.
        /// </summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"""
                DO $$
                DECLARE
                    r RECORD;
                    zone text;
                    zone_source text;
                    reverted bigint;
                BEGIN
                    {RequirePostgres13(MigrationName + ".Down")}

                    FOR r IN
                        SELECT t."Id" AS tenant_id, ts."Timezone" AS raw_zone
                          FROM tenants t
                          LEFT JOIN tenant_settings ts ON ts."TenantId" = t."Id"
                    LOOP
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

                        reverted := 0;
                        IF lower(zone) NOT IN ('utc', 'etc/utc', 'universal', 'zulu') THEN
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
                               AND NOT {EdiWrittenRow};
                            GET DIAGNOSTICS reverted = ROW_COUNT;
                        END IF;

                        INSERT INTO audit_logs
                            ("Id", "TenantId", "UserId", "EntityType", "EntityId", "Action",
                             "OldValuesJson", "NewValuesJson", "Timestamp", "IpAddress", "CorrelationId")
                        VALUES (
                            gen_random_uuid(), r.tenant_id, NULL, 'DataMigration',
                            '{MigrationId}', '{MigrationName}.Down',
                            NULL,
                            json_build_object(
                                'timezoneUsed', zone,
                                'timezoneSource', zone_source,
                                'reverted', reverted)::text,
                            now(), NULL, NULL);
                    END LOOP;
                END $$;
                """);
        }
    }
}
