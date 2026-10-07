using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.PostgresClient.PostgresTypes;
using CodeBrix.PostgresClient.Tests.Support;
using SilverAssertions;
using Xunit;
using static CodeBrix.PostgresClient.Tests.TestUtil;

namespace CodeBrix.PostgresClient.Tests; //was previously: Npgsql.Tests;

/// <summary>
/// This tests the new CoreCLR schema/metadata API, which returns ReadOnlyCollection&lt;DbColumn&gt;.
/// Note that this API is also available on .NET Framework.
/// For the old DataTable-based API, see <see cref="ReaderOldSchemaTests"/>.
/// </summary>
public abstract class ReaderNewSchemaTests(SyncOrAsync syncOrAsync) : SyncOrAsyncTestBase(syncOrAsync)
{
    // ReSharper disable once InconsistentNaming
    [Fact]
    public async Task allow_db_null()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "nullable INTEGER, non_nullable INTEGER NOT NULL");

        using var cmd = new PgSqlCommand($"SELECT nullable,non_nullable,8 FROM {table}", conn);
        using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SchemaOnly | CommandBehavior.KeyInfo, cancellationToken: TestContext.Current.CancellationToken);

        //Act
        var columns = await GetColumnSchema(reader);

        //Assert
        columns[0].AllowDBNull.Should().BeTrue();
        columns[1].AllowDBNull.Should().BeFalse();
        columns[2].AllowDBNull.Should().BeNull();
    }

    [Fact]
    public async Task BaseCatalogName()
    {
        //Arrange
        var dbName = new PgSqlConnectionStringBuilder(ConnectionString).Database;
        using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "foo INTEGER");

        using var cmd = new PgSqlCommand($"SELECT foo,8 FROM {table}", conn);
        using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SchemaOnly, cancellationToken: TestContext.Current.CancellationToken);

        //Act
        var columns = await GetColumnSchema(reader);

        //Assert
        columns[0].BaseCatalogName.Should().Be(dbName);
        columns[1].BaseCatalogName.Should().Be(dbName);
    }

    [Fact]
    public async Task BaseColumnName()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "foo INTEGER");

        using var cmd = new PgSqlCommand($"SELECT foo, foo AS foobar, 8 AS bar, 8, '8'::VARCHAR(10) FROM {table}", conn);
        await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SchemaOnly | CommandBehavior.KeyInfo, cancellationToken: TestContext.Current.CancellationToken);

        //Act
        var columns = await GetColumnSchema(reader);

        //Assert
        columns[0].BaseColumnName.Should().Be("foo");
        columns[1].BaseColumnName.Should().Be("foo");
        columns[2].BaseColumnName.Should().BeNull();
        columns[3].BaseColumnName.Should().BeNull();
        columns[4].BaseColumnName.Should().BeNull();
    }

    [Fact]
    public async Task BaseColumnName_with_column_aliases()
    {
        //Arrange
        using var conn = OpenConnection();

        conn.ExecuteNonQuery(@"
                CREATE TEMP TABLE data (
                    Cod varchar(5) NOT NULL,
                    Descr varchar(40),
                    Date date,
                    CONSTRAINT PK_test_Cod PRIMARY KEY (Cod)
                );
            ");

        var cmd = new PgSqlCommand("SELECT Cod as CodAlias, Descr as DescrAlias, Date, NULL AS Generated FROM data", conn);

        using var dr = cmd.ExecuteReader(CommandBehavior.SchemaOnly | CommandBehavior.KeyInfo);

        //Act
        var cols = await GetColumnSchema(dr);

        //Assert
        cols[0].BaseColumnName.Should().Be("cod");
        cols[0].ColumnName.Should().Be("codalias");
        cols[0].IsAliased.Should().BeTrue();

        cols[1].BaseColumnName.Should().Be("descr");
        cols[1].ColumnName.Should().Be("descralias");
        cols[1].IsAliased.Should().BeTrue();

        cols[2].BaseColumnName.Should().Be("date");
        cols[2].ColumnName.Should().Be("date");
        cols[2].IsAliased.Should().BeFalse();

        cols[3].BaseColumnName.Should().BeNull();
        cols[3].ColumnName.Should().Be("generated");
        cols[3].IsAliased.Should().BeNull();
    }

    [Fact]
    public async Task BaseSchemaName()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "foo INTEGER");

        using var cmd = new PgSqlCommand($"SELECT foo,8 FROM {table}", conn);
        using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SchemaOnly | CommandBehavior.KeyInfo, cancellationToken: TestContext.Current.CancellationToken);

        //Act
        var columns = await GetColumnSchema(reader);

        //Assert
        columns[0].BaseSchemaName.Should().Be("public");
        columns[1].BaseSchemaName.Should().BeNull();
    }

    [Fact]
    public async Task BaseServerName()
    {
        //Arrange
        var host = new PgSqlConnectionStringBuilder(ConnectionString).Host;
        using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "foo INTEGER");

        using var cmd = new PgSqlCommand($"SELECT foo,8 FROM {table}", conn);
        using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SchemaOnly, cancellationToken: TestContext.Current.CancellationToken);

        //Act
        var columns = await GetColumnSchema(reader);

        //Assert
        columns[0].BaseServerName.Should().Be(host);
        columns[1].BaseServerName.Should().Be(host);
    }

    [Fact]
    public async Task BaseTableName()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "foo INTEGER");

        using var cmd = new PgSqlCommand($"SELECT foo,8 FROM {table}", conn);
        using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SchemaOnly | CommandBehavior.KeyInfo, cancellationToken: TestContext.Current.CancellationToken);

        //Act
        var columns = await GetColumnSchema(reader);

        //Assert
        columns[0].BaseTableName.Should().StartWith("temp_table");
        columns[1].BaseTableName.Should().BeNull();
    }

    [Fact]
    public async Task ColumnName()
    {
        await using (var conn = await OpenConnectionAsync())
        {
            var table = await CreateTempTable(conn, "foo INTEGER");

            using var cmd = new PgSqlCommand($"SELECT foo, foo AS foobar, 8 AS bar, 8, '8'::VARCHAR(10) FROM {table}", conn);
            using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SchemaOnly, cancellationToken: TestContext.Current.CancellationToken);

            var columns = await GetColumnSchema(reader);
            columns[0].ColumnName.Should().Be("foo");
            columns[1].ColumnName.Should().Be("foobar");
            columns[2].ColumnName.Should().Be("bar");
            columns[3].ColumnName.Should().Be("?column?");
            columns[4].ColumnName.Should().Be("varchar");
        }

        // See https://github.com/npgsql/npgsql/issues/1676
        using (var conn = await OpenConnectionAsync())
        {
            var table = await CreateTempTable(conn, "col TEXT");

            var behavior = CommandBehavior.SchemaOnly | CommandBehavior.KeyInfo;
            //var behavior = CommandBehavior.SchemaOnly;
            using var command = new PgSqlCommand($"SELECT col AS col_alias FROM {table}", conn);
            using var reader = command.ExecuteReader(behavior);
            var columns = await GetColumnSchema(reader);
            columns[0].ColumnName.Should().Be("col_alias");
        }
    }

    [Fact]
    public async Task ColumnOrdinal()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "first INTEGER, second INTEGER");

        using var cmd = new PgSqlCommand($"SELECT second,first FROM {table}", conn);
        using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SchemaOnly, cancellationToken: TestContext.Current.CancellationToken);

        //Act
        var columns = await GetColumnSchema(reader);

        //Assert
        columns[0].ColumnName.Should().Be("second");
        columns[0].ColumnOrdinal.Should().Be(0);
        columns[1].ColumnName.Should().Be("first");
        columns[1].ColumnOrdinal.Should().Be(1);
    }

    [Fact]
    public async Task ColumnAttributeNumber()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "first INTEGER, second INTEGER");

        using var cmd = new PgSqlCommand($"SELECT second,first FROM {table}", conn);
        using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SchemaOnly | CommandBehavior.KeyInfo, cancellationToken: TestContext.Current.CancellationToken);

        //Act
        var columns = await GetColumnSchema(reader);

        //Assert
        columns[0].ColumnName.Should().Be("second");
        columns[0].ColumnAttributeNumber.Should().Be(2);
        columns[1].ColumnName.Should().Be("first");
        columns[1].ColumnAttributeNumber.Should().Be(1);
    }

    [Fact]
    public async Task ColumnSize()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        await IgnoreOnRedshift(conn, "Column size is never unlimited on Redshift");
        var table = await CreateTempTable(conn, "bounded VARCHAR(30), unbounded VARCHAR");

        using var cmd = new PgSqlCommand($"SELECT bounded,unbounded,'a'::VARCHAR(10),'b'::VARCHAR FROM {table}", conn);
        using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SchemaOnly, cancellationToken: TestContext.Current.CancellationToken);

        //Act
        var columns = await GetColumnSchema(reader);

        //Assert
        columns[0].ColumnSize.Should().Be(30);
        columns[1].ColumnSize.Should().BeNull();
        columns[2].ColumnSize.Should().Be(10);
        columns[3].ColumnSize.Should().BeNull();
    }

    [Fact]
    public async Task IsAutoIncrement()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        await IgnoreOnRedshift(conn, "Serial columns not support on Redshift");

        var table = await CreateTempTable(conn, "serial SERIAL, int INT");

        await using var cmd = new PgSqlCommand($"SELECT serial, int, 8 FROM {table}", conn);
        await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SchemaOnly | CommandBehavior.KeyInfo, cancellationToken: TestContext.Current.CancellationToken);

        //Act
        var columns = await GetColumnSchema(reader);

        //Assert
        columns[0].IsAutoIncrement.Should().BeTrue("Serial not identified as autoincrement");
        columns[1].IsAutoIncrement.Should().BeFalse("Regular int column identified as autoincrement");
        columns[2].IsAutoIncrement.Should().BeNull("Literal int identified as autoincrement");
    }

    [Fact]
    public async Task IsAutoIncrement_identity()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        await IgnoreOnRedshift(conn, "Identity columns not support on Redshift");
        MinimumPgVersion(conn, "10.0", "IDENTITY introduced in PostgreSQL 10");

        var table =
            await CreateTempTable(conn, "identity1 INT GENERATED BY DEFAULT AS IDENTITY, identity2 INT GENERATED ALWAYS AS IDENTITY");

        await using var cmd = new PgSqlCommand($"SELECT identity1, identity2 FROM {table}", conn);
        await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SchemaOnly | CommandBehavior.KeyInfo, cancellationToken: TestContext.Current.CancellationToken);

        //Act
        var columns = await GetColumnSchema(reader);

        //Assert
        columns[0].IsAutoIncrement.Should().BeTrue("PG 10 IDENTITY not identified as autoincrement");
        columns[1].IsAutoIncrement.Should().BeTrue("PG 10 IDENTITY not identified as autoincrement");
    }

    [Fact]
    public async Task IsIdentity()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        await IgnoreOnRedshift(conn, "Identity columns not support on Redshift");
        MinimumPgVersion(conn, "10.0", "IDENTITY introduced in PostgreSQL 10");
        var table = await CreateTempTable(
            conn,
            "identity1 INT GENERATED BY DEFAULT AS IDENTITY, identity2 INT GENERATED ALWAYS AS IDENTITY, serial SERIAL, int INT");

        await using var cmd = new PgSqlCommand($"SELECT identity1, identity2, serial, int, 8 FROM {table}", conn);
        await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SchemaOnly | CommandBehavior.KeyInfo, cancellationToken: TestContext.Current.CancellationToken);

        //Act
        var columns = await GetColumnSchema(reader);

        //Assert
        columns[0].IsIdentity.Should().BeTrue("PG 10 IDENTITY not identified as identity");
        columns[1].IsIdentity.Should().BeTrue("PG 10 IDENTITY not identified as identity");
        columns[2].IsIdentity.Should().BeFalse("Serial identified as identity");
        columns[3].IsIdentity.Should().BeFalse("Regular int column identified as identity");
        columns[4].IsIdentity.Should().BeFalse("Literal int identified as identity");
    }

    [Fact]
    public async Task IsKey()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        await IgnoreOnRedshift(conn, "Key not supported in reader schema on Redshift");
        var table = await CreateTempTable(conn, "id INT PRIMARY KEY, non_id INT, uniq INT UNIQUE");

        using var cmd = new PgSqlCommand($"SELECT id,non_id,uniq,8 FROM {table}", conn);
        using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SchemaOnly | CommandBehavior.KeyInfo, cancellationToken: TestContext.Current.CancellationToken);

        //Act
        var columns = await GetColumnSchema(reader);

        //Assert
        columns[0].IsKey.Should().BeTrue();
        columns[1].IsKey.Should().BeFalse();

        // Note: according to the old API docs any unique column is considered key.
        // https://msdn.microsoft.com/en-us/library/system.data.sqlclient.sqldatareader.getschematable(v=vs.110).aspx
        // But in the new API we have a separate IsUnique so IsKey should be false
        columns[2].IsKey.Should().BeFalse();

        columns[3].IsKey.Should().BeNull();
    }

    [Fact]
    public async Task IsKey_composite()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        await IgnoreOnRedshift(conn, "Key not supported in reader schema on Redshift");
        var table = await CreateTempTable(conn, "id1 INT, id2 INT, PRIMARY KEY (id1, id2)");

        using var cmd = new PgSqlCommand($"SELECT id1,id2 FROM {table}", conn);
        using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SchemaOnly | CommandBehavior.KeyInfo, cancellationToken: TestContext.Current.CancellationToken);

        //Act
        var columns = await GetColumnSchema(reader);

        //Assert
        columns[0].IsKey.Should().BeTrue();
        columns[1].IsKey.Should().BeTrue();
    }

    [Fact]
    public async Task IsLong()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        await IgnoreOnRedshift(conn, "bytea not supported on Redshift");
        var table = await CreateTempTable(conn, "long BYTEA, non_long INT");

        using var cmd = new PgSqlCommand($"SELECT long, non_long, 8 FROM {table}", conn);
        using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SchemaOnly, cancellationToken: TestContext.Current.CancellationToken);

        //Act
        var columns = await GetColumnSchema(reader);

        //Assert
        columns[0].IsLong.Should().BeTrue();
        columns[1].IsLong.Should().BeFalse();
        columns[2].IsLong.Should().BeFalse();
    }

    [Fact]
    public async Task IsReadOnly_on_view()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        var view = await GetTempViewName(conn);
        var table = await GetTempTableName(conn);

        await conn.ExecuteNonQueryAsync($@"
CREATE VIEW {view} AS SELECT 8 AS foo;
CREATE TABLE {table} (bar INTEGER)", cancellationToken: TestContext.Current.CancellationToken);

        using var cmd = new PgSqlCommand($"SELECT foo,bar FROM {view},{table}", conn);
        using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SchemaOnly | CommandBehavior.KeyInfo, cancellationToken: TestContext.Current.CancellationToken);

        //Act
        var columns = await GetColumnSchema(reader);

        //Assert
        columns[0].IsReadOnly.Should().BeTrue();
        columns[1].IsReadOnly.Should().BeFalse();
    }

    [Fact]
    public async Task IsReadOnly_on_non_column()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        using var cmd = new PgSqlCommand("SELECT 8", conn);
        using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SchemaOnly, cancellationToken: TestContext.Current.CancellationToken);

        //Act
        var columns = await GetColumnSchema(reader);

        //Assert
        columns.Single().IsReadOnly.Should().BeTrue();
    }

    [Fact]
    public async Task IsUnique()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        await IgnoreOnRedshift(conn, "Unique not supported in reader schema on Redshift");
        var table = await GetTempTableName(conn);

        await conn.ExecuteNonQueryAsync($@"
CREATE TABLE {table} (id INT PRIMARY KEY, non_id INT, uniq INT UNIQUE, non_id_second INT, non_id_third INT);
CREATE UNIQUE INDEX idx_{table} ON {table} (non_id_second, non_id_third)", cancellationToken: TestContext.Current.CancellationToken);

        using var cmd = new PgSqlCommand($"SELECT id,non_id,uniq,8,non_id_second,non_id_third FROM {table}", conn);
        using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SchemaOnly | CommandBehavior.KeyInfo, cancellationToken: TestContext.Current.CancellationToken);

        //Act
        var columns = await GetColumnSchema(reader);

        //Assert
        columns[0].IsUnique.Should().BeTrue();
        columns[1].IsUnique.Should().BeFalse();
        columns[2].IsUnique.Should().BeTrue();
        columns[3].IsUnique.Should().BeNull();
        columns[4].IsUnique.Should().BeFalse();
        columns[5].IsUnique.Should().BeFalse();
    }

    [Fact]
    public async Task NumericPrecision()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        await IgnoreOnRedshift(conn, "Precision is never unlimited on Redshift");
        var table = await CreateTempTable(conn, "a NUMERIC(8), b NUMERIC, c INTEGER");

        using var cmd = new PgSqlCommand($"SELECT a,b,c,8.3::NUMERIC(8) FROM {table}", conn);
        using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SchemaOnly, cancellationToken: TestContext.Current.CancellationToken);

        //Act
        var columns = await GetColumnSchema(reader);

        //Assert
        columns[0].NumericPrecision.Should().Be(8);
        columns[1].NumericPrecision.Should().BeNull();
        columns[2].NumericPrecision.Should().BeNull();
        columns[3].NumericPrecision.Should().Be(8);
    }

    [Fact]
    public async Task NumericScale()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        await IgnoreOnRedshift(conn, "Scale is never unlimited on Redshift");
        var table = await CreateTempTable(conn, "a NUMERIC(8,5), b NUMERIC, c INTEGER");

        using var cmd = new PgSqlCommand($"SELECT a,b,c,8.3::NUMERIC(8,5) FROM {table}", conn);
        using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SchemaOnly, cancellationToken: TestContext.Current.CancellationToken);

        //Act
        var columns = await GetColumnSchema(reader);

        //Assert
        columns[0].NumericScale.Should().Be(5);
        columns[1].NumericScale.Should().BeNull();
        columns[2].NumericScale.Should().BeNull();
        columns[3].NumericScale.Should().Be(5);
    }

    [Fact]
    public async Task DataType()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "foo INTEGER");

        using var cmd = new PgSqlCommand($"SELECT foo,8::INTEGER FROM {table}", conn);
        using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SchemaOnly, cancellationToken: TestContext.Current.CancellationToken);

        //Act
        var columns = await GetColumnSchema(reader);

        //Assert
        columns[0].DataType.Should().BeSameAs(typeof(int));
        columns[1].DataType.Should().BeSameAs(typeof(int));
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/1305")]
    public async Task DataType_unknown_type()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "foo INTEGER");

        using var cmd = new PgSqlCommand($"SELECT foo::INTEGER FROM {table}", conn);
        cmd.AllResultTypesAreUnknown = true;
        using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SchemaOnly, cancellationToken: TestContext.Current.CancellationToken);

        //Act
        var columns = await GetColumnSchema(reader);

        //Assert
        columns[0].DataType.Should().BeSameAs(typeof(int));
    }

    [Fact]
    public async Task DataType_with_composite()
    {
        //Arrange
        await using var adminConnection = await OpenConnectionAsync();
        await IgnoreOnRedshift(adminConnection, "Composite types not support on Redshift");
        var type = await GetTempTypeName(adminConnection);
        await adminConnection.ExecuteNonQueryAsync($"CREATE TYPE {type} AS (foo int)", cancellationToken: TestContext.Current.CancellationToken);
        var tableName = await CreateTempTable(adminConnection, $"comp {type}");

        var dataSourceBuilder = CreateDataSourceBuilder();
        dataSourceBuilder.MapComposite<SomeComposite>(type);
        await using var dataSource = dataSourceBuilder.Build();
        await using var connection = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        await using var cmd = new PgSqlCommand($"SELECT comp,'(4)'::{type} FROM {tableName}", connection);
        await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SchemaOnly, cancellationToken: TestContext.Current.CancellationToken);

        //Act
        var columns = await GetColumnSchema(reader);

        //Assert
        columns[0].DataType.Should().BeSameAs(typeof(SomeComposite));
        columns[0].UdtAssemblyQualifiedName.Should().Be(typeof(SomeComposite).AssemblyQualifiedName);
        columns[1].DataType.Should().BeSameAs(typeof(SomeComposite));
        columns[1].UdtAssemblyQualifiedName.Should().Be(typeof(SomeComposite).AssemblyQualifiedName);
    }

    [Fact]
    public async Task DataType_with_array()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "foo INTEGER[]");

        using var cmd = new PgSqlCommand($"SELECT foo, ARRAY[1::INTEGER, 2::INTEGER] FROM {table}", conn);
        using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SchemaOnly, cancellationToken: TestContext.Current.CancellationToken);

        //Act
        var columns = await GetColumnSchema(reader);

        //Assert
        columns[0].DataType.Should().BeSameAs(typeof(Array));
        columns[1].DataType.Should().BeSameAs(typeof(Array));
    }

    [Fact]
    public async Task UdtAssemblyQualifiedName()
    {
        //Arrange
        // Also see DataTypeWithComposite
        using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "foo INTEGER");

        using var cmd = new PgSqlCommand($"SELECT foo,8 FROM {table}", conn);
        using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SchemaOnly, cancellationToken: TestContext.Current.CancellationToken);

        //Act
        var columns = await GetColumnSchema(reader);

        //Assert
        columns[0].UdtAssemblyQualifiedName.Should().BeNull();
        columns[1].UdtAssemblyQualifiedName.Should().BeNull();
    }

    [Fact]
    public async Task PostgresType()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "foo INTEGER");

        using var cmd = new PgSqlCommand($"SELECT foo,8 FROM {table}", conn);
        using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SchemaOnly, cancellationToken: TestContext.Current.CancellationToken);
        var columns = await GetColumnSchema(reader);

        //Act
        var intType = columns[0].PostgresType;

        //Assert
        columns[1].PostgresType.Should().BeSameAs(intType);
        intType.Name.Should().Be("integer");
        intType.InternalName.Should().Be("int4");
    }

    [Fact]
    public async Task column_schema_with_and_without_KeyInfo()
    {
        await using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "foo INTEGER");

        using var cmd = new PgSqlCommand($"SELECT foo, foo AS foobar, 8 AS bar, 8, '8'::VARCHAR(10) FROM {table}", conn);
        await using (var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken))
        {
            var columns = await GetColumnSchema(reader);

            columns[0].ColumnName.Should().Be("foo");
            columns[0].BaseColumnName.Should().BeNull();
            columns[0].BaseTableName.Should().BeNull();
            columns[0].BaseSchemaName.Should().BeNull();
            columns[0].IsAliased.Should().BeNull();
            columns[0].IsKey.Should().BeNull();
            columns[0].IsUnique.Should().BeNull();
            columns[1].ColumnName.Should().Be("foobar");
            columns[1].BaseColumnName.Should().BeNull();
            columns[1].BaseTableName.Should().BeNull();
            columns[1].BaseSchemaName.Should().BeNull();
            columns[1].IsAliased.Should().BeNull();
            columns[1].IsKey.Should().BeNull();
            columns[1].IsUnique.Should().BeNull();
            columns[2].ColumnName.Should().Be("bar");
            columns[2].BaseColumnName.Should().BeNull();
            columns[2].BaseTableName.Should().BeNull();
            columns[2].BaseSchemaName.Should().BeNull();
            columns[2].IsAliased.Should().BeNull();
            columns[2].IsKey.Should().BeNull();
            columns[2].IsUnique.Should().BeNull();
            columns[3].ColumnName.Should().Be("?column?");
            columns[3].BaseColumnName.Should().BeNull();
            columns[3].BaseTableName.Should().BeNull();
            columns[3].BaseSchemaName.Should().BeNull();
            columns[3].IsAliased.Should().BeNull();
            columns[3].IsKey.Should().BeNull();
            columns[3].IsUnique.Should().BeNull();
            columns[4].ColumnName.Should().Be("varchar");
            columns[4].BaseColumnName.Should().BeNull();
            columns[4].BaseTableName.Should().BeNull();
            columns[4].BaseSchemaName.Should().BeNull();
            columns[4].IsAliased.Should().BeNull();
            columns[4].IsKey.Should().BeNull();
            columns[4].IsUnique.Should().BeNull();

        }

        await using (var readerInfo = await cmd.ExecuteReaderAsync(CommandBehavior.KeyInfo, cancellationToken: TestContext.Current.CancellationToken))
        {
            var columnsInfo = await GetColumnSchema(readerInfo);

            columnsInfo[0].ColumnName.Should().Be("foo");
            columnsInfo[0].BaseColumnName.Should().Be("foo");
            columnsInfo[0].BaseSchemaName.Should().Be("public");
            columnsInfo[0].IsAliased.Should().Be(false);
            columnsInfo[0].IsKey.Should().Be(false);
            columnsInfo[0].IsUnique.Should().Be(false);
            columnsInfo[1].ColumnName.Should().Be("foobar");
            columnsInfo[1].BaseColumnName.Should().Be("foo");
            columnsInfo[1].BaseSchemaName.Should().Be("public");
            columnsInfo[1].IsAliased.Should().Be(true);
            columnsInfo[1].IsKey.Should().Be(false);
            columnsInfo[1].IsUnique.Should().Be(false);
            columnsInfo[2].ColumnName.Should().Be("bar");
            columnsInfo[2].BaseColumnName.Should().BeNull();
            columnsInfo[2].BaseSchemaName.Should().BeNull();
            columnsInfo[2].IsAliased.Should().BeNull();
            columnsInfo[2].IsKey.Should().BeNull();
            columnsInfo[2].IsUnique.Should().BeNull();
            columnsInfo[3].ColumnName.Should().Be("?column?");
            columnsInfo[3].BaseColumnName.Should().BeNull();
            columnsInfo[3].BaseSchemaName.Should().BeNull();
            columnsInfo[3].IsAliased.Should().BeNull();
            columnsInfo[3].IsKey.Should().BeNull();
            columnsInfo[3].IsUnique.Should().BeNull();
            columnsInfo[4].ColumnName.Should().Be("varchar");
            columnsInfo[4].BaseColumnName.Should().BeNull();
            columnsInfo[4].BaseSchemaName.Should().BeNull();
            columnsInfo[4].IsAliased.Should().BeNull();
            columnsInfo[4].IsKey.Should().BeNull();
            columnsInfo[4].IsUnique.Should().BeNull();
        }
    }

    /// <seealso cref="ReaderTests.GetDataTypeName"/>
    [Theory]
    [InlineData("integer")]
    [InlineData("real")]
    [InlineData("integer[]")]
    [InlineData("character varying(10)", 10)]
    [InlineData("character varying")]
    [InlineData("character varying(10)[]", 10)]
    [InlineData("character(10)", 10)]
    [InlineData("character", 1)]
    [InlineData("character(1)", 1)]
    [InlineData("numeric(1000, 2)", null, 1000, 2)]
    [InlineData("numeric(1000)", null, 1000, null)]
    [InlineData("numeric")]
    [InlineData("timestamp without time zone")]
    [InlineData("timestamp(2) without time zone", null, 2)]
    [InlineData("timestamp(2) with time zone", null, 2)]
    [InlineData("time without time zone")]
    [InlineData("time(2) without time zone", null, 2)]
    [InlineData("time(2) with time zone", null, 2)]
    [InlineData("interval")]
    [InlineData("interval(2)", null, 2)]
    [InlineData("bit", 1)]
    [InlineData("bit(3)", 3)]
    [InlineData("bit varying")]
    [InlineData("bit varying(3)", 3)]
    public async Task DataTypeName(string typeName, int? size = null, int? precision = null, int? scale = null)
    {
        //Arrange
        var openingParen = typeName.IndexOf('(');
        var typeNameWithoutFacets = openingParen == -1
            ? typeName
            : typeName.Substring(0, openingParen) + typeName.Substring(typeName.IndexOf(')') + 1);

        using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, $"foo {typeName}");

        using var cmd = new PgSqlCommand($"SELECT foo,NULL::{typeName} FROM {table}", conn);
        using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SchemaOnly | CommandBehavior.KeyInfo, cancellationToken: TestContext.Current.CancellationToken);
        var columns = await GetColumnSchema(reader);
        var tableColumn = columns[0];

        //Act
        var nonTableColumn = columns[1];

        //Assert
        tableColumn.DataTypeName.Should().Be(typeNameWithoutFacets);
        tableColumn.ColumnSize.Should().Be(size);
        tableColumn.NumericPrecision.Should().Be(precision);
        tableColumn.NumericScale.Should().Be(scale);
        nonTableColumn.DataTypeName.Should().Be(typeNameWithoutFacets);
        nonTableColumn.ColumnSize.Should().Be(size);
        nonTableColumn.NumericPrecision.Should().Be(precision);
        nonTableColumn.NumericScale.Should().Be(scale);
    }

    [Fact]
    public async Task DefaultValue()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "with_default INTEGER DEFAULT(8), without_default INTEGER");

        using var cmd = new PgSqlCommand($"SELECT with_default,without_default,8 FROM {table}", conn);
        using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SchemaOnly | CommandBehavior.KeyInfo, cancellationToken: TestContext.Current.CancellationToken);

        //Act
        var columns = await GetColumnSchema(reader);

        //Assert
        columns[0].DefaultValue.Should().Be("8");
        columns[1].DefaultValue.Should().BeNull();
        columns[2].DefaultValue.Should().BeNull();
    }

    [Fact]
    public async Task same_column_name()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        var table1 = await GetTempTableName(conn);
        var table2 = await GetTempTableName(conn);

        await conn.ExecuteNonQueryAsync($@"
CREATE TABLE {table1} (foo INTEGER);
CREATE TABLE {table2} (foo INTEGER)", cancellationToken: TestContext.Current.CancellationToken);

        using var cmd = new PgSqlCommand($"SELECT {table1}.foo,{table2}.foo FROM {table1},{table2}", conn);
        using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SchemaOnly | CommandBehavior.KeyInfo, cancellationToken: TestContext.Current.CancellationToken);

        //Act
        var columns = await GetColumnSchema(reader);

        //Assert
        columns[0].ColumnName.Should().Be("foo");
        columns[0].BaseTableName.Should().StartWith("temp_table");
        columns[1].ColumnName.Should().Be("foo");
        columns[1].BaseTableName.Should().StartWith("temp_table");
        columns[0].BaseTableName.Should().NotBe(columns[1].BaseTableName);
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/1553")]
    public async Task domain_type()
    {
        //Arrange
        // if (IsMultiplexing)
        //     Assert.Ignore("Multiplexing: ReloadTypes");
        using var conn = await OpenConnectionAsync();
        await IgnoreOnRedshift(conn, "Domain types not support on Redshift");

        const string domainTypeName = "my_domain";
        var schema = await CreateTempSchema(conn);
        var tableName = await GetTempTableName(conn);
        await conn.ExecuteNonQueryAsync($"CREATE DOMAIN {schema}.{domainTypeName} AS varchar(2)", cancellationToken: TestContext.Current.CancellationToken);
        conn.ReloadTypes();
        await conn.ExecuteNonQueryAsync($"CREATE TABLE {tableName} (domain {schema}.{domainTypeName})", cancellationToken: TestContext.Current.CancellationToken);
        using var cmd = new PgSqlCommand($"SELECT domain FROM {tableName}", conn);
        using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SchemaOnly | CommandBehavior.KeyInfo, cancellationToken: TestContext.Current.CancellationToken);
        var columns = await GetColumnSchema(reader);

        //Act
        var domainSchema = columns.Single(c => c.ColumnName == "domain");

        //Assert
        domainSchema.ColumnSize.Should().Be(2);
        var pgType = domainSchema.PostgresType;
        pgType.Should().BeAssignableTo<PostgresDomainType>();
        ((PostgresDomainType)pgType).BaseType.Name.Should().Be("character varying");
        // For domains we should return the underlying type
        domainSchema.PgSqlDbType.Should().Be(CodeBrix.PostgresClient.PgSqlTypes.PgSqlDbType.Varchar);
    }

    [Fact]
    public async Task PgSqlDbType()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "foo INTEGER");

        using var cmd = new PgSqlCommand($"SELECT foo,8::INTEGER FROM {table}", conn);
        using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SchemaOnly, cancellationToken: TestContext.Current.CancellationToken);

        //Act
        var columns = await GetColumnSchema(reader);

        //Assert
        columns[0].PgSqlDbType.Should().Be(CodeBrix.PostgresClient.PgSqlTypes.PgSqlDbType.Integer);
        columns[1].PgSqlDbType.Should().Be(CodeBrix.PostgresClient.PgSqlTypes.PgSqlDbType.Integer);
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/1950")]
    public async Task no_resultset()
    {
        using var conn = await OpenConnectionAsync();
        using var cmd = new PgSqlCommand("COMMIT", conn);
        using var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        reader.Read();
        await GetColumnSchema(reader);
    }

    [Fact]
    public async Task IsAliased()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "foo INTEGER");

        using var cmd = new PgSqlCommand($"SELECT foo, foo AS bar, NULL AS foobar FROM {table}", conn);
        await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SchemaOnly | CommandBehavior.KeyInfo, cancellationToken: TestContext.Current.CancellationToken);

        //Act
        var columns = await GetColumnSchema(reader);

        //Assert
        columns[0].IsAliased.Should().BeFalse();
        columns[1].IsAliased.Should().BeTrue();
        columns[2].IsAliased.Should().BeNull();
    }

    [Fact] // #4672
    public async Task with_parameter_without_value()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "foo INTEGER");

        using var cmd = new PgSqlCommand($"SELECT foo FROM {table} WHERE foo > @p", conn)
        {
            Parameters = { new() { ParameterName = "p", PgSqlDbType = CodeBrix.PostgresClient.PgSqlTypes.PgSqlDbType.Integer } }
        };
        await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SchemaOnly | CommandBehavior.KeyInfo, cancellationToken: TestContext.Current.CancellationToken);

        //Act
        var columns = await GetColumnSchema(reader);

        //Assert
        columns[0].ColumnName.Should().Be("foo");
    }

    [Fact]
    public async Task GetColumnSchema_via_interface()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "foo INTEGER");

        using var cmd = new PgSqlCommand($"SELECT foo FROM {table} WHERE foo > @p", conn)
        {
            Parameters = { new() { ParameterName = "p", PgSqlDbType = CodeBrix.PostgresClient.PgSqlTypes.PgSqlDbType.Integer } }
        };
        await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SchemaOnly | CommandBehavior.KeyInfo, cancellationToken: TestContext.Current.CancellationToken);

        var iface = (IDbColumnSchemaGenerator)reader;

        //Act
        var schema = iface.GetColumnSchema();

        //Assert
        schema.Should().NotBeNull();
        schema.Count.Should().Be(1);
        schema[0].Should().NotBeNull();
    }

    #region Not supported

    [Fact]
    public async Task IsExpression()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "foo INTEGER");

        using var cmd = new PgSqlCommand($"SELECT * FROM {table}", conn);

        //Act
        using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SchemaOnly, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        reader.GetColumnSchema().Single().IsExpression.Should().BeFalse();
    }

    [Fact]
    public async Task IsHidden()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "foo INTEGER");

        using var cmd = new PgSqlCommand($"SELECT * FROM {table}", conn);

        //Act
        using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SchemaOnly, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        reader.GetColumnSchema().Single().IsHidden.Should().BeFalse();
    }

    #endregion

    class SomeComposite
    {
        public int Foo { get; set; }
    }

    async Task<IReadOnlyList<Schema.PgSqlDbColumn>> GetColumnSchema(PgSqlDataReader reader)
        => IsAsync ? (await reader.GetColumnSchemaAsync(TestContext.Current.CancellationToken)).Cast<Schema.PgSqlDbColumn>().ToArray() : reader.GetColumnSchema();
}

