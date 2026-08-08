namespace Moongazing.OrionInbox.Diagnostics;

using System.Diagnostics;
using System.Diagnostics.Metrics;

using Moongazing.Orion.Abstractions.Diagnostics;

/// <summary>
/// OpenTelemetry instrumentation for the inbox. Built on the Orion family's
/// <see cref="OrionInstrumentation"/> spine, so it shares the family's naming and static-tag
/// conventions: a <see cref="Meter"/> and <see cref="ActivitySource"/> named
/// <c>Moongazing.OrionInbox</c> (subscribe by that name) carrying <c>orion.inbox.processed</c> and
/// <c>orion.inbox.duplicate</c> (deliveries) and <c>orion.inbox.pruned</c> (expired rows deleted),
/// plus a per-delivery <c>OrionInbox.process</c> span. Multi-tenant / multi-region labels configured
/// through <see cref="OrionInstrumentation.SetStaticTags"/> are stamped onto every measurement.
/// <para>
/// A process-wide <see cref="Shared"/> instance makes telemetry emit by default; the DI registration
/// supplies a container-managed singleton instead. Dispose it to release the meter.
/// </para>
/// </summary>
public sealed class InboxDiagnostics : OrionInstrumentation
{
    /// <summary>The meter / activity-source name OpenTelemetry consumers subscribe to.</summary>
    public const string MeterName = "Moongazing.OrionInbox";

    /// <summary>The activity name of the span covering one <c>ProcessAsync</c> call.</summary>
    public const string ProcessActivityName = "OrionInbox.process";

    private static readonly System.Lazy<InboxDiagnostics> SharedInstance =
        new(static () => new InboxDiagnostics());

    /// <summary>Create the meter and its instruments.</summary>
    public InboxDiagnostics()
        : base(OrionTelemetry.ScopeName("OrionInbox"), MeterVersion.Value)
    {
        Processed = Meter.CreateCounter<long>(
            OrionTelemetry.MetricName("inbox", "processed"),
            unit: "{message}",
            description: "First deliveries whose handler ran and committed.");

        Duplicate = Meter.CreateCounter<long>(
            OrionTelemetry.MetricName("inbox", "duplicate"),
            unit: "{message}",
            description: "Redeliveries skipped because the message id was already committed.");

        Pruned = Meter.CreateCounter<long>(
            OrionTelemetry.MetricName("inbox", "pruned"),
            unit: "{row}",
            description: "Expired dedup rows deleted by the background prune.");
    }

    /// <summary>The process-wide default instance, so telemetry emits without explicit wiring.</summary>
    public static InboxDiagnostics Shared => SharedInstance.Value;

    /// <summary>Counts first deliveries whose handler ran and committed.</summary>
    public Counter<long> Processed { get; }

    /// <summary>Counts redeliveries skipped as duplicates.</summary>
    public Counter<long> Duplicate { get; }

    /// <summary>Counts expired dedup rows deleted by the prune.</summary>
    public Counter<long> Pruned { get; }

    /// <summary>Start the span covering one delivery, or null when nothing is listening.</summary>
    /// <returns>The started <see cref="Activity"/>, or null.</returns>
    public Activity? StartProcess() =>
        ActivitySource.StartActivity(ProcessActivityName, ActivityKind.Consumer);

    /// <summary>Record a first delivery that was processed.</summary>
    public void RecordProcessed() => Processed.Add(1, StaticTags);

    /// <summary>Record a redelivery skipped as a duplicate.</summary>
    public void RecordDuplicate() => Duplicate.Add(1, StaticTags);

    /// <summary>Record <paramref name="rows"/> expired dedup rows deleted by the prune.</summary>
    /// <param name="rows">The number of rows deleted.</param>
    public void RecordPruned(long rows)
    {
        if (rows > 0)
        {
            Pruned.Add(rows, StaticTags);
        }
    }
}
