using Xunit.Sdk;
using Xunit.v3;

// Full test-case parallelism on the single shared Chromium instance: each test gets its own
// browser context, and the shared AppHost fixture is parallel-safe. Capped at 4 concurrent
// Chromium contexts.
//
// Algorithm = Conservative (the started-tests semaphore), NOT Aggressive, by empirical evidence on
// a clean environment:
//   * Aggressive at MaxThreads 8 → 22 failures; at MaxThreads 6 → 12 failures. In both cases the
//     stack trace is Npgsql.PostgresException 53300 "sorry, too many clients already" (shared
//     PostgreSQL), the same contention that rules out Aggressive for the integration suite.
//   * Root cause is the same as integration: Aggressive STARTS every test case up front and each
//     browser test seeds the shared PostgreSQL via a DbContext before its first await — i.e. before
//     the SynchronizationContext continuation gate (MaxThreads) applies — so the cap cannot bound
//     concurrently checked-out connections. Conservative caps how many tests START, keeping
//     concurrent seeding within the pool.
// MaxThreads = 4 also keeps the many load-sensitive interactive tests (drawer/hydration/history/
// computed-style polls) deterministic; 6 and 8 shared the CPU too thinly and produced intermittent
// timing flakes. The Phase 1-2 hardening (wider catch clauses, BrowserRetryPolicy-driven mutation/
// crop/focus polls) is what removes the failures, not more concurrency.
[assembly: Parallelization(Mode = ParallelMode.All, MaxThreads = 4, Algorithm = ParallelAlgorithm.Conservative)]