public sealed class ReaderNewSchemaTests_Sync() : ReaderNewSchemaTests(SyncOrAsync.Sync);
public sealed class ReaderNewSchemaTests_Async() : ReaderNewSchemaTests(SyncOrAsync.Async);

[Collection(NonParallelCollection.Name)]
public abstract class ReaderNewSchemaTestsNonParallel(SyncOrAsync syncOrAsync) : SyncOrAsyncTestBase(syncOrAsync)
{
    [Fact]
    public async Task PgSqlDbType_extension()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        await EnsureExtensionAsync(conn, "hstore", "9.1");

        using var cmd = new PgSqlCommand("SELECT NULL::HSTORE", conn);
        using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SchemaOnly, cancellationToken: TestContext.Current.CancellationToken);

        //Act
        var columns = await GetColumnSchema(reader);

        //Assert
        // The full datatype name for PostGIS is public.geometry (unlike int4 which is in pg_catalog).
        columns[0].PgSqlDbType.Should().Be(CodeBrix.PostgresClient.PgSqlTypes.PgSqlDbType.Hstore);
    }

    async Task<IReadOnlyList<Schema.PgSqlDbColumn>> GetColumnSchema(PgSqlDataReader reader)
        => IsAsync ? (await reader.GetColumnSchemaAsync(TestContext.Current.CancellationToken)).Cast<Schema.PgSqlDbColumn>().ToArray() : reader.GetColumnSchema();
}

public sealed class ReaderNewSchemaTestsNonParallel_Sync() : ReaderNewSchemaTestsNonParallel(SyncOrAsync.Sync);
public sealed class ReaderNewSchemaTestsNonParallel_Async() : ReaderNewSchemaTestsNonParallel(SyncOrAsync.Async);
