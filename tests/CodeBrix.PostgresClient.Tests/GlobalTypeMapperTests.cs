using System;
using System.Threading.Tasks;
using CodeBrix.PostgresClient.Internal;
using CodeBrix.PostgresClient.Internal.Postgres;
using CodeBrix.PostgresClient.Tests.Support;
using SilverAssertions;
using Xunit;
using static CodeBrix.PostgresClient.Tests.TestUtil;

namespace CodeBrix.PostgresClient.Tests; //was previously: Npgsql.Tests;

[Collection(NonParallelCollection.Name)]
public class GlobalTypeMapperTests : TestBase, IDisposable
{
    [Fact]
    public async Task MapEnum()
    {
        await using var adminConnection = await OpenConnectionAsync();
        var type = await GetTempTypeName(adminConnection);
        PgSqlConnection.GlobalTypeMapper.MapEnum<Mood>(type);

        await using var dataSource1 = CreateDataSource();

        await using (var connection = await dataSource1.OpenConnectionAsync(TestContext.Current.CancellationToken))
        {
            await connection.ExecuteNonQueryAsync($"CREATE TYPE {type} AS ENUM ('sad', 'ok', 'happy')", cancellationToken: TestContext.Current.CancellationToken);
            await connection.ReloadTypesAsync(TestContext.Current.CancellationToken);

            await AssertType(connection, Mood.Happy, "happy", type, pgSqlDbType: null);
        }

        PgSqlConnection.GlobalTypeMapper.UnmapEnum<Mood>(type);

        // Global mapping changes have no effect on already-built data sources
        await AssertType(dataSource1, Mood.Happy, "happy", type, pgSqlDbType: null);

        // But they do affect new data sources
        await using var dataSource2 = CreateDataSource();
        await AssertType(dataSource2, "happy", "happy", type, pgSqlDbType: null, isDefault: false);
    }

    [Fact]
    public async Task MapEnum_NonGeneric()
    {
        await using var adminConnection = await OpenConnectionAsync();
        var type = await GetTempTypeName(adminConnection);
        PgSqlConnection.GlobalTypeMapper.MapEnum(typeof(Mood), type);

        try
        {
            await using var dataSource1 = CreateDataSource();

            await using (var connection = await dataSource1.OpenConnectionAsync(TestContext.Current.CancellationToken))
            {
                await connection.ExecuteNonQueryAsync($"CREATE TYPE {type} AS ENUM ('sad', 'ok', 'happy')", cancellationToken: TestContext.Current.CancellationToken);
                await connection.ReloadTypesAsync(TestContext.Current.CancellationToken);

                await AssertType(connection, Mood.Happy, "happy", type, pgSqlDbType: null);
            }

            PgSqlConnection.GlobalTypeMapper.UnmapEnum(typeof(Mood), type);

            // Global mapping changes have no effect on already-built data sources
            await AssertType(dataSource1, Mood.Happy, "happy", type, pgSqlDbType: null);

            // But they do affect new data sources
            await using var dataSource2 = CreateDataSource();
            await Assert.ThrowsAsync<InvalidCastException>(() => AssertType(dataSource2, Mood.Happy, "happy", type, pgSqlDbType: null));
        }
        finally
        {
            PgSqlConnection.GlobalTypeMapper.UnmapEnum<Mood>(type);
        }
    }

    [Fact]
    public async Task Reset()
    {
        //Arrange
        await using var adminConnection = await OpenConnectionAsync();
        var type = await GetTempTypeName(adminConnection);
        PgSqlConnection.GlobalTypeMapper.MapEnum<Mood>(type);

        await using var dataSource1 = CreateDataSource();

        await using (var connection = await dataSource1.OpenConnectionAsync(TestContext.Current.CancellationToken))
        {
            await connection.ExecuteNonQueryAsync($"CREATE TYPE {type} AS ENUM ('sad', 'ok', 'happy')", cancellationToken: TestContext.Current.CancellationToken);
            await connection.ReloadTypesAsync(TestContext.Current.CancellationToken);
        }

        //Act
        // A global mapping change has no effects on data sources which have already been built
        PgSqlConnection.GlobalTypeMapper.Reset();

        //Assert
        // Global mapping changes have no effect on already-built data sources
        await AssertType(dataSource1, Mood.Happy, "happy", type, pgSqlDbType: null);

        // But they do affect new data sources
        await using var dataSource2 = CreateDataSource();
        await AssertType(dataSource2, "happy", "happy", type, pgSqlDbType: null, isDefault: false);
    }

    [Fact]
    public void Reset_and_add_resolver()
    {
        PgSqlConnection.GlobalTypeMapper.Reset();
        PgSqlConnection.GlobalTypeMapper.AddTypeInfoResolverFactory(new DummyResolverFactory());
    }

    public void Dispose()
        => PgSqlConnection.GlobalTypeMapper.Reset();

    enum Mood { Sad, Ok, Happy }

    class DummyResolverFactory : PgTypeInfoResolverFactory
    {
        public override IPgTypeInfoResolver CreateResolver() => new DummyResolver();
        public override IPgTypeInfoResolver CreateArrayResolver() => null;

        class DummyResolver : IPgTypeInfoResolver
        {
            public PgTypeInfo GetTypeInfo(Type type, DataTypeName? dataTypeName, PgSerializerOptions options) => null;
        }
    }
}
