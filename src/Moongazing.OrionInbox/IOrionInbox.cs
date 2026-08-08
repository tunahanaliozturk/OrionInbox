namespace Moongazing.OrionInbox;

using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// The consumer-side inbox: dedups a message id and runs its handler exactly once. Call
/// <see cref="ProcessAsync{T}"/> at the transport edge (a broker consumer, a webhook receiver, a
/// polling worker) with the broker- or producer-assigned message id and the deserialized payload.
/// <para>
/// A first delivery runs the registered <see cref="IInboxHandler{T}"/> and commits its writes with
/// the dedup row in one transaction, returning <see cref="InboxResult.Processed"/>. A redelivery of
/// the same id is a no-op that returns <see cref="InboxResult.Duplicate"/>. Either outcome means the
/// broker should acknowledge the message. Paired with an at-least-once transport and an
/// <c>OrionPatch</c> outbox, this yields exactly-once <em>effects</em> end to end.
/// </para>
/// </summary>
public interface IOrionInbox
{
    /// <summary>
    /// Deduplicate <paramref name="messageId"/> and, on a first delivery, run the registered
    /// <see cref="IInboxHandler{T}"/> for <paramref name="payload"/> atomically with the dedup row.
    /// </summary>
    /// <typeparam name="T">The payload type; the handler is resolved as <see cref="IInboxHandler{T}"/>.</typeparam>
    /// <param name="messageId">The stable deduplication id (idempotency key).</param>
    /// <param name="payload">The deserialized payload passed to the handler on a first delivery.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see cref="InboxResult.Processed"/> on a first delivery, <see cref="InboxResult.Duplicate"/> on a redelivery.</returns>
    Task<InboxResult> ProcessAsync<T>(string messageId, T payload, CancellationToken cancellationToken = default);
}
