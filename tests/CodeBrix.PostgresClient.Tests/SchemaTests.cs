using System;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CodeBrix.PostgresClient.PgSqlTypes;
using SilverAssertions;
using Xunit;
using static CodeBrix.PostgresClient.Tests.TestUtil;

namespace CodeBrix.PostgresClient.Tests; //was previously: Npgsql.Tests;

public abstract class SchemaTests(SyncOrAsync syncOrAsync) : SyncOrAsyncTestBase(syncOrAsync)
{
    [Fact]
    public async Task meta_data_collections()
    {
        await using var conn = await OpenConnectionAsync();

        var metaDataCollections = await GetSchema(conn, DbMetaDataCollectionNames.MetaDataCollections);
        metaDataCollections.Rows.Count.Should().BeGreaterThan(0);

        foreach (var row in metaDataCollections.Rows.OfType<DataRow>())
        {
            var collectionName = (string)row["CollectionName"];
            (await GetSchema(conn, collectionName)).Should().NotBeNull($"collection {collectionName} is advertised in MetaDataCollections");
        }
    }

    // Calling GetSchema() without a parameter should be the same as passing MetaDataCollections
    [Fact]
    public async Task no_parameter()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();

        var dataTable1 = await GetSchema(conn);
        var collections1 = dataTable1.Rows
            .Cast<DataRow>()
            .Select(r => (string)r["CollectionName"])
            .ToList();

        var dataTable2 = await GetSchema(conn, DbMetaDataCollectionNames.MetaDataCollections);
        var collections2 = dataTable2.Rows
            .Cast<DataRow>()
            .Select(r => (string)r["CollectionName"])
            .ToList();

