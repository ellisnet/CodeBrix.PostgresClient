using System;
using System.Data;
using System.Threading.Tasks;
using CodeBrix.PostgresClient.PgSqlTypes;
using CodeBrix.PostgresClient.Tests.Support;
using Xunit;
using static CodeBrix.PostgresClient.Util.Statics;

namespace CodeBrix.PostgresClient.Tests.Types; //was previously: Npgsql.Tests.Types;

public abstract class DateTimeInfinityTests : TestBase, IDisposable
{
    public static readonly TheoryData<DateTime, string, string> TimestampDateTimeValues = new()
    {
        { DateTime.MinValue.AddYears(1), "0002-01-01 00:00:00", "0002-01-01 00:00:00" },
        { DateTime.MinValue, "0001-01-01 00:00:00", "-infinity" },
        { DateTime.MaxValue, "9999-12-31 23:59:59.999999", "infinity" }
    };

    public static readonly TheoryData<DateTime, string, string> TimestampTzDateTimeValues = new()
    {
        { DateTime.MinValue.AddYears(1), "0002-01-01 00:00:00+00", "0002-01-01 00:00:00+00" },
        { DateTime.MinValue, "0001-01-01 00:00:00+00", "-infinity" },
        { DateTime.MaxValue, "9999-12-31 23:59:59.999999+00", "infinity" }
    };

    public static readonly TheoryData<DateTimeOffset, string, string> TimestampTzDateTimeOffsetValues = new()
    {
        { DateTimeOffset.MinValue.ToUniversalTime().AddYears(1), "0002-01-01 00:00:00+00", "0002-01-01 00:00:00+00" },
        { DateTimeOffset.MinValue, "0001-01-01 00:00:00+00", "-infinity" },
        { DateTimeOffset.MaxValue, "9999-12-31 23:59:59.999999+00", "infinity" }
    };

    public static readonly TheoryData<DateTime, string, string> DateDateTimeValues = new()
    {
        { DateTime.MinValue.AddYears(1), "0002-01-01", "0002-01-01" },
        { DateTime.MinValue, "0001-01-01", "-infinity" },
        { DateTime.MaxValue, "9999-12-31", "infinity" }
    };

    // As we can't roundtrip DateTime.MaxValue due to precision differences with postgres we are lenient with equality for this particular value.
    static readonly Func<DateTime, DateTime, bool> MaxValuePrecisionLenientComparer =
        (expected, actual) => expected == DateTime.MaxValue && actual == new DateTime(expected.Ticks - 9) || actual == expected;

    [Theory, MemberData(nameof(TimestampDateTimeValues))]
    public Task timestamp_DateTime(DateTime dateTime, string sqlLiteral, string infinityConvertedSqlLiteral)
        => AssertType(dateTime, DisableDateTimeInfinityConversions ? sqlLiteral : infinityConvertedSqlLiteral,
            "timestamp without time zone", PgSqlDbType.Timestamp, DbType.DateTime2,
            comparer: MaxValuePrecisionLenientComparer,
            isDefault: true);

    [Theory, MemberData(nameof(TimestampTzDateTimeValues))]
    public Task timestamptz_DateTime(DateTime dateTime, string sqlLiteral, string infinityConvertedSqlLiteral)
        => AssertType(new(dateTime.Ticks, DateTimeKind.Utc), DisableDateTimeInfinityConversions ? sqlLiteral : infinityConvertedSqlLiteral,
            "timestamp with time zone", PgSqlDbType.TimestampTz, DbType.DateTime, DbType.DateTime,
            comparer: MaxValuePrecisionLenientComparer,
            isDefault: true);

    [Theory, MemberData(nameof(TimestampTzDateTimeOffsetValues))]
    public Task timestamptz_DateTimeOffset(DateTimeOffset dateTime, string sqlLiteral, string infinityConvertedSqlLiteral)
        => AssertType(dateTime, DisableDateTimeInfinityConversions ? sqlLiteral : infinityConvertedSqlLiteral,
            "timestamp with time zone", PgSqlDbType.TimestampTz, DbType.DateTime, DbType.DateTime,
            comparer: (expected, actual) => MaxValuePrecisionLenientComparer(expected.DateTime, actual.DateTime),
            isDefault: false);

    [Theory, MemberData(nameof(DateDateTimeValues))]
    public Task date_DateTime(DateTime dateTime, string sqlLiteral, string infinityConvertedSqlLiteral)
        => AssertType(DisableDateTimeInfinityConversions ? dateTime.Date : dateTime, DisableDateTimeInfinityConversions ? sqlLiteral : infinityConvertedSqlLiteral,
            "date", PgSqlDbType.Date, DbType.Date,
            isDefault: false);

    public static readonly TheoryData<DateOnly, string, string> DateOnlyDateTimeValues = new()
    {
        { DateOnly.MinValue.AddYears(1), "0002-01-01", "0002-01-01" },
        { DateOnly.MinValue, "0001-01-01", "-infinity" },
        { DateOnly.MaxValue, "9999-12-31", "infinity" }
    };

    [Theory, MemberData(nameof(DateOnlyDateTimeValues))]
    public Task date_DateOnly(DateOnly dateTime, string sqlLiteral, string infinityConvertedSqlLiteral)
        => AssertType(dateTime,
            DisableDateTimeInfinityConversions ? sqlLiteral : infinityConvertedSqlLiteral, "date", PgSqlDbType.Date, DbType.Date,
            isDefault: false);

    PgSqlDataSource _dataSource;
    protected override PgSqlDataSource DataSource => _dataSource ??= CreateDataSource(csb => csb.Timezone = "UTC");

    protected DateTimeInfinityTests(bool disableDateTimeInfinityConversions)
    {
#if DEBUG
        DisableDateTimeInfinityConversions = disableDateTimeInfinityConversions;
#else
        if (disableDateTimeInfinityConversions)
        {
            Assert.Skip(
                "DateTimeInfinityTests rely on the PgSql.DisableDateTimeInfinityConversions AppContext switch and can only be run in DEBUG builds");
        }
#endif
    }

    public void Dispose()
    {
#if DEBUG
        DisableDateTimeInfinityConversions = false;
#endif
        DataSource.Dispose();
    }
}

#if DEBUG
[Collection(NonParallelCollection.Name)]
#endif
public sealed class DateTimeInfinityTests_InfinityConversionsEnabled() : DateTimeInfinityTests(false);
#if DEBUG
[Collection(NonParallelCollection.Name)]
public sealed class DateTimeInfinityTests_InfinityConversionsDisabled() : DateTimeInfinityTests(true);
#endif
