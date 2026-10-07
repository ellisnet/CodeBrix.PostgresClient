using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Transactions;
using CodeBrix.PostgresClient.Tests.Support;
using SilverAssertions;
using Xunit;
using static CodeBrix.PostgresClient.Tests.TestUtil;

namespace CodeBrix.PostgresClient.Tests; //was previously: Npgsql.Tests;

[Collection(NonParallelCollection.Name)]
public class DistributedTransactionTests : TestBase, IClassFixture<DistributedTransactionTestsFixture>
{
    readonly DistributedTransactionTestsFixture _fixture;

    public DistributedTransactionTests(DistributedTransactionTestsFixture fixture)
    {
        _fixture = fixture;
        if (fixture.SkipReason is not null)
            Assert.Skip(fixture.SkipReason);
        if (fixture.IgnoreExceptOnBuildServerReason is not null)
            IgnoreExceptOnBuildServer(fixture.IgnoreExceptOnBuildServerReason);

        EnlistResource.Counter = 0;
    }

    [Fact]
    public void two_connections_rollback_implicit_enlistment()
    {
        using var adminConn = OpenConnection();
        var table = CreateTempTable(adminConn, "name TEXT");

        var dataSource = EnlistOnDataSource;

        using (new TransactionScope())
        using (var conn1 = dataSource.OpenConnection())
        using (var conn2 = dataSource.OpenConnection())
        {
            conn1.ExecuteNonQuery($"INSERT INTO {table} (name) VALUES ('test1')");
            conn2.ExecuteNonQuery($"INSERT INTO {table} (name) VALUES ('test2')");
        }

        Retry(() =>
        {
            AssertNoDistributedIdentifier();
            AssertNoPreparedTransactions();
            AssertNumberOfRows(adminConn, table, 0);
        });
    }

    [Fact]
    public void two_connections_rollback_explicit_enlistment()
    {
        using var adminConn = OpenConnection();
        var table = CreateTempTable(adminConn, "name TEXT");

        var dataSource = EnlistOffDataSource;

        using (var conn1 = dataSource.OpenConnection())
        using (var conn2 = dataSource.OpenConnection())
        using (new TransactionScope())
        {
            conn1.EnlistTransaction(Transaction.Current);
            conn2.EnlistTransaction(Transaction.Current);

            conn1.ExecuteNonQuery($"INSERT INTO {table} (name) VALUES ('test1')").Should().Be(1, "Unexpected first insert rowcount");
            conn2.ExecuteNonQuery($"INSERT INTO {table} (name) VALUES ('test2')").Should().Be(1, "Unexpected second insert rowcount");
        }

        Retry(() =>
        {
            AssertNoDistributedIdentifier();
            AssertNoPreparedTransactions();
            AssertNumberOfRows(adminConn, table, 0);
        });
    }

    [Fact]
    public void two_connections_commit()
    {
        using var adminConn = OpenConnection();
        var table = CreateTempTable(adminConn, "name TEXT");

        var dataSource = EnlistOnDataSource;

        using (var scope = new TransactionScope())
        using (var conn1 = dataSource.OpenConnection())
        using (var conn2 = dataSource.OpenConnection())
        {
            conn1.ExecuteNonQuery($"INSERT INTO {table} (name) VALUES ('test1')");
            conn2.ExecuteNonQuery($"INSERT INTO {table} (name) VALUES ('test2')");

            scope.Complete();
        }

        Retry(() =>
        {
            AssertNoDistributedIdentifier();
            AssertNoPreparedTransactions();
            AssertNumberOfRows(adminConn, table, 2);
        });
    }