        //Assert
        collections1.Should().BeEquivalentTo(collections2);
    }

    // Calling GetSchema(collectionName [, restrictions]) case insensitive collectionName can be used
    [Fact]
    public async Task case_insensitive_collection_name()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();

        var dataTable1 = await GetSchema(conn, DbMetaDataCollectionNames.MetaDataCollections);
        var collections1 = dataTable1.Rows
            .Cast<DataRow>()
            .Select(r => (string)r["CollectionName"])
            .ToList();

        var dataTable2 = await GetSchema(conn, "METADATACOLLECTIONS");
        var collections2 = dataTable2.Rows
            .Cast<DataRow>()
            .Select(r => (string)r["CollectionName"])
            .ToList();

        var dataTable3 = await GetSchema(conn, "metadatacollections");
        var collections3 = dataTable3.Rows
            .Cast<DataRow>()
            .Select(r => (string)r["CollectionName"])
            .ToList();

        var dataTable4 = await GetSchema(conn, "MetaDataCollections");
        var collections4 = dataTable4.Rows
            .Cast<DataRow>()
            .Select(r => (string)r["CollectionName"])
            .ToList();

        var dataTable5 = await GetSchema(conn, "METADATACOLLECTIONS", null);
        var collections5 = dataTable5.Rows
            .Cast<DataRow>()
            .Select(r => (string)r["CollectionName"])
            .ToList();

        var dataTable6 = await GetSchema(conn, "metadatacollections", null);
        var collections6 = dataTable6.Rows
            .Cast<DataRow>()
            .Select(r => (string)r["CollectionName"])
            .ToList();

        var dataTable7 = await GetSchema(conn, "MetaDataCollections", null);
        var collections7 = dataTable7.Rows
            .Cast<DataRow>()
            .Select(r => (string)r["CollectionName"])
            .ToList();

        //Assert
        collections1.Should().BeEquivalentTo(collections2);
        collections1.Should().BeEquivalentTo(collections3);
        collections1.Should().BeEquivalentTo(collections4);
        collections1.Should().BeEquivalentTo(collections5);
        collections1.Should().BeEquivalentTo(collections6);
        collections1.Should().BeEquivalentTo(collections7);
    }

    [Fact]
    public async Task data_source_information()
    {
        await using var conn = await OpenConnectionAsync();
        var dataTable = await GetSchema(conn, DbMetaDataCollectionNames.MetaDataCollections);
        var metadata = dataTable.Rows
            .Cast<DataRow>()
            .Single(r => r["CollectionName"].Equals("DataSourceInformation"));
        metadata["NumberOfRestrictions"].Should().Be(0);
        metadata["NumberOfIdentifierParts"].Should().Be(0);

        var dataSourceInfo = await GetSchema(conn, DbMetaDataCollectionNames.DataSourceInformation);
        var row = dataSourceInfo.Rows.Cast<DataRow>().Single();

        row["DataSourceProductName"].Should().Be("PgSql");

        var pgVersion = conn.PostgreSqlVersion;
        row["DataSourceProductVersion"].Should().Be(pgVersion.ToString());

        var parsedNormalizedVersion = Version.Parse((string)row["DataSourceProductVersionNormalized"]);
        parsedNormalizedVersion.Should().Be(conn.PostgreSqlVersion);

        Regex.Match("\"some_identifier\"", (string)row["QuotedIdentifierPattern"]).Groups[1].Value.Should().Be("some_identifier");
    }

    [Fact]
    public async Task data_types()
    {
        await using var adminConnection = await OpenConnectionAsync();
        var enumType = await GetTempTypeName(adminConnection);
        var compositeType = await GetTempTypeName(adminConnection);
        var domainType = await GetTempTypeName(adminConnection);
        await adminConnection.ExecuteNonQueryAsync($@"
CREATE TYPE {enumType} AS ENUM ('a', 'b');
CREATE TYPE {compositeType} AS (a INTEGER);
CREATE DOMAIN {domainType} AS TEXT", cancellationToken: TestContext.Current.CancellationToken);

        var dataSourceBuilder = CreateDataSourceBuilder();
        dataSourceBuilder.MapEnum<TestEnum>(enumType);
        dataSourceBuilder.MapComposite<TestComposite>(compositeType);
        await using var dataSource = dataSourceBuilder.Build();
        await using var connection = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        var dataTable = await GetSchema(connection, DbMetaDataCollectionNames.MetaDataCollections);
        var metadata = dataTable.Rows
            .Cast<DataRow>()
            .Single(r => r["CollectionName"].Equals("DataTypes"));
        metadata["NumberOfRestrictions"].Should().Be(0);
        metadata["NumberOfIdentifierParts"].Should().Be(0);

        var dataTypes = await GetSchema(connection, DbMetaDataCollectionNames.DataTypes);

        var intRow = dataTypes.Rows.Cast<DataRow>().Single(r => r["TypeName"].Equals("integer"));
        intRow["DataType"].Should().Be("System.Int32");
        intRow["ProviderDbType"].Should().Be((int)PgSqlDbType.Integer);
        intRow["IsUnsigned"].Should().Be(false);
        intRow["OID"].Should().Be(23);

        var textRow = dataTypes.Rows.Cast<DataRow>().Single(r => r["TypeName"].Equals("text"));
        textRow["DataType"].Should().Be("System.String");
        textRow["ProviderDbType"].Should().Be((int)PgSqlDbType.Text);
        textRow["IsUnsigned"].Should().BeSameAs(DBNull.Value);
        textRow["OID"].Should().Be(25);

        var numericRow = dataTypes.Rows.Cast<DataRow>().Single(r => r["TypeName"].Equals("numeric"));
        numericRow["DataType"].Should().Be("System.Decimal");
        numericRow["ProviderDbType"].Should().Be((int)PgSqlDbType.Numeric);
        numericRow["IsUnsigned"].Should().Be(false);
        numericRow["OID"].Should().Be(1700);
        numericRow["CreateFormat"].Should().Be("NUMERIC({0},{1})");
        numericRow["CreateParameters"].Should().Be("precision, scale");

        var intArrayRow = dataTypes.Rows.Cast<DataRow>().Single(r => r["TypeName"].Equals("integer[]"));
        intArrayRow["DataType"].Should().Be("System.Int32[]");
        intArrayRow["ProviderDbType"].Should().Be((int)(PgSqlDbType.Integer | PgSqlDbType.Array));
        intArrayRow["OID"].Should().Be(1007);
        intArrayRow["CreateFormat"].Should().Be("INTEGER[]");

        var numericArrayRow = dataTypes.Rows.Cast<DataRow>().Single(r => r["TypeName"].Equals("numeric[]"));
        numericArrayRow["CreateFormat"].Should().Be("NUMERIC({0},{1})[]");
        numericArrayRow["CreateParameters"].Should().Be("precision, scale");

        var intRangeRow = dataTypes.Rows.Cast<DataRow>().Single(r => ((string)r["TypeName"]).EndsWith("int4range"));
        ((string)intRangeRow["DataType"]).Should().StartWith("CodeBrix.PostgresClient.PgSqlTypes.PgSqlRange`1[[System.Int32");
        intRangeRow["ProviderDbType"].Should().Be((int)(PgSqlDbType.Integer | PgSqlDbType.Range));
        intRangeRow["OID"].Should().Be(3904);

        var enumRow = dataTypes.Rows.Cast<DataRow>().Single(r => ((string)r["TypeName"]).EndsWith("." + enumType));
        enumRow["DataType"].Should().Be("CodeBrix.PostgresClient.Tests.SchemaTests+TestEnum");
        enumRow["ProviderDbType"].Should().BeSameAs(DBNull.Value);

        var compositeRow = dataTypes.Rows.Cast<DataRow>().Single(r => ((string)r["TypeName"]).EndsWith("." + compositeType));
        compositeRow["DataType"].Should().Be("CodeBrix.PostgresClient.Tests.SchemaTests+TestComposite");
        compositeRow["ProviderDbType"].Should().BeSameAs(DBNull.Value);

        var domainRow = dataTypes.Rows.Cast<DataRow>().Single(r => ((string)r["TypeName"]).EndsWith("." + domainType));
        domainRow["DataType"].Should().Be("System.String");
        domainRow["ProviderDbType"].Should().Be((int)PgSqlDbType.Text);
        domainRow["IsBestMatch"].Should().Be(false);
    }

    enum TestEnum { A, B };

    class TestComposite { public int A { get; set; } }

    [Fact]
    public async Task restrictions()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        //Act
        var restrictions = await GetSchema(conn, DbMetaDataCollectionNames.Restrictions);
        //Assert
        restrictions.Rows.Count.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task reserved_words()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        //Act
        var reservedWords = await GetSchema(conn, DbMetaDataCollectionNames.ReservedWords);
        //Assert
        reservedWords.Rows.Count.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task databases()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        var database = await conn.ExecuteScalarAsync("SELECT current_database()", cancellationToken: TestContext.Current.CancellationToken);

        var dataTable = await GetSchema(conn, "Databases");
        var databases = dataTable.Rows
            .Cast<DataRow>()
            .Select(r => (string)r["database_name"])
            .ToList();

        //Assert
        databases.Should().Contain((string)database);
    }

    [Fact]
    public async Task schemata()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        var schema = await CreateTempSchema(conn);

        var dataTable = await GetSchema(conn, "Schemata");
        var row = dataTable.Rows.Cast<DataRow>().Single(r => (string)r["schema_name"] == schema);

        //Assert
        row["catalog_name"].Should().Be(await conn.ExecuteScalarAsync("SELECT current_database()", cancellationToken: TestContext.Current.CancellationToken));
        row["schema_owner"].Should().Be(await conn.ExecuteScalarAsync("SELECT current_user", cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task foreign_keys()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        //Act
        var dt = await GetSchema(conn, "ForeignKeys");
        //Assert
        dt.Should().NotBeNull();
    }

    [Fact]
    public async Task parameter_marker_format()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();

        var table = await CreateTempTable(conn, "int INTEGER");
        await conn.ExecuteNonQueryAsync($"INSERT INTO {table} (int) VALUES (4)", cancellationToken: TestContext.Current.CancellationToken);

        var dt = await GetSchema(conn, "DataSourceInformation");
        var parameterMarkerFormat = (string)dt.Rows[0]["ParameterMarkerFormat"];

        await using var command = conn.CreateCommand();
        const string parameterName = "@p_int";
        command.CommandText = $"SELECT * FROM {table} WHERE int=" + string.Format(parameterMarkerFormat, parameterName);
        command.Parameters.Add(new PgSqlParameter(parameterName, 4));
        //Act
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        //Assert
        reader.Read().Should().BeTrue();
    }

    [Fact]
    public async Task precision_and_scale()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(
            conn, "explicit_both NUMERIC(10,2), explicit_precision NUMERIC(10), implicit_both NUMERIC, integer INTEGER, text TEXT");

        var dataTable = await GetSchema(conn, "Columns", [null, null, table]);
        var rows = dataTable.Rows.Cast<DataRow>().ToList();

        var explicitBoth = rows.Single(r => (string)r["column_name"] == "explicit_both");
        //Assert
        explicitBoth["numeric_precision"].Should().Be(10);
        explicitBoth["numeric_scale"].Should().Be(2);

        var explicitPrecision = rows.Single(r => (string)r["column_name"] == "explicit_precision");
        explicitPrecision["numeric_precision"].Should().Be(10);
        explicitPrecision["numeric_scale"].Should().Be(0); // Not good

        // Consider exposing actual precision/scale even for implicit
        var implicitBoth = rows.Single(r => (string)r["column_name"] == "implicit_both");
        implicitBoth["numeric_precision"].Should().Be(DBNull.Value);
        implicitBoth["numeric_scale"].Should().Be(DBNull.Value);

        var integer = rows.Single(r => (string)r["column_name"] == "integer");
        integer["numeric_precision"].Should().Be(32);
        integer["numeric_scale"].Should().Be(0);

        var text = rows.Single(r => (string)r["column_name"] == "text");
        text["numeric_precision"].Should().Be(DBNull.Value);
        text["numeric_scale"].Should().Be(DBNull.Value);
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/1831")]
    public async Task no_system_tables()
    {
        await using var conn = await OpenConnectionAsync();

        var dataTable = await GetSchema(conn, "Tables");
        var tables = dataTable.Rows
            .Cast<DataRow>()
            .Select(r => (string)r["TABLE_NAME"])
            .ToList();
        tables.Should().NotContain("pg_type");  // schema pg_catalog
        tables.Should().NotContain("tables");   // schema information_schema

        dataTable = await GetSchema(conn, "Views");
        var views = dataTable.Rows
            .Cast<DataRow>()
            .Select(r => (string)r["TABLE_NAME"])
            .ToList();
        views.Should().NotContain("pg_user");  // schema pg_catalog
        views.Should().NotContain("views");    // schema information_schema
    }

    [Fact]
    public async Task GetSchema_tables_with_restrictions()
    {
        await using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "bar INTEGER");

        var dt = await GetSchema(conn, "Tables", [null, null, table]);
        foreach (var row in dt.Rows.OfType<DataRow>())
            row["table_name"].Should().Be(table);
    }

    [Fact]
    public async Task GetSchema_views_with_restrictions()
    {
        await using var conn = await OpenConnectionAsync();
        var view = await GetTempViewName(conn);

        await conn.ExecuteNonQueryAsync($"CREATE VIEW {view} AS SELECT 8 AS foo", cancellationToken: TestContext.Current.CancellationToken);

        var dt = await GetSchema(conn, "Views", [null, null, view]);
        foreach (var row in dt.Rows.OfType<DataRow>())
            row["table_name"].Should().Be(view);
    }

    [Fact]
    public async Task GetSchema_materialized_views_with_restrictions()
    {
        await using var conn = await OpenConnectionAsync();
        var viewName = await GetTempMaterializedViewName(conn);

        await conn.ExecuteNonQueryAsync($"CREATE MATERIALIZED VIEW {viewName} AS SELECT 8 AS foo", cancellationToken: TestContext.Current.CancellationToken);

        var dt = await GetSchema(conn, "MaterializedViews", [null, viewName, null, null]);
        foreach (var row in dt.Rows.OfType<DataRow>())
            row["table_name"].Should().Be(viewName);
    }

    [Fact]
    public async Task primary_key()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "id INT PRIMARY KEY, f1 INT");

        var dataTable = await GetSchema(conn, "CONSTRAINTCOLUMNS", [null, null, table]);
        var column = dataTable.Rows.Cast<DataRow>().Single();

        //Assert
        column["table_schema"].Should().Be("public");
        column["table_name"].Should().Be(table);
        column["column_name"].Should().Be("id");
        column["constraint_type"].Should().Be("PRIMARY KEY");
    }

    [Fact]
    public async Task primary_key_composite()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "id1 INT, id2 INT, f1 INT, PRIMARY KEY (id1, id2)");

        var dataTable = await GetSchema(conn, "CONSTRAINTCOLUMNS", [null, null, table]);
        var columns = dataTable.Rows.Cast<DataRow>().OrderBy(r => r["ordinal_number"]).ToList();

        //Assert
        columns.All(r => r["table_schema"].Equals("public")).Should().BeTrue();
        columns.All(r => r["table_name"].Equals(table)).Should().BeTrue();
        columns.All(r => r["constraint_type"].Equals("PRIMARY KEY")).Should().BeTrue();

        columns[0]["column_name"].Should().Be("id1");
        columns[1]["column_name"].Should().Be("id2");
    }

    [Fact]
    public async Task unique_constraint()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "f1 INT, f2 INT, UNIQUE (f1, f2)");

        var database = await conn.ExecuteScalarAsync("SELECT current_database()", cancellationToken: TestContext.Current.CancellationToken);

        var dataTable = await GetSchema(conn, "CONSTRAINTCOLUMNS", [null, null, table]);
        var columns = dataTable.Rows.Cast<DataRow>().ToList();

        //Assert
        columns.All(r => r["constraint_catalog"].Equals(database)).Should().BeTrue();
        columns.All(r => r["constraint_schema"].Equals("public")).Should().BeTrue();
        columns.All(r => r["constraint_name"] is not null).Should().BeTrue();
        columns.All(r => r["table_catalog"].Equals(database)).Should().BeTrue();
        columns.All(r => r["table_schema"].Equals("public")).Should().BeTrue();
        columns.All(r => r["table_name"].Equals(table)).Should().BeTrue();
        columns.All(r => r["constraint_type"].Equals("UNIQUE KEY")).Should().BeTrue();

        columns.Count.Should().Be(2);

        // Columns are not necessarily in the correct order
        var firstColumn = columns.FirstOrDefault(x => (string)x["column_name"] == "f1");
        firstColumn.Should().NotBeNull();
        firstColumn["ordinal_number"].Should().Be(1);

        var secondColumn = columns.FirstOrDefault(x => (string)x["column_name"] == "f2");
        secondColumn.Should().NotBeNull();
        secondColumn["ordinal_number"].Should().Be(2);
    }

    [Fact]
    public async Task unique_index_composite()
    {
        await using var conn = await OpenConnectionAsync();
        var table = await GetTempTableName(conn);
        var constraint = table + "_uq";
        await conn.ExecuteNonQueryAsync(@$"
CREATE TABLE {table} (
    f1 INT,
    f2 INT,
    CONSTRAINT {constraint} UNIQUE (f1, f2)
)", cancellationToken: TestContext.Current.CancellationToken);

        var database = await conn.ExecuteScalarAsync("SELECT current_database()", cancellationToken: TestContext.Current.CancellationToken);

        var dataTable = await GetSchema(conn, "INDEXES", [null, null, table]);
        var index = dataTable.Rows.Cast<DataRow>().Single();

        index["table_schema"].Should().Be("public");
        index["table_name"].Should().Be(table);
        index["index_name"].Should().Be(constraint);
        index["type_desc"].Should().Be("");

        string[] indexColumnRestrictions = [null, null, table];
        var dataTable2 = await GetSchema(conn, "INDEXCOLUMNS", indexColumnRestrictions);
        var columns = dataTable2.Rows.Cast<DataRow>().ToList();

        columns.All(r => r["constraint_catalog"].Equals(database)).Should().BeTrue();
        columns.All(r => r["constraint_schema"].Equals("public")).Should().BeTrue();
        columns.All(r => r["constraint_name"].Equals(constraint)).Should().BeTrue();
        columns.All(r => r["table_catalog"].Equals(database)).Should().BeTrue();
        columns.All(r => r["table_schema"].Equals("public")).Should().BeTrue();
        columns.All(r => r["table_name"].Equals(table)).Should().BeTrue();

        columns[0]["column_name"].Should().Be("f1");
        columns[1]["column_name"].Should().Be("f2");

        string[] indexColumnRestrictions3 = [(string) database , "public", table, constraint, "f1"];
        var dataTable3 = await GetSchema(conn, "INDEXCOLUMNS", indexColumnRestrictions3);
        var columns3 = dataTable3.Rows.Cast<DataRow>().ToList();
        columns3.Count.Should().Be(1);
        columns3.Single()["column_name"].Should().Be("f1");
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/1886")]
    public async Task column_schema_data_types()
    {
        await using var conn = await OpenConnectionAsync();

        var columnDefinition = @"
p0 integer PRIMARY KEY NOT NULL,
achar char,
char character(3),
vchar character varying(10),
text text,
bytea bytea,
abit bit(1),
bit bit(3),
vbit bit varying(5),
boolean boolean,
smallint smallint,
integer integer,
bigint bigint,
real real,
double double precision,
numeric numeric,
money money,
date date,
timetz time with time zone,
timestamptz timestamp with time zone,
time time without time zone,
timestamp timestamp without time zone,
point point,
box box,
lseg lseg,
path path,
polygon polygon,
circle circle,
line line,
inet inet,
macaddr macaddr,
uuid uuid,
interval interval,
name name,
refcursor refcursor,
numrange numrange,
oidvector oidvector,
""bigint[]"" bigint[],
cidr cidr,
maccaddr8 macaddr8,
jsonb jsonb,
json json,
xml xml,
tsvector tsvector,
tsquery tsquery,
tid tid,
xid xid,
cid cid";
        var table = await CreateTempTable(conn, columnDefinition);

        var columnsSchema = await GetSchema(conn, "Columns", [null, null, table]);
        var columns = columnsSchema.Rows.Cast<DataRow>().ToList();

        var dataTypes = await GetSchema(conn, DbMetaDataCollectionNames.DataTypes);

        var nonMatching = columns.FirstOrDefault(col => !dataTypes.Rows.Cast<DataRow>().Any(row => row["TypeName"].Equals(col["data_type"])));
        if (nonMatching is not null)
            Assert.Fail($"Could not find matching data type for column {nonMatching["column_name"]} with type {nonMatching["data_type"]}");
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/4392")]
    public async Task enum_in_public_schema()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        var enumName = await GetTempTypeName(conn);
        var table = await GetTempTableName(conn);

        await conn.ExecuteNonQueryAsync($@"
CREATE TYPE {enumName} AS ENUM ('red', 'yellow', 'blue');
CREATE TABLE {table} (color {enumName});", cancellationToken: TestContext.Current.CancellationToken);

        var dataTable = await GetSchema(conn, "Columns", [null, null, table]);
        var row = dataTable.Rows.Cast<DataRow>().Single();
        //Assert
        row["data_type"].Should().Be(enumName);
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/4392")]
    public async Task enum_in_non_public_schema()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        const string enumName = "my_enum";
        var schema = await CreateTempSchema(conn);
        var table = await GetTempTableName(conn);

        await conn.ExecuteNonQueryAsync($@"
CREATE TYPE {schema}.{enumName} AS ENUM ('red', 'yellow', 'blue');
CREATE TABLE {table} (color {schema}.{enumName});", cancellationToken: TestContext.Current.CancellationToken);

        var dataTable = await GetSchema(conn, "Columns", [null, null, table]);
        var row = dataTable.Rows.Cast<DataRow>().Single();
        //Assert
        row["data_type"].Should().Be($"{schema}.{enumName}");
    }

    [Fact]
    public async Task slim_builder_introspection_without_unsupported_type_exceptions()
    {
        //Arrange
        await using var dataSource = new PgSqlSlimDataSourceBuilder(ConnectionString).Build();
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        //Assert
        await FluentActions.Awaiting(() => GetSchema(conn, DbMetaDataCollectionNames.DataTypes)).Should().NotThrowAsync();
    }

    // ReSharper disable MethodHasAsyncOverload
    async Task<DataTable> GetSchema(PgSqlConnection conn)
        => IsAsync ? await conn.GetSchemaAsync(TestContext.Current.CancellationToken) : conn.GetSchema();

    async Task<DataTable> GetSchema(PgSqlConnection conn, string collectionName)
        => IsAsync ? await conn.GetSchemaAsync(collectionName, TestContext.Current.CancellationToken) : conn.GetSchema(collectionName);

    async Task<DataTable> GetSchema(PgSqlConnection conn, string collectionName, string[] restrictions)
        => IsAsync ? await conn.GetSchemaAsync(collectionName, restrictions, TestContext.Current.CancellationToken) : conn.GetSchema(collectionName, restrictions);
    // ReSharper restore MethodHasAsyncOverload
}

public sealed class SchemaTests_Sync() : SchemaTests(SyncOrAsync.Sync);
public sealed class SchemaTests_Async() : SchemaTests(SyncOrAsync.Async);
