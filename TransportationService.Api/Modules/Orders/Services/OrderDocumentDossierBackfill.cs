namespace TransportationService.Api.Modules.Orders.Services;

/// <summary>
/// D6: the one-off data step of migration <c>DossierDocumentScopeAndIssuedTransportDocuments</c> —
/// fills <c>order_documents."DossierId"</c> of every existing row from the OWNING dossier of its
/// order, with the same precedence as <c>OwningDossierResolver</c>: the order's own wrapper
/// (<c>OriginTransportOrderId</c>) wins, else the oldest active link. Rows whose order sits in no
/// dossier keep NULL and stay reachable through the order exactly as before.
/// <para>
/// Only the link column is written: no row is moved, deleted or duplicated, no stored file is
/// touched and <c>UpdatedAt</c> is left alone. Idempotent — it only ever fills a NULL. Plain SQL
/// that both PostgreSQL and SQLite accept, so the test suite runs the very statement the migration runs.
/// FROZEN: this text is part of an applied migration — never edit it; a later correction is a new migration.
/// </para>
/// </summary>
public static class OrderDocumentDossierBackfill
{
    public const string Sql = """
        UPDATE order_documents AS doc
        SET "DossierId" = resolved."DossierId"
        FROM (
            SELECT src."Id" AS "DocumentId",
                   COALESCE(
                       (SELECT w."Id"
                        FROM transport_dossiers w
                        WHERE w."TenantId" = src."TenantId" AND w."IsDeleted" = false
                          AND w."OriginTransportOrderId" = src."TransportOrderId"
                        LIMIT 1),
                       (SELECT l."DossierId"
                        FROM dossier_orders l
                        JOIN transport_dossiers d
                          ON d."Id" = l."DossierId" AND d."TenantId" = src."TenantId" AND d."IsDeleted" = false
                        WHERE l."TenantId" = src."TenantId" AND l."IsDeleted" = false
                          AND l."TransportOrderId" = src."TransportOrderId"
                        ORDER BY l."CreatedAt", l."Id"
                        LIMIT 1)) AS "DossierId"
            FROM order_documents src
            WHERE src."DossierId" IS NULL AND src."TransportOrderId" IS NOT NULL
        ) AS resolved
        WHERE doc."Id" = resolved."DocumentId" AND resolved."DossierId" IS NOT NULL;
        """;
}
