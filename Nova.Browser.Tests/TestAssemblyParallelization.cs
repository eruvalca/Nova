using Xunit.Sdk;
using Xunit.v3;

// Full test-case parallelism on the single shared Chromium instance: each test gets its own
// browser context, and the shared AppHost fixture is parallel-safe (audited in
// plans/test-parallel-execution-parallelmode-all.md). Capped at 4 concurrent Chromium contexts.
[assembly: Parallelization(Mode = ParallelMode.All, MaxThreads = 4, Algorithm = ParallelAlgorithm.Conservative)]
