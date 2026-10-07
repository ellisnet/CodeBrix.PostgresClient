using System.Threading.Tasks;
using SilverAssertions;
using Xunit;

namespace CodeBrix.PostgresClient.Tests.Support;

public class TestDatabaseTests
{
    [Fact]
    public async Task ConnectionString_opens_a_connection_to_the_test_database()
    {
        //Arrange
        await using var connection = new PgSqlConnection(TestDatabase.ConnectionString);

        //Act
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new PgSqlCommand("SELECT current_user, current_database()", connection);
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        await reader.ReadAsync(TestContext.Current.CancellationToken);

        //Assert
        reader.GetString(0).Should().Be(TestDatabase.TestUser);
        reader.GetString(1).Should().Be(TestDatabase.TestUser);
    }

    [Theory]
    [InlineData("pgsql_tests_scram", "Disable")]
    [InlineData("pgsql_tests_ssl", "Require")]
    [InlineData("pgsql_tests_nossl", "Disable")]
    public async Task container_accepts_the_special_test_users(string user, string sslMode)
    {
        //Arrange
        var builder = new PgSqlConnectionStringBuilder(TestDatabase.ConnectionString)
        {
            Username = user,
            Password = user,
            SslMode = System.Enum.Parse<SslMode>(sslMode),
            Pooling = false
        };
        await using var connection = new PgSqlConnection(builder.ConnectionString);

        //Act
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new PgSqlCommand("SELECT ssl FROM pg_stat_ssl WHERE pid = pg_backend_pid()", connection);
        var usesSsl = await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);

        //Assert
        usesSsl.Should().Be(sslMode == "Require");
    }

    [Fact]
    public async Task container_has_logical_replication_enabled()
    {
        //Arrange
        await using var connection = new PgSqlConnection(TestDatabase.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new PgSqlCommand("SHOW wal_level", connection);

        //Act
        var walLevel = await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);

        //Assert
        walLevel.Should().Be("logical");
    }
}