    [Fact]
    public void two_connections_with_failure()
    {
        // Use our own data source since this test breaks the connection with a critical failure, affecting database state tracking.
        using var dataSource = CreateDataSource(csb => csb.Enlist = true);
        using var adminConn = dataSource.OpenConnection();
        var table = CreateTempTable(adminConn, "name TEXT");

        using var scope = new TransactionScope();
        using var conn1 = dataSource.OpenConnection();
        using var conn2 = dataSource.OpenConnection();

        conn1.ExecuteNonQuery($"INSERT INTO {table} (name) VALUES ('test1')");
        conn2.ExecuteNonQuery($"INSERT INTO {table} (name) VALUES ('test2')");

        conn1.ExecuteNonQuery($"SELECT pg_terminate_backend({conn2.ProcessID})");
        scope.Complete();
        Assert.Throws<TransactionAbortedException>(() => scope.Dispose());

        AssertNoDistributedIdentifier();
        AssertNoPreparedTransactions();
        AssertNumberOfRows(adminConn, table, 0);
    }

    // Transaction race, bool distributed
    // Explicit: fails on Appveyor (https://ci.appveyor.com/project/roji/npgsql/build/3.3.0-250)
    [Theory(Explicit = true)]
    [InlineData(false)]
    [InlineData(true)]
    public void transaction_race(bool distributed)
    {
        using var adminConn = OpenConnection();
        var table = CreateTempTable(adminConn, "name TEXT");

        var dataSource = EnlistOnDataSource;

        for (var i = 1; i <= 100; i++)
        {
            var eventQueue = new ConcurrentQueue<TransactionEvent>();
            try
            {
                using (var tx = new TransactionScope())
                using (var conn1 = dataSource.OpenConnection())
                {
                    eventQueue.Enqueue(new TransactionEvent("Scope started, connection enlisted"));
                    conn1.ExecuteNonQuery($"INSERT INTO {table} (name) VALUES ('test1')");
                    eventQueue.Enqueue(new TransactionEvent("Insert done"));

                    if (distributed)
                    {
                        EnlistResource.EscalateToDistributed(eventQueue);
                        AssertHasDistributedIdentifier();
                    }
                    else
                    {
                        EnlistResource.EnlistVolatile(eventQueue);
                        AssertNoDistributedIdentifier();
                    }

                    tx.Complete();
                    eventQueue.Enqueue(new TransactionEvent("Scope completed"));
                }

                eventQueue.Enqueue(new TransactionEvent("Scope disposed"));
                AssertNoDistributedIdentifier();

                if (distributed)
                {
                    // There may be a race condition here, where the prepared transaction above still hasn't completed.
                    // This is by design of MS DTC. Giving it up to 100ms to complete. If it proves flaky, raise
                    // maxLoop.
                    const int maxLoop = 20;
                    for (var j = 0; j < maxLoop; j++)
                    {
                        Thread.Sleep(10);
                        try
                        {
                            AssertNumberOfRows(adminConn, table, i);
                            break;
                        }
                        catch
                        {
                            if (j == maxLoop - 1)
                                throw;
                        }
                    }
                }
                else
                    AssertNumberOfRows(adminConn, table, i);
            }
            catch (Exception ex)
            {
                Assert.Fail($"""
                    Failed at iteration {i}.
                    Events:
                    {FormatEventQueue(eventQueue)}
                    Exception {ex}
                    """);
            }
        }
    }

    // Connection reuse race after transaction, bool distributed
    [Theory(Explicit = true)]
    [InlineData(false)]
    [InlineData(true)]
    public void connection_reuse_race_after_transaction(bool distributed)
    {
        using var adminConn = OpenConnection();
        var table = CreateTempTable(adminConn, "name TEXT");

        var dataSource = EnlistOffDataSource;

        for (var i = 1; i <= 100; i++)
        {
            var eventQueue = new ConcurrentQueue<TransactionEvent>();
            try
            {
                using var conn1 = dataSource.OpenConnection();

                using (var scope = new TransactionScope())
                {
                    conn1.EnlistTransaction(Transaction.Current);
                    eventQueue.Enqueue(new TransactionEvent("Scope started, connection enlisted"));

                    if (distributed)
                    {
                        EnlistResource.EscalateToDistributed(eventQueue);
                        AssertHasDistributedIdentifier();
                    }
                    else
                    {
                        EnlistResource.EnlistVolatile(eventQueue);
                        AssertNoDistributedIdentifier();
                    }

                    conn1.ExecuteNonQuery($"INSERT INTO {table} (name) VALUES ('test1')");
                    eventQueue.Enqueue(new TransactionEvent("Insert done"));

                    scope.Complete();
                    eventQueue.Enqueue(new TransactionEvent("Scope completed"));
                }

                eventQueue.Enqueue(new TransactionEvent("Scope disposed"));

                var act = () => conn1.ExecuteScalar($"SELECT COUNT(*) FROM {table}");
                act.Should().NotThrow();
            }
            catch (Exception ex)
            {
                Assert.Fail($"""
                    Failed at iteration {i}.
                    Events:
                    {FormatEventQueue(eventQueue)}
                    Exception {ex}
                    """);
            }
        }
    }

