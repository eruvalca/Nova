using Xunit.Sdk;
using Xunit.v3;

// Full test-case parallelism against the single shared AppHost. Safe: the simulated current
// user is AsyncLocal-backed (flow-local per test), seeding is per-test unique, and there is no
// static mutable state.
// Algorithm = Conservative (started-tests semaphore) and NOT Aggressive, by evidence: on a clean
// environment (no external AppHost holding the shared ports) Aggressive at the CPU-thread default
// exhausted the PostgreSQL connection pool, failing 63 tests with Npgsql.PostgresException 53300
// "sorry, too many clients already" (some cascading into 38s HTTP timeouts). Capping MaxThreads
// does not help: Aggressive *starts* every test case up front and each test opens a DbContext and
// checks out a pooled connection before its first await, i.e. before the SynchronizationContext
// continuation gate (MaxThreads) applies, so the cap cannot bound concurrently checked-out
// connections. Conservative caps *started* tests at MaxThreads, keeping connection use within the
// pool. Do not switch this suite to Aggressive.
[assembly: Parallelization(Mode = ParallelMode.All, Algorithm = ParallelAlgorithm.Conservative)]
