using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using CodeBrix.PostgresClient.PgSqlTypes;
using CodeBrix.PostgresClient.Properties;
using SilverAssertions;
using Xunit;

namespace CodeBrix.PostgresClient.Tests.Types; //was previously: Npgsql.Tests.Types;

public abstract class JsonDynamicTests : MultiplexingTestBase, IClassFixture<JsonDynamicTestsFixture>
{
    [Fact]
    public async Task as_poco()
        => await AssertType(
            new WeatherForecast
            {
                Date = new DateTime(2019, 9, 1),
                Summary = "Partly cloudy",
                TemperatureC = 10
            },
            IsJsonb
                ? """{"Date": "2019-09-01T00:00:00", "Summary": "Partly cloudy", "TemperatureC": 10}"""
                : """{"Date":"2019-09-01T00:00:00","TemperatureC":10,"Summary":"Partly cloudy"}""",
            PostgresType,
            PgSqlDbType,
            isDefault: false);

    [Fact]
    public async Task as_poco_long()
    {
        //Arrange
        using var conn = CreateConnection();
        var bigString = new string('x', Math.Max(conn.Settings.ReadBufferSize, conn.Settings.WriteBufferSize));

        //Assert
        await AssertType(
            new WeatherForecast
            {
                Date = new DateTime(2019, 9, 1),
                Summary = bigString,
                TemperatureC = 10
            },
            // Warning: in theory jsonb order and whitespace may change across versions
            IsJsonb
                ? $$"""{"Date": "2019-09-01T00:00:00", "Summary": "{{bigString}}", "TemperatureC": 10}"""
                : $$"""{"Date":"2019-09-01T00:00:00","TemperatureC":10,"Summary":"{{bigString}}"}""",
            PostgresType,
            PgSqlDbType,
            isDefault: false);
    }

    [Fact]
    public async Task as_poco_supported_only_with_EnableDynamicJson()
    {
        //Arrange
        // This test uses base.DataSource, which doesn't have EnableDynamicJson()

        var errorMessage = string.Format(
            PgSqlStrings.DynamicJsonNotEnabled,
            nameof(WeatherForecast),
            nameof(PgSqlSlimDataSourceBuilder.EnableDynamicJson),
            nameof(PgSqlDataSourceBuilder));

        //Act
        var exception = await AssertTypeUnsupportedWrite(
                new WeatherForecast
                {
                    Date = new DateTime(2019, 9, 1),
                    Summary = "Partly cloudy",
                    TemperatureC = 10
                },
                PostgresType,
                base.DataSource);

        //Assert
        exception.InnerException.Should().BeAssignableTo<NotSupportedException>();
        exception.InnerException.Message.Should().Be(errorMessage);

        exception = await AssertTypeUnsupportedRead<WeatherForecast>(
            IsJsonb
                ? """{"Date": "2019-09-01T00:00:00", "Summary": "Partly cloudy", "TemperatureC": 10}"""
                : """{"Date":"2019-09-01T00:00:00","TemperatureC":10,"Summary":"Partly cloudy"}""",
            PostgresType,
            base.DataSource);

        exception.InnerException.Should().BeAssignableTo<NotSupportedException>();
        exception.InnerException.Message.Should().Be(errorMessage);
    }

    [Fact]
    public async Task poco_does_not_stomp_GetValue_string()
    {
        //Arrange
        var dataSource = CreateDataSourceBuilder()
            .EnableDynamicJson([typeof(WeatherForecast)], [typeof(WeatherForecast)])
            .Build();
        var sqlLiteral =
            IsJsonb
                ? """{"Date": "2019-09-01T00:00:00", "Summary": "Partly cloudy", "TemperatureC": 10}"""
                : """{"Date":"2019-09-01T00:00:00","TemperatureC":10,"Summary":"Partly cloudy"}""";
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using var cmd = new PgSqlCommand($"SELECT '{sqlLiteral}'::{(IsJsonb ? "jsonb" : "json")}", conn);

        //Act
        await using var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        await reader.ReadAsync(TestContext.Current.CancellationToken);

        //Assert
        reader.GetValue(0).Should().BeOfType<string>();
    }

