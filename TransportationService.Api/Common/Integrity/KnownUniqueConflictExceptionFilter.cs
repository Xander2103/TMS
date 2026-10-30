using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common.Diagnostics;
using TransportationService.Api.Common.Persistence;

namespace TransportationService.Api.Common.Integrity;

/// <summary>
/// PERF-H2 (performance/concurrency final remediation 2026-10-21): an EXPECTED race that ends on a
/// unique constraint answers 409 with a domain sentence instead of a raw 500 — but only for the
/// constraints in the fixed registry below. Every other <see cref="DbUpdateException"/> (an unknown
/// index, a foreign key, a check constraint) still surfaces as 500, because a blanket "unique
/// violation = 409" would hide genuine defects behind a friendly message.
/// <para>
/// The registry holds the indexes a normal UI workflow can hit when two users act at once and the
/// service's own pre-check or retry has already been exhausted or bypassed: driver assignment
/// (planning entry), role replacement, document/invoice numbers and their sequence rows, the live
/// transport document, credit notes, dossier links and stop executions. Deliberately NOT in it:
/// order/dossier/trip numbers (serialized by TenantNumbering's row lock), master-data codes and
/// e-mail/customer numbers (validation matters; a race there must be seen), background dedupe keys
/// (notifications, HR reminders, tasks, calendar sync, alerts), settings singletons and the
/// backfill idempotency key <c>UX_transport_dossiers_origin_order</c>.
/// </para>
/// </summary>
public sealed class KnownUniqueConflictExceptionFilter : IExceptionFilter
{
    public static readonly IReadOnlyDictionary<string, string> KnownConstraints =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // Planning: two planners assigned a driver to the same trip; the planning-entry INSERT
            // reaches the index before the trip's version token is judged.
            ["IX_trip_planning_entries_TenantId_TripId"] =
                "De rit is intussen door een andere gebruiker gewijzigd. Herlaad de rit en probeer opnieuw.",
            // Identity: two replacements of one user's role set.
            ["PK_user_roles"] =
                "De rollen van deze gebruiker zijn intussen door iemand anders gewijzigd. Herlaad en probeer opnieuw.",
            // Transport documents: number, idempotency key, the live document and the series row.
            ["IX_issued_transport_documents_TenantId_DocumentNumber"] =
                "Het documentnummer is intussen door een gelijktijdige uitgifte in gebruik genomen. Probeer opnieuw.",
            ["IX_issued_transport_documents_TenantId_RequestId"] =
                "Deze uitgifte is intussen al verwerkt. Vernieuw de lijst.",
            ["IX_issued_transport_documents_Live"] =
                "Er is intussen al een actief document van deze soort uitgegeven voor deze opdracht. Vernieuw de lijst.",
            ["IX_transport_document_sequences_TenantId_Kind_Year"] =
                "De nummerreeks van dit document werd intussen door een andere uitgifte gestart. Probeer opnieuw.",
            // Invoicing: legal number, series row and the one-live-credit-note rule.
            ["IX_invoices_TenantId_InvoiceNumber"] =
                "Het factuurnummer is intussen door een gelijktijdige uitgifte in gebruik genomen. Herlaad de factuur en probeer opnieuw.",
            ["IX_invoice_sequences_TenantId_LegalEntityId_Kind_Year_Month"] =
                "De factuurnummerreeks werd intussen door een andere uitgifte gestart. Probeer opnieuw.",
            ["IX_invoices_one_live_credit_note"] =
                "Er bestaat intussen al een creditnota voor deze factuur. Herlaad de factuur.",
            // Dossiers: the same order linked, or the same relation added, by two users at once.
            ["UX_dossier_orders_active_link"] =
                "Deze opdracht is intussen al aan het dossier gekoppeld. Herlaad het dossier.",
            ["UX_dossier_relations_active"] =
                "Deze dossierrelatie bestaat intussen al. Herlaad het dossier.",
            // Trip execution: the stop execution is created on the driver's first action; a double
            // tap or two devices race on it.
            ["IX_stop_executions_TripId_TransportOrderStopId"] =
                "Deze stop werd intussen al geregistreerd. Vernieuw de rit en probeer opnieuw.",
        };

    /// <summary>
    /// True (with the Dutch sentence) when <paramref name="exception"/> is a unique violation on a
    /// registered constraint. A <see cref="DbUpdateConcurrencyException"/> is not a unique violation
    /// and belongs to <see cref="ConcurrencyConflictExceptionFilter"/>.
    /// </summary>
    public static bool TryMap(Exception? exception, out string message)
    {
        message = string.Empty;
        if (exception is not DbUpdateException update || update is DbUpdateConcurrencyException)
        {
            return false;
        }

        var constraint = UniqueViolations.ConstraintName(update);
        return constraint is not null && KnownConstraints.TryGetValue(constraint, out message!);
    }

    public void OnException(ExceptionContext context)
    {
        if (!TryMap(context.Exception, out var message))
        {
            return;
        }

        context.Result = new ConflictObjectResult(new
        {
            message,
            code = ErrorCodes.ConcurrencyConflict,
            correlationId = CorrelationId.For(context.HttpContext),
        });
        context.ExceptionHandled = true;
    }
}
