using System;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using SilverAssertions;
using Xunit;

namespace CodeBrix.PostgresClient.Tests; //was previously: Npgsql.Tests;

public class PoolTests : TestBase
{
    [Fact]
    public async Task MinPoolSize_equals_MaxPoolSize()
    {
        await using var dataSource = CreateDataSource(csb =>
        {
            csb.MinPoolSize = 30;
            csb.MaxPoolSize = 30;
        });
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public Task MinPoolSize_bigger_than_MaxPoolSize_throws()
        => Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await using var dataSource = CreateDataSource(csb =>
            {
                csb.MinPoolSize = 2;
                csb.MaxPoolSize = 1;
            });
        });

    [Fact]
    public async Task reuse_connector_before_creating_new()
    {
        //Arrange
        await using var dataSource = CreateDataSource();
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        var backendId = conn.Connector.BackendProcessId;

        //Act
        await conn.CloseAsync();
        await conn.OpenAsync(TestContext.Current.CancellationToken);

        //Assert
        conn.Connector.BackendProcessId.Should().Be(backendId);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task get_connector_from_exhausted_pool(bool async)
    {
        //Arrange
        await using var dataSource = CreateDataSource(csb =>
        {
            csb.MaxPoolSize = 1;
            csb.Timeout = 0;
        });

        await using var conn1 = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        // Pool is exhausted
        await using var conn2 = dataSource.CreateConnection();
        _ = Task.Delay(1000, cancellationToken: TestContext.Current.CancellationToken).ContinueWith(async _ =>
        {
            if (async)
                await conn1.CloseAsync();
            else
                conn1.Close();
        });

        //Act
        if (async)
            await conn2.OpenAsync(TestContext.Current.CancellationToken);
        else
            conn2.Open();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task timeout_getting_connector_from_exhausted_pool(bool async)
    {
        //Arrange
        await using var dataSource = CreateDataSource(csb =>
        {
            csb.MaxPoolSize = 1;
            csb.Timeout = 2;
        });

        await using (var conn1 = dataSource.CreateConnection())
        {
            await conn1.OpenAsync(TestContext.Current.CancellationToken);
            // Pool is now exhausted

            //Act
            await using var conn2 = dataSource.CreateConnection();
            var e = async
                ? await Assert.ThrowsAsync<PgSqlException>(async () => await conn2.OpenAsync(TestContext.Current.CancellationToken))
                : Assert.Throws<PgSqlException>(() => conn2.Open());

            //Assert
            e.InnerException.Should().BeOfType<TimeoutException>();
        }

        // conn1 should now be back in the pool as idle
        await using var conn3 = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
    }

    // Timing-based
    [Fact(Explicit = true)]
    public async Task OpenAsync_cancel()
    {
        await using var dataSource = CreateDataSource(csb => csb.MaxPoolSize = 1);
        await using var conn1 = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        AssertPoolState(dataSource, open: 1, idle: 0);

        // Pool is exhausted
        await using (var conn2 = dataSource.CreateConnection())
        {
            var cts = new CancellationTokenSource(1000);
            var openTask = conn2.OpenAsync(cts.Token);
            AssertPoolState(dataSource, open: 1, idle: 0);
            var act = async () => await openTask;
            await act.Should().ThrowExactlyAsync<OperationCanceledException>();
        }

        AssertPoolState(dataSource, open: 1, idle: 0);
        await using (var conn2 = dataSource.CreateConnection())
        await using (new Timer(o => conn1.Close(), null, 1000, Timeout.Infinite))
        {
            await conn2.OpenAsync(TestContext.Current.CancellationToken);
            AssertPoolState(dataSource, open: 1, idle: 0);
        }
        AssertPoolState(dataSource, open: 1, idle: 1);
    }

    // Makes sure that when a pooled connection is closed it's properly reset, and that parameter settings aren't leaked
    [Fact]
    public async Task reset_on_close()
    {
        //Arrange
        await using var dataSource = CreateDataSource(csb => csb.SearchPath = "public");
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        ((string)await conn.ExecuteScalarAsync("SHOW search_path", cancellationToken: TestContext.Current.CancellationToken)).Should().NotContain("pg_temp");
        var backendId = conn.Connector.BackendProcessId;
        await conn.ExecuteNonQueryAsync("SET search_path=pg_temp", cancellationToken: TestContext.Current.CancellationToken);

        //Act
        await conn.CloseAsync();
        await conn.OpenAsync(TestContext.Current.CancellationToken);

        //Assert
        conn.Connector.BackendProcessId.Should().Be(backendId);
        (await conn.ExecuteScalarAsync("SHOW search_path", cancellationToken: TestContext.Current.CancellationToken)).Should().Be("public");
    }

    [Fact]
    public Task ConnectionPruningInterval_zero_throws()
        => Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await using var dataSource = CreateDataSource(csb => csb.ConnectionPruningInterval = 0);
        });

    [Fact]
    public Task ConnectionPruningInterval_bigger_than_ConnectionIdleLifetime_throws()
        => Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await using var dataSource = CreateDataSource(csb =>
            {
                csb.ConnectionIdleLifetime = 1;
                csb.ConnectionPruningInterval = 2;
            });
        });

    // Slow, and flaky under pressure, based on timing
    [Theory(Explicit = true)]
    [InlineData(0, 2, 1, 2)] // min pool size 0, sample twice
    [InlineData(1, 2, 1, 2)] // min pool size 1, sample twice
    [InlineData(2, 2, 1, 2)] // min pool size 2, sample twice
    [InlineData(2, 3, 2, 2)] // test rounding up, should sample twice.
    [InlineData(2, 1, 1, 1)] // test sample once.
    [InlineData(2, 20, 3, 7)] // test high samples.
    public async Task prune_idle_connectors(int minPoolSize, int connectionIdleLifeTime, int connectionPruningInterval, int samples)
    {
        await using var dataSource = CreateDataSource(csb =>
        {
            csb.MinPoolSize = minPoolSize;
            csb.ConnectionIdleLifetime = connectionIdleLifeTime;
            csb.ConnectionPruningInterval = connectionPruningInterval;
        });

        var connectionPruningIntervalMs = connectionPruningInterval * 1000;

        await using var conn1 = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using var conn2 = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using var conn3 = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        await conn1.CloseAsync();
        await conn2.CloseAsync();
        AssertPoolState(dataSource, open: 3, idle: 2);

        var paddingMs = 100; // 100ms
        var sleepInterval = connectionPruningIntervalMs + paddingMs;
        var total = 0;

        for (var i = 0; i < samples - 1; i++)
        {
            total += sleepInterval;
            Thread.Sleep(sleepInterval);
            // ConnectionIdleLifetime not yet reached.
            AssertPoolState(dataSource, open: 3, idle: 2);
        }

        // final cycle to do pruning.
        Thread.Sleep(Math.Max(sleepInterval, (connectionIdleLifeTime * 1000) - total));

        // ConnectionIdleLifetime reached, we still have one connection open minimum,
        // and as a result we have minPoolSize - 1 idle connections.
        AssertPoolState(dataSource, open: Math.Max(1, minPoolSize), idle: Math.Max(0, minPoolSize - 1));
    }

    // Timing-based
    [Fact(Explicit = true)]
    public async Task prune_counts_max_lifetime_exceeded()
    {
        await using var dataSource = CreateDataSource(csb =>
        {
            csb.MinPoolSize = 0;
            // Idle lifetime 2 seconds, 2 samples
            csb.ConnectionIdleLifetime = 2;
            csb.ConnectionPruningInterval = 1;
            csb.ConnectionLifetime = 5;
        });

        // conn1 will exceed max lifetime
        await using var conn1 = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        // make conn1 4 seconds older than the others, so it exceeds max lifetime
        Thread.Sleep(4000);

        await using var conn2 = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using var conn3 = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        await conn1.CloseAsync();
        await conn2.CloseAsync();
        AssertPoolState(dataSource, open: 3, idle: 2);

        // wait for 1 sample
        Thread.Sleep(1000);
        // ConnectionIdleLifetime not yet reached.
        AssertPoolState(dataSource, open: 3, idle: 2);

        // close conn3, so we can see if too many connectors get pruned
        await conn3.CloseAsync();

        // wait for last sample + a bit more time for reliability
        Thread.Sleep(1500);

        // ConnectionIdleLifetime reached
        // - conn1 should have been closed due to max lifetime (but this should count as pruning)
        // - conn2 or conn3 should have been closed due to idle pruning
        // - conn3 or conn2 should remain
        AssertPoolState(dataSource, open: 1, idle: 1);
    }

    // Makes sure that when a waiting async open is is given a connection, the continuation is executed in the TP rather than on the closing thread
    [Fact]
    public async Task Close_releases_waiter_on_another_thread()
    {
        //Arrange
        await using var dataSource = CreateDataSource(csb => csb.MaxPoolSize = 1);
        await using var conn1 = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken); // Pool is now exhausted

        AssertPoolState(dataSource, open: 1, idle: 0);

        // The thread currently inside conn1.Close(), or -1 when no Close() call is in progress. The waiter must be
        // released asynchronously: its continuation must never run inline on the thread that is closing conn1.
        // (Upstream blocked the closing thread until the waiter finished, which made "different thread" provable;
        // xUnit forbids that blocking wait, so the inline case is detected directly instead.)
        var threadInsideClose = -1;
        var ranInlineInsideClose = false;

        Func<Task> asyncOpener = async () =>
        {
            using (var conn2 = dataSource.CreateConnection())
            {
                await conn2.OpenAsync();
                ranInlineInsideClose = Volatile.Read(ref threadInsideClose) == Environment.CurrentManagedThreadId;
                AssertPoolState(dataSource, open: 1, idle: 0);
            }
            AssertPoolState(dataSource, open: 1, idle: 1);
        };

        // Start an async open which will not complete as the pool is exhausted.
        var asyncOpenerTask = asyncOpener();

        //Act
        Volatile.Write(ref threadInsideClose, Environment.CurrentManagedThreadId);
        conn1.Close();  // Complete the async open by closing conn1
        Volatile.Write(ref threadInsideClose, -1);
        await asyncOpenerTask;

        //Assert
        AssertPoolState(dataSource, open: 1, idle: 1);
        ranInlineInsideClose.Should().BeFalse("the waiter's continuation must not run synchronously inside Close()");
    }

    [Fact] //TODO: parallelize
    public async Task release_waiter_on_connection_failure()
    {
        //Arrange
        await using var dataSource = CreateDataSource(csb =>
        {
            csb.Port = 9999;
            csb.MaxPoolSize = 1;
        });

        //Act
        var tasks = Enumerable.Range(0, 2).Select(i => Task.Run(async () =>
        {
            await using var conn = await dataSource.OpenConnectionAsync();
        })).ToArray();
        var whenAll = Task.WhenAll(tasks);
        await Assert.ThrowsAsync<PgSqlException>(() => whenAll);

        //Assert
        var ex = whenAll.Exception;
        ex.InnerExceptions.Should().HaveCount(2);
        foreach (var inner in ex.InnerExceptions)
            inner.Should().BeOfType<PgSqlException>();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void ClearPool(int iterations)
    {
        var connString = new PgSqlConnectionStringBuilder(ConnectionString)
        {
            ApplicationName = nameof(ClearPool) + iterations
        }.ToString();

        PgSqlConnection conn = null;
        try
        {
            for (var i = 0; i < iterations; i++)
            {
                using (conn = new PgSqlConnection(connString))
                {
                    conn.Open();
                }

                // Now have one connection in the pool
                PoolManager.Pools.TryGetValue(connString, out var pool).Should().BeTrue();
                AssertPoolState(pool, open: 1, idle: 1);

                PgSqlConnection.ClearPool(conn);
                AssertPoolState(pool, open: 0, idle: 0);
            }
        }
        finally
        {
            if (conn is not null)
                PgSqlConnection.ClearPool(conn);
        }
    }

    [Fact]
    public void ClearPool_with_busy()
    {
        var connString = new PgSqlConnectionStringBuilder(ConnectionString)
        {
            ApplicationName = nameof(ClearPool_with_busy)
        }.ToString();

        var conn = new PgSqlConnection(connString);
        try
        {
            PgSqlDataSource pool;
            using (conn)
            {
                conn.Open();
                PgSqlConnection.ClearPool(conn);
                // conn is still busy but should get closed when returned to the pool

                PoolManager.Pools.TryGetValue(connString, out pool).Should().BeTrue();
                AssertPoolState(pool, open: 1, idle: 0);
            }

            AssertPoolState(pool, open: 0, idle: 0);
        }
        finally
        {
            PgSqlConnection.ClearPool(conn);
        }
    }

    [Fact]
    public void ClearPool_with_no_pool()
    {
        //Arrange
        var connString = new PgSqlConnectionStringBuilder(ConnectionString)
        {
            ApplicationName = nameof(ClearPool_with_no_pool)
        }.ToString();
        using var conn = new PgSqlConnection(connString);

        //Act
        PgSqlConnection.ClearPool(conn);
    }

    // https://github.com/npgsql/npgsql/commit/45e33ecef21f75f51a625c7b919a50da3ed8e920#r28239653
    [Fact]
    public void Open_physical_failure()
    {
        //Arrange
        using var dataSource = CreateDataSource(csb =>
        {
            csb.Port = 44444;
            csb.MaxPoolSize = 1;
        });
        using var conn = dataSource.CreateConnection();

        //Assert
        for (var i = 0; i < 1; i++)
        {
            var act = () => conn.Open();
            act.Should().ThrowExactly<PgSqlException>().WithInnerExceptionExactly<SocketException>();
        }
        AssertPoolState(dataSource, open: 0, idle: 0);
    }

    //[Fact(Explicit = true)]
    //[InlineData(10, 10, 30, true)]
    //[InlineData(10, 10, 30, false)]
    //[InlineData(10, 20, 30, true)]
    //[InlineData(10, 20, 30, false)]
    async Task exercise_pool(int maxPoolSize, int numTasks, int seconds, bool async)
    {
        await using var dataSource = CreateDataSource(csb => csb.MaxPoolSize = maxPoolSize);

        Console.WriteLine($"Spinning up {numTasks} parallel tasks for {seconds} seconds (MaxPoolSize={maxPoolSize})...");
        StopFlag = 0;
        var tasks = Enumerable.Range(0, numTasks).Select(i => Task.Run(async () =>
        {
            while (StopFlag == 0)
            {
                await using var conn = dataSource.CreateConnection();
                if (async)
                    await conn.OpenAsync();
                else
                    conn.Open();
            }
        })).ToArray();

        Thread.Sleep(seconds * 1000);
        Interlocked.Exchange(ref StopFlag, 1);
        Console.WriteLine("Stopped. Waiting for all tasks to stop...");
        Task.WaitAll(tasks);
        Console.WriteLine("Done");
    }

    [Fact]
    public async Task ConnectionLifetime()
    {
        //Arrange
        await using var dataSource = CreateDataSource(csb => csb.ConnectionLifetime = 1);
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        var processId = conn.ProcessID;
        await conn.CloseAsync();

        //Act
        await Task.Delay(2000, cancellationToken: TestContext.Current.CancellationToken);
        await conn.OpenAsync(TestContext.Current.CancellationToken);

        //Assert
        conn.ProcessID.Should().NotBe(processId);
    }

    #region Support

    volatile int StopFlag;

    void AssertPoolState(PgSqlDataSource pool, int open, int idle)
    {
        if (pool == null)
            throw new ArgumentNullException(nameof(pool));

        var (openState, idleState, _) = pool.Statistics;
        openState.Should().Be(open, $"Open should be {open} but is {openState}");
        idleState.Should().Be(idle, $"Idle should be {idle} but is {idleState}");
    }

    // With MaxPoolSize=1, opens many connections in parallel and executes a simple SELECT. Since there's only one
    // physical connection, all operations will be completely serialized
    [Fact]
    public async Task one_physical_connection_many_commands()
    {
        const int numParallelCommands = 10000;

        await using var dataSource = CreateDataSource(csb =>
        {
            csb.MaxPoolSize = 1;
            csb.MaxAutoPrepare = 5;
            csb.AutoPrepareMinUsages = 5;
            csb.Timeout = 0;
        });

        await Task.WhenAll(Enumerable.Range(0, numParallelCommands)
            .Select(async i =>
            {
                await using var conn = await dataSource.OpenConnectionAsync();
                await using var cmd = new PgSqlCommand("SELECT " + i, conn);
                var result = await cmd.ExecuteScalarAsync();
                result.Should().Be(i);
            }));
    }

    // When multiplexing, and the pool is totally saturated (at Max Pool Size and 0 idle connectors), we select
    // the connector with the least commands in flight and execute on it. We must never select a connector with
    // a pending transaction on it.
    // TODO: Test not tested
    [Fact(Skip = "Multiplexing: fails")]
    public async Task multiplexed_command_doesnt_get_executed_on_transactioned_connector()
    {
        await using var dataSource = CreateDataSource(csb =>
        {
            csb.MaxPoolSize = 1;
            csb.Timeout = 1;
        });

        await using var connWithTx = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using var tx = await connWithTx.BeginTransactionAsync(TestContext.Current.CancellationToken);
        // connWithTx should now be bound with the only physical connector available.
        // Any commands execute should timeout

        await using var conn2 = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using var cmd = new PgSqlCommand("SELECT 1", conn2);
        await Assert.ThrowsAsync<PgSqlException>(() => cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }

    #endregion
}
