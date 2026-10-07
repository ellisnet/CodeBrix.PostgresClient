using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using SilverAssertions;
using Xunit;
using static CodeBrix.PostgresClient.Tests.TestUtil;

namespace CodeBrix.PostgresClient.Tests; //was previously: Npgsql.Tests;

public abstract class BatchTests : MultiplexingTestBase, IDisposable
{
    #region Parameters

    [Fact]
    public async Task named_parameters()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        await using var batch = new PgSqlBatch(conn)
        {
            BatchCommands =
            {
                new("SELECT @p") { Parameters = { new("p", 8) } },
                new("SELECT @p1, @p2") { Parameters = { new("p1", 9), new("p2", 10) } }
            }
        };

        //Act
        await using var reader = await batch.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        (await reader.ReadAsync(TestContext.Current.CancellationToken)).Should().BeTrue();
        reader.FieldCount.Should().Be(1);
        reader[0].Should().Be(8);
        (await reader.ReadAsync(TestContext.Current.CancellationToken)).Should().BeFalse();
        (await reader.NextResultAsync(TestContext.Current.CancellationToken)).Should().BeTrue();
        reader.FieldCount.Should().Be(2);
        (await reader.ReadAsync(TestContext.Current.CancellationToken)).Should().BeTrue();
        reader[0].Should().Be(9);
        reader[1].Should().Be(10);
        (await reader.ReadAsync(TestContext.Current.CancellationToken)).Should().BeFalse();
        (await reader.NextResultAsync(TestContext.Current.CancellationToken)).Should().BeFalse();
    }

    [Fact]
    public async Task positional_parameters()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        await using var batch = new PgSqlBatch(conn)
        {
            BatchCommands =
            {
                new("SELECT $1") { Parameters = { new() { Value = 8 } } },
                new("SELECT $1, $2") { Parameters = { new() { Value = 9 }, new() { Value = 10 } } }
            }
        };

        //Act
        await using var reader = await batch.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        (await reader.ReadAsync(TestContext.Current.CancellationToken)).Should().BeTrue();
        reader.FieldCount.Should().Be(1);
        reader[0].Should().Be(8);
        (await reader.ReadAsync(TestContext.Current.CancellationToken)).Should().BeFalse();
        (await reader.NextResultAsync(TestContext.Current.CancellationToken)).Should().BeTrue();
        (await reader.ReadAsync(TestContext.Current.CancellationToken)).Should().BeTrue();
        reader.FieldCount.Should().Be(2);
        reader[0].Should().Be(9);
        reader[1].Should().Be(10);
        (await reader.ReadAsync(TestContext.Current.CancellationToken)).Should().BeFalse();
        (await reader.NextResultAsync(TestContext.Current.CancellationToken)).Should().BeFalse();
    }

    #endregion Parameters

    #region PgSqlBatchCommand

    [Fact]
    public async Task RecordsAffected_and_Rows()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "name TEXT");

        await using var batch = new PgSqlBatch(conn)
        {
            BatchCommands =
            {
                new($"INSERT INTO {table} (name) VALUES ('a'), ('b')"),
                new($"UPDATE {table} SET name='c' WHERE name='b'"),
                new($"UPDATE {table} SET name='d' WHERE name='doesnt_exist'"),
                new($"SELECT name FROM {table}"),
                new($"DELETE FROM {table}")
            }
        };

        //Act
        await using var reader = await batch.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken);

        // Consume SELECT result set to parse the CommandComplete
        await reader.CloseAsync();

        //Assert
        var command = batch.BatchCommands[0];
        command.RecordsAffected.Should().Be(2);
        command.Rows.Should().Be(2);

        command = batch.BatchCommands[1];
        command.RecordsAffected.Should().Be(1);
        command.Rows.Should().Be(1);

        command = batch.BatchCommands[2];
        command.RecordsAffected.Should().Be(0);
        command.Rows.Should().Be(0);

        command = batch.BatchCommands[3];
        command.RecordsAffected.Should().Be(-1);
        command.Rows.Should().Be(2);

        command = batch.BatchCommands[4];
        command.RecordsAffected.Should().Be(2);
        command.Rows.Should().Be(2);
    }

    [Fact]
    public async Task merge_records_affected_and_rows()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();

        MinimumPgVersion(conn, "15.0", "MERGE statement was introduced in PostgreSQL 15");

        var table = await CreateTempTable(conn, "name TEXT");

        await using var batch = new PgSqlBatch(conn)
        {
            BatchCommands =
            {
                new($"INSERT INTO {table} (name) VALUES ('a'), ('b')"),
                new($"MERGE INTO {table} S USING (SELECT 'b' as name) T ON T.name = S.name WHEN MATCHED THEN UPDATE SET name = 'c'"),
                new($"MERGE INTO {table} S USING (SELECT 'b' as name) T ON T.name = S.name WHEN NOT MATCHED THEN INSERT (name) VALUES ('b')"),
                new($"MERGE INTO {table} S USING (SELECT 'b' as name) T ON T.name = S.name WHEN MATCHED THEN DELETE"),
                new($"MERGE INTO {table} S USING (SELECT 'b' as name) T ON T.name = S.name WHEN NOT MATCHED THEN DO NOTHING")
            }
        };

        //Act
        await using var reader = await batch.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken);

        // Consume MERGE result set to parse the CommandComplete
        await reader.CloseAsync();

        //Assert
        var command = batch.BatchCommands[0];
        command.StatementType.Should().Be(StatementType.Insert);
        command.RecordsAffected.Should().Be(2);
        command.Rows.Should().Be(2);

        command = batch.BatchCommands[1];
        command.StatementType.Should().Be(StatementType.Merge);
        command.RecordsAffected.Should().Be(1);
        command.Rows.Should().Be(1);

        command = batch.BatchCommands[2];
        command.StatementType.Should().Be(StatementType.Merge);
        command.RecordsAffected.Should().Be(1);
        command.Rows.Should().Be(1);

        command = batch.BatchCommands[3];
        command.StatementType.Should().Be(StatementType.Merge);
        command.RecordsAffected.Should().Be(1);
        command.Rows.Should().Be(1);

        command = batch.BatchCommands[4];
        command.StatementType.Should().Be(StatementType.Merge);
        command.RecordsAffected.Should().Be(0);
        command.Rows.Should().Be(0);
    }

    [Fact]
    public async Task statement_types()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "name TEXT");

        await using var batch = new PgSqlBatch(conn)
        {
            BatchCommands =
            {
                new($"INSERT INTO {table} (name) VALUES ('a'), ('b')"),
                new($"UPDATE {table} SET name='c' WHERE name='b'"),
                new($"UPDATE {table} SET name='d' WHERE name='doesnt_exist'"),
                new("BEGIN"),
                new($"SELECT name FROM {table}"),
                new($"DELETE FROM {table}"),
                new("COMMIT")
            }
        };

        //Act
        await using var reader = await batch.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken);

        // Consume SELECT result set to parse the CommandComplete
        await reader.CloseAsync();

        //Assert
        batch.BatchCommands[0].StatementType.Should().Be(StatementType.Insert);
        batch.BatchCommands[1].StatementType.Should().Be(StatementType.Update);
        batch.BatchCommands[2].StatementType.Should().Be(StatementType.Update);
        batch.BatchCommands[3].StatementType.Should().Be(StatementType.Other);
        batch.BatchCommands[4].StatementType.Should().Be(StatementType.Select);
        batch.BatchCommands[5].StatementType.Should().Be(StatementType.Delete);
        batch.BatchCommands[6].StatementType.Should().Be(StatementType.Other);
    }

    [Fact]
    public async Task StatementType_Call()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        MinimumPgVersion(conn, "11.0", "Stored procedures are supported starting with PG 11");

        var sproc = await GetTempProcedureName(conn);
        await conn.ExecuteNonQueryAsync($"CREATE PROCEDURE {sproc}() LANGUAGE sql AS ''", cancellationToken: TestContext.Current.CancellationToken);

        await using var batch = new PgSqlBatch(conn)
        {
            BatchCommands = { new($"CALL {sproc}()") }
        };

        //Act
        await using var reader = await batch.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken);

        // Consume SELECT result set to parse the CommandComplete
        await reader.CloseAsync();

        //Assert
        batch.BatchCommands[0].StatementType.Should().Be(StatementType.Call);
    }

    [Fact]
    public async Task CommandType_StoredProcedure()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        MinimumPgVersion(conn, "11.0", "Stored procedures are supported starting with PG 11");

        var sproc = await GetTempProcedureName(conn);
        await conn.ExecuteNonQueryAsync($"CREATE PROCEDURE {sproc}() LANGUAGE sql AS ''", cancellationToken: TestContext.Current.CancellationToken);

        await using var batch = new PgSqlBatch(conn)
        {
            BatchCommands = { new($"{sproc}") {CommandType = CommandType.StoredProcedure} }
        };

        //Act
        await using var reader = await batch.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken);

        // Consume SELECT result set to parse the CommandComplete
        await reader.CloseAsync();

        //Assert
        batch.BatchCommands[0].StatementType.Should().Be(StatementType.Call);
    }

    [Fact]
    public async Task StatementType_Merge()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        MinimumPgVersion(conn, "15.0", "Stored procedures are supported starting with PG 11");

        var table = await CreateTempTable(conn, "name TEXT");

        await using var batch = new PgSqlBatch(conn)
        {
            BatchCommands = { new($"MERGE INTO {table} S USING (SELECT 'b' as name) T ON T.name = S.name WHEN NOT MATCHED THEN DO NOTHING") }
        };

        //Act
        await using var reader = await batch.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken);

        // Consume SELECT result set to parse the CommandComplete
        await reader.CloseAsync();

        //Assert
        batch.BatchCommands[0].StatementType.Should().Be(StatementType.Merge);
    }

    [Fact]
    public async Task statement_oid()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();

        MaximumPgVersionExclusive(conn, "12.0",
            "Support for 'CREATE TABLE ... WITH OIDS' has been removed in 12.0. See https://www.postgresql.org/docs/12/release-12.html#id-1.11.6.5.4");

        var table = await GetTempTableName(conn);
        await conn.ExecuteNonQueryAsync($"CREATE TABLE {table} (name TEXT) WITH OIDS", cancellationToken: TestContext.Current.CancellationToken);
        await using var batch = new PgSqlBatch(conn)
        {
            BatchCommands =
            {
                new($"INSERT INTO {table} (name) VALUES (@p1)") { Parameters = { new("p1", "foo") } },
                new($"UPDATE {table} SET name='b' WHERE name=@p2") { Parameters = { new("p2", "bar") } }
            }
        };

        //Act
        await batch.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);

        //Assert
        batch.BatchCommands[0].OID.Should().NotBe(0);
        batch.BatchCommands[1].OID.Should().Be(0);
    }

    [Fact]
    public void CanCreateParameter() => new PgSqlBatchCommand().CanCreateParameter.Should().BeTrue();

    [Fact]
    public void CreateParameter() => new PgSqlBatchCommand().CreateParameter().Should().NotBeNull();

    #endregion PgSqlBatchCommand

    #region Command behaviors

    [Fact]
    public async Task single_result()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        await using var batch = new PgSqlBatch(conn)
        {
            BatchCommands = { new("SELECT 1"), new("SELECT 2") }
        };

        //Act
        var reader = await batch.ExecuteReaderAsync(CommandBehavior.SingleResult | Behavior, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        reader.Read().Should().BeTrue();
        reader.GetInt32(0).Should().Be(1);
        reader.NextResult().Should().BeFalse();
    }

    [Fact]
    public async Task single_row()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        await using var batch = new PgSqlBatch(conn)
        {
            BatchCommands = { new("SELECT 1"), new("SELECT 2") }
        };

        //Act
        await using var reader = await batch.ExecuteReaderAsync(CommandBehavior.SingleRow | Behavior, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        reader.Read().Should().BeTrue();
        reader.GetInt32(0).Should().Be(1);
        reader.Read().Should().BeFalse();
        reader.NextResult().Should().BeFalse();
    }

    [Fact]
    public async Task schema_only_get_field_type()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        await using var batch = new PgSqlBatch(conn)
        {
            BatchCommands = { new("SELECT 1"), new("SELECT 'foo'") }
        };

        //Act
        await using var reader = await batch.ExecuteReaderAsync(CommandBehavior.SchemaOnly | Behavior, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        reader.GetFieldType(0).Should().BeSameAs(typeof(int));
        (await reader.NextResultAsync(TestContext.Current.CancellationToken)).Should().BeTrue();
        reader.GetFieldType(0).Should().BeSameAs(typeof(string));
        (await reader.NextResultAsync(TestContext.Current.CancellationToken)).Should().BeFalse();
    }

    [Fact]
    public async Task schema_only_returns_no_data()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        await using var batch = new PgSqlBatch(conn)
        {
            BatchCommands = { new("SELECT 1"), new("SELECT 'foo'") }
        };

        //Act
        await using var reader = await batch.ExecuteReaderAsync(CommandBehavior.SchemaOnly | Behavior, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        reader.Read().Should().BeFalse();
        (await reader.NextResultAsync(TestContext.Current.CancellationToken)).Should().BeTrue();
        reader.Read().Should().BeFalse();
        (await reader.NextResultAsync(TestContext.Current.CancellationToken)).Should().BeFalse();
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/693")]
    public async Task close_connection()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        await using var batch = new PgSqlBatch(conn)
        {
            BatchCommands = { new("SELECT 1"), new("SELECT 2") }
        };

        //Act
        await using (var reader = await batch.ExecuteReaderAsync(CommandBehavior.CloseConnection | Behavior, cancellationToken: TestContext.Current.CancellationToken))
            while (reader.Read()) {}

        //Assert
        conn.State.Should().Be(ConnectionState.Closed);
    }

    #endregion Command behaviors

    #region Error barriers

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task batch_with_error_at_start(bool withErrorBarriers)
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "id INT");

        await using var batch = new PgSqlBatch(conn)
        {
            BatchCommands =
            {
                new("INVALID SQL"),
                new($"INSERT INTO {table} (id) VALUES (8)")
            },
            EnableErrorBarriers = withErrorBarriers
        };

        //Act
        var exception = await Assert.ThrowsAsync<PostgresException>(async () => await batch.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken));

        //Assert
        exception.BatchCommand.Should().BeSameAs(batch.BatchCommands[0]);

        (await conn.ExecuteScalarAsync($"SELECT count(*) FROM {table}", cancellationToken: TestContext.Current.CancellationToken)).Should().Be(withErrorBarriers ? 1L : 0L);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task batch_with_error_at_end(bool withErrorBarriers)
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "id INT");

        await using var batch = new PgSqlBatch(conn)
        {
            BatchCommands =
            {
                new($"INSERT INTO {table} (id) VALUES (8)"),
                new("INVALID SQL")
            },
            EnableErrorBarriers = withErrorBarriers
        };

        //Act
        var exception = await Assert.ThrowsAsync<PostgresException>(async () => await batch.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken));

        //Assert
        exception.BatchCommand.Should().BeSameAs(batch.BatchCommands[1]);

        (await conn.ExecuteScalarAsync($"SELECT count(*) FROM {table}", cancellationToken: TestContext.Current.CancellationToken)).Should().Be(withErrorBarriers ? 1L : 0L);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task batch_with_multiple_errors(bool withErrorBarriers)
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "id INT");

        await using var batch = new PgSqlBatch(conn)
        {
            BatchCommands =
            {
                new($"INSERT INTO {table} (id) VALUES (8)"),
                new("INVALID SQL"),
                new($"INSERT INTO {table} (id) VALUES (9)"),
                new("INVALID SQL"),
                new($"INSERT INTO {table} (id) VALUES (10)")
            },
            EnableErrorBarriers = withErrorBarriers
        };

        if (withErrorBarriers)
        {
            // A Sync is inserted after each command, so all commands are executed and all exceptions are thrown as an AggregateException
            var exception = await Assert.ThrowsAsync<PgSqlException>(async () => await batch.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken));
            var aggregateException = (AggregateException)exception.InnerException;
            ((PostgresException)aggregateException.InnerExceptions[0]).BatchCommand.Should().BeSameAs(batch.BatchCommands[1]);
            ((PostgresException)aggregateException.InnerExceptions[1]).BatchCommand.Should().BeSameAs(batch.BatchCommands[3]);

            (await conn.ExecuteScalarAsync($"SELECT count(*) FROM {table}", cancellationToken: TestContext.Current.CancellationToken)).Should().Be(3L);
        }
        else
        {
            // PG skips all commands after the first error; an exception is only raised for the first one, and the entire batch is
            // rolled back (implicit transaction).
            var exception = await Assert.ThrowsAsync<PostgresException>(async () => await batch.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken));
            exception.BatchCommand.Should().BeSameAs(batch.BatchCommands[1]);

            (await conn.ExecuteScalarAsync($"SELECT count(*) FROM {table}", cancellationToken: TestContext.Current.CancellationToken)).Should().Be(0L);
        }

        (await conn.ExecuteScalarAsync("SELECT 1", cancellationToken: TestContext.Current.CancellationToken)).Should().Be(1);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task batch_close_dispose_reader_with_multiple_errors(bool withErrorBarriers, bool dispose)
    {
        //Arrange
        // Create a temp pool since we dispose the reader (and check the state afterwards) and it can be reused by another connection
        await using var dataSource = CreateDataSource(x => x.IncludeFailedBatchedCommand = true);
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        var table = await CreateTempTable(conn, "id INT");

        await using var batch = new PgSqlBatch(conn)
        {
            BatchCommands =
            {
                new("SELECT NULL WHERE 1=0"),
                new($"INSERT INTO {table} (id) VALUES (8)"),
                new("INVALID SQL"),
                new($"INSERT INTO {table} (id) VALUES (9)"),
                new("INVALID SQL"),
                new($"INSERT INTO {table} (id) VALUES (10)")
            },
            EnableErrorBarriers = withErrorBarriers
        };

        await using (var reader = await batch.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken))
        {
            if (withErrorBarriers)
            {
                // A Sync is inserted after each command, so all commands are executed and all exceptions are thrown as an AggregateException
                var exception = await Assert.ThrowsAsync<PgSqlException>(async () =>
                {
                    if (dispose)
                        await reader.DisposeAsync();
                    else
                        await reader.CloseAsync();
                });
                var aggregateException = (AggregateException)exception.InnerException;
                ((PostgresException)aggregateException.InnerExceptions[0]).BatchCommand.Should().BeSameAs(batch.BatchCommands[2]);
                ((PostgresException)aggregateException.InnerExceptions[1]).BatchCommand.Should().BeSameAs(batch.BatchCommands[4]);
            }
            else
            {
                // PG skips all commands after the first error; an exception is only raised for the first one, and the entire batch is
                // rolled back (implicit transaction).
                var exception = await Assert.ThrowsAsync<PostgresException>(async () =>
                {
                    if (dispose)
                        await reader.DisposeAsync();
                    else
                        await reader.CloseAsync();
                });

                exception.BatchCommand.Should().BeSameAs(batch.BatchCommands[2]);
            }

            reader.State.Should().Be(dispose ? ReaderState.Disposed : ReaderState.Closed);
        }

        (await conn.ExecuteScalarAsync("SELECT 1", cancellationToken: TestContext.Current.CancellationToken)).Should().Be(1);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task batch_with_result_sets_and_error(bool withErrorBarriers)
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "id INT");

        await using var batch = new PgSqlBatch(conn)
        {
            BatchCommands =
            {
                new($"INSERT INTO {table} (id) VALUES (9)"),
                new("SELECT 1"),
                new("INVALID SQL"),
                new($"INSERT INTO {table} (id) VALUES (9)"),
                new("SELECT 2")
            },
            EnableErrorBarriers = withErrorBarriers
        };

        await using (var reader = await batch.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken))
        {
            (await reader.ReadAsync(TestContext.Current.CancellationToken)).Should().BeTrue();
            reader[0].Should().Be(1);
            (await reader.ReadAsync(TestContext.Current.CancellationToken)).Should().BeFalse();

            await Assert.ThrowsAsync<PostgresException>(async () => await reader.NextResultAsync(TestContext.Current.CancellationToken));

            reader.State.Should().Be(ReaderState.Consumed);
            (await reader.ReadAsync(TestContext.Current.CancellationToken)).Should().BeFalse();
            (await reader.NextResultAsync(TestContext.Current.CancellationToken)).Should().BeFalse();
        }

        (await conn.ExecuteScalarAsync($"SELECT count(*) FROM {table}", cancellationToken: TestContext.Current.CancellationToken)).Should().Be(withErrorBarriers ? 2L : 0L);
    }

    [Fact]
    public async Task error_with_append_error_barrier()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "id INT");

        await using var batch = new PgSqlBatch(conn)
        {
            BatchCommands =
            {
                new($"INSERT INTO {table} (id) VALUES (8)"),
                new("INVALID SQL") { AppendErrorBarrier = true },
                new($"INSERT INTO {table} (id) VALUES (9)")
            }
        };

        //Act
        // A Sync is placed after the 2nd command (INVALID SQL), so the 1st command is rolled back but not the 3rd.
        var exception = await Assert.ThrowsAsync<PostgresException>(async () => await batch.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken));

        //Assert
        exception.BatchCommand.Should().BeSameAs(batch.BatchCommands[1]);

        (await conn.ExecuteScalarAsync($"SELECT id FROM {table} ORDER BY id", cancellationToken: TestContext.Current.CancellationToken)).Should().Be(9);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AppendErrorBarrier_on_last_command(bool enabled)
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "id INT");

        await using var batch = new PgSqlBatch(conn)
        {
            BatchCommands =
            {
                new($"INSERT INTO {table} (id) VALUES (8)"),
                new($"INSERT INTO {table} (id) VALUES (9)") { AppendErrorBarrier = enabled }
            },
            EnableErrorBarriers = true
        };

        //Act
        var result = await batch.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);

        //Assert
        result.Should().Be(2);
    }

    [Fact]
    public async Task error_barriers_with_schema_only()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();

        await using var batch = new PgSqlBatch(conn)
        {
            BatchCommands =
            {
                new("SELECT 1"),
                new("SELECT 'foo'")
            },
            EnableErrorBarriers = true
        };

        //Act
        await using var reader = await batch.ExecuteReaderAsync(CommandBehavior.SchemaOnly | Behavior, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        var columnSchema = await reader.GetColumnSchemaAsync(TestContext.Current.CancellationToken);
        columnSchema[0].DataType.Should().BeSameAs(typeof(int));

        (await reader.NextResultAsync(TestContext.Current.CancellationToken)).Should().BeTrue();
        columnSchema = await reader.GetColumnSchemaAsync(TestContext.Current.CancellationToken);
        columnSchema[0].DataType.Should().BeSameAs(typeof(string));
    }

    #endregion Error barriers

    #region Miscellaneous

    [Fact]
    public async Task single_batch_command()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        await using var batch = new PgSqlBatch(conn)
        {
            BatchCommands = { new("SELECT 8") }
        };

        //Act
        await using var reader = await batch.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        (await reader.ReadAsync(TestContext.Current.CancellationToken)).Should().BeTrue();
        reader.FieldCount.Should().Be(1);
        reader[0].Should().Be(8);
        (await reader.ReadAsync(TestContext.Current.CancellationToken)).Should().BeFalse();
        (await reader.NextResultAsync(TestContext.Current.CancellationToken)).Should().BeFalse();
    }

    [Fact]
    public async Task empty_batch()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        await using var batch = new PgSqlBatch(conn);

        //Act
        await using var reader = await batch.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        (await reader.ReadAsync(TestContext.Current.CancellationToken)).Should().BeFalse();
        (await reader.NextResultAsync(TestContext.Current.CancellationToken)).Should().BeFalse();
    }

    [Fact]
    public async Task semicolon_is_not_allowed_with_no_parameters()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        await using var batch = new PgSqlBatch(conn)
        {
            BatchCommands = { new("SELECT 1; SELECT 2") }
        };

        //Act
        var act = () => batch.ExecuteReaderAsync(Behavior);

        //Assert
        await act.Should().ThrowExactlyAsync<PostgresException>();
    }

    [Fact]
    public async Task semicolon_is_not_allowed_with_named_parameters()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        await using var batch = new PgSqlBatch(conn)
        {
            BatchCommands =
            {
                new("SELECT @p1; SELECT 2")
                {
                    Parameters = { new("p1", 1) }
                }
            }
        };

        //Act
        var act = () => batch.ExecuteReaderAsync(Behavior);

        //Assert
        await act.Should().ThrowExactlyAsync<NotSupportedException>();
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/967")]
    public async Task pgsql_exception_references_batch_command_with_single_command()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        var function = await GetTempFunctionName(conn);

        await conn.ExecuteNonQueryAsync($@"
CREATE OR REPLACE FUNCTION {function}() RETURNS VOID AS
   'BEGIN RAISE EXCEPTION ''testexception'' USING ERRCODE = ''12345''; END;'
LANGUAGE 'plpgsql'", cancellationToken: TestContext.Current.CancellationToken);

        // We use PgSqlConnection.CreateBatch to test that the batch isn't recycled when referenced in an exception
        var batch = conn.CreateBatch();
        batch.BatchCommands.Add(new($"SELECT {function}()"));

        //Act
        var e = await Assert.ThrowsAsync<PostgresException>(async () => await batch.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken));

        //Assert
        e.BatchCommand.Should().BeSameAs(batch.BatchCommands[0]);

        // Make sure the command isn't recycled by the connection when it's disposed - this is important since internal command
        // resources are referenced by the exception above, which is very likely to escape the using statement of the command.
        batch.Dispose();
        var cmd2 = conn.CreateBatch();
        batch.Should().NotBeSameAs(cmd2);
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/967")]
    public async Task pgsql_exception_references_batch_command_with_multiple_commands()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        var function = await GetTempFunctionName(conn);

        await conn.ExecuteNonQueryAsync($@"
CREATE OR REPLACE FUNCTION {function}() RETURNS VOID AS
   'BEGIN RAISE EXCEPTION ''testexception'' USING ERRCODE = ''12345''; END;'
LANGUAGE 'plpgsql'", cancellationToken: TestContext.Current.CancellationToken);

        // We use PgSqlConnection.CreateBatch to test that the batch isn't recycled when referenced in an exception
        var batch = conn.CreateBatch();
        batch.BatchCommands.Add(new("SELECT 1"));
        batch.BatchCommands.Add(new($"SELECT {function}()"));

        await using (var reader = await batch.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken))
        {
            var e = await Assert.ThrowsAsync<PostgresException>(async () => await reader.NextResultAsync(TestContext.Current.CancellationToken));
            e.BatchCommand.Should().BeSameAs(batch.BatchCommands[1]);
        }

        // Make sure the command isn't recycled by the connection when it's disposed - this is important since internal command
        // resources are referenced by the exception above, which is very likely to escape the using statement of the command.
        batch.Dispose();
        var cmd2 = conn.CreateBatch();
        batch.Should().NotBeSameAs(cmd2);
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/4202")]
    public async Task ExecuteScalar_without_parameters()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        var batch = new PgSqlBatch(conn) { BatchCommands = { new("SELECT 1") } };

        //Act
        var result = await batch.ExecuteScalarAsync(TestContext.Current.CancellationToken);

        //Assert
        result.Should().Be(1);
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/4264")]
    public async Task batch_with_auto_prepare_reuse()
    {
        //Arrange
        await using var dataSource = CreateDataSource(csb => csb.MaxAutoPrepare = 20);
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        var tempTableName = await CreateTempTable(conn, "id int");

        //Act
        await using var batch = new PgSqlBatch(conn);
        for (var i = 0; i < 2; ++i)
        {
            for (var j = 0; j < 10; ++j)
            {
                batch.BatchCommands.Add(new PgSqlBatchCommand($"DELETE FROM {tempTableName} WHERE 1=0"));
            }
            await batch.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
            batch.BatchCommands.Clear();
        }
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/5239")]
    public async Task batch_dispose_reuse()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        PgSqlBatch firstBatch;
        await using (var batch = conn.CreateBatch())
        {
            firstBatch = batch;

            batch.BatchCommands.Add(new PgSqlBatchCommand("SELECT 1"));
            (await batch.ExecuteScalarAsync(TestContext.Current.CancellationToken)).Should().Be(1);
        }

        await using (var batch = conn.CreateBatch())
        {
            batch.Should().BeSameAs(firstBatch);

            batch.BatchCommands.Add(new PgSqlBatchCommand("SELECT 2"));
            (await batch.ExecuteScalarAsync(TestContext.Current.CancellationToken)).Should().Be(2);
        }

        await conn.CloseAsync();
        await conn.OpenAsync(TestContext.Current.CancellationToken);

        await using (var batch = conn.CreateBatch())
        {
            batch.Should().BeSameAs(firstBatch);

            batch.BatchCommands.Add(new PgSqlBatchCommand("SELECT 3"));
            (await batch.ExecuteScalarAsync(TestContext.Current.CancellationToken)).Should().Be(3);
        }
    }

    #endregion Miscellaneous

    #region Initialization / setup / teardown

    // ReSharper disable InconsistentNaming
    readonly bool IsSequential;
    readonly CommandBehavior Behavior;
    // ReSharper restore InconsistentNaming

    PgSqlDataSource _dataSource;
    protected override PgSqlDataSource DataSource => _dataSource ??= CreateDataSource(csb => csb.IncludeFailedBatchedCommand = true);

    protected BatchTests(MultiplexingMode multiplexingMode, CommandBehavior behavior) : base(multiplexingMode)
    {
        Behavior = behavior;
        IsSequential = (Behavior & CommandBehavior.SequentialAccess) != 0;
    }

    // xUnit creates a new instance per test, so only dispose a data source that this test actually created
    public void Dispose() => _dataSource?.Dispose();

    #endregion
}

public sealed class BatchTests_NonMultiplexing_Default() : BatchTests(MultiplexingMode.NonMultiplexing, CommandBehavior.Default);
public sealed class BatchTests_Multiplexing_Default() : BatchTests(MultiplexingMode.Multiplexing, CommandBehavior.Default);
public sealed class BatchTests_NonMultiplexing_SequentialAccess() : BatchTests(MultiplexingMode.NonMultiplexing, CommandBehavior.SequentialAccess);
public sealed class BatchTests_Multiplexing_SequentialAccess() : BatchTests(MultiplexingMode.Multiplexing, CommandBehavior.SequentialAccess);
