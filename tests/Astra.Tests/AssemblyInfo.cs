// SQL integration fixtures share one isolated database. Explicit concurrency
// tests still run parallel transactions; unrelated fixture lifetimes do not overlap.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
