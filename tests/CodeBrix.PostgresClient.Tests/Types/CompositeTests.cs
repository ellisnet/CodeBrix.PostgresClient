using System;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using CodeBrix.PostgresClient.PgSqlTypes;
using CodeBrix.PostgresClient.PostgresTypes;
using CodeBrix.PostgresClient.Tests.Support;
using SilverAssertions;
using Xunit;
using static CodeBrix.PostgresClient.Tests.TestUtil;

namespace CodeBrix.PostgresClient.Tests.Types; //was previously: Npgsql.Tests.Types;

public abstract class CompositeTests(MultiplexingMode multiplexingMode) : MultiplexingTestBase(multiplexingMode)
{
    [Fact]
    public async Task basic()
    {
        //Arrange
        await using var adminConnection = await OpenConnectionAsync();
        var type = await GetTempTypeName(adminConnection);

        await adminConnection.ExecuteNonQueryAsync($"CREATE TYPE {type} AS (x int, some_text text)", cancellationToken: TestContext.Current.CancellationToken);

        var dataSourceBuilder = CreateDataSourceBuilder();
        dataSourceBuilder.MapComposite<SomeComposite>(type);
        await using var dataSource = dataSourceBuilder.Build();
        await using var connection = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        //Assert
        await AssertType(
            connection,
            new SomeComposite { SomeText = "foo", X = 8 },
            "(8,foo)",
            type,
            pgSqlDbType: null);
    }

    [Fact]
    public async Task basic_with_custom_default_translator()
    {
        //Arrange
        await using var adminConnection = await OpenConnectionAsync();
        var type = await GetTempTypeName(adminConnection);

        await adminConnection.ExecuteNonQueryAsync($"CREATE TYPE {type} AS (x int, s text)", cancellationToken: TestContext.Current.CancellationToken);

        var dataSourceBuilder = CreateDataSourceBuilder();
        dataSourceBuilder.DefaultNameTranslator = new CustomTranslator();
        dataSourceBuilder.MapComposite<SomeComposite>(type);
        await using var dataSource = dataSourceBuilder.Build();
        await using var connection = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        //Assert
        await AssertType(
            connection,
            new SomeComposite { SomeText = "foo", X = 8 },
            "(8,foo)",
            type,
            pgSqlDbType: null);
    }

    [Fact]
    public async Task basic_with_custom_translator()
    {
        //Arrange
        await using var adminConnection = await OpenConnectionAsync();
        var type = await GetTempTypeName(adminConnection);

        await adminConnection.ExecuteNonQueryAsync($"CREATE TYPE {type} AS (x int, s text)", cancellationToken: TestContext.Current.CancellationToken);

        var dataSourceBuilder = CreateDataSourceBuilder();
        dataSourceBuilder.MapComposite<SomeComposite>(type, new CustomTranslator());
        await using var dataSource = dataSourceBuilder.Build();
        await using var connection = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        //Assert
        await AssertType(
            connection,
            new SomeComposite { SomeText = "foo", X = 8 },
            "(8,foo)",
            type,
            pgSqlDbType: null);
    }

    class CustomTranslator : IPgSqlNameTranslator
    {
        public string TranslateTypeName(string clrName) => throw new NotImplementedException();

        public string TranslateMemberName(string clrName) => clrName[0].ToString().ToLowerInvariant();
    }

