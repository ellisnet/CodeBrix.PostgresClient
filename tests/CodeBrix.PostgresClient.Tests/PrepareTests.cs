using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.PostgresClient.BackendMessages;
using CodeBrix.PostgresClient.Internal.Postgres;
using CodeBrix.PostgresClient.PgSqlTypes;
using CodeBrix.PostgresClient.Tests.Support;
using SilverAssertions;
using Xunit;
using static CodeBrix.PostgresClient.Tests.TestUtil;

namespace CodeBrix.PostgresClient.Tests; //was previously: Npgsql.Tests;

public class PrepareTests: TestBase
{
    static uint Int4Oid => PostgresMinimalDatabaseInfo.DefaultTypeCatalog.GetOid(DataTypeNames.Int4).Value;

    [Fact]
    public void basic()
    {
        //Arrange
        using var conn = OpenConnectionAndUnprepare();
        using (var cmd = new PgSqlCommand("SELECT 1", conn))
        {
            AssertNumPreparedStatements(conn, 0);
            cmd.ExecuteScalar().Should().Be(1);
            cmd.IsPrepared.Should().BeFalse();

            //Act
            cmd.Prepare();

            //Assert
            AssertNumPreparedStatements(conn, 1);
            cmd.IsPrepared.Should().BeTrue();
            cmd.ExecuteScalar().Should().Be(1);
        }
        AssertNumPreparedStatements(conn, 1);
        conn.UnprepareAll();
    }

