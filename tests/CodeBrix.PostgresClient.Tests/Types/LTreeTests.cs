using System;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.PostgresClient.PgSqlTypes;
using CodeBrix.PostgresClient.Properties;
using SilverAssertions;
using Xunit;

namespace CodeBrix.PostgresClient.Tests.Types; //was previously: Npgsql.Tests.Types;

public abstract class LTreeTests(MultiplexingMode multiplexingMode, LTreeTestsFixture fixture)
    : MultiplexingTestBase(multiplexingMode), IClassFixture<LTreeTestsFixture>, IAsyncLifetime
{
    [Fact]
    public Task lquery()
        => AssertType("Top.Science.*", "Top.Science.*", "lquery", PgSqlDbType.LQuery, isDefaultForWriting: false);

    [Fact]
    public Task ltree()
        => AssertType("Top.Science.Astronomy", "Top.Science.Astronomy", "ltree", PgSqlDbType.LTree, isDefaultForWriting: false);

    [Fact]
    public Task ltxtquery()
        => AssertType("Science & Astronomy", "Science & Astronomy", "ltxtquery", PgSqlDbType.LTxtQuery, isDefaultForWriting: false);

    [Fact]
    public async Task ltree_not_supported_by_default_on_PgSqlSlimSourceBuilder()
    {
        //Arrange
        var errorMessage = string.Format(
            PgSqlStrings.LTreeNotEnabled, nameof(PgSqlSlimDataSourceBuilder.EnableLTree), nameof(PgSqlSlimDataSourceBuilder));

        var dataSourceBuilder = new PgSqlSlimDataSourceBuilder(ConnectionString);
        await using var dataSource = dataSourceBuilder.Build();

        //Act
        var exception =
            await AssertTypeUnsupportedRead<PgSqlRange<int>>("Top.Science.Astronomy", "ltree", dataSource);
        exception.InnerException.Message.Should().Be(errorMessage);
        exception = await AssertTypeUnsupportedWrite<string>("Top.Science.Astronomy", "ltree", dataSource);
        exception.InnerException.Message.Should().Be(errorMessage);
    }

    [Fact]
    public async Task PgSqlSlimSourceBuilder_EnableLTree()
    {
        //Arrange
        var dataSourceBuilder = new PgSqlSlimDataSourceBuilder(ConnectionString);
        dataSourceBuilder.EnableLTree();
        await using var dataSource = dataSourceBuilder.Build();

        //Assert
        await AssertType(dataSource, "Top.Science.Astronomy", "Top.Science.Astronomy", "ltree", PgSqlDbType.LTree, isDefaultForWriting: false, skipArrayCheck: true);
    }

    [Fact]
    public async Task PgSqlSlimSourceBuilder_EnableArrays()
    {
        //Arrange
        var dataSourceBuilder = new PgSqlSlimDataSourceBuilder(ConnectionString);
        dataSourceBuilder.EnableLTree();
        dataSourceBuilder.EnableArrays();
        await using var dataSource = dataSourceBuilder.Build();

        //Assert
        await AssertType(dataSource, "Top.Science.Astronomy", "Top.Science.Astronomy", "ltree", PgSqlDbType.LTree, isDefaultForWriting: false);
    }

    public ValueTask InitializeAsync()
        => new(fixture.EnsureSetUp(OpenConnectionAsync));

    public ValueTask DisposeAsync()
        => ValueTask.CompletedTask;
}

public sealed class LTreeTests_NonMultiplexing(LTreeTestsFixture fixture) : LTreeTests(MultiplexingMode.NonMultiplexing, fixture);
public sealed class LTreeTests_Multiplexing(LTreeTestsFixture fixture) : LTreeTests(MultiplexingMode.Multiplexing, fixture);

/// <summary>
/// The once-per-test-class setup of <see cref="LTreeTests"/>: makes sure the ltree extension exists, using the first test's
/// own data source so its type information gets reloaded.
/// </summary>
public sealed class LTreeTestsFixture
{
    readonly SemaphoreSlim _setUpLock = new(1);
    bool _isSetUp;

    internal async Task EnsureSetUp(Func<ValueTask<PgSqlConnection>> openConnection)
    {
        await _setUpLock.WaitAsync(TestContext.Current.CancellationToken);
        try
        {
            if (_isSetUp)
                return;

            await using var conn = await openConnection();
            TestUtil.MinimumPgVersion(conn, "13.0");
            await TestUtil.EnsureExtensionAsync(conn, "ltree");
            _isSetUp = true;
        }
        finally
        {
            _setUpLock.Release();
        }
    }
}
