using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using CodeBrix.PostgresClient.PgSqlTypes;
using SilverAssertions;
using Xunit;
using static CodeBrix.PostgresClient.Tests.TestUtil;

namespace CodeBrix.PostgresClient.Tests.Types; //was previously: Npgsql.Tests.Types;

// Since this test suite manipulates TimeZone, it is incompatible with multiplexing
public class DateTimeTests : TestBase
{
    #region Date

    [Fact]
    public Task date_as_DateOnly()
        => AssertType(new DateOnly(2020, 10, 1), "2020-10-01", "date", PgSqlDbType.Date, DbType.Date);

    [Fact]
    public Task date_as_DateTime()
        => AssertType(new DateTime(2020, 10, 1), "2020-10-01", "date", PgSqlDbType.Date, DbType.Date, isDefault: false);

    [Fact]
    public Task date_as_DateTime_with_date_and_time_before_2000()
        => AssertTypeWrite(new DateTime(1980, 10, 1, 11, 0, 0), "1980-10-01", "date", PgSqlDbType.Date, DbType.Date, isDefault: false);

    // Internal PostgreSQL representation (days since 2020-01-01), for out-of-range values.
    [Fact]
    public Task date_as_int()
        => AssertType(7579, "2020-10-01", "date", PgSqlDbType.Date, DbType.Date, isDefault: false);

    [Fact]
    public Task daterange_as_PgSqlRange_of_DateOnly()
        => AssertType(
            new PgSqlRange<DateOnly>(new(2002, 3, 4), true, new(2002, 3, 6), false),
            "[2002-03-04,2002-03-06)",
            "daterange",
            PgSqlDbType.DateRange,
            skipArrayCheck: true); // PgSqlRange<T>[] is mapped to multirange by default, not array; test separately

    [Fact]
    public Task daterange_array_as_PgSqlRange_of_DateOnly_array()
        => AssertType(
            new[]
            {
                new PgSqlRange<DateOnly>(new(2002, 3, 4), true, new(2002, 3, 6), false),
                new PgSqlRange<DateOnly>(new(2002, 3, 8), true, new(2002, 3, 9), false)
            },
            """{"[2002-03-04,2002-03-06)","[2002-03-08,2002-03-09)"}""",
            "daterange[]",
            PgSqlDbType.DateRange | PgSqlDbType.Array,
            isDefaultForWriting: false);

    [Fact]
    public Task daterange_as_PgSqlRange_of_DateTime()
        => AssertType(
            new PgSqlRange<DateTime>(new(2002, 3, 4), true, new(2002, 3, 6), false),
            "[2002-03-04,2002-03-06)",
            "daterange",
            PgSqlDbType.DateRange,
            isDefault: false);

    [Fact]
    public async Task datemultirange_as_array_of_PgSqlRange_of_DateOnly()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        MinimumPgVersion(conn, "14.0", "Multirange types were introduced in PostgreSQL 14");

