using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CodeBrix.PostgresClient.Internal.Converters;
using CodeBrix.PostgresClient.PgSqlTypes;
using CodeBrix.PostgresClient.Tests.Support;
using SilverAssertions;
using Xunit;
using static CodeBrix.PostgresClient.Tests.TestUtil;

namespace CodeBrix.PostgresClient.Tests.Types; //was previously: Npgsql.Tests.Types;

// ReSharper disable BitwiseOperatorOnEnumWithoutFlags

/// <summary>
/// Tests on PostgreSQL arrays
/// </summary>
/// <remarks>
/// https://www.postgresql.org/docs/current/static/arrays.html
/// </remarks>
public abstract class ArrayTests(MultiplexingMode multiplexingMode) : MultiplexingTestBase(multiplexingMode)
{
    public static readonly object[][] ArrayTestCases =
    [
        [new[] { 1, 2, 3 }, "{1,2,3}", "integer[]", PgSqlDbType.Integer | PgSqlDbType.Array],
        [Array.Empty<int>(), "{}", "integer[]", PgSqlDbType.Integer | PgSqlDbType.Array],
        [new[,] { { 1, 2, 3 }, { 7, 8, 9 } }, "{{1,2,3},{7,8,9}}", "integer[]", PgSqlDbType.Integer | PgSqlDbType.Array],
        [new[] { [1, 2], new byte[] { 3, 4 } }, """{"\\x0102","\\x0304"}""", "bytea[]", PgSqlDbType.Bytea | PgSqlDbType.Array]
    ];

    [Theory, MemberData(nameof(ArrayTestCases))]
    public Task arrays<T>(T array, string sqlLiteral, string pgTypeName, PgSqlDbType? pgSqlDbType)
        => AssertType(array, sqlLiteral, pgTypeName, pgSqlDbType);

    [Fact]
    public async Task nullable_ints()
    {
        //Arrange
        var connectionStringBuilder = new PgSqlConnectionStringBuilder(ConnectionString)
        {
            ArrayNullabilityMode = ArrayNullabilityMode.Always
        };
        var dataSourceBuilder = new PgSqlDataSourceBuilder(connectionStringBuilder.ToString());
        await using var dataSource = dataSourceBuilder.Build();

        //Assert
        await AssertType(dataSource, new int?[] { 1, 2, null, 3 }, "{1,2,NULL,3}", "integer[]", PgSqlDbType.Integer | PgSqlDbType.Array);
    }

    // Checks that PG arrays containing nulls can't be read as CLR arrays of non-nullable value types (the default).
    [Fact]
    public async Task nullable_ints_cannot_be_read_as_non_nullable()
        => await AssertTypeUnsupportedRead<InvalidOperationException>("{1,NULL,2}", "int[]");

