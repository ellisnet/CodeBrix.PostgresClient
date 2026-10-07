using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using CodeBrix.PostgresClient.PgSqlTypes;
using CodeBrix.PostgresClient.Properties;
using SilverAssertions;
using Xunit;
using static CodeBrix.PostgresClient.Tests.TestUtil;

namespace CodeBrix.PostgresClient.Tests.Types; //was previously: Npgsql.Tests.Types;

public class MultirangeTests(MultirangeTestsFixture fixture) : TestBase, IClassFixture<MultirangeTestsFixture>, IAsyncLifetime
{
    public static readonly object[][] MultirangeTestCases =
    [
        // int4multirange
        [
                new PgSqlRange<int>[]
                {
                    new(3, true, false, 7, false, false),
                    new(9, true, false, 0, false, true)
                },
                "{[3,7),[9,)}", "int4multirange", PgSqlDbType.IntegerMultirange, true, true, default(PgSqlRange<int>)],

        // int8multirange
        [
                new PgSqlRange<long>[]
                {
                    new(3, true, false, 7, false, false),
                    new(9, true, false, 0, false, true)
                },
                "{[3,7),[9,)}", "int8multirange", PgSqlDbType.BigIntMultirange, true, true, default(PgSqlRange<long>)],

        // nummultirange
        // numeric is non-discrete so doesn't undergo normalization, use that to test bound scenarios which otherwise get normalized
        [
                new PgSqlRange<decimal>[]
                {
                    new(3, true, false, 7, true, false),
                    new(9, false, false, 0, false, true)
                },
                "{[3,7],(9,)}", "nummultirange", PgSqlDbType.NumericMultirange, true, true, default(PgSqlRange<decimal>)],

        // daterange
        [
                new PgSqlRange<DateOnly>[]
                {
                    new(new(2020, 1, 1), true, false, new(2020, 1, 5), false, false),
                    new(new(2020, 1, 10), true, false, default, false, true)
                },
                "{[2020-01-01,2020-01-05),[2020-01-10,)}", "datemultirange", PgSqlDbType.DateMultirange, true, false, default(PgSqlRange<DateOnly>)],

        // tsmultirange
        [
                new PgSqlRange<DateTime>[]
                {
                    new(new(2020, 1, 1), true, false, new(2020, 1, 5), false, false),
                    new(new(2020, 1, 10), true, false, default, false, true)
                },
                """{["2020-01-01 00:00:00","2020-01-05 00:00:00"),["2020-01-10 00:00:00",)}""", "tsmultirange", PgSqlDbType.TimestampMultirange, true, true, default(PgSqlRange<DateTime>)],

        // tstzmultirange
        [
                new PgSqlRange<DateTime>[]
                {
                    new(new(2020, 1, 1, 0, 0, 0, kind: DateTimeKind.Utc), true, false, new(2020, 1, 5, 0, 0, 0, kind: DateTimeKind.Utc), false, false),
                    new(new(2020, 1, 10, 0, 0, 0, kind: DateTimeKind.Utc), true, false, default, false, true)
                },
                """{["2020-01-01 01:00:00+01","2020-01-05 01:00:00+01"),["2020-01-10 01:00:00+01",)}""", "tstzmultirange", PgSqlDbType.TimestampTzMultirange, true, true, default(PgSqlRange<DateTime>)],

        [
                new PgSqlRange<DateOnly>[]
                {
                    new(new(2020, 1, 1), true, false, new(2020, 1, 5), false, false),
                    new(new(2020, 1, 10), true, false, default, false, true)
                },
                "{[2020-01-01,2020-01-05),[2020-01-10,)}", "datemultirange", PgSqlDbType.DateMultirange, false, false, default(PgSqlRange<DateOnly>)]
    ];

    [Theory, MemberData(nameof(MultirangeTestCases))]
    public Task multirange_as_array<T, TRange>(
        T multirangeAsArray, string sqlLiteral, string pgTypeName, PgSqlDbType? pgSqlDbType, bool isDefaultForReading, bool isDefaultForWriting, TRange _)
        => AssertType(multirangeAsArray, sqlLiteral, pgTypeName, pgSqlDbType, isDefaultForReading: isDefaultForReading,
            isDefaultForWriting: isDefaultForWriting);

    // The row's isDefaultForReading value is not used here (a List is never the default read type), hence the discard name _1
    [Theory, MemberData(nameof(MultirangeTestCases))]
    public Task multirange_as_list<T, TRange>(
        T multirangeAsArray, string sqlLiteral, string pgTypeName, PgSqlDbType? pgSqlDbType, bool _1, bool isDefaultForWriting, TRange _)
        where T : IList<TRange>
        => AssertType(
            new List<TRange>(multirangeAsArray),
            sqlLiteral, pgTypeName, pgSqlDbType, isDefaultForReading: false, isDefaultForWriting: isDefaultForWriting);

