using System;
using System.Threading.Tasks;
using CodeBrix.PostgresClient.PgSqlTypes;
using SilverAssertions;
using Xunit;
using static CodeBrix.PostgresClient.Tests.TestUtil;

namespace CodeBrix.PostgresClient.Tests.Types; //was previously: Npgsql.Tests.Types;

public abstract class DomainTests(MultiplexingMode multiplexingMode) : MultiplexingTestBase(multiplexingMode)
{
    // Resolves a domain type handler via the different pathways
    [Fact]
    public async Task domain_resolution()
    {
        if (IsMultiplexing)
            Assert.Skip("Multiplexing, ReloadTypes");

        await using var dataSource = CreateDataSource(csb => csb.Pooling = false);
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        var type = await GetTempTypeName(conn);
        await conn.ExecuteNonQueryAsync($"CREATE DOMAIN {type} AS text", cancellationToken: TestContext.Current.CancellationToken);

        // Resolve type by DataTypeName
        conn.ReloadTypes();
        using (var cmd = new PgSqlCommand("SELECT @p", conn))
        {
            cmd.Parameters.Add(new PgSqlParameter { ParameterName="p", DataTypeName = type, Value = DBNull.Value });
            using (var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken))
            {
                reader.Read();
                reader.GetDataTypeName(0).Should().Be("text");
            }
        }

        // When sending back domain types, PG sends back the type OID of their base type. So we never need to resolve domains from
        // a type OID.
        conn.ReloadTypes();
        using (var cmd = new PgSqlCommand($"SELECT 'foo'::{type}", conn))
        using (var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken))
        {
            reader.Read();
            reader.GetDataTypeName(0).Should().Be("text");
            reader.GetString(0).Should().Be("foo");
        }
    }

    [Fact]
    public async Task domain()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        var type = await GetTempTypeName(conn);
        await conn.ExecuteNonQueryAsync($"CREATE DOMAIN {type} AS text", cancellationToken: TestContext.Current.CancellationToken);
        //Assert
        (await conn.ExecuteScalarAsync($"SELECT 'foo'::{type}", cancellationToken: TestContext.Current.CancellationToken)).Should().Be("foo");
    }

    [Fact]
    public async Task domain_in_composite()
    {
        //Arrange
        await using var adminConnection = await OpenConnectionAsync();
        var domainType = await GetTempTypeName(adminConnection);
        var compositeType = await GetTempTypeName(adminConnection);
        await adminConnection.ExecuteNonQueryAsync($@"
CREATE DOMAIN {domainType} AS text;
CREATE TYPE {compositeType} AS (value {domainType});", cancellationToken: TestContext.Current.CancellationToken);

        var dataSourceBuilder = CreateDataSourceBuilder();
        dataSourceBuilder.MapComposite<SomeComposite>(compositeType);
        await using var dataSource = dataSourceBuilder.Build();
        await using var connection = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        //Act
        var result = (SomeComposite)(await connection.ExecuteScalarAsync($"SELECT ROW('foo')::{compositeType}", cancellationToken: TestContext.Current.CancellationToken));
        //Assert
        result.Value.Should().Be("foo");
    }

    class SomeComposite
    {
        public string Value { get; set; }
    }

    [Fact]
    public async Task domain_over_range()
    {
        //Arrange
        await using var adminConnection = await OpenConnectionAsync();
        var type = await GetTempTypeName(adminConnection);
        var rangeType = await GetTempTypeName(adminConnection);

        await adminConnection.ExecuteNonQueryAsync($"CREATE DOMAIN {type} AS integer; CREATE TYPE {rangeType} AS RANGE(subtype={type})", cancellationToken: TestContext.Current.CancellationToken);

        var dataSourceBuilder = CreateDataSourceBuilder();
        dataSourceBuilder.EnableUnmappedTypes();
        await using var dataSource = dataSourceBuilder.Build();
        await using var connection = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        //Assert
        await AssertType(
            connection,
            new PgSqlRange<int>(1, 2),
            "[1,2]",
            rangeType,
            pgSqlDbType: null,
            isDefaultForWriting: false);
    }
}

public sealed class DomainTests_NonMultiplexing() : DomainTests(MultiplexingMode.NonMultiplexing);
public sealed class DomainTests_Multiplexing() : DomainTests(MultiplexingMode.Multiplexing);
