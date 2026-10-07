using System;
using System.Data;
using System.Threading.Tasks;
using CodeBrix.PostgresClient.Internal;
using CodeBrix.PostgresClient.Tests.Support;
using CodeBrix.PostgresClient.Util;
using SilverAssertions;
using Xunit;
using static CodeBrix.PostgresClient.Tests.TestUtil;

// ReSharper disable MethodHasAsyncOverload
// ReSharper disable UseAwaitUsing

namespace CodeBrix.PostgresClient.Tests; //was previously: Npgsql.Tests;

public abstract class TransactionTests(MultiplexingMode multiplexingMode) : MultiplexingTestBase(multiplexingMode)
{
    // Basic insert within a committed transaction
    [Theory]
    [InlineData(PrepareOrNot.NotPrepared)]
    [InlineData(PrepareOrNot.Prepared)]
    public async Task Commit(PrepareOrNot prepare)
    {
        if (prepare == PrepareOrNot.Prepared && IsMultiplexing)
            return;

        //Arrange
        await using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "name TEXT");

        //Act
        var tx = await conn.BeginTransactionAsync(TestContext.Current.CancellationToken);
        await using (tx)
        {
            var cmd = new PgSqlCommand($"INSERT INTO {table} (name) VALUES ('X')", conn, tx);
            if (prepare == PrepareOrNot.Prepared)
                cmd.Prepare();
            cmd.ExecuteNonQuery();
            conn.ExecuteScalar($"SELECT COUNT(*) FROM {table}").Should().Be(1);
            tx.Commit();
            tx.IsCompleted.Should().BeTrue();
            FluentActions.Invoking(() => tx.Connection).Should().NotThrow();
            (await conn.ExecuteScalarAsync($"SELECT COUNT(*) FROM {table}", cancellationToken: TestContext.Current.CancellationToken)).Should().Be(1);
        }

