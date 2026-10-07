using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CodeBrix.PostgresClient.NameTranslation;
using CodeBrix.PostgresClient.PgSqlTypes;
using CodeBrix.PostgresClient.PostgresTypes;
using CodeBrix.PostgresClient.Properties;
using SilverAssertions;
using Xunit;
using static CodeBrix.PostgresClient.Tests.TestUtil;

namespace CodeBrix.PostgresClient.Tests.Types; //was previously: Npgsql.Tests.Types;

public abstract class EnumTests(MultiplexingMode multiplexingMode) : MultiplexingTestBase(multiplexingMode)
{
    enum Mood { Sad, Ok, Happy }
    enum AnotherEnum { Value1, Value2 }

    [Fact]
    public async Task data_source_mapping()
    {
        //Arrange
        await using var adminConnection = await OpenConnectionAsync();
        var type = await GetTempTypeName(adminConnection);
        await adminConnection.ExecuteNonQueryAsync($"CREATE TYPE {type} AS ENUM ('sad', 'ok', 'happy')", cancellationToken: TestContext.Current.CancellationToken);

        var dataSourceBuilder = CreateDataSourceBuilder();
        dataSourceBuilder.MapEnum<Mood>(type);
        //Act
        await using var dataSource = dataSourceBuilder.Build();

        //Assert
        await AssertType(dataSource, Mood.Happy, "happy", type, pgSqlDbType: null);
    }

    [Fact]
    public async Task data_source_unmap()
    {
        //Arrange
        await using var adminConnection = await OpenConnectionAsync();
        var type = await GetTempTypeName(adminConnection);
        await adminConnection.ExecuteNonQueryAsync($"CREATE TYPE {type} AS ENUM ('sad', 'ok', 'happy')", cancellationToken: TestContext.Current.CancellationToken);

        var dataSourceBuilder = CreateDataSourceBuilder();
        dataSourceBuilder.MapEnum<Mood>(type);

        //Act
        var isUnmapSuccessful = dataSourceBuilder.UnmapEnum<Mood>(type);
        await using var dataSource = dataSourceBuilder.Build();

        //Assert
        isUnmapSuccessful.Should().BeTrue();
        await Assert.ThrowsAsync<InvalidCastException>(() => AssertType(dataSource, Mood.Happy, "happy", type, pgSqlDbType: null));
    }

    [Fact]
    public async Task data_source_mapping_non_generic()
    {
        //Arrange
        await using var adminConnection = await OpenConnectionAsync();
        var type = await GetTempTypeName(adminConnection);
        await adminConnection.ExecuteNonQueryAsync($"CREATE TYPE {type} AS ENUM ('sad', 'ok', 'happy')", cancellationToken: TestContext.Current.CancellationToken);

        var dataSourceBuilder = CreateDataSourceBuilder();
        dataSourceBuilder.MapEnum(typeof(Mood), type);
        //Act
        await using var dataSource = dataSourceBuilder.Build();
        //Assert
        await AssertType(dataSource, Mood.Happy, "happy", type, pgSqlDbType: null);
    }

    [Fact]
    public async Task data_source_unmap_non_generic()
    {
        //Arrange
        await using var adminConnection = await OpenConnectionAsync();
        var type = await GetTempTypeName(adminConnection);
        await adminConnection.ExecuteNonQueryAsync($"CREATE TYPE {type} AS ENUM ('sad', 'ok', 'happy')", cancellationToken: TestContext.Current.CancellationToken);

        var dataSourceBuilder = CreateDataSourceBuilder();
        dataSourceBuilder.MapEnum(typeof(Mood), type);

        //Act
        var isUnmapSuccessful = dataSourceBuilder.UnmapEnum(typeof(Mood), type);
        await using var dataSource = dataSourceBuilder.Build();

        //Assert
        isUnmapSuccessful.Should().BeTrue();
        await Assert.ThrowsAsync<InvalidCastException>(() => AssertType(dataSource, Mood.Happy, "happy", type, pgSqlDbType: null));
    }

