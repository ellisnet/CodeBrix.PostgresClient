using System;
using System.Data;
using System.Threading.Tasks;
using CodeBrix.PostgresClient.Properties;
using SilverAssertions;
using Xunit;

namespace CodeBrix.PostgresClient.Tests.Types; //was previously: Npgsql.Tests.Types;

public abstract class RecordTests(MultiplexingMode multiplexingMode) : MultiplexingTestBase(multiplexingMode)
{
    [Fact]
    [IssueLink("https://github.com/npgsql/npgsql/issues/724")]
    [IssueLink("https://github.com/npgsql/npgsql/issues/1980")]
    public async Task Read_Record_as_object_array()
    {
        var recordLiteral = "(1,'foo'::text)::record";
        await using var conn = await OpenConnectionAsync();
        await using var cmd = new PgSqlCommand($"SELECT {recordLiteral}, ARRAY[{recordLiteral}, {recordLiteral}]", conn);
        await using var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        reader.Read();

        var record = (object[])reader[0];
        record[0].Should().Be(1);
        record[1].Should().Be("foo");

        var array = (object[][])reader[1];
        array.Length.Should().Be(2);
        array[0][0].Should().Be(1);
        array[1][0].Should().Be(1);
    }

    [Fact]
    public async Task Read_Record_as_ValueTuple()
    {
        await using var dataSource = CreateDataSource(b => b.EnableRecordsAsTuples());
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        var recordLiteral = "(1,'foo'::text)::record";
        await using var cmd = new PgSqlCommand($"SELECT {recordLiteral}, ARRAY[{recordLiteral}, {recordLiteral}]", conn);
        await using var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        reader.Read();

        var record = reader.GetFieldValue<(int, string)>(0);
        record.Item1.Should().Be(1);
        record.Item2.Should().Be("foo");

        var array = reader.GetFieldValue<(int, string)[]>(1);
        array.Length.Should().Be(2);
        array[0].Item1.Should().Be(1);
        array[0].Item2.Should().Be("foo");
        array[1].Item1.Should().Be(1);
        array[1].Item2.Should().Be("foo");
    }

    [Fact]
    public async Task Read_Record_as_Tuple()
    {
        await using var dataSource = CreateDataSource(b => b.EnableRecordsAsTuples());
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        var recordLiteral = "(1,'foo'::text)::record";
        await using var cmd = new PgSqlCommand($"SELECT {recordLiteral}, ARRAY[{recordLiteral}, {recordLiteral}]", conn);
        await using var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        reader.Read();

        var record = reader.GetFieldValue<Tuple<int, string>>(0);
        record.Item1.Should().Be(1);
        record.Item2.Should().Be("foo");

        var array = reader.GetFieldValue<Tuple<int, string>[]>(1);
        array.Length.Should().Be(2);
        array[0].Item1.Should().Be(1);
        array[0].Item2.Should().Be("foo");
        array[1].Item1.Should().Be(1);
        array[1].Item2.Should().Be("foo");
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/1238")]
    public async Task record_with_non_int_field()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        await using var cmd = new PgSqlCommand("SELECT ('one'::TEXT, 2)", conn);
        //Act
        await using var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        reader.Read();
        var record = reader.GetFieldValue<object[]>(0);
        //Assert
        record[0].Should().Be("one");
        record[1].Should().Be(2);
    }

    [Fact]
    public async Task as_ValueTuple_supported_only_with_EnableRecordsAsTuples()
    {
        //Arrange
        await using var connection = await DataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using var command = new PgSqlCommand("SELECT (1, 'foo')::record", connection);
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        await reader.ReadAsync(TestContext.Current.CancellationToken);

        var errorMessage = string.Format(
            PgSqlStrings.RecordsNotEnabled,
            nameof(PgSqlSlimDataSourceBuilder.EnableRecordsAsTuples),
            nameof(PgSqlDataSourceBuilder),
            nameof(PgSqlSlimDataSourceBuilder.EnableRecords));

        //Assert
        var exception = Assert.Throws<InvalidCastException>(() => reader.GetFieldValue<(int, string)>(0));
        exception.InnerException.Should().BeAssignableTo<NotSupportedException>();
        exception.InnerException.Message.Should().Be(errorMessage);
    }

    [Fact]
    public async Task records_not_supported_by_default_on_PgSqlSlimSourceBuilder()
    {
        var dataSourceBuilder = new PgSqlSlimDataSourceBuilder(ConnectionString);
        await using var dataSource = dataSourceBuilder.Build();
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using var cmd = conn.CreateCommand();

        // RecordHandler doesn't support writing, so we only check for reading
        cmd.CommandText = "SELECT ('one'::text, 2)";
        await using var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        await reader.ReadAsync(TestContext.Current.CancellationToken);

        var errorMessage = string.Format(
            PgSqlStrings.RecordsNotEnabled,
            nameof(PgSqlSlimDataSourceBuilder.EnableRecordsAsTuples),
            nameof(PgSqlSlimDataSourceBuilder),
            nameof(PgSqlSlimDataSourceBuilder.EnableRecords));

        var exception = Assert.Throws<InvalidCastException>(() => reader.GetValue(0));
        exception.InnerException.Should().BeAssignableTo<NotSupportedException>();
        exception.InnerException.Message.Should().Be(errorMessage);

        exception = Assert.Throws<InvalidCastException>(() => reader.GetFieldValue<object[]>(0));
        exception.InnerException.Should().BeAssignableTo<NotSupportedException>();
        exception.InnerException.Message.Should().Be(errorMessage);
    }

    [Fact]
    public async Task PgSqlSlimSourceBuilder_EnableRecords()
    {
        //Arrange
        var dataSourceBuilder = new PgSqlSlimDataSourceBuilder(ConnectionString);
        dataSourceBuilder.EnableRecords();
        await using var dataSource = dataSourceBuilder.Build();
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using var cmd = conn.CreateCommand();

        // RecordHandler doesn't support writing, so we only check for reading
        cmd.CommandText = "SELECT ('one'::text, 2)";
        await using var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        await reader.ReadAsync(TestContext.Current.CancellationToken);

        //Assert
        FluentActions.Invoking(() => reader.GetValue(0)).Should().NotThrow();
        FluentActions.Invoking(() => reader.GetFieldValue<object[]>(0)).Should().NotThrow();
    }
}

public sealed class RecordTests_NonMultiplexing() : RecordTests(MultiplexingMode.NonMultiplexing);
public sealed class RecordTests_Multiplexing() : RecordTests(MultiplexingMode.Multiplexing);