    [Fact]
    public async Task custom_JsonSerializerOptions()
    {
        //Arrange
        await using var dataSource = CreateDataSourceBuilder()
            .ConfigureJsonOptions(new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })
            .EnableDynamicJson()
            .Build();

        //Assert
        await AssertTypeWrite(
            dataSource,
            new WeatherForecast
            {
                Date = new DateTime(2019, 9, 1),
                Summary = "Partly cloudy",
                TemperatureC = 10
            },
            IsJsonb
                ? """{"date": "2019-09-01T00:00:00", "summary": "Partly cloudy", "temperatureC": 10}"""
                : """{"date":"2019-09-01T00:00:00","temperatureC":10,"summary":"Partly cloudy"}""",
            PostgresType,
            PgSqlDbType,
            isDefault: false);
    }

    [Fact(Skip = "TODO We should not change the default type for json/jsonb, it makes little sense.")]
    public async Task poco_default_mapping()
    {
        //Arrange
        var dataSourceBuilder = CreateDataSourceBuilder();
        if (IsJsonb)
            dataSourceBuilder.EnableDynamicJson(jsonbClrTypes: [typeof(WeatherForecast)]);
        else
            dataSourceBuilder.EnableDynamicJson(jsonClrTypes: [typeof(WeatherForecast)]);
        await using var dataSource = dataSourceBuilder.Build();

        //Assert
        await AssertType(
            dataSource,
            new WeatherForecast
            {
                Date = new DateTime(2019, 9, 1),
                Summary = "Partly cloudy",
                TemperatureC = 10
            },
            IsJsonb
                ? """{"Date": "2019-09-01T00:00:00", "Summary": "Partly cloudy", "TemperatureC": 10}"""
                : """{"Date":"2019-09-01T00:00:00","TemperatureC":10,"Summary":"Partly cloudy"}""",
            PostgresType,
            PgSqlDbType,
            isDefaultForReading: false,
            isPgSqlDbTypeInferredFromClrType: false);
    }

    #region Polymorphic

    [Fact]
    public async Task poco_polymorphic_mapping()
    {
        //Arrange
        await using var dataSource = CreateDataSource(builder =>
        {
            var types = new[] {typeof(WeatherForecast)};
            builder
                .ConfigureJsonOptions(new() { AllowOutOfOrderMetadataProperties = true })
                .EnableDynamicJson(jsonClrTypes: IsJsonb ? [] : types, jsonbClrTypes: !IsJsonb ? [] : types);
        });

        var value = new ExtendedDerivedWeatherForecast
        {
            Date = new DateTime(2019, 9, 1),
            Summary = "Partly cloudy",
            TemperatureC = 10
        };

        // Note: we assert a specific string representation, though jsonb doesn't guarantee the property ordering; so the assert may break
        // for jsonb if PostgreSQL changes its implementation.
        var sql =
            IsJsonb
                ? """{"Date": "2019-09-01T00:00:00", "$type": "extended", "Summary": "Partly cloudy", "TemperatureC": 10, "TemperatureF": 49}"""
                : """{"$type":"extended","TemperatureF":49,"Date":"2019-09-01T00:00:00","TemperatureC":10,"Summary":"Partly cloudy"}""";

        //Assert
        await AssertTypeWrite(dataSource, value, sql, PostgresType, PgSqlDbType, isPgSqlDbTypeInferredFromClrType: false);
        await AssertTypeRead<WeatherForecast>(dataSource, sql, PostgresType, value, isDefault: false);
    }

    [Fact]
    public async Task poco_polymorphic_mapping_read_parents()
    {
        //Arrange
        await using var dataSource = CreateDataSource(builder =>
        {
            var types = new[] {typeof(WeatherForecast)};
            builder
                .ConfigureJsonOptions(new() { AllowOutOfOrderMetadataProperties = true })
                .EnableDynamicJson(jsonClrTypes: IsJsonb ? [] : types, jsonbClrTypes: !IsJsonb ? [] : types);
        });

        var value = new ExtendedDerivedWeatherForecast
        {
            Date = new DateTime(2019, 9, 1),
            Summary = "Partly cloudy",
            TemperatureC = 10
        };

        // Note: we assert a specific string representation, though jsonb doesn't guarantee the property ordering; so the assert may break
        // for jsonb if PostgreSQL changes its implementation.
        var sql =
            IsJsonb
                ? """{"Date": "2019-09-01T00:00:00", "$type": "extended", "Summary": "Partly cloudy", "TemperatureC": 10, "TemperatureF": 49}"""
                : """{"$type":"extended","TemperatureF":49,"Date":"2019-09-01T00:00:00","TemperatureC":10,"Summary":"Partly cloudy"}""";

        //Assert
        await AssertTypeWrite<WeatherForecast>(dataSource, value, sql, PostgresType, PgSqlDbType,
            isPgSqlDbTypeInferredFromClrType: false);

        await AssertTypeRead<WeatherForecast>(dataSource, sql, PostgresType, value, isDefault: false);
        await AssertTypeRead(dataSource, sql, PostgresType,
            new DerivedWeatherForecast
            {
                Date = new DateTime(2019, 9, 1),
                Summary = "Partly cloudy",
                TemperatureC = 10
            },
            isDefault: false);
        await AssertTypeRead(dataSource, sql, PostgresType, value, isDefault: false);
    }

    [Fact]
    public async Task poco_exact_polymorphic_mapping()
    {
        //Arrange
        await using var dataSource = CreateDataSource(builder =>
        {
            var types = new[] {typeof(ExtendedDerivedWeatherForecast)};
            builder
                .ConfigureJsonOptions(new() { AllowOutOfOrderMetadataProperties = true })
                .EnableDynamicJson(jsonClrTypes: IsJsonb ? [] : types, jsonbClrTypes: !IsJsonb ? [] : types);
        });

        var value = new ExtendedDerivedWeatherForecast
        {
            Date = new DateTime(2019, 9, 1),
            Summary = "Partly cloudy",
            TemperatureC = 10
        };

        // Note: we assert a specific string representation, though jsonb doesn't guarantee the property ordering; so the assert may break
        // for jsonb if PostgreSQL changes its implementation.
        var sql =
            IsJsonb
                ? """{"Date": "2019-09-01T00:00:00", "Summary": "Partly cloudy", "TemperatureC": 10, "TemperatureF": 49}"""
                : """{"TemperatureF":49,"Date":"2019-09-01T00:00:00","TemperatureC":10,"Summary":"Partly cloudy"}""";

        //Assert
        await AssertTypeWrite(dataSource, value, sql, PostgresType, PgSqlDbType, isPgSqlDbTypeInferredFromClrType: false);
        await AssertTypeRead(dataSource, sql, PostgresType, value, isDefault: false);
    }

    [Fact]
    public async Task poco_unspecified_polymorphic_mapping()
    {
        //Arrange

        await using var dataSource = CreateDataSource(builder =>
        {
            builder
                .ConfigureJsonOptions(new() { AllowOutOfOrderMetadataProperties = true })
                .EnableDynamicJson();
        });

        var value = new ExtendedDerivedWeatherForecast
        {
            Date = new DateTime(2019, 9, 1),
            Summary = "Partly cloudy",
            TemperatureC = 10
        };

        // Note: we assert a specific string representation, though jsonb doesn't guarantee the property ordering; so the assert may break
        // for jsonb if PostgreSQL changes its implementation.
        var sql =
            IsJsonb
                ? """{"Date": "2019-09-01T00:00:00", "$type": "extended", "Summary": "Partly cloudy", "TemperatureC": 10, "TemperatureF": 49}"""
                : """{"$type":"extended","TemperatureF":49,"Date":"2019-09-01T00:00:00","TemperatureC":10,"Summary":"Partly cloudy"}""";

        //Assert
        await AssertTypeWrite(dataSource, value, sql, PostgresType, PgSqlDbType, isDefault: false);

        // Reading as DerivedWeatherForecast should not cause us to get an instance of ExtendedDerivedWeatherForecast (as it doesn't define JsonDerivedType)
        await AssertTypeRead(dataSource, sql, PostgresType,
            new DerivedWeatherForecast
            {
                Date = new DateTime(2019, 9, 1),
                Summary = "Partly cloudy",
                TemperatureC = 10
            },
            isDefault: false);
        await AssertTypeRead<WeatherForecast>(dataSource, sql, PostgresType, value, isDefault: false);
    }

    [Fact]
    public async Task poco_polymorphic_mapping_without_AllowOutOfOrderMetadataProperties()
    {
        //Arrange
        await using var dataSource = CreateDataSource(builder =>
        {
            var types = new[] {typeof(WeatherForecast)};
            builder
                .ConfigureJsonOptions(new() { AllowOutOfOrderMetadataProperties = false })
                .EnableDynamicJson(jsonClrTypes: IsJsonb ? [] : types, jsonbClrTypes: !IsJsonb ? [] : types);
        });

        var value = new ExtendedDerivedWeatherForecast
        {
            Date = new DateTime(2019, 9, 1),
            Summary = "Partly cloudy",
            TemperatureC = 10
        };

        // Note: we assert a specific string representation, though jsonb doesn't guarantee the property ordering; so the assert may break
        // for jsonb if PostgreSQL changes its implementation.
        var sql =
            IsJsonb
                ? """{"Date": "2019-09-01T00:00:00", "Summary": "Partly cloudy", "TemperatureC": 10, "TemperatureF": 49}"""
                : """{"$type":"extended","TemperatureF":49,"Date":"2019-09-01T00:00:00","TemperatureC":10,"Summary":"Partly cloudy"}""";

        //Assert
        await AssertTypeWrite(dataSource, value, sql, PostgresType, PgSqlDbType, isPgSqlDbTypeInferredFromClrType: false);

        // As we have disabled polymorphism for jsonb when AllowOutOfOrderMetadataProperties = false we should be able to read it as equalt to a WeatherForecast instance.
        if (IsJsonb)
            await AssertTypeRead(dataSource, sql, PostgresType,
                new WeatherForecast
                {
                    Date = new DateTime(2019, 9, 1),
                    Summary = "Partly cloudy",
                    TemperatureC = 10
                },
                isDefault: false);

        // Reading as DerivedWeatherForecast should not cause us to get an instance of ExtendedDerivedWeatherForecast (as it doesn't define JsonDerivedType)
        await AssertTypeRead(dataSource, sql, PostgresType,
            new DerivedWeatherForecast
            {
                Date = new DateTime(2019, 9, 1),
                Summary = "Partly cloudy",
                TemperatureC = 10
            },
            isDefault: false);

        // We won't get the original value back for jsonb as we can't support polymorphism without also enforcing AllowOutOfOrderMetadataProperties is true.
        // If we output $type, jsonb won't have that at the start and STJ will throw due to it appearing later in the object. So it's disabled entirely.
        if (!IsJsonb)
            await AssertTypeRead<WeatherForecast>(dataSource, sql, PostgresType, value, isDefault: false);
    }

    [Fact]
    public async Task poco_unspecified_polymorphic_mapping_without_AllowOutOfOrderMetadataProperties()
    {
        //Arrange
        await using var dataSource = CreateDataSource(builder =>
        {
            builder
                .ConfigureJsonOptions(new() { AllowOutOfOrderMetadataProperties = false })
                .EnableDynamicJson();
        });

        var value = new ExtendedDerivedWeatherForecast
        {
            Date = new DateTime(2019, 9, 1),
            Summary = "Partly cloudy",
            TemperatureC = 10
        };

        // Note: we assert a specific string representation, though jsonb doesn't guarantee the property ordering; so the assert may break
        // for jsonb if PostgreSQL changes its implementation.
        var sql =
            IsJsonb
                ? """{"Date": "2019-09-01T00:00:00", "Summary": "Partly cloudy", "TemperatureC": 10, "TemperatureF": 49}"""
                : """{"$type":"extended","TemperatureF":49,"Date":"2019-09-01T00:00:00","TemperatureC":10,"Summary":"Partly cloudy"}""";

        //Assert
        await AssertTypeWrite(dataSource, value, sql, PostgresType, PgSqlDbType, isDefault: false);

        // As we have disabled polymorphism for jsonb when AllowOutOfOrderMetadataProperties = false we should be able to read it as equalt to a WeatherForecast instance.
        if (IsJsonb)
            await AssertTypeRead(dataSource, sql, PostgresType,
                new WeatherForecast
                {
                    Date = new DateTime(2019, 9, 1),
                    Summary = "Partly cloudy",
                    TemperatureC = 10
                },
                isDefault: false);

        // Reading as DerivedWeatherForecast should not cause us to get an instance of ExtendedDerivedWeatherForecast (as it doesn't define JsonDerivedType)
        await AssertTypeRead(dataSource, sql, PostgresType,
            new DerivedWeatherForecast
            {
                Date = new DateTime(2019, 9, 1),
                Summary = "Partly cloudy",
                TemperatureC = 10
            },
            isDefault: false);

        // We won't get the original value back for jsonb as we can't support polymorphism without also enforcing AllowOutOfOrderMetadataProperties is true.
        // If we output $type, jsonb won't have that at the start and STJ will throw due to it appearing later in the object. So it's disabled entirely.
        if (!IsJsonb)
            await AssertTypeRead<WeatherForecast>(dataSource, sql, PostgresType, value, isDefault: false);
    }

    // ReSharper disable UnusedAutoPropertyAccessor.Local
    // ReSharper disable UnusedMember.Local
    [JsonDerivedType(typeof(ExtendedDerivedWeatherForecast), typeDiscriminator: "extended")]
    record WeatherForecast
    {
        public DateTime Date { get; set; }
        public int TemperatureC { get; set; }
        public string Summary { get; set; } = "";
    }

    record DerivedWeatherForecast : WeatherForecast;

    record ExtendedDerivedWeatherForecast : DerivedWeatherForecast
    {
        public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
    }
    // ReSharper restore UnusedMember.Local
    // ReSharper restore UnusedAutoPropertyAccessor.Local

    #endregion Polymorphic

    protected JsonDynamicTests(MultiplexingMode multiplexingMode, PgSqlDbType pgSqlDbType, JsonDynamicTestsFixture fixture)
        : base(multiplexingMode)
    {
        DataSource = fixture.GetDataSource(ConnectionString);

        if (pgSqlDbType == PgSqlDbType.Jsonb)
            using (var conn = OpenConnection())
                TestUtil.MinimumPgVersion(conn, "9.4.0", "JSONB data type not yet introduced");

        PgSqlDbType = pgSqlDbType;
    }

    protected override PgSqlDataSource DataSource { get; }

    bool IsJsonb => PgSqlDbType == PgSqlDbType.Jsonb;
    string PostgresType => IsJsonb ? "jsonb" : "json";
    readonly PgSqlDbType PgSqlDbType;
}