    [Fact]
    public async Task unmapped_multirange_with_mapped_subtype()
    {
        //Arrange
        await using var dataSource = CreateDataSource(b => b.EnableUnmappedTypes().ConnectionStringBuilder.MaxPoolSize = 1);
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        var typeName = await GetTempTypeName(conn);
        await conn.ExecuteNonQueryAsync($"CREATE TYPE {typeName} AS RANGE(subtype=text)", cancellationToken: TestContext.Current.CancellationToken);
        await Task.Yield(); // TODO: fix multiplexing deadlock bug
        conn.ReloadTypes();
        (await conn.ExecuteScalarAsync("SELECT 1", cancellationToken: TestContext.Current.CancellationToken)).Should().Be(1);

        var value = new[] {new PgSqlRange<char[]>(
            new string('a', conn.Settings.WriteBufferSize + 10).ToCharArray(),
            new string('z', conn.Settings.WriteBufferSize + 10).ToCharArray()
        )};

        await using var cmd = new PgSqlCommand("SELECT @p", conn);
        cmd.Parameters.Add(new PgSqlParameter { DataTypeName = typeName + "_multirange", ParameterName = "p", Value = value });

        //Act
        await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess, TestContext.Current.CancellationToken);
        await reader.ReadAsync(TestContext.Current.CancellationToken);

        //Assert
        reader.GetFieldType(0).Should().Be(typeof(PgSqlRange<string>[]));
        var result = reader.GetFieldValue<PgSqlRange<char[]>[]>(0);
        (result[0].LowerBound.SequenceEqual(value[0].LowerBound) && result[0].UpperBound.SequenceEqual(value[0].UpperBound))
            .Should().BeTrue("the read multirange should have the written bounds");
    }

    [Fact]
    public async Task unmapped_multirange_supported_only_with_EnableUnmappedTypes()
    {
        //Arrange
        await using var connection = await DataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        var rangeType = await GetTempTypeName(connection);
        var multirangeTypeName = rangeType + "_multirange";
        await connection.ExecuteNonQueryAsync($"CREATE TYPE {rangeType} AS RANGE(subtype=text)", cancellationToken: TestContext.Current.CancellationToken);
        await Task.Yield(); // TODO: fix multiplexing deadlock bug
        await connection.ReloadTypesAsync(TestContext.Current.CancellationToken);

        var errorMessage = string.Format(
            PgSqlStrings.UnmappedRangesNotEnabled,
            nameof(PgSqlSlimDataSourceBuilder.EnableUnmappedTypes),
            nameof(PgSqlDataSourceBuilder));

        //Act
        var exception = await AssertTypeUnsupportedWrite(
            new PgSqlRange<string>[]
            {
                new("bar", "foo"),
                new("moo", "zoo"),
            },
            multirangeTypeName);

        //Assert
        exception.InnerException.Should().BeAssignableTo<NotSupportedException>();
        exception.InnerException.Message.Should().Be(errorMessage);

        exception = await AssertTypeUnsupportedRead("""{["bar","foo"],["moo","zoo"]}""",
            multirangeTypeName);
        exception.InnerException.Should().BeAssignableTo<NotSupportedException>();
        exception.InnerException.Message.Should().Be(errorMessage);

        exception = await AssertTypeUnsupportedRead<PgSqlRange<string>>(
            """{["bar","foo"],["moo","zoo"]}""",
            multirangeTypeName);
        exception.InnerException.Should().BeAssignableTo<NotSupportedException>();
        exception.InnerException.Message.Should().Be(errorMessage);
    }

    protected override PgSqlDataSource DataSource => fixture.DataSource;

    public async ValueTask InitializeAsync()
    {
        await using var conn = await OpenConnectionAsync();
        MinimumPgVersion(conn, "14.0", "Multirange types were introduced in PostgreSQL 14");
    }

    public ValueTask DisposeAsync()
        => ValueTask.CompletedTask;
}

/// <summary>
/// The once-per-test-class state of <see cref="MultirangeTests"/>: the data source (with the Europe/Berlin time zone) all the
/// tests share, disposed when the class is done.
/// </summary>
public sealed class MultirangeTestsFixture : IDisposable
{
    internal PgSqlDataSource DataSource { get; } = new PgSqlDataSourceBuilder(TestUtil.ConnectionString)
    {
        ConnectionStringBuilder = { Timezone = "Europe/Berlin" }
    }.Build();

    public void Dispose() => DataSource.Dispose();
}
