namespace Moongazing.OrionInbox;

using System;

/// <summary>
/// A message delivered to an <see cref="IInboxHandler{T}"/>: its deduplication id, the deserialized
/// payload, and the moment the inbox first accepted it. The handler runs only for a first delivery —
/// a redelivery of the same <see cref="MessageId"/> is skipped before the handler is invoked.
/// </summary>
/// <typeparam name="T">The payload type.</typeparam>
public sealed class InboxMessage<T>
{
    /// <summary>Create a message wrapper for the handler.</summary>
    /// <param name="messageId">The stable, broker- or producer-assigned deduplication id (the idempotency key).</param>
    /// <param name="payload">The deserialized message payload.</param>
    /// <param name="receivedAtUtc">The UTC instant the inbox first accepted this message.</param>
    /// <param name="consumer">The consumer scope this delivery belongs to (empty for the default single consumer).</param>
    public InboxMessage(string messageId, T payload, DateTimeOffset receivedAtUtc, string consumer)
    {
        ArgumentException.ThrowIfNullOrEmpty(messageId);
        ArgumentNullException.ThrowIfNull(consumer);
        MessageId = messageId;
        Payload = payload;
        ReceivedAtUtc = receivedAtUtc;
        Consumer = consumer;
    }

    /// <summary>The stable deduplication id (idempotency key) this delivery was accepted under.</summary>
    public string MessageId { get; }

    /// <summary>The deserialized payload.</summary>
    public T Payload { get; }

    /// <summary>The UTC instant the inbox first accepted this message, read from the family clock.</summary>
    public DateTimeOffset ReceivedAtUtc { get; }

    /// <summary>The consumer scope; empty string for the default single-consumer setup.</summary>
    public string Consumer { get; }
}