        //Assert
        await AssertType(
            new[]
            {
                new PgSqlRange<DateOnly>(new(2002, 3, 4), true, new(2002, 3, 6), false),
                new PgSqlRange<DateOnly>(new(2002, 3, 8), true, new(2002, 3, 11), false)
            },
            "{[2002-03-04,2002-03-06),[2002-03-08,2002-03-11)}",
            "datemultirange",
            PgSqlDbType.DateMultirange);
    }

    [Fact]
    public async Task datemultirange_as_array_of_PgSqlRange_of_DateTime()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        MinimumPgVersion(conn, "14.0", "Multirange types were introduced in PostgreSQL 14");

        //Assert
        await AssertType(
            new[]
            {
                new PgSqlRange<DateTime>(new(2002, 3, 4), true, new(2002, 3, 6), false),
                new PgSqlRange<DateTime>(new(2002, 3, 8), true, new(2002, 3, 11), false)
            },
            "{[2002-03-04,2002-03-06),[2002-03-08,2002-03-11)}",
            "datemultirange",
            PgSqlDbType.DateMultirange,
            isDefault: false);
    }

    #endregion

    #region Time

    [Fact]
    public Task time_as_TimeOnly()
        => AssertType(
            new TimeOnly(10, 45, 34, 500),
            "10:45:34.5",
            "time without time zone",
            PgSqlDbType.Time,
            DbType.Time);

    [Fact]
    public Task time_as_TimeSpan()
        => AssertType(
            new TimeSpan(0, 10, 45, 34, 500),
            "10:45:34.5",
            "time without time zone",
            PgSqlDbType.Time,
            DbType.Time,
            isDefault: false);

    #endregion

    #region Time with timezone

    public static readonly TheoryData<DateTimeOffset, string> TimeTzValues = new()
    {
        { new DateTimeOffset(1, 1, 2, 13, 3, 45, 510, TimeSpan.FromHours(2)), "13:03:45.51+02" },
        { new DateTimeOffset(1, 1, 2, 1, 0, 45, 510, TimeSpan.FromHours(-3)), "01:00:45.51-03" },
        { new DateTimeOffset(1212720130000, TimeSpan.Zero), "09:41:12.013+00" },
        { new DateTimeOffset(1, 1, 2, 1, 0, 0, new TimeSpan(0, 2, 0, 0)), "01:00:00+02" }
    };

    [Theory, MemberData(nameof(TimeTzValues))]
    public Task timetz_as_DateTimeOffset(DateTimeOffset time, string sqlLiteral)
        => AssertType(time, sqlLiteral, "time with time zone", PgSqlDbType.TimeTz, isDefault: false);

    #endregion

    #region Timestamp

    public static readonly TheoryData<DateTime, string> TimestampValues = new()
    {
        { new DateTime(1998, 4, 12, 13, 26, 38, DateTimeKind.Unspecified), "1998-04-12 13:26:38" },
        { new DateTime(2015, 1, 27, 8, 45, 12, 345, DateTimeKind.Unspecified), "2015-01-27 08:45:12.345" },
        { new DateTime(2013, 7, 25, 0, 0, 0, DateTimeKind.Unspecified), "2013-07-25 00:00:00" }
    };

    [Theory, MemberData(nameof(TimestampValues))]
    public async Task timestamp_as_DateTime(DateTime dateTime, string sqlLiteral)
    {
        await AssertType(dateTime, sqlLiteral, "timestamp without time zone", PgSqlDbType.Timestamp, DbType.DateTime2,
            // Explicitly check kind as well.
            comparer: (actual, expected) => actual.Kind == expected.Kind && actual.Equals(expected));

        await AssertType(
            new List<DateTime> { dateTime, dateTime }, $$"""{"{{sqlLiteral}}","{{sqlLiteral}}"}""", "timestamp without time zone[]", PgSqlDbType.Timestamp | PgSqlDbType.Array,
            isDefaultForReading: false);
    }

    [Fact]
    public Task timestamp_cannot_write_utc_DateTime()
        => AssertTypeUnsupportedWrite<DateTime, ArgumentException>(new DateTime(1998, 4, 12, 13, 26, 38, DateTimeKind.Utc), "timestamp without time zone");

    [Fact]
    public Task timestamp_as_long()
        => AssertType(
            -54297202000000,
            "1998-04-12 13:26:38",
            "timestamp without time zone",
            PgSqlDbType.Timestamp,
            DbType.DateTime2,
            isDefault: false);

    [Fact]
    public Task timestamp_cannot_use_as_DateTimeOffset()
        => AssertTypeUnsupported(
            new DateTimeOffset(1998, 4, 12, 13, 26, 38, TimeSpan.Zero),
            "1998-04-12 13:26:38",
            "timestamp without time zone");

    [Fact]
    public Task tsrange_as_PgSqlRange_of_DateTime()
        => AssertType(
            new PgSqlRange<DateTime>(
                new(1998, 4, 12, 13, 26, 38, DateTimeKind.Local),
                new(1998, 4, 12, 15, 26, 38, DateTimeKind.Local)),
            @"[""1998-04-12 13:26:38"",""1998-04-12 15:26:38""]",
            "tsrange",
            PgSqlDbType.TimestampRange,
            skipArrayCheck: true); // PgSqlRange<T>[] is mapped to multirange by default, not array; test separately

    [Fact]
    public Task tsrange_array_as_PgSqlRange_of_DateTime_array()
        => AssertType(
            new[]
            {
                new PgSqlRange<DateTime>(
                    new(1998, 4, 12, 13, 26, 38, DateTimeKind.Local),
                    new(1998, 4, 12, 15, 26, 38, DateTimeKind.Local)),
                new PgSqlRange<DateTime>(
                    new(1998, 4, 13, 13, 26, 38, DateTimeKind.Local),
                    new(1998, 4, 13, 15, 26, 38, DateTimeKind.Local)),
            },
            """{"[\"1998-04-12 13:26:38\",\"1998-04-12 15:26:38\"]","[\"1998-04-13 13:26:38\",\"1998-04-13 15:26:38\"]"}""",
            "tsrange[]",
            PgSqlDbType.TimestampRange | PgSqlDbType.Array,
            isDefault: false);

    [Fact]
    public async Task tsmultirange_as_array_of_PgSqlRange_of_DateTime()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        MinimumPgVersion(conn, "14.0", "Multirange types were introduced in PostgreSQL 14");

        //Assert
        await AssertType(
            new[]
            {
                new PgSqlRange<DateTime>(
                    new(1998, 4, 12, 13, 26, 38, DateTimeKind.Local),
                    new(1998, 4, 12, 15, 26, 38, DateTimeKind.Local)),
                new PgSqlRange<DateTime>(
                    new(1998, 4, 13, 13, 26, 38, DateTimeKind.Local),
                    new(1998, 4, 13, 15, 26, 38, DateTimeKind.Local)),
            },
            @"{[""1998-04-12 13:26:38"",""1998-04-12 15:26:38""],[""1998-04-13 13:26:38"",""1998-04-13 15:26:38""]}",
            "tsmultirange",
            PgSqlDbType.TimestampMultirange);
    }

    #endregion

    #region Timestamp with timezone

    // Note that the below text representations are local (according to TimeZone, which is set to Europe/Berlin in this test class),
    // because that's how PG does timestamptz *text* representation.
    public static readonly TheoryData<DateTime, string> TimestampTzWriteValues = new()
    {
        { new DateTime(1998, 4, 12, 13, 26, 38, DateTimeKind.Utc), "1998-04-12 15:26:38+02" },
        { new DateTime(2015, 1, 27, 8, 45, 12, 345, DateTimeKind.Utc), "2015-01-27 09:45:12.345+01" },
        { new DateTime(2013, 7, 25, 0, 0, 0, DateTimeKind.Utc), "2013-07-25 02:00:00+02" }
    };

    [Theory, MemberData(nameof(TimestampTzWriteValues))]
    public async Task timestamptz_as_DateTime(DateTime dateTime, string sqlLiteral)
    {
        await AssertType(dateTime, sqlLiteral, "timestamp with time zone", PgSqlDbType.TimestampTz, DbType.DateTime,
            // Explicitly check kind as well.
            comparer: (actual, expected) => actual.Kind == expected.Kind && actual.Equals(expected));

        await AssertType(
            new List<DateTime> { dateTime, dateTime }, $$"""{"{{sqlLiteral}}","{{sqlLiteral}}"}""", "timestamp with time zone[]", PgSqlDbType.TimestampTz | PgSqlDbType.Array,
            isDefaultForReading: false);

    }

    [Fact]
    public async Task timestamptz_infinity_as_DateTime()
    {
        await AssertType(DateTime.MinValue, "-infinity", "timestamp with time zone", PgSqlDbType.TimestampTz, DbType.DateTime,
            isDefault: false);
        await AssertType(DateTime.MaxValue, "infinity", "timestamp with time zone", PgSqlDbType.TimestampTz, DbType.DateTime,
            isDefault: false);
    }

    [Fact]
    public async Task timestamptz_cannot_write_non_utc_DateTime()
    {
        await AssertTypeUnsupportedWrite<DateTime, ArgumentException>(new DateTime(1998, 4, 12, 13, 26, 38, DateTimeKind.Unspecified), "timestamp with time zone");
        await AssertTypeUnsupportedWrite<DateTime, ArgumentException>(new DateTime(1998, 4, 12, 13, 26, 38, DateTimeKind.Local), "timestamp with time zone");
    }

    [Fact]
    public async Task timestamptz_as_DateTimeOffset_utc()
    {
        //Act
        var dateTimeOffset = await AssertType(
            new DateTimeOffset(1998, 4, 12, 13, 26, 38, TimeSpan.Zero),
            "1998-04-12 15:26:38+02",
            "timestamp with time zone",
            PgSqlDbType.TimestampTz,
            DbType.DateTime,
            isDefaultForReading: false);

        //Assert
        dateTimeOffset.Offset.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public Task timestamptz_as_DateTimeOffset_utc_with_DbType_DateTimeOffset()
        => AssertTypeWrite(
            new DateTimeOffset(1998, 4, 12, 13, 26, 38, TimeSpan.Zero),
            "1998-04-12 15:26:38+02",
            "timestamp with time zone",
            PgSqlDbType.TimestampTz,
            DbType.DateTimeOffset,
            inferredDbType: DbType.DateTime,
            isDefault: false);

    [Fact]
    public Task timestamptz_cannot_write_non_utc_DateTimeOffset()
        => AssertTypeUnsupportedWrite<DateTimeOffset, ArgumentException>(new DateTimeOffset(1998, 4, 12, 13, 26, 38, TimeSpan.FromHours(2)));

    [Fact]
    public Task timestamptz_as_long()
        => AssertType(
            -54297202000000,
            "1998-04-12 15:26:38+02",
            "timestamp with time zone",
            PgSqlDbType.TimestampTz,
            DbType.DateTime,
            isDefault: false);

    [Fact]
    public async Task timestamptz_array_as_DateTimeOffset_array()
    {
        //Act
        var dateTimeOffsets = await AssertType(
            new[]
            {
                new DateTimeOffset(1998, 4, 12, 13, 26, 38, TimeSpan.Zero),
                new DateTimeOffset(1999, 4, 12, 13, 26, 38, TimeSpan.Zero)
            },
            """{"1998-04-12 15:26:38+02","1999-04-12 15:26:38+02"}""",
            "timestamp with time zone[]",
            PgSqlDbType.TimestampTz | PgSqlDbType.Array,
            isDefaultForReading: false);

        //Assert
        dateTimeOffsets[0].Offset.Should().Be(TimeSpan.Zero);
        dateTimeOffsets[1].Offset.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public Task tstzrange_as_PgSqlRange_of_DateTime()
        => AssertType(
            new PgSqlRange<DateTime>(
                new DateTime(1998, 4, 12, 13, 26, 38, DateTimeKind.Utc),
                new DateTime(1998, 4, 12, 15, 26, 38, DateTimeKind.Utc)),
            @"[""1998-04-12 15:26:38+02"",""1998-04-12 17:26:38+02""]",
            "tstzrange",
            PgSqlDbType.TimestampTzRange,
            skipArrayCheck: true); // PgSqlRange<T>[] is mapped to multirange by default, not array; test separately

    [Fact]
    public Task tstzrange_array_as_PgSqlRange_of_DateTime_array()
        => AssertType(
            new[]
            {
                new PgSqlRange<DateTime>(
                    new(1998, 4, 12, 13, 26, 38, DateTimeKind.Utc),
                    new(1998, 4, 12, 15, 26, 38, DateTimeKind.Utc)),
                new PgSqlRange<DateTime>(
                    new(1998, 4, 13, 13, 26, 38, DateTimeKind.Utc),
                    new(1998, 4, 13, 15, 26, 38, DateTimeKind.Utc)),
            },
            """{"[\"1998-04-12 15:26:38+02\",\"1998-04-12 17:26:38+02\"]","[\"1998-04-13 15:26:38+02\",\"1998-04-13 17:26:38+02\"]"}""",
            "tstzrange[]",
            PgSqlDbType.TimestampTzRange | PgSqlDbType.Array,
            isDefault: false);

    [Fact]
    public async Task tstzmultirange_as_array_of_PgSqlRange_of_DateTime()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        MinimumPgVersion(conn, "14.0", "Multirange types were introduced in PostgreSQL 14");

        //Assert
        await AssertType(
            new[]
            {
                new PgSqlRange<DateTime>(
                    new DateTime(1998, 4, 12, 13, 26, 38, DateTimeKind.Utc),
                    new DateTime(1998, 4, 12, 15, 26, 38, DateTimeKind.Utc)),
                new PgSqlRange<DateTime>(
                    new DateTime(1998, 4, 13, 13, 26, 38, DateTimeKind.Utc),
                    new DateTime(1998, 4, 13, 15, 26, 38, DateTimeKind.Utc)),
            },
            @"{[""1998-04-12 15:26:38+02"",""1998-04-12 17:26:38+02""],[""1998-04-13 15:26:38+02"",""1998-04-13 17:26:38+02""]}",
            "tstzmultirange",
            PgSqlDbType.TimestampTzMultirange);
    }

    [Fact]
    public Task cannot_mix_DateTime_Kinds_in_array()
        => AssertTypeUnsupportedWrite<DateTime[], ArgumentException>([
            new DateTime(1998, 4, 12, 13, 26, 38, DateTimeKind.Utc),
            new DateTime(1998, 4, 12, 13, 26, 38, DateTimeKind.Local)
        ]);

    [Fact]
    public Task cannot_mix_DateTime_Kinds_in_range()
        => AssertTypeUnsupportedWrite<PgSqlRange<DateTime>, ArgumentException>(new PgSqlRange<DateTime>(
            new DateTime(1998, 4, 12, 13, 26, 38, DateTimeKind.Utc),
            new DateTime(1998, 4, 12, 13, 26, 38, DateTimeKind.Local)));

    [Fact]
    public async Task cannot_mix_DateTime_Kinds_in_multirange()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        MinimumPgVersion(conn, "14.0", "Multirange types were introduced in PostgreSQL 14");

        //Assert
        await AssertTypeUnsupportedWrite<PgSqlRange<DateTime>[], ArgumentException>([
            new PgSqlRange<DateTime>(
                new DateTime(1998, 4, 12, 13, 26, 38, DateTimeKind.Utc),
                new DateTime(1998, 4, 12, 15, 26, 38, DateTimeKind.Utc)),
            new PgSqlRange<DateTime>(
                new DateTime(1998, 4, 12, 13, 26, 38, DateTimeKind.Utc),
                new DateTime(1998, 4, 12, 15, 26, 38, DateTimeKind.Utc)),
            new PgSqlRange<DateTime>(
                new DateTime(1998, 4, 12, 13, 26, 38, DateTimeKind.Utc),
                new DateTime(1998, 4, 12, 15, 26, 38, DateTimeKind.Utc)),
            new PgSqlRange<DateTime>(
                new DateTime(1998, 4, 12, 13, 26, 38, DateTimeKind.Utc),
                new DateTime(1998, 4, 12, 15, 26, 38, DateTimeKind.Utc)),
            new PgSqlRange<DateTime>(
                new DateTime(1998, 4, 12, 13, 26, 38, DateTimeKind.Utc),
                new DateTime(1998, 4, 12, 15, 26, 38, DateTimeKind.Utc)),
            new PgSqlRange<DateTime>(
                new DateTime(1998, 4, 12, 13, 26, 38, DateTimeKind.Utc),
                new DateTime(1998, 4, 12, 15, 26, 38, DateTimeKind.Utc)),
            new PgSqlRange<DateTime>(
                new DateTime(1998, 4, 12, 13, 26, 38, DateTimeKind.Utc),
                new DateTime(1998, 4, 12, 15, 26, 38, DateTimeKind.Utc)),
            new PgSqlRange<DateTime>(
                new DateTime(1998, 4, 12, 13, 26, 38, DateTimeKind.Utc),
                new DateTime(1998, 4, 12, 15, 26, 38, DateTimeKind.Utc)),
            new PgSqlRange<DateTime>(
                new DateTime(1998, 4, 13, 13, 26, 38, DateTimeKind.Local),
                new DateTime(1998, 4, 13, 15, 26, 38, DateTimeKind.Local))
        ]);
    }

    [Fact]
    public void PgSqlParameterDbType_is_value_dependent_datetime_or_datetime2()
    {
        //Arrange
        var localtimestamp = new PgSqlParameter { Value = DateTime.Now };
        var unspecifiedtimestamp = new PgSqlParameter { Value = new DateTime() };

        //Assert
        localtimestamp.DbType.Should().Be(DbType.DateTime2);
        unspecifiedtimestamp.DbType.Should().Be(DbType.DateTime2);

        // We don't support any DateTimeOffset other than offset 0 which maps to timestamptz,
        // we might add an exception for offset == DateTimeOffset.Now.Offset (local offset) mapping to timestamp at some point.
        // var dtotimestamp = new PgSqlParameter { Value = DateTimeOffset.Now };
        // Assert.AreEqual(DbType.DateTime2, dtotimestamp.DbType);

        var timestamptz = new PgSqlParameter { Value = DateTime.UtcNow };
        var dtotimestamptz = new PgSqlParameter { Value = DateTimeOffset.UtcNow };
        timestamptz.DbType.Should().Be(DbType.DateTime);
        dtotimestamptz.DbType.Should().Be(DbType.DateTime);
    }

    [Fact]
    public void PgSqlParameterPgSqlDbType_is_value_dependent_timestamp_or_timestamptz()
    {
        //Arrange
        var localtimestamp = new PgSqlParameter { Value = DateTime.Now };
        var unspecifiedtimestamp = new PgSqlParameter { Value = new DateTime() };

        //Assert
        localtimestamp.PgSqlDbType.Should().Be(PgSqlDbType.Timestamp);
        unspecifiedtimestamp.PgSqlDbType.Should().Be(PgSqlDbType.Timestamp);

        var timestamptz = new PgSqlParameter { Value = DateTime.UtcNow };
        var dtotimestamptz = new PgSqlParameter { Value = DateTimeOffset.UtcNow };
        timestamptz.PgSqlDbType.Should().Be(PgSqlDbType.TimestampTz);
        dtotimestamptz.PgSqlDbType.Should().Be(PgSqlDbType.TimestampTz);
    }

    [Fact]
    public async Task array_of_nullable_timestamptz()
    {
        //Arrange
        await using var datasource = CreateDataSource(csb =>
        {
            csb.ArrayNullabilityMode = ArrayNullabilityMode.PerInstance;
            csb.Timezone = "Europe/Berlin";
        });

        //Assert
        await AssertType(datasource,
            new DateTime?[]
            {
                new DateTime(1998, 4, 12, 13, 26, 38, DateTimeKind.Utc),
                null
            },
            @"{""1998-04-12 15:26:38+02"",NULL}",
            "timestamp with time zone[]",
            PgSqlDbType.TimestampTz | PgSqlDbType.Array);

        // Make sure delayed converter resolution works when null precedes a non-null value.
        // We expect the resolution of null values to not lock in the default type timestamp.
        // This would cause the subsequent non-null value to fail to convert, as it requires timestamptz.
        await AssertType(datasource,
            new DateTime?[]
            {
                null,
                new DateTime(1998, 4, 12, 13, 26, 38, DateTimeKind.Utc)
            },
            @"{NULL,""1998-04-12 15:26:38+02""}",
            "timestamp with time zone[]",
            PgSqlDbType.TimestampTz | PgSqlDbType.Array);

        await AssertType(datasource,
            new DateTime?[]
            {
                new DateTime(1998, 4, 12, 13, 26, 38, DateTimeKind.Utc),
            },
            @"{""1998-04-12 15:26:38+02""}",
            "timestamp with time zone[]",
            PgSqlDbType.TimestampTz | PgSqlDbType.Array,
            isDefaultForReading: false); // we write DateTime?[], but will read DateTime[] from GetValue
    }

    #endregion

    #region Interval

    public static readonly TheoryData<TimeSpan, string> IntervalValues = new()
    {
        { new TimeSpan(0, 2, 3, 4, 5), "02:03:04.005" },
        { new TimeSpan(1, 2, 3, 4, 5), "1 day 02:03:04.005" },
        { new TimeSpan(61, 2, 3, 4, 5), "61 days 02:03:04.005" },
        { new TimeSpan(new TimeSpan(2, 3, 4).Ticks + 10), "02:03:04.000001" }
    };

    [Theory, MemberData(nameof(IntervalValues))]
    public Task interval_as_TimeSpan(TimeSpan timeSpan, string sqlLiteral)
        => AssertType(timeSpan, sqlLiteral, "interval", PgSqlDbType.Interval);

    [Fact]
    public Task interval_write_as_TimeSpan_truncates_ticks()
        => AssertTypeWrite(
            new TimeSpan(new TimeSpan(2, 3, 4).Ticks + 1),
            "02:03:04",
            "interval",
            PgSqlDbType.Interval);

    [Fact]
    public Task interval_as_PgSqlInterval()
        => AssertType(
            new PgSqlInterval(2, 15, 7384005000),
            "2 mons 15 days 02:03:04.005", "interval",
            PgSqlDbType.Interval,
            isDefaultForReading: false);

    [Fact]
    public Task interval_with_months_cannot_read_as_TimeSpan()
        => AssertTypeUnsupportedRead<TimeSpan, InvalidCastException>("1 month 2 days", "interval");

    #endregion

    protected override async ValueTask<PgSqlConnection> OpenConnectionAsync()
    {
        var conn = await base.OpenConnectionAsync();
        await conn.ExecuteNonQueryAsync("SET TimeZone='Europe/Berlin'");
        return conn;
    }

    protected override PgSqlConnection OpenConnection()
        => throw new NotSupportedException();
}
