using System;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using CodeBrix.PostgresClient.PgSqlTypes;
using SilverAssertions;
using Xunit;
using static CodeBrix.PostgresClient.Tests.TestUtil;

namespace CodeBrix.PostgresClient.Tests; //was previously: Npgsql.Tests;

public class AutoPrepareTests : TestBase
{
    [Fact]
    public void basic()
    {
        //Arrange
        using var dataSource = CreateDataSource(csb =>
        {
            csb.MaxAutoPrepare = 10;
            csb.AutoPrepareMinUsages = 2;
        });
        using var conn = dataSource.OpenConnection();
        using var checkCmd = new PgSqlCommand(CountPreparedStatements, conn);
        checkCmd.Prepare();

        conn.ExecuteNonQuery("SELECT 1");
        checkCmd.ExecuteScalar().Should().Be(0L);

        using (var cmd = new PgSqlCommand("SELECT 1", conn))
        {
            cmd.IsPrepared.Should().BeFalse();
            cmd.ExecuteScalar();
            cmd.IsPrepared.Should().BeTrue();
            checkCmd.ExecuteScalar().Should().Be(1L);
            cmd.ExecuteScalar();
            cmd.IsPrepared.Should().BeTrue();
            checkCmd.ExecuteScalar().Should().Be(1L);
        }

        using (var cmd = new PgSqlCommand("SELECT 1", conn))
        {
            cmd.ExecuteScalar();
            cmd.IsPrepared.Should().BeTrue();
        }
        checkCmd.ExecuteScalar().Should().Be(1L);
    }

    // Passes the maximum limit for autoprepared statements, recycling the least-recently used one
    [Fact]
    public void recycle()
    {
        //Arrange
        using var dataSource = CreateDataSource(csb =>
        {
            csb.AutoPrepareMinUsages = 2;
            csb.MaxAutoPrepare = 2;
        });
        using var conn = dataSource.OpenConnection();
        using var checkCmd = new PgSqlCommand(CountPreparedStatements, conn);
        checkCmd.Prepare();

        checkCmd.ExecuteScalar().Should().Be(0L);
        var cmd1 = new PgSqlCommand("SELECT 1", conn);
        cmd1.ExecuteNonQuery(); cmd1.ExecuteNonQuery();
        cmd1.IsPrepared.Should().BeTrue();
        checkCmd.ExecuteScalar().Should().Be(1L);

        var cmd2 = new PgSqlCommand("SELECT 2", conn);
        cmd2.ExecuteNonQuery(); cmd2.ExecuteNonQuery();
        cmd2.IsPrepared.Should().BeTrue();
        checkCmd.ExecuteScalar().Should().Be(2L);

        cmd1.ExecuteNonQuery();

        //Act
        // Cause another statement to be autoprepared. This should eject cmd2.
        conn.ExecuteNonQuery("SELECT 3"); conn.ExecuteNonQuery("SELECT 3");

        //Assert
        checkCmd.ExecuteScalar().Should().Be(2L);

        cmd2.ExecuteNonQuery();
        cmd2.IsPrepared.Should().BeFalse();
        using (var getTextCmd = new PgSqlCommand("SELECT statement FROM pg_prepared_statements WHERE statement NOT LIKE '%COUNT%' ORDER BY statement", conn))
        using (var reader = getTextCmd.ExecuteReader())
        {
            reader.Read().Should().BeTrue();
            reader.GetString(0).Should().Be("SELECT 1");
            reader.Read().Should().BeTrue();
            reader.GetString(0).Should().Be("SELECT 3");
        }
    }

    [Fact]
    public void persist()
    {
        //Arrange
        using var dataSource = CreateDataSource(csb =>
        {
            csb.MaxAutoPrepare = 10;
            csb.AutoPrepareMinUsages = 2;
        });

        //Act
        using (var conn = dataSource.OpenConnection())
        using (var checkCmd = new PgSqlCommand(CountPreparedStatements, conn))
        {
            checkCmd.Prepare();
            conn.ExecuteNonQuery("SELECT 1"); conn.ExecuteNonQuery("SELECT 1");
            checkCmd.ExecuteScalar().Should().Be(1L);
        }

        //Assert
        // We now have two prepared statements which should be persisted

        using (var conn = dataSource.OpenConnection())
        using (var checkCmd = new PgSqlCommand(CountPreparedStatements, conn))
        {
            checkCmd.Prepare();
            checkCmd.ExecuteScalar().Should().Be(1L);
            using (var cmd = new PgSqlCommand("SELECT 1", conn))
            {
                cmd.ExecuteScalar();
                //cmd.IsPrepared.Should().BeTrue();
            }
            checkCmd.ExecuteScalar().Should().Be(1L);
        }
    }

