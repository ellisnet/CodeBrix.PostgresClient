using System.Data;
using System.Threading.Tasks;
using SilverAssertions;
using Xunit;
using static CodeBrix.PostgresClient.Tests.TestUtil;

namespace CodeBrix.PostgresClient.Tests; //was previously: Npgsql.Tests;

public class AsyncTests : TestBase
{
    [Fact]
    public async Task non_query()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        var tableName = await CreateTempTable(conn, "int INTEGER");
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"INSERT INTO {tableName} (int) VALUES (4)";

        //Act
        await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);

        //Assert
        (await conn.ExecuteScalarAsync($"SELECT int FROM {tableName}", cancellationToken: TestContext.Current.CancellationToken)).Should().Be(4);
    }

    [Fact]
    public async Task scalar()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        await using var cmd = new PgSqlCommand("SELECT 1", conn);

        //Act
        var result = await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken);

        //Assert
        result.Should().Be(1);
    }

    [Fact]
    public async Task reader()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        await using var cmd = new PgSqlCommand("SELECT 1", conn);

        //Act
        await using var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        await reader.ReadAsync(TestContext.Current.CancellationToken);

        //Assert
        reader[0].Should().Be(1);
    }

    [Fact]
    public async Task columnar()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        await using var cmd = new PgSqlCommand("SELECT NULL, 2, 'Some Text'", conn);

        //Act
        await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken: TestContext.Current.CancellationToken);
        await reader.ReadAsync(TestContext.Current.CancellationToken);

        //Assert
        (await reader.IsDBNullAsync(0, cancellationToken: TestContext.Current.CancellationToken)).Should().BeTrue();
        (await reader.GetFieldValueAsync<string>(2, cancellationToken: TestContext.Current.CancellationToken)).Should().Be("Some Text");
    }
}
