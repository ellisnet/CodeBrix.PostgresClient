using System.Collections.Concurrent;

namespace CodeBrix.PostgresClient.Tests; //was previously: Npgsql.Tests;

/// <summary>
/// Base class for test classes that run once without and once with multiplexing. A derived test
/// class is abstract and takes the mode as a constructor parameter; two sealed one-line subclasses
/// (<c>FooTests_NonMultiplexing</c> and <c>FooTests_Multiplexing</c>) give xUnit the two concrete
/// classes to run.
/// </summary>
public abstract class MultiplexingTestBase : TestBase
{
    protected bool IsMultiplexing => MultiplexingMode == MultiplexingMode.Multiplexing;

    protected MultiplexingMode MultiplexingMode { get; }

    readonly ConcurrentDictionary<(string ConnString, bool IsMultiplexing), string> _connStringCache
        = new();

    public override string ConnectionString { get; }

    protected MultiplexingTestBase(MultiplexingMode multiplexingMode)
    {
        MultiplexingMode = multiplexingMode;

        // If the test requires multiplexing to be on or off, use a small cache to avoid reparsing and
        // regenerating the connection string every time
        ConnectionString = _connStringCache.GetOrAdd((base.ConnectionString, IsMultiplexing),
            tup => new PgSqlConnectionStringBuilder(tup.ConnString)
            {
                Multiplexing = tup.IsMultiplexing
            }.ToString());
    }
}

public enum MultiplexingMode
{
    NonMultiplexing,
    Multiplexing
}
