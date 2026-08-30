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
    /// Fix: re-interpret each stored naive wall clock in the tenant's own
    /// <c>tenant_settings."Timezone"</c> and store the resulting instant —
    /// <c>((col AT TIME ZONE 'UTC') AT TIME ZONE ts."Timezone")</c>.
    /// </para>
    /// <para>
    /// Scope is deliberately narrow: only the eight client-written window columns of
    /// <c>transport_order_stops</c>. Execution, POD, ETA, scan, audit and status-history stamps
    /// come from the server clock, are true UTC instants, and are NOT touched. Neither are
    /// <c>dock_appointments</c>: that screen still writes AND reads the raw wall clock, so it is
    /// internally consistent today and must be re-encoded together with its own client fix.
    /// </para>
    /// <para>
    /// Runs exactly once — <c>__EFMigrationsHistory</c> is the marker; EF never replays an applied
    /// migration, so no additional flag is needed. It must be applied in the SAME release as the
    /// client-side convention fix, with no order saved in between (the deploy script already stops
    /// the old app, migrates, then starts the new one).
    /// </para>
    /// </summary>
    public partial class StopWindowTenantZoneReencoding : Migration
    {
        /// <summary>
        /// Guard shared by <c>Up</c> and <c>Down</c>: skip tenants on UTC (the expression is a
        /// mathematical no-op there anyway, this just avoids rewriting the rows) and tenants whose
        /// <c>Timezone</c> is not a name PostgreSQL knows — the setting is free text, and an
        /// unknown name would abort the whole migration instead of skipping one tenant.
        /// </summary>
        private const string TenantZoneGuard = """
                       lower(btrim(ts."Timezone")) NOT IN ('utc', 'etc/utc', 'universal', 'zulu')
                   AND EXISTS (SELECT 1 FROM pg_timezone_names z WHERE lower(z.name) = lower(btrim(ts."Timezone")))
            """;

        private const string HasAnyWindow = """
                   (s."PlannedFrom" IS NOT NULL OR s."PlannedTo" IS NOT NULL
                    OR s."RequestedFrom" IS NOT NULL OR s."RequestedTo" IS NOT NULL
                    OR s."ConfirmedFrom" IS NOT NULL OR s."ConfirmedTo" IS NOT NULL
                    OR s."EarliestAllowed" IS NOT NULL OR s."LatestAllowed" IS NOT NULL)
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"""
                DO $$
                DECLARE
                    unknown_zones text;
                    edi_rows bigint;
                    touched bigint;
                BEGIN
                    -- Free-text timezone settings that PostgreSQL cannot resolve are reported, not
                    -- guessed: those tenants keep the old encoding and need a manual pass.
                    SELECT string_agg(DISTINCT ts."Timezone", ', ')
                      INTO unknown_zones
                      FROM tenant_settings ts
                     WHERE lower(btrim(ts."Timezone")) NOT IN ('utc', 'etc/utc', 'universal', 'zulu')
                       AND NOT EXISTS (SELECT 1 FROM pg_timezone_names z
                                        WHERE lower(z.name) = lower(btrim(ts."Timezone")));
                    IF unknown_zones IS NOT NULL THEN
                        RAISE WARNING 'StopWindowTenantZoneReencoding: onbekende tijdzone-instelling(en) overgeslagen: %. Corrigeer TenantSettings.Timezone en her-encodeer die tenants handmatig.', unknown_zones;
                    END IF;

                    -- EDI partners send a real offset, so their rows were already true instants and
                    -- are shifted once by this migration. Volume is reported so it can be checked.
                    SELECT count(*)
                      INTO edi_rows
                      FROM transport_order_stops s
                      JOIN edi_messages m
                        ON m."TenantId" = s."TenantId"
                       AND m."ResultEntityType" = 'TransportOrder'
                       AND m."ResultEntityId" = s."TransportOrderId"::text
                     WHERE {HasAnyWindow};
                    IF edi_rows > 0 THEN
                        RAISE WARNING 'StopWindowTenantZoneReencoding: % stopregel(s) van EDI-opdrachten worden mee verschoven; controleer die vensters met de partner.', edi_rows;
                    END IF;

                    -- The re-encoding itself. NULL AT TIME ZONE ... is NULL, so empty windows stay
                    -- empty. Soft-deleted stops are included on purpose: they are still referenced
                    -- by packages/executions (C-01) and must not diverge from their live twins.
                    UPDATE transport_order_stops s
                       SET "PlannedFrom"     = ((s."PlannedFrom"     AT TIME ZONE 'UTC') AT TIME ZONE btrim(ts."Timezone")),
                           "PlannedTo"       = ((s."PlannedTo"       AT TIME ZONE 'UTC') AT TIME ZONE btrim(ts."Timezone")),
                           "RequestedFrom"   = ((s."RequestedFrom"   AT TIME ZONE 'UTC') AT TIME ZONE btrim(ts."Timezone")),
                           "RequestedTo"     = ((s."RequestedTo"     AT TIME ZONE 'UTC') AT TIME ZONE btrim(ts."Timezone")),
                           "ConfirmedFrom"   = ((s."ConfirmedFrom"   AT TIME ZONE 'UTC') AT TIME ZONE btrim(ts."Timezone")),
                           "ConfirmedTo"     = ((s."ConfirmedTo"     AT TIME ZONE 'UTC') AT TIME ZONE btrim(ts."Timezone")),
                           "EarliestAllowed" = ((s."EarliestAllowed" AT TIME ZONE 'UTC') AT TIME ZONE btrim(ts."Timezone")),
                           "LatestAllowed"   = ((s."LatestAllowed"   AT TIME ZONE 'UTC') AT TIME ZONE btrim(ts."Timezone"))
                      FROM tenant_settings ts
                     WHERE ts."TenantId" = s."TenantId"
                       AND {TenantZoneGuard}
                       AND {HasAnyWindow};

                    GET DIAGNOSTICS touched = ROW_COUNT;
                    RAISE NOTICE 'StopWindowTenantZoneReencoding: % stopregel(s) her-geëncodeerd naar de tenant-tijdzone.', touched;
                END $$;
                """);
        }

        /// <summary>
        /// Exact inverse: read each instant as tenant wall clock and store that wall clock as UTC
        /// again. Rolling this back means rolling the application back to the client that produced
        /// the old encoding, so restoring it is the correct behaviour. Not bit-exact for an instant
        /// whose tenant wall clock falls in a DST gap or repeated hour — a window the operator has
        /// to re-check by hand; see the task report.
        /// </summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"""
                DO $$
                DECLARE
                    touched bigint;
                BEGIN
                    UPDATE transport_order_stops s
                       SET "PlannedFrom"     = ((s."PlannedFrom"     AT TIME ZONE btrim(ts."Timezone")) AT TIME ZONE 'UTC'),
                           "PlannedTo"       = ((s."PlannedTo"       AT TIME ZONE btrim(ts."Timezone")) AT TIME ZONE 'UTC'),
                           "RequestedFrom"   = ((s."RequestedFrom"   AT TIME ZONE btrim(ts."Timezone")) AT TIME ZONE 'UTC'),
                           "RequestedTo"     = ((s."RequestedTo"     AT TIME ZONE btrim(ts."Timezone")) AT TIME ZONE 'UTC'),
                           "ConfirmedFrom"   = ((s."ConfirmedFrom"   AT TIME ZONE btrim(ts."Timezone")) AT TIME ZONE 'UTC'),
                           "ConfirmedTo"     = ((s."ConfirmedTo"     AT TIME ZONE btrim(ts."Timezone")) AT TIME ZONE 'UTC'),
                           "EarliestAllowed" = ((s."EarliestAllowed" AT TIME ZONE btrim(ts."Timezone")) AT TIME ZONE 'UTC'),
                           "LatestAllowed"   = ((s."LatestAllowed"   AT TIME ZONE btrim(ts."Timezone")) AT TIME ZONE 'UTC')
                      FROM tenant_settings ts
                     WHERE ts."TenantId" = s."TenantId"
                       AND {TenantZoneGuard}
                       AND {HasAnyWindow};

                    GET DIAGNOSTICS touched = ROW_COUNT;
                    RAISE NOTICE 'StopWindowTenantZoneReencoding (Down): % stopregel(s) terug naar de oude wandklok-als-UTC-encodering.', touched;
                END $$;
                """);
        }
    }
}
