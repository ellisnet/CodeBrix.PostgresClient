using System.Data;
using System.Threading.Tasks;
using CodeBrix.PostgresClient.PgSqlTypes;
using CodeBrix.PostgresClient.PostgresTypes;
using SilverAssertions;
using Xunit;
using static CodeBrix.PostgresClient.Tests.TestUtil;

namespace CodeBrix.PostgresClient.Tests; //was previously: Npgsql.Tests;

// Stored procedures were introduced in PostgreSQL 11; the test server is always newer than that, so the
// one-time minimum-version check is not needed.
public class StoredProcedureTests : TestBase
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task with_input_parameters(bool withPositional, bool withNamed)
    {
        //Arrange
        var table = await CreateTempTable(DataSource, "foo int, bar int");
        var sproc = await GetTempProcedureName(DataSource);

        await DataSource.ExecuteNonQueryAsync(@$"
CREATE PROCEDURE {sproc}(a int, b int)
LANGUAGE SQL
AS $$
    INSERT INTO {table} VALUES (a, b);
$$", TestContext.Current.CancellationToken);

        //Act
        await using (var command = DataSource.CreateCommand(sproc))
        {
            command.CommandType = CommandType.StoredProcedure;

            command.Parameters.Add(withPositional
                ? new() { Value = 8 }
                : new() { ParameterName = "a", Value = 8 });

            command.Parameters.Add(withNamed
                ? new() { ParameterName = "b", Value = 9 }
                : new() { Value = 9 });

            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        //Assert
        await using (var command = DataSource.CreateCommand($"SELECT * FROM {table}"))
        await using (var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken))
        {
            await reader.ReadAsync(TestContext.Current.CancellationToken);
            reader[0].Should().Be(8);
            reader[1].Should().Be(9);
        }
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task with_output_parameters(bool withPositional, bool withNamed)
    {
        //Arrange
        MinimumPgVersion(DataSource, "14.0", "Stored procedure OUT parameters are only support starting with version 14");

        var sproc = await GetTempProcedureName(DataSource);

        await DataSource.ExecuteNonQueryAsync(@$"
CREATE PROCEDURE {sproc}(a int, OUT out1 int, OUT out2 int, b int)
LANGUAGE plpgsql
AS $$
BEGIN
    out1 = a;
    out2 = b;
END$$", TestContext.Current.CancellationToken);

        await using var command = DataSource.CreateCommand(sproc);
        command.CommandType = CommandType.StoredProcedure;

        command.Parameters.Add(new() { Value = 8 });

        command.Parameters.Add(withPositional
            ? new() { Direction = ParameterDirection.Output }
            : new() { ParameterName = "out1", Direction = ParameterDirection.Output });

        command.Parameters.Add(withNamed
            ? new() { ParameterName = "out2", Direction = ParameterDirection.Output }
            : new() { Direction = ParameterDirection.Output });

        command.Parameters.Add(new() { ParameterName = "b", Value = 9 });

        //Act
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        await reader.ReadAsync(TestContext.Current.CancellationToken);

        //Assert
        reader[0].Should().Be(8);
        reader[1].Should().Be(9);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task with_input_output_parameters(bool withPositional, bool withNamed)
    {
        //Arrange
        var sproc = await GetTempProcedureName(DataSource);

        await DataSource.ExecuteNonQueryAsync(@$"
CREATE PROCEDURE {sproc}(a int, INOUT inout1 int, INOUT inout2 int, b int)
LANGUAGE plpgsql
AS $$
BEGIN
    inout1 = inout1 + a;
    inout2 = inout2 + b;
END$$", TestContext.Current.CancellationToken);

        await using var command = DataSource.CreateCommand(sproc);
        command.CommandType = CommandType.StoredProcedure;

        command.Parameters.Add(new() { Value = 8 });

        command.Parameters.Add(withPositional
            ? new() { Value = 1, Direction = ParameterDirection.InputOutput }
            : new() { ParameterName = "inout1", Value = 1, Direction = ParameterDirection.InputOutput });

        command.Parameters.Add(withNamed
            ? new() { ParameterName = "inout2", Value = 2, Direction = ParameterDirection.InputOutput }
            : new() { Value = 2, Direction = ParameterDirection.InputOutput });

        command.Parameters.Add(new() { ParameterName = "b", Value = 9 });

        //Act
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        await reader.ReadAsync(TestContext.Current.CancellationToken);

        //Assert
        reader[0].Should().Be(9);
        reader[1].Should().Be(11);
    }

    [Fact]
    public async Task batch_positional_parameters_works()
    {
        //Arrange
        var tempname = await GetTempProcedureName(DataSource);
        await using var connection = await DataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, TestContext.Current.CancellationToken);
        await using var batch = new PgSqlBatch(connection, transaction)
        {
            BatchCommands =
            {
                new(tempname)
                {
                    CommandType = CommandType.StoredProcedure,
                    Parameters =
                    {
                        new() { Value = "" },
                        new() { DbType = DbType.Int64, Direction = ParameterDirection.Output }
                    }
                },
                new ("COMMIT")
            }
        };

        //Assert
        await Assert.ThrowsAsync<PostgresException>(() => batch.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task batch_stored_procedure_output_parameters_works()
    {
        //Arrange
        // Proper OUT params were introduced in PostgreSQL 14
        MinimumPgVersion(DataSource, "14.0", "Stored procedure OUT parameters are only support starting with version 14");
        var sproc = await GetTempProcedureName(DataSource);

        await using var connection = await DataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, TestContext.Current.CancellationToken);
        var c = connection.CreateCommand();
        c.CommandText = $"""
        CREATE OR REPLACE PROCEDURE {sproc}
        (
            p_username TEXT,
            OUT p_user_id BIGINT
        )
        LANGUAGE plpgsql
        AS $$
        BEGIN
            p_user_id = 1;
        	return;
        END;
        $$;
        """;
        await c.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);

        await using var batch = new PgSqlBatch(connection, transaction)
        {
            BatchCommands =
            {
                new(sproc)
                {
                    CommandType = CommandType.StoredProcedure,
                    Parameters =
                    {
                        new() { Value = "" },
                        new() { PgSqlDbType = PgSqlDbType.Bigint, Direction = ParameterDirection.Output }
                    }
                },
                new(sproc)
                {
                    CommandType = CommandType.StoredProcedure,
                    Parameters =
                    {
                        new() { Value = "" },
                        new() { PgSqlDbType = PgSqlDbType.Bigint, Direction = ParameterDirection.Output }
                    }
                }
            }
        };

        //Act
        await batch.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);

        //Assert
        batch.BatchCommands[0].Parameters[1].Value.Should().Be(1);
        batch.BatchCommands[1].Parameters[1].Value.Should().Be(1);
    }

    #region DeriveParameters

    // Tests function parameter derivation with IN, OUT and INOUT parameters
    [Fact]
    public async Task DeriveParameters_procedure_various()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        MinimumPgVersion(conn, "14.0", "Stored procedure OUT parameters are only support starting with version 14");
        var sproc = await GetTempProcedureName(conn);

        await conn.ExecuteNonQueryAsync($@"
CREATE PROCEDURE {sproc}(IN param1 INT, OUT param2 text, INOUT param3 INT) AS $$
BEGIN
    param2 = 'sometext';
    param3 = param1 + param3;
END;
$$ LANGUAGE plpgsql", cancellationToken: TestContext.Current.CancellationToken);

        await using var command = new PgSqlCommand(sproc, conn) { CommandType = CommandType.StoredProcedure };

        //Act
        PgSqlCommandBuilder.DeriveParameters(command);

        //Assert
        command.Parameters.Should().HaveCount(3);
        command.Parameters[0].Direction.Should().Be(ParameterDirection.Input);
        command.Parameters[0].PgSqlDbType.Should().Be(PgSqlDbType.Integer);
        command.Parameters[0].PostgresType.Should().BeOfType<PostgresBaseType>();
        command.Parameters[0].DataTypeName.Should().Be("integer");
        command.Parameters[0].ParameterName.Should().Be("param1");
        command.Parameters[1].Direction.Should().Be(ParameterDirection.Output);
        command.Parameters[1].PgSqlDbType.Should().Be(PgSqlDbType.Text);
        command.Parameters[1].PostgresType.Should().BeOfType<PostgresBaseType>();
        command.Parameters[1].DataTypeName.Should().Be("text");
        command.Parameters[1].ParameterName.Should().Be("param2");
        command.Parameters[2].Direction.Should().Be(ParameterDirection.InputOutput);
        command.Parameters[2].PgSqlDbType.Should().Be(PgSqlDbType.Integer);
        command.Parameters[2].PostgresType.Should().BeOfType<PostgresBaseType>();
        command.Parameters[2].DataTypeName.Should().Be("integer");
        command.Parameters[2].ParameterName.Should().Be("param3");
        command.Parameters[0].Value = 5;
        command.Parameters[2].Value = 4;
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        command.Parameters[0].Value.Should().Be(5);
        command.Parameters[1].Value.Should().Be("sometext");
        command.Parameters[2].Value.Should().Be(9);
    }

    // Tests function parameter derivation with IN-only parameters
    [Fact]
    public async Task DeriveParameters_procedure_in_only()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        var sproc = await GetTempProcedureName(conn);

        await conn.ExecuteNonQueryAsync($@"CREATE PROCEDURE {sproc}(IN param1 INT, IN param2 INT) AS '' LANGUAGE sql", cancellationToken: TestContext.Current.CancellationToken);

        await using var cmd = new PgSqlCommand(sproc, conn) { CommandType = CommandType.StoredProcedure };

        //Act
        PgSqlCommandBuilder.DeriveParameters(cmd);

        //Assert
        cmd.Parameters.Should().HaveCount(2);
        cmd.Parameters[0].Direction.Should().Be(ParameterDirection.Input);
        cmd.Parameters[1].Direction.Should().Be(ParameterDirection.Input);
        cmd.Parameters[0].Value = 5;
        cmd.Parameters[1].Value = 4;
        await FluentActions.Awaiting(() => cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken)).Should().NotThrowAsync();
    }

    // Tests function parameter derivation with no parameters
    [Fact]
    public async Task DeriveParameters_procedure_no_params()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        var sproc = await GetTempProcedureName(conn);

        await conn.ExecuteNonQueryAsync($@"CREATE PROCEDURE {sproc}() AS '' LANGUAGE sql", cancellationToken: TestContext.Current.CancellationToken);

        await using var cmd = new PgSqlCommand(sproc, conn) { CommandType = CommandType.StoredProcedure };

        //Act
        PgSqlCommandBuilder.DeriveParameters(cmd);

        //Assert
        cmd.Parameters.Should().BeEmpty();
    }

    [Fact]
    public async Task DeriveParameters_procedure_with_case_sensitive_name()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        await conn.ExecuteNonQueryAsync(@"CREATE OR REPLACE PROCEDURE ""ProcedureCaseSensitive""(int4, text) AS '' LANGUAGE sql", cancellationToken: TestContext.Current.CancellationToken);

        try
        {
            await using var command = new PgSqlCommand(@"""ProcedureCaseSensitive""", conn) { CommandType = CommandType.StoredProcedure };

            //Act
            PgSqlCommandBuilder.DeriveParameters(command);

            //Assert
            command.Parameters[0].PgSqlDbType.Should().Be(PgSqlDbType.Integer);
            command.Parameters[1].PgSqlDbType.Should().Be(PgSqlDbType.Text);
        }
        finally
        {
            await conn.ExecuteNonQueryAsync(@"DROP PROCEDURE ""ProcedureCaseSensitive""", cancellationToken: TestContext.Current.CancellationToken);
        }
    }

    // Tests function parameter derivation for quoted functions with double quotes in the name works
    [Fact]
    public async Task DeriveParameters_quote_characters_in_function_name()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        var sproc = @"""""""ProcedureQuote""""CharactersInName""""""";
        await conn.ExecuteNonQueryAsync($"CREATE OR REPLACE PROCEDURE {sproc}(int4, text) AS 'SELECT 0' LANGUAGE sql", cancellationToken: TestContext.Current.CancellationToken);

        try
        {
            await using var command = new PgSqlCommand(sproc, conn) { CommandType = CommandType.StoredProcedure };

            //Act
            PgSqlCommandBuilder.DeriveParameters(command);

            //Assert
            command.Parameters[0].PgSqlDbType.Should().Be(PgSqlDbType.Integer);
            command.Parameters[1].PgSqlDbType.Should().Be(PgSqlDbType.Text);
        }
        finally
        {
            await conn.ExecuteNonQueryAsync("DROP PROCEDURE " + sproc, cancellationToken: TestContext.Current.CancellationToken);
        }
    }

    // Tests function parameter derivation for quoted functions with dots in the name works
    [Fact]
    public async Task DeriveParameters_dot_character_in_function_name()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        await conn.ExecuteNonQueryAsync(
            @"CREATE OR REPLACE PROCEDURE ""My.Dotted.Procedure""(int4, text) AS 'SELECT 0' LANGUAGE sql", cancellationToken: TestContext.Current.CancellationToken);

        try
        {
            await using var command = new PgSqlCommand(@"""My.Dotted.Procedure""", conn) { CommandType = CommandType.StoredProcedure };

            //Act
            PgSqlCommandBuilder.DeriveParameters(command);

            //Assert
            command.Parameters[0].PgSqlDbType.Should().Be(PgSqlDbType.Integer);
            command.Parameters[1].PgSqlDbType.Should().Be(PgSqlDbType.Text);
        }
        finally
        {
            await conn.ExecuteNonQueryAsync(@"DROP PROCEDURE ""My.Dotted.Procedure""", cancellationToken: TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task DeriveParameters_parameter_name_from_function()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        MinimumPgVersion(conn, "14.0", "Stored procedure OUT parameters are only support starting with version 14");
        var sproc = await GetTempProcedureName(conn);

        await conn.ExecuteNonQueryAsync(
            $"CREATE PROCEDURE {sproc}(x int, y int, out sum int, out product int) AS 'SELECT $1 + $2, $1 * $2' LANGUAGE sql", cancellationToken: TestContext.Current.CancellationToken);
        await using var command = new PgSqlCommand(sproc, conn) { CommandType = CommandType.StoredProcedure };

        //Act
        PgSqlCommandBuilder.DeriveParameters(command);

        //Assert
        command.Parameters[0].ParameterName.Should().Be("x");
        command.Parameters[1].ParameterName.Should().Be("y");
    }

    [Fact]
    public async Task DeriveParameters_non_existing_procedure()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        var invalidCommandName = new PgSqlCommand("invalidprocedurename", conn) { CommandType = CommandType.StoredProcedure };

        //Act
        var ex = Assert.Throws<PostgresException>(() => PgSqlCommandBuilder.DeriveParameters(invalidCommandName));

        //Assert
        ex.SqlState.Should().Be(PostgresErrorCodes.UndefinedFunction);
    }

    // Tests if the right function according to search_path is used in function parameter derivation
    [Fact]
    public async Task DeriveParameters_procedure_correct_schema_resolution()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        var schema1 = await CreateTempSchema(conn);
        var schema2 = await CreateTempSchema(conn);

        await conn.ExecuteNonQueryAsync($@"
CREATE PROCEDURE {schema1}.redundantsproc() AS 'SELECT 1' LANGUAGE sql;
CREATE PROCEDURE {schema2}.redundantsproc(IN param1 INT, IN param2 INT) AS 'SELECT param1 + param2' LANGUAGE sql;
SET search_path TO {schema2};", cancellationToken: TestContext.Current.CancellationToken);
        await using var command = new PgSqlCommand("redundantsproc", conn) { CommandType = CommandType.StoredProcedure };

        //Act
        PgSqlCommandBuilder.DeriveParameters(command);

        //Assert
        command.Parameters.Should().HaveCount(2);
        command.Parameters[0].Direction.Should().Be(ParameterDirection.Input);
        command.Parameters[1].Direction.Should().Be(ParameterDirection.Input);
    }

    // Tests if function parameter derivation throws an exception if the specified function is not in the search_path
    [Fact]
    public async Task DeriveParameters_throws_for_existing_procedure_that_is_not_in_search_path()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        var schema = await CreateTempSchema(conn);

        await conn.ExecuteNonQueryAsync($@"
CREATE PROCEDURE {schema}.schema1sproc() AS 'SELECT 1' LANGUAGE sql;
RESET search_path;", cancellationToken: TestContext.Current.CancellationToken);
        await using var command = new PgSqlCommand("schema1sproc", conn) { CommandType = CommandType.StoredProcedure };

        //Act
        var ex = Assert.Throws<PostgresException>(() => PgSqlCommandBuilder.DeriveParameters(command));

        //Assert
        ex.SqlState.Should().Be(PostgresErrorCodes.UndefinedFunction);
    }

    // Tests if an exception is thrown if multiple functions with the specified name are in the search_path
    [Fact]
    public async Task DeriveParameters_throws_for_multiple_procedures_name_hits_in_search_path()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        var schema1 = await CreateTempSchema(conn);
        var schema2 = await CreateTempSchema(conn);

        await conn.ExecuteNonQueryAsync(
            $@"
CREATE PROCEDURE {schema1}.redundantsproc() AS 'SELECT 1' LANGUAGE sql;
CREATE PROCEDURE {schema1}.redundantsproc(IN param1 INT, IN param2 INT) AS 'SELECT param1 + param2' LANGUAGE sql;
SET search_path TO {schema1}, {schema2};", cancellationToken: TestContext.Current.CancellationToken);
        var command = new PgSqlCommand("redundantsproc", conn) { CommandType = CommandType.StoredProcedure };

        //Act
        var ex = Assert.Throws<PostgresException>(() => PgSqlCommandBuilder.DeriveParameters(command));

        //Assert
        ex.SqlState.Should().Be(PostgresErrorCodes.AmbiguousFunction);
    }

    #endregion DeriveParameters
}
