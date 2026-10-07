using System;
using System.Data;
using System.Threading.Tasks;
using CodeBrix.PostgresClient.PgSqlTypes;
using SilverAssertions;
using Xunit;

namespace CodeBrix.PostgresClient.Tests; //was previously: Npgsql.Tests;

public abstract class CommandParameterTests(MultiplexingMode multiplexingMode) : MultiplexingTestBase(multiplexingMode)
{
    [Theory]
    [InlineData(CommandBehavior.Default)]
    [InlineData(CommandBehavior.SequentialAccess)]
    public async Task input_and_output_parameters(CommandBehavior behavior)
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        using var cmd = new PgSqlCommand("SELECT @c-1 AS c, @a+2 AS b", conn);
        cmd.Parameters.Add(new PgSqlParameter("a", 3));
        var b = new PgSqlParameter { ParameterName = "b", Direction = ParameterDirection.Output };
        cmd.Parameters.Add(b);
        var c = new PgSqlParameter { ParameterName = "c", Direction = ParameterDirection.InputOutput, Value = 4 };
        cmd.Parameters.Add(c);

        //Act
        using (await cmd.ExecuteReaderAsync(behavior, TestContext.Current.CancellationToken))
        {
            //Assert
            b.Value.Should().Be(5);
            c.Value.Should().Be(3);
        }
    }

    [Theory]
    [InlineData(PrepareOrNot.NotPrepared)]
    [InlineData(PrepareOrNot.Prepared)]
    public async Task send_PgSqlDbType_unknown(PrepareOrNot prepare)
    {
        //Arrange
        if (prepare == PrepareOrNot.Prepared && IsMultiplexing)
            return;

        using var conn = await OpenConnectionAsync();
        using var cmd = new PgSqlCommand("SELECT @p::TIMESTAMP", conn);
        cmd.CommandText = "SELECT @p::TIMESTAMP";
        cmd.Parameters.Add(new PgSqlParameter("p", PgSqlDbType.Unknown) { Value = "2008-1-1" });
        if (prepare == PrepareOrNot.Prepared)
            cmd.Prepare();

        //Act
        using var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        reader.Read();

        //Assert
        reader.GetValue(0).Should().Be(new DateTime(2008, 1, 1));
    }

    [Fact]
    public async Task positional_parameter()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        await using var cmd = new PgSqlCommand("SELECT $1", conn);
        cmd.Parameters.Add(new PgSqlParameter { PgSqlDbType = PgSqlDbType.Integer, Value = 8 });

        //Assert
        (await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken)).Should().Be(8);
    }

    [Fact]
    public async Task positional_parameters_are_not_supported_with_legacy_batching()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        await using var cmd = new PgSqlCommand("SELECT $1; SELECT $1", conn);
        cmd.Parameters.Add(new PgSqlParameter { PgSqlDbType = PgSqlDbType.Integer, Value = 8 });

        //Act
        var ex = await Assert.ThrowsAsync<PostgresException>(async () => await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken));

        //Assert
        ex.SqlState.Should().Be(PostgresErrorCodes.SyntaxError);
    }

    [Fact]
    public async Task unreferenced_named_parameter_works()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        await using var cmd = new PgSqlCommand("SELECT 1", conn);
        cmd.Parameters.AddWithValue("not_used", 8);

        //Assert
        (await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken)).Should().Be(1);
    }

    [Fact]
    public async Task unreferenced_positional_parameter_works()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        await using var cmd = new PgSqlCommand("SELECT 1", conn);
        cmd.Parameters.Add(new PgSqlParameter { Value = 8 });

        //Assert
        (await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken)).Should().Be(1);
    }

    [Fact]
    public async Task mixing_positional_and_named_parameters_is_not_supported()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        await using var cmd = new PgSqlCommand("SELECT $1, @p", conn);
        cmd.Parameters.Add(new PgSqlParameter { Value = 8 });
        cmd.Parameters.Add(new PgSqlParameter { ParameterName = "p", Value = 9 });

        //Assert
        await Assert.ThrowsAsync<NotSupportedException>(() => cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    [IssueLink("https://github.com/npgsql/npgsql/issues/4171")]
    public async Task reuse_command_with_different_parameter_placeholder_types()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();

        //Act
        cmd.CommandText = "SELECT @p1";
        cmd.Parameters.AddWithValue("@p1", 8);
        _ = await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken);

        cmd.CommandText = "SELECT $1";
        cmd.Parameters[0].ParameterName = null;
        _ = await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task positional_output_parameters_are_not_supported()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        await using var cmd = new PgSqlCommand("SELECT $1", conn);
        cmd.Parameters.Add(new PgSqlParameter { Value = 8, Direction = ParameterDirection.InputOutput });

        //Assert
        await Assert.ThrowsAsync<NotSupportedException>(() => cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Parameters_get_name()
    {
        //Arrange
        var command = new PgSqlCommand();

        // Add parameters.
        command.Parameters.Add(new PgSqlParameter(":Parameter1", DbType.Boolean));
        command.Parameters.Add(new PgSqlParameter(":Parameter2", DbType.Int32));
        command.Parameters.Add(new PgSqlParameter(":Parameter3", DbType.DateTime));
        command.Parameters.Add(new PgSqlParameter("Parameter4", DbType.DateTime));

        //Act
        var idbPrmtr = command.Parameters["Parameter1"];
        idbPrmtr.Should().NotBeNull();
        command.Parameters[0].Value = 1;

        //Assert
        // Get by indexers.

        command.Parameters["Parameter1"].ParameterName.Should().Be(":Parameter1");
        command.Parameters["Parameter2"].ParameterName.Should().Be(":Parameter2");
        command.Parameters["Parameter3"].ParameterName.Should().Be(":Parameter3");
        command.Parameters["Parameter4"].ParameterName.Should().Be("Parameter4"); //Should this work?

        command.Parameters[0].ParameterName.Should().Be(":Parameter1");
        command.Parameters[1].ParameterName.Should().Be(":Parameter2");
        command.Parameters[2].ParameterName.Should().Be(":Parameter3");
        command.Parameters[3].ParameterName.Should().Be("Parameter4");
    }

    [Fact]
    public async Task same_param_multiple_times()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        using var cmd = new PgSqlCommand("SELECT @p1, @p1", conn);
        cmd.Parameters.AddWithValue("@p1", 8);

        //Act
        using var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        reader.Read();

        //Assert
        reader[0].Should().Be(8);
        reader[1].Should().Be(8);
    }

    [Fact]
    public async Task generic_parameter()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        using var cmd = new PgSqlCommand("SELECT @p1, @p2, @p3, @p4", conn);
        cmd.Parameters.Add(new PgSqlParameter<int>("p1", 8));
        cmd.Parameters.Add(new PgSqlParameter<short>("p2", 8) { PgSqlDbType = PgSqlDbType.Integer });
        cmd.Parameters.Add(new PgSqlParameter<string>("p3", "hello"));
        cmd.Parameters.Add(new PgSqlParameter<char[]>("p4", ['f', 'o', 'o']));

        //Act
        using var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        reader.Read();

        //Assert
        reader.GetInt32(0).Should().Be(8);
        reader.GetInt32(1).Should().Be(8);
        reader.GetString(2).Should().Be("hello");
        reader.GetString(3).Should().Be("foo");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task parameter_must_be_set(bool genericParam)
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        await using var cmd = new PgSqlCommand("SELECT @p1::TEXT", conn);
        cmd.Parameters.Add(
            genericParam
                ? new PgSqlParameter<object>("p1", null)
                : new PgSqlParameter("p1", null)
        );

        //Act
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () => await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken));

        //Assert
        ex.Message.Should().Be("Parameter 'p1' must have either its DbType, PgSqlDbType, DataTypeName or its Value set.");
    }

    [Fact]
    public async Task object_generic_param_does_runtime_lookup()
    {
        await AssertTypeWrite<object>(1, "1", "integer", PgSqlDbType.Integer, DbType.Int32, DbType.Int32, isDefault: false,
            isPgSqlDbTypeInferredFromClrType: true, skipArrayCheck: true);
        await AssertTypeWrite<object>(new[] {1, 1}, "{1,1}", "integer[]", PgSqlDbType.Integer | PgSqlDbType.Array, isDefault: false,
            isPgSqlDbTypeInferredFromClrType: true, skipArrayCheck: true);
    }

    [Fact]
    public async Task object_generic_parameter_works()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        await using var cmd = new PgSqlCommand("SELECT $1", conn);
        cmd.Parameters.Add(new PgSqlParameter<object> { PgSqlDbType = PgSqlDbType.Integer, Value = 8 });

        //Assert
        (await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken)).Should().Be(8);
    }
}

public sealed class CommandParameterTests_NonMultiplexing() : CommandParameterTests(MultiplexingMode.NonMultiplexing);
public sealed class CommandParameterTests_Multiplexing() : CommandParameterTests(MultiplexingMode.Multiplexing);
