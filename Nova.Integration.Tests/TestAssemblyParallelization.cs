using Xunit.Sdk;
using Xunit.v3;

// Full test-case parallelism against the single shared AppHost. Safe: the simulated current
// user is AsyncLocal-backed (flow-local per test), seeding is per-test unique, and there is no
// static mutable state (audited in plans/test-parallel-execution-parallelmode-all.md).
// Thread count defaults to CPU threads; tune MaxThreads if Phase 4 load validation shows contention.
[assembly: Parallelization(Mode = ParallelMode.All, Algorithm = ParallelAlgorithm.Conservative)]
