namespace Moongazing.OrionInbox;

using System;

/// <summary>
/// Configuration for the inbox: which consumer scope it dedups under, how long a message id is
/// remembered, and how often expired ids are pruned. Validated at startup.
/// </summary>
public sealed class InboxOptions
{
    private string consumer = string.Empty;

    /// <summary>
    /// The consumer scope this inbox dedups under. When several independent consumers share one
    /// dedup table, give each a distinct name so one consumer's accepted ids do not mask another's.
    /// Defaults to the empty string (a single, unnamed consumer). Never null.
    /// </summary>
    public string Consumer
    {
        get => consumer;
        set => consumer = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// How long a processed message id is remembered before it is eligible for pruning. A redelivery
    /// after this window is no longer recognised as a duplicate, so set it comfortably longer than
    /// the broker's maximum redelivery horizon. Defaults to 7 days.
    /// </summary>
    public TimeSpan DedupWindow { get; set; } = TimeSpan.FromDays(7);

    /// <summary>
    /// How often the background prune sweeps expired dedup rows. Set to
    /// <see cref="System.Threading.Timeout.InfiniteTimeSpan"/> to disable the background prune (for
    /// example when an external job owns retention). Defaults to 1 hour.
    /// </summary>
    public TimeSpan PruneInterval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// The maximum number of expired rows deleted per prune sweep, bounding a single delete's lock
    /// footprint on a large table; the sweep repeats until fewer than this many remain. Defaults to
    /// 1000. Must be positive.
    /// </summary>
    public int PruneBatchSize { get; set; } = 1000;

    /// <summary>Whether the background prune is enabled (a finite, positive <see cref="PruneInterval"/>).</summary>
    public bool PruneEnabled => PruneInterval != System.Threading.Timeout.InfiniteTimeSpan;

    /// <summary>Validate the option values, throwing on an unusable configuration.</summary>
    public void Validate()
    {
        if (DedupWindow <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(DedupWindow), DedupWindow, "DedupWindow must be positive.");
        }
        if (PruneInterval <= TimeSpan.Zero && PruneInterval != System.Threading.Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(nameof(PruneInterval), PruneInterval, "PruneInterval must be positive or Timeout.InfiniteTimeSpan.");
        }
        if (PruneBatchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(PruneBatchSize), PruneBatchSize, "PruneBatchSize must be positive.");
        }
    }
}
