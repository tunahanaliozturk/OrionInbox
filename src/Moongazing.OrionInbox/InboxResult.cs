namespace Moongazing.OrionInbox;

/// <summary>How a delivery ended when it passed through the inbox.</summary>
public enum InboxStatus
{
    /// <summary>First delivery: the handler ran and its effects committed with the dedup row.</summary>
    Processed = 0,

    /// <summary>Redelivery: the message id was already committed, so the handler was skipped. No effect.</summary>
    Duplicate = 1,
}

/// <summary>
/// The outcome of <see cref="IOrionInbox.ProcessAsync{T}"/>: whether this delivery was processed for
/// the first time or skipped as a duplicate. Either way the broker should acknowledge the message —
/// a duplicate has already been handled, so there is nothing left to do.
/// <para>
/// Modelled as a value type carrying an <see cref="InboxStatus"/> rather than a bare enum so later
/// waves can attach data (e.g. a dead-letter <c>Error</c> after the retry budget) without a breaking
/// change to the return type.
/// </para>
/// </summary>
public readonly struct InboxResult : System.IEquatable<InboxResult>
{
    private InboxResult(InboxStatus status) => Status = status;

    /// <summary>The delivery outcome.</summary>
    public InboxStatus Status { get; }

    /// <summary>True when the handler ran on a first delivery.</summary>
    public bool IsProcessed => Status == InboxStatus.Processed;

    /// <summary>True when the delivery was a duplicate and the handler was skipped.</summary>
    public bool IsDuplicate => Status == InboxStatus.Duplicate;

    /// <summary>A first-delivery outcome.</summary>
    public static InboxResult Processed { get; } = new(InboxStatus.Processed);

    /// <summary>A duplicate-delivery outcome.</summary>
    public static InboxResult Duplicate { get; } = new(InboxStatus.Duplicate);

    /// <inheritdoc />
    public bool Equals(InboxResult other) => Status == other.Status;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is InboxResult other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => (int)Status;

    /// <summary>Equality by <see cref="Status"/>.</summary>
    public static bool operator ==(InboxResult left, InboxResult right) => left.Equals(right);

    /// <summary>Inequality by <see cref="Status"/>.</summary>
    public static bool operator !=(InboxResult left, InboxResult right) => !left.Equals(right);

    /// <inheritdoc />
    public override string ToString() => Status.ToString();
}
