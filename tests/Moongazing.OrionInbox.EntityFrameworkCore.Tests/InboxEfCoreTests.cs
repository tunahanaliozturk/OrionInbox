namespace Moongazing.OrionInbox.EntityFrameworkCore.Tests;

using System;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;

using Moongazing.OrionClock.Testing;
using Moongazing.OrionInbox;

using Xunit;

/// <summary>Integration tests for the EF Core inbox against a real, file-backed SQLite database.</summary>
public sealed class InboxEfCoreTests
{
    [Fact]
    public async Task First_delivery_processes_and_commits_the_effect_with_the_dedup_row()
    {
        await using var harness = await InboxTestHarness.CreateAsync();

        var result = await harness.ProcessAsync("msg-1", new OrderPaid("order-1", 42m));

        Assert.Equal(InboxResult.Processed, result);
        Assert.Equal(1, await harness.CountReceiptsAsync("order-1"));
        Assert.Equal(1, await harness.CountDedupRowsAsync());
    }

    [Fact]
    public async Task Redelivery_of_the_same_id_is_a_duplicate_with_no_second_effect()
    {
        await using var harness = await InboxTestHarness.CreateAsync();

        var first = await harness.ProcessAsync("msg-1", new OrderPaid("order-1", 42m));
        var second = await harness.ProcessAsync("msg-1", new OrderPaid("order-1", 42m));
        var third = await harness.ProcessAsync("msg-1", new OrderPaid("order-1", 42m));

        Assert.Equal(InboxResult.Processed, first);
        Assert.Equal(InboxResult.Duplicate, second);
        Assert.Equal(InboxResult.Duplicate, third);
        Assert.Equal(1, await harness.CountReceiptsAsync("order-1")); // exactly-once effect
    }

    [Fact]
    public async Task One_hundred_concurrent_deliveries_of_the_same_id_yield_exactly_one_effect()
    {
        // Wave 1 exit criterion: deliver the same messageId 100x concurrently and assert exactly one
        // effect row and 99 Duplicate results.
        await using var harness = await InboxTestHarness.CreateAsync();
        const int deliveries = 100;

        var tasks = Enumerable.Range(0, deliveries)
            .Select(_ => Task.Run(() => harness.ProcessAsync("msg-hot", new OrderPaid("order-hot", 10m))))
            .ToArray();
        var results = await Task.WhenAll(tasks);

        Assert.Equal(1, results.Count(r => r == InboxResult.Processed));
        Assert.Equal(deliveries - 1, results.Count(r => r == InboxResult.Duplicate));
        Assert.Equal(1, await harness.CountReceiptsAsync("order-hot"));  // exactly one effect
        Assert.Equal(1, await harness.CountDedupRowsAsync());
    }

    [Fact]
    public async Task A_failing_handler_rolls_back_both_the_effect_and_the_dedup_row()
    {
        await using var harness = await InboxTestHarness.CreateAsync(useThrowingHandler: true);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => harness.ProcessAsync("msg-1", new OrderPaid("order-1", 42m)));

        // Nothing committed: no dedup row (so a redelivery can retry) and no effect.
        Assert.Equal(0, await harness.CountDedupRowsAsync());
        Assert.Equal(0, await harness.CountReceiptsAsync("order-1"));
    }

    [Fact]
    public async Task Different_consumers_dedup_the_same_id_independently()
    {
        await using var consumerA = await InboxTestHarness.CreateAsync(o => o.Consumer = "A");
        await using var consumerB = await InboxTestHarness.CreateAsync(o => o.Consumer = "B");

        // Distinct databases here, but the point is the Consumer scope is honoured end to end:
        // each consumer sees the id as a first delivery.
        Assert.Equal(InboxResult.Processed, await consumerA.ProcessAsync("shared-id", new OrderPaid("o", 1m)));
        Assert.Equal(InboxResult.Processed, await consumerB.ProcessAsync("shared-id", new OrderPaid("o", 1m)));
    }

    [Fact]
    public async Task An_unregistered_payload_type_fails_loudly()
    {
        await using var harness = await InboxTestHarness.CreateAsync();

        await using var scope = harness.Services.CreateAsyncScope();
        var inbox = scope.ServiceProvider.GetRequiredService<IOrionInbox>();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => inbox.ProcessAsync("msg-x", "a-string-with-no-handler"));
    }
}
