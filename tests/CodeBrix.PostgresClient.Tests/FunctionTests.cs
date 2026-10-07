using System;
using System.Data;
using System.Threading.Tasks;
using CodeBrix.PostgresClient.PgSqlTypes;
using CodeBrix.PostgresClient.PostgresTypes;
using CodeBrix.PostgresClient.Tests.Support;
using SilverAssertions;
using Xunit;
using static CodeBrix.PostgresClient.Tests.TestUtil;

namespace CodeBrix.PostgresClient.Tests; //was previously: Npgsql.Tests;

/// <summary>
/// A fixture for tests which interact with functions.
/// All tests should create functions in the pg_temp schema only to ensure there's no interaction between
/// the tests.
/// </summary>
[Collection(NonParallelCollection.Name)] // Manipulates the EnableStoredProcedureCompatMode global flag
public class FunctionTests : TestBase, IDisposable
{
#if DEBUG
    public FunctionTests() => PgSqlCommand.EnableStoredProcedureCompatMode = true;

    public void Dispose() => PgSqlCommand.EnableStoredProcedureCompatMode = false;
#else
    public FunctionTests()
        => Assert.Skip("Cannot test function invocation via CommandType.StoredProcedure since that depends on the global EnableStoredProcedureCompatMode compatibility flag");

    public void Dispose()
    {
    }
#endif

