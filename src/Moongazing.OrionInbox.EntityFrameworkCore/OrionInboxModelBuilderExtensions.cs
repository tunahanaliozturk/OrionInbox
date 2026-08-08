namespace Moongazing.OrionInbox.EntityFrameworkCore;

using Microsoft.EntityFrameworkCore;

using Moongazing.OrionInbox.EntityFrameworkCore.Configuration;

/// <summary>
/// Registers the inbox dedup table on a <see cref="DbContext"/>'s model.
/// </summary>
public static class OrionInboxModelBuilderExtensions
{
    /// <summary>
    /// Add the <c>OrionInbox_Messages</c> dedup table to <paramref name="modelBuilder"/>. Call this
    /// from your context's <see cref="DbContext.OnModelCreating"/> so the dedup ledger lives in the
    /// same database as your domain writes — that shared context is what lets the dedup row and the
    /// handler's effects commit in one transaction.
    /// </summary>
    /// <param name="modelBuilder">The model builder to configure.</param>
    /// <returns>The same <paramref name="modelBuilder"/>, for chaining.</returns>
    public static ModelBuilder ApplyOrionInboxConfiguration(this ModelBuilder modelBuilder)
    {
        System.ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.ApplyConfiguration(new InboxMessageEntityConfiguration());
        return modelBuilder;
    }
}
