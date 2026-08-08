namespace Moongazing.OrionInbox;

using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Handles a message consumed through the inbox. The inbox runs <see cref="HandleAsync"/> only on a
/// first delivery, inside the same transaction that records the deduplication row — so the handler's
/// writes and the dedup row commit together, or not at all.
/// <para>
/// The handler MUST NOT commit the transaction itself (no <c>SaveChanges</c>, no explicit commit):
/// the inbox owns the transaction boundary. Add your entities / issue your writes and return; the
/// inbox persists them atomically with the dedup row. Throwing rolls the whole delivery back, so a
/// later redelivery retries it cleanly (dead-letter after a retry budget arrives in a later wave).
/// </para>
/// </summary>
/// <typeparam name="T">The payload type this handler consumes.</typeparam>
public interface IInboxHandler<T>
{
    /// <summary>Process a first delivery of <paramref name="message"/>.</summary>
    /// <param name="message">The message: its dedup id, payload, and receipt time.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task HandleAsync(InboxMessage<T> message, CancellationToken cancellationToken);
}