    // Connection reuse race after rollback, bool distributed
    // Explicit: currently failing.
    [Theory(Explicit = true)]
    [InlineData(false)]
    [InlineData(true)]
    public void connection_reuse_race_after_rollback(bool distributed)
    {
        using var adminConn = OpenConnection();
        var table = CreateTempTable(adminConn, "name TEXT");

        var dataSource = EnlistOffDataSource;

        for (var i = 1; i <= 100; i++)
        {
            var eventQueue = new ConcurrentQueue<TransactionEvent>();
            try
            {
                using var conn1 = dataSource.OpenConnection();

                using (new TransactionScope())
                {
                    conn1.EnlistTransaction(Transaction.Current);
                    eventQueue.Enqueue(new TransactionEvent("Scope started, connection enlisted"));

                    if (distributed)
                    {
                        EnlistResource.EscalateToDistributed(eventQueue);
                        AssertHasDistributedIdentifier();
                    }
                    else
                    {
                        EnlistResource.EnlistVolatile(eventQueue);
                        AssertNoDistributedIdentifier();
                    }

                    conn1.ExecuteNonQuery($"INSERT INTO {table} (name) VALUES ('test1')");
                    eventQueue.Enqueue(new TransactionEvent("Insert done"));

                    eventQueue.Enqueue(new TransactionEvent("Scope not completed"));
                }

                eventQueue.Enqueue(new TransactionEvent("Scope disposed"));
                conn1.EnlistTransaction(null);
                eventQueue.Enqueue(new TransactionEvent("Connection enlisted with null"));
                var act = () => conn1.ExecuteScalar($"SELECT COUNT(*) FROM {table}");
                act.Should().NotThrow();
            }
            catch (Exception ex)
            {
                Assert.Fail($"""
                    Failed at iteration {i}.
                    Events:
                    {FormatEventQueue(eventQueue)}
                    Exception {ex}
                    """);
            }
        }
    }

