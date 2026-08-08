namespace Moongazing.OrionInbox.EntityFrameworkCore;

using System;

/// <summary>
/// One row per accepted message id — the durable dedup ledger. A row exists if and only if the
/// message's handler has committed, so its presence is the exactly-once guard. The unique key on
/// (<see cref="MessageId"/>, <see cref="Consumer"/>) is what makes a concurrent redelivery collapse:
/// two simultaneous first deliveries race to insert this row and exactly one wins.
/// </summary>
public sealed class InboxMessageRow
{
    /// <summary>The stable deduplication id (idempotency key).</summary>
    public string MessageId { get; set; } = string.Empty;

    /// <summary>
    /// The consumer scope, so one table can serve several independent consumers. Empty string for
    /// the default single consumer (never null — it is part of the key).
    /// </summary>
    public string Consumer { get; set; } = string.Empty;

    /// <summary>The UTC instant the message was first accepted, read from the family clock. Drives pruning.</summary>
    public DateTime ReceivedAtUtc { get; set; }
}
