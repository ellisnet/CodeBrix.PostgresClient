using System;
using System.ComponentModel;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CodeBrix.PostgresClient.PgSqlTypes;
using CodeBrix.PostgresClient.Properties;
using CodeBrix.PostgresClient.Util;
using SilverAssertions;
using Xunit;
using static CodeBrix.PostgresClient.Tests.TestUtil;

namespace CodeBrix.PostgresClient.Tests.Types; //was previously: Npgsql.Tests.Types;

public abstract class RangeTests(MultiplexingMode multiplexingMode, RangeTestsFixture fixture)
    : MultiplexingTestBase(multiplexingMode), IClassFixture<RangeTestsFixture>
{
    public static readonly object[][] RangeTestCases =
    [
        [new PgSqlRange<int>(1, true, 10, false), "[1,10)", "int4range", PgSqlDbType.IntegerRange],
        [new PgSqlRange<long>(1, true, 10, false), "[1,10)", "int8range", PgSqlDbType.BigIntRange],
        [new PgSqlRange<decimal>(1, true, 10, false), "[1,10)", "numrange", PgSqlDbType.NumericRange],
        [new PgSqlRange<DateTime>(
                    new DateTime(2020, 1, 1, 12, 0, 0), true,
                    new DateTime(2020, 1, 3, 13, 0, 0), false),
                """["2020-01-01 12:00:00","2020-01-03 13:00:00")""", "tsrange", PgSqlDbType.TimestampRange],
        // Note that the below text representations are local (according to TimeZone, which is set to Europe/Berlin in this test class),
        // because that's how PG does timestamptz *text* representation.
        [new PgSqlRange<DateTime>(
                    new DateTime(2020, 1, 1, 12, 0, 0, DateTimeKind.Utc), true,
                    new DateTime(2020, 1, 3, 13, 0, 0, DateTimeKind.Utc), false),
                """["2020-01-01 13:00:00+01","2020-01-03 14:00:00+01")""", "tstzrange", PgSqlDbType.TimestampTzRange],

        // Note that numrange is a non-discrete range, and therefore doesn't undergo normalization to inclusive/exclusive in PG
        [PgSqlRange<decimal>.Empty, "empty", "numrange", PgSqlDbType.NumericRange],
        [new PgSqlRange<decimal>(1, true, 10, true), "[1,10]", "numrange", PgSqlDbType.NumericRange],
        [new PgSqlRange<decimal>(1, false, 10, false), "(1,10)", "numrange", PgSqlDbType.NumericRange],
        [new PgSqlRange<decimal>(1, true, 10, false), "[1,10)", "numrange", PgSqlDbType.NumericRange],
        [new PgSqlRange<decimal>(1, false, 10, true), "(1,10]", "numrange", PgSqlDbType.NumericRange],
        [new PgSqlRange<decimal>(1, false, true, 10, false, false), "(,10)", "numrange", PgSqlDbType.NumericRange],
        [new PgSqlRange<decimal>(1, true, false, 10, false, true), "[1,)", "numrange", PgSqlDbType.NumericRange]
    ];

    // See more test cases in DateTimeTests
    [Theory, MemberData(nameof(RangeTestCases))]
    public Task range<T>(T range, string sqlLiteral, string pgTypeName, PgSqlDbType? pgSqlDbType)
        => AssertType(range, sqlLiteral, pgTypeName, pgSqlDbType,
            // PgSqlRange<T>[] is mapped to multirange by default, not array, so the built-in AssertType testing for arrays fails
            // (see below)
            skipArrayCheck: true);

    // This re-executes the same scenario as above, but with isDefaultForWriting: false and without skipArrayCheck: true.
    // This tests coverage of range arrays (as opposed to multiranges).
    [Theory, MemberData(nameof(RangeTestCases))]
    public Task range_array<T>(T range, string sqlLiteral, string pgTypeName, PgSqlDbType? pgSqlDbType)
        => AssertType(range, sqlLiteral, pgTypeName, pgSqlDbType, isDefaultForWriting: false);

    [Fact]
    public void equality_finite()
    {
        var r1 = new PgSqlRange<int>(0, true, false, 1, false, false);

        //different bounds
        var r2 = new PgSqlRange<int>(1, true, false, 2, false, false);
        (r1 == r2).Should().BeFalse();

        //lower bound is not inclusive
        var r3 = new PgSqlRange<int>(0, false, false, 1, false, false);
        (r1 == r3).Should().BeFalse();

        //upper bound is inclusive
        var r4 = new PgSqlRange<int>(0, true, false, 1, true, false);
        (r1 == r4).Should().BeFalse();

        var r5 = new PgSqlRange<int>(0, true, false, 1, false, false);
        (r1 == r5).Should().BeTrue();

        //check some other combinations while we are here
        (r2 == r3).Should().BeFalse();
        (r2 == r4).Should().BeFalse();
        (r3 == r4).Should().BeFalse();
    }

    [Fact]
    public void equality_infinite()
    {
        var r1 = new PgSqlRange<int>(0, false, true, 1, false, false);

        //different upper bound (lower bound shouldn't matter since it is infinite)
        var r2 = new PgSqlRange<int>(1, false, true, 2, false, false);
        (r1 == r2).Should().BeFalse();

        //upper bound is inclusive
        var r3 = new PgSqlRange<int>(0, false, true, 1, true, false);
        (r1 == r3).Should().BeFalse();

        //value of lower bound shouldn't matter since it is infinite
        var r4 = new PgSqlRange<int>(10, false, true, 1, false, false);
        (r1 == r4).Should().BeTrue();

        //check some other combinations while we are here
        (r2 == r3).Should().BeFalse();
        (r2 == r4).Should().BeFalse();
        (r3 == r4).Should().BeFalse();
    }

    [Fact]
    public void GetHashCode_value_types()
    {
        //Arrange
        PgSqlRange<int> a = default;
        PgSqlRange<int> b = PgSqlRange<int>.Empty;
        PgSqlRange<int> c = PgSqlRange<int>.Parse("(,)");

        //Assert
        a.Equals(b).Should().BeFalse();
        a.Equals(c).Should().BeFalse();
        b.Equals(c).Should().BeFalse();
        b.GetHashCode().Should().NotBe(a.GetHashCode());
        c.GetHashCode().Should().NotBe(a.GetHashCode());
        c.GetHashCode().Should().NotBe(b.GetHashCode());
    }

    [Fact]
    public void GetHashCode_reference_types()
    {
        //Arrange
        PgSqlRange<string> a= default;
        PgSqlRange<string> b = PgSqlRange<string>.Empty;
        PgSqlRange<string> c = PgSqlRange<string>.Parse("(,)");

        //Assert
        a.Equals(b).Should().BeFalse();
        a.Equals(c).Should().BeFalse();
        b.Equals(c).Should().BeFalse();
        b.GetHashCode().Should().NotBe(a.GetHashCode());
        c.GetHashCode().Should().NotBe(a.GetHashCode());
        c.GetHashCode().Should().NotBe(b.GetHashCode());
    }

    [Fact]
    public async Task timestamptz_range_with_DateTimeOffset()
    {
        //Arrange
        // The default CLR mapping for timestamptz is DateTime, but it also supports DateTimeOffset.
        // The range should also support both, defaulting to the first.
        using var conn = await OpenConnectionAsync();
        using var cmd = new PgSqlCommand("SELECT @p", conn);

        var dto1 = new DateTimeOffset(2010, 1, 3, 10, 0, 0, TimeSpan.Zero);
        var dto2 = new DateTimeOffset(2010, 1, 4, 10, 0, 0, TimeSpan.Zero);
        var range = new PgSqlRange<DateTimeOffset>(dto1, dto2);
        cmd.Parameters.AddWithValue("p", range);

        //Act
        using var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);

        await reader.ReadAsync(TestContext.Current.CancellationToken);
        var actual = reader.GetFieldValue<PgSqlRange<DateTimeOffset>>(0);

        //Assert
        actual.Should().Be(range);
    }

    [Fact]
    public async Task unmapped_range_with_mapped_subtype()
    {
        //Arrange
        await using var dataSource = CreateDataSource(b => b.EnableUnmappedTypes().ConnectionStringBuilder.MaxPoolSize = 1);
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        var typeName = await GetTempTypeName(conn);
        await conn.ExecuteNonQueryAsync($"CREATE TYPE {typeName} AS RANGE(subtype=text)", cancellationToken: TestContext.Current.CancellationToken);
        await Task.Yield(); // TODO: fix multiplexing deadlock bug
        conn.ReloadTypes();
        (await conn.ExecuteScalarAsync("SELECT 1", cancellationToken: TestContext.Current.CancellationToken)).Should().Be(1);

        var value = new PgSqlRange<char[]>(
            new string('a', conn.Settings.WriteBufferSize + 10).ToCharArray(),
            new string('z', conn.Settings.WriteBufferSize + 10).ToCharArray()
        );

        await using var cmd = new PgSqlCommand("SELECT @p", conn);
        cmd.Parameters.Add(new PgSqlParameter { DataTypeName = typeName, ParameterName = "p", Value = value });

        //Act
        await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess, TestContext.Current.CancellationToken);
        await reader.ReadAsync(TestContext.Current.CancellationToken);

        //Assert
        reader.GetFieldType(0).Should().Be(typeof(PgSqlRange<string>));
        var result = reader.GetFieldValue<PgSqlRange<char[]>>(0);
        (result.LowerBound.SequenceEqual(value.LowerBound) && result.UpperBound.SequenceEqual(value.UpperBound))
            .Should().BeTrue("the read range should have the written bounds");
    }

    [Fact]
    public async Task unmapped_range_supported_only_with_EnableUnmappedTypes()
    {
        //Arrange
        await using var connection = await DataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        var rangeType = await GetTempTypeName(connection);
        await connection.ExecuteNonQueryAsync($"CREATE TYPE {rangeType} AS RANGE(subtype=text)", cancellationToken: TestContext.Current.CancellationToken);
        await Task.Yield(); // TODO: fix multiplexing deadlock bug
        await connection.ReloadTypesAsync(TestContext.Current.CancellationToken);

        var errorMessage = string.Format(
            PgSqlStrings.UnmappedRangesNotEnabled,
            nameof(PgSqlSlimDataSourceBuilder.EnableUnmappedTypes),
            nameof(PgSqlDataSourceBuilder));

        //Act
        var exception = await AssertTypeUnsupportedWrite(new PgSqlRange<string>("bar", "foo"), rangeType);

        //Assert
        exception.InnerException.Should().BeAssignableTo<NotSupportedException>();
        exception.InnerException.Message.Should().Be(errorMessage);

        exception = await AssertTypeUnsupportedRead("""["bar","foo"]""", rangeType);
        exception.InnerException.Should().BeAssignableTo<NotSupportedException>();
        exception.InnerException.Message.Should().Be(errorMessage);

        exception = await AssertTypeUnsupportedRead<PgSqlRange<string>>("""["bar","foo"]""", rangeType);
        exception.InnerException.Should().BeAssignableTo<NotSupportedException>();
        exception.InnerException.Message.Should().Be(errorMessage);
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/4441")]
    public async Task array_of_range()
    {
        //Arrange
        bool supportsMultirange;

        await using (var conn = await OpenConnectionAsync())
        {
            supportsMultirange = conn.PostgreSqlVersion.IsGreaterOrEqual(14);
        }

        // Starting with PG14, we map CLR PgSqlRange<T>[] to PG multiranges by default, but also support mapping to PG array of range.
        // (wee also MultirangeTests for additional multirange-specific tests).
        // Earlier versions don't have multirange, so the default mapping is to PG array of range.

        // Note that when PgSqlDbType inference, we don't know the PG version (since PgSqlParameter can exist in isolation). So
        // if PgSqlParameter.Value is set to PgSqlRange<T>[], PgSqlDbType always returns multirange (hence
        // isPgSqlDbTypeInferredFromClrType is false).

        //Assert
        await AssertType(
            new PgSqlRange<int>[]
            {
                new(3, lowerBoundIsInclusive: true, 4, upperBoundIsInclusive: false),
                new(5, lowerBoundIsInclusive: true, 6, upperBoundIsInclusive: false)
            },
            """{"[3,4)","[5,6)"}""",
            "int4range[]",
            PgSqlDbType.IntegerRange | PgSqlDbType.Array,
            isDefaultForWriting: !supportsMultirange,
            isPgSqlDbTypeInferredFromClrType: false);
    }

    [Fact]
    public async Task ranges_not_supported_by_default_on_PgSqlSlimSourceBuilder()
    {
        //Arrange
        var errorMessage = string.Format(
            PgSqlStrings.RangesNotEnabled, nameof(PgSqlSlimDataSourceBuilder.EnableRanges), nameof(PgSqlSlimDataSourceBuilder));

        var dataSourceBuilder = new PgSqlSlimDataSourceBuilder(ConnectionString);
        await using var dataSource = dataSourceBuilder.Build();

        //Act
        var exception = await AssertTypeUnsupportedRead<PgSqlRange<int>>("[1,10)", "int4range", dataSource);
        exception.InnerException.Message.Should().Be(errorMessage);
        exception = await AssertTypeUnsupportedWrite(new PgSqlRange<int>(1, true, 10, false), "int4range", dataSource);
        exception.InnerException.Message.Should().Be(errorMessage);
    }

    [Fact]
    public async Task PgSqlSlimSourceBuilder_EnableRanges()
    {
        //Arrange
        var dataSourceBuilder = new PgSqlSlimDataSourceBuilder(ConnectionString);
        dataSourceBuilder.EnableRanges();
        await using var dataSource = dataSourceBuilder.Build();

        //Assert
        await AssertType(
            dataSource,
            new PgSqlRange<int>(1, true, 10, false), "[1,10)", "int4range", PgSqlDbType.IntegerRange, skipArrayCheck: true);
    }

    protected override PgSqlConnection OpenConnection()
        => throw new NotSupportedException();

    #region ParseTests

    [Theory]
    [MemberData(nameof(DateTimeRangeTheoryData))]
    public void roundtrip_DateTime_ranges_through_ToString_and_Parse(PgSqlRange<DateTime> input)
    {
        //Act
        var wellKnownText = input.ToString();
        var result = PgSqlRange<DateTime>.Parse(wellKnownText);

        //Assert
        result.Should().Be(input);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("EMPTY")]
    [InlineData("  EmPtY  ")]
    public void Parse_empty(string value)
    {
        //Act
        var result = PgSqlRange<int>.Parse(value);

        //Assert
        result.Should().Be(PgSqlRange<int>.Empty);
    }

    [Theory]
    [InlineData("(0,1)")]
    [InlineData("(0,1]")]
    [InlineData("[0,1)")]
    [InlineData("[0,1]")]
    [InlineData(" [ 0 , 1 ] ")]
    public void roundtrip_int_ranges_through_ToString_and_Parse(string input)
    {
        //Act
        var result = PgSqlRange<int>.Parse(input);

        //Assert
        result.ToString().Should().Be(input.Replace(" ", null));
    }

    [Theory]
    [InlineData("(1,1)", "empty")]
    [InlineData("[1,1)", "empty")]
    [InlineData("[,1]", "(,1]")]
    [InlineData("[1,]", "[1,)")]
    [InlineData("[,]", "(,)")]
    [InlineData("[-infinity,infinity]", "(,)")]
    [InlineData("[ -infinity , infinity ]", "(,)")]
    [InlineData("[-infinity,infinity)", "(,)")]
    [InlineData("(-infinity,infinity]", "(,)")]
    [InlineData("(-infinity,infinity)", "(,)")]
    [InlineData("[null,null]", "(,)")]
    [InlineData("[null,infinity]", "(,)")]
    [InlineData("[-infinity,null]", "(,)")]
    public void int_range_Parse_ToString_returns_normalized_representations(string input, string normalized)
    {
        //Act
        var result = PgSqlRange<int>.Parse(input);

        //Assert
        result.ToString().Should().Be(normalized);
    }

    [Theory]
    [InlineData("(1,1)", "empty")]
    [InlineData("[1,1)", "empty")]
    [InlineData("[,1]", "(,1]")]
    [InlineData("[1,]", "[1,)")]
    [InlineData("[,]", "(,)")]
    [InlineData("[-infinity,infinity]", "(,)")]
    [InlineData("[ -infinity , infinity ]", "(,)")]
    [InlineData("[-infinity,infinity)", "(,)")]
    [InlineData("(-infinity,infinity]", "(,)")]
    [InlineData("(-infinity,infinity)", "(,)")]
    [InlineData("[null,null]", "(,)")]
    [InlineData("[null,infinity]", "(,)")]
    [InlineData("[-infinity,null]", "(,)")]
    public void nullable_int_range_Parse_ToString_returns_normalized_representations(string input, string normalized)
    {
        //Act
        var result = PgSqlRange<int?>.Parse(input);

        //Assert
        result.ToString().Should().Be(normalized);
    }

    [Theory]
    [InlineData("(a,a)", "empty")]
    [InlineData("[a,a)", "empty")]
    [InlineData("[a,a]", "[a,a]")]
    [InlineData("(a,b)", "(a,b)")]
    public void string_range_Parse_ToString_returns_normalized_representations(string input, string normalized)
    {
        //Act
        var result = PgSqlRange<string>.Parse(input);

        //Assert
        result.ToString().Should().Be(normalized);
    }

    [Theory]
    [InlineData("(one,two)")]
    public void roundtrip_string_ranges_through_ToString_and_Parse2(string input)
    {
        //Act
        var result = PgSqlRange<SimpleType>.Parse(input);

        //Assert
        result.ToString().Should().Be(input);
    }

    [Theory]
    [InlineData("0, 1)")]
    [InlineData("(0 1)")]
    [InlineData("(0, 1")]
    [InlineData(" 0, 1 ")]
    public void Parse_malformed_range_throws(string input)
        => Assert.Throws<FormatException>(() => PgSqlRange<int>.Parse(input));

    [Fact(Skip = "Fails only on build server, can't reproduce locally.")]
    public void type_converter()
    {
        //Arrange
        PgSqlRange<int>.RangeTypeConverter.Register();
        var converter = TypeDescriptor.GetConverter(typeof(PgSqlRange<int>));

        //Act
        converter.Should().BeAssignableTo<PgSqlRange<int>.RangeTypeConverter>();
        converter.CanConvertFrom(typeof(string)).Should().BeTrue();
        var result = converter.ConvertFromString("empty");

        //Assert
        result.Should().BeOfType<PgSqlRange<int>>().Which.IsEmpty.Should().BeTrue();
    }

    #endregion

    #region TheoryData

    [TypeConverter(typeof(SimpleTypeConverter))]
    class SimpleType
    {
        string Value { get; }

        SimpleType(string value)
            => Value = value;

        public override string ToString()
            => Value;

        class SimpleTypeConverter : TypeConverter
        {
            public override bool CanConvertFrom(ITypeDescriptorContext context, Type sourceType)
                => typeof(string) == sourceType;

            public override object ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, object value)
                => new SimpleType(value.ToString());
        }
    }

    // ReSharper disable once InconsistentNaming
    static readonly DateTime May_17_2018 = DateTime.Parse("2018-05-17");

    // ReSharper disable once InconsistentNaming
    static readonly DateTime May_18_2018 = DateTime.Parse("2018-05-18");

    /// <summary>
    /// Provides theory data for <see cref="PgSqlRange{T}"/> of <see cref="DateTime"/>.
    /// </summary>
    public static object[][] DateTimeRangeTheoryData =>
        new object[][]
        {
            // (2018-05-17, 2018-05-18)
            [new PgSqlRange<DateTime>(May_17_2018, false, false, May_18_2018, false, false)],

            // [2018-05-17, 2018-05-18]
            [new PgSqlRange<DateTime>(May_17_2018, true, false, May_18_2018, true, false)],

            // [2018-05-17, 2018-05-18)
            [new PgSqlRange<DateTime>(May_17_2018, true, false, May_18_2018, false, false)],

            // (2018-05-17, 2018-05-18]
            [new PgSqlRange<DateTime>(May_17_2018, false, false, May_18_2018, true, false)],

            // (,)
            [new PgSqlRange<DateTime>(default, false, true, default, false, true)],
            [new PgSqlRange<DateTime>(May_17_2018, false, true, May_18_2018, false, true)],

            // (2018-05-17,)
            [new PgSqlRange<DateTime>(May_17_2018, false, false, default, false, true)],
            [new PgSqlRange<DateTime>(May_17_2018, false, false, May_18_2018, false, true)],

            // (,2018-05-18)
            [new PgSqlRange<DateTime>(default, false, true, May_18_2018, false, false)],
            [new PgSqlRange<DateTime>(May_17_2018, false, true, May_18_2018, false, false)]
        };

    #endregion

    protected override PgSqlDataSource DataSource => fixture.GetDataSource(ConnectionString);
}

public sealed class RangeTests_NonMultiplexing(RangeTestsFixture fixture) : RangeTests(MultiplexingMode.NonMultiplexing, fixture);
public sealed class RangeTests_Multiplexing(RangeTestsFixture fixture) : RangeTests(MultiplexingMode.Multiplexing, fixture);

/// <summary>
/// The once-per-test-class state of <see cref="RangeTests"/>: the data source (with the Europe/Berlin time zone) all the tests of
/// one concrete class share, built on first use from that class's connection string and disposed when the class is done.
/// </summary>
public sealed class RangeTestsFixture : IDisposable
{
    readonly object _lock = new();
    PgSqlDataSource _dataSource;

    internal PgSqlDataSource GetDataSource(string connectionString)
    {
        lock (_lock)
        {
            return _dataSource ??= new PgSqlDataSourceBuilder(connectionString)
            {
                ConnectionStringBuilder = { Timezone = "Europe/Berlin" }
            }.Build();
        }
    }

    public void Dispose() => _dataSource?.Dispose();
}
