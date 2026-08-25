using Xunit.Sdk;
using Xunit.v3;

// Full test-case parallelism. Safe: the suite has no collection fixtures, no static mutable
// state, and per-test in-memory SQLite connections. Algorithm = Aggressive (SynchronizationContext-
// based cap) because the suite mixes CPU-bound policy tests with await-bound service-shell tests,
// so Aggressive keeps more continuations in flight than Conservative's started-tests semaphore.
// Thread count defaults to CPU threads.
[assembly: Parallelization(Mode = ParallelMode.All, Algorithm = ParallelAlgorithm.Aggressive)]
