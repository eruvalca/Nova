using Xunit.Sdk;
using Xunit.v3;

// Full test-case parallelism. Safe: the suite has no collection fixtures, no static mutable
// state, and per-test in-memory SQLite connections (audited in
// plans/test-parallel-execution-parallelmode-all.md). Thread count defaults to CPU threads.
[assembly: Parallelization(Mode = ParallelMode.All, Algorithm = ParallelAlgorithm.Conservative)]