    [Fact]
    public async Task nested()
    {
        //Arrange
        await using var adminConnection = await OpenConnectionAsync();
        var containerType = await GetTempTypeName(adminConnection);
        var containeeType = await GetTempTypeName(adminConnection);

        await adminConnection.ExecuteNonQueryAsync($@"
CREATE TYPE {containeeType} AS (x int, some_text text);
CREATE TYPE {containerType} AS (a int, containee {containeeType});", cancellationToken: TestContext.Current.CancellationToken);

        var dataSourceBuilder = CreateDataSourceBuilder();
        // Registration in inverse dependency order should work
        dataSourceBuilder
            .MapComposite<SomeCompositeContainer>(containerType)
            .MapComposite<SomeComposite>(containeeType);
        await using var dataSource = dataSourceBuilder.Build();
        await using var connection = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        //Assert
        await AssertType(
            connection,
            new SomeCompositeContainer { A = 8, Containee = new() { SomeText = "foo", X = 9 } },
            @"(8,""(9,foo)"")",
            containerType,
            pgSqlDbType: null);
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/1168")]
    public async Task with_schema()
    {
        //Arrange
        await using var adminConnection = await OpenConnectionAsync();
        var schema = await CreateTempSchema(adminConnection);

        await adminConnection.ExecuteNonQueryAsync($"CREATE TYPE {schema}.some_composite AS (x int, some_text text)", cancellationToken: TestContext.Current.CancellationToken);

        var dataSourceBuilder = CreateDataSourceBuilder();
        dataSourceBuilder.MapComposite<SomeComposite>($"{schema}.some_composite");
        await using var dataSource = dataSourceBuilder.Build();
        await using var connection = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        //Assert
        await AssertType(
            connection,
            new SomeComposite { SomeText = "foo", X = 8 },
            "(8,foo)",
            $"{schema}.some_composite",
            pgSqlDbType: null);
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/4365")]
    public async Task in_different_schemas_same_type_with_nested()
    {
        //Arrange
        await using var adminConnection = await OpenConnectionAsync();
        var firstSchemaName = await CreateTempSchema(adminConnection);
        var secondSchemaName = await CreateTempSchema(adminConnection);

        await adminConnection.ExecuteNonQueryAsync($@"
CREATE TYPE {firstSchemaName}.containee AS (x int, some_text text);
CREATE TYPE {firstSchemaName}.container AS (a int, containee {firstSchemaName}.containee);
CREATE TYPE {secondSchemaName}.containee AS (x int, some_text text);
CREATE TYPE {secondSchemaName}.container AS (a int, containee {secondSchemaName}.containee);", cancellationToken: TestContext.Current.CancellationToken);

        var dataSourceBuilder = CreateDataSourceBuilder();
        dataSourceBuilder
            .MapComposite<SomeComposite>($"{firstSchemaName}.containee")
            .MapComposite<SomeCompositeContainer>($"{firstSchemaName}.container")
            .MapComposite<SomeComposite>($"{secondSchemaName}.containee")
            .MapComposite<SomeCompositeContainer>($"{secondSchemaName}.container");
        await using var dataSource = dataSourceBuilder.Build();
        await using var connection = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        //Assert
        await AssertType(
            connection,
            new SomeCompositeContainer { A = 8, Containee = new() { SomeText = "foo", X = 9 } },
            @"(8,""(9,foo)"")",
            $"{secondSchemaName}.container",
            pgSqlDbType: null,
            isDefaultForWriting: false);

        await AssertType(
            connection,
            new SomeCompositeContainer { A = 8, Containee = new() { SomeText = "foo", X = 9 } },
            @"(8,""(9,foo)"")",
            $"{firstSchemaName}.container",
            pgSqlDbType: null,
            isDefaultForWriting: true);
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/5972")]
    public async Task with_schema_and_dots_in_type_name()
    {
        //Arrange
        await using var adminConnection = await OpenConnectionAsync();
        var schema = await CreateTempSchema(adminConnection);
        var typename = "Some.Composite.with.dots";

        await adminConnection.ExecuteNonQueryAsync($"CREATE TYPE {schema}.\"{typename}\" AS (x int, some_text text)", cancellationToken: TestContext.Current.CancellationToken);

        var dataSourceBuilder = CreateDataSourceBuilder();
        dataSourceBuilder.MapComposite<SomeComposite>($"{schema}.{typename}");
        await using var dataSource = dataSourceBuilder.Build();
        await using var connection = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        //Assert
        await AssertType(
            connection,
            new SomeComposite { SomeText = "foobar", X = 10 },
            "(10,foobar)",
            $"{schema}.\"{typename}\"",
            pgSqlDbType: null);
    }

    [Fact]
    public async Task struct_composite()
    {
        //Arrange
        await using var adminConnection = await OpenConnectionAsync();
        var type = await GetTempTypeName(adminConnection);

        await adminConnection.ExecuteNonQueryAsync($"CREATE TYPE {type} AS (x int, some_text text)", cancellationToken: TestContext.Current.CancellationToken);

        var dataSourceBuilder = CreateDataSourceBuilder();
        dataSourceBuilder.MapComposite<SomeCompositeStruct>(type);
        await using var dataSource = dataSourceBuilder.Build();
        await using var connection = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        //Assert
        await AssertType(
            connection,
            new SomeCompositeStruct { SomeText = "foo", X = 8 },
            "(8,foo)",
            type,
            pgSqlDbType: null);
    }

    [Fact]
    public async Task array()
    {
        //Arrange
        await using var adminConnection = await OpenConnectionAsync();
        var type = await GetTempTypeName(adminConnection);

        await adminConnection.ExecuteNonQueryAsync($"CREATE TYPE {type} AS (x int, some_text text)", cancellationToken: TestContext.Current.CancellationToken);

        var dataSourceBuilder = CreateDataSourceBuilder();
        dataSourceBuilder.MapComposite<SomeComposite>(type);
        await using var dataSource = dataSourceBuilder.Build();
        await using var connection = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        //Assert
        await AssertType(
            connection,
            new SomeComposite[] { new() { SomeText = "foo", X = 8 }, new() { SomeText = "bar", X = 9 }},
            @"{""(8,foo)"",""(9,bar)""}",
            type + "[]",
            pgSqlDbType: null);
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/859")]
    public async Task name_translation()
    {
        //Arrange
        await using var adminConnection = await OpenConnectionAsync();
        var type = await GetTempTypeName(adminConnection);

        await adminConnection.ExecuteNonQueryAsync(@$"
CREATE TYPE {type} AS (simple int, two_words int, some_database_name int)", cancellationToken: TestContext.Current.CancellationToken);

        var dataSourceBuilder = CreateDataSourceBuilder();
        dataSourceBuilder.MapComposite<NameTranslationComposite>(type);
        await using var dataSource = dataSourceBuilder.Build();
        await using var connection = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        //Assert
        await AssertType(
            connection,
            new NameTranslationComposite { Simple = 2, TwoWords = 3, SomeClrName = 4 },
            "(2,3,4)",
            type,
            pgSqlDbType: null);
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/856")]
    public async Task composite_containing_domain_type()
    {
        //Arrange
        await using var adminConnection = await OpenConnectionAsync();
        var domainType = await GetTempTypeName(adminConnection);
        var compositeType = await GetTempTypeName(adminConnection);

        await adminConnection.ExecuteNonQueryAsync($@"
CREATE DOMAIN {domainType} AS TEXT;
CREATE TYPE {compositeType} AS (street TEXT, postal_code {domainType})", cancellationToken: TestContext.Current.CancellationToken);

        var dataSourceBuilder = CreateDataSourceBuilder();
        dataSourceBuilder.MapComposite<Address>(compositeType);
        await using var dataSource = dataSourceBuilder.Build();
        await using var connection = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        //Assert
        await AssertType(
            connection,
            new Address { PostalCode = "12345", Street = "Main St." },
            @"(""Main St."",12345)",
            compositeType,
            pgSqlDbType: null);
    }

    [Fact]
    public async Task composite_containing_array_type()
    {
        //Arrange
        await using var adminConnection = await OpenConnectionAsync();
        var compositeType = await GetTempTypeName(adminConnection);

        await adminConnection.ExecuteNonQueryAsync($@"
CREATE TYPE {compositeType} AS (ints int4[])", cancellationToken: TestContext.Current.CancellationToken);

        var dataSourceBuilder = CreateDataSourceBuilder();
        dataSourceBuilder.MapComposite<SomeCompositeWithArray>(compositeType);
        await using var dataSource = dataSourceBuilder.Build();
        await using var connection = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        //Assert
        await AssertType(
            connection,
            new SomeCompositeWithArray { Ints = [1, 2, 3, 4] },
            @"(""{1,2,3,4}"")",
            compositeType,
            pgSqlDbType: null,
            comparer: (actual, expected) => actual.Ints.SequenceEqual(expected.Ints));
    }

    [Fact]
    public async Task composite_containing_enum_type()
    {
        //Arrange
        await using var adminConnection = await OpenConnectionAsync();
        var enumType = await GetTempTypeName(adminConnection);
        var compositeType = await GetTempTypeName(adminConnection);

        await adminConnection.ExecuteNonQueryAsync($@"
CREATE TYPE {enumType} AS enum ('value1', 'value2', 'value3');
CREATE TYPE {compositeType} AS (enum_value {enumType});", cancellationToken: TestContext.Current.CancellationToken);

        var dataSourceBuilder = CreateDataSourceBuilder();
        dataSourceBuilder.MapComposite<SomeCompositeWithEnum>(compositeType);
        dataSourceBuilder.MapEnum<SomeCompositeWithEnum.TestEnum>(enumType);
        await using var dataSource = dataSourceBuilder.Build();
        await using var connection = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        //Assert
        await AssertType(
            connection,
            new SomeCompositeWithEnum { EnumValue = SomeCompositeWithEnum.TestEnum.Value2 },
            @"(value2)",
            compositeType,
            pgSqlDbType: null,
            comparer: (actual, expected) => actual.EnumValue == expected.EnumValue);
    }

    [Fact]
    public async Task composite_containing_IPAddress()
    {
        //Arrange
        await using var adminConnection = await OpenConnectionAsync();
        var compositeType = await GetTempTypeName(adminConnection);

        await adminConnection.ExecuteNonQueryAsync($@"
CREATE TYPE {compositeType} AS (address inet)", cancellationToken: TestContext.Current.CancellationToken);

        var dataSourceBuilder = CreateDataSourceBuilder();
        dataSourceBuilder.MapComposite<SomeCompositeWithIPAddress>(compositeType);
        await using var dataSource = dataSourceBuilder.Build();
        await using var connection = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        //Assert
        await AssertType(
            connection,
            new SomeCompositeWithIPAddress { Address = IPAddress.Loopback },
            @"(127.0.0.1)",
            compositeType,
            pgSqlDbType: null,
            comparer: (actual, expected) => actual.Address.Equals(expected.Address));
    }

    [Fact]
    public async Task composite_containing_converter_resolver_type()
    {
        //Arrange
        await using var adminConnection = await OpenConnectionAsync();
        var compositeType = await GetTempTypeName(adminConnection);

        await adminConnection.ExecuteNonQueryAsync($@"
CREATE TYPE {compositeType} AS (date_times timestamp[])", cancellationToken: TestContext.Current.CancellationToken);

        var dataSourceBuilder = CreateDataSourceBuilder();
        dataSourceBuilder.ConnectionStringBuilder.Timezone = "Europe/Berlin";
        dataSourceBuilder.MapComposite<SomeCompositeWithConverterResolverType>(compositeType);
        await using var dataSource = dataSourceBuilder.Build();
        await using var connection = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        //Assert
        await AssertType(
            connection,
            new SomeCompositeWithConverterResolverType { DateTimes = [new DateTime(DateTime.UnixEpoch.Ticks, DateTimeKind.Unspecified), new DateTime(DateTime.UnixEpoch.Ticks, DateTimeKind.Unspecified).AddDays(1)
                ]
            },
            """("{""1970-01-01 00:00:00"",""1970-01-02 00:00:00""}")""",
            compositeType,
            pgSqlDbType: null,
            comparer: (actual, expected) => actual.DateTimes.SequenceEqual(expected.DateTimes));
    }

    [Fact]
    public async Task composite_containing_converter_resolver_type_throws()
    {
        //Arrange
        await using var adminConnection = await OpenConnectionAsync();
        var compositeType = await GetTempTypeName(adminConnection);

        await adminConnection.ExecuteNonQueryAsync($@"
CREATE TYPE {compositeType} AS (date_times timestamp[])", cancellationToken: TestContext.Current.CancellationToken);

        var dataSourceBuilder = CreateDataSourceBuilder();
        dataSourceBuilder.ConnectionStringBuilder.Timezone = "Europe/Berlin";
        dataSourceBuilder.MapComposite<SomeCompositeWithConverterResolverType>(compositeType);
        await using var dataSource = dataSourceBuilder.Build();
        await using var connection = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        //Assert
        await Assert.ThrowsAsync<ArgumentException>(() => AssertType(
            connection,
            new SomeCompositeWithConverterResolverType { DateTimes = [DateTime.UnixEpoch] }, // UTC DateTime
            """("{""1970-01-01 01:00:00"",""1970-01-02 01:00:00""}")""",
            compositeType,
            pgSqlDbType: null,
            comparer: (actual, expected) => actual.DateTimes.SequenceEqual(expected.DateTimes)));
    }

    [Theory, IssueLink("https://github.com/npgsql/npgsql/issues/990")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task table_as_composite(bool enabled)
    {
        await using var adminConnection = await OpenConnectionAsync();
        var table = await CreateTempTable(adminConnection, "x int, some_text text");

        var dataSourceBuilder = CreateDataSourceBuilder();
        dataSourceBuilder.MapComposite<SomeComposite>(table);
        dataSourceBuilder.ConfigureTypeLoading(b => b.EnableTableCompositesLoading(enabled));
        await using var dataSource = dataSourceBuilder.Build();
        await using var connection = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        if (enabled)
            await DoAssertion();
        else
        {
            await Assert.ThrowsAsync<NotSupportedException>(DoAssertion);
            // Start a transaction specifically for multiplexing (to bind a connector to the connection)
            await using var tx = await connection.BeginTransactionAsync(TestContext.Current.CancellationToken);
            connection.Connector.DatabaseInfo.CompositeTypes.SingleOrDefault(c => c.Name.Contains(table)).Should().BeNull();
            connection.Connector.DatabaseInfo.ArrayTypes.SingleOrDefault(c => c.Name.Contains(table)).Should().BeNull();

        }

        Task DoAssertion()
            => AssertType(
            connection,
            new SomeComposite { SomeText = "foo", X = 8 },
            "(8,foo)",
            table,
            pgSqlDbType: null);
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/1267")]
    public async Task table_as_composite_with_deleted_columns()
    {
        //Arrange
        await using var adminConnection = await OpenConnectionAsync();
        var table = await CreateTempTable(adminConnection, "x int, some_text text, bar int");
        await adminConnection.ExecuteNonQueryAsync($"ALTER TABLE {table} DROP COLUMN bar;", cancellationToken: TestContext.Current.CancellationToken);

        var dataSourceBuilder = CreateDataSourceBuilder();
        dataSourceBuilder.ConfigureTypeLoading(b => b.EnableTableCompositesLoading());
        dataSourceBuilder.MapComposite<SomeComposite>(table);
        await using var dataSource = dataSourceBuilder.Build();
        await using var connection = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        //Assert
        await AssertType(
            connection,
            new SomeComposite { SomeText = "foo", X = 8 },
            "(8,foo)",
            table,
            pgSqlDbType: null);
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/1125")]
    public async Task nullable_property_in_class_composite()
    {
        //Arrange
        await using var adminConnection = await OpenConnectionAsync();
        var type = await GetTempTypeName(adminConnection);

        await adminConnection.ExecuteNonQueryAsync($"CREATE TYPE {type} AS (foo INT)", cancellationToken: TestContext.Current.CancellationToken);

        var dataSourceBuilder = CreateDataSourceBuilder();
        dataSourceBuilder.MapComposite<ClassWithNullableProperty>(type);
        await using var dataSource = dataSourceBuilder.Build();
        await using var connection = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        //Assert
        await AssertType(
            connection,
            new ClassWithNullableProperty { Foo = 8 },
            "(8)",
            type,
            pgSqlDbType: null);

        await AssertType(
            connection,
            new ClassWithNullableProperty { Foo = null },
            "()",
            type,
            pgSqlDbType: null);
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/1125")]
    public async Task nullable_property_in_struct_composite()
    {
        //Arrange
        await using var adminConnection = await OpenConnectionAsync();
        var type = await GetTempTypeName(adminConnection);

        await adminConnection.ExecuteNonQueryAsync($"CREATE TYPE {type} AS (foo INT)", cancellationToken: TestContext.Current.CancellationToken);

        var dataSourceBuilder = CreateDataSourceBuilder();
        dataSourceBuilder.MapComposite<StructWithNullableProperty>(type);
        await using var dataSource = dataSourceBuilder.Build();
        await using var connection = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        //Assert
        await AssertType(
            connection,
            new StructWithNullableProperty { Foo = 8 },
            "(8)",
            type,
            pgSqlDbType: null);

        await AssertType(
            connection,
            new StructWithNullableProperty { Foo = null },
            "()",
            type,
            pgSqlDbType: null);
    }

    [Fact]
    public async Task postgres_type()
    {
        //Arrange
        await using var connection = await OpenConnectionAsync();
        var type1 = await GetTempTypeName(connection);
        var type2 = await GetTempTypeName(connection);

        await connection.ExecuteNonQueryAsync(@$"
CREATE TYPE {type1} AS (x int, some_text text);
CREATE TYPE {type2} AS (comp {type1}, comps {type1}[]);", cancellationToken: TestContext.Current.CancellationToken);
        await connection.ReloadTypesAsync(TestContext.Current.CancellationToken);

        await using var cmd = new PgSqlCommand(
            $"SELECT ROW(ROW(8, 'foo')::{type1}, ARRAY[ROW(9, 'bar')::{type1}, ROW(10, 'baz')::{type1}])::{type2}",
            connection);
        //Act
        await using var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        await reader.ReadAsync(TestContext.Current.CancellationToken);

        var comp2Type = (PostgresCompositeType)reader.GetPostgresType(0);
        //Assert
        comp2Type.Name.Should().Be(type2);
        comp2Type.FullName.Should().Be($"public.{type2}");
        comp2Type.Fields.Should().HaveCount(2);
        var field1 = comp2Type.Fields[0];
        var field2 = comp2Type.Fields[1];
        field1.Name.Should().Be("comp");
        field2.Name.Should().Be("comps");
        var comp1Type = (PostgresCompositeType)field1.Type;
        comp1Type.Name.Should().Be(type1);
        var arrType = (PostgresArrayType)field2.Type;
        arrType.Name.Should().Be(type1 + "[]");
        var elemType = arrType.Element;
        elemType.Should().BeSameAs(comp1Type);
    }

    [Fact]
    public async Task duplicate_constructor_parameters()
    {
        //Arrange
        await using var adminConnection = await OpenConnectionAsync();
        var type = await GetTempTypeName(adminConnection);

        await adminConnection.ExecuteNonQueryAsync($"CREATE TYPE {type} AS (long int8, boolean bool)", cancellationToken: TestContext.Current.CancellationToken);

        var dataSourceBuilder = CreateDataSourceBuilder();
        dataSourceBuilder.MapComposite<DuplicateOneLongOneBool>(type);
        await using var dataSource = dataSourceBuilder.Build();
        await using var connection = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        //Assert
        var ex = await Assert.ThrowsAsync<InvalidCastException>(async () => await AssertType(
            connection,
            new DuplicateOneLongOneBool(true, 1),
            "(1,t)",
            type,
            pgSqlDbType: null));
        ex.InnerException.Should().BeOfType<AmbiguousMatchException>();
    }

    [Fact]
    public async Task partial_constructor_missing_setter()
    {
        //Arrange
        await using var adminConnection = await OpenConnectionAsync();
        var type = await GetTempTypeName(adminConnection);

        await adminConnection.ExecuteNonQueryAsync($"CREATE TYPE {type} AS (long int8, boolean bool)", cancellationToken: TestContext.Current.CancellationToken);

        var dataSourceBuilder = CreateDataSourceBuilder();
        dataSourceBuilder.MapComposite<MissingSetterOneLongOneBool>(type);
        await using var dataSource = dataSourceBuilder.Build();
        await using var connection = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        //Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () => await AssertTypeRead(
            connection,
            "(1,t)",
            type,
            new MissingSetterOneLongOneBool(true, 1)));
        ex.Message.Should().Contain("No (public) setter for");
    }

    [Fact]
    public async Task partial_constructor_works()
    {
        //Arrange
        await using var adminConnection = await OpenConnectionAsync();
        var type = await GetTempTypeName(adminConnection);

        await adminConnection.ExecuteNonQueryAsync($"CREATE TYPE {type} AS (long int8, boolean bool)", cancellationToken: TestContext.Current.CancellationToken);

        var dataSourceBuilder = CreateDataSourceBuilder();
        dataSourceBuilder.MapComposite<OneLongOneBool>(type);
        await using var dataSource = dataSourceBuilder.Build();
        await using var connection = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        //Assert
        await AssertType(
            connection,
            new OneLongOneBool(1) { BooleanValue = true },
            "(1,t)",
            type,
            pgSqlDbType: null);
    }

    [Fact]
    public async Task composite_over_range()
    {
        //Arrange
        await using var adminConnection = await OpenConnectionAsync();
        var type = await GetTempTypeName(adminConnection);
        var rangeType = await GetTempTypeName(adminConnection);

        await adminConnection.ExecuteNonQueryAsync($"CREATE TYPE {type} AS (x int, some_text text); CREATE TYPE {rangeType} AS RANGE(subtype={type})", cancellationToken: TestContext.Current.CancellationToken);

        var dataSourceBuilder = CreateDataSourceBuilder();
        dataSourceBuilder.MapComposite<SomeComposite>(type);
        dataSourceBuilder.EnableUnmappedTypes();
        await using var dataSource = dataSourceBuilder.Build();
        await using var connection = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        var composite1 = new SomeComposite
        {
            SomeText = "foo",
            X = 8
        };

        var composite2 = new SomeComposite
        {
            SomeText = "bar",
            X = 42
        };

        //Assert
        await AssertType(
            connection,
            new PgSqlRange<SomeComposite>(composite1, composite2),
            "[\"(8,foo)\",\"(42,bar)\"]",
            rangeType,
            pgSqlDbType: null,
            isDefaultForWriting: false);
    }

    #region Test Types

    readonly struct DuplicateOneLongOneBool
    {
        public DuplicateOneLongOneBool(bool boolean, [PgName("boolean")] int @bool)
        {
        }

        [PgName("long")]
        public long LongValue { get; }

        [PgName("boolean")]
        public bool BooleanValue { get; }
    }

    readonly struct MissingSetterOneLongOneBool
    {
        public MissingSetterOneLongOneBool(long @long)
            => LongValue = @long;

        public MissingSetterOneLongOneBool(bool boolean, [PgName("boolean")]int @bool)
        {
        }

        [PgName("long")]
        public long LongValue { get; }

        [PgName("boolean")]
        public bool BooleanValue { get; }
    }

    struct OneLongOneBool
    {
        public OneLongOneBool(bool boolean, [PgName("boolean")]int @bool)
        {
        }

        public OneLongOneBool(long @long)
            => LongValue = @long;

        public OneLongOneBool(double other)
        {
        }

        public OneLongOneBool(int boolean, [PgName("boolean")]bool @bool)
        {
        }

        [PgName("long")]
        public long LongValue { get; }

        [PgName("boolean")]
        public bool BooleanValue { get; set; }
    }

    internal record SomeComposite
    {
        public int X { get; set; }
        public string SomeText { get; set; } = null;
    }

    record SomeCompositeContainer
    {
        public int A { get; set; }
        public SomeComposite Containee { get; set; }  = null;
    }

    struct SomeCompositeStruct
    {
        public int X { get; set; }
        public string SomeText { get; set; }
    }

    class SomeCompositeWithArray
    {
        public int[] Ints { get; set; }
    }

    class SomeCompositeWithEnum
    {
        public enum TestEnum
        {
            Value1,
            Value2,
            Value3
        }

        public TestEnum EnumValue { get; set; }
    }

    class SomeCompositeWithIPAddress
    {
        public IPAddress Address { get; set; }
    }

    class SomeCompositeWithConverterResolverType
    {
        public DateTime[] DateTimes { get; set; }
    }

    record NameTranslationComposite
    {
        public int Simple { get; set; }
        public int TwoWords { get; set; }
        [PgName("some_database_name")]
        public int SomeClrName { get; set; }
    }

    record Address
    {
        public string Street { get; set; } = default;
        public string PostalCode { get; set; } = default;
    }

    record ClassWithNullableProperty
    {
        public int? Foo { get; set; }
    }

    struct StructWithNullableProperty
    {
        public int? Foo { get; set; }
    }

    #endregion
}

public sealed class CompositeTests_NonMultiplexing() : CompositeTests(MultiplexingMode.NonMultiplexing);
public sealed class CompositeTests_Multiplexing() : CompositeTests(MultiplexingMode.Multiplexing);

[Collection(NonParallelCollection.Name)]
public abstract class CompositeTestsNonParallel(MultiplexingMode multiplexingMode) : MultiplexingTestBase(multiplexingMode)
{
    [Fact]
    public async Task global_mapping()
    {
        await using var adminConnection = await OpenConnectionAsync();
        var type = await GetTempTypeName(adminConnection);

        await adminConnection.ExecuteNonQueryAsync($"CREATE TYPE {type} AS (x int, some_text text)", cancellationToken: TestContext.Current.CancellationToken);
        PgSqlConnection.GlobalTypeMapper.MapComposite<CompositeTests.SomeComposite>(type);

        try
        {
            var dataSourceBuilder = CreateDataSourceBuilder();
            dataSourceBuilder.MapComposite<CompositeTests.SomeComposite>(type);
            await using var dataSource = dataSourceBuilder.Build();
            await using var connection = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
            await connection.ReloadTypesAsync(TestContext.Current.CancellationToken);

            await AssertType(
                connection,
                new CompositeTests.SomeComposite { SomeText = "foo", X = 8 },
                "(8,foo)",
                type,
                pgSqlDbType: null);
        }
        finally
        {
            PgSqlConnection.GlobalTypeMapper.Reset();
        }
    }
}

public sealed class CompositeTestsNonParallel_NonMultiplexing() : CompositeTestsNonParallel(MultiplexingMode.NonMultiplexing);
public sealed class CompositeTestsNonParallel_Multiplexing() : CompositeTestsNonParallel(MultiplexingMode.Multiplexing);
