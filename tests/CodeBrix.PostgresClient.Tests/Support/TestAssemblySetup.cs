using CodeBrix.PostgresClient.Tests.Support;
using Xunit;

[assembly: AssemblyFixture(typeof(TestDatabaseLifetime))]

namespace CodeBrix.PostgresClient.Tests.Support;

/// <summary>
/// The test collection for tests that must not run in parallel with any other test (the original
/// suite's [NonParallelizable] tests): they change process-wide state such as global type mappings,
/// event listeners or AppContext switches, or they depend on exact pool / connection counts.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class NonParallelCollection
{
    /// <summary>The collection name to use in <c>[Collection(NonParallelCollection.Name)]</c>.</summary>
    public const string Name = "NonParallel";
}