    [Fact]
    public async Task positional_parameter()
    {
        //Arrange
        await using var dataSource = CreateDataSource(csb =>
        {
            csb.AutoPrepareMinUsages = 2;
            csb.MaxAutoPrepare = 2;
        });
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using var checkCmd = new PgSqlCommand(CountPreparedStatements, conn);
        await checkCmd.PrepareAsync(TestContext.Current.CancellationToken);

        await using var cmd = new PgSqlCommand("SELECT $1", conn);
        cmd.Parameters.Add(new PgSqlParameter { PgSqlDbType = PgSqlDbType.Integer, Value = 8 });

        cmd.IsPrepared.Should().BeFalse();
        (await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken)).Should().Be(8);
        cmd.IsPrepared.Should().BeFalse();
        (await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken)).Should().Be(8);
        cmd.IsPrepared.Should().BeTrue();
        (await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken)).Should().Be(8);
        cmd.IsPrepared.Should().BeTrue();
    }

    [Fact]
    public void promote_auto_to_explicit()
    {
        //Arrange
        using var dataSource = CreateDataSource(csb =>
        {
            csb.MaxAutoPrepare = 10;
            csb.AutoPrepareMinUsages = 2;
        });
        using var conn = dataSource.OpenConnection();
        using var checkCmd = new PgSqlCommand(CountPreparedStatements, conn);
        using var cmd1 = new PgSqlCommand("SELECT 1", conn);
        using var cmd2 = new PgSqlCommand("SELECT 1", conn);
        checkCmd.Prepare();

        cmd1.ExecuteNonQuery(); cmd1.ExecuteNonQuery();
        // cmd1 is now autoprepared
        checkCmd.ExecuteScalar().Should().Be(1L);
        conn.Connector.PreparedStatementManager.NumPrepared.Should().Be(2);

        //Act
        // Promote (replace) the autoprepared statement with an explicit one.
        cmd2.Prepare();

        //Assert
        checkCmd.ExecuteScalar().Should().Be(1L);
        conn.Connector.PreparedStatementManager.NumPrepared.Should().Be(2);

        // cmd1's statement is no longer valid (has been closed), make sure it still works (will run unprepared)
        cmd2.ExecuteScalar();

        // Trigger autoprepare on a different query to confirm we didn't leave replaced statement in a bad state
        using var cmd3 = new PgSqlCommand("SELECT 2", conn);
        cmd3.ExecuteNonQuery(); cmd3.ExecuteNonQuery();
    }

    [Fact]
    public void candidate_eject()
    {
        //Arrange
        using var dataSource = CreateDataSource(csb =>
        {
            csb.MaxAutoPrepare = 10;
            csb.AutoPrepareMinUsages = 3;
        });
        using var conn = dataSource.OpenConnection();
        using var cmd = conn.CreateCommand();

        //Act
        for (var i = 0; i < PreparedStatementManager.CandidateCount; i++)
        {
            cmd.CommandText = $"SELECT {i}";
            cmd.ExecuteNonQuery();
        }

        // The candidate list is now full with single-use statements.

        cmd.CommandText = "SELECT 'double_use'";
        cmd.ExecuteNonQuery(); cmd.ExecuteNonQuery();
        // We now have a single statement that has been used twice.

        for (var i = PreparedStatementManager.CandidateCount; i < PreparedStatementManager.CandidateCount * 2; i++)
        {
            cmd.CommandText = $"SELECT {i}";
            cmd.ExecuteNonQuery();
        }

        //Assert
        // The new single-use statements should have ejected all previous single-use statements
        cmd.CommandText = "SELECT 1";
        cmd.ExecuteNonQuery(); cmd.ExecuteNonQuery();
        cmd.IsPrepared.Should().BeFalse();

        // But the double-use statement should still be there
        cmd.CommandText = "SELECT 'double_use'";
        cmd.ExecuteNonQuery();
        cmd.IsPrepared.Should().BeTrue();
    }

    [Fact]
    public void one_command_same_sql_twice()
    {
        //Arrange
        using var dataSource = CreateDataSource(csb =>
        {
            csb.MaxAutoPrepare = 10;
            csb.AutoPrepareMinUsages = 2;
        });
        using var conn = dataSource.OpenConnection();
        using var cmd = new PgSqlCommand("SELECT 1; SELECT 1; SELECT 1; SELECT 1", conn);
        //cmd.Prepare();
        //cmd.IsPrepared.Should().BeTrue();

        //Act
        cmd.ExecuteNonQuery();

        //Assert
        conn.ExecuteScalar(CountPreparedStatements).Should().Be(1L);
    }

    [Fact]
    public void across_close_open_different_connector()
    {
        //Arrange
        using var dataSource = CreateDataSource(csb =>
        {
            csb.MaxAutoPrepare = 10;
            csb.AutoPrepareMinUsages = 2;
        });
        using var conn1 = dataSource.CreateConnection();
        using var conn2 = dataSource.CreateConnection();
        using var cmd = new PgSqlCommand("SELECT 1", conn1);
        conn1.Open();
        cmd.ExecuteNonQuery(); cmd.ExecuteNonQuery();
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
    public void unprepare_all()
    {
        //Arrange
        using var dataSource = CreateDataSource(csb =>
        {
            csb.MaxAutoPrepare = 10;
            csb.AutoPrepareMinUsages = 2;
        });
        using var conn = dataSource.OpenConnection();
        using var cmd = new PgSqlCommand("SELECT 1", conn);
        cmd.Prepare();  // Explicit
        conn.ExecuteNonQuery("SELECT 2"); conn.ExecuteNonQuery("SELECT 2");  // Auto
        conn.ExecuteScalar(CountPreparedStatements).Should().Be(2L);

        //Act
        conn.UnprepareAll();

        //Assert
        conn.ExecuteScalar(CountPreparedStatements).Should().Be(0L);
    }

    // Prepares the same SQL with different parameters (overloading)
    [Fact]
    public void overloaded_sql()
    {
        //Arrange
        using var dataSource = CreateDataSource(csb =>
        {
            csb.MaxAutoPrepare = 10;
            csb.AutoPrepareMinUsages = 2;
        });
        using var conn = dataSource.OpenConnection();

        using (var cmd = new PgSqlCommand("SELECT @p", conn))
        {
            cmd.Parameters.AddWithValue("p", PgSqlDbType.Integer, 8);
            cmd.ExecuteNonQuery();
            cmd.ExecuteNonQuery();
            cmd.IsPrepared.Should().BeTrue();
        }
        using (var cmd = new PgSqlCommand("SELECT @p", conn))
        {
            cmd.Parameters.AddWithValue("p", PgSqlDbType.Text, "foo");
            cmd.ExecuteScalar().Should().Be("foo");
            cmd.ExecuteScalar().Should().Be("foo");
            cmd.IsPrepared.Should().BeFalse();
        }

        // SQL overloading is a pretty rare/exotic scenario. Handling it properly would involve keying
        // prepared statements not just by SQL but also by the parameter types, which would pointlessly
        // increase allocations. Instead, the second execution simply runs unprepared.
        conn.ExecuteScalar(CountPreparedStatements).Should().Be(1L);
    }

    // Tests parameter derivation a parameterized query (CommandType.Text) that is already auto-prepared.
    [Fact]
    public void derive_parameters_for_auto_prepared_statement()
    {
        //Arrange
        const string query = "SELECT @p::integer";
        const int answer = 42;
        using var dataSource = CreateDataSource(csb =>
        {
            csb.MaxAutoPrepare = 10;
            csb.AutoPrepareMinUsages = 2;
        });
        using var conn = dataSource.OpenConnection();
        using var checkCmd = new PgSqlCommand(CountPreparedStatements, conn);
        using var cmd = new PgSqlCommand(query, conn);
        checkCmd.Prepare();
        cmd.Parameters.AddWithValue("@p", PgSqlDbType.Integer, answer);
        cmd.ExecuteNonQuery(); cmd.ExecuteNonQuery(); // cmd1 is now autoprepared
        checkCmd.ExecuteScalar().Should().Be(1L);
        conn.Connector.PreparedStatementManager.NumPrepared.Should().Be(2);

        //Act
        // Derive parameters for the already autoprepared statement
        PgSqlCommandBuilder.DeriveParameters(cmd);

        //Assert
        cmd.Parameters.Count.Should().Be(1);
        cmd.Parameters[0].ParameterName.Should().Be("p");

        // DeriveParameters should have silently unprepared the autoprepared statements
        checkCmd.ExecuteScalar().Should().Be(0L);
        conn.Connector.PreparedStatementManager.NumPrepared.Should().Be(1);

        cmd.Parameters["@p"].Value = answer;
        cmd.ExecuteScalar().Should().Be(answer);
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/2644")]
    public void row_description_properly_cloned()
    {
        //Arrange
        using var dataSource = CreateDataSource(csb =>
        {
            csb.MaxAutoPrepare = 10;
            csb.AutoPrepareMinUsages = 2;
        });
        using var conn = dataSource.OpenConnection();
        conn.UnprepareAll();
        using var cmd1 = new PgSqlCommand("SELECT 1 AS foo", conn);
        using var cmd2 = new PgSqlCommand("SELECT 1 AS bar", conn);

        //Act
        cmd1.ExecuteNonQuery();
        cmd1.ExecuteNonQuery();  // Query is now auto-prepared
        cmd2.ExecuteNonQuery();
        using var reader = cmd1.ExecuteReader();

        //Assert
        reader.GetName(0).Should().Be("foo");
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/3106")]
    public async Task dont_auto_prepare_more_than_max_statements_in_batch()
    {
        //Arrange
        const int maxAutoPrepare = 50;

        await using var dataSource = CreateDataSource(csb => csb.MaxAutoPrepare = maxAutoPrepare);
        await using var connection = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        //Act
        for (var i = 0; i < 100; i++)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = string.Join("", Enumerable.Range(0, 100).Select(n => $"SELECT {n};"));
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        //Assert
        ((long)await connection.ExecuteScalarAsync(CountPreparedStatements, cancellationToken: TestContext.Current.CancellationToken)).Should().BeLessThanOrEqualTo(maxAutoPrepare);
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/3106")]
    public async Task dont_auto_prepare_more_than_max_statements_in_batch_random()
    {
        //Arrange
        const int maxAutoPrepare = 10;

        await using var dataSource = CreateDataSource(csb => csb.MaxAutoPrepare = maxAutoPrepare);
        await using var connection = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        var random = new Random(1);

        //Act
        for (var i = 0; i < 100; i++)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = string.Join("", Enumerable.Range(0, 100).Select(n => $"SELECT {random.Next(200)};"));
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        //Assert
        ((long)await connection.ExecuteScalarAsync(CountPreparedStatements, cancellationToken: TestContext.Current.CancellationToken)).Should().BeLessThanOrEqualTo(maxAutoPrepare);
    }

    [Fact]
    public async Task replace_and_execute_within_same_batch()
    {
        //Arrange
        await using var dataSource = CreateDataSource(csb =>
        {
            csb.MaxAutoPrepare = 1;
            csb.AutoPrepareMinUsages = 2;
        });
        await using var connection = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        for (var i = 0; i < 2; i++)
            await connection.ExecuteNonQueryAsync("SELECT 1", cancellationToken: TestContext.Current.CancellationToken);

        //Act
        // SELECT 1 is now auto-prepared and occupying the only slot.
        // Within the same batch, cause another SQL to replace it, and then execute it.
        await connection.ExecuteNonQueryAsync("SELECT 2; SELECT 2; SELECT 1", cancellationToken: TestContext.Current.CancellationToken);
    }

    // Exclude some internal CodeBrix.PostgresClient queries which include pg_type as well as the count statement itself
    const string CountPreparedStatements = """
SELECT COUNT(*) FROM pg_prepared_statements
WHERE statement NOT LIKE '%pg_prepared_statements%'
AND statement NOT LIKE '%pg_type%'
""";

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/2665")]
    public async Task auto_prepared_command_failure()
    {
        //Arrange
        await using var dataSource = CreateDataSource(csb =>
        {
            csb.MaxAutoPrepare = 10;
            csb.AutoPrepareMinUsages = 2;
        });
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        var tableName = await GetTempTableName(conn);
        await conn.ExecuteNonQueryAsync($"CREATE TABLE {tableName} (id integer)", cancellationToken: TestContext.Current.CancellationToken);

        await using (var command = new PgSqlCommand($"INSERT INTO {tableName} (id) VALUES (1)", conn))
        {
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
            await conn.ExecuteNonQueryAsync($"DROP TABLE {tableName}", cancellationToken: TestContext.Current.CancellationToken);
            await Assert.ThrowsAsync<PostgresException>(async () => await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
        }

        await conn.ExecuteNonQueryAsync($"CREATE TABLE {tableName} (id integer)", cancellationToken: TestContext.Current.CancellationToken);

        await using (var command = new PgSqlCommand($"INSERT INTO {tableName} (id) VALUES (1)", conn))
        {
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/3002")]
    public void replace_with_bad_sql()
    {
        //Arrange
        using var dataSource = CreateDataSource(csb =>
        {
            csb.MaxAutoPrepare = 2;
            csb.AutoPrepareMinUsages = 1;
        });
        using var conn = dataSource.OpenConnection();

        conn.ExecuteNonQuery("SELECT 1");
        conn.ExecuteNonQuery("SELECT 2");

        //Act
        // Attempt to replace SELECT 1, but fail because of bad SQL.
        // Because of the issue, PreparedStatementManager.NumPrepared is reduced from 2 to 1
        Assert.Throws<PostgresException>(() => conn.ExecuteNonQuery("SELECTBAD"))
            .SqlState.Should().Be(PostgresErrorCodes.SyntaxError);
        // Prevent SELECT 2 from being the LRU
        conn.ExecuteNonQuery("SELECT 2");
        // And attempt to replace again, reducing PreparedStatementManager.NumPrepared to 0
        Assert.Throws<PostgresException>(() => conn.ExecuteNonQuery("SELECTBAD"))
            .SqlState.Should().Be(PostgresErrorCodes.SyntaxError);

        // Since PreparedStatementManager.NumPrepared is 0, CodeBrix.PostgresClient will now send DISCARD ALL, but our internal state thinks
        // SELECT 2 is still prepared.
        conn.Close();
        conn.Open();

        //Assert
        conn.ExecuteScalar("SELECT 2").Should().Be(2);
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/4082")]
    public async Task batch_statement_execution_error_cleanup()
    {
        //Arrange
        await using var dataSource = CreateDataSource(csb =>
        {
            csb.MaxAutoPrepare = 2;
            csb.AutoPrepareMinUsages = 1;
        });
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        var funcName = await GetTempFunctionName(conn);

        // Create a function we can use to raise an error with a single statement
        await conn.ExecuteNonQueryAsync(
$"""
CREATE OR REPLACE FUNCTION {funcName}() RETURNS VOID AS
    'BEGIN RAISE EXCEPTION ''testexception'' USING ERRCODE = ''12345'', DETAIL = ''testdetail''; END;'
LANGUAGE 'plpgsql';
""", cancellationToken: TestContext.Current.CancellationToken);

        conn.UnprepareAll();

        // Occupy _auto1 and _auto2
        await conn.ExecuteNonQueryAsync("SELECT 1", cancellationToken: TestContext.Current.CancellationToken);
        await conn.ExecuteNonQueryAsync("SELECT 2", cancellationToken: TestContext.Current.CancellationToken);

        //Act
        // Execute two new SELECTs which will replace the above two. _auto1 will now contain SELECT pg_temp.emit_exception()
        // and _auto2 will contain SELECT 4. Note that they must be in this order because only the statements following
        // the error-triggering statement will be unprepared.
        //
        // We expect error 12345. Prior to the error being raised, the SELECT pg_temp.emit_exception will be successfully prepared
        // and the previous _auto1 (SELECT 1) will be successfully closed. However, the subsequent SELECT 4 will not be prepared,
        // and the previous _auto2 (SELECT 2) will not be properly closed. SELECT 4 will then be unprepared.
        var ex = await Assert.ThrowsAsync<PostgresException>(async () => await conn.ExecuteNonQueryAsync($"SELECT {funcName}(); SELECT 4", cancellationToken: TestContext.Current.CancellationToken));

        //Assert
        ex.Should().BeOfType<PostgresException>().Which.SqlState.Should().Be("12345");

        // The PreparedStatementManager prioritises replacement of unprepared statements, so we know this will replace SELECT 4 in
        // _auto2. The code previously assumed that cleanup was never required when replacing an unprepared statement (since it
        // was never prepared in PG) and this is true in most cases. However, in this case, SELECT 3 needs to logically replace
        // SELECT 2.
        //
        // Due to the bug, _auto2 never gets cleaned up and this throws a 42P05 (prepared statement "_auto2" already exists)
        // when we try to use that slot
        (await conn.ExecuteScalarAsync("SELECT 3", cancellationToken: TestContext.Current.CancellationToken)).Should().Be(3);
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/4404"), IssueLink("https://github.com/npgsql/npgsql/issues/5220")]
    public async Task schema_only()
    {
        //Arrange
        await using var dataSource = CreateDataSource(csb =>
        {
            csb.AutoPrepareMinUsages = 2;
            csb.MaxAutoPrepare = 10;
        });
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using var cmd = new PgSqlCommand("SELECT 1", conn);

        //Act
        for (var i = 0; i < 5; i++)
        {
            await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SchemaOnly, cancellationToken: TestContext.Current.CancellationToken);
        }

        //Assert
        // Make sure there is no protocol desync due to #5220
        await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken);
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/6038")]
    public async Task auto_prepared_schema_only_correct_schema()
    {
        //Arrange
        await using var dataSource = CreateDataSource(csb =>
        {
            csb.MaxAutoPrepare = 1;
            csb.AutoPrepareMinUsages = 5;
        });
        await using var connection = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        var table1 = await CreateTempTable(connection, "foo int");
        var table2 = await CreateTempTable(connection, "bar int");

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT * FROM {table1}";
        for (var i = 0; i < 5; i++)
        {
            // Make sure we prepare the first query
            await using (await cmd.ExecuteReaderAsync(CommandBehavior.SchemaOnly, cancellationToken: TestContext.Current.CancellationToken)) { }
        }

        //Act
        cmd.CommandText = $"SELECT * FROM {table2}";
        // The second query will load RowDescription, which is a singleton on PgSqlConnector
        // This shouldn't affect the first query, because we create a copy of RowDescription on prepare
        await using (await cmd.ExecuteReaderAsync(CommandBehavior.SchemaOnly, cancellationToken: TestContext.Current.CancellationToken)) { }

        cmd.CommandText = $"SELECT * FROM {table1}";
        // If we indeed made a copy of RowDescription on prepare, we should get the column for the first query and not for the second
        await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SchemaOnly | CommandBehavior.KeyInfo, cancellationToken: TestContext.Current.CancellationToken);
        var columns = await reader.GetColumnSchemaAsync(TestContext.Current.CancellationToken);

        //Assert
        columns.Count.Should().Be(1);
        columns[0].ColumnName.Should().Be("foo");
    }

    [Fact]
    public async Task auto_prepared_schema_only_replace()
    {
        //Arrange
        await using var dataSource = CreateDataSource(csb =>
        {
            csb.MaxAutoPrepare = 1;
            csb.AutoPrepareMinUsages = 5;
        });
        await using var connection = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT 1";
        for (var i = 0; i < 5; i++)
        {
            await using (await cmd.ExecuteReaderAsync(CommandBehavior.SchemaOnly, cancellationToken: TestContext.Current.CancellationToken)) { }
        }

        //Act
        cmd.CommandText = "SELECT 2";
        for (var i = 0; i < 5; i++)
        {
            await using (await cmd.ExecuteReaderAsync(CommandBehavior.SchemaOnly, cancellationToken: TestContext.Current.CancellationToken)) { }
        }
    }

    [Fact]
    public async Task auto_prepared_statement_invalidation()
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
        for (var i = 0; i < 2; i++)
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);

        //Act
        await connection.ExecuteNonQueryAsync($"ALTER TABLE {table} RENAME COLUMN foo TO bar", cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        // Since we've changed the table schema, the next execution of the prepared statement will error with 0A000
        var exception = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
        exception.SqlState.Should().Be(PostgresErrorCodes.FeatureNotSupported); // cached plan must not change result type

        // However, CodeBrix.PostgresClient should invalidate the prepared statement in this case, so the next execution should work
        await FluentActions.Awaiting(() => command.ExecuteNonQueryAsync()).Should().NotThrowAsync();
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/6432")]
    public async Task reuse_batch_with_different_connectors()
    {
        //Arrange
        await using var dataSource = CreateDataSource(csb =>
        {
            csb.MaxAutoPrepare = 10;
            csb.AutoPrepareMinUsages = 2;
        });
        await using var batch = new PgSqlBatch();
        batch.BatchCommands.Add(new PgSqlBatchCommand("SELECT 1"));
        await using (var connection = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken))
        {
            batch.Connection = connection;

            for (var i = 0; i < 2; i++)
                await batch.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        //Act
        dataSource.Clear();

        //Assert
        await using (var connection = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken))
        {
            batch.Connection = connection;
            await batch.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }
    }

    void DumpPreparedStatements(PgSqlConnection conn)
    {
        using var cmd = new PgSqlCommand("SELECT name,statement FROM pg_prepared_statements", conn);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            Console.WriteLine($"{reader.GetString(0)}: {reader.GetString(1)}");
    }
}
