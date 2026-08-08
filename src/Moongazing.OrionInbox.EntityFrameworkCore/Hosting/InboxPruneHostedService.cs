namespace Moongazing.OrionInbox.EntityFrameworkCore.Hosting;

using System;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Moongazing.Orion.Abstractions.Time;
using Moongazing.OrionInbox;
using Moongazing.OrionInbox.Diagnostics;

/// <summary>
/// Background service that deletes expired dedup rows so the inbox table does not grow without bound.
/// It sleeps <see cref="InboxOptions.PruneInterval"/> between sweeps on the family clock's
/// <see cref="TimeProvider"/> — so a fake clock fast-forwards the schedule in tests — and each sweep
/// deletes rows older than <see cref="InboxOptions.DedupWindow"/> in <see cref="InboxOptions.PruneBatchSize"/>
/// batches. A failed sweep is logged and retried on the next interval; it never takes the host down.
/// </summary>
/// <typeparam name="TDbContext">The application's EF Core context holding the dedup table.</typeparam>
public sealed partial class InboxPruneHostedService<TDbContext> : BackgroundService
    where TDbContext : DbContext
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Error, Message = "OrionInbox prune sweep failed; retrying after the next interval.")]
    private static partial void LogPruneFailed(ILogger logger, Exception exception);

    private readonly IServiceScopeFactory scopeFactory;
    private readonly TimeProvider timeProvider;
    private readonly IOrionClock clock;
    private readonly InboxDiagnostics diagnostics;
    private readonly ILogger<InboxPruneHostedService<TDbContext>> logger;
    private readonly InboxOptions options;

    /// <summary>Create the prune service.</summary>
    /// <param name="scopeFactory">Factory for the per-sweep DI scope that resolves the context.</param>
    /// <param name="timeProvider">The family clock as a <see cref="TimeProvider"/>, driving the sweep interval.</param>
    /// <param name="clock">The family clock, read for the retention cutoff.</param>
    /// <param name="diagnostics">Instrumentation; records rows pruned.</param>
    /// <param name="logger">Logger for non-fatal sweep failures.</param>
    /// <param name="options">The inbox configuration.</param>
    public InboxPruneHostedService(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        IOrionClock clock,
        InboxDiagnostics diagnostics,
        ILogger<InboxPruneHostedService<TDbContext>> logger,
        IOptions<InboxOptions> options)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(options);
        this.scopeFactory = scopeFactory;
        this.timeProvider = timeProvider;
        this.clock = clock;
        this.diagnostics = diagnostics;
        this.logger = logger;
        this.options = options.Value;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.PruneEnabled)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(options.PruneInterval, timeProvider, stoppingToken).ConfigureAwait(false);
                await PruneAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break; // graceful shutdown
            }
#pragma warning disable CA1031 // a prune sweep must never crash the host; log and retry next interval
            catch (Exception ex)
#pragma warning restore CA1031
            {
                LogPruneFailed(logger, ex);
            }
        }
    }

    /// <summary>Delete all currently-expired dedup rows in batches. Returns the total deleted.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of rows deleted this sweep.</returns>
    public async Task<long> PruneAsync(CancellationToken cancellationToken = default)
    {
        var cutoff = (clock.UtcNow - options.DedupWindow).UtcDateTime;
        var consumer = options.Consumer;
        long total = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<TDbContext>();

            // ExecuteDeleteAsync issues one server-side DELETE with no change-tracking; the ordered
            // Take bounds each batch so a huge backlog is drained in bounded-lock chunks.
            var batchIds = await db.Set<InboxMessageRow>()
                .Where(r => r.Consumer == consumer && r.ReceivedAtUtc < cutoff)
                .OrderBy(r => r.ReceivedAtUtc)
                .Take(options.PruneBatchSize)
                .Select(r => r.MessageId)
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            if (batchIds.Count == 0)
            {
                break;
            }

            var deleted = await db.Set<InboxMessageRow>()
                .Where(r => r.Consumer == consumer && batchIds.Contains(r.MessageId))
                .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);

            total += deleted;
            if (batchIds.Count < options.PruneBatchSize)
            {
                break;
            }
        }

        diagnostics.RecordPruned(total);
        return total;
    }
}
