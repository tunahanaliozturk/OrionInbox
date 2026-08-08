namespace Moongazing.OrionInbox.EntityFrameworkCore.Configuration;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

/// <summary>
/// EF Core mapping for <see cref="InboxMessageRow"/> to table <c>OrionInbox_Messages</c>. The
/// composite primary key on (<c>MessageId</c>, <c>Consumer</c>) is the dedup guard — a duplicate
/// insert violates it, which is how a redelivery is detected — and lets one table serve multiple
/// consumers. A secondary index on <c>ReceivedAtUtc</c> keeps the retention prune a range scan
/// rather than a full-table scan.
/// </summary>
public sealed class InboxMessageEntityConfiguration : IEntityTypeConfiguration<InboxMessageRow>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<InboxMessageRow> builder)
    {
        System.ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("OrionInbox_Messages");
        builder.HasKey(x => new { x.MessageId, x.Consumer });

        builder.Property(x => x.MessageId).IsRequired().HasMaxLength(256);
        builder.Property(x => x.Consumer).IsRequired().HasMaxLength(128);
        builder.Property(x => x.ReceivedAtUtc).IsRequired();

        builder.HasIndex(x => x.ReceivedAtUtc).HasDatabaseName("IX_OrionInbox_Messages_ReceivedAtUtc");
    }
}
