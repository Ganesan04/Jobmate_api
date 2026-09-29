using Xunit;

// Every test in this project is an acceptance test that runs against the single guarded
// jobmate_test database and truncates it between tests. Two test classes running at the
// same time would delete and lock out each other's rows, so they are run one at a time.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
