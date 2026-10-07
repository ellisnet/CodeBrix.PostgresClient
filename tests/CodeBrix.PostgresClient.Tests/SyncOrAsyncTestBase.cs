namespace CodeBrix.PostgresClient.Tests; //was previously: Npgsql.Tests;

/// <summary>
/// Base class for test classes that run once synchronously and once asynchronously. A derived test
/// class is abstract and takes the mode as a constructor parameter; two sealed one-line subclasses
/// (<c>FooTests_Sync</c> and <c>FooTests_Async</c>) give xUnit the two concrete classes to run.
/// </summary>
public abstract class SyncOrAsyncTestBase(SyncOrAsync syncOrAsync) : TestBase
{
    protected bool IsAsync => SyncOrAsync == SyncOrAsync.Async;

    protected SyncOrAsync SyncOrAsync { get; } = syncOrAsync;
}

public enum SyncOrAsync
{
    Sync,
    Async
}
