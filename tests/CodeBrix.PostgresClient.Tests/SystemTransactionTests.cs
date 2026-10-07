using System;
using System.Data;
using System.Threading;
using System.Transactions;
using SilverAssertions;
using Xunit;
using static CodeBrix.PostgresClient.Tests.TestUtil;

namespace CodeBrix.PostgresClient.Tests; //was previously: Npgsql.Tests;

// This test suite contains ambient transaction tests, except those involving distributed transactions which are only
// supported on .NET Framework / Windows. Distributed transaction tests are in DistributedTransactionTests.
public class SystemTransactionTests(SystemTransactionTestsFixture fixture) : TestBase, IClassFixture<SystemTransactionTestsFixture>
{
    // Single connection enlisting explicitly, committing
    [Fact]
    public void explicit_enlist()
    {
        var dataSource = EnlistOffDataSource;
        var tableName = CreateTempTable(dataSource, "name TEXT");
        using var conn = dataSource.OpenConnection();
        using (var scope = new TransactionScope())
        {
            conn.EnlistTransaction(Transaction.Current);
            conn.ExecuteNonQuery(@$"INSERT INTO {tableName} (name) VALUES ('test')").Should().Be(1, "Unexpected insert rowcount");
            AssertNoDistributedIdentifier();
            AssertNoPreparedTransactions();
            scope.Complete();
        }
        AssertNoDistributedIdentifier();
        AssertNoPreparedTransactions();
        using (var tx = conn.BeginTransaction())
        {
            conn.ExecuteScalar(@$"SELECT COUNT(*) FROM {tableName}").Should().Be(1, "Unexpected data count");
            tx.Rollback();
        }
    }

    // Single connection enlisting implicitly, committing
    [Fact]
    public void implicit_enlist()
    {
        var dataSource = EnlistOnDataSource;
        var tableName = CreateTempTable(dataSource, "name TEXT");
        using var conn = dataSource.CreateConnection();
        using (var scope = new TransactionScope())
        {
            conn.Open();
            conn.ExecuteNonQuery(@$"INSERT INTO {tableName} (name) VALUES ('test')").Should().Be(1, "Unexpected insert rowcount");
            AssertNoDistributedIdentifier();
            AssertNoPreparedTransactions();
            scope.Complete();
        }
        using (var tx = conn.BeginTransaction())
        {
            conn.ExecuteScalar(@$"SELECT COUNT(*) FROM {tableName}").Should().Be(1, "Unexpected data count");
            tx.Rollback();
        }
    }

    [Fact]
    public void enlist_off()
    {
        var dataSource = EnlistOffDataSource;
        var tableName = CreateTempTable(dataSource, "name TEXT");
        using (new TransactionScope())
        using (var conn1 = dataSource.OpenConnection())
        using (var conn2 = dataSource.OpenConnection())
        {
            conn1.EnlistedTransaction.Should().BeNull();
            conn1.ExecuteNonQuery(@$"INSERT INTO {tableName} (name) VALUES ('test')").Should().Be(1, "Unexpected insert rowcount");
            conn2.ExecuteScalar($"SELECT COUNT(*) FROM {tableName}").Should().Be(1, "Unexpected data count");
        }

        // Scope disposed and not completed => rollback, but no enlistment, so changes should still be there.
        using (var conn3 = dataSource.OpenConnection())
        {
            conn3.ExecuteScalar($"SELECT COUNT(*) FROM {tableName}").Should().Be(1, "Insert unexpectedly rollback-ed");
        }
    }

    // Single connection enlisting explicitly, rollback
    [Fact]
    public void rollback_explicit_enlist()
    {
        using var dataSource = CreateDataSource();
        var tableName = CreateTempTable(dataSource, "name TEXT");
        using var conn = dataSource.OpenConnection();
        using (new TransactionScope())
        {
            conn.EnlistTransaction(Transaction.Current);
            conn.ExecuteNonQuery(@$"INSERT INTO {tableName} (name) VALUES ('test')").Should().Be(1, "Unexpected insert rowcount");
            // No commit
        }
        AssertNoDistributedIdentifier();
        AssertNoPreparedTransactions();
        using (var tx = conn.BeginTransaction())
        {
            conn.ExecuteScalar(@$"SELECT COUNT(*) FROM {tableName}").Should().Be(0, "Unexpected data count");
            tx.Rollback();
        }
    }

