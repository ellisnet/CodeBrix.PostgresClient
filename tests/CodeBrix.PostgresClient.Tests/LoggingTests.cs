using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CodeBrix.PostgresClient.PgSqlTypes;
using SilverAssertions;
using Xunit;
using static CodeBrix.PostgresClient.Tests.TestUtil;

namespace CodeBrix.PostgresClient.Tests; //was previously: Npgsql.Tests;

public abstract class LoggingTests(MultiplexingMode multiplexingMode) : MultiplexingTestBase(multiplexingMode)
{
    [Fact]
    public async Task command_ExecuteScalar_single_statement_without_parameters()
    {
        //Arrange
        await using var dataSource = CreateLoggingDataSource(out var listLoggerProvider);
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using var cmd = new PgSqlCommand("SELECT 1", conn);

        //Act
        using (listLoggerProvider.Record())
        {
            await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken);
        }

        //Assert
        var executingCommandEvent = listLoggerProvider.Log.Single(l => l.Id == PgSqlEventId.CommandExecutionCompleted);
        executingCommandEvent.Message.Should().Contain("Command execution completed").And.Contain("SELECT 1");
        AssertLoggingStateContains(executingCommandEvent, "CommandText", "SELECT 1");
        AssertLoggingStateDoesNotContain(executingCommandEvent, "Parameters");

