namespace Moongazing.OrionInbox.Tests;

using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;

using Moongazing.Orion.Abstractions.Diagnostics;
using Moongazing.OrionInbox.Diagnostics;

using Xunit;

/// <summary>Unit tests for the framework-free inbox core: options validation, result value semantics, telemetry.</summary>
public sealed class InboxCoreTests
{
    [Fact]
    public void Options_default_to_a_sane_configuration()
    {
        var options = new InboxOptions();
        options.Validate(); // does not throw

        Assert.Equal(string.Empty, options.Consumer);
        Assert.Equal(TimeSpan.FromDays(7), options.DedupWindow);
        Assert.Equal(TimeSpan.FromHours(1), options.PruneInterval);
        Assert.True(options.PruneEnabled);
    }

    [Fact]
    public void Options_reject_non_positive_dedup_window_and_batch_size()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new InboxOptions { DedupWindow = TimeSpan.Zero }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new InboxOptions { PruneBatchSize = 0 }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new InboxOptions { PruneInterval = TimeSpan.Zero }.Validate());
    }

    [Fact]
    public void Infinite_prune_interval_disables_the_prune_but_stays_valid()
    {
        var options = new InboxOptions { PruneInterval = System.Threading.Timeout.InfiniteTimeSpan };
        options.Validate(); // does not throw
        Assert.False(options.PruneEnabled);
    }

    [Fact]
    public void Consumer_cannot_be_set_to_null()
    {
        Assert.Throws<ArgumentNullException>(() => new InboxOptions { Consumer = null! });
    }

    [Fact]
    public void Result_has_value_equality_and_readable_status()
    {
        Assert.True(InboxResult.Processed.IsProcessed);
        Assert.True(InboxResult.Duplicate.IsDuplicate);
        Assert.Equal(InboxResult.Processed, InboxResult.Processed);
        Assert.NotEqual(InboxResult.Processed, InboxResult.Duplicate);
        Assert.Equal("Duplicate", InboxResult.Duplicate.ToString());
    }

    [Fact]
    public void Message_requires_a_non_empty_id_and_consumer()
    {
        Assert.Throws<ArgumentException>(() => new InboxMessage<string>("", "p", DateTimeOffset.UnixEpoch, ""));
        Assert.Throws<ArgumentNullException>(() => new InboxMessage<string>("id", "p", DateTimeOffset.UnixEpoch, null!));
    }

    [Fact]
    public void Diagnostics_record_processed_duplicate_and_pruned_counters()
    {
        using var diagnostics = new InboxDiagnostics();
        var counts = new Dictionary<string, long>(StringComparer.Ordinal);

        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (OrionInstrumentation.ListensTo(instrument, diagnostics))
                {
                    l.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, _, _) =>
        {
            lock (counts) { counts[instrument.Name] = counts.GetValueOrDefault(instrument.Name) + value; }
        });
        listener.Start();

        diagnostics.RecordProcessed();
        diagnostics.RecordProcessed();
        diagnostics.RecordDuplicate();
        diagnostics.RecordPruned(5);
        diagnostics.RecordPruned(0); // no-op, must not emit

        Assert.Equal(2, counts[OrionTelemetry.MetricName("inbox", "processed")]);
        Assert.Equal(1, counts[OrionTelemetry.MetricName("inbox", "duplicate")]);
        Assert.Equal(5, counts[OrionTelemetry.MetricName("inbox", "pruned")]);
    }
}