    // Connection reuse race chaining transactions, bool distributed
    [Theory(Explicit = true)]
    [InlineData(false)]
    [InlineData(true)]
    public void connection_reuse_race_chaining_transaction(bool distributed)
    {
        using var adminConn = OpenConnection();
        var table = CreateTempTable(adminConn, "name TEXT");

        var dataSource = EnlistOffDataSource;

        for (var i = 1; i <= 100; i++)
        {
            var eventQueue = new ConcurrentQueue<TransactionEvent>();
            try
            {
                using var conn1 = dataSource.OpenConnection();

                using (var scope = new TransactionScope())
                {
                    eventQueue.Enqueue(new TransactionEvent("First scope started"));
                    conn1.EnlistTransaction(Transaction.Current);
                    eventQueue.Enqueue(new TransactionEvent("First scope, connection enlisted"));

                    if (distributed)
                    {
                        EnlistResource.EscalateToDistributed(eventQueue);
                        AssertHasDistributedIdentifier();
                    }
                    else
                    {
                        EnlistResource.EnlistVolatile(eventQueue);
                        AssertNoDistributedIdentifier();
                    }

                    conn1.ExecuteNonQuery($"INSERT INTO {table} (name) VALUES ('test1')");
                    eventQueue.Enqueue(new TransactionEvent("First insert done"));

                    scope.Complete();
                    eventQueue.Enqueue(new TransactionEvent("First scope completed"));
                }
                eventQueue.Enqueue(new TransactionEvent("First scope disposed"));

                using (var scope = new TransactionScope())
                {
                    eventQueue.Enqueue(new TransactionEvent("Second scope started"));
                    conn1.EnlistTransaction(Transaction.Current);
                    eventQueue.Enqueue(new TransactionEvent("Second scope, connection enlisted"));

                    if (distributed)
                    {
                        EnlistResource.EscalateToDistributed(eventQueue);
                        AssertHasDistributedIdentifier();
                    }
                    else
                    {
                        EnlistResource.EnlistVolatile(eventQueue);
                        AssertNoDistributedIdentifier();
                    }

                    conn1.ExecuteNonQuery($"INSERT INTO {table} (name) VALUES ('test1')");
                    eventQueue.Enqueue(new TransactionEvent("Second insert done"));

                    scope.Complete();
                    eventQueue.Enqueue(new TransactionEvent("Second scope completed"));
                }
                eventQueue.Enqueue(new TransactionEvent("Second scope disposed"));
            }
            catch (Exception ex)
            {
                Assert.Fail($"""
                    Failed at iteration {i}.
                    Events:
                    {FormatEventQueue(eventQueue)}
                    Exception {ex}
                    """);
            }
        }
    }

    [Fact]
    [IssueLink("https://github.com/npgsql/npgsql/issues/5246")]
    public void transaction_complete_with_undisposed_connections()
    {
        using var deleteOuter = new TransactionScope();
        using (var delImidiate = new TransactionScope(TransactionScopeOption.RequiresNew))
        {
            var deleteNow = EnlistOnDataSource.OpenConnection();
            deleteNow.ExecuteNonQuery("SELECT 'del_now'");
            var deleteNow2 = EnlistOnDataSource.OpenConnection();
            deleteNow2.ExecuteNonQuery("SELECT 'del_now2'");
            delImidiate.Complete();
        }
        var deleteConn = EnlistOnDataSource.OpenConnection();
        deleteConn.ExecuteNonQuery("SELECT 'delete, this should commit last'");
        deleteOuter.Complete();
    }

    #region Utilities

    // MSDTC is asynchronous, i.e. Commit/Rollback may return before the transaction has actually completed in the database;
    // so allow some time for assertions to succeed.
    static void Retry(Action action)
    {
        const int Retries = 50;

        for (var i = 0; i < Retries; i++)
        {
            try
            {
                action();
                return;
            }
            catch (Xunit.Sdk.XunitException)
            {
                if (i == Retries - 1)
                {
                    throw;
                }

                Thread.Sleep(100);
            }
        }
    }

    void AssertNoPreparedTransactions()
        => GetNumberOfPreparedTransactions().Should().Be(0, "Prepared transactions found");

    int GetNumberOfPreparedTransactions()
    {
        var dataSource = EnlistOffDataSource;
        using (var conn = dataSource.OpenConnection())
        using (var cmd = new PgSqlCommand("SELECT COUNT(*) FROM pg_prepared_xacts WHERE database = @database", conn))
        {
            cmd.Parameters.Add(new PgSqlParameter("database", conn.Database));
            return (int)(long)cmd.ExecuteScalar();
        }
    }

    void AssertNumberOfRows(PgSqlConnection connection, string table, int expected)
        => connection.ExecuteScalar($"SELECT COUNT(*) FROM {table}").Should().Be(expected, "Unexpected data count");

    static void AssertNoDistributedIdentifier()
        => (Transaction.Current?.TransactionInformation.DistributedIdentifier ?? Guid.Empty).Should().Be(Guid.Empty, "Distributed identifier found");

    static void AssertHasDistributedIdentifier()
        => (Transaction.Current?.TransactionInformation.DistributedIdentifier ?? Guid.Empty).Should().NotBe(Guid.Empty, "Distributed identifier not found");

