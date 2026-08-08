namespace Moongazing.OrionInbox.EntityFrameworkCore.DependencyInjection;

using System;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Moongazing.OrionClock;
using Moongazing.OrionInbox;
using Moongazing.OrionInbox.Diagnostics;
using Moongazing.OrionInbox.EntityFrameworkCore.Hosting;

/// <summary>
/// DI wiring for the EF Core inbox.
/// </summary>
public static class OrionInboxServiceCollectionExtensions
{
    /// <summary>
    /// Register the EF Core inbox over <typeparamref name="TDbContext"/>: the family clock, the
    /// <see cref="InboxOptions"/>, the shared <see cref="InboxDiagnostics"/>, a scoped
    /// <see cref="IOrionInbox"/>, and — unless retention is disabled — the background prune service.
    /// Register your <typeparamref name="TDbContext"/> separately (with the dedup table mapped via
    /// <see cref="OrionInboxModelBuilderExtensions.ApplyOrionInboxConfiguration"/>) and your handlers
    /// with <see cref="AddInboxHandler{TMessage, THandler}"/>.
    /// </summary>
    /// <typeparam name="TDbContext">The application's EF Core context holding the dedup and domain tables.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional configuration of the inbox options.</param>
    /// <returns>The same <paramref name="services"/>, for chaining (e.g. <see cref="AddInboxHandler{TMessage, THandler}"/>).</returns>
    public static IServiceCollection AddOrionInbox<TDbContext>(this IServiceCollection services, Action<InboxOptions>? configure = null)
        where TDbContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);

        // Registers OrionClock as TimeProvider AND IOrionClock via TryAdd, so a consumer override wins.
        services.AddOrionClock();

        // The prune hosted service logs non-fatal sweep failures; ensure a logging stack exists even
        // when the caller has not wired one (both AddLogging and AddOrionClock use TryAdd, so a
        // consumer's own configuration always wins).
        services.AddLogging();

        var optionsBuilder = services.AddOptions<InboxOptions>();
        if (configure is not null)
        {
            optionsBuilder.Configure(configure);
        }
        // Fail fast on an unusable configuration when the options are first resolved.
        optionsBuilder.PostConfigure(static o => o.Validate());

        services.TryAddSingleton<InboxDiagnostics>();
        services.TryAddScoped<IOrionInbox, EfCoreInbox<TDbContext>>();
        services.AddHostedService<InboxPruneHostedService<TDbContext>>();

        return services;
    }

    /// <summary>
    /// Register <typeparamref name="THandler"/> as the <see cref="IInboxHandler{TMessage}"/> for
    /// <typeparamref name="TMessage"/>. Handlers are scoped so they share the per-delivery
    /// <c>DbContext</c> scope with the processor — that shared context is what commits the handler's
    /// writes atomically with the dedup row.
    /// </summary>
    /// <typeparam name="TMessage">The payload type the handler consumes.</typeparam>
    /// <typeparam name="THandler">The handler implementation.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The same <paramref name="services"/>, for chaining.</returns>
    public static IServiceCollection AddInboxHandler<TMessage, THandler>(this IServiceCollection services)
        where THandler : class, IInboxHandler<TMessage>
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddScoped<IInboxHandler<TMessage>, THandler>();
        return services;
    }
}
