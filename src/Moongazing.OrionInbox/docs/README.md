# OrionInbox

The framework-free core of a transactional inbox for .NET: the handler contract, message, result, options and telemetry that make a redelivered message's effect land exactly once.

![OrionInbox overview: the broker delivers at least once, your consumer calls IOrionInbox.ProcessAsync, and the store commits the dedup row with your handler's writes](https://raw.githubusercontent.com/tunahanaliozturk/OrionInbox/master/docs/diagrams/overview.png)

## Install

    dotnet add package OrionInbox

This package holds the contracts only. For a working inbox, install the store, which references this package:

    dotnet add package OrionInbox.EntityFrameworkCore

## Quick start

Write a handler. It adds its writes and returns; it must not call `SaveChanges` or commit, because the inbox owns the transaction:

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

Call the inbox at the transport edge, in one DI scope per delivery:

```csharp
using var scope = provider.CreateScope();
var inbox = scope.ServiceProvider.GetRequiredService<IOrionInbox>();

InboxResult result = await inbox.ProcessAsync(messageId, payload, cancellationToken);
// result.IsProcessed: first delivery, handler ran and committed.
// result.IsDuplicate: the id was already committed, handler skipped.
// Acknowledge the message in both cases. If ProcessAsync throws, do not acknowledge.
```

`IOrionInbox` is implemented and registered by `OrionInbox.EntityFrameworkCore` (`AddOrionInbox<TDbContext>()` and `AddInboxHandler<TMessage, THandler>()`).

## Types

- `IOrionInbox` - `ProcessAsync<T>(messageId, payload, cancellationToken)` returns `InboxResult`.
- `IInboxHandler<T>` - `HandleAsync(InboxMessage<T>, CancellationToken)`, run only on a first delivery. Throwing rolls the whole delivery back.
- `InboxMessage<T>` - `MessageId`, `Payload`, `ReceivedAtUtc` and `Consumer`.
- `InboxResult` - a value type with `Status` (`InboxStatus.Processed` or `InboxStatus.Duplicate`), `IsProcessed` and `IsDuplicate`.

## Options

`InboxOptions`, validated when first resolved:

- `Consumer` - the consumer scope ids are deduplicated under. Default empty string.
- `DedupWindow` - how long a processed id is remembered. Default 7 days.
- `PruneInterval` - how often expired ids are pruned; `Timeout.InfiniteTimeSpan` disables pruning. Default 1 hour.
- `PruneBatchSize` - rows deleted per prune batch. Default 1000.

## Telemetry and AOT

- Meter and activity source `Moongazing.OrionInbox`: counters `orion.inbox.processed`, `orion.inbox.duplicate` and `orion.inbox.pruned`, and an `OrionInbox.process` span per delivery. Built on `OrionInstrumentation` from `Orion.Abstractions`.
- AOT- and trim-compatible (`IsAotCompatible`), checked by a NativeAOT smoke test in CI. Targets net8.0, net9.0 and net10.0.

## Related packages

- `OrionInbox.EntityFrameworkCore` - the EF Core store: dedup table, atomic `ProcessAsync`, DI wiring and background prune.
- `OrionClock` - the clock the inbox stamps and prunes with; `FakeOrionClock` from `OrionClock.Testing` drives it in tests.

## Links

- Documentation and full README: https://github.com/tunahanaliozturk/OrionInbox
- Changelog: https://github.com/tunahanaliozturk/OrionInbox/blob/master/CHANGELOG.md
- License: MIT