        if (!IsMultiplexing)
            AssertLoggingStateContains(executingCommandEvent, "ConnectorId", conn.ProcessID);
    }

    [Fact]
    public async Task command_ExecuteScalar_single_statement_with_positional_parameters()
    {
        //Arrange
        await using var dataSource = CreateLoggingDataSource(out var listLoggerProvider);
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using var cmd = new PgSqlCommand("SELECT $1, $2", conn);
        cmd.Parameters.Add(new() { Value = 8 });
        cmd.Parameters.Add(new() { PgSqlDbType = PgSqlDbType.Integer, Value = DBNull.Value });

        //Act
        using (listLoggerProvider.Record())
        {
            await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken);
        }

        //Assert
        var executingCommandEvent = listLoggerProvider.Log.Single(l => l.Id == PgSqlEventId.CommandExecutionCompleted);
        executingCommandEvent.Message.Should().Contain("Command execution completed")
            .And.Contain("SELECT $1, $2")
            .And.Contain("Parameters: [8, NULL]");
        AssertLoggingStateContains(executingCommandEvent, "CommandText", "SELECT $1, $2");
        AssertLoggingStateContains(executingCommandEvent, "Parameters", new object[] { 8, "NULL" });

        if (!IsMultiplexing)
            AssertLoggingStateContains(executingCommandEvent, "ConnectorId", conn.ProcessID);
    }

    [Fact]
    public async Task command_ExecuteScalar_single_statement__Should_unwrap_array_and_truncate_and_write_nulls()
    {
        //Arrange
        await using var dataSource = CreateLoggingDataSource(out var listLoggerProvider);
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using var cmd = new PgSqlCommand("SELECT $1, $2, $3, $4, $5, $6", conn);
        cmd.Parameters.Add(new PgSqlParameter<int> { TypedValue = 1024 });
        cmd.Parameters.Add(new PgSqlParameter<int[]> { TypedValue = [1, 2, 3], PgSqlDbType = PgSqlDbType.Array | PgSqlDbType.Integer });
        cmd.Parameters.Add(new PgSqlParameter<int[]> { TypedValue = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12], PgSqlDbType = PgSqlDbType.Array | PgSqlDbType.Integer });
        cmd.Parameters.Add(new PgSqlParameter<int?[]> { TypedValue = [1, null], PgSqlDbType = PgSqlDbType.Array | PgSqlDbType.Integer });
        cmd.Parameters.Add(new PgSqlParameter<int?> { TypedValue = null });
        cmd.Parameters.Add(new() { PgSqlDbType = PgSqlDbType.Integer, Value = DBNull.Value });

        //Act
        using (listLoggerProvider.Record())
        {
            await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken);
        }

        //Assert
        var executingCommandEvent = listLoggerProvider.Log.Single(l => l.Id == PgSqlEventId.CommandExecutionCompleted);
        executingCommandEvent.Message.Should().Contain("Command execution completed")
            .And.Contain("SELECT $1, $2, $3, $4, $5, $6")
            .And.Contain("Parameters: [1024, [1, 2, 3], [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, ...], [1, NULL], NULL, NULL]");
        AssertLoggingStateContains(executingCommandEvent, "CommandText", "SELECT $1, $2, $3, $4, $5, $6");
        AssertLoggingStateContains(executingCommandEvent, "Parameters", new object[] { 1024, "[1, 2, 3]", "[1, 2, 3, 4, 5, 6, 7, 8, 9, 10, ...]", "[1, NULL]", "NULL", "NULL" });

        if (!IsMultiplexing)
            AssertLoggingStateContains(executingCommandEvent, "ConnectorId", conn.ProcessID);
    }

    [Fact]
    public async Task command_ExecuteScalar_single_statement_with_named_parameters()
    {
        //Arrange
        await using var dataSource = CreateLoggingDataSource(out var listLoggerProvider);
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using var cmd = new PgSqlCommand("SELECT @p1, @p2", conn);
        cmd.Parameters.Add(new() { ParameterName = "p1", Value = 8 });
        cmd.Parameters.Add(new() { ParameterName = "p2", PgSqlDbType = PgSqlDbType.Integer, Value = DBNull.Value });

        //Act
        using (listLoggerProvider.Record())
        {
            await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken);
        }

        //Assert
        var executingCommandEvent = listLoggerProvider.Log.Single(l => l.Id == PgSqlEventId.CommandExecutionCompleted);
        executingCommandEvent.Message.Should().Contain("Command execution completed")
            .And.Contain("SELECT $1, $2")
            .And.Contain("Parameters: [8, NULL]");
        AssertLoggingStateContains(executingCommandEvent, "CommandText", "SELECT $1, $2");
        AssertLoggingStateContains(executingCommandEvent, "Parameters", new object[] { 8, "NULL" });

        if (!IsMultiplexing)
            AssertLoggingStateContains(executingCommandEvent, "ConnectorId", conn.ProcessID);
    }

    [Fact]
    public async Task command_ExecuteScalar_single_statement_with_parameter_logging_off()
    {
        //Arrange
        await using var dataSource = CreateLoggingDataSource(out var listLoggerProvider, sensitiveDataLoggingEnabled: false);
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using var cmd = new PgSqlCommand("SELECT $1, $2", conn);
        cmd.Parameters.Add(new() { Value = 8 });
        cmd.Parameters.Add(new() { Value = 9 });

        //Act
        using (listLoggerProvider.Record())
        {
            await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken);
        }

        //Assert
        var executingCommandEvent = listLoggerProvider.Log.Single(l => l.Id == PgSqlEventId.CommandExecutionCompleted);
        executingCommandEvent.Message.Should().Contain("Command execution completed").And.Contain($"SELECT $1, $2");
        AssertLoggingStateContains(executingCommandEvent, "CommandText", "SELECT $1, $2");
        AssertLoggingStateDoesNotContain(executingCommandEvent, "Parameters");
    }

    [Fact]
    public async Task command_ExecuteScalar_multiple_statement_without_parameters()
    {
        //Arrange
        await using var dataSource = CreateLoggingDataSource(out var listLoggerProvider);
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using var cmd = new PgSqlCommand("SELECT 1; SELECT 2", conn);

        //Act
        using (listLoggerProvider.Record())
        {
            await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken);
        }

        //Assert
        var executingCommandEvent = listLoggerProvider.Log.Single(l => l.Id == PgSqlEventId.CommandExecutionCompleted);
        executingCommandEvent.Message.Should().Contain("Batch execution completed").And.Contain("[(SELECT 1, []), (SELECT 2, [])]");
        var batchCommands = (IList<(string CommandText, IEnumerable<object> Parameters)>)AssertLoggingStateContains(executingCommandEvent, "BatchCommands");
        batchCommands.Count.Should().Be(2);
        batchCommands[0].CommandText.Should().Be("SELECT 1");
        batchCommands[0].Parameters.Should().BeEmpty();
        batchCommands[1].CommandText.Should().Be("SELECT 2");
        batchCommands[1].Parameters.Should().BeEmpty();
        AssertLoggingStateDoesNotContain(executingCommandEvent, "Parameters");

        if (!IsMultiplexing)
            AssertLoggingStateContains(executingCommandEvent, "ConnectorId", conn.ProcessID);
    }

    [Fact]
    public async Task command_ExecuteScalar_multiple_statement_with_parameters()
    {
        //Arrange
        await using var dataSource = CreateLoggingDataSource(out var listLoggerProvider);
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using var cmd = new PgSqlCommand("SELECT @p1; SELECT @p2", conn);
        cmd.Parameters.Add(new() { ParameterName = "p1", Value = 8 });
        cmd.Parameters.Add(new() { ParameterName = "p2", Value = 9 });

        //Act
        using (listLoggerProvider.Record())
        {
            await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken);
        }

        //Assert
        var executingCommandEvent = listLoggerProvider.Log.Single(l => l.Id == PgSqlEventId.CommandExecutionCompleted);
        executingCommandEvent.Message.Should().Contain("Batch execution completed").And.Contain("[(SELECT $1, [8]), (SELECT $1, [9])]");
        var batchCommands = (IList<(string CommandText, IEnumerable<object> Parameters)>)AssertLoggingStateContains(executingCommandEvent, "BatchCommands");
        batchCommands.Count.Should().Be(2);
        batchCommands[0].CommandText.Should().Be("SELECT $1");
        batchCommands[0].Parameters.First().Should().Be(8);
        batchCommands[1].CommandText.Should().Be("SELECT $1");
        batchCommands[1].Parameters.First().Should().Be(9);
        AssertLoggingStateDoesNotContain(executingCommandEvent, "Parameters");

        if (!IsMultiplexing)
            AssertLoggingStateContains(executingCommandEvent, "ConnectorId", conn.ProcessID);
    }

    [Fact]
    public async Task command_ExecuteScalar_multiple_statement_with_parameter_logging_off()
    {
        //Arrange
        await using var dataSource = CreateLoggingDataSource(out var listLoggerProvider, sensitiveDataLoggingEnabled: false);
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using var cmd = new PgSqlCommand("SELECT @p1; SELECT @p2", conn);
        cmd.Parameters.Add(new() { ParameterName = "p1", Value = 8 });
        cmd.Parameters.Add(new() { ParameterName = "p2", Value = 9 });

        //Act
        using (listLoggerProvider.Record())
        {
            await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken);
        }

        //Assert
        var executingCommandEvent = listLoggerProvider.Log.Single(l => l.Id == PgSqlEventId.CommandExecutionCompleted);
        executingCommandEvent.Message.Should().Contain("Batch execution completed").And.Contain("[SELECT $1, SELECT $1]");
        var batchCommands = (IList<string>)AssertLoggingStateContains(executingCommandEvent, "BatchCommands");
        batchCommands.Count.Should().Be(2);
        batchCommands[0].Should().Be("SELECT $1");
        batchCommands[1].Should().Be("SELECT $1");
        AssertLoggingStateDoesNotContain(executingCommandEvent, "Parameters");

        if (!IsMultiplexing)
            AssertLoggingStateContains(executingCommandEvent, "ConnectorId", conn.ProcessID);
    }

    [Fact]
    public async Task batch_ExecuteScalar_single_statement_without_parameters()
    {
        //Arrange
        await using var dataSource = CreateLoggingDataSource(out var listLoggerProvider);
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using var cmd = new PgSqlBatch(conn)
        {
            BatchCommands = { new("SELECT 1") }
        };

        //Act
        using (listLoggerProvider.Record())
        {
            await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken);
        }

        //Assert
        var executingCommandEvent = listLoggerProvider.Log.Single(l => l.Id == PgSqlEventId.CommandExecutionCompleted);
        executingCommandEvent.Message.Should().Contain("Command execution completed").And.Contain("SELECT 1");
        AssertLoggingStateContains(executingCommandEvent, "CommandText", "SELECT 1");
        AssertLoggingStateDoesNotContain(executingCommandEvent, "Parameters");

        if (!IsMultiplexing)
            AssertLoggingStateContains(executingCommandEvent, "ConnectorId", conn.ProcessID);
    }

    [Fact]
    public async Task batch_ExecuteScalar_multiple_statements_with_parameters()
    {
        //Arrange
        await using var dataSource = CreateLoggingDataSource(out var listLoggerProvider);
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using var batch = new PgSqlBatch(conn)
        {
            BatchCommands =
            {
                new("SELECT $1") { Parameters = { new() { Value = 8 } } },
                new("SELECT $1, 9") { Parameters = { new() { Value = 9 } } }
            }
        };

        //Act
        using (listLoggerProvider.Record())
        {
            await batch.ExecuteScalarAsync(TestContext.Current.CancellationToken);
        }

        //Assert
        var executingCommandEvent = listLoggerProvider.Log.Single(l => l.Id == PgSqlEventId.CommandExecutionCompleted);
        executingCommandEvent.Message.Should().Contain("Batch execution completed").And.Contain("[(SELECT $1, [8]), (SELECT $1, 9, [9])]");
        AssertLoggingStateDoesNotContain(executingCommandEvent, "CommandText");
        AssertLoggingStateDoesNotContain(executingCommandEvent, "Parameters");

        if (!IsMultiplexing)
            AssertLoggingStateContains(executingCommandEvent, "ConnectorId", conn.ProcessID);

        var batchCommands = (IList<(string CommandText, IEnumerable<object> Parameters)>)AssertLoggingStateContains(executingCommandEvent, "BatchCommands");
        batchCommands.Count.Should().Be(2);
        batchCommands[0].CommandText.Should().Be("SELECT $1");
        batchCommands[0].Parameters.First().Should().Be(8);
        batchCommands[1].CommandText.Should().Be("SELECT $1, 9");
        batchCommands[1].Parameters.First().Should().Be(9);
    }

    [Fact]
    public async Task batch_ExecuteScalar_single_statement_with_parameter_logging_off()
    {
        //Arrange
        await using var dataSource = CreateLoggingDataSource(out var listLoggerProvider, sensitiveDataLoggingEnabled: false);
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using var batch = new PgSqlBatch(conn)
        {
            BatchCommands =
            {
                new("SELECT $1") { Parameters = { new() { Value = 8 } } },
                new("SELECT $1, 9") { Parameters = { new() { Value = 9 } } }
            }
        };

        //Act
        using (listLoggerProvider.Record())
        {
            await batch.ExecuteScalarAsync(TestContext.Current.CancellationToken);
        }

        //Assert
        var executingCommandEvent = listLoggerProvider.Log.Single(l => l.Id == PgSqlEventId.CommandExecutionCompleted);
        executingCommandEvent.Message.Should().Contain("Batch execution completed").And.Contain("[SELECT $1, SELECT $1, 9]");
        var batchCommands = (IList<string>)AssertLoggingStateContains(executingCommandEvent, "BatchCommands");
        batchCommands.Count.Should().Be(2);
        batchCommands[0].Should().Be("SELECT $1");
        batchCommands[1].Should().Be("SELECT $1, 9");
    }
}

public sealed class LoggingTests_NonMultiplexing() : LoggingTests(MultiplexingMode.NonMultiplexing);
public sealed class LoggingTests_Multiplexing() : LoggingTests(MultiplexingMode.Multiplexing);