    // Single connection enlisting implicitly, rollback
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [IssueLink("https://github.com/npgsql/npgsql/issues/2408")]
    public void rollback_implicit_enlist(bool pooling)
    {
        using var dataSource = CreateDataSource(csb => csb.Pooling = pooling);
        var tableName = CreateTempTable(dataSource, "name TEXT");

        using (new TransactionScope())
        using (var conn = dataSource.OpenConnection())
        {
            conn.ExecuteNonQuery(@$"INSERT INTO {tableName} (name) VALUES ('test')").Should().Be(1, "Unexpected insert rowcount");
            AssertNoDistributedIdentifier();
            AssertNoPreparedTransactions();
            // No commit
        }

        AssertNumberOfRows(0, tableName);
    }

    [Fact]
    public void two_consecutive_connections()
    {
        var dataSource = EnlistOnDataSource;
        var tableName = CreateTempTable(dataSource, "name TEXT");
        using (var scope = new TransactionScope())
        {
            using (var conn1 = dataSource.OpenConnection())
            {
                conn1.ExecuteNonQuery(@$"INSERT INTO {tableName} (name) VALUES ('test1')").Should().Be(1, "Unexpected first insert rowcount");
            }

            using (var conn2 = dataSource.OpenConnection())
            {
                conn2.ExecuteNonQuery(@$"INSERT INTO {tableName} (name) VALUES ('test2')").Should().Be(1, "Unexpected second insert rowcount");
            }

            // Consecutive connections used in same scope should not promote the transaction to distributed.
            AssertNoDistributedIdentifier();
            AssertNoPreparedTransactions();
            scope.Complete();
        }
        AssertNumberOfRows(2, tableName);
    }

    [Fact]
    public void close_connection()
    {
        // We assert the number of idle connections below
        using var dataSource = CreateDataSource(csb => csb.Enlist = true);
        var tableName = CreateTempTable(dataSource, "name TEXT");
        using (var scope = new TransactionScope())
        using (var conn = dataSource.OpenConnection())
        {
            conn.ExecuteNonQuery(@$"INSERT INTO {tableName} (name) VALUES ('test')").Should().Be(1, "Unexpected insert rowcount");
            conn.Close();
            AssertNoDistributedIdentifier();
            AssertNoPreparedTransactions();
            scope.Complete();
        }
        AssertNumberOfRows(1, tableName);
        dataSource.Statistics.Idle.Should().Be(1);
    }

    [Fact]
    public void enlist_to_two_transactions()
    {
        var dataSource = EnlistOffDataSource;
        var tableName = CreateTempTable(dataSource, "name TEXT");
        using var conn = dataSource.OpenConnection();
        var ctx = new CommittableTransaction();
        conn.EnlistTransaction(ctx);
        Assert.Throws<InvalidOperationException>(() => conn.EnlistTransaction(new CommittableTransaction()));
        ctx.Rollback();

        using var tx = conn.BeginTransaction();
        conn.ExecuteScalar(@$"SELECT COUNT(*) FROM {tableName}").Should().Be(0);
        tx.Rollback();
    }

    [Fact]
    public void enlist_twice_to_same_transaction()
    {
        var dataSource = EnlistOffDataSource;
        var tableName = CreateTempTable(dataSource, "name TEXT");
        using var conn = dataSource.OpenConnection();
        var ctx = new CommittableTransaction();
        conn.EnlistTransaction(ctx);
        conn.EnlistTransaction(ctx);
        ctx.Rollback();

        using var tx = conn.BeginTransaction();
        conn.ExecuteScalar(@$"SELECT COUNT(*) FROM {tableName}").Should().Be(0);
        tx.Rollback();
    }

    [Fact]
    public void scope_after_scope()
    {
        var dataSource = EnlistOffDataSource;
        var tableName = CreateTempTable(dataSource, "name TEXT");
        using var conn = dataSource.OpenConnection();
        using (new TransactionScope())
            conn.EnlistTransaction(Transaction.Current);
        using (new TransactionScope())
            conn.EnlistTransaction(Transaction.Current);

        using (var tx = conn.BeginTransaction())
        {
            conn.ExecuteScalar(@$"SELECT COUNT(*) FROM {tableName}").Should().Be(0);
            tx.Rollback();
        }
    }

    [Fact]
    public void reuse_connection()
    {
        // We check the ProcessID below
        using var dataSource = CreateDataSource(csb => csb.Enlist = true);
        var tableName = CreateTempTable(dataSource, "name TEXT");
        using (var scope = new TransactionScope())
        using (var conn = dataSource.CreateConnection())
        {
            conn.Open();
            var processId = conn.ProcessID;
            conn.ExecuteNonQuery(@$"INSERT INTO {tableName} (name) VALUES ('test1')");
            conn.Close();

            conn.Open();
            conn.ProcessID.Should().Be(processId);
            conn.ExecuteNonQuery(@$"INSERT INTO {tableName} (name) VALUES ('test2')");
            conn.Close();

            scope.Complete();
        }
        AssertNumberOfRows(2, tableName);
    }