public sealed class JsonDynamicTests_NonMultiplexing_Json(JsonDynamicTestsFixture fixture)
    : JsonDynamicTests(MultiplexingMode.NonMultiplexing, PgSqlDbType.Json, fixture);
public sealed class JsonDynamicTests_NonMultiplexing_Jsonb(JsonDynamicTestsFixture fixture)
    : JsonDynamicTests(MultiplexingMode.NonMultiplexing, PgSqlDbType.Jsonb, fixture);
public sealed class JsonDynamicTests_Multiplexing_Json(JsonDynamicTestsFixture fixture)
    : JsonDynamicTests(MultiplexingMode.Multiplexing, PgSqlDbType.Json, fixture);
public sealed class JsonDynamicTests_Multiplexing_Jsonb(JsonDynamicTestsFixture fixture)
    : JsonDynamicTests(MultiplexingMode.Multiplexing, PgSqlDbType.Jsonb, fixture);

/// <summary>
/// The once-per-test-class state of <see cref="JsonDynamicTests"/>: the data source (with dynamic JSON enabled) all the tests of
/// one concrete class share, built on first use from that class's connection string and disposed when the class is done.
/// </summary>
public sealed class JsonDynamicTestsFixture : IDisposable
{
    readonly object _lock = new();
    PgSqlDataSource _dataSource;

    internal PgSqlDataSource GetDataSource(string connectionString)
    {
        lock (_lock)
        {
            return _dataSource ??= new PgSqlDataSourceBuilder(connectionString).EnableDynamicJson().Build();
        }
    }

    public void Dispose() => _dataSource?.Dispose();
}
