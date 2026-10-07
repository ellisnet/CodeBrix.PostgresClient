using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CodeBrix.PostgresClient.Tests.Support;
using SilverAssertions;
using Xunit;

namespace CodeBrix.PostgresClient.Tests;

public class PgSqlMapperTests
{
    public enum Status
    {
        Draft = 0,
        Active = 1,
        Retired = 2
    }

    public class Widget
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public decimal Price { get; set; }
        public Status Status { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateOnly ReleaseDate { get; set; }
        public Guid? ExternalId { get; set; }
        public int[] Tags { get; set; }
    }

    static PgSqlConnection CreateConnection()
        => new(TestDatabase.ConnectionString);

    static async Task<string> CreateWidgetTableAsync(PgSqlConnection connection)
    {
        var table = TestUtil.GetUniqueIdentifier("mapper_widgets_");
        await connection.ExecuteAsync(
            $"""
            CREATE TABLE {table} (
                id integer PRIMARY KEY,
                name text NOT NULL,
                price numeric(10,2) NOT NULL,
                status integer NOT NULL,
                created_at timestamp without time zone NOT NULL,
                release_date date NOT NULL,
                external_id uuid NULL,
                tags integer[] NULL
            )
            """,
            cancellationToken: TestContext.Current.CancellationToken);
        await connection.ExecuteAsync(
            $"""
            INSERT INTO {table} VALUES
                (1, 'Sprocket', 9.99, 1, '2026-01-02 03:04:05', '2026-02-01', 'a3bb189e-8bf9-3888-9912-ace4e6543002', ARRAY[1,2,3]),
                (2, 'Gizmo', 19.50, 0, '2026-03-04 05:06:07', '2026-04-01', NULL, NULL),
                (3, 'Doohickey', 5.00, 2, '2026-05-06 07:08:09', '2026-06-01', NULL, ARRAY[]::integer[])
            """,
            cancellationToken: TestContext.Current.CancellationToken);
        return table;
    }

    [Fact]
    public async Task QueryAsync_materializes_pocos_with_snake_case_columns()
    {
        //Arrange
        await using var connection = CreateConnection();
        var table = await CreateWidgetTableAsync(connection);

        //Act
        var widgets = (await connection.QueryAsync<Widget>($"SELECT * FROM {table} ORDER BY id",
            cancellationToken: TestContext.Current.CancellationToken)).ToList();

        //Assert
        widgets.Should().HaveCount(3);
        widgets[0].Name.Should().Be("Sprocket");
        widgets[0].Price.Should().Be(9.99m);
        widgets[0].Status.Should().Be(Status.Active);
        widgets[0].CreatedAt.Should().Be(new DateTime(2026, 1, 2, 3, 4, 5));
        widgets[0].ReleaseDate.Should().Be(new DateOnly(2026, 2, 1));
        widgets[0].ExternalId.Should().Be(Guid.Parse("a3bb189e-8bf9-3888-9912-ace4e6543002"));
        widgets[0].Tags.Should().Equal(1, 2, 3);
        widgets[1].ExternalId.Should().BeNull();
        widgets[1].Tags.Should().BeNull();
        widgets[2].Tags.Should().BeEmpty();
    }

    [Fact]
    public async Task Query_materializes_pocos_synchronously()
    {
        //Arrange
        await using var connection = CreateConnection();
        var table = await CreateWidgetTableAsync(connection);

        //Act
        var names = connection.Query<Widget>($"SELECT id, name FROM {table} WHERE price > @min ORDER BY id", new { min = 6m })
            .Select(w => w.Name)
            .ToList();

        //Assert
        names.Should().Equal("Sprocket", "Gizmo");
    }

    [Fact]
    public async Task Query_materializes_simple_types_from_the_first_column()
    {
        //Arrange
        await using var connection = CreateConnection();

        //Act
        var values = connection.Query<int>("SELECT generate_series(1, 4)").ToList();

        //Assert
        values.Should().Equal(1, 2, 3, 4);
    }

    [Fact]
    public async Task Query_returns_dynamic_rows()
    {
        //Arrange
        await using var connection = CreateConnection();

        //Act
        var row = connection.Query("SELECT 42 AS answer, 'hello'::text AS greeting, NULL::text AS nothing").Single();

        //Assert
        ((int)row.answer).Should().Be(42);
        ((string)row.greeting).Should().Be("hello");
        ((object)row.nothing).Should().BeNull();
    }