    PgSqlDataSource EnlistOnDataSource => _fixture.EnlistOnDataSource;

    PgSqlDataSource EnlistOffDataSource => _fixture.EnlistOffDataSource;

    static string FormatEventQueue(ConcurrentQueue<TransactionEvent> eventQueue)
    {
        eventQueue.Enqueue(new TransactionEvent(@"-------------
Start formatting event queue, going to sleep a bit for late events
-------------"));
        Thread.Sleep(20);
        var eventsMessage = new StringBuilder();
        foreach (var evt in eventQueue)
        {
            eventsMessage.AppendLine(evt.Message);
        }
        return eventsMessage.ToString();
    }

    // Idea from NHibernate test project, DtcFailuresFixture
    public class EnlistResource : IEnlistmentNotification
    {
        public static int Counter { get; set; }

        readonly bool _shouldRollBack;
        readonly string _name;
        readonly ConcurrentQueue<TransactionEvent> _eventQueue;

        public static void EnlistVolatile(ConcurrentQueue<TransactionEvent> eventQueue)
            => EnlistVolatile(false, eventQueue);

        public static void EnlistVolatile(bool shouldRollBack = false, ConcurrentQueue<TransactionEvent> eventQueue = null)
            => Enlist(false, shouldRollBack, eventQueue);

        public static void EscalateToDistributed(ConcurrentQueue<TransactionEvent> eventQueue)
            => EscalateToDistributed(false, eventQueue);

        public static void EscalateToDistributed(bool shouldRollBack = false, ConcurrentQueue<TransactionEvent> eventQueue = null)
            => Enlist(true, shouldRollBack, eventQueue);

        static void Enlist(bool durable, bool shouldRollBack, ConcurrentQueue<TransactionEvent> eventQueue)
        {
            Counter++;

            var name = $"{(durable ? "Durable" : "Volatile")} resource {Counter}";
            var resource = new EnlistResource(shouldRollBack, name, eventQueue);
            if (durable)
                Transaction.Current.EnlistDurable(Guid.NewGuid(), resource, EnlistmentOptions.None);
            else
                Transaction.Current.EnlistVolatile(resource, EnlistmentOptions.None);

            Transaction.Current.TransactionCompleted += resource.Current_TransactionCompleted;

            eventQueue?.Enqueue(new TransactionEvent(name + ": enlisted"));
        }

        EnlistResource(bool shouldRollBack, string name, ConcurrentQueue<TransactionEvent> eventQueue)
        {
            _shouldRollBack = shouldRollBack;
            _name = name;
            _eventQueue = eventQueue;
        }

        public void Prepare(PreparingEnlistment preparingEnlistment)
        {
            _eventQueue?.Enqueue(new TransactionEvent(_name + ": prepare phase start"));
            Thread.Sleep(1);
            if (_shouldRollBack)
            {
                _eventQueue?.Enqueue(new TransactionEvent(_name + ": prepare phase, calling rollback-ed"));
                preparingEnlistment.ForceRollback();
            }
            else
            {
                _eventQueue?.Enqueue(new TransactionEvent(_name + ": prepare phase, calling prepared"));
                preparingEnlistment.Prepared();
            }
            Thread.Sleep(1);
            _eventQueue?.Enqueue(new TransactionEvent(_name + ": prepare phase end"));
        }

        public void Commit(Enlistment enlistment)
        {
            _eventQueue?.Enqueue(new TransactionEvent(_name + ": commit phase start"));
            Thread.Sleep(1);
            _eventQueue?.Enqueue(new TransactionEvent(_name + ": commit phase, calling done"));
            enlistment.Done();
            Thread.Sleep(1);
            _eventQueue?.Enqueue(new TransactionEvent(_name + ": commit phase end"));
        }

        public void Rollback(Enlistment enlistment)
        {
            _eventQueue?.Enqueue(new TransactionEvent(_name + ": rollback phase start"));
            Thread.Sleep(1);
            _eventQueue?.Enqueue(new TransactionEvent(_name + ": rollback phase, calling done"));
            enlistment.Done();
            Thread.Sleep(1);
            _eventQueue?.Enqueue(new TransactionEvent(_name + ": rollback phase end"));
        }

        public void InDoubt(Enlistment enlistment)
        {
            _eventQueue?.Enqueue(new TransactionEvent(_name + ": in-doubt phase start"));
            Thread.Sleep(1);
            _eventQueue?.Enqueue(new TransactionEvent(_name + ": in-doubt phase, calling done"));
            enlistment.Done();
            Thread.Sleep(1);
            _eventQueue?.Enqueue(new TransactionEvent(_name + ": in-doubt phase end"));
        }

        void Current_TransactionCompleted(object sender, TransactionEventArgs e)
        {
            _eventQueue?.Enqueue(new TransactionEvent(_name + ": transaction completed start"));
            Thread.Sleep(1);
            _eventQueue?.Enqueue(new TransactionEvent(_name + ": transaction completed middle"));
            Thread.Sleep(1);
            _eventQueue?.Enqueue(new TransactionEvent(_name + ": transaction completed end"));
        }
    }

    public class TransactionEvent(string message)
    {
        public string Message { get; } = $"{message} (TId {Thread.CurrentThread.ManagedThreadId})";
    }

    #endregion Utilities

    #region Setup

    internal static string CreateTempTable(PgSqlConnection conn, string columns)
    {
        var tableName = "temp_table" + Interlocked.Increment(ref _tempTableCounter);
        conn.ExecuteNonQuery(@$"
START TRANSACTION; SELECT pg_advisory_xact_lock(0);
DROP TABLE IF EXISTS {tableName} CASCADE;
COMMIT;
CREATE TABLE {tableName} ({columns})");
        return tableName;
    }

    #endregion
}

/// <summary>
/// One-time setup for <see cref="DistributedTransactionTests"/>: checks the platform and server configuration, rolls
/// back lingering prepared transactions and creates the enlisting and non-enlisting data sources.
/// </summary>
public sealed class DistributedTransactionTestsFixture : TestBase, IDisposable
{
    public PgSqlDataSource EnlistOnDataSource { get; }

    public PgSqlDataSource EnlistOffDataSource { get; }

    /// <summary>When not null, every test in the class is skipped with this reason.</summary>
    public string SkipReason { get; }

    /// <summary>When not null, every test in the class is skipped (or failed on the build server) with this reason.</summary>
    public string IgnoreExceptOnBuildServerReason { get; }

    public DistributedTransactionTestsFixture()
    {
        if (!OperatingSystem.IsWindows())
        {
            SkipReason = "Distributed transactions are only supported on Windows";
            return;
        }

        using var connection = OpenConnection();

        // Make sure prepared transactions are enabled in postgresql.conf (disabled by default)
        if (int.Parse((string)connection.ExecuteScalar("SHOW max_prepared_transactions")) == 0)
        {
            IgnoreExceptOnBuildServerReason = "max_prepared_transactions is set to 0 in your postgresql.conf";
            return;
        }

        // Roll back any lingering prepared transactions from failed previous runs
        var lingeringTransactions = new List<string>();
        using (var cmd = new PgSqlCommand("SELECT gid FROM pg_prepared_xacts WHERE database=@database", connection))
        {
            cmd.Parameters.AddWithValue("database", new PgSqlConnectionStringBuilder(ConnectionString).Database);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                lingeringTransactions.Add(reader.GetString(0));
        }
        foreach (var xactGid in lingeringTransactions)
            connection.ExecuteNonQuery($"ROLLBACK PREPARED '{xactGid}'");

        EnlistOnDataSource = CreateDataSource(csb => csb.Enlist = true);
        EnlistOffDataSource = CreateDataSource(csb => csb.Enlist = false);
    }

    public void Dispose()
    {
        EnlistOnDataSource?.Dispose();
        EnlistOffDataSource?.Dispose();
    }
}
