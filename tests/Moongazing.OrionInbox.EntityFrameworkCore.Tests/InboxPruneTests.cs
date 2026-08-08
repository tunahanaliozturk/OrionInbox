namespace Moongazing.OrionInbox.EntityFrameworkCore.Tests;

using System;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Moongazing.OrionClock.Testing;
using Moongazing.OrionInbox;
using Moongazing.OrionInbox.EntityFrameworkCore.Hosting;

using Xunit;

/// <summary>The background prune deletes dedup rows past the retention window, measured on the family clock.</summary>
public sealed class InboxPruneTests
{
    private static InboxPruneHostedService<InboxTestDbContext> PruneService(InboxTestHarness harness) =>
        harness.Services.GetServices<IHostedService>()
            .OfType<InboxPruneHostedService<InboxTestDbContext>>()
            .Single();

    [Fact]
    public async Task Prune_deletes_rows_older_than_the_dedup_window()
    {
        var clock = new FakeOrionClock();
        await using var harness = await InboxTestHarness.CreateAsync(
            o => o.DedupWindow = TimeSpan.FromDays(7),
            clock: clock);

        await harness.ProcessAsync("msg-old", new OrderPaid("order-1", 1m));
        Assert.Equal(1, await harness.CountDedupRowsAsync());

        // Advance past the retention window: the row is now expired.
        clock.Advance(TimeSpan.FromDays(8));
        var deleted = await PruneService(harness).PruneAsync();

        Assert.Equal(1, deleted);
        Assert.Equal(0, await harness.CountDedupRowsAsync());
    }

    [Fact]
    public async Task Prune_keeps_rows_still_within_the_dedup_window()
    {
        var clock = new FakeOrionClock();
        await using var harness = await InboxTestHarness.CreateAsync(
            o => o.DedupWindow = TimeSpan.FromDays(7),
            clock: clock);

        await harness.ProcessAsync("msg-fresh", new OrderPaid("order-1", 1m));

        clock.Advance(TimeSpan.FromDays(3)); // still inside the window
        var deleted = await PruneService(harness).PruneAsync();

        Assert.Equal(0, deleted);
        Assert.Equal(1, await harness.CountDedupRowsAsync());
        // And the still-remembered id is still deduplicated.
        Assert.Equal(InboxResult.Duplicate, await harness.ProcessAsync("msg-fresh", new OrderPaid("order-1", 1m)));
    }

    [Fact]
    public async Task Prune_drains_a_backlog_larger_than_one_batch()
    {
        var clock = new FakeOrionClock();
        await using var harness = await InboxTestHarness.CreateAsync(
            o => { o.DedupWindow = TimeSpan.FromHours(1); o.PruneBatchSize = 10; },
            clock: clock);

        for (var i = 0; i < 25; i++)
        {
            await harness.ProcessAsync($"msg-{i}", new OrderPaid($"order-{i}", 1m));
        }
        Assert.Equal(25, await harness.CountDedupRowsAsync());

        clock.Advance(TimeSpan.FromHours(2));
        var deleted = await PruneService(harness).PruneAsync();

        Assert.Equal(25, deleted);              // all drained despite a batch size of 10
        Assert.Equal(0, await harness.CountDedupRowsAsync());
    }
}
