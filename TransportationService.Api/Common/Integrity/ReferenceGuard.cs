using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace TransportationService.Api.Common.Integrity;

/// <summary>One kind of record that still points at a master-data record ("12 opdrachten").</summary>
public sealed record ReferenceCountDto(string Key, int Count, string Label);

/// <summary>
/// Master-data remediation 2026-10-18 (MD-B2/H7/H8/H9/M12): what still references a master-data
/// record. Each domain builds its own report with EXPLICIT checks (no reflection over the model):
/// only history and operational records are counted — harmless configuration that can safely go
/// with the record is not. An empty report = the record is unused and may be deleted.
/// </summary>
public sealed class ReferenceReport
{
    private readonly List<ReferenceCountDto> _items = [];

    public IReadOnlyList<ReferenceCountDto> Items => _items;

    public bool HasReferences => _items.Count > 0;

    public ReferenceReport Add(string key, int count, string singular, string plural)
    {
        if (count > 0)
        {
            _items.Add(new ReferenceCountDto(key, count, $"{count} {(count == 1 ? singular : plural)}"));
        }

        return this;
    }

    /// <summary>"12 opdrachten, 4 dossiers en 3 facturen".</summary>
    public string Sentence() => _items.Count switch
    {
        0 => string.Empty,
        1 => _items[0].Label,
        _ => string.Join(", ", _items.Take(_items.Count - 1).Select(i => i.Label)) + " en " + _items[^1].Label,
    };
}

/// <summary>
/// A destructive delete refused because history or operational records still reference the
/// record. Answered with 409 and <c>code = record.referenced</c>, the Dutch explanation, the
/// structured counts (for a bullet list) and whether deactivating is the way out.
/// </summary>
public sealed class ReferencedRecordException : Exception
{
    public ReferencedRecordException(string message, ReferenceReport report, bool canDeactivate = true)
        : base(message)
    {
        Report = report;
        CanDeactivate = canDeactivate;
    }

    public ReferenceReport Report { get; }

    public bool CanDeactivate { get; }

    /// <summary>
    /// The standard sentence: "Deze klant kan niet worden verwijderd omdat er historische gegevens
    /// aan gekoppeld zijn: 12 opdrachten en 3 facturen. Gebruik "Deactiveren" …".
    /// </summary>
    public static ReferencedRecordException For(string subject, ReferenceReport report, string? deactivateHint)
    {
        var message = $"{subject} kan niet worden verwijderd omdat er historische of operationele gegevens aan gekoppeld zijn: {report.Sentence()}.";
        if (deactivateHint is not null)
        {
            message += " " + deactivateHint;
        }

        return new ReferencedRecordException(message, report, deactivateHint is not null);
    }
}

/// <summary>Translates <see cref="ReferencedRecordException"/> into a 409 with the reference counts.</summary>
public sealed class ReferencedRecordExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not ReferencedRecordException exception)
        {
            return;
        }

        context.Result = new ConflictObjectResult(new
        {
            message = exception.Message,
            code = ErrorCodes.RecordReferenced,
            references = exception.Report.Items,
            canDeactivate = exception.CanDeactivate,
        });
        context.ExceptionHandled = true;
    }
}