    [Fact]
    public async Task dual_enums()
    {
        //Arrange
        await using var adminConnection = await OpenConnectionAsync();
        var type1 = await GetTempTypeName(adminConnection);
        var type2 = await GetTempTypeName(adminConnection);
        await adminConnection.ExecuteNonQueryAsync($@"
CREATE TYPE {type1} AS ENUM ('sad', 'ok', 'happy');
CREATE TYPE {type2} AS ENUM ('label1', 'label2', 'label3')", cancellationToken: TestContext.Current.CancellationToken);

        var dataSourceBuilder = CreateDataSourceBuilder();
        dataSourceBuilder.MapEnum<Mood>(type1);
        dataSourceBuilder.MapEnum<TestEnum>(type2);
        //Act
        await using var dataSource = dataSourceBuilder.Build();

        //Assert
        await AssertType(dataSource, new[] { Mood.Ok, Mood.Sad }, "{ok,sad}", type1 + "[]", pgSqlDbType: null);
    }

    [Fact]
    public async Task array()
    {
        //Arrange
        await using var adminConnection = await OpenConnectionAsync();
        var type = await GetTempTypeName(adminConnection);
        await adminConnection.ExecuteNonQueryAsync($"CREATE TYPE {type} AS ENUM ('sad', 'ok', 'happy')", cancellationToken: TestContext.Current.CancellationToken);

        var dataSourceBuilder = CreateDataSourceBuilder();
        dataSourceBuilder.MapEnum<Mood>(type);
        //Act
        await using var dataSource = dataSourceBuilder.Build();

        //Assert
        await AssertType(dataSource, new[] { Mood.Ok, Mood.Happy }, "{ok,happy}", type + "[]", pgSqlDbType: null);
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/859")]
    public async Task name_translation_default_snake_case()
    {
        //Arrange
        await using var adminConnection = await OpenConnectionAsync();
        var enumName1 = await GetTempTypeName(adminConnection);
        await adminConnection.ExecuteNonQueryAsync($"CREATE TYPE {enumName1} AS ENUM ('simple', 'two_words', 'some_database_name')", cancellationToken: TestContext.Current.CancellationToken);

        var dataSourceBuilder = CreateDataSourceBuilder();
        dataSourceBuilder.MapEnum<NameTranslationEnum>(enumName1);
        //Act
        await using var dataSource = dataSourceBuilder.Build();

        //Assert
        await AssertType(dataSource, NameTranslationEnum.Simple, "simple", enumName1, pgSqlDbType: null);
        await AssertType(dataSource, NameTranslationEnum.TwoWords, "two_words", enumName1, pgSqlDbType: null);
        await AssertType(dataSource, NameTranslationEnum.SomeClrName, "some_database_name", enumName1, pgSqlDbType: null);
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/859")]
    public async Task name_translation_null()
    {
        //Arrange
        await using var adminConnection = await OpenConnectionAsync();
        var type = await GetTempTypeName(adminConnection);
        await adminConnection.ExecuteNonQueryAsync($"CREATE TYPE {type} AS ENUM ('Simple', 'TwoWords', 'some_database_name')", cancellationToken: TestContext.Current.CancellationToken);

        var dataSourceBuilder = CreateDataSourceBuilder();
        dataSourceBuilder.MapEnum<NameTranslationEnum>(type, nameTranslator: new PgSqlNullNameTranslator());
        //Act
        await using var dataSource = dataSourceBuilder.Build();

        //Assert
        await AssertType(dataSource, NameTranslationEnum.Simple, "Simple", type, pgSqlDbType: null);
        await AssertType(dataSource, NameTranslationEnum.TwoWords, "TwoWords", type, pgSqlDbType: null);
        await AssertType(dataSource, NameTranslationEnum.SomeClrName, "some_database_name", type, pgSqlDbType: null);
    }

    [Fact]
    public async Task unmapped_enum_as_clr_enum()
    {
        //Arrange
        await using var dataSource = CreateDataSource(b => b.EnableUnmappedTypes());
        await using var connection = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        var type1 = await GetTempTypeName(connection);
        var type2 = await GetTempTypeName(connection);
        await connection.ExecuteNonQueryAsync(@$"
CREATE TYPE {type1} AS ENUM ('sad', 'ok', 'happy');
CREATE TYPE {type2} AS ENUM ('value1', 'value2');", cancellationToken: TestContext.Current.CancellationToken);
        //Act
        await connection.ReloadTypesAsync(TestContext.Current.CancellationToken);

        //Assert
        await AssertType(connection, Mood.Happy, "happy", type1, pgSqlDbType: null, isDefault: false);
        await AssertType(connection, AnotherEnum.Value2, "value2", type2, pgSqlDbType: null, isDefault: false);
    }

    [Fact]
    public async Task unmapped_enum_as_clr_enum_supported_only_with_EnableUnmappedTypes()
    {
        await using var connection = await DataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        var enumType = await GetTempTypeName(connection);
        await connection.ExecuteNonQueryAsync($"CREATE TYPE {enumType} AS ENUM ('sad', 'ok', 'happy')", cancellationToken: TestContext.Current.CancellationToken);
        await connection.ReloadTypesAsync(TestContext.Current.CancellationToken);

        var errorMessage = string.Format(
            PgSqlStrings.UnmappedEnumsNotEnabled,
            nameof(PgSqlSlimDataSourceBuilder.EnableUnmappedTypes),
            nameof(PgSqlDataSourceBuilder));

        var exception = await AssertTypeUnsupportedWrite(Mood.Happy, enumType);
        exception.InnerException.Should().BeAssignableTo<NotSupportedException>();
        exception.InnerException.Message.Should().Be(errorMessage);

        exception = await AssertTypeUnsupportedRead<Mood>("happy", enumType);
        exception.InnerException.Should().BeAssignableTo<NotSupportedException>();
        exception.InnerException.Message.Should().Be(errorMessage);
    }

    [Fact]
    public async Task unmapped_enum_as_string()
    {
        //Arrange
        await using var connection = await OpenConnectionAsync();
        var type = await GetTempTypeName(connection);
        await connection.ExecuteNonQueryAsync($"CREATE TYPE {type} AS ENUM ('sad', 'ok', 'happy')", cancellationToken: TestContext.Current.CancellationToken);
        //Act
        await connection.ReloadTypesAsync(TestContext.Current.CancellationToken);

        //Assert
        await AssertType(connection, "happy", "happy", type, pgSqlDbType: null, isDefaultForWriting: false);
    }

    enum NameTranslationEnum
    {
        Simple,
        TwoWords,
        [PgName("some_database_name")]
        SomeClrName
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/632")]
    public async Task same_name_in_different_schemas()
    {
        //Arrange
        await using var adminConnection = await OpenConnectionAsync();
        var schema1 = await CreateTempSchema(adminConnection);
        var schema2 = await CreateTempSchema(adminConnection);
        await adminConnection.ExecuteNonQueryAsync($@"
CREATE TYPE {schema1}.my_enum AS ENUM ('one');
CREATE TYPE {schema2}.my_enum AS ENUM ('alpha');", cancellationToken: TestContext.Current.CancellationToken);

        var dataSourceBuilder = CreateDataSourceBuilder();
        dataSourceBuilder.MapEnum<Enum1>($"{schema1}.my_enum");
        dataSourceBuilder.MapEnum<Enum2>($"{schema2}.my_enum");
        //Act
        await using var dataSource = dataSourceBuilder.Build();

        //Assert
        await AssertType(dataSource, Enum1.One, "one", $"{schema1}.my_enum", pgSqlDbType: null);
        await AssertType(dataSource, Enum2.Alpha, "alpha", $"{schema2}.my_enum", pgSqlDbType: null);
    }

    enum Enum1 { One }
    enum Enum2 { Alpha }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/1779")]
    public async Task GetPostgresType()
    {
        //Arrange
        await using var dataSource = CreateDataSource();
        using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        var type = await GetTempTypeName(conn);
        await conn.ExecuteNonQueryAsync($"CREATE TYPE {type} AS ENUM ('sad', 'ok', 'happy')", cancellationToken: TestContext.Current.CancellationToken);
        conn.ReloadTypes();

        using var cmd = new PgSqlCommand($"SELECT 'ok'::{type}", conn);
        //Act
        using var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        reader.Read();
        var enumType = (PostgresEnumType)reader.GetPostgresType(0);
        //Assert
        enumType.Name.Should().Be(type);
        enumType.Labels.Should().Equal(new List<string> { "sad", "ok", "happy" });
    }

    enum TestEnum
    {
        label1,
        label2,
        [PgName("label3")]
        Label3
    }
}

public sealed class EnumTests_NonMultiplexing() : EnumTests(MultiplexingMode.NonMultiplexing);
public sealed class EnumTests_Multiplexing() : EnumTests(MultiplexingMode.Multiplexing);
