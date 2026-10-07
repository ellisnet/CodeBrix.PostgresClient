using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.PostgresClient.BackendMessages;
using CodeBrix.PostgresClient.Internal;
using CodeBrix.PostgresClient.Internal.Postgres;
using CodeBrix.PostgresClient.PgSqlTypes;
using CodeBrix.PostgresClient.Tests.Support;
using SilverAssertions;
using Xunit;
using static CodeBrix.PostgresClient.Tests.TestUtil;

namespace CodeBrix.PostgresClient.Tests; //was previously: Npgsql.Tests;

public abstract class CommandTests(MultiplexingMode multiplexingMode) : MultiplexingTestBase(multiplexingMode)
{
    static uint Int4Oid => PostgresMinimalDatabaseInfo.DefaultTypeCatalog.GetOid(DataTypeNames.Int4).Value;
    static uint TextOid => PostgresMinimalDatabaseInfo.DefaultTypeCatalog.GetOid(DataTypeNames.Text).Value;

    #region Legacy batching

    [Theory]
    [InlineData(new[] { true })]
    [InlineData(new[] { false })]
    [InlineData(new[] { true, true })]
    [InlineData(new[] { false, false })]
    [InlineData(new[] { false, true })]
    [InlineData(new[] { true, false })]
    public async Task multiple_statements(bool[] queries)
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "name TEXT");
        var sb = new StringBuilder();
        foreach (var query in queries)
            sb.Append(query ? "SELECT 1;" : $"UPDATE {table} SET name='yo' WHERE 1=0;");
        var sql = sb.ToString();

        foreach (var prepare in new[] { false, true })
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            if (prepare && !IsMultiplexing)
                await cmd.PrepareAsync(TestContext.Current.CancellationToken);
            await using var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
            var numResultSets = queries.Count(q => q);
            for (var i = 0; i < numResultSets; i++)
            {
                (await reader.ReadAsync(TestContext.Current.CancellationToken)).Should().BeTrue();
                reader[0].Should().Be(1);
                (await reader.NextResultAsync(TestContext.Current.CancellationToken)).Should().Be(i != numResultSets - 1);
            }
        }
    }

    [Theory]
    [InlineData(PrepareOrNot.NotPrepared)]
    [InlineData(PrepareOrNot.Prepared)]
    public async Task multiple_statements_with_parameters(PrepareOrNot prepare)
    {
        if (prepare == PrepareOrNot.Prepared && IsMultiplexing)
            return;

        //Arrange
        await using var conn = await OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT @p1; SELECT @p2";
        var p1 = new PgSqlParameter("p1", PgSqlDbType.Integer);
        var p2 = new PgSqlParameter("p2", PgSqlDbType.Text);
        cmd.Parameters.Add(p1);
        cmd.Parameters.Add(p2);
        if (prepare == PrepareOrNot.Prepared)
            cmd.Prepare();
        p1.Value = 8;
        p2.Value = "foo";

        //Act
        await using var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);

        //Assert
        (await reader.ReadAsync(TestContext.Current.CancellationToken)).Should().BeTrue();
        reader.GetInt32(0).Should().Be(8);
        (await reader.NextResultAsync(TestContext.Current.CancellationToken)).Should().BeTrue();
        (await reader.ReadAsync(TestContext.Current.CancellationToken)).Should().BeTrue();
        reader.GetString(0).Should().Be("foo");
        (await reader.NextResultAsync(TestContext.Current.CancellationToken)).Should().BeFalse();
    }

    [Theory]
    [InlineData(PrepareOrNot.NotPrepared)]
    [InlineData(PrepareOrNot.Prepared)]
    public async Task single_row_legacy_batching(PrepareOrNot prepare)
    {
        if (prepare == PrepareOrNot.Prepared && IsMultiplexing)
            return;

        //Arrange
        using var conn = await OpenConnectionAsync();
        using var cmd = new PgSqlCommand("SELECT 1; SELECT 2", conn);
        if (prepare == PrepareOrNot.Prepared)
            cmd.Prepare();

        //Act
        using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        reader.Read().Should().BeTrue();
        reader.GetInt32(0).Should().Be(1);
        reader.Read().Should().BeFalse();
        reader.NextResult().Should().BeFalse();
    }

    // Makes sure a later command can depend on an earlier one
    [Fact]
    [IssueLink("https://github.com/npgsql/npgsql/issues/641")]
    public async Task multiple_statements_with_dependencies()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "a INT");

        //Act
        await conn.ExecuteNonQueryAsync($"ALTER TABLE {table} ADD COLUMN b INT; INSERT INTO {table} (b) VALUES (8)", cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        (await conn.ExecuteScalarAsync($"SELECT b FROM {table}", cancellationToken: TestContext.Current.CancellationToken)).Should().Be(8);
    }

    // Forces async write mode when the first statement in a multi-statement command is big
    [Fact]
    [IssueLink("https://github.com/npgsql/npgsql/issues/641")]
    public async Task multiple_statements_large_first_command()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        using var cmd = new PgSqlCommand($"SELECT repeat('X', {conn.Settings.WriteBufferSize}); SELECT @p", conn);
        var expected1 = new string('X', conn.Settings.WriteBufferSize);
        var expected2 = new string('Y', conn.Settings.WriteBufferSize);
        cmd.Parameters.AddWithValue("p", expected2);

        //Act
        using var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);

        //Assert
        reader.Read();
        reader.GetString(0).Should().Be(expected1);
        reader.NextResult();
        reader.Read();
        reader.GetString(0).Should().Be(expected2);
    }

    #endregion

    #region Timeout

    // Checks that CommandTimeout gets enforced as a socket timeout
    [Fact]
    [IssueLink("https://github.com/npgsql/npgsql/issues/327")]
    public async Task timeout()
    {
        if (IsMultiplexing)
            return; // Multiplexing, Timeout

        //Arrange
        await using var dataSource = CreateDataSource(csb => csb.CommandTimeout = 1);
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using var cmd = CreateSleepCommand(conn, 10);

        //Act
        var exception = Assert.Throws<PgSqlException>(() => cmd.ExecuteNonQuery());

        //Assert
        exception.InnerException.Should().BeOfType<TimeoutException>();
        conn.FullState.Should().Be(ConnectionState.Open);
    }

    // Times out an async operation, testing that cancellation occurs successfully
    [Fact]
    [IssueLink("https://github.com/npgsql/npgsql/issues/607")]
    public async Task timeout_async_soft()
    {
        if (IsMultiplexing)
            return; // Multiplexing, Timeout

        //Arrange
        await using var dataSource = CreateDataSource(csb => csb.CommandTimeout = 1);
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using var cmd = CreateSleepCommand(conn, 10);

        //Act
        var exception = await Assert.ThrowsAsync<PgSqlException>(async () => await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));

        //Assert
        exception.InnerException.Should().BeOfType<TimeoutException>();
        conn.FullState.Should().Be(ConnectionState.Open);
    }

    // Times out an async operation, with unsuccessful cancellation (socket break)
    [Fact]
    [IssueLink("https://github.com/npgsql/npgsql/issues/607")]
    public async Task timeout_async_hard()
    {
        if (IsMultiplexing)
            return; // Multiplexing, Timeout

        //Arrange
        var builder = new PgSqlConnectionStringBuilder(ConnectionString) { CommandTimeout = 1 };
        await using var postmasterMock = PgPostmasterMock.Start(builder.ConnectionString);
        await using var dataSource = CreateDataSource(postmasterMock.ConnectionString);
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await postmasterMock.WaitForServerConnection();

        var processId = conn.ProcessID;

        //Act
        var exception = await Assert.ThrowsAsync<PgSqlException>(async () => await conn.ExecuteScalarAsync("SELECT 1", cancellationToken: TestContext.Current.CancellationToken));

        //Assert
        exception.InnerException.Should().BeOfType<TimeoutException>();
        conn.FullState.Should().Be(ConnectionState.Broken);
        (await postmasterMock.WaitForCancellationRequest()).ProcessId.Should().Be(processId);
    }

    [Fact]
    public async Task timeout_from_connection_string()
    {
        //Arrange
        PgSqlConnector.MinimumInternalCommandTimeout.Should().NotBe(PgSqlCommand.DefaultTimeout);
        var timeout = PgSqlConnector.MinimumInternalCommandTimeout;
        await using var dataSource = CreateDataSource(csb => csb.CommandTimeout = timeout);
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using var command = new PgSqlCommand("SELECT 1", conn);
        command.CommandTimeout.Should().Be(timeout);

        //Act
        command.CommandTimeout = 10;
        await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);

        //Assert
        command.CommandTimeout.Should().Be(10);
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/395")]
    public async Task timeout_switch_connection()
    {
        //Arrange
        var csb = new PgSqlConnectionStringBuilder(ConnectionString);
        if (csb.CommandTimeout is >= 100 and < 105)
            IgnoreExceptOnBuildServer("Bad default command timeout");

        await using var dataSource1 = CreateDataSource(ConnectionString + ";CommandTimeout=100");
        await using var c1 = dataSource1.CreateConnection();
        await using var cmd = c1.CreateCommand();
        cmd.CommandTimeout.Should().Be(100);
        await using var dataSource2 = CreateDataSource(ConnectionString + ";CommandTimeout=101");

        await using (var c2 = dataSource2.CreateConnection())
        {
            cmd.Connection = c2;
            cmd.CommandTimeout.Should().Be(101);
        }
        cmd.CommandTimeout = 102;
        await using (var c2 = dataSource2.CreateConnection())
        {
            cmd.Connection = c2;
            cmd.CommandTimeout.Should().Be(102);
        }
    }

    [Theory]
    [InlineData(SyncOrAsync.Sync)]
    [InlineData(SyncOrAsync.Async)]
    public async Task Prepare_timeout_hard(SyncOrAsync async)
    {
        if (IsMultiplexing)
            return; // Multiplexing, Timeout

        //Arrange
        var builder = new PgSqlConnectionStringBuilder(ConnectionString) { CommandTimeout = 1 };
        await using var postmasterMock = PgPostmasterMock.Start(builder.ConnectionString);
        await using var dataSource = CreateDataSource(postmasterMock.ConnectionString);
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await postmasterMock.WaitForServerConnection();

        var processId = conn.ProcessID;

        var cmd = new PgSqlCommand("SELECT 1", conn);

        //Act
        var exception = await Assert.ThrowsAsync<PgSqlException>(async () =>
        {
            if (async == SyncOrAsync.Sync)
                cmd.Prepare();
            else
                await cmd.PrepareAsync(TestContext.Current.CancellationToken);
        });

        //Assert
        exception.InnerException.Should().BeOfType<TimeoutException>();
        conn.FullState.Should().Be(ConnectionState.Broken);
        (await postmasterMock.WaitForCancellationRequest()).ProcessId.Should().Be(processId);
    }

    #endregion

    #region Cancel

    // Basic cancellation scenario
    [Fact]
    public async Task Cancel()
    {
        if (IsMultiplexing)
            return;

        //Arrange
        await using var conn = await OpenConnectionAsync();
        await using var cmd = CreateSleepCommand(conn, 5);

        var queryTask = Task.Run(() => cmd.ExecuteNonQuery());
        // We have to be sure the command's state is InProgress, otherwise the cancellation request will never be sent
        cmd.WaitUntilCommandIsInProgress();

        //Act
        cmd.Cancel();

        //Assert
        var exception = await Assert.ThrowsAsync<OperationCanceledException>(async () => await queryTask);
        exception.InnerException.Should().BeOfType<PostgresException>()
            .Which.SqlState.Should().Be(PostgresErrorCodes.QueryCanceled);
    }

    [Fact]
    public async Task Cancel_async_immediately()
    {
        if (IsMultiplexing)
            return; // Multiplexing, cancellation

        //Arrange
        await using var conn = await OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1";

        //Act
        var t = cmd.ExecuteScalarAsync(new(canceled: true));

        //Assert
        t.IsCompleted.Should().BeTrue(); // checks, if a query has completed synchronously
        t.Status.Should().Be(TaskStatus.Canceled);
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await t);

        conn.FullState.Should().Be(ConnectionState.Open);
        (await conn.ExecuteScalarAsync("SELECT 1", cancellationToken: TestContext.Current.CancellationToken)).Should().Be(1);
    }

    // Cancels an async query with the cancellation token, with successful PG cancellation
    [Fact]
    public async Task Cancel_async_soft()
    {
        if (IsMultiplexing)
            return; // Multiplexing, cancellation

        //Arrange
        await using var conn = await OpenConnectionAsync();
        await using var cmd = CreateSleepCommand(conn);
        using var cancellationSource = new CancellationTokenSource();
        var t = cmd.ExecuteNonQueryAsync(cancellationSource.Token);

        //Act
        cancellationSource.Cancel();

        //Assert
        var exception = await Assert.ThrowsAsync<OperationCanceledException>(async () => await t);
        exception.InnerException.Should().BeOfType<PostgresException>()
            .Which.SqlState.Should().Be(PostgresErrorCodes.QueryCanceled);
        exception.CancellationToken.Should().Be(cancellationSource.Token);

        conn.FullState.Should().Be(ConnectionState.Open);
        (await conn.ExecuteScalarAsync("SELECT 1", cancellationToken: TestContext.Current.CancellationToken)).Should().Be(1);
    }

    // Cancels an async query with the cancellation token and prepended query, with successful PG cancellation
    [Fact]
    [IssueLink("https://github.com/npgsql/npgsql/issues/5191")]
    public async Task Cancel_async_soft_with_prepended_query()
    {
        if (IsMultiplexing)
            return; // Multiplexing, cancellation

        //Arrange
        await using var postmasterMock = PgPostmasterMock.Start(ConnectionString);
        await using var dataSource = CreateDataSource(postmasterMock.ConnectionString);
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        var server = await postmasterMock.WaitForServerConnection();

        var processId = conn.ProcessID;

        await using var tx = await conn.BeginTransactionAsync(TestContext.Current.CancellationToken);
        await using var cmd = CreateSleepCommand(conn);
        using var cancellationSource = new CancellationTokenSource();
        var t = cmd.ExecuteNonQueryAsync(cancellationSource.Token);

        await server.ExpectSimpleQuery("BEGIN TRANSACTION ISOLATION LEVEL READ COMMITTED");

        //Act
        cancellationSource.Cancel();
        await server
            .WriteCommandComplete()
            .WriteReadyForQuery(TransactionStatus.InTransactionBlock)
            .FlushAsync();

        //Assert
         (await postmasterMock.WaitForCancellationRequest()).ProcessId.Should().Be(processId);

         await server
             .WriteErrorResponse(PostgresErrorCodes.QueryCanceled)
             .WriteReadyForQuery()
             .FlushAsync();

        var exception = await Assert.ThrowsAsync<OperationCanceledException>(async () => await t);
        exception.InnerException.Should().BeOfType<PostgresException>()
            .Which.SqlState.Should().Be(PostgresErrorCodes.QueryCanceled);
        exception.CancellationToken.Should().Be(cancellationSource.Token);

        conn.FullState.Should().Be(ConnectionState.Open);
    }

    // Cancels an async query with the cancellation token, with unsuccessful PG cancellation (socket break)
    [Fact]
    public async Task Cancel_async_hard()
    {
        if (IsMultiplexing)
            return; // Multiplexing, cancellation

        //Arrange
        await using var postmasterMock = PgPostmasterMock.Start(ConnectionString);
        await using var dataSource = CreateDataSource(postmasterMock.ConnectionString);
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await postmasterMock.WaitForServerConnection();

        var processId = conn.ProcessID;

        using var cancellationSource = new CancellationTokenSource();
        using var cmd = new PgSqlCommand("SELECT 1", conn);
        var t = cmd.ExecuteScalarAsync(cancellationSource.Token);

        //Act
        cancellationSource.Cancel();

        //Assert
        var exception = await Assert.ThrowsAsync<OperationCanceledException>(async () => await t);
        exception.InnerException.Should().BeOfType<TimeoutException>();
        exception.CancellationToken.Should().Be(cancellationSource.Token);

        conn.FullState.Should().Be(ConnectionState.Broken);
        (await postmasterMock.WaitForCancellationRequest()).ProcessId.Should().Be(processId);
    }

    [Theory(Skip = "https://github.com/npgsql/npgsql/issues/4668")]
    [IssueLink("https://github.com/npgsql/npgsql/issues/3466")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task bug_3466(bool isBroken)
    {
        if (IsMultiplexing)
            return; // Multiplexing, cancellation

        //Arrange
        var csb = new PgSqlConnectionStringBuilder(ConnectionString)
        {
            Pooling = false
        };
        await using var postmasterMock = PgPostmasterMock.Start(csb.ToString(), completeCancellationImmediately: false);
        await using var dataSource = CreateDataSource(postmasterMock.ConnectionString);
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        var serverMock = await postmasterMock.WaitForServerConnection();

        var processId = conn.ProcessID;

        using var cancellationSource = new CancellationTokenSource();
        await using var cmd = new PgSqlCommand("SELECT 1", conn)
        {
            CommandTimeout = 4
        };
        var t = Task.Run(() => cmd.ExecuteScalar());
        // We have to be sure the command's state is InProgress, otherwise the cancellation request will never be sent
        cmd.WaitUntilCommandIsInProgress();

        //Act
        // Perform cancellation, which will block on the server side
        var cancelTask = Task.Run(() => cmd.Cancel(), cancellationToken: TestContext.Current.CancellationToken);
        // Note what we have to wait for the cancellation request, otherwise the connection might be closed concurrently
        // and the cancellation request is never send
        var cancellationRequest = await postmasterMock.WaitForCancellationRequest();

        //Assert
        if (isBroken)
        {
            await Assert.ThrowsAsync<OperationCanceledException>(async () => await t);
            conn.FullState.Should().Be(ConnectionState.Broken);
        }
        else
        {
            await serverMock
                .WriteParseComplete()
                .WriteBindComplete()
                .WriteRowDescription(new FieldDescription(Int4Oid))
                .WriteDataRow(BitConverter.GetBytes(BinaryPrimitives.ReverseEndianness(1)))
                .WriteCommandComplete()
                .WriteReadyForQuery()
                .FlushAsync();
            await FluentActions.Awaiting(async () => await t).Should().NotThrowAsync();
            conn.FullState.Should().Be(ConnectionState.Open);
            await conn.CloseAsync();
        }

        // Release the cancellation at the server side, and make sure it completes without an exception
        cancellationRequest.Complete();
        await FluentActions.Awaiting(async () => await cancelTask).Should().NotThrowAsync();
    }

    // Check that cancel only affects the command on which its was invoked
    // Explicit: timing-sensitive
    [Fact(Explicit = true)]
    public async Task Cancel_cross_command()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        await using var cmd1 = CreateSleepCommand(conn, 2);
        await using var cmd2 = new PgSqlCommand("SELECT 1", conn);

        //Act
        var cancelTask = Task.Factory.StartNew(() =>
        {
            Thread.Sleep(300);
            cmd2.Cancel();
        }, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        await FluentActions.Awaiting(() => cmd1.ExecuteNonQueryAsync()).Should().NotThrowAsync();
        await cancelTask;
    }

    #endregion

    #region Cursors

    [Fact]
    public async Task cursor_statement()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "name TEXT");
        using var t = conn.BeginTransaction();

        for (var x = 0; x < 5; x++)
            await conn.ExecuteNonQueryAsync($"INSERT INTO {table} (name) VALUES ('X')", cancellationToken: TestContext.Current.CancellationToken);

        var i = 0;
        var command = new PgSqlCommand($"DECLARE TE CURSOR FOR SELECT * FROM {table}", conn);
        command.ExecuteNonQuery();
        command.CommandText = "FETCH FORWARD 3 IN TE";
        var dr = command.ExecuteReader();

        while (dr.Read())
            i++;
        i.Should().Be(3);
        dr.Close();

        i = 0;
        command.CommandText = "FETCH BACKWARD 1 IN TE";
        var dr2 = command.ExecuteReader();
        while (dr2.Read())
            i++;
        i.Should().Be(1);
        dr2.Close();

        command.CommandText = "close te;";
        command.ExecuteNonQuery();
    }

    [Fact]
    public async Task cursor_move_records_affected()
    {
        //Arrange
        using var connection = await OpenConnectionAsync();
        using var transaction = connection.BeginTransaction();
        var command = new PgSqlCommand("DECLARE curs CURSOR FOR SELECT * FROM (VALUES (1), (2), (3)) as t", connection);
        command.ExecuteNonQuery();
        command.CommandText = "MOVE FORWARD ALL IN curs";

        //Act
        var count = command.ExecuteNonQuery();

        //Assert
        count.Should().Be(3);
    }

    #endregion

    #region CommandBehavior.CloseConnection

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/693")]
    public async Task close_connection()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();

        //Act
        using (var cmd = new PgSqlCommand("SELECT 1", conn))
        using (var reader = await cmd.ExecuteReaderAsync(CommandBehavior.CloseConnection, cancellationToken: TestContext.Current.CancellationToken))
            while (reader.Read()) {}

        //Assert
        conn.State.Should().Be(ConnectionState.Closed);
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/1194")]
    public async Task close_connection_with_open_reader_with_close_connection()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        var cmd = new PgSqlCommand("SELECT 1", conn);
        var reader = await cmd.ExecuteReaderAsync(CommandBehavior.CloseConnection, cancellationToken: TestContext.Current.CancellationToken);
        var wasClosed = false;
        reader.ReaderClosed += (sender, args) => { wasClosed = true; };

        //Act
        conn.Close();

        //Assert
        wasClosed.Should().BeTrue();
    }

    [Fact]
    public async Task close_connection_with_exception()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();

        //Act
        using (var cmd = new PgSqlCommand("SE", conn))
            await Assert.ThrowsAsync<PostgresException>(() => cmd.ExecuteReaderAsync(CommandBehavior.CloseConnection, cancellationToken: TestContext.Current.CancellationToken));

        //Assert
        conn.State.Should().Be(ConnectionState.Closed);
    }

    #endregion

    [Theory]
    [InlineData(PrepareOrNot.NotPrepared)]
    [InlineData(PrepareOrNot.Prepared)]
    public async Task single_row(PrepareOrNot prepare)
    {
        if (prepare == PrepareOrNot.Prepared && IsMultiplexing)
            return;

        //Arrange
        await using var conn = await OpenConnectionAsync();
        await using var cmd = new PgSqlCommand("SELECT 1, 2 UNION SELECT 3, 4", conn);
        if (prepare == PrepareOrNot.Prepared)
            cmd.Prepare();

        //Act
        await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        Assert.Throws<InvalidOperationException>(() => reader.GetInt32(0));
        reader.Read().Should().BeTrue();
        reader.GetInt32(0).Should().Be(1);
        reader.Read().Should().BeFalse();
    }

    [Fact]
    public async Task CommandText_not_set()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();

        await using (var cmd = new PgSqlCommand())
        {
            cmd.Connection = conn;
            await Assert.ThrowsAsync<InvalidOperationException>(() => cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
            cmd.CommandText = null;
            await Assert.ThrowsAsync<InvalidOperationException>(() => cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
            cmd.CommandText = "";
        }

        await using (var cmd = conn.CreateCommand())
            await Assert.ThrowsAsync<InvalidOperationException>(() => cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ExecuteScalar()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "name TEXT");
        await using var command = new PgSqlCommand($"SELECT name FROM {table}", conn);

        (await command.ExecuteScalarAsync(TestContext.Current.CancellationToken)).Should().BeNull();

        await conn.ExecuteNonQueryAsync($"INSERT INTO {table} (name) VALUES (NULL)", cancellationToken: TestContext.Current.CancellationToken);
        (await command.ExecuteScalarAsync(TestContext.Current.CancellationToken)).Should().Be(DBNull.Value);

        await conn.ExecuteNonQueryAsync($"TRUNCATE {table}", cancellationToken: TestContext.Current.CancellationToken);
        for (var i = 0; i < 2; i++)
            await conn.ExecuteNonQueryAsync($"INSERT INTO {table} (name) VALUES ('X')", cancellationToken: TestContext.Current.CancellationToken);
        (await command.ExecuteScalarAsync(TestContext.Current.CancellationToken)).Should().Be("X");
    }

    [Fact]
    public async Task ExecuteNonQuery()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        await using var cmd = new PgSqlCommand { Connection = conn };
        var table = await CreateTempTable(conn, "name TEXT");

        cmd.CommandText = $"INSERT INTO {table} (name) VALUES ('John')";
        (await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken)).Should().Be(1);

        cmd.CommandText = $"INSERT INTO {table} (name) VALUES ('John'); INSERT INTO {table} (name) VALUES ('John')";
        (await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken)).Should().Be(2);

        cmd.CommandText = $"INSERT INTO {table} (name) VALUES ('{new string('x', conn.Settings.WriteBufferSize)}')";
        (await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken)).Should().Be(1);
    }

    // Makes sure a command is unusable after it is disposed
    [Fact]
    public async Task Dispose()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        var cmd = new PgSqlCommand("SELECT 1", conn);

        //Act
        cmd.Dispose();

        //Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(() => cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => cmd.PrepareAsync(TestContext.Current.CancellationToken));
    }

    // Disposing a command with an open reader does not close the reader. This is the SqlClient behavior.
    [Fact]
    public async Task command_dispose_does_not_close_reader()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        var cmd = new PgSqlCommand("SELECT 1, 2", conn);
        await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);

        //Act
        cmd.Dispose();

        //Assert
        cmd = new PgSqlCommand("SELECT 3", conn);
        await Assert.ThrowsAsync<PgSqlOperationInProgressException>(() => cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task non_standards_conforming_strings()
    {
        //Arrange
        await using var dataSource = CreateDataSource();
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        if (IsMultiplexing)
        {
            await Assert.ThrowsAsync<NotSupportedException>(async () => await conn.ExecuteNonQueryAsync("set standard_conforming_strings=off", cancellationToken: TestContext.Current.CancellationToken));
        }
        else
        {
            await conn.ExecuteNonQueryAsync("set standard_conforming_strings=off", cancellationToken: TestContext.Current.CancellationToken);
            (await conn.ExecuteScalarAsync("SELECT 1", cancellationToken: TestContext.Current.CancellationToken)).Should().Be(1);
            await conn.ExecuteNonQueryAsync("set standard_conforming_strings=on", cancellationToken: TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task parameter_and_operator_unclear()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        //Without parenthesis the meaning of [, . and potentially other characters is
        //a syntax error. See comment in PgSqlCommand.GetClearCommandText() on "usually-redundant parenthesis".
        await using var command = new PgSqlCommand("select :arr[2]", conn);
        command.Parameters.AddWithValue(":arr", new int[] {5, 4, 3, 2, 1});

        //Act
        await using var rdr = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        rdr.Read();

        //Assert
        rdr.GetInt32(0).Should().Be(4);
    }

    [Fact]
    [IssueLink("https://github.com/npgsql/npgsql/issues/4171")]
    public async Task cached_command_clears_parameters_placeholder_type()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();

        await using (var cmd1 = conn.CreateCommand())
        {
            cmd1.CommandText = "SELECT @p1";
            cmd1.Parameters.AddWithValue("@p1", 8);
            await using var reader1 = await cmd1.ExecuteReaderAsync(TestContext.Current.CancellationToken);
            reader1.Read();
            reader1[0].Should().Be(8);
        }

        await using (var cmd2 = conn.CreateCommand())
        {
            cmd2.CommandText = "SELECT $1";
            cmd2.Parameters.AddWithValue(8);
            await using var reader2 = await cmd2.ExecuteReaderAsync(TestContext.Current.CancellationToken);
            reader2.Read();
            reader2[0].Should().Be(8);
        }
    }

    [Theory]
    [InlineData(CommandBehavior.Default)]
    [InlineData(CommandBehavior.SequentialAccess)]
    public async Task statement_mapped_output_parameters(CommandBehavior behavior)
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        var command = new PgSqlCommand("select 3 as unknown, 4 as param1, 5 as param2, 6;", conn);

        var p = new PgSqlParameter("param1", PgSqlDbType.Integer);
        p.Direction = ParameterDirection.Output;
        p.Value = -1;
        command.Parameters.Add(p);

        p = new PgSqlParameter("param2", PgSqlDbType.Integer);
        p.Direction = ParameterDirection.Output;
        p.Value = -1;
        command.Parameters.Add(p);

        p = new PgSqlParameter("p", PgSqlDbType.Integer);
        p.Direction = ParameterDirection.Output;
        p.Value = -1;
        command.Parameters.Add(p);

        //Act
        await using var reader = await command.ExecuteReaderAsync(behavior, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        command.Parameters["p"].Value.Should().Be(3);
        command.Parameters["param1"].Value.Should().Be(4);
        command.Parameters["param2"].Value.Should().Be(5);

        reader.Read();

        reader.GetInt32(0).Should().Be(3);
        reader.GetInt32(1).Should().Be(4);
        reader.GetInt32(2).Should().Be(5);
        reader.GetInt32(3).Should().Be(6);
    }

    [Theory]
    [InlineData(CommandBehavior.Default)]
    [InlineData(CommandBehavior.SequentialAccess)]
    public async Task statement_mapped_generic_output_parameters(CommandBehavior behavior)
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        var command = new PgSqlCommand("select '' as unknown, 4 as param1, 5 as param2, 6;", conn);

        var p = new PgSqlParameter<int>("param1", PgSqlDbType.Integer);
        p.Direction = ParameterDirection.Output;
        p.Value = -1;
        command.Parameters.Add(p);

        p = new PgSqlParameter<int>("param2", PgSqlDbType.Integer);
        p.Direction = ParameterDirection.Output;
        p.Value = -1;
        command.Parameters.Add(p);

        // char[] is one alternative mapping for text.
        var textP = new PgSqlParameter<char[]>("p", PgSqlDbType.Text);
        textP.Direction = ParameterDirection.Output;
        textP.Value = "text".ToCharArray();
        command.Parameters.Add(textP);

        //Act
        await using var reader = await command.ExecuteReaderAsync(behavior, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        ((char[])command.Parameters["p"].Value).Should().BeEquivalentTo(Array.Empty<char>());
        command.Parameters["param1"].Value.Should().Be(4);
        command.Parameters["param2"].Value.Should().Be(5);

        reader.Read();

        reader.GetFieldValue<char[]>(0).Should().BeEquivalentTo(Array.Empty<char>());
        reader.GetInt32(1).Should().Be(4);
        reader.GetInt32(2).Should().Be(5);
        reader.GetInt32(3).Should().Be(6);
    }

    [Fact]
    public async Task bug_1006158_output_parameters()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        MinimumPgVersion(conn, "14.0", "Stored procedure OUT parameters are only support starting with version 14");
        var sproc = await GetTempProcedureName(conn);

        var createFunction = $@"
CREATE PROCEDURE {sproc}(OUT a integer, OUT b boolean) AS $$
BEGIN
    a := 3;
    b := true;
END
$$ LANGUAGE plpgsql;";

        var command = new PgSqlCommand(createFunction, conn);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);

        command = new PgSqlCommand(sproc, conn);
        command.CommandType = CommandType.StoredProcedure;

        command.Parameters.Add(new PgSqlParameter("a", DbType.Int32));
        command.Parameters[0].Direction = ParameterDirection.Output;
        command.Parameters.Add(new PgSqlParameter("b", DbType.Boolean));
        command.Parameters[1].Direction = ParameterDirection.Output;

        //Act
        _ = await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);

        //Assert
        command.Parameters[0].Value.Should().Be(3);
        command.Parameters[1].Value.Should().Be(true);
    }

    [Fact]
    public async Task bug_1010788_update_row_source()
    {
        if (IsMultiplexing)
            return;

        //Arrange
        using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "id SERIAL PRIMARY KEY, name TEXT");

        var command = new PgSqlCommand($"SELECT * FROM {table}", conn);
        command.UpdatedRowSource.Should().Be(UpdateRowSource.Both);

        //Act
        var cmdBuilder = new PgSqlCommandBuilder();
        var da = new PgSqlDataAdapter(command);
        cmdBuilder.DataAdapter = da;

        //Assert
        da.SelectCommand.Should().NotBeNull();
        cmdBuilder.DataAdapter.Should().NotBeNull();

        var updateCommand = cmdBuilder.GetUpdateCommand();
        updateCommand.UpdatedRowSource.Should().Be(UpdateRowSource.None);
    }

    [Fact]
    public async Task table_direct()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "name TEXT");

        await conn.ExecuteNonQueryAsync($"INSERT INTO {table} (name) VALUES ('foo')", cancellationToken: TestContext.Current.CancellationToken);
        using var cmd = new PgSqlCommand(table, conn) { CommandType = CommandType.TableDirect };

        //Act
        using var rdr = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);

        //Assert
        rdr.Read().Should().BeTrue();
        rdr["name"].Should().Be("foo");
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/503")]
    public async Task invalid_utf8()
    {
        //Arrange
        const string badString = "SELECT 'abc\uD801\uD802d'";
        await using var dataSource = CreateDataSource();
        using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        //Act
        var act = () => conn.ExecuteScalarAsync(badString);

        //Assert
        await act.Should().ThrowExactlyAsync<EncoderFallbackException>();
    }

    [Theory, IssueLink("https://github.com/npgsql/npgsql/issues/395")]
    [InlineData(PrepareOrNot.Prepared)]
    [InlineData(PrepareOrNot.NotPrepared)]
    public async Task use_across_connection_change(PrepareOrNot prepare)
    {
        if (prepare == PrepareOrNot.Prepared && IsMultiplexing)
            return;

        //Arrange
        using var conn1 = await OpenConnectionAsync();
        using var conn2 = await OpenConnectionAsync();
        using var cmd = new PgSqlCommand("SELECT 1", conn1);
        if (prepare == PrepareOrNot.Prepared)
            cmd.Prepare();

        //Act
        cmd.Connection = conn2;

        //Assert
        cmd.IsPrepared.Should().BeFalse();
        if (prepare == PrepareOrNot.Prepared)
            cmd.Prepare();
        (await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken)).Should().Be(1);
    }

    // The asserts we're testing are debug only.
    [Fact]
    public async Task use_after_reload_types_invalidates_cached_infos()
    {
        if (IsMultiplexing)
            return;

        //Arrange
        using var conn1 = await OpenConnectionAsync();
        using var cmd = new PgSqlCommand("SELECT 1", conn1);
        cmd.Prepare();
        using (var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken))
        {
            await reader.ReadAsync(TestContext.Current.CancellationToken);
            FluentActions.Invoking(() => reader.GetInt32(0)).Should().NotThrow();
        }

        //Act
        await conn1.ReloadTypesAsync(TestContext.Current.CancellationToken);

        //Assert
        using (var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken))
        {
            await reader.ReadAsync(TestContext.Current.CancellationToken);
            FluentActions.Invoking(() => reader.GetInt32(0)).Should().NotThrow();
        }
    }

    [Fact]
    public async Task parameter_overflow_message_length_throws()
    {
        //Arrange
        // Create a separate dataSource because of Multiplexing (otherwise we can break unrelated queries)
        await using var dataSource = CreateDataSource();
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using var cmd = new PgSqlCommand("SELECT @a, @b, @c, @d, @e, @f, @g, @h", conn);

        var largeParam = new string('A', 1 << 29);
        cmd.Parameters.AddWithValue("a", largeParam);
        cmd.Parameters.AddWithValue("b", largeParam);
        cmd.Parameters.AddWithValue("c", largeParam);
        cmd.Parameters.AddWithValue("d", largeParam);
        cmd.Parameters.AddWithValue("e", largeParam);
        cmd.Parameters.AddWithValue("f", largeParam);
        cmd.Parameters.AddWithValue("g", largeParam);
        cmd.Parameters.AddWithValue("h", largeParam);

        //Act
        var act = () => cmd.ExecuteReaderAsync();

        //Assert
        await act.Should().ThrowExactlyAsync<OverflowException>();
    }

    [Fact]
    public async Task composite_overflow_message_length_throws()
    {
        //Arrange
        await using var adminConnection = await OpenConnectionAsync();
        var type = await GetTempTypeName(adminConnection);

        await adminConnection.ExecuteNonQueryAsync(
            $"CREATE TYPE {type} AS (a text, b text, c text, d text, e text, f text, g text, h text)", cancellationToken: TestContext.Current.CancellationToken);

        var dataSourceBuilder = CreateDataSourceBuilder();
        dataSourceBuilder.MapComposite<BigComposite>(type);
        await using var dataSource = dataSourceBuilder.Build();
        await using var connection = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        var largeString = new string('A', 1 << 29);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT @a";
        cmd.Parameters.AddWithValue("a", new BigComposite
        {
            A = largeString,
            B = largeString,
            C = largeString,
            D = largeString,
            E = largeString,
            F = largeString,
            G = largeString,
            H = largeString
        });

        //Act
        var act = async () => await cmd.ExecuteNonQueryAsync();

        //Assert
        await act.Should().ThrowExactlyAsync<OverflowException>();
    }

    record BigComposite
    {
        public string A { get; set; } = null;
        public string B { get; set; } = null;
        public string C { get; set; } = null;
        public string D { get; set; } = null;
        public string E { get; set; } = null;
        public string F { get; set; } = null;
        public string G { get; set; } = null;
        public string H { get; set; } = null;
    }

    [Fact]
    public async Task array_overflow_message_length_throws()
    {
        //Arrange
        // Create a separate dataSource because of Multiplexing (otherwise we can break unrelated queries)
        await using var dataSource = CreateDataSource();
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        var largeString = new string('A', 1 << 29);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT @a";
        var array = new[]
        {
            largeString,
            largeString,
            largeString,
            largeString,
            largeString,
            largeString,
            largeString,
            largeString
        };
        cmd.Parameters.AddWithValue("a", array);

        //Act
        var act = async () => await cmd.ExecuteNonQueryAsync();

        //Assert
        await act.Should().ThrowExactlyAsync<OverflowException>();
    }

    [Fact]
    public async Task range_overflow_message_length_throws()
    {
        //Arrange
        await using var adminConnection = await OpenConnectionAsync();
        var type = await GetTempTypeName(adminConnection);
        var rangeType = await GetTempTypeName(adminConnection);

        await adminConnection.ExecuteNonQueryAsync(
            $"CREATE TYPE {type} AS (a text, b text, c text, d text, e text, f text, g text, h text);CREATE TYPE {rangeType} AS RANGE(subtype={type})", cancellationToken: TestContext.Current.CancellationToken);

        var dataSourceBuilder = CreateDataSourceBuilder();
        dataSourceBuilder.MapComposite<BigComposite>(type);
        dataSourceBuilder.EnableUnmappedTypes();
        await using var dataSource = dataSourceBuilder.Build();
        await using var connection = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        var largeString = new string('A', (1 << 28) + 2000000);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT @a";
        var composite = new BigComposite
        {
            A = largeString,
            B = largeString,
            C = largeString,
            D = largeString
        };
        var range = new PgSqlRange<BigComposite>(composite, composite);
        cmd.Parameters.Add(new PgSqlParameter
        {
            Value = range,
            ParameterName = "a",
            DataTypeName = rangeType
        });

        //Act
        var act = async () => await cmd.ExecuteNonQueryAsync();

        //Assert
        await act.Should().ThrowExactlyAsync<OverflowException>();
    }

    [Fact]
    public async Task multirange_overflow_message_length_throws()
    {
        //Arrange
        await using var adminConnection = await OpenConnectionAsync();
        MinimumPgVersion(adminConnection, "14.0", "Multirange types were introduced in PostgreSQL 14");
        var type = await GetTempTypeName(adminConnection);
        var rangeType = await GetTempTypeName(adminConnection);

        await adminConnection.ExecuteNonQueryAsync(
            $"CREATE TYPE {type} AS (a text, b text, c text, d text, e text, f text, g text, h text);CREATE TYPE {rangeType} AS RANGE(subtype={type})", cancellationToken: TestContext.Current.CancellationToken);

        var dataSourceBuilder = CreateDataSourceBuilder();
        dataSourceBuilder.MapComposite<BigComposite>(type);
        dataSourceBuilder.EnableUnmappedTypes();
        await using var dataSource = dataSourceBuilder.Build();
        await using var connection = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        var largeString = new string('A', (1 << 28) + 2000000);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT @a";
        var composite = new BigComposite
        {
            A = largeString
        };
        var range = new PgSqlRange<BigComposite>(composite, composite);
        var multirange = new[]
        {
            range,
            range,
            range,
            range
        };
        cmd.Parameters.Add(new PgSqlParameter
        {
            Value = multirange,
            ParameterName = "a",
            DataTypeName = rangeType + "_multirange"
        });

        //Act
        var act = async () => await cmd.ExecuteNonQueryAsync();

        //Assert
        await act.Should().ThrowExactlyAsync<OverflowException>();
    }

    // CreateCommand before connection open
    [Fact]
    [IssueLink("https://github.com/npgsql/npgsql/issues/565")]
    public async Task create_command_before_connection_open()
    {
        //Arrange
        using var conn = new PgSqlConnection(ConnectionString);
        var cmd = new PgSqlCommand("SELECT 1", conn);

        //Act
        conn.Open();

        //Assert
        (await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken)).Should().Be(1);
    }

    [Fact]
    public async Task Connection_not_set_throws()
    {
        //Arrange
        var cmd = new PgSqlCommand("SELECT 1");

        //Act
        var act = () => cmd.ExecuteScalarAsync();

        //Assert
        await act.Should().ThrowExactlyAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Connection_not_open_throws()
    {
        //Arrange
        using var conn = CreateConnection();
        var cmd = new PgSqlCommand("SELECT 1", conn);

        //Act
        var act = () => cmd.ExecuteScalarAsync();

        //Assert
        await act.Should().ThrowExactlyAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExecuteNonQuery_Throws_PostgresException(bool async)
    {
        if (!async && IsMultiplexing)
            return;

        //Arrange
        await using var conn = await OpenConnectionAsync();

        var table1 = await CreateTempTable(conn, "id integer PRIMARY key, t varchar(40)");
        var table2 = await CreateTempTable(conn, $"id SERIAL primary key, {table1}_id integer references {table1}(id) INITIALLY DEFERRED");

        var sql = $"insert into {table2} ({table1}_id) values (1) returning id";

        //Act
        var ex = async
            ? await Assert.ThrowsAsync<PostgresException>(async () => await conn.ExecuteNonQueryAsync(sql, cancellationToken: TestContext.Current.CancellationToken))
            : Assert.Throws<PostgresException>(() => conn.ExecuteNonQuery(sql));

        //Assert
        ex.SqlState.Should().Be(PostgresErrorCodes.ForeignKeyViolation);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExecuteScalar_Throws_PostgresException(bool async)
    {
        if (!async && IsMultiplexing)
            return;

        //Arrange
        await using var conn = await OpenConnectionAsync();

        var table1 = await CreateTempTable(conn, "id integer PRIMARY key, t varchar(40)");
        var table2 = await CreateTempTable(conn, $"id SERIAL primary key, {table1}_id integer references {table1}(id) INITIALLY DEFERRED");

        var sql = $"insert into {table2} ({table1}_id) values (1) returning id";

        //Act
        var ex = async
            ? await Assert.ThrowsAsync<PostgresException>(async () => await conn.ExecuteScalarAsync(sql, cancellationToken: TestContext.Current.CancellationToken))
            : Assert.Throws<PostgresException>(() => conn.ExecuteScalar(sql));

        //Assert
        ex.SqlState.Should().Be(PostgresErrorCodes.ForeignKeyViolation);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExecuteReader_Throws_PostgresException(bool async)
    {
        if (!async && IsMultiplexing)
            return;

        //Arrange
        await using var conn = await OpenConnectionAsync();

        var table1 = await CreateTempTable(conn, "id integer PRIMARY key, t varchar(40)");
        var table2 = await CreateTempTable(conn, $"id SERIAL primary key, {table1}_id integer references {table1}(id) INITIALLY DEFERRED");

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"insert into {table2} ({table1}_id) values (1) returning id";

        //Act
        await using var reader = async
            ? await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken)
            : cmd.ExecuteReader();

        //Assert
        (async ? await reader.ReadAsync(TestContext.Current.CancellationToken) : reader.Read()).Should().BeTrue();
        var value = reader.GetInt32(0);
        value.Should().Be(1);
        (async ? await reader.ReadAsync(TestContext.Current.CancellationToken) : reader.Read()).Should().BeFalse();
        var ex = async
            ? await Assert.ThrowsAsync<PostgresException>(async () => await reader.NextResultAsync(TestContext.Current.CancellationToken))
            : Assert.Throws<PostgresException>(() => reader.NextResult());
        ex.SqlState.Should().Be(PostgresErrorCodes.ForeignKeyViolation);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void command_is_recycled(bool allResultTypesAreUnknown)
    {
        //Arrange
        using var conn = OpenConnection();
        var cmd1 = conn.CreateCommand();
        cmd1.CommandText = "SELECT @p1";
        if (allResultTypesAreUnknown)
            cmd1.AllResultTypesAreUnknown = true;
        else
            cmd1.UnknownResultTypeList = [true];
        var tx = conn.BeginTransaction();
        cmd1.Transaction = tx;
        cmd1.Parameters.AddWithValue("p1", 8);
        _ = cmd1.ExecuteScalar();

        //Act
        cmd1.Dispose();
        var cmd2 = conn.CreateCommand();

        //Assert
        cmd2.Should().BeSameAs(cmd1);
        cmd2.CommandText.Should().BeEmpty();
        cmd2.CommandType.Should().Be(CommandType.Text);
        cmd2.Transaction.Should().BeNull();
        cmd2.Parameters.Should().BeEmpty();
        cmd2.AllResultTypesAreUnknown.Should().BeFalse();
        cmd2.UnknownResultTypeList.Should().BeNull();
        // TODO: Leaving this for now, since it'll be replaced by the new batching API
        // cmd2.Statements.Should().BeEmpty();
    }

    [Fact]
    public void command_recycled_resets_command_type()
    {
        //Arrange
        using var conn = CreateConnection();
        var cmd1 = conn.CreateCommand();
        cmd1.CommandType = CommandType.StoredProcedure;

        //Act
        cmd1.Dispose();
        var cmd2 = conn.CreateCommand();

        //Assert
        cmd2.CommandType.Should().Be(CommandType.Text);
    }

    [Theory]
    [IssueLink("https://github.com/npgsql/npgsql/issues/831")]
    [IssueLink("https://github.com/npgsql/npgsql/issues/2795")]
    [InlineData(PrepareOrNot.NotPrepared)]
    [InlineData(PrepareOrNot.Prepared)]
    public async Task many_parameters(PrepareOrNot prepare)
    {
        if (prepare == PrepareOrNot.Prepared && IsMultiplexing)
            return;

        //Arrange
        using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "some_column INT");
        using var cmd = new PgSqlCommand { Connection = conn };
        var sb = new StringBuilder($"INSERT INTO {table} (some_column) VALUES ");
        for (var i = 0; i < ushort.MaxValue; i++)
        {
            var paramName = "p" + i;
            cmd.Parameters.Add(new PgSqlParameter(paramName, 8));
            if (i > 0)
                sb.Append(", ");
            sb.Append($"(@{paramName})");
        }

        cmd.CommandText = sb.ToString();

        if (prepare == PrepareOrNot.Prepared)
            cmd.Prepare();

        //Act
        await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    // Bypasses PostgreSQL's uint16 limitation on the number of parameters
    [Theory]
    [IssueLink("https://github.com/npgsql/npgsql/issues/831")]
    [IssueLink("https://github.com/npgsql/npgsql/issues/858")]
    [IssueLink("https://github.com/npgsql/npgsql/issues/2703")]
    [InlineData(PrepareOrNot.NotPrepared)]
    [InlineData(PrepareOrNot.Prepared)]
    public async Task too_many_parameters_throws(PrepareOrNot prepare)
    {
        if (prepare == PrepareOrNot.Prepared && IsMultiplexing)
            return;

        //Arrange
        using var conn = await OpenConnectionAsync();
        using var cmd = new PgSqlCommand { Connection = conn };
        var sb = new StringBuilder("SOME RANDOM SQL ");
        for (var i = 0; i < ushort.MaxValue + 1; i++)
        {
            var paramName = "p" + i;
            cmd.Parameters.Add(new PgSqlParameter(paramName, 8));
            if (i > 0)
                sb.Append(", ");
            sb.Append('@');
            sb.Append(paramName);
        }

        cmd.CommandText = sb.ToString();

        if (prepare == PrepareOrNot.Prepared)
        {
            Assert.ThrowsAny<PgSqlException>(() => cmd.Prepare())
                .Message.Should().Be("A statement cannot have more than 65535 parameters");
        }
        else
        {
            (await Assert.ThrowsAnyAsync<PgSqlException>(() => cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken)))
                .Message.Should().Be("A statement cannot have more than 65535 parameters");
        }
    }

    // An individual statement cannot have more than 65535 parameters, but a command can (across multiple statements).
    [Fact]
    [IssueLink("https://github.com/npgsql/npgsql/issues/1199")]
    public async Task many_parameters_across_statements()
    {
        //Arrange
        // Create a command with 1000 statements which have 70 params each
        using var conn = await OpenConnectionAsync();
        using var cmd = new PgSqlCommand { Connection = conn };
        var paramIndex = 0;
        var sb = new StringBuilder();
        for (var statementIndex = 0; statementIndex < 1000; statementIndex++)
        {
            if (statementIndex > 0)
                sb.Append("; ");
            sb.Append("SELECT ");
            var startIndex = paramIndex;
            var endIndex = paramIndex + 70;
            for (; paramIndex < endIndex; paramIndex++)
            {
                var paramName = "p" + paramIndex;
                cmd.Parameters.Add(new PgSqlParameter(paramName, 8));
                if (paramIndex > startIndex)
                    sb.Append(", ");
                sb.Append('@');
                sb.Append(paramName);
            }
        }

        cmd.CommandText = sb.ToString();

        //Act
        await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    // Makes sure that PgSql doesn't attempt to send all data before the user can start reading. That would cause a deadlock.
    [Fact]
    public async Task batched_big_statements_do_not_deadlock()
    {
        //Arrange
        // We're going to send a large multistatement query that would exhaust both the client's and server's
        // send and receive buffers (assume 64k per buffer).
        var data = new string('x', 1024);
        using var conn = await OpenConnectionAsync();
        var sb = new StringBuilder();
        for (var i = 0; i < 500; i++)
            sb.Append("SELECT @p;");
        using var cmd = new PgSqlCommand(sb.ToString(), conn);
        cmd.Parameters.AddWithValue("p", PgSqlDbType.Text, data);

        //Act
        using var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);

        //Assert
        for (var i = 0; i < 500; i++)
        {
            reader.Read();
            reader.GetString(0).Should().Be(data);
            reader.NextResult();
        }
    }

    [Fact]
    public void batched_small_then_big_statements_do_not_deadlock_in_sync_io()
    {
        if (IsMultiplexing)
            return; // Multiplexing, sync I/O

        //Arrange
        // This makes sure we switch to async writing for batches, starting from the 2nd statement at the latest.
        // Otherwise, a small first first statement followed by a huge big one could cause us to deadlock, as we're stuck
        // synchronously sending the 2nd statement while PG is stuck sending the results of the 1st.
        using var conn = OpenConnection();
        var data = new string('x', 5_000_000);
        using var cmd = new PgSqlCommand("SELECT generate_series(1, 500000); SELECT @p", conn);
        cmd.Parameters.AddWithValue("p", PgSqlDbType.Text, data);

        //Act
        cmd.ExecuteNonQuery();
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/1429")]
    public async Task same_command_different_param_values()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        using var cmd = new PgSqlCommand("SELECT @p", conn);
        cmd.Parameters.AddWithValue("p", 8);
        await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);

        //Act
        cmd.Parameters[0].Value = 9;

        //Assert
        (await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken)).Should().Be(9);
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/1429")]
    public async Task same_command_different_param_instances()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        using var cmd = new PgSqlCommand("SELECT @p", conn);
        cmd.Parameters.AddWithValue("p", 8);
        await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);

        //Act
        cmd.Parameters.RemoveAt(0);
        cmd.Parameters.AddWithValue("p", 9);

        //Assert
        (await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken)).Should().Be(9);
    }

    [Fact(Skip = "Flaky"), IssueLink("https://github.com/npgsql/npgsql/issues/3509")]
    public async Task bug_3509()
    {
        if (IsMultiplexing)
            return;

        //Arrange
        var csb = new PgSqlConnectionStringBuilder(ConnectionString)
        {
            KeepAlive = 1,
        };
        await using var postmasterMock = PgPostmasterMock.Start(csb.ToString());
        await using var dataSource = CreateDataSource(postmasterMock.ConnectionString);
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        var serverMock = await postmasterMock.WaitForServerConnection();
        // Wait for a keepalive to arrive at the server, reply with an error
        await serverMock.WaitForData();

        //Act
        var queryTask = Task.Run(async () => await conn.ExecuteNonQueryAsync("SELECT 1"));
        // TODO: kind of flaky - think of the way to rewrite
        // giving a queryTask some time to get stuck on a lock
        await Task.Delay(300, cancellationToken: TestContext.Current.CancellationToken);
        await serverMock
            .WriteErrorResponse("42")
            .WriteReadyForQuery()
            .FlushAsync();

        await serverMock
            .WriteScalarResponseAndFlush(1);

        //Assert
        var ex = await Assert.ThrowsAsync<PgSqlException>(async () => await queryTask);
        ex.InnerException.Should().BeOfType<PgSqlException>()
            .Which.InnerException.Should().BeOfType<PostgresException>();
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/4134")]
    public async Task cached_command_double_dispose()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();

        var cmd1 = conn.CreateCommand();

        //Act
        cmd1.Dispose();
        cmd1.Dispose();

        //Assert
        var cmd2 = conn.CreateCommand();
        cmd2.Should().BeSameAs(cmd1);

        cmd2.CommandText = "SELECT 1";
        (await cmd2.ExecuteScalarAsync(TestContext.Current.CancellationToken)).Should().Be(1);
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/4330")]
    public async Task Prepare_with_positional_placeholders_after_named()
    {
        if (IsMultiplexing)
            return; // Explicit preparation

        //Arrange
        await using var conn = await OpenConnectionAsync();

        await using var command = new PgSqlCommand("SELECT @p", conn);
        command.Parameters.AddWithValue("p", 10);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);

        command.Parameters.Clear();

        //Act
        command.CommandText = "SELECT $1";
        command.Parameters.Add(new() { PgSqlDbType = PgSqlDbType.Integer });

        //Assert
        await FluentActions.Awaiting(() => command.PrepareAsync()).Should().NotThrowAsync();
    }

    // Most of 08* errors are coming whenever there was an error while connecting to a remote server from a cluster, so the connection to the cluster is still OK
    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/4621")]
    public async Task postgres_connection_errors_not_break_connection()
    {
        if (IsMultiplexing)
            return;

        //Arrange
        await using var postmasterMock = PgPostmasterMock.Start(ConnectionString);
        await using var dataSource = CreateDataSource(postmasterMock.ConnectionString);
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1";

        //Act
        var queryTask = cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);

        var server = await postmasterMock.WaitForServerConnection();
        await server
            .WriteErrorResponse(PostgresErrorCodes.SqlClientUnableToEstablishSqlConnection)
            .WriteReadyForQuery()
            .FlushAsync();

        //Assert
        var ex = await Assert.ThrowsAsync<PostgresException>(async () => await queryTask);
        ex.SqlState.Should().Be(PostgresErrorCodes.SqlClientUnableToEstablishSqlConnection);
        conn.FullState.Should().Be(ConnectionState.Open);
    }

    // Concurrent write and read failure can lead to deadlocks while cleaning up the connector.
    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/4804")]
    public async Task concurrent_read_write_failure_deadlock()
    {
        if (IsMultiplexing)
            return;

        //Arrange
        await using var postmasterMock = PgPostmasterMock.Start(ConnectionString);
        await using var dataSource = CreateDataSource(postmasterMock.ConnectionString);
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        await using var cmd = conn.CreateCommand();
        // Attempt to send a big enough query to fill buffers
        // That way the write side should be stuck, waiting for the server to empty buffers
        cmd.CommandText = new string('a', 8_000_000);
        var queryTask = cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);

        //Act
        var server = await postmasterMock.WaitForServerConnection();
        server.Close();

        //Assert
        await Assert.ThrowsAsync<PgSqlException>(async () => await queryTask);
    }

    // Make sure we don't cancel a prepended query (and do not deadlock in case of a failure)
    // Explicit: flaky due to #5033
    [Theory(Explicit = true), IssueLink("https://github.com/npgsql/npgsql/issues/4906")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task not_cancel_prepended_query(bool failPrependedQuery)
    {
        if (IsMultiplexing)
            return;

        //Arrange
        await using var postmasterMock = PgPostmasterMock.Start(ConnectionString);
        var csb = new PgSqlConnectionStringBuilder(postmasterMock.ConnectionString)
        {
            NoResetOnClose = false
        };
        await using var dataSource = CreateDataSource(csb.ConnectionString);
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        // reopen connection to append prepended query
        await conn.CloseAsync();
        await conn.OpenAsync(TestContext.Current.CancellationToken);

        using var cts = new CancellationTokenSource();
        var queryTask = conn.ExecuteNonQueryAsync("SELECT 1", cancellationToken: cts.Token);

        var server = await postmasterMock.WaitForServerConnection();
        await server.ExpectSimpleQuery("DISCARD ALL");
        await server.ExpectExtendedQuery();

        //Act
        var cancelTask = Task.Run(cts.Cancel, cancellationToken: TestContext.Current.CancellationToken);
        var cancellationRequestTask = postmasterMock.WaitForCancellationRequest().AsTask();
        // Give 1 second to make sure we didn't send cancellation request
        await Task.Delay(1000, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        cancelTask.IsCompleted.Should().BeFalse();
        cancellationRequestTask.IsCompleted.Should().BeFalse();

        if (failPrependedQuery)
        {
            await server
                .WriteErrorResponse(PostgresErrorCodes.SyntaxError)
                .WriteReadyForQuery()
                .FlushAsync();

            await cancelTask;
            await cancellationRequestTask;

            await Assert.ThrowsAsync<PostgresException>(async () => await queryTask);
            conn.State.Should().Be(ConnectionState.Closed);
            return;
        }

        await server
            .WriteCommandComplete()
            .WriteReadyForQuery()
            .FlushAsync();

        await cancelTask;
        await cancellationRequestTask;

        await server
            .WriteErrorResponse(PostgresErrorCodes.QueryCanceled)
            .WriteReadyForQuery()
            .FlushAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(async () => await queryTask);

        queryTask = conn.ExecuteNonQueryAsync("SELECT 1", cancellationToken: TestContext.Current.CancellationToken);
        await server.ExpectExtendedQuery();
        await server
            .WriteParseComplete()
            .WriteBindComplete()
            .WriteNoData()
            .WriteCommandComplete()
            .WriteReadyForQuery()
            .FlushAsync();
        await queryTask;
    }

    [Fact]
    public async Task cancel_while_reading_from_long_running_query()
    {
        if (IsMultiplexing)
            return;

        //Arrange
        await using var conn = await OpenConnectionAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
SELECT *, CASE WHEN "t"."i" = 50000 THEN pg_sleep(100) ELSE NULL END
FROM
(
    SELECT generate_series(1, 1000000) AS "i"
) AS "t"
""";

        //Act
        using (var cts = new CancellationTokenSource())
        await using (var reader = await cmd.ExecuteReaderAsync(cts.Token))
        {
            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            {
                var i = 0;
                while (await reader.ReadAsync(cts.Token))
                {
                    i++;
                    if (i == 10)
                        cts.Cancel();
                }
            });
        }

        //Assert
        cmd.CommandText = "SELECT 42";
        (await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken)).Should().Be(42);
    }

    // Make sure we do not lose unread messages after resetting oversize buffer
    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/5218")]
    public async Task oversize_buffer_lost_messages()
    {
        if (IsMultiplexing)
            return;

        //Arrange
        var csb = new PgSqlConnectionStringBuilder(ConnectionString)
        {
            NoResetOnClose = true
        };
        await using var mock = PgPostmasterMock.Start(csb.ConnectionString);
        await using var dataSource = CreateDataSource(mock.ConnectionString);
        await using var connection = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        var connector = connection.Connector;

        var server = await mock.WaitForServerConnection();
        await server
            .WriteParseComplete()
            .WriteBindComplete()
            .WriteRowDescription(new FieldDescription(TextOid))
            .WriteDataRowWithFlush(Encoding.ASCII.GetBytes(new string('a', connection.Settings.ReadBufferSize * 2)));
        // Just to make sure we have enough space
        await server.FlushAsync();
        await server
            .WriteDataRow(Encoding.ASCII.GetBytes("abc"))
            .WriteCommandComplete()
            .WriteReadyForQuery()
            .WriteParameterStatus("SomeKey", "SomeValue")
            .FlushAsync();

        //Act
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT 1";
        await using (await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken)) { }

        await connection.CloseAsync();
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        //Assert
        connection.Connector.Should().BeSameAs(connector);
        // We'll get new value after the next query reads ParameterStatus from the buffer
        connection.PostgresParameters.Should().NotContain(new KeyValuePair<string, string>("SomeKey", "SomeValue"));

        await server
            .WriteParseComplete()
            .WriteBindComplete()
            .WriteRowDescription(new FieldDescription(TextOid))
            .WriteDataRow(Encoding.ASCII.GetBytes("abc"))
            .WriteCommandComplete()
            .WriteReadyForQuery()
            .FlushAsync();

        await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);

        connection.PostgresParameters.Should().ContainKey("SomeKey").WhoseValue.Should().Be("SomeValue");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task completed_transaction_throws(bool commit)
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync(TestContext.Current.CancellationToken);
        await using var cmd = conn.CreateCommand();

        //Act
        if (commit)
            await tx.CommitAsync(TestContext.Current.CancellationToken);
        else
            await tx.RollbackAsync(TestContext.Current.CancellationToken);

        //Assert
        Assert.Throws<InvalidOperationException>(() => cmd.Transaction = tx);
    }

    // Writing to properties of a disposed command raises ObjectDisposedException.
    [Fact]
    public async Task disposed_command_throws_on_assignment()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        var command = new PgSqlCommand("SELECT 1");

        //Act
        command.Dispose();

        //Assert
        Assert.Throws<ObjectDisposedException>(() => command.Connection = conn);
        Assert.Throws<ObjectDisposedException>(() => command.CommandText = "SELECT 2");

        command.Connection.Should().BeNull();
        command.CommandText.Should().Be("SELECT 1");
    }
}

public sealed class CommandTests_NonMultiplexing() : CommandTests(MultiplexingMode.NonMultiplexing);
public sealed class CommandTests_Multiplexing() : CommandTests(MultiplexingMode.Multiplexing);

/// <summary>
/// The CommandTests that disable SQL rewriting through a process-wide AppContext switch, so they must not run
/// in parallel with any other test.
/// </summary>
[Collection(NonParallelCollection.Name)]
public abstract class CommandTestsNonParallel(MultiplexingMode multiplexingMode) : MultiplexingTestBase(multiplexingMode)
{
    // Disables sql rewriting
    [Fact]
    public async Task legacy_batching_is_not_supported_when_enable_sql_parsing_is_disabled()
    {
        //Arrange
        using var _ = DisableSqlRewriting();

        using var conn = await OpenConnectionAsync();
        using var cmd = new PgSqlCommand("SELECT 1; SELECT 2", conn);

        //Act
        var exception = await Assert.ThrowsAsync<PostgresException>(async () => await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken));

        //Assert
        exception.SqlState.Should().Be(PostgresErrorCodes.SyntaxError);
    }

    // Disables sql rewriting
    [Fact]
    public async Task positional_parameters_are_supported_when_enable_sql_parsing_is_disabled()
    {
        //Arrange
        using var _ = DisableSqlRewriting();

        using var conn = await OpenConnectionAsync();
        using var cmd = new PgSqlCommand("SELECT $1", conn);
        cmd.Parameters.Add(new PgSqlParameter { PgSqlDbType = PgSqlDbType.Integer, Value = 8 });

        //Act
        var result = await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken);

        //Assert
        result.Should().Be(8);
    }

    // Disables sql rewriting
    [Fact]
    public async Task named_parameters_are_not_supported_when_enable_sql_parsing_is_disabled()
    {
        //Arrange
        using var _ = DisableSqlRewriting();

        using var conn = await OpenConnectionAsync();
        using var cmd = new PgSqlCommand("SELECT @p", conn);
        cmd.Parameters.Add(new PgSqlParameter("p", 8));

        //Act
        var act = async () => await cmd.ExecuteScalarAsync();

        //Assert
        await act.Should().ThrowExactlyAsync<NotSupportedException>();
    }
}

[Collection(NonParallelCollection.Name)]
public sealed class CommandTestsNonParallel_NonMultiplexing() : CommandTestsNonParallel(MultiplexingMode.NonMultiplexing);
[Collection(NonParallelCollection.Name)]
public sealed class CommandTestsNonParallel_Multiplexing() : CommandTestsNonParallel(MultiplexingMode.Multiplexing);