    // Simple function with no parameters, results accessed as a resultset
    [Fact]
    public async Task resultset()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        var function = await GetTempFunctionName(conn);
        await conn.ExecuteNonQueryAsync($"CREATE FUNCTION {function}() RETURNS integer AS 'SELECT 8' LANGUAGE sql", cancellationToken: TestContext.Current.CancellationToken);
        await using var cmd = new PgSqlCommand(function, conn) { CommandType = CommandType.StoredProcedure };
        //Assert
        (await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken)).Should().Be(8);
    }

    // Basic function call with an in parameter
    [Fact]
    public async Task param_input()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        var function = await GetTempFunctionName(conn);
        await conn.ExecuteNonQueryAsync($"CREATE FUNCTION {function}(IN param text) RETURNS text AS 'SELECT param' LANGUAGE sql", cancellationToken: TestContext.Current.CancellationToken);
        await using var cmd = new PgSqlCommand(function, conn);
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.Parameters.AddWithValue("@param", "hello");
        //Assert
        (await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken)).Should().Be("hello");
    }

    // Basic function call with an out parameter
    [Fact]
    public async Task param_output()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        var function = await GetTempFunctionName(conn);
        await conn.ExecuteNonQueryAsync(@$"
CREATE FUNCTION {function} (IN param_in text, OUT param_out text) AS $$
BEGIN
    param_out=param_in;
END
$$ LANGUAGE plpgsql", cancellationToken: TestContext.Current.CancellationToken);
        await using var cmd = new PgSqlCommand(function, conn);
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.Parameters.AddWithValue("@param_in", "hello");
        var outParam = new PgSqlParameter("param_out", DbType.String) { Direction = ParameterDirection.Output };
        cmd.Parameters.Add(outParam);
        //Act
        await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        //Assert
        outParam.Value.Should().Be("hello");
    }

    // Basic function call with an in/out parameter
    [Fact]
    public async Task param_inputOutput()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        var function = await GetTempFunctionName(conn);
        await conn.ExecuteNonQueryAsync($@"
CREATE FUNCTION {function} (INOUT param integer) AS $$
BEGIN
    param=param+1;
END
$$ LANGUAGE plpgsql", cancellationToken: TestContext.Current.CancellationToken);
        await using var cmd = new PgSqlCommand(function, conn);
        cmd.CommandType = CommandType.StoredProcedure;
        var outParam = new PgSqlParameter("param", DbType.Int32)
        {
            Direction = ParameterDirection.InputOutput,
            Value = 8
        };
        cmd.Parameters.Add(outParam);
        //Act
        await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        //Assert
        outParam.Value.Should().Be(9);
    }

    [Fact]
    public async Task void_function()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        MinimumPgVersion(conn, "9.1.0", "no binary output function available for type void before 9.1.0");
        var command = new PgSqlCommand("pg_sleep", conn);
        command.Parameters.AddWithValue(0);
        command.CommandType = CommandType.StoredProcedure;
        //Act
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task named_parameters()
    {
        await using var conn = await OpenConnectionAsync();
        MinimumPgVersion(conn, "9.4.0", "make_timestamp was introduced in 9.4");
        await using var command = new PgSqlCommand("make_timestamp", conn);
        command.CommandType = CommandType.StoredProcedure;
        command.Parameters.AddWithValue("year", 2015);
        command.Parameters.AddWithValue("month", 8);
        command.Parameters.AddWithValue("mday", 1);
        command.Parameters.AddWithValue("hour", 2);
        command.Parameters.AddWithValue("min", 3);
        command.Parameters.AddWithValue("sec", 4);
        var dt = (DateTime)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));

        dt.Should().Be(new DateTime(2015, 8, 1, 2, 3, 4));

        command.Parameters[0].Value = 2014;
        command.Parameters[0].ParameterName = ""; // 2014 will be sent as a positional parameter
        dt = (DateTime)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
        dt.Should().Be(new DateTime(2014, 8, 1, 2, 3, 4));
    }

    [Fact]
    public async Task too_many_output_params()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        var command = new PgSqlCommand("VALUES (4,5), (6,7)", conn);
        command.Parameters.Add(new PgSqlParameter("a", DbType.Int32)
        {
            Direction = ParameterDirection.Output,
            Value = -1
        });
        command.Parameters.Add(new PgSqlParameter("b", DbType.Int32)
        {
            Direction = ParameterDirection.Output,
            Value = -1
        });
        command.Parameters.Add(new PgSqlParameter("c", DbType.Int32)
        {
            Direction = ParameterDirection.Output,
            Value = -1
        });

        //Act
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);

        //Assert
        command.Parameters["a"].Value.Should().Be(4);
        command.Parameters["b"].Value.Should().Be(5);
        command.Parameters["c"].Value.Should().Be(-1);
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/5793")]
    public async Task ReturnValue_parameter_ignored()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        var funcName = await GetTempFunctionName(conn);
        await conn.ExecuteNonQueryAsync(@$"CREATE FUNCTION {funcName}() RETURNS integer AS 'SELECT 8;' LANGUAGE 'sql'", cancellationToken: TestContext.Current.CancellationToken);
        await using var cmd = new PgSqlCommand(funcName, conn) { CommandType = CommandType.StoredProcedure };
        var param = new PgSqlParameter
        {
            ParameterName = "@ReturnValue",
            PgSqlDbType = PgSqlDbType.Integer,
            Direction = ParameterDirection.ReturnValue,
            Value = 0
        };
        cmd.Parameters.Add(param);
        //Assert
        cmd.ExecuteScalar().Should().Be(8);
        param.Value.Should().Be(0);
    }

    [Fact]
    public async Task CommandBehavior_SchemaOnly_support_function_call()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        var function = await GetTempFunctionName(conn);

        await conn.ExecuteNonQueryAsync($"CREATE OR REPLACE FUNCTION {function}() RETURNS SETOF integer as 'SELECT 1;' LANGUAGE 'sql';", cancellationToken: TestContext.Current.CancellationToken);
        var command = new PgSqlCommand(function, conn) { CommandType = CommandType.StoredProcedure };
        await using var dr = await command.ExecuteReaderAsync(CommandBehavior.SchemaOnly, cancellationToken: TestContext.Current.CancellationToken);
        var i = 0;
        //Act
        while (dr.Read())
            i++;
        //Assert
        i.Should().Be(0);
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/5820")]
    public async Task output_param_cast_error()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        var function = await GetTempFunctionName(conn);
        await conn.ExecuteNonQueryAsync(@$"
CREATE FUNCTION {function} (INOUT param_in int4, OUT param_out interval) AS $$
BEGIN
    param_out = interval '5 years';
END
$$ LANGUAGE plpgsql", cancellationToken: TestContext.Current.CancellationToken);
        await using var cmd = new PgSqlCommand(function, conn);
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.Parameters.Add(new PgSqlParameter("param_in", DbType.Int32)
        {
            Direction = ParameterDirection.InputOutput,
            Value = 1
        });
        cmd.Parameters.Add(new PgSqlParameter("param_out", PgSqlDbType.Interval)
        {
            Direction = ParameterDirection.Output
        });
        //Assert
        await Assert.ThrowsAsync<InvalidCastException>(() => cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
        var act = async () => await conn.ExecuteNonQueryAsync("SELECT 1", cancellationToken: TestContext.Current.CancellationToken);
        await act.Should().NotThrowAsync();
    }

    #region DeriveParameters

    // Tests function parameter derivation with IN, OUT and INOUT parameters
    [Fact]
    public async Task DeriveParameters_function_various()
    {
        await using var conn = await OpenConnectionAsync();
        var function = await GetTempFunctionName(conn);

        // This function returns record because of the two Out (InOut & Out) parameters
        await conn.ExecuteNonQueryAsync($@"
CREATE FUNCTION {function}(IN param1 INT, OUT param2 text, INOUT param3 INT) RETURNS record AS $$
BEGIN
    param2 = 'sometext';
    param3 = param1 + param3;
END;
$$ LANGUAGE plpgsql", cancellationToken: TestContext.Current.CancellationToken);

        await using var cmd = new PgSqlCommand(function, conn) { CommandType = CommandType.StoredProcedure };
        PgSqlCommandBuilder.DeriveParameters(cmd);
        cmd.Parameters.Should().HaveCount(3);
        cmd.Parameters[0].Direction.Should().Be(ParameterDirection.Input);
        cmd.Parameters[0].PgSqlDbType.Should().Be(PgSqlDbType.Integer);
        cmd.Parameters[0].PostgresType.Should().BeOfType<PostgresBaseType>();
        cmd.Parameters[0].DataTypeName.Should().Be("integer");
        cmd.Parameters[0].ParameterName.Should().Be("param1");
        cmd.Parameters[1].Direction.Should().Be(ParameterDirection.Output);
        cmd.Parameters[1].PgSqlDbType.Should().Be(PgSqlDbType.Text);
        cmd.Parameters[1].PostgresType.Should().BeOfType<PostgresBaseType>();
        cmd.Parameters[1].DataTypeName.Should().Be("text");
        cmd.Parameters[1].ParameterName.Should().Be("param2");
        cmd.Parameters[2].Direction.Should().Be(ParameterDirection.InputOutput);
        cmd.Parameters[2].PgSqlDbType.Should().Be(PgSqlDbType.Integer);
        cmd.Parameters[2].PostgresType.Should().BeOfType<PostgresBaseType>();
        cmd.Parameters[2].DataTypeName.Should().Be("integer");
        cmd.Parameters[2].ParameterName.Should().Be("param3");
        cmd.Parameters[0].Value = 5;
        cmd.Parameters[2].Value = 4;
        await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        cmd.Parameters[0].Value.Should().Be(5);
        cmd.Parameters[1].Value.Should().Be("sometext");
        cmd.Parameters[2].Value.Should().Be(9);
    }

    // Tests function parameter derivation with IN-only parameters
    [Fact]
    public async Task DeriveParameters_function_in_only()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        var function = await GetTempFunctionName(conn);

        // This function returns record because of the two Out (InOut & Out) parameters
        await conn.ExecuteNonQueryAsync(
            $@"CREATE FUNCTION {function}(IN param1 INT, IN param2 INT) RETURNS int AS 'SELECT param1 + param2' LANGUAGE sql", cancellationToken: TestContext.Current.CancellationToken);

        await using var cmd = new PgSqlCommand(function, conn) { CommandType = CommandType.StoredProcedure };
        //Act
        PgSqlCommandBuilder.DeriveParameters(cmd);
        //Assert
        cmd.Parameters.Should().HaveCount(2);
        cmd.Parameters[0].Direction.Should().Be(ParameterDirection.Input);
        cmd.Parameters[1].Direction.Should().Be(ParameterDirection.Input);
        cmd.Parameters[0].Value = 5;
        cmd.Parameters[1].Value = 4;
        (await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken)).Should().Be(9);
    }

    // Tests function parameter derivation with no parameters
    [Fact]
    public async Task DeriveParameters_function_no_params()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        var function = await GetTempFunctionName(conn);

        await conn.ExecuteNonQueryAsync($@"CREATE FUNCTION {function}() RETURNS int AS 'SELECT 4' LANGUAGE sql", cancellationToken: TestContext.Current.CancellationToken);

        await using var cmd = new PgSqlCommand(function, conn) { CommandType = CommandType.StoredProcedure };
        //Act
        PgSqlCommandBuilder.DeriveParameters(cmd);
        //Assert
        cmd.Parameters.Should().BeEmpty();
    }

    [Fact]
    public async Task DeriveParameters_function_with_case_sensitive_name()
    {
        await using var conn = await OpenConnectionAsync();
        await conn.ExecuteNonQueryAsync(
            @"CREATE OR REPLACE FUNCTION ""FunctionCaseSensitive""(int4, text) RETURNS int4 AS 'SELECT 0' LANGUAGE sql", cancellationToken: TestContext.Current.CancellationToken);

        try
        {
            await using var command = new PgSqlCommand(@"""FunctionCaseSensitive""", conn) { CommandType = CommandType.StoredProcedure };
            PgSqlCommandBuilder.DeriveParameters(command);
            command.Parameters[0].PgSqlDbType.Should().Be(PgSqlDbType.Integer);
            command.Parameters[1].PgSqlDbType.Should().Be(PgSqlDbType.Text);
        }
        finally
        {
            await conn.ExecuteNonQueryAsync(@"DROP FUNCTION ""FunctionCaseSensitive""", cancellationToken: TestContext.Current.CancellationToken);
        }
    }

    // Tests function parameter derivation for quoted functions with double quotes in the name works
    [Fact]
    public async Task DeriveParameters_quote_characters_in_function_name()
    {
        await using var conn = await OpenConnectionAsync();
        var function = @"""""""FunctionQuote""""CharactersInName""""""";
        await conn.ExecuteNonQueryAsync($"CREATE OR REPLACE FUNCTION {function}(int4, text) RETURNS int4 AS 'SELECT 0' LANGUAGE sql", cancellationToken: TestContext.Current.CancellationToken);

        try
        {
            await using var command = new PgSqlCommand(function, conn) { CommandType = CommandType.StoredProcedure };
            PgSqlCommandBuilder.DeriveParameters(command);
            command.Parameters[0].PgSqlDbType.Should().Be(PgSqlDbType.Integer);
            command.Parameters[1].PgSqlDbType.Should().Be(PgSqlDbType.Text);
        }
        finally
        {
            await conn.ExecuteNonQueryAsync("DROP FUNCTION " + function, cancellationToken: TestContext.Current.CancellationToken);
        }
    }

    // Tests function parameter derivation for quoted functions with dots in the name works
    [Fact]
    public async Task DeriveParameters_dot_character_in_function_name()
    {
        await using var conn = await OpenConnectionAsync();
        await conn.ExecuteNonQueryAsync(
            @"CREATE OR REPLACE FUNCTION ""My.Dotted.Function""(int4, text) RETURNS int4 AS 'SELECT 0' LANGUAGE sql", cancellationToken: TestContext.Current.CancellationToken);

        try
        {
            await using var command = new PgSqlCommand(@"""My.Dotted.Function""", conn) { CommandType = CommandType.StoredProcedure };
            PgSqlCommandBuilder.DeriveParameters(command);
            command.Parameters[0].PgSqlDbType.Should().Be(PgSqlDbType.Integer);
            command.Parameters[1].PgSqlDbType.Should().Be(PgSqlDbType.Text);
        }
        finally
        {
            await conn.ExecuteNonQueryAsync(@"DROP FUNCTION ""My.Dotted.Function""", cancellationToken: TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task DeriveParameters_parameter_name_from_function()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        var function = await GetTempFunctionName(conn);

        await conn.ExecuteNonQueryAsync(
            $"CREATE FUNCTION {function}(x int, y int, out sum int, out product int) AS 'SELECT $1 + $2, $1 * $2' LANGUAGE sql", cancellationToken: TestContext.Current.CancellationToken);
        await using var command = new PgSqlCommand(function, conn) { CommandType = CommandType.StoredProcedure };
        //Act
        PgSqlCommandBuilder.DeriveParameters(command);
        //Assert
        command.Parameters[0].ParameterName.Should().Be("x");
        command.Parameters[1].ParameterName.Should().Be("y");
    }

    [Fact]
    public async Task DeriveParameters_non_existing_function()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        var invalidCommandName = new PgSqlCommand("invalidfunctionname", conn) { CommandType = CommandType.StoredProcedure };
        //Act
        var act = () => PgSqlCommandBuilder.DeriveParameters(invalidCommandName);
        //Assert
        act.Should().ThrowExactly<PostgresException>().Where(e => e.SqlState == PostgresErrorCodes.UndefinedFunction);
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/1212")]
    public async Task DeriveParameters_function_with_table_parameters()
    {
        await using var conn = await OpenConnectionAsync();
        MinimumPgVersion(conn, "9.2.0");
        var function = await GetTempFunctionName(conn);

        // This function returns record because of the two Out (InOut & Out) parameters
        await conn.ExecuteNonQueryAsync(
            $"CREATE FUNCTION {function}(IN in1 INT) RETURNS TABLE(t1 INT, t2 INT) AS 'SELECT in1, in1+1' LANGUAGE sql", cancellationToken: TestContext.Current.CancellationToken);

        await using var cmd = new PgSqlCommand(function, conn) { CommandType = CommandType.StoredProcedure };
        PgSqlCommandBuilder.DeriveParameters(cmd);
        cmd.Parameters.Should().HaveCount(3);
        cmd.Parameters[0].Direction.Should().Be(ParameterDirection.Input);
        cmd.Parameters[1].Direction.Should().Be(ParameterDirection.Output);
        cmd.Parameters[2].Direction.Should().Be(ParameterDirection.Output);
        cmd.Parameters[0].Value = 5;
        await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        cmd.Parameters[1].Value.Should().Be(5);
        cmd.Parameters[2].Value.Should().Be(6);
    }

    // Tests if the right function according to search_path is used in function parameter derivation
    [Fact]
    public async Task DeriveParameters_function_correct_schema_resolution()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        var schema1 = await CreateTempSchema(conn);
        var schema2 = await CreateTempSchema(conn);

        await conn.ExecuteNonQueryAsync($@"
CREATE FUNCTION {schema1}.redundantfunc() RETURNS int AS 'SELECT 1' LANGUAGE sql;
CREATE FUNCTION {schema2}.redundantfunc(IN param1 INT, IN param2 INT) RETURNS int AS 'SELECT param1 + param2' LANGUAGE sql;
SET search_path TO {schema2};", cancellationToken: TestContext.Current.CancellationToken);
        await using var command = new PgSqlCommand("redundantfunc", conn) { CommandType = CommandType.StoredProcedure };
        //Act
        PgSqlCommandBuilder.DeriveParameters(command);
        //Assert
        command.Parameters.Should().HaveCount(2);
        command.Parameters[0].Direction.Should().Be(ParameterDirection.Input);
        command.Parameters[1].Direction.Should().Be(ParameterDirection.Input);
        command.Parameters[0].Value = 5;
        command.Parameters[1].Value = 4;
        (await command.ExecuteScalarAsync(TestContext.Current.CancellationToken)).Should().Be(9);
    }

    // Tests if function parameter derivation throws an exception if the specified function is not in the search_path
    [Fact]
    public async Task DeriveParameters_throws_for_existing_function_that_is_not_in_search_path()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        var schema = await CreateTempSchema(conn);

        await conn.ExecuteNonQueryAsync($@"
CREATE FUNCTION {schema}.schema1func() RETURNS int AS 'SELECT 1' LANGUAGE sql;
RESET search_path;", cancellationToken: TestContext.Current.CancellationToken);
        await using var command = new PgSqlCommand("schema1func", conn) { CommandType = CommandType.StoredProcedure };
        //Act
        var act = () => PgSqlCommandBuilder.DeriveParameters(command);
        //Assert
        act.Should().ThrowExactly<PostgresException>().Where(e => e.SqlState == PostgresErrorCodes.UndefinedFunction);
    }

    // Tests if an exception is thrown if multiple functions with the specified name are in the search_path
    [Fact]
    public async Task DeriveParameters_throws_for_multiple_function_name_hits_in_search_path()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        var schema1 = await CreateTempSchema(conn);
        var schema2 = await CreateTempSchema(conn);

        await conn.ExecuteNonQueryAsync(
            $@"
CREATE FUNCTION {schema1}.redundantfunc() RETURNS int AS 'SELECT 1' LANGUAGE sql;
CREATE FUNCTION {schema1}.redundantfunc(IN param1 INT, IN param2 INT) RETURNS int AS 'SELECT param1 + param2' LANGUAGE sql;
SET search_path TO {schema1}, {schema2};", cancellationToken: TestContext.Current.CancellationToken);
        var command = new PgSqlCommand("redundantfunc", conn) { CommandType = CommandType.StoredProcedure };
        //Act
        var act = () => PgSqlCommandBuilder.DeriveParameters(command);
        //Assert
        act.Should().ThrowExactly<PostgresException>().Where(e => e.SqlState == PostgresErrorCodes.AmbiguousFunction);
    }

    #region Set returning functions

    // Tests parameter derivation for a function that returns SETOF sometype
    [Fact]
    public async Task DeriveParameters_function_returning_setof_type()
    {
        await using var conn = await OpenConnectionAsync();
        MinimumPgVersion(conn, "9.2.0");

        var table = await GetTempTableName(conn);
        var function = await GetTempFunctionName(conn);

        // This function returns record because of the two Out (InOut & Out) parameters
        await conn.ExecuteNonQueryAsync($@"
CREATE TABLE {table} (fooid int, foosubid int, fooname text);
INSERT INTO {table} VALUES (1, 1, 'Joe'), (1, 2, 'Ed'), (2, 1, 'Mary');
CREATE FUNCTION {function}(int) RETURNS SETOF {table} AS $$
    SELECT * FROM {table} WHERE {table}.fooid = $1 ORDER BY {table}.foosubid;
$$ LANGUAGE sql", cancellationToken: TestContext.Current.CancellationToken);

        await using var cmd = new PgSqlCommand(function, conn) { CommandType = CommandType.StoredProcedure };
        PgSqlCommandBuilder.DeriveParameters(cmd);
        cmd.Parameters.Should().HaveCount(4);
        cmd.Parameters[0].Direction.Should().Be(ParameterDirection.Input);
        cmd.Parameters[1].Direction.Should().Be(ParameterDirection.Output);
        cmd.Parameters[2].Direction.Should().Be(ParameterDirection.Output);
        cmd.Parameters[3].Direction.Should().Be(ParameterDirection.Output);
        cmd.Parameters[0].Value = 1;
        await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        cmd.Parameters[0].Value.Should().Be(1);
    }

    // Tests parameter derivation for a function that returns TABLE
    [Fact]
    public async Task DeriveParameters_function_returning_table()
    {
        await using var conn = await OpenConnectionAsync();
        MinimumPgVersion(conn, "9.2.0");

        var table = await GetTempTableName(conn);
        var function = await GetTempFunctionName(conn);

        // This function returns record because of the two Out (InOut & Out) parameters
        await conn.ExecuteNonQueryAsync($@"
CREATE TABLE {table} (fooid int, foosubid int, fooname text);
INSERT INTO {table} VALUES (1, 1, 'Joe'), (1, 2, 'Ed'), (2, 1, 'Mary');
CREATE FUNCTION {function}(int) RETURNS TABLE(fooid int, foosubid int, fooname text) AS $$
    SELECT * FROM {table} WHERE {table}.fooid = $1 ORDER BY {table}.foosubid;
$$ LANGUAGE sql", cancellationToken: TestContext.Current.CancellationToken);

        await using var cmd = new PgSqlCommand(function, conn) { CommandType = CommandType.StoredProcedure };
        PgSqlCommandBuilder.DeriveParameters(cmd);
        cmd.Parameters.Should().HaveCount(4);
        cmd.Parameters[0].Direction.Should().Be(ParameterDirection.Input);
        cmd.Parameters[1].Direction.Should().Be(ParameterDirection.Output);
        cmd.Parameters[2].Direction.Should().Be(ParameterDirection.Output);
        cmd.Parameters[3].Direction.Should().Be(ParameterDirection.Output);
        cmd.Parameters[0].Value = 1;
        await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        cmd.Parameters[0].Value.Should().Be(1);
    }

    // Tests parameter derivation for a function that returns SETOF record
    [Fact]
    public async Task DeriveParameters_function_returning_setof_record()
    {
        await using var conn = await OpenConnectionAsync();
        MinimumPgVersion(conn, "9.2.0");

        var table = await GetTempTableName(conn);
        var function = await GetTempFunctionName(conn);

        // This function returns record because of the two Out (InOut & Out) parameters
        await conn.ExecuteNonQueryAsync($@"
CREATE TABLE {table} (fooid int, foosubid int, fooname text);
INSERT INTO {table} VALUES (1, 1, 'Joe'), (1, 2, 'Ed'), (2, 1, 'Mary');
CREATE FUNCTION {function}(int, OUT fooid int, OUT foosubid int, OUT fooname text) RETURNS SETOF record AS $$
    SELECT * FROM {table} WHERE {table}.fooid = $1 ORDER BY {table}.foosubid;
$$ LANGUAGE sql", cancellationToken: TestContext.Current.CancellationToken);

        await using var cmd = new PgSqlCommand(function, conn) { CommandType = CommandType.StoredProcedure };
        PgSqlCommandBuilder.DeriveParameters(cmd);
        cmd.Parameters.Should().HaveCount(4);
        cmd.Parameters[0].Direction.Should().Be(ParameterDirection.Input);
        cmd.Parameters[1].Direction.Should().Be(ParameterDirection.Output);
        cmd.Parameters[2].Direction.Should().Be(ParameterDirection.Output);
        cmd.Parameters[3].Direction.Should().Be(ParameterDirection.Output);
        cmd.Parameters[0].Value = 1;
        await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        cmd.Parameters[0].Value.Should().Be(1);
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/2022")]
    public async Task DeriveParameters_function_returning_setof_type_with_dropped_column()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        MinimumPgVersion(conn, "9.2.0");

        var table = await GetTempTableName(conn);
        var function = await GetTempFunctionName(conn);

        await conn.ExecuteNonQueryAsync($@"
CREATE TABLE {table} (id serial PRIMARY KEY, t1 text, t2 text);
CREATE FUNCTION {function}() RETURNS SETOF {table} AS 'SELECT * FROM {table}' LANGUAGE sql;
ALTER TABLE {table} DROP t2;", cancellationToken: TestContext.Current.CancellationToken);

        await using var cmd = new PgSqlCommand(function, conn) { CommandType = CommandType.StoredProcedure };
        //Act
        PgSqlCommandBuilder.DeriveParameters(cmd);
        //Assert
        cmd.Parameters.Should().HaveCount(2);
        cmd.Parameters[0].Direction.Should().Be(ParameterDirection.Output);
        cmd.Parameters[0].PgSqlDbType.Should().Be(PgSqlDbType.Integer);
        cmd.Parameters[1].Direction.Should().Be(ParameterDirection.Output);
        cmd.Parameters[1].PgSqlDbType.Should().Be(PgSqlDbType.Text);
    }

    #endregion

    #endregion DeriveParameters
}
