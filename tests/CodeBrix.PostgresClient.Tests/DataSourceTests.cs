using System;
using System.Data;
using System.Data.Common;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using SilverAssertions;
using Xunit;
using static CodeBrix.PostgresClient.Tests.TestUtil;

// ReSharper disable MethodHasAsyncOverload

namespace CodeBrix.PostgresClient.Tests; //was previously: Npgsql.Tests;

public class DataSourceTests : TestBase
{
    [Fact]
    public async Task create_connection()
    {
        //Arrange
        await using var dataSource = PgSqlDataSource.Create(ConnectionString);

        //Act
        await using var connection = dataSource.CreateConnection();

        //Assert
        connection.State.Should().Be(ConnectionState.Closed);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        (await connection.ExecuteScalarAsync("SELECT 1", cancellationToken: TestContext.Current.CancellationToken)).Should().Be(1);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task open_connection(bool async)
    {
        //Arrange
        await using var dataSource = PgSqlDataSource.Create(ConnectionString);

        //Act
        await using var connection = async
            ? await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken)
            : dataSource.OpenConnection();

        //Assert
        connection.State.Should().Be(ConnectionState.Open);
        (await connection.ExecuteScalarAsync("SELECT 1", cancellationToken: TestContext.Current.CancellationToken)).Should().Be(1);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExecuteScalar_on_connectionless_command(bool async)
    {
        //Arrange
        await using var dataSource = PgSqlDataSource.Create(ConnectionString);
        await using var command = dataSource.CreateCommand();
        command.CommandText = "SELECT 1";

        //Assert
        if (async)
            (await command.ExecuteScalarAsync(TestContext.Current.CancellationToken)).Should().Be(1);
        else
            command.ExecuteScalar().Should().Be(1);

        dataSource.Statistics.Should().Be((1, 1, 0));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExecuteNonQuery_on_connectionless_command(bool async)
    {
        //Arrange
        await using var dataSource = PgSqlDataSource.Create(ConnectionString);
        await using var command = dataSource.CreateCommand();
        command.CommandText = "SELECT 1";

        //Assert
        if (async)
            (await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken)).Should().Be(-1);
        else
            command.ExecuteNonQuery().Should().Be(-1);

        dataSource.Statistics.Should().Be((1, 1, 0));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExecuteReader_on_connectionless_command(bool async)
    {
        //Arrange
        await using var dataSource = PgSqlDataSource.Create(ConnectionString);
        await using var command = dataSource.CreateCommand();
        command.CommandText = "SELECT 1";

        //Assert
        await using (var reader = async ? await command.ExecuteReaderAsync(TestContext.Current.CancellationToken) : command.ExecuteReader())
        {
            reader.Read().Should().BeTrue();
            reader.GetInt32(0).Should().Be(1);
        }

        dataSource.Statistics.Should().Be((1, 1, 0));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExecuteScalar_on_connectionless_batch(bool async)
    {
        //Arrange
        await using var dataSource = PgSqlDataSource.Create(ConnectionString);
        await using var batch = dataSource.CreateBatch();
        batch.BatchCommands.Add(new("SELECT 1"));
        batch.BatchCommands.Add(new("SELECT 2"));

        //Assert
        if (async)
            (await batch.ExecuteScalarAsync(TestContext.Current.CancellationToken)).Should().Be(1);
        else
            batch.ExecuteScalar().Should().Be(1);

        dataSource.Statistics.Should().Be((1, 1, 0));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExecuteNonQuery_on_connectionless_batch(bool async)
    {
        //Arrange
        await using var dataSource = PgSqlDataSource.Create(ConnectionString);
        await using var batch = dataSource.CreateBatch();
        batch.BatchCommands.Add(new("SELECT 1"));
        batch.BatchCommands.Add(new("SELECT 2"));

        //Assert
        if (async)
            (await batch.ExecuteNonQueryAsync(TestContext.Current.CancellationToken)).Should().Be(-1);
        else
            batch.ExecuteNonQuery().Should().Be(-1);

        dataSource.Statistics.Should().Be((1, 1, 0));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExecuteReader_on_connectionless_batch(bool async)
    {
        //Arrange
        await using var dataSource = PgSqlDataSource.Create(ConnectionString);
        await using var batch = dataSource.CreateBatch();
        batch.BatchCommands.Add(new("SELECT 1"));
        batch.BatchCommands.Add(new("SELECT 2"));

        //Assert
        using (var reader = async ? await batch.ExecuteReaderAsync(TestContext.Current.CancellationToken) : batch.ExecuteReader())
        {
            reader.Read().Should().BeTrue();
            reader.GetInt32(0).Should().Be(1);
            reader.NextResult().Should().BeTrue();
            reader.Read().Should().BeTrue();
            reader.GetInt32(0).Should().Be(2);
        }

        dataSource.Statistics.Should().Be((1, 1, 0));
    }

    [Fact]
    public void Clear()
    {
        //Arrange
        using var dataSource = PgSqlDataSource.Create(ConnectionString);
        var connection1 = dataSource.OpenConnection();
        var connection2 = dataSource.OpenConnection();
        connection1.Close();
        dataSource.Statistics.Should().Be((2, 1, 1));

        //Act
        dataSource.Clear();

        //Assert
        dataSource.Statistics.Should().Be((1, 0, 1));

        var connection3 = dataSource.OpenConnection();
        dataSource.Statistics.Should().Be((2, 0, 2));

        connection2.Close();
        dataSource.Statistics.Should().Be((1, 0, 1));

        connection3.Close();
        dataSource.Statistics.Should().Be((1, 1, 0));
    }

    [Fact]
    public void Dispose()
    {
        //Arrange
        using var dataSource = PgSqlDataSource.Create(ConnectionString);
        var connection1 = dataSource.OpenConnection();
        var connection2 = dataSource.OpenConnection();
        connection1.Close();
        dataSource.Statistics.Should().Be((2, 1, 1));

        //Act
        dataSource.Dispose();

        //Assert
        dataSource.Statistics.Should().Be((1, 0, 1));

        var act = () => dataSource.OpenConnection();
        act.Should().ThrowExactly<ObjectDisposedException>();
        dataSource.Statistics.Should().Be((1, 0, 1));

        connection2.Close();
        dataSource.Statistics.Should().Be((0, 0, 0));
    }

    [Fact]
    public async Task DisposeAsync()
    {
        //Arrange
        await using var dataSource = PgSqlDataSource.Create(ConnectionString);
        var connection1 = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        var connection2 = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await connection1.CloseAsync();
        dataSource.Statistics.Should().Be((2, 1, 1));

        //Act
        await dataSource.DisposeAsync();

        //Assert
        var act = async () => await dataSource.OpenConnectionAsync();
        await act.Should().ThrowExactlyAsync<ObjectDisposedException>();
        dataSource.Statistics.Should().Be((1, 0, 1));

        await connection2.CloseAsync();
        dataSource.Statistics.Should().Be((0, 0, 0));
    }

    [Fact]
    public void no_password_without_PersistSecurityInfo()
    {
        //Arrange
        if (string.IsNullOrEmpty(new PgSqlConnectionStringBuilder(ConnectionString).Password))
            Assert.Fail("No password in default connection string, test cannot run");

        //Act
        using var dataSource = PgSqlDataSource.Create(ConnectionString);
        var parsedConnectionString = new PgSqlConnectionStringBuilder(dataSource.ConnectionString);

        //Assert
        parsedConnectionString.Password.Should().BeNull();
    }

    [Fact]
    public async Task cannot_access_connection_transaction_on_data_source_command()
    {
        //Arrange
        await using var command = DataSource.CreateCommand();

        //Assert
        ((Func<object>)(() => command.Connection)).Should().ThrowExactly<NotSupportedException>();
        ((Action)(() => command.Connection = null)).Should().ThrowExactly<NotSupportedException>();
        ((Func<object>)(() => command.Transaction)).Should().ThrowExactly<NotSupportedException>();
        ((Action)(() => command.Transaction = null)).Should().ThrowExactly<NotSupportedException>();

        ((Action)(() => command.Prepare())).Should().ThrowExactly<NotSupportedException>();
        await ((Func<Task>)(() => command.PrepareAsync())).Should().ThrowExactlyAsync<NotSupportedException>();
    }

    [Fact]
    public async Task cannot_access_connection_transaction_on_data_source_batch()
    {
        //Arrange
        await using var batch = DataSource.CreateBatch();

        //Assert
        ((Func<object>)(() => batch.Connection)).Should().ThrowExactly<NotSupportedException>();
        ((Action)(() => batch.Connection = null)).Should().ThrowExactly<NotSupportedException>();
        ((Func<object>)(() => batch.Transaction)).Should().ThrowExactly<NotSupportedException>();
        ((Action)(() => batch.Transaction = null)).Should().ThrowExactly<NotSupportedException>();

        ((Action)(() => batch.Prepare())).Should().ThrowExactly<NotSupportedException>();
        await ((Func<Task>)(() => batch.PrepareAsync())).Should().ThrowExactlyAsync<NotSupportedException>();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task cannot_get_connection_after_dispose_pooled(bool async)
    {
        //Arrange
        var dataSource = PgSqlDataSource.Create(ConnectionString);

        //Assert
        if (async)
        {
            await dataSource.DisposeAsync();
            var act = async () => await dataSource.OpenConnectionAsync();
            await act.Should().ThrowExactlyAsync<ObjectDisposedException>();
        }
        else
        {
            dataSource.Dispose();
            var act = () => dataSource.OpenConnection();
            act.Should().ThrowExactly<ObjectDisposedException>();
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task cannot_get_connection_after_dispose_unpooled(bool async)
    {
        //Arrange
        var connectionStringBuilder = new PgSqlConnectionStringBuilder(ConnectionString) { Pooling = false };
        var dataSource = PgSqlDataSource.Create(connectionStringBuilder);

        //Assert
        if (async)
        {
            await dataSource.DisposeAsync();
            var act = async () => await dataSource.OpenConnectionAsync();
            await act.Should().ThrowExactlyAsync<ObjectDisposedException>();
        }
        else
        {
            dataSource.Dispose();
            var act = () => dataSource.OpenConnection();
            act.Should().ThrowExactly<ObjectDisposedException>();
        }
    }

    [Theory] // #4752
    [InlineData(true)]
    [InlineData(false)]
    public async Task as_DbDataSource(bool async)
    {
        //Arrange
        await using DbDataSource dataSource = PgSqlDataSource.Create(ConnectionString);

        //Act
        await using var connection = async
            ? await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken)
            : dataSource.OpenConnection();

        //Assert
        connection.State.Should().Be(ConnectionState.Open);

        await using var command = dataSource.CreateCommand("SELECT 1");

        (async
            ? await command.ExecuteScalarAsync(TestContext.Current.CancellationToken)
            : command.ExecuteScalar()).Should().Be(1);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task executing_command_on_disposed_datasource(bool multiplexing)
    {
        //Arrange
        var csb = new PgSqlConnectionStringBuilder(ConnectionString)
        {
            Multiplexing = multiplexing
        };
        DbDataSource dataSource = PgSqlDataSource.Create(csb.ConnectionString);
        await using (var _ = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken)) {}
        await dataSource.DisposeAsync();

        //Act
        await using var command = dataSource.CreateCommand("SELECT 1");

        //Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(() => command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/4840")]
    public async Task multiplexing_connectionless_command_open_connection()
    {
        //Arrange
        var csb = new PgSqlConnectionStringBuilder(ConnectionString)
        {
            Multiplexing = true
        };
        await using var dataSource = PgSqlDataSource.Create(csb.ConnectionString);

        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using var _ = await conn.BeginTransactionAsync(TestContext.Current.CancellationToken);

        await using var command = dataSource.CreateCommand();
        command.CommandText = "SELECT 1";

        //Act
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);

        //Assert
        reader.Read().Should().BeTrue();
        reader.GetInt32(0).Should().Be(1);
    }

    [Fact]
    public async Task connection_string_builder_settings_are_frozen_on_Build()
    {
        //Arrange
        var builder = CreateDataSourceBuilder();
        builder.ConnectionStringBuilder.ApplicationName = "foo";
        await using var dataSource = builder.Build();

        //Act
        builder.ConnectionStringBuilder.ApplicationName = "bar";

        //Assert
        await using var command = dataSource.CreateCommand("SHOW application_name");
        (await command.ExecuteScalarAsync(TestContext.Current.CancellationToken)).Should().Be("foo");
    }

    class Test
    {
        public int Id { get; set; }
    }

    [Fact]
    public async Task ConfigureJsonOptions_is_order_independent()
    {
        // Expect failure, no options
        {
            var builder = CreateDataSourceBuilder();
            builder.EnableDynamicJson();
            await using var dataSource = builder.Build();

            await using var command = dataSource.CreateCommand("SELECT '{\"id\": 1}'::json;");
            using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
            reader.Read();
            reader.GetFieldValue<Test>(0).Id.Should().Be(default(int));
        }

        // Expect success, ConfigureJsonOptions before EnableDynamicJson
        {
            var builder = CreateDataSourceBuilder();
            builder.ConfigureJsonOptions(new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            builder.EnableDynamicJson();
            await using var dataSource = builder.Build();

            await using var command = dataSource.CreateCommand("SELECT '{\"id\": 1}'::json;");
            using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
            reader.Read();
            reader.GetFieldValue<Test>(0).Id.Should().Be(1);
        }

        // Expect success, EnableDynamicJson before ConfigureJsonOptions
        {
            var builder = CreateDataSourceBuilder();
            builder.EnableDynamicJson();
            builder.ConfigureJsonOptions(new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            await using var dataSource = builder.Build();

            await using var command = dataSource.CreateCommand("SELECT '{\"id\": 1}'::json;");
            using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
            reader.Read();
            reader.GetFieldValue<Test>(0).Id.Should().Be(1);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ReloadTypes(bool async)
    {
        //Arrange
        await using var adminConnection = await OpenConnectionAsync();
        var type = await GetTempTypeName(adminConnection);

        var dataSourceBuilder = CreateDataSourceBuilder();
        dataSourceBuilder.MapEnum<Mood>(type);
        await using var dataSource = dataSourceBuilder.Build();

        await using var connection = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await connection.ExecuteNonQueryAsync($"CREATE TYPE {type} AS ENUM ('sad', 'ok', 'happy')", cancellationToken: TestContext.Current.CancellationToken);

        //Act
        if (async)
            await dataSource.ReloadTypesAsync(TestContext.Current.CancellationToken);
        else
            dataSource.ReloadTypes();

        //Assert
        await Assert.ThrowsAsync<InvalidCastException>(async () => await connection.ExecuteScalarAsync($"SELECT 'happy'::{type}", cancellationToken: TestContext.Current.CancellationToken));

        // Close connection and reopen to make sure it picks up the new type and mapping from the data source
        await connection.CloseAsync();
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        var act = async () => await connection.ExecuteScalarAsync($"SELECT 'happy'::{type}");
        await act.Should().NotThrowAsync();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ReloadTypes_across_data_sources(bool async)
    {
        //Arrange
        await using var adminConnection = await OpenConnectionAsync();
        var type = await GetTempTypeName(adminConnection);

        var dataSourceBuilder = CreateDataSourceBuilder();
        dataSourceBuilder.MapEnum<Mood>(type);
        await using var dataSource1 = dataSourceBuilder.Build();
        await using var connection1 = await dataSource1.OpenConnectionAsync(TestContext.Current.CancellationToken);

        await using var dataSource2 = dataSourceBuilder.Build();
        await using var connection2 = await dataSource2.OpenConnectionAsync(TestContext.Current.CancellationToken);

        await connection1.ExecuteNonQueryAsync($"CREATE TYPE {type} AS ENUM ('sad', 'ok', 'happy')", cancellationToken: TestContext.Current.CancellationToken);

        //Act
        if (async)
            await dataSource1.ReloadTypesAsync(TestContext.Current.CancellationToken);
        else
            dataSource1.ReloadTypes();

        //Assert
        await Assert.ThrowsAsync<InvalidCastException>(async () => await connection1.ExecuteScalarAsync($"SELECT 'happy'::{type}", cancellationToken: TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidCastException>(async () => await connection2.ExecuteScalarAsync($"SELECT 'happy'::{type}", cancellationToken: TestContext.Current.CancellationToken));

        // Close connection and reopen to check that the new type and mapping is not available in dataSource2
        await connection2.CloseAsync();
        await connection2.OpenAsync(TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidCastException>(async () => await connection2.ExecuteScalarAsync($"SELECT 'happy'::{type}", cancellationToken: TestContext.Current.CancellationToken));

        await dataSource2.ReloadTypesAsync(TestContext.Current.CancellationToken);

        // Close connection2 and reopen to make sure it picks up the new type and mapping from dataSource2
        await connection2.CloseAsync();
        await connection2.OpenAsync(TestContext.Current.CancellationToken);

        var act = async () => await connection2.ExecuteScalarAsync($"SELECT 'happy'::{type}");
        await act.Should().NotThrowAsync();
    }

    enum Mood { Sad, Ok, Happy }
}