    [Fact]
    public async Task QueryAsync_returns_dynamic_rows()
    {
        //Arrange
        await using var connection = CreateConnection();

        //Act
        var rows = (await connection.QueryAsync("SELECT n FROM generate_series(1, 3) AS n",
            cancellationToken: TestContext.Current.CancellationToken)).ToList();

        //Assert
        rows.Select(r => (int)r.n).Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task QueryFirst_returns_the_first_row()
        => (await CreateConnection().QueryFirstAsync<int>("SELECT generate_series(7, 9)",
            cancellationToken: TestContext.Current.CancellationToken)).Should().Be(7);

    [Fact]
    public async Task QueryFirst_throws_when_there_are_no_rows()
    {
        //Arrange
        await using var connection = CreateConnection();

        //Act
        var act = () => connection.QueryFirst<int>("SELECT 1 WHERE false");

        //Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task QueryFirstOrDefault_returns_default_when_there_are_no_rows()
    {
        //Arrange
        await using var connection = CreateConnection();

        //Act
        var value = await connection.QueryFirstOrDefaultAsync<string>("SELECT 'x' WHERE false",
            cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        value.Should().BeNull();
    }

    [Fact]
    public async Task QuerySingle_throws_when_there_is_more_than_one_row()
    {
        //Arrange
        await using var connection = CreateConnection();

        //Act
        var act = () => connection.QuerySingleAsync<int>("SELECT generate_series(1, 2)",
            cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task QuerySingleOrDefault_returns_the_single_row()
    {
        //Arrange
        await using var connection = CreateConnection();

        //Act
        var value = connection.QuerySingleOrDefault<long>("SELECT @v::bigint", new { v = 12345678901L });

        //Assert
        value.Should().Be(12345678901L);
    }

    [Fact]
    public async Task Execute_returns_rows_affected_and_binds_parameters()
    {
        //Arrange
        await using var connection = CreateConnection();
        var table = await CreateWidgetTableAsync(connection);

        //Act
        var affected = await connection.ExecuteAsync($"UPDATE {table} SET price = price + @delta WHERE status = @status",
            new { delta = 1m, status = Status.Active }, cancellationToken: TestContext.Current.CancellationToken);
        var price = await connection.ExecuteScalarAsync<decimal>($"SELECT price FROM {table} WHERE id = 1",
            cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        affected.Should().Be(1);
        price.Should().Be(10.99m);
    }

    [Fact]
    public async Task Execute_binds_null_parameter_values_as_sql_null()
    {
        //Arrange
        await using var connection = CreateConnection();
        var table = await CreateWidgetTableAsync(connection);

        //Act
        connection.Execute($"UPDATE {table} SET external_id = @externalId WHERE id = 1", new { externalId = (Guid?)null });
        var isNull = connection.ExecuteScalar<bool>($"SELECT external_id IS NULL FROM {table} WHERE id = 1");

        //Assert
        isNull.Should().BeTrue();
    }

    [Fact]
    public async Task Execute_accepts_a_dictionary_of_parameters()
    {
        //Arrange
        await using var connection = CreateConnection();
        var parameters = new Dictionary<string, object> { ["a"] = 20, ["b"] = 22 };

        //Act
        var sum = connection.ExecuteScalar<int>("SELECT @a + @b", parameters);

        //Assert
        sum.Should().Be(42);
    }

    [Fact]
    public async Task Query_skips_properties_the_sql_does_not_reference()
    {
        //Arrange
        await using var connection = CreateConnection();

        //Act
        var value = connection.ExecuteScalar<int>("SELECT @used", new { used = 5, unused = "ignored" });

        //Assert
        value.Should().Be(5);
    }

    [Fact]
    public async Task Query_expands_sequences_into_in_lists()
    {
        //Arrange
        await using var connection = CreateConnection();
        var table = await CreateWidgetTableAsync(connection);

        //Act
        var names = (await connection.QueryAsync<string>($"SELECT name FROM {table} WHERE id IN @ids ORDER BY id",
            new { ids = new[] { 1, 3 } }, cancellationToken: TestContext.Current.CancellationToken)).ToList();

        //Assert
        names.Should().Equal("Sprocket", "Doohickey");
    }

    [Fact]
    public async Task Query_expands_an_empty_sequence_to_match_no_rows()
    {
        //Arrange
        await using var connection = CreateConnection();
        var table = await CreateWidgetTableAsync(connection);

        //Act
        var count = connection.Query<int>($"SELECT id FROM {table} WHERE id IN @ids", new { ids = Array.Empty<int>() }).Count();

        //Assert
        count.Should().Be(0);
    }

    [Fact]
    public async Task Query_binds_a_PgSqlParameter_as_a_postgres_array()
    {
        //Arrange
        await using var connection = CreateConnection();
        var table = await CreateWidgetTableAsync(connection);
        var ids = new PgSqlParameter { Value = new[] { 2, 3 } };

        //Act
        var names = connection.Query<string>($"SELECT name FROM {table} WHERE id = ANY(@ids) ORDER BY id",
            new Dictionary<string, object> { ["ids"] = ids }).ToList();

        //Assert
        names.Should().Equal("Gizmo", "Doohickey");
    }

    [Fact]
    public async Task ExecuteScalar_returns_default_for_null()
    {
        //Arrange
        await using var connection = CreateConnection();

        //Act
        var value = connection.ExecuteScalar<int?>("SELECT NULL::integer");

        //Assert
        value.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteReader_returns_an_open_reader()
    {
        //Arrange
        await using var connection = CreateConnection();
        var values = new List<int>();

        //Act
        await using (var reader = await connection.ExecuteReaderAsync("SELECT generate_series(1, 3)",
            cancellationToken: TestContext.Current.CancellationToken))
        {
            while (await reader.ReadAsync(TestContext.Current.CancellationToken))
                values.Add(reader.GetInt32(0));
        }

        //Assert
        values.Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task ExecuteReader_closes_a_connection_it_opened_when_the_reader_is_disposed()
    {
        //Arrange
        await using var connection = CreateConnection();

        //Act
        using (var reader = connection.ExecuteReader("SELECT 1"))
            reader.Read();

        //Assert
        connection.State.Should().Be(System.Data.ConnectionState.Closed);
    }

    [Fact]
    public async Task methods_leave_an_already_open_connection_open()
    {
        //Arrange
        await using var connection = CreateConnection();
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        //Act
        connection.ExecuteScalar<int>("SELECT 1");

        //Assert
        connection.State.Should().Be(System.Data.ConnectionState.Open);
    }

    [Fact]
    public async Task methods_close_a_connection_they_opened()
    {
        //Arrange
        await using var connection = CreateConnection();

        //Act
        await connection.ExecuteScalarAsync<int>("SELECT 1", cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        connection.State.Should().Be(System.Data.ConnectionState.Closed);
    }

    [Fact]
    public async Task methods_run_inside_the_given_transaction()
    {
        //Arrange
        await using var connection = CreateConnection();
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var table = await CreateWidgetTableAsync(connection);

        //Act
        await using (var transaction = await connection.BeginTransactionAsync(TestContext.Current.CancellationToken))
        {
            connection.Execute($"DELETE FROM {table}", transaction: transaction);
            connection.ExecuteScalar<long>($"SELECT count(*) FROM {table}", transaction: transaction).Should().Be(0);
            await transaction.RollbackAsync(TestContext.Current.CancellationToken);
        }
        var count = connection.ExecuteScalar<long>($"SELECT count(*) FROM {table}");

        //Assert
        count.Should().Be(3);
    }

    [Fact]
    public async Task QueryMultiple_reads_each_result_set_in_turn()
    {
        //Arrange
        await using var connection = CreateConnection();
        var table = await CreateWidgetTableAsync(connection);

        //Act
        using var grid = await connection.QueryMultipleAsync(
            $"SELECT count(*) FROM {table}; SELECT id, name FROM {table} WHERE id = @id",
            new { id = 2 }, cancellationToken: TestContext.Current.CancellationToken);
        var count = grid.Read<long>().Single();
        var widget = grid.Read<Widget>().Single();

        //Assert
        count.Should().Be(3);
        widget.Name.Should().Be("Gizmo");
        grid.IsConsumed.Should().BeTrue();
    }

    [Fact]
    public async Task QueryMultiple_throws_when_reading_past_the_last_result_set()
    {
        //Arrange
        await using var connection = CreateConnection();
        using var grid = connection.QueryMultiple("SELECT 1");
        grid.Read<int>();

        //Act
        var act = () => grid.Read<int>();

        //Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task QueryMultiple_closes_the_connection_it_opened_on_dispose()
    {
        //Arrange
        await using var connection = CreateConnection();

        //Act
        using (var grid = connection.QueryMultiple("SELECT 1"))
            grid.Read<int>();

        //Assert
        connection.State.Should().Be(System.Data.ConnectionState.Closed);
    }

    [Fact]
    public void Query_throws_on_null_connection()
    {
        //Arrange
        PgSqlConnection connection = null;

        //Act
        var act = () => connection.Query<int>("SELECT 1");

        //Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Execute_throws_on_blank_sql()
    {
        //Arrange
        using var connection = CreateConnection();

        //Act
        var act = () => connection.Execute("   ");

        //Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ToParameterValue_converts_enums_to_their_underlying_value()
        => PgSqlMapper.ToParameterValue(Status.Retired).Should().Be(2);

    [Fact]
    public void ToParameterValue_converts_null_to_DBNull()
        => PgSqlMapper.ToParameterValue(null).Should().Be(DBNull.Value);

    [Theory]
    [InlineData(typeof(int), true)]
    [InlineData(typeof(string), true)]
    [InlineData(typeof(DateOnly), true)]
    [InlineData(typeof(int[]), true)]
    [InlineData(typeof(Guid?), true)]
    [InlineData(typeof(Widget), false)]
    public void IsSimpleType_classifies_types(Type type, bool expected)
        => PgSqlMapper.IsSimpleType(type).Should().Be(expected);

    [Fact]
    public void ConvertValue_converts_between_postgres_and_clr_shapes()
    {
        //Act
        var dateOnly = PgSqlMapper.ConvertValue(new DateTime(2026, 10, 7), typeof(DateOnly));
        var timeOnly = PgSqlMapper.ConvertValue(new TimeSpan(13, 14, 15), typeof(TimeOnly));
        var narrowed = PgSqlMapper.ConvertValue(42L, typeof(int));
        var parsedEnum = PgSqlMapper.ConvertValue("retired", typeof(Status));

        //Assert
        dateOnly.Should().Be(new DateOnly(2026, 10, 7));
        timeOnly.Should().Be(new TimeOnly(13, 14, 15));
        narrowed.Should().Be(42);
        parsedEnum.Should().Be(Status.Retired);
    }
}
