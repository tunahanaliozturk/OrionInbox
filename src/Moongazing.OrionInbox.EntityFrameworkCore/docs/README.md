# OrionInbox.EntityFrameworkCore

The EF Core store for OrionInbox: a dedup table in your own database and an atomic `ProcessAsync` that commits the dedup row and your handler's writes in one transaction, so a redelivered message lands once.

![How EfCoreInbox.ProcessAsync handles one delivery: an existing row returns Duplicate; otherwise it inserts the dedup row first, a key conflict returns Duplicate, the handler runs in the same transaction, and a throwing handler rolls everything back](https://raw.githubusercontent.com/tunahanaliozturk/OrionInbox/master/docs/diagrams/process-message.png)

## Install

    dotnet add package OrionInbox.EntityFrameworkCore

This brings in `OrionInbox`, the core contracts. Works with any relational EF Core provider (EF Core 8 or later). A retrying execution strategy (`EnableRetryOnFailure`) is not supported yet: it rejects the transaction `ProcessAsync` opens.

## Quick start

Map the dedup table on your context:

```csharp
using Microsoft.EntityFrameworkCore;
using Moongazing.OrionInbox.EntityFrameworkCore;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Receipt> Receipts => Set<Receipt>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyOrionInboxConfiguration(); // table OrionInbox_Messages
    }
}
```

Write a handler that adds its writes and returns (no `SaveChanges`; the inbox owns the transaction):

```csharp
using Moongazing.OrionInbox;

public sealed class OrderPaidHandler(AppDbContext db) : IInboxHandler<OrderPaid>
{
    public Task HandleAsync(InboxMessage<OrderPaid> message, CancellationToken cancellationToken)
    {
        db.Receipts.Add(new Receipt(message.Payload.OrderId, message.Payload.Amount));
        return Task.CompletedTask;
    }
}
```

Register and process, one DI scope per delivery:

```csharp
using Moongazing.OrionInbox;
using Moongazing.OrionInbox.EntityFrameworkCore.DependencyInjection;

services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connectionString));
services.AddOrionInbox<AppDbContext>(o => o.DedupWindow = TimeSpan.FromDays(7));
services.AddInboxHandler<OrderPaid, OrderPaidHandler>();

// at the transport edge
using var scope = provider.CreateScope();
var inbox = scope.ServiceProvider.GetRequiredService<IOrionInbox>();
InboxResult result = await inbox.ProcessAsync(messageId, payload, cancellationToken);
// Processed or Duplicate: acknowledge. An exception: do not acknowledge, let the broker redeliver.
```

## Behaviour

- A committed id returns `Duplicate` without opening a transaction or running the handler.
- Otherwise the dedup row is inserted first, inside a transaction, so of two concurrent deliveries only one gets past the key on (`MessageId`, `Consumer`). The loser rolls back, re-queries, and returns `Duplicate`; if the row is still absent the `DbUpdateException` is a real storage fault and is rethrown.
- The handler runs in the same transaction on the same scoped `DbContext`, then `SaveChangesAsync` and `CommitAsync` store the row and the effects together.
- A handler that throws rolls back the row with its writes; the exception reaches your code and the redelivery retries cleanly.
- A missing handler registration throws `InvalidOperationException`.

## Options and retention

`AddOrionInbox<TDbContext>(o => ...)` configures `InboxOptions`:

- `Consumer` - dedup scope when several consumers share the table. Default empty string.
- `DedupWindow` - how long an id is remembered. Default 7 days. Keep it longer than the broker's redelivery horizon: an id redelivered after it has been pruned is processed again.
- `PruneInterval` - how often `InboxPruneHostedService<TDbContext>` deletes the configured consumer's rows older than `DedupWindow`. Default 1 hour; `Timeout.InfiniteTimeSpan` disables it.
- `PruneBatchSize` - rows per delete batch. Default 1000.

The prune waits on the `OrionClock` `TimeProvider`, so a `FakeOrionClock` fast-forwards it in tests. A failed sweep is logged and retried on the next interval.

## AOT

Not NativeAOT-published: EF Core itself is not AOT-clean. The `OrionInbox` core is AOT-compatible. Targets net8.0, net9.0 and net10.0.

## Related packages

- `OrionInbox` - the core contracts and telemetry (`IOrionInbox`, `IInboxHandler<T>`, `InboxResult`, `InboxOptions`).
- `OrionClock` - the family clock the inbox stamps rows and schedules the prune with.

## Links

- Documentation and full README: https://github.com/tunahanaliozturk/OrionInbox
- Changelog: https://github.com/tunahanaliozturk/OrionInbox/blob/master/CHANGELOG.md
- License: MIT
