using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using CodeBrix.PostgresClient.PgSqlTypes;
using SilverAssertions;
using Xunit;

namespace CodeBrix.PostgresClient.Tests.Types; //was previously: Npgsql.Tests.Types;

public abstract class JsonTests : MultiplexingTestBase
{
    [Fact]
    public async Task as_string()
        => await AssertType("""{"K": "V"}""", """{"K": "V"}""", PostgresType, PgSqlDbType, isDefaultForWriting: false);

    [Fact]
    public async Task as_string_long()
    {
        //Arrange
        await using var conn = CreateConnection();

        var value = new StringBuilder()
            .Append(@"{""K"": """)
            .Append('x', conn.Settings.WriteBufferSize)
            .Append(@"""}")
            .ToString();

        //Assert
        await AssertType(value, value, PostgresType, PgSqlDbType, isDefaultForWriting: false);
    }

    [Fact]
    public async Task as_string_with_GetTextReader()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        await using var cmd = new PgSqlCommand($$"""SELECT '{"K": "V"}'::{{PostgresType}}""", conn);

        //Act
        await using var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        reader.Read();
        using var textReader = await reader.GetTextReaderAsync(0, TestContext.Current.CancellationToken);

        //Assert
        (await textReader.ReadToEndAsync(TestContext.Current.CancellationToken)).Should().Be(@"{""K"": ""V""}");
    }

    [Fact]
    public async Task as_char_array()
        => await AssertType("""{"K": "V"}""".ToCharArray(), """{"K": "V"}""", PostgresType, PgSqlDbType, isDefault: false);

    [Fact]
    public async Task as_bytes()
        => await AssertType("""{"K": "V"}"""u8.ToArray(), """{"K": "V"}""", PostgresType, PgSqlDbType, isDefault: false);

    [Fact]
    public async Task write_as_ReadOnlyMemory_of_byte()
        => await AssertTypeWrite(new ReadOnlyMemory<byte>("""{"K": "V"}"""u8.ToArray()), """{"K": "V"}""", PostgresType, PgSqlDbType,
            isDefault: false);

    [Fact]
    public async Task write_as_ArraySegment_of_char()
        => await AssertTypeWrite(new ArraySegment<char>("""{"K": "V"}""".ToCharArray()), """{"K": "V"}""", PostgresType, PgSqlDbType,
            isDefault: false);

    [Fact]
    public Task as_MemoryStream()
        => AssertTypeWrite(() => new MemoryStream("""{"K": "V"}"""u8.ToArray()), """{"K": "V"}""", PostgresType, PgSqlDbType, isDefault: false);

    [Fact]
    public async Task as_JsonDocument()
        => await AssertType(
            JsonDocument.Parse("""{"K": "V"}"""),
            IsJsonb ? """{"K": "V"}""" : """{"K":"V"}""",
            PostgresType,
            PgSqlDbType,
            isDefault: false,
            comparer: (x, y) => x.RootElement.GetProperty("K").GetString() == y.RootElement.GetProperty("K").GetString());

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/5540")]
    public async Task as_JsonDocument_with_null_root()
        => await AssertType(
            JsonDocument.Parse("null"),
            "null",
            PostgresType,
            PgSqlDbType,
            isDefault: false,
            comparer: (x, y) => x.RootElement.ValueKind == y.RootElement.ValueKind,
            skipArrayCheck: true);

    [Fact]
    public async Task as_JsonElement_with_null_root()
        => await AssertType(
            JsonDocument.Parse("null").RootElement,
            "null",
            PostgresType,
            PgSqlDbType,
            isDefault: false,
            comparer: (x, y) => x.ValueKind == y.ValueKind,
            skipArrayCheck: true);

    [Fact]
    public async Task as_JsonDocument_supported_only_with_SystemTextJson()
    {
        //Arrange
        await using var slimDataSource = new PgSqlSlimDataSourceBuilder(ConnectionString).Build();

        //Assert
        await AssertTypeUnsupported(
            JsonDocument.Parse("""{"K": "V"}"""),
            """{"K": "V"}""",
            PostgresType,
            slimDataSource);
    }

    [Fact]
    public Task roundtrip_string()
        => AssertType(
            @"{""p"": 1}",
            @"{""p"": 1}",
            PostgresType,
            PgSqlDbType,
            isDefault: false,
            isPgSqlDbTypeInferredFromClrType: false);

    [Fact]
    public Task roundtrip_char_array()
        => AssertType(
            @"{""p"": 1}".ToCharArray(),
            @"{""p"": 1}",
            PostgresType,
            PgSqlDbType,
            isDefault: false,
            isPgSqlDbTypeInferredFromClrType: false);

    [Fact]
    public Task roundtrip_byte_array()
        => AssertType(
            Encoding.ASCII.GetBytes(@"{""p"": 1}"),
            @"{""p"": 1}",
            PostgresType,
            PgSqlDbType,
            isDefault: false,
            isPgSqlDbTypeInferredFromClrType: false);

    [Fact]
    [IssueLink("https://github.com/npgsql/npgsql/issues/2811")]
    [IssueLink("https://github.com/npgsql/efcore.pg/issues/1177")]
    [IssueLink("https://github.com/npgsql/efcore.pg/issues/1082")]
    public async Task can_read_two_json_documents()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();

        //Act
        JsonDocument car;
        await using (var cmd = new PgSqlCommand("""SELECT '{"key" : "foo"}'::jsonb""", conn))
        await using (var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken))
        {
            reader.Read();
            car = reader.GetFieldValue<JsonDocument>(0);
        }

        await using (var cmd = new PgSqlCommand("""SELECT '{"key" : "bar"}'::jsonb""", conn))
        await using (var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken))
        {
            reader.Read();
            reader.GetFieldValue<JsonDocument>(0);
        }

        //Assert
        car.RootElement.GetProperty("key").GetString().Should().Be("foo");
    }

    [Fact]
    public Task roundtrip_JsonObject()
        => AssertType(
            new JsonObject { ["Bar"] = 8 },
            IsJsonb ? """{"Bar": 8}""" : """{"Bar":8}""",
            PostgresType,
            PgSqlDbType,
            // By default we map JsonObject to jsonb
            isDefaultForWriting: IsJsonb,
            isDefaultForReading: false,
            isPgSqlDbTypeInferredFromClrType: false,
            comparer: (x, y) => x.ToString() == y.ToString());

    [Fact]
    public Task roundtrip_JsonArray()
        => AssertType(
            new JsonArray { 1, 2, 3 },
            IsJsonb ? "[1, 2, 3]" : "[1,2,3]",
            PostgresType,
            PgSqlDbType,
            // By default we map JsonArray to jsonb
            isDefaultForWriting: IsJsonb,
            isDefaultForReading: false,
            isPgSqlDbTypeInferredFromClrType: false,
            comparer: (x, y) => x.ToString() == y.ToString());

    [Fact]
    [IssueLink("https://github.com/npgsql/npgsql/issues/4537")]
    public async Task write_jsonobject_array_without_pgsqldbtype()
    {
        //Arrange
        // By default we map JsonObject to jsonb
        if (!IsJsonb)
            return;

        await using var conn = await OpenConnectionAsync();
        var tableName = await TestUtil.CreateTempTable(conn, "key SERIAL PRIMARY KEY, ingredients json[]");

        await using var cmd = new PgSqlCommand { Connection = conn };

        var jsonObject1 = new JsonObject
        {
            { "name", "value1" },
            { "amount", 1 },
            { "unit", "ml" }
        };

        var jsonObject2 = new JsonObject
        {
            { "name", "value2" },
            { "amount", 2 },
            { "unit", "g" }
        };

        cmd.CommandText = $"INSERT INTO {tableName} (ingredients) VALUES (@p)";
        cmd.Parameters.Add(new("p", new[] { jsonObject1, jsonObject2 }));

        //Act
        await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    protected JsonTests(MultiplexingMode multiplexingMode, PgSqlDbType pgSqlDbType)
        : base(multiplexingMode)
    {
        if (pgSqlDbType == PgSqlDbType.Jsonb)
            using (var conn = OpenConnection())
                TestUtil.MinimumPgVersion(conn, "9.4.0", "JSONB data type not yet introduced");

        PgSqlDbType = pgSqlDbType;
    }

    bool IsJsonb => PgSqlDbType == PgSqlDbType.Jsonb;
    string PostgresType => IsJsonb ? "jsonb" : "json";
    readonly PgSqlDbType PgSqlDbType;
}

public sealed class JsonTests_NonMultiplexing_Json() : JsonTests(MultiplexingMode.NonMultiplexing, PgSqlDbType.Json);
public sealed class JsonTests_NonMultiplexing_Jsonb() : JsonTests(MultiplexingMode.NonMultiplexing, PgSqlDbType.Jsonb);
public sealed class JsonTests_Multiplexing_Json() : JsonTests(MultiplexingMode.Multiplexing, PgSqlDbType.Json);
public sealed class JsonTests_Multiplexing_Jsonb() : JsonTests(MultiplexingMode.Multiplexing, PgSqlDbType.Jsonb);
