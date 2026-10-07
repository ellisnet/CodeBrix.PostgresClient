using System.Data;
using System.Threading.Tasks;
using CodeBrix.PostgresClient.PgSqlTypes;
using SilverAssertions;
using Xunit;
using static CodeBrix.PostgresClient.Tests.TestUtil;

namespace CodeBrix.PostgresClient.Tests.Types; //was previously: Npgsql.Tests.Types;

public abstract class JsonPathTests(MultiplexingMode multiplexingMode) : MultiplexingTestBase(multiplexingMode)
{
    public static readonly TheoryData<string, string> ReadWriteCases = new()
    {
        { "'$'", "$" },
        { "'$\"varname\"'", "$\"varname\"" }
    };

    [Theory]
    [InlineData("$")]
    [InlineData("$\"varname\"")]
    public async Task json_path(string jsonPath)
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        MinimumPgVersion(conn, "12.0", "The jsonpath type was introduced in PostgreSQL 12");

        //Assert
        await AssertType(
            jsonPath, jsonPath, "jsonpath", PgSqlDbType.JsonPath, isDefaultForWriting: false, isPgSqlDbTypeInferredFromClrType: false,
            inferredDbType: DbType.Object);
    }

    [Theory]
    [MemberData(nameof(ReadWriteCases))]
    public async Task read(string query, string expected)
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        MinimumPgVersion(conn, "12.0", "The jsonpath type was introduced in PostgreSQL 12");

        //Act
        using var cmd = new PgSqlCommand($"SELECT {query}::jsonpath", conn);
        using var rdr = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        rdr.Read();

        //Assert
        rdr.GetFieldValue<string>(0).Should().Be(expected);
        rdr.GetTextReader(0).ReadToEnd().Should().Be(expected);
    }

    [Theory]
    [MemberData(nameof(ReadWriteCases))]
    public async Task write(string query, string expected)
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        MinimumPgVersion(conn, "12.0", "The jsonpath type was introduced in PostgreSQL 12");

        //Act
        using var cmd = new PgSqlCommand($"SELECT 'Passed' WHERE @p::text = {query}::text", conn) { Parameters = { new PgSqlParameter("p", PgSqlDbType.JsonPath) { Value = expected } } };
        using var rdr = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);

        //Assert
        rdr.Read().Should().BeTrue();
    }
}

public sealed class JsonPathTests_NonMultiplexing() : JsonPathTests(MultiplexingMode.NonMultiplexing);
public sealed class JsonPathTests_Multiplexing() : JsonPathTests(MultiplexingMode.Multiplexing);
