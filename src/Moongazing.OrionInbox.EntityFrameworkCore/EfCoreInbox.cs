namespace Moongazing.OrionInbox.EntityFrameworkCore;

using System;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Moongazing.Orion.Abstractions.Time;
using Moongazing.OrionInbox;
using Moongazing.OrionInbox.Diagnostics;

/// <summary>
/// EF Core-backed <see cref="IOrionInbox"/>. On a first delivery it opens one transaction, inserts
/// the <see cref="InboxMessageRow"/> dedup row, runs the registered <see cref="IInboxHandler{T}"/>
/// (whose writes go to the same <typeparamref name="TDbContext"/>), and commits — so the dedup row
/// and the handler's effects land together or not at all. A redelivery is detected by the unique key
/// on the dedup row and reported as <see cref="InboxResult.Duplicate"/> before the handler runs.
/// </summary>
/// <remarks>
/// <para>
/// Register one <typeparamref name="TDbContext"/> scope per delivery: the processor and the handler
/// share that scoped context, which is what puts the dedup row and the effects in one transaction.
/// </para>
/// <para>
/// Provider-agnostic duplicate detection: a duplicate insert raises a
/// <see cref="DbUpdateException"/> whose provider-specific error code varies, so rather than parse
/// it the processor rolls back and re-queries the row's existence — if it now exists the failure was
/// a duplicate; otherwise it was a genuine storage fault and is rethrown so the message is not
/// silently dropped.
/// </para>
/// </remarks>
/// <typeparam name="TDbContext">The application's EF Core context, holding both the dedup table and the domain tables.</typeparam>
public sealed class EfCoreInbox<TDbContext> : IOrionInbox
    where TDbContext : DbContext
{
    private readonly TDbContext db;
    private readonly IServiceProvider services;
    private readonly IOrionClock clock;
    private readonly InboxDiagnostics diagnostics;
    private readonly string consumer;

    /// <summary>Create the processor bound to a scoped <paramref name="db"/>.</summary>
    /// <param name="db">The scoped context shared with the handler; both dedup row and effects commit through it.</param>
    /// <param name="services">The scope's service provider, used to resolve the message's handler.</param>
    /// <param name="clock">The family clock stamped on each accepted row.</param>
    /// <param name="diagnostics">The instrumentation the processor records to.</param>
    /// <param name="options">The inbox configuration (consumer scope, retention).</param>
    public EfCoreInbox(TDbContext db, IServiceProvider services, IOrionClock clock, InboxDiagnostics diagnostics, IOptions<InboxOptions> options)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(options);
        this.db = db;
        this.services = services;
        this.clock = clock;
        this.diagnostics = diagnostics;
        consumer = options.Value.Consumer;
    }

    /// <inheritdoc />
    public async Task<InboxResult> ProcessAsync<T>(string messageId, T payload, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(messageId);

        using var activity = diagnostics.StartProcess();

        // Fast path: a message committed by an earlier delivery is a duplicate without opening a
        // transaction or running the handler at all.
        if (await ExistsAsync(messageId, cancellationToken).ConfigureAwait(false))
        {
            return Duplicate(activity);
        }

        var handler = services.GetService<IInboxHandler<T>>()
            ?? throw new InvalidOperationException(
                $"No IInboxHandler<{typeof(T).Name}> is registered. Register it with AddInboxHandler<{typeof(T).Name}, YourHandler>().");

        var receivedAt = clock.UtcNow;
        var row = new InboxMessageRow
        {
            MessageId = messageId,
            Consumer = consumer,
            ReceivedAtUtc = receivedAt.UtcDateTime,
        };

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        // Insert the dedup row FIRST so a concurrent redelivery loses here — before its handler runs.
        db.Set<InboxMessageRow>().Add(row);
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            db.Entry(row).State = EntityState.Detached;
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);

            // Distinguish a duplicate-key conflict from a genuine storage failure: re-query outside
            // the rolled-back transaction. If the row exists now, a concurrent delivery won the race.
            if (await ExistsAsync(messageId, cancellationToken).ConfigureAwait(false))
            {
                return Duplicate(activity);
            }
            throw;
        }

        // First delivery: run the handler in the same transaction, then commit its writes with the row.
        await handler.HandleAsync(
            new InboxMessage<T>(messageId, payload, receivedAt, consumer),
            cancellationToken).ConfigureAwait(false);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        diagnostics.RecordProcessed();
        if (activity is { IsAllDataRequested: true })
        {
            activity.SetTag("orion.inbox.outcome", "processed");
        }
        return InboxResult.Processed;
    }

    private Task<bool> ExistsAsync(string messageId, CancellationToken cancellationToken) =>
        db.Set<InboxMessageRow>()
            .AsNoTracking()
            .AnyAsync(r => r.MessageId == messageId && r.Consumer == consumer, cancellationToken);

    private InboxResult Duplicate(System.Diagnostics.Activity? activity)
    {
        diagnostics.RecordDuplicate();
        if (activity is { IsAllDataRequested: true })
        {
            activity.SetTag("orion.inbox.outcome", "duplicate");
        }
        return InboxResult.Duplicate;
    }
}