        //Assert
        // With multiplexing we can't assume that disposed PgSqlTransaction will throw ObjectDisposedException
        // Because disposed PgSqlTransaction might be reused by another thread
        if (!IsMultiplexing)
            Assert.Throws<ObjectDisposedException>(() => tx.Connection);
    }

    // Basic insert within a committed transaction
    [Theory]
    [InlineData(PrepareOrNot.NotPrepared)]
    [InlineData(PrepareOrNot.Prepared)]
    public async Task CommitAsync(PrepareOrNot prepare)
    {
        if (prepare == PrepareOrNot.Prepared && IsMultiplexing)
            return;

        //Arrange
        await using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "name TEXT");

        //Act
        var tx = await conn.BeginTransactionAsync(TestContext.Current.CancellationToken);
        await using (tx)
        {
            var cmd = new PgSqlCommand($"INSERT INTO {table} (name) VALUES ('X')", conn, tx);
            if (prepare == PrepareOrNot.Prepared)
                cmd.Prepare();
            await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
            conn.ExecuteScalar($"SELECT COUNT(*) FROM {table}").Should().Be(1);
            await tx.CommitAsync(TestContext.Current.CancellationToken);
            tx.IsCompleted.Should().BeTrue();
            FluentActions.Invoking(() => tx.Connection).Should().NotThrow();
            (await conn.ExecuteScalarAsync($"SELECT COUNT(*) FROM {table}", cancellationToken: TestContext.Current.CancellationToken)).Should().Be(1);
        }

        //Assert
        // With multiplexing we can't assume that disposed PgSqlTransaction will throw ObjectDisposedException
        // Because disposed PgSqlTransaction might be reused by another thread
        if (!IsMultiplexing)
            Assert.Throws<ObjectDisposedException>(() => tx.Connection);
    }

    // Basic insert within a rolled back transaction
    [Theory]
    [InlineData(PrepareOrNot.NotPrepared)]
    [InlineData(PrepareOrNot.Prepared)]
    public async Task Rollback(PrepareOrNot prepare)
    {
        if (prepare == PrepareOrNot.Prepared && IsMultiplexing)
            return;

        //Arrange
        await using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "name TEXT");

        //Act
        var tx = await conn.BeginTransactionAsync(TestContext.Current.CancellationToken);
        await using (tx)
        {
            var cmd = new PgSqlCommand($"INSERT INTO {table} (name) VALUES ('X')", conn, tx);
            if (prepare == PrepareOrNot.Prepared)
                cmd.Prepare();
            cmd.ExecuteNonQuery();
            conn.ExecuteScalar($"SELECT COUNT(*) FROM {table}").Should().Be(1);
            tx.Rollback();
            tx.IsCompleted.Should().BeTrue();
            FluentActions.Invoking(() => tx.Connection).Should().NotThrow();
            (await conn.ExecuteScalarAsync($"SELECT COUNT(*) FROM {table}", cancellationToken: TestContext.Current.CancellationToken)).Should().Be(0);
        }

        //Assert
        // With multiplexing we can't assume that disposed PgSqlTransaction will throw ObjectDisposedException
        // Because disposed PgSqlTransaction might be reused by another thread
        if (!IsMultiplexing)
            Assert.Throws<ObjectDisposedException>(() => tx.Connection);
    }

    // Basic insert within a rolled back transaction
    [Theory]
    [InlineData(PrepareOrNot.NotPrepared)]
    [InlineData(PrepareOrNot.Prepared)]
    public async Task RollbackAsync(PrepareOrNot prepare)
    {
        if (prepare == PrepareOrNot.Prepared && IsMultiplexing)
            return;

        //Arrange
        await using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "name TEXT");

        //Act
        var tx = await conn.BeginTransactionAsync(TestContext.Current.CancellationToken);
        await using (tx)
        {
            var cmd = new PgSqlCommand($"INSERT INTO {table} (name) VALUES ('X')", conn, tx);
            if (prepare == PrepareOrNot.Prepared)
                cmd.Prepare();
            await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
            conn.ExecuteScalar($"SELECT COUNT(*) FROM {table}").Should().Be(1);
            await tx.RollbackAsync(TestContext.Current.CancellationToken);
            tx.IsCompleted.Should().BeTrue();
            FluentActions.Invoking(() => tx.Connection).Should().NotThrow();
            (await conn.ExecuteScalarAsync($"SELECT COUNT(*) FROM {table}", cancellationToken: TestContext.Current.CancellationToken)).Should().Be(0);
        }

        //Assert
        // With multiplexing we can't assume that disposed PgSqlTransaction will throw ObjectDisposedException
        // Because disposed PgSqlTransaction might be reused by another thread
        if (!IsMultiplexing)
            Assert.Throws<ObjectDisposedException>(() => tx.Connection);
    }

    // Dispose a transaction in progress, should roll back
    [Fact]
    public async Task Rollback_on_Dispose()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "name TEXT");

        //Act
        await using (var tx = await conn.BeginTransactionAsync(TestContext.Current.CancellationToken))
        {
            await conn.ExecuteNonQueryAsync($"INSERT INTO {table} (name) VALUES ('X')", tx: tx, cancellationToken: TestContext.Current.CancellationToken);
        }

        //Assert
        (await conn.ExecuteScalarAsync($"SELECT COUNT(*) FROM {table}", cancellationToken: TestContext.Current.CancellationToken)).Should().Be(0);
    }

    [Fact]
    public async Task Rollback_on_Close()
    {
        //Arrange
        await using var conn1 = await OpenConnectionAsync();
        var table = await CreateTempTable(conn1, "name TEXT");

        //Act
        using (var conn2 = await OpenConnectionAsync())
        {
            var tx = await conn2.BeginTransactionAsync(TestContext.Current.CancellationToken);
            await conn2.ExecuteNonQueryAsync($"INSERT INTO {table} (name) VALUES ('X')", tx, TestContext.Current.CancellationToken);
        }

        //Assert
        (await conn1.ExecuteScalarAsync($"SELECT COUNT(*) FROM {table}", cancellationToken: TestContext.Current.CancellationToken)).Should().Be(0);
    }

    // Intentionally generates an error, putting us in a failed transaction block. Rolls back.
    [Fact]
    public async Task Rollback_failed()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "name TEXT");

        await using var tx = await conn.BeginTransactionAsync(TestContext.Current.CancellationToken);
        await conn.ExecuteNonQueryAsync($"INSERT INTO {table} (name) VALUES ('X')", tx: tx, cancellationToken: TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<PostgresException>(async () => await conn.ExecuteNonQueryAsync("BAD QUERY", cancellationToken: TestContext.Current.CancellationToken));

        //Act
        tx.Rollback();

        //Assert
        tx.IsCompleted.Should().BeTrue();
        (await conn.ExecuteScalarAsync($"SELECT COUNT(*) FROM {table}", cancellationToken: TestContext.Current.CancellationToken)).Should().Be(0);
    }

    // Commits an empty transaction
    [Fact]
    public async Task empty_commit()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();

        //Act
        await conn.BeginTransaction().CommitAsync(TestContext.Current.CancellationToken);
    }

    // Rolls back an empty transaction
    [Fact]
    public async Task empty_rollback()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();

        //Act
        await conn.BeginTransaction().RollbackAsync(TestContext.Current.CancellationToken);
    }

    // Disposes an empty transaction
    [Fact]
    public async Task empty_dispose()
    {
        //Arrange
        await using var dataSource = CreateDataSource();

        //Act
        using (var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken))
        using (conn.BeginTransaction())
        { }

        //Assert
        using (var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken))
        {
            // Make sure the pending BEGIN TRANSACTION didn't leak from the previous open
            var ex = await Assert.ThrowsAsync<PostgresException>(async () => await conn.ExecuteNonQueryAsync("SAVEPOINT foo", cancellationToken: TestContext.Current.CancellationToken));
            ex.SqlState.Should().Be(PostgresErrorCodes.NoActiveSqlTransaction);
        }
    }

    // Tests that the isolation levels are properly supported
    [Theory]
    [InlineData(IsolationLevel.ReadCommitted,   "read committed")]
    [InlineData(IsolationLevel.ReadUncommitted, "read uncommitted")]
    [InlineData(IsolationLevel.RepeatableRead,  "repeatable read")]
    [InlineData(IsolationLevel.Serializable,    "serializable")]
    [InlineData(IsolationLevel.Snapshot,        "repeatable read")]
    [InlineData(IsolationLevel.Unspecified,     "read committed")]
    public async Task isolation_levels(IsolationLevel level, string expectedName)
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();

        //Act
        var tx = conn.BeginTransaction(level);

        //Assert
        conn.ExecuteScalar("SHOW TRANSACTION ISOLATION LEVEL").Should().Be(expectedName);
        await tx.CommitAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task IsolationLevel_Chaos_is_unsupported()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();

        //Assert
        Assert.Throws<NotSupportedException>(() => conn.BeginTransaction(IsolationLevel.Chaos));
    }

    // Rollback of an already rolled back transaction
    [Fact]
    public async Task Rollback_twice()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        var transaction = conn.BeginTransaction();

        //Act
        transaction.Rollback();

        //Assert
        Assert.Throws<InvalidOperationException>(() => transaction.Rollback());
    }

    // Makes sure the creating a transaction via DbConnection sets the proper isolation level
    [Fact]
    [IssueLink("https://github.com/npgsql/npgsql/issues/559")]
    public async Task default_isolation_level()
    {
        await using var conn = await OpenConnectionAsync();
        var tx = conn.BeginTransaction();
        tx.IsolationLevel.Should().Be(IsolationLevel.ReadCommitted);
        tx.Rollback();

        tx = conn.BeginTransaction(IsolationLevel.Unspecified);
        tx.IsolationLevel.Should().Be(IsolationLevel.ReadCommitted);
        tx.Rollback();
    }

    // Makes sure that transactions started in SQL work, except in multiplexing
    [Fact]
    public async Task via_sql()
    {
        if (IsMultiplexing)
            Assert.Skip("Multiplexing: not implemented");

        //Arrange
        await using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "name TEXT");

        if (IsMultiplexing)
        {
            await Assert.ThrowsAsync<NotSupportedException>(async () => await conn.ExecuteNonQueryAsync("BEGIN", cancellationToken: TestContext.Current.CancellationToken));
            return;
        }

        //Act
        await conn.ExecuteNonQueryAsync("BEGIN", cancellationToken: TestContext.Current.CancellationToken);
        await conn.ExecuteNonQueryAsync($"INSERT INTO {table} (name) VALUES ('X')", cancellationToken: TestContext.Current.CancellationToken);
        await conn.ExecuteNonQueryAsync("ROLLBACK", cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        conn.ExecuteScalar($"SELECT COUNT(*) FROM {table}").Should().Be(0);
    }

    [Fact]
    public async Task nested()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();

        //Act
        conn.BeginTransaction();

        //Assert
        Assert.Throws<InvalidOperationException>(() => conn.BeginTransaction());
    }

    [Fact]
    public void begin_transaction_on_closed_connection_throws()
    {
        //Arrange
        using var conn = new PgSqlConnection();

        //Assert
        Assert.Throws<InvalidOperationException>(() => conn.BeginTransaction());
    }

    [Fact]
    public async Task Rollback_failed_transaction_with_timeout()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();

        var tx = conn.BeginTransaction();
        using var cmd = new PgSqlCommand("BAD QUERY", conn, tx);
        cmd.CommandTimeout.Should().NotBe(1);
        cmd.CommandTimeout = 1;

        //Act
        try
        {
            cmd.ExecuteScalar();
            Assert.Fail("Expected a PostgresException");
        }
        catch (PostgresException)
        {
            //Assert
            // Timeout at the backend is now 1
            await tx.RollbackAsync(TestContext.Current.CancellationToken);
            (await conn.ExecuteScalarAsync("SELECT 1", cancellationToken: TestContext.Current.CancellationToken)).Should().Be(1);
        }
    }

    // If a custom command timeout is set, a failed transaction could not be rollbacked to a previous savepoint
    [Fact]
    [IssueLink("https://github.com/npgsql/npgsql/issues/363")]
    [IssueLink("https://github.com/npgsql/npgsql/issues/184")]
    public async Task failed_transaction_cannot_rollback_to_savepoint_with_custom_timeout()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();

        var transaction = conn.BeginTransaction();
        transaction.Save("TestSavePoint");

        using var cmd = new PgSqlCommand("SELECT unknown_thing", conn);
        cmd.CommandTimeout = 1;

        //Act
        try
        {
            cmd.ExecuteScalar();
        }
        catch (PostgresException)
        {
            //Assert
            transaction.Rollback("TestSavePoint");
            conn.ExecuteScalar("SELECT 1").Should().Be(1);
        }
    }

    // Closes a (pooled) connection with a failed transaction and a custom timeout
    [Fact]
    [IssueLink("https://github.com/npgsql/npgsql/issues/719")]
    public async Task failed_transaction_on_close_with_custom_timeout()
    {
        //Arrange
        await using var dataSource = CreateDataSource(csb => csb.Pooling = true);

        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        conn.BeginTransaction();
        var backendProcessId = conn.ProcessID;
        using (var badCmd = new PgSqlCommand("SEL", conn))
        {
            badCmd.CommandTimeout = PgSqlCommand.DefaultTimeout + 1;
            Assert.Throws<PostgresException>(() => badCmd.ExecuteNonQuery());
        }

        //Act
        // Connection now in failed transaction state, and a custom timeout is in place
        conn.Close();
        conn.Open();
        conn.BeginTransaction();

        //Assert
        conn.ProcessID.Should().Be(backendProcessId);
        conn.ExecuteScalar("SELECT 1").Should().Be(1);
    }

    [Fact]
    [IssueLink("https://github.com/npgsql/npgsql/issues/555")]
    public async Task transaction_on_recycled_connection()
    {
        if (IsMultiplexing)
            Assert.Skip("Multiplexing: fails");

        //Arrange
        // Use application name to make sure we have our very own private connection pool
        await using var conn = new PgSqlConnection(ConnectionString + $";Application Name={GetUniqueIdentifier(nameof(transaction_on_recycled_connection))}");
        conn.Open();
        var prevConnectorId = conn.Connector.Id;
        conn.Close();
        conn.Open();
        conn.Connector.Id.Should().Be(prevConnectorId, "the connection pool must return the same connector for this test to be meaningful");

        //Act
        var tx = conn.BeginTransaction();
        conn.ExecuteScalar("SELECT 1");
        await tx.CommitAsync(TestContext.Current.CancellationToken);
        PgSqlConnection.ClearPool(conn);
    }

    [Fact]
    public async Task savepoint()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "name TEXT");
        const string name = "theSavePoint";

        //Act
        using (var tx = conn.BeginTransaction())
        {
            tx.Save(name);

            await conn.ExecuteNonQueryAsync($"INSERT INTO {table} (name) VALUES ('savepointtest')", tx: tx, cancellationToken: TestContext.Current.CancellationToken);
            conn.ExecuteScalar($"SELECT COUNT(*) FROM {table}", tx: tx).Should().Be(1);
            tx.Rollback(name);
            conn.ExecuteScalar($"SELECT COUNT(*) FROM {table}", tx: tx).Should().Be(0);
            await conn.ExecuteNonQueryAsync($"INSERT INTO {table} (name) VALUES ('savepointtest')", tx: tx, cancellationToken: TestContext.Current.CancellationToken);
            tx.Release(name);
            conn.ExecuteScalar($"SELECT COUNT(*) FROM {table}", tx: tx).Should().Be(1);

            await tx.CommitAsync(TestContext.Current.CancellationToken);
        }
        (await conn.ExecuteScalarAsync($"SELECT COUNT(*) FROM {table}", cancellationToken: TestContext.Current.CancellationToken)).Should().Be(1);
    }

    [Fact]
    public async Task savepoint_async()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "name TEXT");
        const string name = "theSavePoint";

        //Act
        using (var tx = conn.BeginTransaction())
        {
            await tx.SaveAsync(name, TestContext.Current.CancellationToken);

            await conn.ExecuteNonQueryAsync($"INSERT INTO {table} (name) VALUES ('savepointtest')", tx: tx, cancellationToken: TestContext.Current.CancellationToken);
            conn.ExecuteScalar($"SELECT COUNT(*) FROM {table}", tx: tx).Should().Be(1);
            await tx.RollbackAsync(name, TestContext.Current.CancellationToken);
            conn.ExecuteScalar($"SELECT COUNT(*) FROM {table}", tx: tx).Should().Be(0);
            await conn.ExecuteNonQueryAsync($"INSERT INTO {table} (name) VALUES ('savepointtest')", tx: tx, cancellationToken: TestContext.Current.CancellationToken);
            await tx.ReleaseAsync(name, TestContext.Current.CancellationToken);
            conn.ExecuteScalar($"SELECT COUNT(*) FROM {table}", tx: tx).Should().Be(1);

            await tx.CommitAsync(TestContext.Current.CancellationToken);
        }
        (await conn.ExecuteScalarAsync($"SELECT COUNT(*) FROM {table}", cancellationToken: TestContext.Current.CancellationToken)).Should().Be(1);
    }

    [Fact]
    public async Task savepoint_quoted()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        await using var tx = conn.BeginTransaction();

        //Act
        tx.Save("a;b");
        tx.Rollback("a;b");
    }

    // Makes sure that creating a savepoint doesn't perform an additional roundtrip, but prepends to the next command
    [Fact]
    public async Task savepoint_prepends()
    {
        //Arrange
        await using var postmasterMock = PgPostmasterMock.Start(ConnectionString);
        await using var dataSource = CreateDataSource(postmasterMock.ConnectionString);
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        var pgMock = await postmasterMock.WaitForServerConnection();

        using var tx = conn.BeginTransaction();

        //Act
        var saveTask = tx.SaveAsync("foo", TestContext.Current.CancellationToken);

        //Assert
        saveTask.Status.Should().Be(TaskStatus.RanToCompletion);

        // If we're here, SaveAsync above didn't wait for any response, which is the right behavior

        await pgMock
            .WriteCommandComplete()
            .WriteReadyForQuery() // BEGIN response
            .WriteCommandComplete()
            .WriteReadyForQuery() // SAVEPOINT response
            .WriteScalarResponseAndFlush(1);

        await conn.ExecuteScalarAsync("SELECT 1", cancellationToken: TestContext.Current.CancellationToken);

        await pgMock.ExpectSimpleQuery("BEGIN TRANSACTION ISOLATION LEVEL READ COMMITTED");
        await pgMock.ExpectSimpleQuery("SAVEPOINT foo");
        await pgMock.ExpectExtendedQuery();
    }

    // Check IsCompleted before, during and after a normal committed transaction
    [Fact]
    [IssueLink("https://github.com/npgsql/npgsql/issues/985")]
    public async Task IsCompleted_commit()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "name TEXT");

        //Act
        var tx = conn.BeginTransaction();
        tx.IsCompleted.Should().BeFalse();
        await conn.ExecuteNonQueryAsync($"INSERT INTO {table} (name) VALUES ('X')", tx: tx, cancellationToken: TestContext.Current.CancellationToken);
        tx.IsCompleted.Should().BeFalse();
        await tx.CommitAsync(TestContext.Current.CancellationToken);
        tx.IsCompleted.Should().BeTrue();
    }

    // Check IsCompleted before, during, and after a successful but rolled back transaction
    [Fact]
    [IssueLink("https://github.com/npgsql/npgsql/issues/985")]
    public async Task IsCompleted_rollback()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "name TEXT");

        //Act
        var tx = conn.BeginTransaction();
        tx.IsCompleted.Should().BeFalse();
        await conn.ExecuteNonQueryAsync($"INSERT INTO {table} (name) VALUES ('X')", tx: tx, cancellationToken: TestContext.Current.CancellationToken);
        tx.IsCompleted.Should().BeFalse();
        tx.Rollback();
        tx.IsCompleted.Should().BeTrue();
    }

    // Check IsCompleted before, during, and after a failed then rolled back transaction
    [Fact]
    [IssueLink("https://github.com/npgsql/npgsql/issues/985")]
    public async Task IsCompleted_rollback_failed()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "name TEXT");

        //Act
        var tx = conn.BeginTransaction();
        tx.IsCompleted.Should().BeFalse();
        await conn.ExecuteNonQueryAsync($"INSERT INTO {table} (name) VALUES ('X')", tx: tx, cancellationToken: TestContext.Current.CancellationToken);
        tx.IsCompleted.Should().BeFalse();
        await Assert.ThrowsAsync<PostgresException>(async () => await conn.ExecuteNonQueryAsync("BAD QUERY", cancellationToken: TestContext.Current.CancellationToken));
        tx.IsCompleted.Should().BeFalse();
        tx.Rollback();
        tx.IsCompleted.Should().BeTrue();
        (await conn.ExecuteScalarAsync($"SELECT COUNT(*) FROM {table}", cancellationToken: TestContext.Current.CancellationToken)).Should().Be(0);
    }

    [Fact]
    [IssueLink("https://github.com/npgsql/npgsql/issues/3248")]
    // More at #3254
    public async Task bug_3248_dispose_transaction_rollback()
    {
        if (!IsMultiplexing)
            return;

        //Arrange
        using var conn = await OpenConnectionAsync();

        //Act
        await using (var tx = await conn.BeginTransactionAsync(TestContext.Current.CancellationToken))
        {
            conn.Connector.Should().NotBeNull();
            await Assert.ThrowsAsync<PostgresException>(async () => await conn.ExecuteScalarAsync("SELECT * FROM \"unknown_table\"", tx: tx, cancellationToken: TestContext.Current.CancellationToken));
            conn.Connector.Should().NotBeNull();
        }

        conn.Connector.Should().BeNull();
    }

    [Fact]
    [IssueLink("https://github.com/npgsql/npgsql/issues/3248")]
    // More at #3254
    public async Task bug_3248_dispose_connection_rollback()
    {
        if (!IsMultiplexing)
            return;

        //Arrange
        var conn = await OpenConnectionAsync();
        var tx = conn.BeginTransaction();
        conn.Connector.Should().NotBeNull();
        await Assert.ThrowsAsync<PostgresException>(async () => await conn.ExecuteScalarAsync("SELECT * FROM \"unknown_table\"", tx: tx, cancellationToken: TestContext.Current.CancellationToken));
        conn.Connector.Should().NotBeNull();

        //Act
        await conn.DisposeAsync();

        //Assert
        conn.Connector.Should().BeNull();
    }

    [Theory]
    [IssueLink("https://github.com/npgsql/npgsql/issues/3306")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task bug_3306(bool inTransactionBlock)
    {
        //Arrange
        var conn = await OpenConnectionAsync();
        var tx = await conn.BeginTransactionAsync(TestContext.Current.CancellationToken);
        await conn.ExecuteNonQueryAsync("SELECT 1", tx, TestContext.Current.CancellationToken);
        if (!inTransactionBlock)
            await tx.RollbackAsync(TestContext.Current.CancellationToken);
        await conn.CloseAsync();

        conn = await OpenConnectionAsync();
        var tx2 = await conn.BeginTransactionAsync(TestContext.Current.CancellationToken);

        //Act
        await tx.DisposeAsync();

        //Assert
        tx.IsDisposed.Should().BeTrue();
        tx2.IsDisposed.Should().BeFalse();

        await conn.DisposeAsync();
    }

    [Fact]
    [IssueLink("https://github.com/npgsql/efcore.pg/issues/1593")]
    public async Task access_connection_on_completed_transaction()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        using var tx = await conn.BeginTransactionAsync(TestContext.Current.CancellationToken);

        //Act
        tx.Commit();

        //Assert
        tx.Connection.Should().BeSameAs(conn);
    }

    [Fact]
    public async Task unbound_transaction_reuse()
    {
        await using var dataSource = CreateDataSource(csb =>
        {
            csb.MinPoolSize = 1;
            csb.MaxPoolSize = 1;
        });

        await using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "name TEXT");

        await using var conn1 = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        var tx1 = conn1.BeginTransaction();
        await using (var ___ = tx1)
        {
            using var cmd1 = conn1.CreateCommand();
            cmd1.CommandText = $"INSERT INTO {table} (name) VALUES ('X'); SELECT 1";
            await using (var reader1 = await cmd1.ExecuteReaderAsync(TestContext.Current.CancellationToken))
            {
                (await reader1.ReadAsync(TestContext.Current.CancellationToken)).Should().BeTrue();
                reader1.GetInt32(0).Should().Be(1);
                reader1.RecordsAffected.Should().Be(1);
            }
            await tx1.CommitAsync(TestContext.Current.CancellationToken);
            (await conn1.ExecuteScalarAsync($"SELECT COUNT(*) FROM {table}", cancellationToken: TestContext.Current.CancellationToken)).Should().Be(1);
            await conn1.CloseAsync();
        }

        await using var conn2 = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        var tx2 = conn2.BeginTransaction();
        await using (var ___ = tx2)
        {
            tx2.Should().NotBeSameAs(tx1);
            using var cmd2 = conn2.CreateCommand();
            cmd2.CommandText = $"INSERT INTO {table} (name) VALUES ('Y'); SELECT 2";
            await using (var reader2 = await cmd2.ExecuteReaderAsync(TestContext.Current.CancellationToken))
            {
                (await reader2.ReadAsync(TestContext.Current.CancellationToken)).Should().BeTrue();
                reader2.GetInt32(0).Should().Be(2);
                reader2.RecordsAffected.Should().Be(1);
            }
            await tx2.CommitAsync(TestContext.Current.CancellationToken);
            (await conn2.ExecuteScalarAsync($"SELECT COUNT(*) FROM {table}", cancellationToken: TestContext.Current.CancellationToken)).Should().Be(2);
            await conn2.CloseAsync();
        }

        await using var conn3 = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        var tx3 = conn3.BeginTransaction();
        await using (var ___ = tx3)
        {
            tx3.Should().BeSameAs(tx1);
            using var cmd3 = conn3.CreateCommand();
            cmd3.CommandText = $"INSERT INTO {table} (name) VALUES ('Z'); SELECT 3";
            await using (var reader3 = await cmd3.ExecuteReaderAsync(TestContext.Current.CancellationToken))
            {
                (await reader3.ReadAsync(TestContext.Current.CancellationToken)).Should().BeTrue();
                reader3.GetInt32(0).Should().Be(3);
                reader3.RecordsAffected.Should().Be(1);
            }
            await tx3.CommitAsync(TestContext.Current.CancellationToken);
            (await conn3.ExecuteScalarAsync($"SELECT COUNT(*) FROM {table}", cancellationToken: TestContext.Current.CancellationToken)).Should().Be(3);
            await conn3.CloseAsync();
        }
    }

    [Fact]
    [IssueLink("https://github.com/npgsql/npgsql/issues/3686")]
    public async Task bug_3686()
    {
        if (IsMultiplexing)
            return;

        //Arrange
        await using var dataSource = CreateDataSource(csb => csb.Pooling = false);
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using var tx = await conn.BeginTransactionAsync(TestContext.Current.CancellationToken);
        await conn.ExecuteNonQueryAsync("SELECT 1", tx, TestContext.Current.CancellationToken);
        await tx.CommitAsync(TestContext.Current.CancellationToken);

        //Act
        await conn.CloseAsync();

        //Assert
        FluentActions.Invoking(() =>
        {
            _ = tx.Connection;
        }).Should().NotThrow();
    }

    // Older tests

    [Fact]
    public void bug_184_rollback_fails_on_aborted_transaction()
    {
        //Arrange
        var csb = new PgSqlConnectionStringBuilder(ConnectionString);
        csb.CommandTimeout = 100000;

        using var connTimeoutChanged = new PgSqlConnection(csb.ToString());
        connTimeoutChanged.Open();
        using var t = connTimeoutChanged.BeginTransaction();

        //Act
        try {
            var command = new PgSqlCommand("select count(*) from dta", connTimeoutChanged, t);
            _ = command.ExecuteScalar();
        } catch (Exception) {
            t.Rollback();
        }
    }
}

