<!-- markdownlint-disable MD024 -->

# Changelog

All notable changes to OrionInbox are documented in this file. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.0.0/) and this project adheres to
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.7.0] - 2026-07-29

The first release — the Orion family's Wave 1 consumer-side inbox: dedup a message id and run its
handler exactly once, atomically with the handler's writes.

### Added

- **`OrionInbox`** (framework-free core):
  - **`IInboxHandler<T>`** — a handler that adds its writes and returns; it must not commit, because
    the inbox owns the transaction. Throwing rolls the whole delivery back for a clean retry.
  - **`InboxMessage<T>`** — the dedup id, payload, receipt time, and consumer scope handed to a handler.
  - **`InboxResult`** / **`InboxStatus`** — a value-typed `Processed` / `Duplicate` outcome (a struct,
    so later waves can attach data — e.g. a dead-letter error — without a breaking change).
  - **`IOrionInbox.ProcessAsync<T>(messageId, payload, ct)`** — the transport-edge entry point.
  - **`InboxOptions`** — `Consumer` scope, `DedupWindow`, `PruneInterval`, `PruneBatchSize`, validated.
  - **OpenTelemetry by default** — `InboxDiagnostics` on the family's `OrionInstrumentation` spine:
    a `Moongazing.OrionInbox` meter/activity-source with `orion.inbox.processed`,
    `orion.inbox.duplicate`, and `orion.inbox.pruned`, plus a per-delivery `OrionInbox.process` span.
  - Multi-targets `net8.0`/`net9.0`/`net10.0`; `IsAotCompatible`; a NativeAOT publish smoke test in CI.
- **`OrionInbox.EntityFrameworkCore`** (EF Core store):
  - **`EfCoreInbox<TDbContext>`** — `ProcessAsync` opens one transaction, inserts the dedup row FIRST
    (so a concurrent redelivery loses before its handler runs), runs the registered handler in the same
    context, and commits — the dedup row and the handler's effects land together or not at all. Duplicate
    detection is provider-agnostic (re-queries existence rather than parsing provider error codes).
  - **`InboxMessageRow`** + **`ApplyOrionInboxConfiguration`** — the `OrionInbox_Messages` dedup table,
    keyed uniquely on (`MessageId`, `Consumer`), with a `ReceivedAtUtc` index for the prune.
  - **`AddOrionInbox<TDbContext>()`** / **`AddInboxHandler<TMessage, THandler>()`** — DI wiring; the
    inbox and handlers are scoped so they share the per-delivery `DbContext`.
  - **`InboxPruneHostedService<TDbContext>`** — a background service that deletes dedup rows past
    `DedupWindow` in batches, sleeping on the family clock's `TimeProvider` (so a fake clock
    fast-forwards retention in tests).
  - Binds to `Orion.Abstractions` 1.2.0 and `OrionClock` 0.9.0; requires EF Core 8+. Not
    NativeAOT-published (EF Core is not AOT-clean); the framework-free core carries the AOT smoke.

### Verified

- Exit criteria met: the AOT smoke publishes trim/AOT-clean under `-warnaserror` and exits 0; a test
  delivers the same message id 100× concurrently against a real SQLite store and asserts exactly one
  effect row and 99 `Duplicate` results. 16 tests green across `net8.0`/`net9.0`/`net10.0`.
