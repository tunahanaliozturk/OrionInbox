// NativeAOT smoke test for the OrionInbox core. Publishing this with PublishAot=true must produce
// zero trim/AOT warnings, and running it must exit 0 - that pair is the core's AOT exit criterion.
// Assertions are runtime checks, not a test framework, so the point is to prove these paths survive
// trimming. The EF Core store is not exercised here: EF Core is not AOT-clean by design.
using System.Threading;
using System.Threading.Tasks;

using Moongazing.OrionInbox;
using Moongazing.OrionInbox.Diagnostics;

// Options validate and expose their derived state.
var options = new InboxOptions { Consumer = "smoke", DedupWindow = TimeSpan.FromDays(1) };
options.Validate();
Check(options.PruneEnabled, "prune should be enabled by default");
options.PruneInterval = Timeout.InfiniteTimeSpan;
Check(!options.PruneEnabled, "infinite interval should disable prune");

// Result value semantics.
Check(InboxResult.Processed.IsProcessed && !InboxResult.Processed.IsDuplicate, "processed result wrong");
Check(InboxResult.Duplicate.IsDuplicate && InboxResult.Duplicate == InboxResult.Duplicate, "duplicate result wrong");

// Message wrapper.
var message = new InboxMessage<OrderPaid>("msg-1", new OrderPaid("order-1", 42m), DateTimeOffset.UnixEpoch, "smoke");
Check(message.MessageId == "msg-1" && message.Payload.OrderId == "order-1" && message.Consumer == "smoke", "message wrapper wrong");

// The handler contract is invokable through the interface — the interface dispatch is exactly what
// we want to prove survives trimming, so the variable is deliberately interface-typed.
#pragma warning disable CA1859
IInboxHandler<OrderPaid> handler = new CountingHandler();
#pragma warning restore CA1859
await handler.HandleAsync(message, CancellationToken.None);
Check(((CountingHandler)handler).Handled == 1, "handler was not invoked");

// The telemetry surface constructs, names its meter, and records.
using var diagnostics = new InboxDiagnostics();
Check(diagnostics.Meter.Name == InboxDiagnostics.MeterName, "meter name wrong");
diagnostics.RecordProcessed();
diagnostics.RecordDuplicate();
diagnostics.RecordPruned(3);

Console.WriteLine("OrionInbox AOT smoke test passed.");
return 0;

static void Check(bool condition, string message)
{
    if (!condition)
    {
        Console.Error.WriteLine($"AOT smoke test failed: {message}");
        Environment.Exit(1);
    }
}

internal sealed record OrderPaid(string OrderId, decimal Amount);

internal sealed class CountingHandler : IInboxHandler<OrderPaid>
{
    public int Handled { get; private set; }

    public Task HandleAsync(InboxMessage<OrderPaid> message, CancellationToken cancellationToken)
    {
        Handled++;
        return Task.CompletedTask;
    }
}