public sealed class TransactionTests_NonMultiplexing() : TransactionTests(MultiplexingMode.NonMultiplexing);
public sealed class TransactionTests_Multiplexing() : TransactionTests(MultiplexingMode.Multiplexing);

[Collection(NonParallelCollection.Name)]
public abstract class TransactionTestsNonParallel(MultiplexingMode multiplexingMode) : MultiplexingTestBase(multiplexingMode)
{
    // Tests that a if a DatabaseInfoFactory is registered for a database that doesn't support transactions, no transactions are created
    [Fact]
    public async Task transaction_not_supported()
    {
        // TODO: rewrite to DataSource
        if (IsMultiplexing)
            Assert.Skip("Need to rethink/redo dummy transaction mode");

        var connString = new PgSqlConnectionStringBuilder(ConnectionString)
        {
            ApplicationName = nameof(transaction_not_supported) + IsMultiplexing
        }.ToString();

        PgSqlDatabaseInfo.RegisterFactory(new NoTransactionDatabaseInfoFactory());
        try
        {
            using var conn = new PgSqlConnection(connString);
            await conn.OpenAsync(TestContext.Current.CancellationToken);
            using var tx = conn.BeginTransaction();

            // Detect that we're not really in a transaction
            var prevTxId = conn.ExecuteScalar("SELECT txid_current()");
            var nextTxId = conn.ExecuteScalar("SELECT txid_current()");
            // If we're in an actual transaction, the two IDs should be the same
            // https://stackoverflow.com/questions/1651219/how-to-check-for-pending-operations-in-a-postgresql-transaction
            nextTxId.Should().NotBe(prevTxId);
            conn.Close();
        }
        finally
        {
            PgSqlDatabaseInfo.ResetFactories();
        }

        using (var conn = new PgSqlConnection(connString))
        {
            await conn.OpenAsync(TestContext.Current.CancellationToken);
            PgSqlConnection.ClearPool(conn);
            conn.ReloadTypes();
        }

        // Check that everything is back to normal
        using (var conn = new PgSqlConnection(connString))
        {
            await conn.OpenAsync(TestContext.Current.CancellationToken);
            using (var tx = conn.BeginTransaction())
            {
                var prevTxId = conn.ExecuteScalar("SELECT txid_current()");
                var nextTxId = conn.ExecuteScalar("SELECT txid_current()");
                nextTxId.Should().Be(prevTxId);
            }
        }
    }

    class NoTransactionDatabaseInfoFactory : IPgSqlDatabaseInfoFactory
    {
        public async Task<PgSqlDatabaseInfo> Load(PgSqlConnector conn, PgSqlTimeout timeout, bool async)
        {
            var db = new NoTransactionDatabaseInfo(conn);
            await db.LoadPostgresInfo(conn, timeout, async);
            return db;
        }
    }

    class NoTransactionDatabaseInfo : PostgresDatabaseInfo
    {
        public override bool SupportsTransactions => false;

        internal NoTransactionDatabaseInfo(PgSqlConnector conn) : base(conn) {}
    }
}

public sealed class TransactionTestsNonParallel_NonMultiplexing() : TransactionTestsNonParallel(MultiplexingMode.NonMultiplexing);
public sealed class TransactionTestsNonParallel_Multiplexing() : TransactionTestsNonParallel(MultiplexingMode.Multiplexing);
