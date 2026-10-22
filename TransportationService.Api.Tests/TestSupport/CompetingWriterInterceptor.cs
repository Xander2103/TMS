using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace TransportationService.Api.Tests.TestSupport;

/// <summary>
/// Deterministic race (performance/concurrency final remediation 2026-10-21): performs ONE competing
/// write through a second context over the same connection at the exact moment the system under
/// test calls SaveChanges — after its own checks, before its UPDATE/INSERT — the way
/// <c>IssuedTransportDocumentTests.CompetingIssueInterceptor</c> does. No sleeps, no timing: the
/// competitor always wins the race and the SUT always has to cope with it. The interceptor is
/// shared by every context the harness creates, so it disarms itself BEFORE running the
/// competitor and never fires for the competitor's own save.
/// </summary>
public sealed class CompetingWriterInterceptor : SaveChangesInterceptor
{
    private Func<DbContext, bool>? _trigger;
    private Func<CancellationToken, Task>? _competitor;

    /// <summary>How many times the competitor ran (0 or 1).</summary>
    public int Fired { get; private set; }

    /// <param name="trigger">Recognises the SUT's save (typically: the entity under test is tracked as Modified/Added).</param>
    /// <param name="competitor">The competing write, on a context of its own.</param>
    public void Arm(Func<DbContext, bool> trigger, Func<CancellationToken, Task> competitor)
    {
        _trigger = trigger;
        _competitor = competitor;
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (_trigger is not null && eventData.Context is { } context && _trigger(context))
        {
            var competitor = _competitor!;
            _trigger = null;
            _competitor = null;
            Fired++;
            await competitor(cancellationToken);
        }

        return result;
    }
}