    [Fact]
    public void reuse_connection_rollback()
    {
        // We check the ProcessID below
        using var dataSource = CreateDataSource(csb => csb.Enlist = true);
        var tableName = CreateTempTable(dataSource, "name TEXT");
        using (new TransactionScope())
        using (var conn = dataSource.CreateConnection())
        {
            conn.Open();
            var processId = conn.ProcessID;
            conn.ExecuteNonQuery(@$"INSERT INTO {tableName} (name) VALUES ('test1')");
            conn.Close();

            conn.Open();
            conn.ProcessID.Should().Be(processId);
            conn.ExecuteNonQuery(@$"INSERT INTO {tableName} (name) VALUES ('test2')");
            conn.Close();

            // No commit
        }
        AssertNumberOfRows(0, tableName);
    }

    [Fact(Skip = "Timeout doesn't seem to fire on .NET Core / Linux")]
    public void timeout_triggers_rollback_while_busy()
    {
        var dataSource = EnlistOffDataSource;
        var tableName = CreateTempTable(dataSource, "name TEXT");
        using (var conn = dataSource.OpenConnection())
        {
            using (new TransactionScope(TransactionScopeOption.Required, TimeSpan.FromSeconds(1)))
            {
                conn.EnlistTransaction(Transaction.Current);
                Assert.Throws<PostgresException>(() => CreateSleepCommand(conn, 5).ExecuteNonQuery())
                    .SqlState.Should().Be(PostgresErrorCodes.QueryCanceled);

            }
        }
        AssertNumberOfRows(0, tableName);
    }

    [Fact]
    [IssueLink("https://github.com/npgsql/npgsql/issues/1579")]
    public void schema_connection_should_not_enlist()
    {
        var dataSource = EnlistOnDataSource;
        var tableName = CreateTempTable(dataSource, "name TEXT");
        using var tran = new TransactionScope();
        using var conn = dataSource.OpenConnection();
        using var cmd = new PgSqlCommand($"SELECT * FROM {tableName}", conn);
        using var reader = cmd.ExecuteReader(CommandBehavior.KeyInfo);
        reader.GetColumnSchema();
        AssertNoDistributedIdentifier();
        AssertNoPreparedTransactions();
        tran.Complete();
    }

