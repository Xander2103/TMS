using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportationService.Api.Migrations
{
    /// <summary>
    /// C-01 data repair — re-points package stop pins that dangle on soft-deleted stops.
    /// <para>
    /// Until stop identity became stable, every order edit deleted and recreated ALL stops. The
    /// auditing interceptor turns that delete into a soft delete, so the FK's <c>SetNull</c> never
    /// fired and <c>packages."LoadingStopId"/"DeliveryStopId"</c> kept pointing at rows every query
    /// filter hides. That is worse than a null pin: <c>PackageScanProcessor</c> compares the pin
    /// with the stop being scanned, so a dangling pin BLOCKS every load/unload scan of that package
    /// ("staat gepland voor een andere laadstop") and raises a WrongStopPackage exception, while a
    /// null pin falls back to the order's first loading / last unloading stop by design.
    /// </para>
    /// <para>
    /// Repaired only where the answer is unambiguous: within the same order there is EXACTLY ONE
    /// live stop with the same <c>Sequence</c>, the same <c>StopType</c> and the same place (same
    /// <c>LocationId</c>, or — for free-address stops — the same address/postcode/city/country).
    /// Sequence + type alone are not enough: an edit that reordered or inserted stops shifts them,
    /// and the place is what proves the match is the same physical stop. Anything else is left
    /// exactly as it is and reported; guessing a delivery address is worse than a loud failure.
    /// </para>
    /// <para>
    /// Executions, PODs, ETAs, scans, package events and incidents are NOT touched: they record
    /// something that happened at that stop and are historical facts, not best-effort pins.
    /// </para>
    /// <para>
    /// Counts land in <c>audit_logs</c>, one row per tenant (<c>EntityType = 'DataMigration'</c>):
    /// PostgreSQL <c>RAISE NOTICE</c> output never reaches the operator through
    /// <c>dotnet ef database update</c>, which is how the deploy script applies migrations.
    /// </para>
    /// </summary>
    public partial class PackageStopPinRepair : Migration
    {
        private const string MigrationId = "20260830134439_PackageStopPinRepair";
        private const string MigrationName = "PackageStopPinRepair";

        /// <summary>Live packages of one tenant whose pin resolves to a soft-deleted stop.</summary>
        private static string CountDangling(string into) => $"""
            SELECT count(*) INTO {into}
                          FROM packages p
                         WHERE p."TenantId" = r.tenant_id
                           AND p."IsDeleted" = false
                           AND (EXISTS (SELECT 1 FROM transport_order_stops d
                                         WHERE d."Id" = p."LoadingStopId" AND d."IsDeleted" = true)
                             OR EXISTS (SELECT 1 FROM transport_order_stops d
                                         WHERE d."Id" = p."DeliveryStopId" AND d."IsDeleted" = true));
            """;

        /// <summary>
        /// One repair pass for a single pin column. <paramref name="pin"/> is a column of
        /// <c>packages</c>; the CTE resolves the dead stop it points at, then counts the live
        /// candidates and only updates when there is exactly one.
        /// </summary>
        private static string RepairPin(string pin) => $"""
            WITH dangling AS (
                            SELECT p."Id" AS package_id, d."TransportOrderId", d."TenantId", d."Sequence", d."StopType",
                                   d."LocationId", d."Address", d."PostalCode", d."City", d."CountryCode"
                              FROM packages p
                              JOIN transport_order_stops d ON d."Id" = p."{pin}"
                             WHERE p."TenantId" = r.tenant_id
                               AND p."IsDeleted" = false
                               AND d."IsDeleted" = true
                        ),
                        candidates AS (
                            SELECT x.package_id, c.n, c.live_id
                              FROM dangling x
                              CROSS JOIN LATERAL (
                                  SELECT count(*) AS n, (array_agg(l."Id"))[1] AS live_id
                                    FROM transport_order_stops l
                                   WHERE l."TransportOrderId" = x."TransportOrderId"
                                     AND l."TenantId" = x."TenantId"
                                     AND l."IsDeleted" = false
                                     AND l."Sequence" = x."Sequence"
                                     AND l."StopType" = x."StopType"
                                     AND l."LocationId" IS NOT DISTINCT FROM x."LocationId"
                                     AND (x."LocationId" IS NOT NULL
                                          OR (lower(btrim(coalesce(l."Address", ''))) = lower(btrim(coalesce(x."Address", '')))
                                          AND lower(btrim(coalesce(l."PostalCode", ''))) = lower(btrim(coalesce(x."PostalCode", '')))
                                          AND lower(btrim(coalesce(l."City", ''))) = lower(btrim(coalesce(x."City", '')))
                                          AND upper(btrim(coalesce(l."CountryCode", ''))) = upper(btrim(coalesce(x."CountryCode", '')))))
                              ) c
                        )
                        UPDATE packages p
                           SET "{pin}" = c.live_id
                          FROM candidates c
                         WHERE c.package_id = p."Id"
                           AND c.n = 1
                           AND c.live_id IS NOT NULL;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"""
                DO $$
                DECLARE
                    r RECORD;
                    before_dangling bigint;
                    fixed_loading bigint;
                    fixed_delivery bigint;
                    still_dangling bigint;
                BEGIN
                    FOR r IN SELECT t."Id" AS tenant_id FROM tenants t
                    LOOP
                        {CountDangling("before_dangling")}
                        CONTINUE WHEN before_dangling = 0;

                        {RepairPin("LoadingStopId")}
                        GET DIAGNOSTICS fixed_loading = ROW_COUNT;

                        {RepairPin("DeliveryStopId")}
                        GET DIAGNOSTICS fixed_delivery = ROW_COUNT;

                        {CountDangling("still_dangling")}

                        INSERT INTO audit_logs
                            ("Id", "TenantId", "UserId", "EntityType", "EntityId", "Action",
                             "OldValuesJson", "NewValuesJson", "Timestamp", "IpAddress", "CorrelationId")
                        VALUES (
                            gen_random_uuid(), r.tenant_id, NULL, 'DataMigration',
                            '{MigrationId}', '{MigrationName}',
                            json_build_object('danglingPackages', before_dangling)::text,
                            json_build_object(
                                'repairedLoadingPins', fixed_loading,
                                'repairedDeliveryPins', fixed_delivery,
                                'stillAmbiguous', still_dangling)::text,
                            now(), NULL, NULL);
                    END LOOP;
                END $$;
                """);
        }

        /// <summary>
        /// Deliberately a no-op. Re-attaching a package to a soft-deleted stop would restore the
        /// exact corruption this migration removes — packages that cannot be scanned — and the
        /// pre-repair value cannot be kept anywhere without adding a schema object. Rolling the
        /// application back is safe with the repaired pins in place: they point at live stops of
        /// the same order, which is what every version of the code expects.
        /// </summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No SQL on purpose; see the summary above.
        }
    }
}
