using System;
using System.Data;
using System.Threading.Tasks;
using CodeBrix.PostgresClient.Internal.ResolverFactories;
using CodeBrix.PostgresClient.PgSqlTypes;
using CodeBrix.PostgresClient.Tests.Support;
using SilverAssertions;
using Xunit;
using static CodeBrix.PostgresClient.Util.Statics;

namespace CodeBrix.PostgresClient.Tests.Types; //was previously: Npgsql.Tests.Types;

// Since this test suite manipulates TimeZone, it is incompatible with multiplexing
[Collection(NonParallelCollection.Name)]
public class LegacyDateTimeTests : TestBase, IClassFixture<LegacyDateTimeTestsFixture>
{
    [Theory]
    [InlineData(DateTimeKind.Unspecified)]
    [InlineData(DateTimeKind.Utc)]
    [InlineData(DateTimeKind.Local)]
    public Task timestamp_with_all_DateTime_kinds(DateTimeKind kind)
        => AssertType(
            new DateTime(1998, 4, 12, 13, 26, 38, 789, kind),
            "1998-04-12 13:26:38.789",
            "timestamp without time zone",
            PgSqlDbType.Timestamp,
            DbType.DateTime);

    [Fact]
    public async Task timestamp_read_as_Unspecified_DateTime()
    {
        //Arrange
        await using var command = DataSource.CreateCommand("SELECT '2020-03-01T10:30:00'::timestamp");

        //Act
        var dateTime = (DateTime)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));

        //Assert
        dateTime.Kind.Should().Be(DateTimeKind.Unspecified);
    }

    [Fact]
    public async Task timestamptz_negative_infinity()
    {
        //Act
        var dto = await AssertType(DateTimeOffset.MinValue, "-infinity", "timestamp with time zone", PgSqlDbType.TimestampTz,
            DbType.DateTimeOffset, isDefaultForReading: false);

        //Assert
        dto.Offset.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public async Task timestamptz_infinity()
    {
        //Act
        var dto = await AssertType(
            DateTimeOffset.MaxValue, "infinity", "timestamp with time zone", PgSqlDbType.TimestampTz, DbType.DateTimeOffset,
            isDefaultForReading: false);

        //Assert
        dto.Offset.Should().Be(TimeSpan.Zero);
    }

    [Theory]
    [InlineData(DateTimeKind.Utc)]
    [InlineData(DateTimeKind.Unspecified)]
    public Task timestamptz_write_utc_DateTime_does_not_convert(DateTimeKind kind)
        => AssertTypeWrite(
            new DateTime(1998, 4, 12, 13, 26, 38, 789, kind),
            "1998-04-12 15:26:38.789+02",
            "timestamp with time zone",
            PgSqlDbType.TimestampTz,
            DbType.DateTimeOffset,
            isDefault: false);

    [Fact]
    public Task timestamptz_local_DateTime_converts()
    {
        //Arrange
        // In legacy mode, we convert local DateTime to UTC when writing, and convert to local when reading,
        // using the machine time zone.
        var dateTime = new DateTime(1998, 4, 12, 13, 26, 38, 789, DateTimeKind.Utc).ToLocalTime();

        //Assert
        return AssertType(
            dateTime,
            "1998-04-12 15:26:38.789+02",
            "timestamp with time zone",
            PgSqlDbType.TimestampTz,
            DbType.DateTimeOffset,
            isDefaultForWriting: false);
    }

    protected override PgSqlDataSource DataSource { get; }

    public LegacyDateTimeTests(LegacyDateTimeTestsFixture fixture)
    {
#if DEBUG
        DataSource = fixture.DataSource;
#else
        Assert.Skip(
            "Legacy DateTime tests rely on the PgSql.EnableLegacyTimestampBehavior AppContext switch and can only be run in DEBUG builds");
#endif
    }
}

/// <summary>
/// The once-per-test-class setup of <see cref="LegacyDateTimeTests"/>: turns on the legacy timestamp behavior and builds the
/// data source the tests use, and turns the behavior back off when the class is done.
/// </summary>
public sealed class LegacyDateTimeTestsFixture : IDisposable
{
    internal PgSqlDataSource DataSource { get; }

    public LegacyDateTimeTestsFixture()
    {
#if DEBUG
        LegacyTimestampBehavior = true;
        var builder = new PgSqlDataSourceBuilder(TestUtil.ConnectionString);
        // Can't use the static AdoTypeInfoResolver instance, it already captured the feature flag.
        builder.AddTypeInfoResolverFactory(new AdoTypeInfoResolverFactory());
        builder.ConnectionStringBuilder.Timezone = "Europe/Berlin";
        DataSource = builder.Build();
        PgSqlDataSourceBuilder.ResetGlobalMappings(overwrite: true);
#endif
    }

    public void Dispose()
    {
#if DEBUG
        LegacyTimestampBehavior = false;
        DataSource.Dispose();
        PgSqlDataSourceBuilder.ResetGlobalMappings(overwrite: true);
#endif
    }
}
