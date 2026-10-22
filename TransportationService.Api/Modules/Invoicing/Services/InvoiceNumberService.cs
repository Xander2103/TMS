using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common.Persistence;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Invoicing.Entities;
using TransportationService.Api.Modules.Organization.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Invoicing.Services;

public interface IInvoiceNumberService
{
    /// <summary>
    /// Claims the next number for (entity, period) and saves ALL pending changes on the
    /// context in the same SaveChanges (invoice + order flips + counter). Retry-safe:
    /// concurrent claims conflict on the sequence concurrency token and re-claim.
    /// </summary>
    Task ClaimAsync(LegalEntity entity, InvoiceKind kind, int year, int month, Action<string> assignNumber, CancellationToken cancellationToken);

    /// <summary>Next expected number for (entity, kind, period) WITHOUT claiming it. Purely informative.</summary>
    Task<string> PreviewAsync(LegalEntity entity, InvoiceKind kind, int year, int month, CancellationToken cancellationToken);
}

public class InvoiceNumberService : IInvoiceNumberService
{
    // PERF-B2: without the PostgreSQL series lock (SQLite) a burst of parallel issues retries from
    // the reloaded counter; enough attempts for such a burst.
    private const int MaxAttempts = 10;

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;

    public InvoiceNumberService(TransportationDbContext dbContext, ITenantContext tenantContext)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
    }

    /// <summary>
    /// Credit notes form their own series (ACC-H3/M11): the configured credit-note prefix, else
    /// the invoice prefix + "CN". A format without {PREFIX} gets it prepended so the prefix can
    /// never silently disappear and the two series can never collide.
    /// </summary>
    public static string Compose(LegalEntity entity, InvoiceKind kind, int year, int month, int sequenceValue)
    {
        var padding = Math.Clamp(entity.InvoiceSequencePadding, 2, 8);
        var format = entity.InvoiceNumberFormat;
        var prefix = entity.InvoicePrefix ?? string.Empty;
        if (kind == InvoiceKind.CreditNote)
        {
            prefix = entity.CreditNotePrefix ?? $"{entity.InvoicePrefix}CN";
            if (!format.Contains("{PREFIX}", StringComparison.Ordinal))
            {
                format = "{PREFIX}" + format;
            }
        }

        return format
            .Replace("{PREFIX}", prefix, StringComparison.Ordinal)
            .Replace("{YYYY}", year.ToString("D4"), StringComparison.Ordinal)
            .Replace("{YY}", (year % 100).ToString("D2"), StringComparison.Ordinal)
            .Replace("{MM}", month.ToString("D2"), StringComparison.Ordinal)
            .Replace("{SEQ}", sequenceValue.ToString().PadLeft(padding, '0'), StringComparison.Ordinal);
    }

    public async Task ClaimAsync(LegalEntity entity, InvoiceKind kind, int year, int month, Action<string> assignNumber, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            var sequence = await GetOrAddSequenceAsync(entity.Id, kind, year, month, cancellationToken);

            var number = Compose(entity, kind, year, month, sequence.NextValue);
            assignNumber(number);
            sequence.NextValue++;

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
                return;
            }
            catch (DbUpdateConcurrencyException exception)
                when (attempt < MaxAttempts && exception.Entries.Any(e => ReferenceEquals(e.Entity, sequence)))
            {
                // Another request claimed this value first: reload the counter and retry. PERF-B2:
                // only a conflict on the SEQUENCE row is ours to retry — a conflict on the invoice
                // itself (its Status token: it was issued meanwhile) propagates and answers 409;
                // retrying it used to hand out N+1 and overwrite the winner's number.
                await _dbContext.Entry(sequence).ReloadAsync(cancellationToken);
            }
            catch (DbUpdateException exception) when (attempt < MaxAttempts && UniqueViolations.IsUniqueViolation(exception))
            {
                // Either two requests created the same (entity, period) sequence row, or the
                // composed number collided with an existing invoice. Detach our new row when
                // present and retry against the winner.
                var entry = _dbContext.Entry(sequence);
                if (entry.State == EntityState.Added)
                {
                    entry.State = EntityState.Detached;
                }
                else
                {
                    await entry.ReloadAsync(cancellationToken);
                }

                // MD-H5: a number that already exists in ANOTHER series (another entity, kind or
                // period printing the same numbers, e.g. after a format change) is a configuration
                // conflict — never retried into a 500, never skipped (a legal series has no gaps).
                // PERF-B2: the same number taken by THIS series is an ordinary parallel claim (the
                // winner advanced the counter under us) and is simply retried from the reloaded
                // counter — eight parallel drafts used to get seven of these 400s.
                if (await NumberTakenByAnotherSeriesAsync(number, entity.Id, kind, year, month, cancellationToken))
                {
                    throw new Common.DomainValidationException("invoiceNumber",
                        $"Factuurnummer {number} bestaat al in een andere nummerreeks. Pas het factuurnummerformaat of de prefix van de facturerende entiteit aan.");
                }
            }
        }
    }

    private Task<bool> NumberTakenByAnotherSeriesAsync(
        string number, Guid legalEntityId, InvoiceKind kind, int year, int month, CancellationToken cancellationToken) =>
        _dbContext.Invoices.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(i => i.TenantId == _tenantContext.TenantId && i.InvoiceNumber == number
                           && (i.LegalEntityId != legalEntityId || i.Kind != kind
                               || i.InvoicePeriodYear != year || i.InvoicePeriodMonth != month),
                cancellationToken);

    public async Task<string> PreviewAsync(LegalEntity entity, InvoiceKind kind, int year, int month, CancellationToken cancellationToken)
    {
        var next = await _dbContext.InvoiceSequences.AsNoTracking()
            .Where(s => s.TenantId == _tenantContext.TenantId && s.LegalEntityId == entity.Id && s.Kind == kind && s.Year == year && s.Month == month)
            .Select(s => (int?)s.NextValue)
            .FirstOrDefaultAsync(cancellationToken) ?? 1;
        return Compose(entity, kind, year, month, next);
    }

    private async Task<InvoiceSequence> GetOrAddSequenceAsync(Guid legalEntityId, InvoiceKind kind, int year, int month, CancellationToken cancellationToken)
    {
        var sequence = _dbContext.InvoiceSequences.Local.FirstOrDefault(
                s => s.TenantId == _tenantContext.TenantId && s.LegalEntityId == legalEntityId && s.Kind == kind && s.Year == year && s.Month == month)
            ?? await _dbContext.InvoiceSequences.FirstOrDefaultAsync(
                s => s.TenantId == _tenantContext.TenantId && s.LegalEntityId == legalEntityId && s.Kind == kind && s.Year == year && s.Month == month,
                cancellationToken);

        if (sequence is null)
        {
            sequence = new InvoiceSequence
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantContext.TenantId,
                LegalEntityId = legalEntityId,
                Kind = kind,
                Year = year,
                Month = month,
                NextValue = 1,
            };
            _dbContext.InvoiceSequences.Add(sequence);
        }

        return sequence;
    }
}
