namespace Moongazing.OrionInbox.EntityFrameworkCore.Tests;

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Moongazing.Orion.Abstractions.Time;
using Moongazing.OrionClock;
using Moongazing.OrionInbox;
using Moongazing.OrionInbox.EntityFrameworkCore;
using Moongazing.OrionInbox.EntityFrameworkCore.DependencyInjection;

/// <summary>The effect a handler writes — a receipt per order. Presence proves the handler committed.</summary>
public sealed class Receipt
{
    public int Id { get; set; }

    public string OrderId { get; set; } = string.Empty;

    public decimal Amount { get; set; }
}

/// <summary>The message payload consumed through the inbox.</summary>
public sealed record OrderPaid(string OrderId, decimal Amount);

/// <summary>A handler whose only effect is a DB write; it must NOT call SaveChanges (the inbox owns the transaction).</summary>
public sealed class ReceiptHandler : IInboxHandler<OrderPaid>
{
    private readonly InboxTestDbContext db;

    public ReceiptHandler(InboxTestDbContext db) => this.db = db;

    public Task HandleAsync(InboxMessage<OrderPaid> message, CancellationToken cancellationToken)
    {
        db.Receipts.Add(new Receipt { OrderId = message.Payload.OrderId, Amount = message.Payload.Amount });
        return Task.CompletedTask;
    }
}

/// <summary>A handler that always throws, to prove a failed delivery rolls back the dedup row and the effect together.</summary>
public sealed class ThrowingHandler : IInboxHandler<OrderPaid>
{
    public Task HandleAsync(InboxMessage<OrderPaid> message, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("handler boom");
}

/// <summary>Test context: the domain table (Receipts) plus the inbox dedup table in one database.</summary>
public sealed class InboxTestDbContext : DbContext
{
    public InboxTestDbContext(DbContextOptions<InboxTestDbContext> options) : base(options) { }

    public DbSet<Receipt> Receipts => Set<Receipt>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Receipt>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.OrderId).IsRequired();
        });
        modelBuilder.ApplyOrionInboxConfiguration();
    }
}

/// <summary>
/// Spins up a real, file-backed SQLite database and a DI container wired for the inbox. A file (not
/// <c>:memory:</c>) is used deliberately so many concurrently-created <see cref="InboxTestDbContext"/>
/// instances open independent connections to the SAME database — the only way to exercise the unique
/// constraint under genuine concurrency. WAL journaling plus SQLite's command-timeout retry let the
/// concurrent writers serialize cleanly instead of failing with "database is locked".
/// </summary>
public sealed class InboxTestHarness : IAsyncDisposable
{
    private readonly string dbPath;

    private InboxTestHarness(string dbPath, ServiceProvider services)
    {
        this.dbPath = dbPath;
        Services = services;
    }

    public ServiceProvider Services { get; }

    public static async Task<InboxTestHarness> CreateAsync(
        Action<InboxOptions>? configure = null,
        OrionClock? clock = null,
        bool useThrowingHandler = false)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"orioninbox-test-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={dbPath};Cache=Shared";

        var services = new ServiceCollection();

        // Pre-register the clock so AddOrionClock's TryAdd inside AddOrionInbox is a no-op and this
        // instance wins — lets a test drive time with a FakeOrionClock.
        if (clock is not null)
        {
            services.AddSingleton(clock);
            services.AddSingleton<OrionClock>(clock);
            services.AddSingleton<IOrionClock>(clock);
            services.AddSingleton<TimeProvider>(clock);
        }

        services.AddDbContext<InboxTestDbContext>(
            o => o.UseSqlite(connectionString),
            ServiceLifetime.Scoped);

        services.AddOrionInbox<InboxTestDbContext>(configure);
        if (useThrowingHandler)
        {
            services.AddInboxHandler<OrderPaid, ThrowingHandler>();
        }
        else
        {
            services.AddInboxHandler<OrderPaid, ReceiptHandler>();
        }

        var provider = services.BuildServiceProvider();

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<InboxTestDbContext>();
            await db.Database.EnsureCreatedAsync();
            // WAL improves concurrent-writer behaviour and persists in the file header for every
            // later connection.
            await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
        }

        return new InboxTestHarness(dbPath, provider);
    }

    /// <summary>Run one delivery in its own DI scope, exactly as a real consumer would per message.</summary>
    public async Task<InboxResult> ProcessAsync(string messageId, OrderPaid payload, CancellationToken ct = default)
    {
        await using var scope = Services.CreateAsyncScope();
        var inbox = scope.ServiceProvider.GetRequiredService<IOrionInbox>();
        return await inbox.ProcessAsync(messageId, payload, ct);
    }

    public async Task<int> CountReceiptsAsync(string orderId)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<InboxTestDbContext>();
        return await db.Receipts.CountAsync(r => r.OrderId == orderId);
    }

    public async Task<int> CountDedupRowsAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<InboxTestDbContext>();
        return await db.Set<InboxMessageRow>().CountAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await Services.DisposeAsync();
        SqliteCleanup(dbPath);
    }

    // SQLite keeps a connection pool; clear it so the file handle is released before deletion.
    private static void SqliteCleanup(string dbPath)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            var file = dbPath + suffix;
            if (File.Exists(file))
            {
                try { File.Delete(file); } catch (IOException) { /* best-effort temp cleanup */ }
            }
        }
    }
}
