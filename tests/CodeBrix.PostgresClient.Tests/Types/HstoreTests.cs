using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.PostgresClient.PgSqlTypes;
using Xunit;

namespace CodeBrix.PostgresClient.Tests.Types; //was previously: Npgsql.Tests.Types;

public abstract class HstoreTests(MultiplexingMode multiplexingMode, HstoreTestsFixture fixture)
    : MultiplexingTestBase(multiplexingMode), IClassFixture<HstoreTestsFixture>, IAsyncLifetime
{
    [Fact]
    public Task hstore()
        => AssertType(
            new Dictionary<string, string>
            {
                {"a", "3"},
                {"b", null},
                {"cd", "hello"}
            },
            @"""a""=>""3"", ""b""=>NULL, ""cd""=>""hello""",
            "hstore",
            PgSqlDbType.Hstore, isPgSqlDbTypeInferredFromClrType: false);

    [Fact]
    public Task hstore_empty()
        => AssertType(new Dictionary<string, string>(), @"", "hstore", PgSqlDbType.Hstore, isPgSqlDbTypeInferredFromClrType: false);

    [Fact]
    public Task hstore_as_ImmutableDictionary()
    {
        //Arrange
        var builder = ImmutableDictionary<string, string>.Empty.ToBuilder();
        builder.Add("a", "3");
        builder.Add("b", null);
        builder.Add("cd", "hello");
        var immutableDictionary = builder.ToImmutableDictionary();

        //Assert
        return AssertType(
            immutableDictionary,
            @"""a""=>""3"", ""b""=>NULL, ""cd""=>""hello""",
            "hstore",
            PgSqlDbType.Hstore,
            isDefaultForReading: false, isPgSqlDbTypeInferredFromClrType: false);
    }

    [Fact]
    public Task hstore_as_IDictionary()
        => AssertType<IDictionary<string, string>>(
            new Dictionary<string, string>
            {
                { "a", "3" },
                { "b", null },
                { "cd", "hello" }
            },
            @"""a""=>""3"", ""b""=>NULL, ""cd""=>""hello""",
            "hstore",
            PgSqlDbType.Hstore,
            isDefaultForReading: false, isPgSqlDbTypeInferredFromClrType: false);

    public ValueTask InitializeAsync()
        => new(fixture.EnsureSetUp(OpenConnectionAsync));

    public ValueTask DisposeAsync()
        => ValueTask.CompletedTask;
}

public sealed class HstoreTests_NonMultiplexing(HstoreTestsFixture fixture) : HstoreTests(MultiplexingMode.NonMultiplexing, fixture);
public sealed class HstoreTests_Multiplexing(HstoreTestsFixture fixture) : HstoreTests(MultiplexingMode.Multiplexing, fixture);

/// <summary>
/// The once-per-test-class setup of <see cref="HstoreTests"/>: makes sure the hstore extension exists, using the first test's
/// own data source so its type information gets reloaded.
/// </summary>
public sealed class HstoreTestsFixture
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

            using var conn = await openConnection();
            TestUtil.MinimumPgVersion(conn, "9.1", "Hstore introduced in PostgreSQL 9.1");
            await TestUtil.EnsureExtensionAsync(conn, "hstore", "9.1");
            _isSetUp = true;
        }
        finally
        {
            _setUpLock.Release();
        }
    }
}