    [Fact]
    public async Task async()
    {
        //Arrange
        using var conn = OpenConnectionAndUnprepare();
        using (var cmd = new PgSqlCommand("SELECT 1", conn))
        {
            AssertNumPreparedStatements(conn, 0);
            cmd.ExecuteScalar().Should().Be(1);
            cmd.IsPrepared.Should().BeFalse();

            //Act
            await cmd.PrepareAsync(TestContext.Current.CancellationToken);

            //Assert
            AssertNumPreparedStatements(conn, 1);
            cmd.IsPrepared.Should().BeTrue();
            cmd.ExecuteScalar().Should().Be(1);
        }
        AssertNumPreparedStatements(conn, 1);
        conn.UnprepareAll();
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/3443")]
    public async Task bug_3443()
    {
        //Arrange
        using var conn = OpenConnectionAndUnprepare();
        using var cmd = new PgSqlCommand("SELECT 1", conn);
        AssertNumPreparedStatements(conn, 0);
        cmd.ExecuteScalar().Should().Be(1);
        cmd.IsPrepared.Should().BeFalse();

        //Act
        await Assert.ThrowsAsync<OperationCanceledException>(() => cmd.PrepareAsync(new(canceled: true)));

        //Assert
        AssertNumPreparedStatements(conn, 0);
        cmd.IsPrepared.Should().BeFalse();

        using var cmd2 = new PgSqlCommand("SELECT 1", conn);
        cmd2.Prepare();
        cmd2.ExecuteScalar().Should().Be(1);
        AssertNumPreparedStatements(conn, 1);
        cmd2.IsPrepared.Should().BeTrue();
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/4209")]
    public async Task async_cancel_null_reference_exception()
    {
        for (var i = 0; i < 10; i++)
        {
            //Arrange
            using var conn = OpenConnectionAndUnprepare();
            using var cmd = new PgSqlCommand("SELECT 1", conn);
            using var cts = new CancellationTokenSource();
            using var mre = new ManualResetEventSlim();
            var cancelTask = Task.Run(() =>
            {
                mre.Wait();
                cts.Cancel();
            }, cancellationToken: TestContext.Current.CancellationToken);

            //Act
            try
            {
                mre.Set();
                await cmd.PrepareAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                // There is a race between us checking the cancellation token and the cancellation itself.
                // If the cancellation happens first, we get OperationCancelledException.
                // In other case, PrepareAsync will not be cancelled and shouldn't throw any exceptions.
            }
            await cancelTask;

            //Assert
            conn.State.Should().Be(ConnectionState.Open);
        }
    }

    [Fact]
    public Task Unprepare()
        => UnprepareCore(false);

    [Fact]
    public Task UnprepareAsync()
        => UnprepareCore(true);

    async Task UnprepareCore(bool async)
    {
        //Arrange
        using var conn = OpenConnectionAndUnprepare();
        AssertNumPreparedStatements(conn, 0);
        using var cmd = new PgSqlCommand("SELECT 1", conn);
        if(async)
            await cmd.PrepareAsync();
        else
            cmd.Prepare();

        AssertNumPreparedStatements(conn, 1);

        //Act
        if (async)
            await cmd.UnprepareAsync();
        else
            cmd.Unprepare();

        //Assert
        AssertNumPreparedStatements(conn, 0);
        cmd.IsPrepared.Should().BeFalse();
        cmd.ExecuteScalar().Should().Be(1);
    }

    [Fact]
    public void named_parameters()
    {
        //Arrange
        using var conn = OpenConnectionAndUnprepare();

        for (var i = 0; i < 2; i++)
        {
            using var command = new PgSqlCommand("SELECT @a, @b", conn);
            command.Parameters.Add(new PgSqlParameter("a", DbType.Int32));
            command.Parameters.Add(new PgSqlParameter("b", 8));

            //Act
            command.Prepare();
            command.Parameters[0].Value = 3;
            command.Parameters[1].Value = 5;

            //Assert
            using (var reader = command.ExecuteReader())
            {
                reader.Read().Should().BeTrue();
                reader.GetInt32(0).Should().Be(3);
                reader.GetInt64(1).Should().Be(5);
            }

            command.Unprepare();
        }
    }

    [Fact]
    public void positional_parameters()
    {
        //Arrange
        using var conn = OpenConnectionAndUnprepare();

        for (var i = 0; i < 2; i++)
        {
            using var command = new PgSqlCommand("SELECT $1, $2", conn);
            command.Parameters.Add(new PgSqlParameter { DbType = DbType.Int32 });
            command.Parameters.Add(new PgSqlParameter { Value = 8 });

            //Act
            command.Prepare();
            command.Parameters[0].Value = 3;
            command.Parameters[1].Value = 5;

            //Assert
            using (var reader = command.ExecuteReader())
            {
                reader.Read().Should().BeTrue();
                reader.GetInt32(0).Should().Be(3);
                reader.GetInt64(1).Should().Be(5);
            }

            command.Unprepare();
        }
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/1207")]
    public void double_prepare_same_sql()
    {
        //Arrange
        using var conn = OpenConnectionAndUnprepare();
        using var cmd = new PgSqlCommand("SELECT 1", conn);

        //Act
        cmd.Prepare();
        cmd.Prepare();

        //Assert
        AssertNumPreparedStatements(conn, 1);
        cmd.Unprepare();
        AssertNumPreparedStatements(conn, 0);
    }

    [Fact]
    public void double_prepare_different_sql()
    {
        //Arrange
        using var conn = OpenConnectionAndUnprepare();
        using var cmd = new PgSqlCommand();
        cmd.Connection = conn;

        //Act
        cmd.CommandText = "SELECT 1";
        cmd.Prepare();
        cmd.ExecuteNonQuery();

        cmd.CommandText = "SELECT 2";
        cmd.Prepare();

        //Assert
        AssertNumPreparedStatements(conn, 2);
        cmd.ExecuteNonQuery();

        conn.UnprepareAll();
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/395")]
    public void across_close_open_same_connector()
    {
        //Arrange
        using var dataSource = CreateDataSource();
        using var conn = dataSource.OpenConnection();
        using var cmd = new PgSqlCommand("SELECT 1", conn);
        cmd.Prepare();
        cmd.IsPrepared.Should().BeTrue();
        var processId = conn.ProcessID;

        //Act
        conn.Close();
        conn.Open();

        //Assert
        processId.Should().Be(conn.ProcessID);
        cmd.IsPrepared.Should().BeTrue();
        cmd.ExecuteScalar().Should().Be(1);
        cmd.Prepare();
        cmd.ExecuteScalar().Should().Be(1);
    }

    [Fact]
    public void across_close_open_different_connector()
    {
        //Arrange
        using var dataSource = CreateDataSource();
        using var conn1 = dataSource.CreateConnection();
        using var conn2 = dataSource.CreateConnection();
        using var cmd = new PgSqlCommand("SELECT 1", conn1);
        conn1.Open();
        cmd.Prepare();
        cmd.IsPrepared.Should().BeTrue();
        var processId = conn1.ProcessID;

        //Act
        conn1.Close();
        conn2.Open();
        conn1.Open();

        //Assert
        conn1.ProcessID.Should().NotBe(processId);
        cmd.IsPrepared.Should().BeFalse();
        cmd.ExecuteScalar().Should().Be(1);  // Execute unprepared
        cmd.Prepare();
        cmd.ExecuteScalar().Should().Be(1);
    }

    [Fact]
    public void reuse_prepared_statement()
    {
        //Arrange
        using var dataSource = CreateDataSource();
        using var conn1 = dataSource.OpenConnection();
        var preparedStatement = Array.Empty<byte>();
        using (var cmd1 = new PgSqlCommand("SELECT @p", conn1))
        {
            cmd1.Parameters.AddWithValue("p", 8);
            cmd1.Prepare();
            cmd1.IsPrepared.Should().BeTrue();
            cmd1.ExecuteScalar().Should().Be(8);
            preparedStatement = cmd1.InternalBatchCommands[0].PreparedStatement.Name;
        }

        //Act
        using (var cmd2 = new PgSqlCommand("SELECT @p", conn1))
        {
            cmd2.Parameters.AddWithValue("p", 8);
            cmd2.Prepare();

            //Assert
            cmd2.IsPrepared.Should().BeTrue();
            cmd2.InternalBatchCommands[0].PreparedStatement.Name.Should().Equal(preparedStatement);
            cmd2.ExecuteScalar().Should().Be(8);
        }
    }

    [Fact]
    public void legacy_batching()
    {
        //Arrange
        using var conn = OpenConnectionAndUnprepare();

        using (var cmd = new PgSqlCommand("SELECT 1; SELECT 2", conn))
        {
            cmd.Prepare();
            using (var reader = cmd.ExecuteReader())
            {
                reader.Read();
                reader.GetInt32(0).Should().Be(1);
                reader.NextResult();
                reader.Read();
                reader.GetInt32(0).Should().Be(2);
            }
        }

        AssertNumPreparedStatements(conn, 2);

        using (var cmd = new PgSqlCommand("SELECT 1; SELECT 2", conn))
        {
            cmd.Prepare();
            using (var reader = cmd.ExecuteReader())
            {
                reader.Read();
                reader.GetInt32(0).Should().Be(1);
                reader.NextResult();
                reader.Read();
                reader.GetInt32(0).Should().Be(2);
            }
        }

        AssertNumPreparedStatements(conn, 2);
        conn.UnprepareAll();
    }

    [Fact]
    public void batch()
    {
        //Arrange
        using var conn = OpenConnectionAndUnprepare();

        using (var batch = new PgSqlBatch(conn) { BatchCommands = { new("SELECT 1"), new("SELECT 2") } })
        {
            batch.Prepare();
            using (var reader = batch.ExecuteReader())
            {
                reader.Read();
                reader.GetInt32(0).Should().Be(1);
                reader.NextResult();
                reader.Read();
                reader.GetInt32(0).Should().Be(2);
            }
        }

        using (var cmd = new PgSqlCommand("SELECT 1; SELECT 2", conn))
        {
            cmd.Prepare();
            using (var reader = cmd.ExecuteReader())
            {
                reader.Read();
                reader.GetInt32(0).Should().Be(1);
                reader.NextResult();
                reader.Read();
                reader.GetInt32(0).Should().Be(2);
            }
        }

        AssertNumPreparedStatements(conn, 2);

        using (var cmd = new PgSqlCommand("SELECT 1; SELECT 2", conn))
        {
            cmd.Prepare();
            using (var reader = cmd.ExecuteReader())
            {
                reader.Read();
                reader.GetInt32(0).Should().Be(1);
                reader.NextResult();
                reader.Read();
                reader.GetInt32(0).Should().Be(2);
            }
        }

        AssertNumPreparedStatements(conn, 2);
        conn.UnprepareAll();
    }

    [Fact]
    public void one_command_same_sql_twice()
    {
        //Arrange
        using var conn = OpenConnectionAndUnprepare();
        using var cmd = new PgSqlCommand("SELECT 1; SELECT 1", conn);

        //Act
        cmd.Prepare();

        //Assert
        AssertNumPreparedStatements(conn, 1);
        cmd.ExecuteNonQuery();
        cmd.Unprepare();
    }

    [Fact]
    public void one_command_same_sql_auto_prepare()
    {
        //Arrange
        using var dataSource = CreateDataSource(csb =>
        {
            csb.MaxAutoPrepare = 5;
            csb.AutoPrepareMinUsages = 2;
        });
        using var conn = dataSource.OpenConnection();
        var sql = new StringBuilder();
        for (var i = 0; i < 2 + 1; i++)
            sql.Append("SELECT 1;");

        //Act
        using (var cmd = new PgSqlCommand(sql.ToString(), conn))
            cmd.ExecuteNonQuery();

        //Assert
        AssertNumPreparedStatements(conn, 1);
    }

    [Fact]
    public void one_command_same_sql_twice_with_params()
    {
        //Arrange
        using var conn = OpenConnectionAndUnprepare();
        using var cmd = new PgSqlCommand("SELECT @p1; SELECT @p2", conn);
        cmd.Parameters.Add("p1", PgSqlDbType.Integer);
        cmd.Parameters.Add("p2", PgSqlDbType.Integer);

        //Act
        cmd.Prepare();

        //Assert
        AssertNumPreparedStatements(conn, 1);

        cmd.Parameters[0].Value = 8;
        cmd.Parameters[1].Value = 9;
        using (var reader = cmd.ExecuteReader())
        {
            reader.Read().Should().BeTrue();
            reader.GetInt32(0).Should().Be(8);
            reader.NextResult().Should().BeTrue();
            reader.Read().Should().BeTrue();
            reader.GetInt32(0).Should().Be(9);
            reader.NextResult().Should().BeFalse();
        }

        cmd.Unprepare();
    }

    [Fact]
    public void unprepare_via_different_command()
    {
        //Arrange
        using var conn = OpenConnectionAndUnprepare();
        using var cmd1 = new PgSqlCommand("SELECT 1; SELECT 2", conn);
        using var cmd2 = new PgSqlCommand("SELECT 2; SELECT 3", conn);
        cmd1.Prepare();
        cmd2.Prepare();
        // Both commands reference the same prepared statement
        AssertNumPreparedStatements(conn, 3);

        //Act
        cmd2.Unprepare();

        //Assert
        AssertNumPreparedStatements(conn, 1);
        cmd1.IsPrepared.Should().BeFalse();  // Only partially prepared, so no
        cmd1.ExecuteNonQuery();
        cmd1.Unprepare();
        AssertNumPreparedStatements(conn, 0);
        cmd1.IsPrepared.Should().BeFalse();
        cmd1.ExecuteNonQuery();

        conn.UnprepareAll();
    }

    // Prepares the same SQL with different parameters (overloading)
    [Fact]
    public void overloaded_sql()
    {
        //Arrange
        using var conn = OpenConnectionAndUnprepare();

        using (var cmd = new PgSqlCommand("SELECT @p", conn))
        {
            cmd.Parameters.Add("p", PgSqlDbType.Integer);
            cmd.Prepare();
            cmd.IsPrepared.Should().BeTrue();
        }
        using (var cmd = new PgSqlCommand("SELECT @p", conn))
        {
            cmd.Parameters.AddWithValue("p", PgSqlDbType.Text, "foo");
            cmd.Prepare();
            cmd.ExecuteScalar().Should().Be("foo");
            cmd.IsPrepared.Should().BeFalse();
        }

        // SQL overloading is a pretty rare/exotic scenario. Handling it properly would involve keying
        // prepared statements not just by SQL but also by the parameter types, which would pointlessly
        // increase allocations. Instead, the second execution simply reruns unprepared
        AssertNumPreparedStatements(conn, 1);
        conn.UnprepareAll();
    }

    [Fact]
    public void many_statements_on_unprepare()
    {
        //Arrange
        using var conn = OpenConnectionAndUnprepare();
        using var cmd = new PgSqlCommand();
        cmd.Connection = conn;
        var sb = new StringBuilder();
        for (var i = 0; i < conn.Settings.WriteBufferSize; i++)
            sb.Append("SELECT 1;");
        cmd.CommandText = sb.ToString();
        cmd.Prepare();

        //Act
        cmd.Unprepare();
    }

    [Fact]
    public void IsPrepared_is_false_after_changing_CommandText()
    {
        //Arrange
        using var conn = OpenConnectionAndUnprepare();
        using var cmd = new PgSqlCommand("SELECT 1", conn);
        cmd.Prepare();
        AssertNumPreparedStatements(conn, 1);

        //Act
        cmd.CommandText = "SELECT 2";

        //Assert
        cmd.IsPrepared.Should().BeFalse();
        cmd.ExecuteNonQuery();
        cmd.IsPrepared.Should().BeFalse();
        AssertNumPreparedStatements(conn, 1);
        cmd.Unprepare();
    }

    // Basic persistent prepared system scenario. Checks that statement is not deallocated in the backend after command dispose.
    [Fact]
    public void persistent_across_commands()
    {
        //Arrange
        using var conn = OpenConnectionAndUnprepare();
        AssertNumPreparedStatements(conn, 0);

        //Act
        using (var cmd = new PgSqlCommand("SELECT 1", conn))
        {
            cmd.Prepare();
            AssertNumPreparedStatements(conn, 1);
            cmd.ExecuteScalar().Should().Be(1);
        }

        //Assert
        AssertNumPreparedStatements(conn, 1);

        var stmtName = GetPreparedStatements(conn).Single();

        // Rerun the test using the persistent prepared statement
        using (var cmd = new PgSqlCommand("SELECT 1", conn))
        {
            cmd.Prepare();
            cmd.IsPrepared.Should().BeTrue();
            AssertNumPreparedStatements(conn, 1);
            cmd.ExecuteScalar().Should().Be(1);
        }
        AssertNumPreparedStatements(conn, 1);
        GetPreparedStatements(conn).Single().Should().Be(stmtName);
        conn.UnprepareAll();
    }

    // Basic persistent prepared system scenario. Checks that statement is not deallocated in the backend after connection close.
    [Fact]
    public void persistent_across_connections()
    {
        //Arrange
        using var dataSource = CreateDataSource();
        using var conn = dataSource.OpenConnection();
        var processId = conn.ProcessID;

        AssertNumPreparedStatements(conn, 0);
        using (var cmd = new PgSqlCommand("SELECT 1", conn))
            cmd.Prepare();

        var stmtName = GetPreparedStatements(conn).Single();

        //Act
        conn.Close();
        conn.Open();

        //Assert
        conn.ProcessID.Should().Be(processId, "the same connection should be received from the pool");

        AssertNumPreparedStatements(conn, 1, "the prepared statement should not be deallocated");
        GetPreparedStatements(conn).Single().Should().Be(stmtName, "the prepared statement name should not change");

        // Rerun the test using the persistent prepared statement
        using (var cmd = new PgSqlCommand("SELECT 1", conn))
        {
            cmd.Prepare();
            cmd.ExecuteScalar().Should().Be(1);
        }
        AssertNumPreparedStatements(conn, 1, "the prepared statement should not be deallocated");
        GetPreparedStatements(conn).Single().Should().Be(stmtName, "the prepared statement name should not change");
    }

    // Makes sure that calling Prepare() twice on a command does not deallocate or make a new one after the first prepared statement when command does not change
    [Fact]
    public void persistent_double_prepare_command_unchanged()
    {
        //Arrange
        using var conn = OpenConnectionAndUnprepare();
        using (var cmd = new PgSqlCommand("SELECT 1", conn))
        {
            cmd.Prepare();
            cmd.ExecuteNonQuery();
            var stmtName = GetPreparedStatements(conn).Single();

            //Act
            cmd.Prepare();
            cmd.ExecuteNonQuery();

            //Assert
            AssertNumPreparedStatements(conn, 1, "there should be exactly one prepared statement");
            GetPreparedStatements(conn).Single().Should().Be(stmtName, "the persistent prepared statement name should not change");
        }
        AssertNumPreparedStatements(conn, 1, "the persistent prepared statement should not be deallocated");
        conn.UnprepareAll();
    }

    [Fact]
    public void persistent_double_prepare_command_changed()
    {
        //Arrange
        using var conn = OpenConnectionAndUnprepare();
        using (var cmd = new PgSqlCommand("SELECT 1", conn))
        {
            cmd.Prepare();
            cmd.ExecuteNonQuery();
            cmd.CommandText = "SELECT 2";
            AssertNumPreparedStatements(conn, 1);

            //Act
            cmd.Prepare();

            //Assert
            AssertNumPreparedStatements(conn, 2);
            cmd.ExecuteNonQuery();
        }
        AssertNumPreparedStatements(conn, 2);
        conn.UnprepareAll();
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/2665")]
    public void prepared_command_failure()
    {
        //Arrange
        using var conn = OpenConnection();

        using (var command = new PgSqlCommand("INSERT INTO test_table (id) VALUES (1)", conn))
            Assert.Throws<PostgresException>(() => command.Prepare());

        conn.ExecuteNonQuery("CREATE TEMP TABLE test_table (id integer)");

        using (var command = new PgSqlCommand("INSERT INTO test_table (id) VALUES (1)", conn))
        {
            command.Prepare();
            command.ExecuteNonQuery();
        }
    }

    /*
    [Test]
    public void Unpersist()
    {
        using (var conn = OpenConnectionAndUnprepare())
        {
            using (var cmd = new PgSqlCommand("SELECT 1", conn))
                cmd.Prepare(true);

            // Unpersist via a different command
            using (var cmd = new PgSqlCommand("SELECT 1", conn))
            {
                cmd.Prepare(true);
                cmd.Unpersist();
                AssertNumPreparedStatements(conn, 0);
            }

            // Repersist
            using (var cmd = new PgSqlCommand("SELECT 1", conn))
            {
                cmd.Prepare(true);
                Assert.That(cmd.ExecuteScalar(), Is.EqualTo(1));
                cmd.Unpersist();
                AssertNumPreparedStatements(conn, 0);
            }

            // Unpersist via an unprepared command
            using (var cmd = new PgSqlCommand("SELECT 1", conn))
                cmd.Prepare(true);
            using (var cmd = new PgSqlCommand("SELECT 1", conn))
                cmd.Unpersist();
            AssertNumPreparedStatements(conn, 0);

            // Unpersist via a prepared but unpersisted command
            using (var cmd = new PgSqlCommand("SELECT 1", conn))
                cmd.Prepare(true);
            using (var cmd = new PgSqlCommand("SELECT 1", conn))
            {
                cmd.Prepare(false);
                cmd.Unpersist();
            }
            AssertNumPreparedStatements(conn, 0);
        }
    }

    [Test]
    public void Same_sql_different_params()
    {
        using (var conn = OpenConnectionAndUnprepare())
        using (var cmd = new PgSqlCommand("SELECT @p", conn))
        {
            throw new NotImplementedException("Problem: current setting PgSqlParameter.Value clears/invalidates...");
            cmd.Parameters.Add(new PgSqlParameter("p", PgSqlDbType.Integer));
            cmd.Prepare(true);

            cmd.Parameters[0].PgSqlDbType = PgSqlDbType.Text;
            Assert.That(cmd.IsPrepared, Is.False);
            cmd.Prepare(true);
            using (var crapCmd = new PgSqlCommand("SELECT name,statement,parameter_types::TEXT[] FROM pg_prepared_statements", conn))
            using (var reader = crapCmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    Console.WriteLine($"Statement: {reader.GetString(0)}, {reader.GetString(1)}");
                    foreach (var p in reader.GetFieldValue<string[]>(2))
                    {
                        Console.WriteLine("  Param: " + p);
                    }
                }
            }
            //AssertNumPreparedStatements(conn, 2);
            cmd.Parameters[0].Value = "hello";
            Console.WriteLine(cmd.ExecuteScalar());
        }
    }
    */

    [Fact]
    public void invalid_statement()
    {
        //Arrange
        using var conn = OpenConnection();
        var cmd = new PgSqlCommand("sele", conn);

        //Act
        var act = () => cmd.Prepare();

        //Assert
        act.Should().ThrowExactly<PostgresException>();
    }

    [Fact]
    public void Prepare_multiple_commands_with_parameters()
    {
        //Arrange
        using var conn = OpenConnection();
        using var cmd1 = new PgSqlCommand("SELECT @p1;", conn);
        using var cmd2 = new PgSqlCommand("SELECT @p1; SELECT @p2;", conn);
        var p1 = new PgSqlParameter("p1", PgSqlDbType.Integer);
        var p21 = new PgSqlParameter("p1", PgSqlDbType.Text);
        var p22 = new PgSqlParameter("p2", PgSqlDbType.Text);
        cmd1.Parameters.Add(p1);
        cmd2.Parameters.Add(p21);
        cmd2.Parameters.Add(p22);

        //Act
        cmd1.Prepare();
        cmd2.Prepare();
        p1.Value = 8;
        p21.Value = "foo";
        p22.Value = "bar";

        //Assert
        using (var reader1 = cmd1.ExecuteReader())
        {
            reader1.Read().Should().BeTrue();
            reader1.GetInt32(0).Should().Be(8);
        }
        using (var reader2 = cmd2.ExecuteReader())
        {
            reader2.Read().Should().BeTrue();
            reader2.GetString(0).Should().Be("foo");
            reader2.NextResult().Should().BeTrue();
            reader2.Read().Should().BeTrue();
            reader2.GetString(0).Should().Be("bar");
        }
    }

    [Fact]
    public void multiplexing_not_supported()
    {
        //Arrange
        using var dataSource = CreateDataSource(csb => csb.Multiplexing = true);
        using var conn = dataSource.OpenConnection();
        using var cmd = new PgSqlCommand("SELECT 1", conn);

        Assert.Throws<NotSupportedException>(() => cmd.Prepare());
        Assert.Throws<NotSupportedException>(() => conn.UnprepareAll());
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task explicitly_prepared_statement_invalidation(bool prepareAfterError, bool unprepareAfterError)
    {
        //Arrange
        await using var dataSource = CreateDataSource(csb =>
        {
            csb.MaxAutoPrepare = 10;
            csb.AutoPrepareMinUsages = 2;
        });
        await using var connection = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        var table = await CreateTempTable(connection, "foo int");

        await using var command = new PgSqlCommand($"SELECT * FROM {table}", connection);
        await command.PrepareAsync(TestContext.Current.CancellationToken);

        //Act
        await connection.ExecuteNonQueryAsync($"ALTER TABLE {table} RENAME COLUMN foo TO bar", cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        // Since we've changed the table schema, the next execution of the prepared statement will error with 0A000
        var exception = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
        exception.SqlState.Should().Be(PostgresErrorCodes.FeatureNotSupported); // cached plan must not change result type
        command.IsPrepared.Should().BeFalse();

        if (unprepareAfterError)
        {
            // Just check that calling unprepare after error doesn't break anything
            await command.UnprepareAsync(TestContext.Current.CancellationToken);
            command.IsPrepared.Should().BeFalse();
        }

        if (prepareAfterError)
        {
            // If we explicitly prepare after error, we should replace the previous prepared statement with a new one
            await command.PrepareAsync(TestContext.Current.CancellationToken);
            command.IsPrepared.Should().BeTrue();
        }

        // However, CodeBrix.PostgresClient should invalidate the prepared statement in this case, so the next execution should work
        await FluentActions.Awaiting(() => command.ExecuteNonQueryAsync()).Should().NotThrowAsync();

        if (!prepareAfterError)
        {
            // The command is unprepared, though. It's the user's responsibility to re-prepare if they wish.
            command.IsPrepared.Should().BeFalse();
        }
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/4920")]
    public async Task explicit_prepare_unprepare_many_queries()
    {
        //Arrange
        // Set a specific buffer's size to trigger #4920
        await using var dataSource = CreateDataSource(csb => csb.WriteBufferSize = 5002);
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = string.Join(';', Enumerable.Range(1, 500).Select(x => $"SELECT {x}"));

        //Act
        await cmd.PrepareAsync(TestContext.Current.CancellationToken);
        await cmd.UnprepareAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task explicitly_prepared_batch_sends_prepared_queries()
    {
        //Arrange
        await using var postmaster = PgPostmasterMock.Start(ConnectionString);
        await using var dataSource = CreateDataSource(postmaster.ConnectionString);

        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        var server = await postmaster.WaitForServerConnection();

        await using var batch = new PgSqlBatch(conn)
        {
            BatchCommands = { new("SELECT 1"), new("SELECT 2") }
        };

        //Act
        var prepareTask = batch.PrepareAsync(TestContext.Current.CancellationToken);

        //Assert
        await server.ExpectMessages(
            FrontendMessageCode.Parse, FrontendMessageCode.Describe,
            FrontendMessageCode.Parse, FrontendMessageCode.Describe,
            FrontendMessageCode.Sync);

        await server
            .WriteParseComplete()
            .WriteParameterDescription(new FieldDescription(Int4Oid))
            .WriteRowDescription(new FieldDescription(Int4Oid))
            .WriteParseComplete()
            .WriteParameterDescription(new FieldDescription(Int4Oid))
            .WriteRowDescription(new FieldDescription(Int4Oid))
            .WriteReadyForQuery()
            .FlushAsync();

        await prepareTask;

        for (var i = 0; i < 2; i++)
            await ExecutePreparedBatch(batch, server);

        async Task ExecutePreparedBatch(PgSqlBatch batch, PgServerMock server)
        {
            var executeBatchTask = batch.ExecuteNonQueryAsync();

            await server.ExpectMessages(
                FrontendMessageCode.Bind, FrontendMessageCode.Execute,
                FrontendMessageCode.Bind, FrontendMessageCode.Execute,
                FrontendMessageCode.Sync);

            await server
                .WriteBindComplete()
                .WriteCommandComplete()
                .WriteBindComplete()
                .WriteCommandComplete()
                .WriteReadyForQuery()
                .FlushAsync();

            await executeBatchTask;
        }
    }

    [Fact]
    public async Task auto_prepared_batch_sends_prepared_queries()
    {
        //Arrange
        var csb = new PgSqlConnectionStringBuilder(ConnectionString)
        {
            AutoPrepareMinUsages = 1,
            MaxAutoPrepare = 10
        };
        await using var postmaster = PgPostmasterMock.Start(csb.ConnectionString);
        await using var dataSource = CreateDataSource(postmaster.ConnectionString);

        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        var server = await postmaster.WaitForServerConnection();

        await using var batch = new PgSqlBatch(conn)
        {
            BatchCommands = { new("SELECT 1"), new("SELECT 2") }
        };

        //Act
        var firstBatchExecuteTask = batch.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);

        //Assert
        await server.ExpectMessages(
            FrontendMessageCode.Parse, FrontendMessageCode.Bind, FrontendMessageCode.Describe, FrontendMessageCode.Execute,
            FrontendMessageCode.Parse, FrontendMessageCode.Bind, FrontendMessageCode.Describe, FrontendMessageCode.Execute,
            FrontendMessageCode.Sync);

        await server
            .WriteParseComplete()
            .WriteBindComplete()
            .WriteRowDescription(new FieldDescription(Int4Oid))
            .WriteCommandComplete()
            .WriteParseComplete()
            .WriteBindComplete()
            .WriteRowDescription(new FieldDescription(Int4Oid))
            .WriteCommandComplete()
            .WriteReadyForQuery()
            .FlushAsync();

        await firstBatchExecuteTask;

        for (var i = 0; i < 2; i++)
            await ExecutePreparedBatch(batch, server);

        async Task ExecutePreparedBatch(PgSqlBatch batch, PgServerMock server)
        {
            var executeBatchTask = batch.ExecuteNonQueryAsync();

            await server.ExpectMessages(
                FrontendMessageCode.Bind, FrontendMessageCode.Execute,
                FrontendMessageCode.Bind, FrontendMessageCode.Execute,
                FrontendMessageCode.Sync);

            await server
                .WriteBindComplete()
                .WriteCommandComplete()
                .WriteBindComplete()
                .WriteCommandComplete()
                .WriteReadyForQuery()
                .FlushAsync();

            await executeBatchTask;
        }
    }

    PgSqlConnection OpenConnectionAndUnprepare()
    {
        var conn = OpenConnection();
        conn.UnprepareAll();
        return conn;
    }

    void AssertNumPreparedStatements(PgSqlConnection conn, int expected)
        => conn.ExecuteScalar("SELECT COUNT(*) FROM pg_prepared_statements WHERE statement NOT LIKE '%FROM pg_prepared_statements%'").Should().Be((long)expected);

    void AssertNumPreparedStatements(PgSqlConnection conn, int expected, string message)
        => conn.ExecuteScalar("SELECT COUNT(*) FROM pg_prepared_statements WHERE statement NOT LIKE '%FROM pg_prepared_statements%'").Should().Be((long)expected, message);

    List<string> GetPreparedStatements(PgSqlConnection conn)
    {
        var statements = new List<string>();
        using var cmd = new PgSqlCommand("SELECT name FROM pg_prepared_statements WHERE statement NOT LIKE '%FROM pg_prepared_statement%'", conn);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            statements.Add(reader.GetString(0));
        return statements;
    }
}
