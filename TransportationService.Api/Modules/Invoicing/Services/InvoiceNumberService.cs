using Microsoft.EntityFrameworkCore;
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
    Task ClaimAsync(LegalEntity entity, int year, int month, Action<string> assignNumber, CancellationToken cancellationToken);

    /// <summary>Next expected number for (entity, period) WITHOUT claiming it. Purely informative.</summary>
    Task<string> PreviewAsync(LegalEntity entity, int year, int month, CancellationToken cancellationToken);
}

public class InvoiceNumberService : IInvoiceNumberService
{
    private const int MaxAttempts = 5;

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;

    public InvoiceNumberService(TransportationDbContext dbContext, ITenantContext tenantContext)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
    }

    public static string Compose(LegalEntity entity, int year, int month, int sequenceValue)
    {
        var padding = Math.Clamp(entity.InvoiceSequencePadding, 2, 8);
        return entity.InvoiceNumberFormat
            .Replace("{PREFIX}", entity.InvoicePrefix ?? string.Empty, StringComparison.Ordinal)
            .Replace("{YYYY}", year.ToString("D4"), StringComparison.Ordinal)
            .Replace("{YY}", (year % 100).ToString("D2"), StringComparison.Ordinal)
            .Replace("{MM}", month.ToString("D2"), StringComparison.Ordinal)
            .Replace("{SEQ}", sequenceValue.ToString().PadLeft(padding, '0'), StringComparison.Ordinal);
    }

    public async Task ClaimAsync(LegalEntity entity, int year, int month, Action<string> assignNumber, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            var sequence = await GetOrAddSequenceAsync(entity.Id, year, month, cancellationToken);

            assignNumber(Compose(entity, year, month, sequence.NextValue));
            sequence.NextValue++;

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
                return;
            }
            catch (DbUpdateConcurrencyException) when (attempt < MaxAttempts)
            {
                // Another request claimed this value first: reload the counter and retry.
                await _dbContext.Entry(sequence).ReloadAsync(cancellationToken);
            }
            catch (DbUpdateException) when (attempt < MaxAttempts)
            {
                // Either two requests created the same (entity, period) sequence row, or the
                // composed number collided with an existing invoice (e.g. after a format
                // change). Detach our new row when present and retry against the winner.
                var entry = _dbContext.Entry(sequence);
                if (entry.State == EntityState.Added)
                {
                    entry.State = EntityState.Detached;
                }
                else
                {
                    await entry.ReloadAsync(cancellationToken);
                }
            }
        }
    }

    public async Task<string> PreviewAsync(LegalEntity entity, int year, int month, CancellationToken cancellationToken)
    {
        var next = await _dbContext.InvoiceSequences.AsNoTracking()
            .Where(s => s.TenantId == _tenantContext.TenantId && s.LegalEntityId == entity.Id && s.Year == year && s.Month == month)
            .Select(s => (int?)s.NextValue)
            .FirstOrDefaultAsync(cancellationToken) ?? 1;
        return Compose(entity, year, month, next);
    }

    private async Task<InvoiceSequence> GetOrAddSequenceAsync(Guid legalEntityId, int year, int month, CancellationToken cancellationToken)
    {
        var sequence = _dbContext.InvoiceSequences.Local.FirstOrDefault(
                s => s.TenantId == _tenantContext.TenantId && s.LegalEntityId == legalEntityId && s.Year == year && s.Month == month)
            ?? await _dbContext.InvoiceSequences.FirstOrDefaultAsync(
                s => s.TenantId == _tenantContext.TenantId && s.LegalEntityId == legalEntityId && s.Year == year && s.Month == month,
                cancellationToken);

        if (sequence is null)
        {
            sequence = new InvoiceSequence
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantContext.TenantId,
                LegalEntityId = legalEntityId,
                Year = year,
                Month = month,
                NextValue = 1,
            };
            _dbContext.InvoiceSequences.Add(sequence);
        }

        return sequence;
    }
}