    [Fact]
    [IssueLink("https://github.com/npgsql/npgsql/issues/1737")]
    public void single_unpooled_connection()
    {
        using var dataSource = CreateDataSource(csb =>
        {
            csb.Pooling = false;
            csb.Enlist = true;
        });
        using var scope = new TransactionScope();

        using (var conn = dataSource.OpenConnection())
        using (var cmd = new PgSqlCommand("SELECT 1", conn))
            cmd.ExecuteNonQuery();

        scope.Complete();
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    [IssueLink("https://github.com/npgsql/npgsql/issues/4963"), IssueLink("https://github.com/npgsql/npgsql/issues/5783")]
    public void single_closed_connection_in_transaction_scope(bool pooling, bool multipleHosts)
    {
        using var dataSource = CreateDataSource(csb =>
        {
            csb.Pooling = pooling;
            csb.Enlist = true;
            csb.Host = multipleHosts ? "localhost,127.0.0.1" : csb.Host;
        });

        using (var scope = new TransactionScope())
        using (var conn = dataSource.OpenConnection())
        using (var cmd = new PgSqlCommand("SELECT 1", conn))
        {
            cmd.ExecuteNonQuery();
            conn.Close();
            (pooling ? dataSource.Statistics.Busy : dataSource.Statistics.Total).Should().Be(1);
            scope.Complete();
        }

        (pooling ? dataSource.Statistics.Busy : dataSource.Statistics.Total).Should().Be(0);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [IssueLink("https://github.com/npgsql/npgsql/issues/3863")]
    public void break_connector_while_in_transaction_scope_with_rollback(bool pooling)
    {
        using var dataSource = CreateDataSource(csb => csb.Pooling = pooling);
        using var scope = new TransactionScope();
        var conn = dataSource.OpenConnection();

        conn.ExecuteNonQuery("SELECT 1");
        conn.Connector.Break(new Exception(nameof(break_connector_while_in_transaction_scope_with_rollback)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [IssueLink("https://github.com/npgsql/npgsql/issues/3863")]
    public void break_connector_while_in_transaction_scope_with_commit(bool pooling)
    {
        using var dataSource = CreateDataSource(csb => csb.Pooling = pooling);
        var ex = Assert.Throws<TransactionInDoubtException>(() =>
        {
            using var scope = new TransactionScope();
            var conn = dataSource.OpenConnection();

            conn.ExecuteNonQuery("SELECT 1");
            conn.Connector.Break(new Exception(nameof(break_connector_while_in_transaction_scope_with_commit)));

            scope.Complete();
        });
        ex.InnerException.Should().BeOfType<ObjectDisposedException>();
        ex.InnerException.InnerException.Should().BeOfType<Exception>();
        ex.InnerException.InnerException.Message.Should().Be(nameof(break_connector_while_in_transaction_scope_with_commit));
    }

    [Fact]
    [IssueLink("https://github.com/npgsql/npgsql/issues/4085")]
    public void open_connection_with_enlist_and_aborted_TransactionScope()
    {
        var dataSource = EnlistOnDataSource;
        for (var i = 0; i < 2; i++)
        {
            using var outerScope = new TransactionScope();

            try
            {
                using var innerScope = new TransactionScope();
                throw new Exception("Random exception to abort the transaction scope");
            }
            catch (Exception)
            {
            }

            var ex = Assert.Throws<TransactionException>(() => dataSource.OpenConnection());
            ex.Message.Should().Be("The operation is not valid for the state of the transaction.");
        }
    }

    [Fact]
    [IssueLink("https://github.com/npgsql/npgsql/issues/1594")]
    public void bug_1594()
    {
        var dataSource = EnlistOnDataSource;
        var tableName = CreateTempTable(dataSource, "name TEXT");
        using var outerScope = new TransactionScope();

        using (var conn = dataSource.OpenConnection())
        using (var innerScope1 = new TransactionScope())
        {
            conn.ExecuteNonQuery(@$"INSERT INTO {tableName} (name) VALUES ('test1')");
            innerScope1.Complete();
        }

        using (dataSource.OpenConnection())
        using (new TransactionScope())
        {
            // Don't complete, triggering rollback
        }
    }

    #region Utilities

    void AssertNoPreparedTransactions()
        => GetNumberOfPreparedTransactions().Should().Be(0, "Prepared transactions found");

    int GetNumberOfPreparedTransactions()
    {
        var dataSource = EnlistOffDataSource;
        using var conn = dataSource.OpenConnection();
        using var cmd = new PgSqlCommand("SELECT COUNT(*) FROM pg_prepared_xacts WHERE database = @database", conn);
        cmd.Parameters.Add(new PgSqlParameter("database", conn.Database));
        return (int)(long)cmd.ExecuteScalar();
    }

    void AssertNumberOfRows(int expected, string tableName)
    {
        using var conn = OpenConnection();
        conn.ExecuteScalar(@$"SELECT COUNT(*) FROM {tableName}").Should().Be(expected, "Unexpected data count");
    }

    static void AssertNoDistributedIdentifier()
        => (Transaction.Current?.TransactionInformation.DistributedIdentifier ?? Guid.Empty).Should().Be(Guid.Empty, "Distributed identifier found");

    #endregion Utilities

    #region Setup

    PgSqlDataSource EnlistOnDataSource => fixture.EnlistOnDataSource;

    PgSqlDataSource EnlistOffDataSource => fixture.EnlistOffDataSource;

    internal static string CreateTempTable(PgSqlDataSource dataSource, string columns)
    {
        var tableName = "temp_table" + Interlocked.Increment(ref _tempTableCounter);
        dataSource.ExecuteNonQuery(@$"
START TRANSACTION; SELECT pg_advisory_xact_lock(0);
DROP TABLE IF EXISTS {tableName} CASCADE;
COMMIT;
CREATE TABLE {tableName} ({columns})");
        return tableName;
    }

    #endregion
}

/// <summary>
/// The enlisting and non-enlisting data sources shared by all <see cref="SystemTransactionTests"/>.
/// </summary>
public sealed class SystemTransactionTestsFixture : TestBase, IDisposable
{
    public PgSqlDataSource EnlistOnDataSource { get; }

    public PgSqlDataSource EnlistOffDataSource { get; }

    public SystemTransactionTestsFixture()
    {
        EnlistOnDataSource = CreateDataSource(csb => csb.Enlist = true);
        EnlistOffDataSource = CreateDataSource(csb => csb.Enlist = false);
    }

    public void Dispose()
    {
        EnlistOnDataSource.Dispose();
        EnlistOffDataSource.Dispose();
    }
}