    [Fact]
    public async Task throws_too_many_dimensions()
    {
        //Arrange
        await using var conn = CreateConnection();
        await conn.OpenAsync(TestContext.Current.CancellationToken);
        await using var cmd = new PgSqlCommand("SELECT 1", conn);
        cmd.Parameters.AddWithValue("p", new int[1, 1, 1, 1, 1, 1, 1, 1, 1]); // 9 dimensions

        //Act
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken));

        //Assert
        ex.Message.Should().Be("Postgres arrays can have at most 8 dimensions. (Parameter 'values')");
    }

    // Checks that PG arrays containing nulls are returned as set via ValueTypeArrayMode.
    [Theory]
    [InlineData(ArrayNullabilityMode.Always)]
    [InlineData(ArrayNullabilityMode.Never)]
    [InlineData(ArrayNullabilityMode.PerInstance)]
    public async Task value_type_array_nullabilities(ArrayNullabilityMode mode)
    {
        //Arrange
        await using var dataSource = CreateDataSource(csb => csb.ArrayNullabilityMode = mode);
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using var cmd = new PgSqlCommand(
"""
SELECT onedim, twodim FROM (VALUES
('{1, 2, 3, 4}'::int[],'{{1, 2},{3, 4}}'::int[][]),
('{5, NULL, 6, 7}'::int[],'{{5, NULL},{6, 7}}'::int[][])) AS x(onedim,twodim)
""", conn);

        //Act
        await using var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);

        //Assert
        switch (mode)
        {
        case ArrayNullabilityMode.Never:
            reader.Read();
            var value = reader.GetValue(0);
            reader.GetFieldType(0).Should().Be(typeof(Array));
            value.GetType().Should().Be(typeof(int[]));
            AssertEqualByValue(new []{1, 2, 3, 4}, value);
            reader.GetFieldType(1).Should().Be(typeof(Array));
            reader.GetValue(1).GetType().Should().Be(typeof(int[,]));
            AssertEqualByValue(new [,]{{1, 2}, {3, 4}}, reader.GetValue(1));
            reader.Read();
            reader.GetFieldType(0).Should().Be(typeof(Array));
            Assert.Throws<InvalidCastException>(() => reader.GetValue(0));
            reader.GetFieldType(1).Should().Be(typeof(Array));
            Assert.Throws<InvalidCastException>(() => reader.GetValue(1));
            break;
        case ArrayNullabilityMode.Always:
            reader.Read();
            value = reader.GetValue(0);
            reader.GetFieldType(0).Should().Be(typeof(Array));
            value.GetType().Should().Be(typeof(int?[]));
            AssertEqualByValue(new int?[]{1, 2, 3, 4}, value);
            value = reader.GetValue(1);
            reader.GetFieldType(1).Should().Be(typeof(Array));
            value.GetType().Should().Be(typeof(int?[,]));
            AssertEqualByValue(new int?[,]{{1, 2}, {3, 4}}, value);
            reader.Read();
            value = reader.GetValue(0);
            reader.GetFieldType(0).Should().Be(typeof(Array));
            value.GetType().Should().Be(typeof(int?[]));
            AssertEqualByValue(new int?[]{5, null, 6, 7}, value);
            value = reader.GetValue(1);
            reader.GetFieldType(1).Should().Be(typeof(Array));
            value.GetType().Should().Be(typeof(int?[,]));
            AssertEqualByValue(new int?[,]{{5, null},{6, 7}}, value);
            break;
        case ArrayNullabilityMode.PerInstance:
            reader.Read();
            value = reader.GetValue(0);
            reader.GetFieldType(0).Should().Be(typeof(Array));
            value.GetType().Should().Be(typeof(int[]));
            AssertEqualByValue(new []{1, 2, 3, 4}, value);
            value = reader.GetValue(1);
            reader.GetFieldType(1).Should().Be(typeof(Array));
            value.GetType().Should().Be(typeof(int[,]));
            AssertEqualByValue(new [,]{{1, 2}, {3, 4}}, value);
            reader.Read();
            value = reader.GetValue(0);
            reader.GetFieldType(0).Should().Be(typeof(Array));
            value.GetType().Should().Be(typeof(int?[]));
            AssertEqualByValue(new int?[]{5, null, 6, 7}, value);
            value = reader.GetValue(1);
            reader.GetFieldType(1).Should().Be(typeof(Array));
            value.GetType().Should().Be(typeof(int?[,]));
            AssertEqualByValue(new int?[,]{{5, null},{6, 7}}, value);
            break;
        default:
            throw new UnreachableException($"Unknown case {mode}");
        }
    }

    // Checks that PG arrays containing nulls are returned as set via ValueTypeArrayMode.
    [Theory]
    [InlineData(ArrayNullabilityMode.Always)]
    [InlineData(ArrayNullabilityMode.Never)]
    [InlineData(ArrayNullabilityMode.PerInstance)]
    public async Task value_type_array_nullabilities_converter_resolver(ArrayNullabilityMode mode)
    {
        //Arrange
        await using var dataSource = CreateDataSource(csb =>
        {
            csb.ArrayNullabilityMode = mode;
            csb.Timezone = "Europe/Berlin";
        });
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using var cmd = new PgSqlCommand(
"""
SELECT onedim, twodim FROM (VALUES
('{"1998-04-12 15:26:38+02"}'::timestamptz[],'{{"1998-04-12 15:26:38+02"},{"1998-04-13 15:26:38+02"}}'::timestamptz[][]),
('{"1998-04-14 15:26:38+02", NULL}'::timestamptz[],'{{"1998-04-14 15:26:38+02", NULL},{"1998-04-15 15:26:38+02", "1998-04-16 15:26:38+02"}}'::timestamptz[][])) AS x(onedim,twodim)
""", conn);

        //Act
        await using var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);

        //Assert
        switch (mode)
        {
        case ArrayNullabilityMode.Never:
            reader.Read();
            var value = reader.GetValue(0);
            reader.GetFieldType(0).Should().Be(typeof(Array));
            value.GetType().Should().Be(typeof(DateTime[]));
            AssertEqualByValue(new []{new DateTime(1998, 4, 12, 13, 26, 38, DateTimeKind.Utc)}, value);
            value = reader.GetValue(1);
            reader.GetFieldType(1).Should().Be(typeof(Array));
            value.GetType().Should().Be(typeof(DateTime[,]));
            AssertEqualByValue(new [,]
            {
                { new DateTime(1998, 4, 12, 13, 26, 38, DateTimeKind.Utc) },
                { new DateTime(1998, 4, 13, 13, 26, 38, DateTimeKind.Utc) }
            }, value);
            reader.Read();
            reader.GetFieldType(0).Should().Be(typeof(Array));
            Assert.Throws<InvalidCastException>(() => reader.GetValue(0));
            reader.GetFieldType(1).Should().Be(typeof(Array));
            Assert.Throws<InvalidCastException>(() => reader.GetValue(1));
            break;
        case ArrayNullabilityMode.Always:
            reader.Read();
            value = reader.GetValue(0);
            reader.GetFieldType(0).Should().Be(typeof(Array));
            value.GetType().Should().Be(typeof(DateTime?[]));
            AssertEqualByValue(new DateTime?[]{new DateTime(1998, 4, 12, 13, 26, 38, DateTimeKind.Utc)}, value);
            value = reader.GetValue(1);
            reader.GetFieldType(1).Should().Be(typeof(Array));
            value.GetType().Should().Be(typeof(DateTime?[,]));
            AssertEqualByValue(new DateTime?[,]
            {
                { new DateTime(1998, 4, 12, 13, 26, 38, DateTimeKind.Utc) },
                { new DateTime(1998, 4, 13, 13, 26, 38, DateTimeKind.Utc) }
            }, value);
            reader.Read();
            value = reader.GetValue(0);
            reader.GetFieldType(0).Should().Be(typeof(Array));
            value.GetType().Should().Be(typeof(DateTime?[]));
            AssertEqualByValue(new DateTime?[]{ new DateTime(1998, 4, 14, 13, 26, 38, DateTimeKind.Utc), null }, value);
            value = reader.GetValue(1);
            reader.GetFieldType(1).Should().Be(typeof(Array));
            value.GetType().Should().Be(typeof(DateTime?[,]));
            AssertEqualByValue(new DateTime?[,]
            {
                { new DateTime(1998, 4, 14, 13, 26, 38, DateTimeKind.Utc), null },
                { new DateTime(1998, 4, 15, 13, 26, 38, DateTimeKind.Utc), new DateTime(1998, 4, 16, 13, 26, 38, DateTimeKind.Utc) }
            }, value);
            break;
        case ArrayNullabilityMode.PerInstance:
            reader.Read();
            value = reader.GetValue(0);
            reader.GetFieldType(0).Should().Be(typeof(Array));
            value.GetType().Should().Be(typeof(DateTime[]));
            AssertEqualByValue(new []{new DateTime(1998, 4, 12, 13, 26, 38, DateTimeKind.Utc)}, value);
            value = reader.GetValue(1);
            reader.GetFieldType(1).Should().Be(typeof(Array));
            value.GetType().Should().Be(typeof(DateTime[,]));
            AssertEqualByValue(new [,]
            {
                { new DateTime(1998, 4, 12, 13, 26, 38, DateTimeKind.Utc) },
                { new DateTime(1998, 4, 13, 13, 26, 38, DateTimeKind.Utc) }
            }, value);
            reader.Read();
            value = reader.GetValue(0);
            reader.GetFieldType(0).Should().Be(typeof(Array));
            value.GetType().Should().Be(typeof(DateTime?[]));
            AssertEqualByValue(new DateTime?[]{ new DateTime(1998, 4, 14, 13, 26, 38, DateTimeKind.Utc), null }, value);
            value = reader.GetValue(1);
            reader.GetFieldType(1).Should().Be(typeof(Array));
            value.GetType().Should().Be(typeof(DateTime?[,]));
            AssertEqualByValue(new DateTime?[,]
            {
                { new DateTime(1998, 4, 14, 13, 26, 38, DateTimeKind.Utc), null },
                { new DateTime(1998, 4, 15, 13, 26, 38, DateTimeKind.Utc), new DateTime(1998, 4, 16, 13, 26, 38, DateTimeKind.Utc) }
            }, value);
            break;
        default:
            throw new UnreachableException($"Unknown case {mode}");
        }
    }

    // Note that PG normalizes empty multidimensional arrays to single-dimensional, e.g. ARRAY[[], []]::integer[] returns {}.
    [Fact]
    public async Task write_empty_multidimensional_array()
        => await AssertTypeWrite(new int[0, 0], "{}", "integer[]", PgSqlDbType.Integer | PgSqlDbType.Array);

    [Fact]
    public async Task generic_List()
        => await AssertType(
            new List<int> { 1, 2, 3 }, "{1,2,3}", "integer[]", PgSqlDbType.Integer | PgSqlDbType.Array, isDefaultForReading: false);

    [Fact]
    public async Task write_IList_implementation()
        => await AssertTypeWrite(
            ImmutableArray.Create(1, 2, 3), "{1,2,3}", "integer[]", PgSqlDbType.Integer | PgSqlDbType.Array);

    [Fact]
    public async Task read_IList_implementation_throws()
        => await Assert.ThrowsAsync<InvalidCastException>(() =>
            AssertTypeRead("{1,2,3}", "integer[]", ImmutableArray.Create(1, 2, 3), isDefault: false));

    [Fact]
    public async Task generic_IList()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        await using var cmd = new PgSqlCommand("SELECT @p1", conn);

        var expected = ImmutableArray.Create(1,2,3);
        cmd.Parameters.Add(new PgSqlParameter<IList<int>>("p1", PgSqlDbType.Array | PgSqlDbType.Integer) { TypedValue = expected });

        //Act
        var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        reader.Read();

        //Assert
        reader.GetFieldValue<int[]>(0).Should().Equal(expected);
    }

    // Verifies that an InvalidOperationException is thrown when the returned array has a different number of dimensions from what was requested.
    [Fact]
    public async Task wrong_array_dimensions_throws()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        await using var cmd = new PgSqlCommand("SELECT ARRAY[[1], [2]]", conn);

        //Act
        var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        reader.Read();

        var ex = Assert.Throws<InvalidCastException>(() => reader.GetFieldValue<int[]>(0));

        //Assert
        ex.Message.Should().StartWith("Cannot read an array value with 2 dimensions into a collection type with 1 dimension");
    }

    // Verifies that an attempt to read an Array of value types that contains null values as array of a non-nullable type fails.
    [Fact]
    public async Task read_null_as_non_nullable_array_throws()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        await using var cmd = new PgSqlCommand("SELECT @p1", conn);

        var expected = new int?[] { 1, 5, null, 9 };
        cmd.Parameters.AddWithValue("p1", PgSqlDbType.Array | PgSqlDbType.Integer, expected);

        //Act
        var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        reader.Read();

        //Assert
        Assert.Throws<InvalidCastException>(() => reader.GetFieldValue<int[]>(0))
            .Message.Should().Be(PgArrayConverter.ReadNonNullableCollectionWithNullsExceptionMessage);
    }

    // Verifies that an attempt to read an Array of value types that contains null values as List of a non-nullable type fails.
    [Fact]
    public async Task read_null_as_non_nullable_list_throws()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        await using var cmd = new PgSqlCommand("SELECT @p1", conn);

        var expected = new int?[] { 1, 5, null, 9 };
        cmd.Parameters.AddWithValue("p1", PgSqlDbType.Array | PgSqlDbType.Integer, expected);

        //Act
        var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        reader.Read();

        //Assert
        Assert.Throws<InvalidCastException>(() => reader.GetFieldValue<List<int>>(0))
            .Message.Should().Be(PgArrayConverter.ReadNonNullableCollectionWithNullsExceptionMessage);
    }

    // Roundtrips a large, one-dimensional array of ints that will be chunked
    [Fact]
    public async Task long_one_dimensional()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();

        var expected = new int[conn.Settings.WriteBufferSize/4 + 100];
        for (var i = 0; i < expected.Length; i++)
            expected[i] = i;

        await using var cmd = new PgSqlCommand("SELECT @p", conn);
        var p = new PgSqlParameter {ParameterName = "p", Value = expected};
        cmd.Parameters.Add(p);

        //Act
        await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess, TestContext.Current.CancellationToken);
        reader.Read();

        //Assert
        AssertEqualByValue(expected, reader[0]);
    }

    // Roundtrips a large, two-dimensional array of ints that will be chunked
    [Fact]
    public async Task long_two_dimensional()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        var len = conn.Settings.WriteBufferSize/2 + 100;
        var expected = new int[2, len];
        for (var i = 0; i < len; i++)
            expected[0, i] = i;
        for (var i = 0; i < len; i++)
            expected[1, i] = i;
        await using var cmd = new PgSqlCommand("SELECT @p", conn);
        var p = new PgSqlParameter {ParameterName = "p", Value = expected};
        cmd.Parameters.Add(p);

        //Act
        await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess, TestContext.Current.CancellationToken);
        reader.Read();

        //Assert
        AssertEqualByValue(expected, reader[0]);
    }

    // Reads an one-dimensional array with lower bound != 0
    [Fact]
    public Task read_non_zero_lower_bounded()
        => AssertTypeRead("[2:3]={ 8, 9 }", "integer[]", new[] { 8, 9 });

    // Reads an one-dimensional array with lower bound != 0
    [Fact]
    public Task read_non_zero_lower_bounded_multidimensional()
        => AssertTypeRead("[2:3][2:3]={ {8,9}, {1,2} }", "integer[]", new[,] { { 8, 9 }, { 1, 2 }});

    // Roundtrips a long, one-dimensional array of strings, including a null
    [Fact]
    public async Task strings_with_null()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        var largeString = new StringBuilder();
        largeString.Append('a', conn.Settings.WriteBufferSize);
        var expected = new[] {"value1", null, largeString.ToString(), "val3"};
        await using var cmd = new PgSqlCommand("SELECT @p", conn);
        var p = new PgSqlParameter("p", PgSqlDbType.Array | PgSqlDbType.Text) {Value = expected};
        cmd.Parameters.Add(p);

        //Act
        await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess, TestContext.Current.CancellationToken);
        reader.Read();

        //Assert
        reader.GetFieldValue<string[]>(0).Should().Equal(expected);
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/844")]
    public async Task writing_IEnumerable_is_not_supported()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        await using var cmd = new PgSqlCommand("SELECT @p1", conn);
        cmd.Parameters.AddWithValue("p1", new EnumerableOnly<int>());

        //Act
        var ex = await Assert.ThrowsAsync<InvalidCastException>(async () => await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken));

        //Assert
        ex.InnerException.Message.Should().Contain("array or some implementation of IList<T>");
    }

    class EnumerableOnly<T> : IEnumerable<T>
    {
        public IEnumerator<T> GetEnumerator() => throw new NotImplementedException();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/960")]
    public async Task jagged_arrays_not_supported()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        await using var cmd = new PgSqlCommand("SELECT @p1", conn);
        cmd.Parameters.AddWithValue("p1", PgSqlDbType.Array | PgSqlDbType.Integer, new[] { [8], new[] { 8, 10 } });

        //Act
        var ex = await Assert.ThrowsAsync<InvalidCastException>(async () => await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));

        //Assert
        ex.InnerException.Message.Should().Contain("jagged");
    }

    // Roundtrips one-dimensional and two-dimensional arrays of a PostgreSQL domain.
    [Fact]
    public async Task array_of_domain()
    {
        //Arrange
        if (IsMultiplexing)
            Assert.Skip("Multiplexing, ReloadTypes");

        await using var conn = await OpenConnectionAsync();
        MinimumPgVersion(conn, "11.0", "Arrays of domains were introduced in PostgreSQL 11");
        await conn.ExecuteNonQueryAsync("CREATE DOMAIN pg_temp.posint AS integer CHECK (VALUE > 0);", cancellationToken: TestContext.Current.CancellationToken);
        await conn.ReloadTypesAsync(TestContext.Current.CancellationToken);
        await using var cmd = new PgSqlCommand("SELECT @p1::posint[], @p2::posint[][]", conn);
        var oneDim = new[] { 1, 3, 5, 9 };
        var twoDim = new[,] { { 1, 3 }, { 5, 9 } };
        cmd.Parameters.AddWithValue("p1", PgSqlDbType.Integer | PgSqlDbType.Array, oneDim);
        cmd.Parameters.AddWithValue("p2", PgSqlDbType.Integer | PgSqlDbType.Array, twoDim);

        //Act
        await using var reader = cmd.ExecuteReader();
        reader.Read();

        //Assert
        AssertEqualByValue(oneDim, reader.GetValue(0));
        AssertEqualByValue(oneDim, reader.GetProviderSpecificValue(0));
        reader.GetFieldValue<int[]>(0).Should().Equal(oneDim);
        reader.GetFieldValue<int[]>(0).Should().Equal(oneDim);
        reader.GetFieldType(0).Should().Be(typeof(Array));
        reader.GetProviderSpecificFieldType(0).Should().Be(typeof(Array));

        AssertEqualByValue(twoDim, reader.GetValue(1));
        AssertEqualByValue(twoDim, reader.GetProviderSpecificValue(1));
        AssertEqualByValue(twoDim, reader.GetFieldValue<int[,]>(1));
        reader.GetFieldType(1).Should().Be(typeof(Array));
        reader.GetProviderSpecificFieldType(1).Should().Be(typeof(Array));
    }

    // Roundtrips a PostgreSQL domain over a one-dimensional and a two-dimensional array.
    [Fact]
    public async Task domain_of_array()
    {
        //Arrange
        if (IsMultiplexing)
            Assert.Skip("Multiplexing, ReloadTypes");

        await using var conn = await OpenConnectionAsync();
        MinimumPgVersion(conn, "11.0", "Domains over arrays were introduced in PostgreSQL 11");
        await conn.ExecuteNonQueryAsync(
"""
CREATE DOMAIN pg_temp.int_array_1d  AS int[] CHECK(array_length(VALUE, 1) = 4);
CREATE DOMAIN pg_temp.int_array_2d  AS int[][] CHECK(array_length(VALUE, 2) = 2);
""", cancellationToken: TestContext.Current.CancellationToken);
        await conn.ReloadTypesAsync(TestContext.Current.CancellationToken);
        await using var cmd = new PgSqlCommand("SELECT @p1::int_array_1d, @p2::int_array_2d", conn);
        var oneDim = new[] { 1, 3, 5, 9 };
        var twoDim = new[,] { { 1, 3 }, { 5, 9 } };
        cmd.Parameters.AddWithValue("p1", PgSqlDbType.Integer | PgSqlDbType.Array, oneDim);
        cmd.Parameters.AddWithValue("p2", PgSqlDbType.Integer | PgSqlDbType.Array, twoDim);

        //Act
        await using var reader = cmd.ExecuteReader();
        reader.Read();

        //Assert
        AssertEqualByValue(oneDim, reader.GetValue(0));
        AssertEqualByValue(oneDim, reader.GetProviderSpecificValue(0));
        reader.GetFieldValue<int[]>(0).Should().Equal(oneDim);
        reader.GetFieldType(0).Should().Be(typeof(Array));
        reader.GetProviderSpecificFieldType(0).Should().Be(typeof(Array));

        AssertEqualByValue(twoDim, reader.GetValue(1));
        AssertEqualByValue(twoDim, reader.GetProviderSpecificValue(1));
        AssertEqualByValue(twoDim, reader.GetFieldValue<int[,]>(1));
        reader.GetFieldType(1).Should().Be(typeof(Array));
        reader.GetProviderSpecificFieldType(1).Should().Be(typeof(Array));
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/3417")]
    public async Task read_two_empty_arrays()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        await using var cmd = new PgSqlCommand("SELECT '{}'::INT[], '{}'::INT[]", conn);

        //Act
        await using var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        await reader.ReadAsync(TestContext.Current.CancellationToken);

        //Assert
        reader.GetFieldValue<int[]>(1).Should().BeSameAs(reader.GetFieldValue<int[]>(0));
        // Unlike T[], List<T> is mutable so we should not return the same instance
        reader.GetFieldValue<List<int>>(1).Should().NotBeSameAs(reader.GetFieldValue<List<int>>(0));
    }

    [Fact]
    public async Task arrays_not_supported_by_default_on_PgSqlSlimSourceBuilder()
    {
        //Arrange
        var dataSourceBuilder = new PgSqlSlimDataSourceBuilder(ConnectionString);
        await using var dataSource = dataSourceBuilder.Build();

        //Assert
        await AssertTypeUnsupportedRead<int[], InvalidCastException>("{1,2,3}", "integer[]", dataSource);
        await AssertTypeUnsupportedWrite<int[], InvalidCastException>([1, 2, 3], "integer[]", dataSource);
    }

    [Fact]
    public async Task PgSqlSlimSourceBuilder_EnableArrays()
    {
        //Arrange
        var dataSourceBuilder = new PgSqlSlimDataSourceBuilder(ConnectionString);
        dataSourceBuilder.EnableArrays();
        await using var dataSource = dataSourceBuilder.Build();

        //Assert
        await AssertType(dataSource, new[] { 1, 2, 3 }, "{1,2,3}", "integer[]", PgSqlDbType.Integer | PgSqlDbType.Array);
    }

    static void AssertEqualByValue(object expected, object actual)
        => ValueEquality.AreEqual(expected, actual).Should().BeTrue(
            $"expected {ValueEquality.Format(expected)} but got {ValueEquality.Format(actual)}");
}

public sealed class ArrayTests_NonMultiplexing() : ArrayTests(MultiplexingMode.NonMultiplexing);
public sealed class ArrayTests_Multiplexing() : ArrayTests(MultiplexingMode.Multiplexing);
