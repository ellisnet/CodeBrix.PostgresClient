using System;
using System.Data;
using System.Threading.Tasks;
using CodeBrix.PostgresClient.PgSqlTypes;
using CodeBrix.PostgresClient.PostgresTypes;
using SilverAssertions;
using Xunit;
using static CodeBrix.PostgresClient.Tests.TestUtil;

namespace CodeBrix.PostgresClient.Tests; //was previously: Npgsql.Tests;

public class CommandBuilderTests : TestBase
{
    // See function parameter derivation tests in FunctionTests, and stored procedure derivation tests in StoredProcedureTests

    // Tests parameter derivation for parameterized queries (CommandType.Text)
    [Fact]
    public async Task DeriveParameters_text_one_parameter_with_same_type()
    {
        using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "id int, val text");

        var cmd = new PgSqlCommand(
            $@"INSERT INTO {table} VALUES(:x, 'some value');
                    UPDATE {table} SET val = 'changed value' WHERE id = :x;
                    SELECT val FROM {table} WHERE id = :x;",
            conn);
        PgSqlCommandBuilder.DeriveParameters(cmd);
        cmd.Parameters.Should().HaveCount(1);
        cmd.Parameters[0].Direction.Should().Be(ParameterDirection.Input);
        cmd.Parameters[0].ParameterName.Should().Be("x");
        cmd.Parameters[0].PgSqlDbType.Should().Be(PgSqlDbType.Integer);
        cmd.Parameters[0].Value = 42;
        var retVal = await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken);
        retVal.Should().Be("changed value");
    }

    // Tests parameter derivation for parameterized queries (CommandType.Text) where different types would be inferred for placeholders with the same name.
    [Fact]
    public async Task DeriveParameters_text_one_parameter_with_different_types()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "id int, val text");

        var cmd = new PgSqlCommand(
            $@"INSERT INTO {table} VALUES(:x, 'some value');
                    UPDATE {table} SET val = 'changed value' WHERE id = :x::double precision;
                    SELECT val FROM {table} WHERE id = :x::numeric;",
            conn);
        //Assert
        var ex = Assert.Throws<PgSqlException>(() => PgSqlCommandBuilder.DeriveParameters(cmd));
        ex.Message.Should().Be("The backend parser inferred different types for parameters with the same name. Please try explicit casting within your SQL statement or batch or use different placeholder names.");
    }

    // Tests parameter derivation for parameterized queries (CommandType.Text) with multiple parameters
    [Fact]
    public async Task DeriveParameters_multiple_parameters()
    {
        using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "id int, val text");

        var cmd = new PgSqlCommand(
            $@"INSERT INTO {table} VALUES(:x, 'some value');
                    UPDATE {table} SET val = 'changed value' WHERE id = @y::double precision;
                    SELECT val FROM {table} WHERE id = :z::numeric;",
            conn);
        PgSqlCommandBuilder.DeriveParameters(cmd);
        cmd.Parameters.Should().HaveCount(3);
        cmd.Parameters[0].ParameterName.Should().Be("x");
        cmd.Parameters[1].ParameterName.Should().Be("y");
        cmd.Parameters[2].ParameterName.Should().Be("z");
        cmd.Parameters[0].PgSqlDbType.Should().Be(PgSqlDbType.Integer);
        cmd.Parameters[1].PgSqlDbType.Should().Be(PgSqlDbType.Double);
        cmd.Parameters[2].PgSqlDbType.Should().Be(PgSqlDbType.Numeric);

        cmd.Parameters[0].Value = 42;
        cmd.Parameters[1].Value = 42d;
        cmd.Parameters[2].Value = 42;
        var retVal = await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken);
        retVal.Should().Be("changed value");
    }

    // Tests parameter derivation a parameterized query (CommandType.Text) that is already prepared.
    [Fact]
    public async Task DeriveParameters_text_prepared_statement()
    {
        //Arrange
        const string query = "SELECT @p::integer";
        const int answer = 42;
        await using var dataSource = CreateDataSource();
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using var cmd = new PgSqlCommand(query, conn);
        cmd.Parameters.AddWithValue("@p", PgSqlDbType.Integer, answer);
        //Act
        cmd.Prepare();
        //Assert
        conn.Connector.PreparedStatementManager.NumPrepared.Should().Be(1);

        var ex = Assert.Throws<PgSqlException>(() =>
        {
            // Derive parameters for the already prepared statement
            PgSqlCommandBuilder.DeriveParameters(cmd);

        });

        ex.Message.Should().Be("Deriving parameters isn't supported for commands that are already prepared.");

        // We leave the command intact when throwing so it should still be useable
        cmd.Parameters.Count.Should().Be(1);
        cmd.Parameters[0].ParameterName.Should().Be("@p");
        conn.Connector.PreparedStatementManager.NumPrepared.Should().Be(1);
        cmd.Parameters["@p"].Value = answer;
        (await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken)).Should().Be(answer);
    }

    // Tests parameter derivation for array parameters in parameterized queries (CommandType.Text)
    [Fact]
    public async Task DeriveParameters_text_array()
    {
        using var conn = await OpenConnectionAsync();
        var cmd = new PgSqlCommand("SELECT :a::integer[]", conn);
        var val = new[] { 7, 42 };

        PgSqlCommandBuilder.DeriveParameters(cmd);
        cmd.Parameters.Should().HaveCount(1);
        cmd.Parameters[0].ParameterName.Should().Be("a");
        cmd.Parameters[0].PgSqlDbType.Should().Be(PgSqlDbType.Integer | PgSqlDbType.Array);
        cmd.Parameters[0].Value = val;
        using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SingleResult | CommandBehavior.SingleRow, cancellationToken: TestContext.Current.CancellationToken);
        reader.Read().Should().BeTrue();
        reader.GetFieldValue<int[]>(0).Should().Equal(val);
    }

    // Tests parameter derivation for domain parameters in parameterized queries (CommandType.Text)
    [Fact]
    public async Task DeriveParameters_text_domain()
    {
        using var conn = await OpenConnectionAsync();
        MinimumPgVersion(conn, "11.0", "Arrays of domains and domains over arrays were introduced in PostgreSQL 11");
        var domainType = await GetTempTypeName(conn);
        var domainArrayType = await GetTempTypeName(conn);
        await conn.ExecuteNonQueryAsync($@"
CREATE DOMAIN {domainType} AS integer CHECK (VALUE > 0);
CREATE DOMAIN {domainArrayType} AS int[] CHECK(array_length(VALUE, 1) = 2);", cancellationToken: TestContext.Current.CancellationToken);
        conn.ReloadTypes();

        var cmd = new PgSqlCommand($"SELECT :a::{domainType}, :b::{domainType}[], :c::{domainArrayType}", conn);
        var val = 23;
        var arrayVal = new[] { 7, 42 };

        PgSqlCommandBuilder.DeriveParameters(cmd);
        cmd.Parameters.Should().HaveCount(3);
        cmd.Parameters[0].ParameterName.Should().Be("a");
        cmd.Parameters[0].PgSqlDbType.Should().Be(PgSqlDbType.Integer);
        cmd.Parameters[0].DataTypeName.Should().EndWith(domainType);
        cmd.Parameters[1].ParameterName.Should().Be("b");
        cmd.Parameters[1].PgSqlDbType.Should().Be(PgSqlDbType.Integer | PgSqlDbType.Array);
        cmd.Parameters[1].DataTypeName.Should().EndWith(domainType + "[]");
        cmd.Parameters[2].ParameterName.Should().Be("c");
        cmd.Parameters[2].PgSqlDbType.Should().Be(PgSqlDbType.Integer | PgSqlDbType.Array);
        cmd.Parameters[2].DataTypeName.Should().EndWith(domainArrayType);
        cmd.Parameters[0].Value = val;
        cmd.Parameters[1].Value = arrayVal;
        cmd.Parameters[2].Value = arrayVal;
        using var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        reader.Read();
        reader.GetFieldValue<int>(0).Should().Be(val);
        reader.GetFieldValue<int[]>(1).Should().Equal(arrayVal);
        reader.GetFieldValue<int[]>(2).Should().Equal(arrayVal);
    }

    // Tests parameter derivation for unmapped enum parameters in parameterized queries (CommandType.Text)
    [Fact]
    public async Task DeriveParameters_text_unmapped_enum()
    {
        using var conn = await OpenConnectionAsync();
        var type = await GetTempTypeName(conn);
        await conn.ExecuteNonQueryAsync($@"CREATE TYPE {type} AS ENUM ('Apple', 'Cherry', 'Plum')", cancellationToken: TestContext.Current.CancellationToken);
        conn.ReloadTypes();

        var cmd = new PgSqlCommand($"SELECT :x::{type}", conn);
        const string val1 = "Apple";
        var val2 = new string[] { "Cherry", "Plum" };

        PgSqlCommandBuilder.DeriveParameters(cmd);
        cmd.Parameters.Should().HaveCount(1);
        cmd.Parameters[0].ParameterName.Should().Be("x");
        cmd.Parameters[0].PgSqlDbType.Should().Be(PgSqlDbType.Unknown);
        cmd.Parameters[0].PostgresType.Should().BeAssignableTo<PostgresEnumType>();
        cmd.Parameters[0].PostgresType.Name.Should().Be(type);
        cmd.Parameters[0].DataTypeName.Should().EndWith(type);
        cmd.Parameters[0].Value = val1;
        using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SingleResult | CommandBehavior.SingleRow, cancellationToken: TestContext.Current.CancellationToken);
        reader.Read().Should().BeTrue();
        reader.GetString(0).Should().Be(val1);
    }

    enum Fruit { Apple, Cherry, Plum }

    // Tests parameter derivation for mapped enum parameters in parameterized queries (CommandType.Text)
    [Fact]
    public async Task DeriveParameters_text_mapped_enum()
    {
        await using var adminConnection = await OpenConnectionAsync();
        var type = await GetTempTypeName(adminConnection);
        await adminConnection.ExecuteNonQueryAsync($@"CREATE TYPE {type} AS ENUM ('apple', 'cherry', 'plum')", cancellationToken: TestContext.Current.CancellationToken);

        var dataSourceBuilder = CreateDataSourceBuilder();
        dataSourceBuilder.MapEnum<Fruit>(type);
        await using var dataSource = dataSourceBuilder.Build();
        await using var connection = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        var cmd = new PgSqlCommand($"SELECT :x::{type}, :y::{type}[]", connection);
        const Fruit val1 = Fruit.Apple;
        var val2 = new[] { Fruit.Cherry, Fruit.Plum };

        PgSqlCommandBuilder.DeriveParameters(cmd);
        cmd.Parameters.Should().HaveCount(2);
        cmd.Parameters[0].ParameterName.Should().Be("x");
        cmd.Parameters[0].PgSqlDbType.Should().Be(PgSqlDbType.Unknown);
        cmd.Parameters[0].PostgresType.Should().BeAssignableTo<PostgresEnumType>();
        cmd.Parameters[0].DataTypeName.Should().EndWith(type);
        cmd.Parameters[1].ParameterName.Should().Be("y");
        cmd.Parameters[1].PgSqlDbType.Should().Be(PgSqlDbType.Unknown);
        cmd.Parameters[1].PostgresType.Should().BeAssignableTo<PostgresArrayType>();
        cmd.Parameters[1].DataTypeName.Should().EndWith(type + "[]");
        cmd.Parameters[0].Value = val1;
        cmd.Parameters[1].Value = val2;
        using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SingleResult | CommandBehavior.SingleRow, cancellationToken: TestContext.Current.CancellationToken);
        reader.Read().Should().BeTrue();
        reader.GetFieldValue<Fruit>(0).Should().Be(val1);
        reader.GetFieldValue<Fruit[]>(1).Should().Equal(val2);
    }

    class SomeComposite
    {
        public int X { get; set; }

        [PgName("some_text")]
        public string SomeText { get; set; } = "";
    }

    [Fact]
    public async Task DeriveParameters_text_mapped_composite()
    {
        await using var adminConnection = await OpenConnectionAsync();
        var type = await GetTempTypeName(adminConnection);

        await adminConnection.ExecuteNonQueryAsync($"CREATE TYPE {type} AS (x int, some_text text)", cancellationToken: TestContext.Current.CancellationToken);

        var dataSourceBuilder = CreateDataSourceBuilder();
        dataSourceBuilder.MapComposite<SomeComposite>(type);
        await using var dataSource = dataSourceBuilder.Build();
        await using var connection = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        var expected1 = new SomeComposite { X = 8, SomeText = "foo" };
        var expected2 = new[] { expected1, new SomeComposite {X = 9, SomeText = "bar"} };

        await using var cmd = new PgSqlCommand($"SELECT @p1::{type}, @p2::{type}[]", connection);
        PgSqlCommandBuilder.DeriveParameters(cmd);
        cmd.Parameters.Should().HaveCount(2);
        cmd.Parameters[0].ParameterName.Should().Be("p1");
        cmd.Parameters[0].PgSqlDbType.Should().Be(PgSqlDbType.Unknown);
        cmd.Parameters[0].PostgresType.Should().BeAssignableTo<PostgresCompositeType>();
        cmd.Parameters[0].DataTypeName.Should().EndWith(type);
        var p1Fields = ((PostgresCompositeType)cmd.Parameters[0].PostgresType).Fields;
        p1Fields[0].Name.Should().Be("x");
        p1Fields[1].Name.Should().Be("some_text");

        cmd.Parameters[1].ParameterName.Should().Be("p2");
        cmd.Parameters[1].PgSqlDbType.Should().Be(PgSqlDbType.Unknown);
        cmd.Parameters[1].PostgresType.Should().BeAssignableTo<PostgresArrayType>();
        cmd.Parameters[1].DataTypeName.Should().EndWith(type + "[]");
        var p2Element = ((PostgresArrayType)cmd.Parameters[1].PostgresType).Element;
        p2Element.Should().BeAssignableTo<PostgresCompositeType>();
        p2Element.Name.Should().Be(type);
        var p2Fields = ((PostgresCompositeType)p2Element).Fields;
        p2Fields[0].Name.Should().Be("x");
        p2Fields[1].Name.Should().Be("some_text");

        cmd.Parameters[0].Value = expected1;
        cmd.Parameters[1].Value = expected2;
        using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SingleResult | CommandBehavior.SingleRow, cancellationToken: TestContext.Current.CancellationToken);
        reader.Read().Should().BeTrue();
        reader.GetFieldValue<SomeComposite>(0).SomeText.Should().Be(expected1.SomeText);
        reader.GetFieldValue<SomeComposite>(0).X.Should().Be(expected1.X);
        for (var i = 0; i < 2; i++)
        {
            reader.GetFieldValue<SomeComposite[]>(1)[i].SomeText.Should().Be(expected2[i].SomeText);
            reader.GetFieldValue<SomeComposite[]>(1)[i].X.Should().Be(expected2[i].X);
        }
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/1591")]
    public async Task get_update_command_infers_parameters_with_PgSqlDbType()
    {
        using var conn = await OpenConnectionAsync();
        var table = await GetTempTableName(conn);
        await conn.ExecuteNonQueryAsync($@"
CREATE TABLE {table} (
    Cod varchar(5) NOT NULL,
    Descr varchar(40),
    Data date,
    DataOra timestamp,
    Intero smallInt NOT NULL,
    Decimale money,
    Singolo float,
    Booleano bit,
    Nota varchar(255),
    BigIntArr bigint[],
    VarCharArr character varying(20)[],
    PRIMARY KEY (Cod)
);
INSERT INTO {table} VALUES('key1', 'description', '2018-07-03', '2018-07-03 07:02:00', 123, 123.4, 1234.5, B'1', 'note')", cancellationToken: TestContext.Current.CancellationToken);

        var daDataAdapter =
            new PgSqlDataAdapter(
                $"SELECT Cod, Descr, Data, DataOra, Intero, Decimale, Singolo, Booleano, Nota, BigIntArr, VarCharArr FROM {table}", conn);

        var cbCommandBuilder = new PgSqlCommandBuilder(daDataAdapter);
        var dtTable = new DataTable();

        daDataAdapter.InsertCommand = cbCommandBuilder.GetInsertCommand();
        daDataAdapter.UpdateCommand = cbCommandBuilder.GetUpdateCommand();
        daDataAdapter.DeleteCommand = cbCommandBuilder.GetDeleteCommand();

        daDataAdapter.UpdateCommand.Parameters[0].PgSqlDbType.Should().Be(PgSqlDbType.Varchar);
        daDataAdapter.UpdateCommand.Parameters[1].PgSqlDbType.Should().Be(PgSqlDbType.Varchar);
        daDataAdapter.UpdateCommand.Parameters[2].PgSqlDbType.Should().Be(PgSqlDbType.Date);
        daDataAdapter.UpdateCommand.Parameters[3].PgSqlDbType.Should().Be(PgSqlDbType.Timestamp);
        daDataAdapter.UpdateCommand.Parameters[4].PgSqlDbType.Should().Be(PgSqlDbType.Smallint);
        daDataAdapter.UpdateCommand.Parameters[5].PgSqlDbType.Should().Be(PgSqlDbType.Money);
        daDataAdapter.UpdateCommand.Parameters[6].PgSqlDbType.Should().Be(PgSqlDbType.Double);
        daDataAdapter.UpdateCommand.Parameters[7].PgSqlDbType.Should().Be(PgSqlDbType.Bit);
        daDataAdapter.UpdateCommand.Parameters[8].PgSqlDbType.Should().Be(PgSqlDbType.Varchar);
        daDataAdapter.UpdateCommand.Parameters[9].PgSqlDbType.Should().Be(PgSqlDbType.Array | PgSqlDbType.Bigint);
        daDataAdapter.UpdateCommand.Parameters[10].PgSqlDbType.Should().Be(PgSqlDbType.Array | PgSqlDbType.Varchar);

        daDataAdapter.UpdateCommand.Parameters[11].PgSqlDbType.Should().Be(PgSqlDbType.Varchar);
        daDataAdapter.UpdateCommand.Parameters[13].PgSqlDbType.Should().Be(PgSqlDbType.Varchar);
        daDataAdapter.UpdateCommand.Parameters[15].PgSqlDbType.Should().Be(PgSqlDbType.Date);
        daDataAdapter.UpdateCommand.Parameters[17].PgSqlDbType.Should().Be(PgSqlDbType.Timestamp);
        daDataAdapter.UpdateCommand.Parameters[18].PgSqlDbType.Should().Be(PgSqlDbType.Smallint);
        daDataAdapter.UpdateCommand.Parameters[20].PgSqlDbType.Should().Be(PgSqlDbType.Money);
        daDataAdapter.UpdateCommand.Parameters[22].PgSqlDbType.Should().Be(PgSqlDbType.Double);
        daDataAdapter.UpdateCommand.Parameters[24].PgSqlDbType.Should().Be(PgSqlDbType.Bit);
        daDataAdapter.UpdateCommand.Parameters[26].PgSqlDbType.Should().Be(PgSqlDbType.Varchar);
        daDataAdapter.UpdateCommand.Parameters[28].PgSqlDbType.Should().Be(PgSqlDbType.Array | PgSqlDbType.Bigint);
        daDataAdapter.UpdateCommand.Parameters[30].PgSqlDbType.Should().Be(PgSqlDbType.Array | PgSqlDbType.Varchar);

        daDataAdapter.Fill(dtTable);

        var row = dtTable.Rows[0];

        row[0].Should().Be("key1");
        row[1].Should().Be("description");
        row[2].Should().Be(new DateOnly(2018, 7, 3));
        row[3].Should().Be(new DateTime(2018, 7, 3, 7, 2, 0));
        row[4].Should().Be((short)123);
        row[5].Should().Be(123.4m);
        row[6].Should().Be(1234.5);
        row[7].Should().Be(true);
        row[8].Should().Be("note");

        dtTable.Rows[0]["Singolo"] = 1.1D;

        daDataAdapter.Update(dtTable).Should().Be(1);
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/2560")]
    public async Task get_update_command_with_column_aliases()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "Cod varchar(5) PRIMARY KEY, Descr varchar(40), Data date");
        using var cmd = new PgSqlCommand($"SELECT Cod as CodAlias, Descr as DescrAlias, Data as DataAlias FROM {table}", conn);
        using var daDataAdapter = new PgSqlDataAdapter(cmd);
        using var cbCommandBuilder = new PgSqlCommandBuilder(daDataAdapter);

        //Act
        daDataAdapter.UpdateCommand = cbCommandBuilder.GetUpdateCommand();
        //Assert
        daDataAdapter.UpdateCommand.CommandText.Should().Contain("SET \"cod\" = @p1, \"descr\" = @p2, \"data\" = @p3 WHERE ((\"cod\" = @p4) AND ((@p5 = 1 AND \"descr\" IS NULL) OR (\"descr\" = @p6)) AND ((@p7 = 1 AND \"data\" IS NULL) OR (\"data\" = @p8)))");
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/2846")]
    public async Task get_update_command_with_array_column_type()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "Cod varchar(5) PRIMARY KEY, Vettore character varying(20)[]");
        using var daDataAdapter = new PgSqlDataAdapter($"SELECT cod, vettore FROM {table} ORDER By cod", conn);
        using var cbCommandBuilder = new PgSqlCommandBuilder(daDataAdapter);
        var dtTable = new DataTable();

        cbCommandBuilder.SetAllValues = true;

        daDataAdapter.UpdateCommand = cbCommandBuilder.GetUpdateCommand();

        daDataAdapter.Fill(dtTable);
        dtTable.Rows.Add();
        dtTable.Rows[0]["cod"] = '0';
        dtTable.Rows[0]["vettore"] = new[] { "aaa", "bbb" };

        //Act
        daDataAdapter.Update(dtTable);
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/6240")]
    public async Task get_update_command_with_domain_column_type()
    {
        //Arrange
        await using var adminConnection = await OpenConnectionAsync();
        var domainTypeName = await GetTempTypeName(adminConnection);

        await adminConnection.ExecuteNonQueryAsync($"CREATE DOMAIN {domainTypeName} AS smallint", cancellationToken: TestContext.Current.CancellationToken);

        var tableName = await CreateTempTable(adminConnection, $"id serial PRIMARY KEY, domtest {domainTypeName}");

        await using var dataSource = CreateDataSource();
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        using var adapter = new PgSqlDataAdapter($"select * from {tableName}", conn);

        var builder = new PgSqlCommandBuilder(adapter)
        {
            ConflictOption = ConflictOption.CompareAllSearchableValues,
            SetAllValues = true
        };

        adapter.InsertCommand = builder.GetInsertCommand();
        adapter.UpdateCommand = builder.GetUpdateCommand();
        adapter.DeleteCommand = builder.GetDeleteCommand();

        using var dataTable = new DataTable();

        adapter.Fill(dataTable);

        const short sval = 5;

        var newRow = dataTable.NewRow();
        newRow[1] = sval;
        dataTable.Rows.Add(newRow);

        //Act
        adapter.Update(dataTable);
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/6240")]
    public async Task fill_datatable_with_array_column_type()
    {
        //Arrange
        await using var connection = await OpenConnectionAsync();

        var tableName = await CreateTempTable(connection, "id serial PRIMARY KEY, textarr text[] COLLATE pg_catalog.\"default\"");

        using var adapter = new PgSqlDataAdapter($"select * from {tableName}", connection);

        using var dataTable = new DataTable();

        adapter.FillSchema(dataTable, SchemaType.Source);

        adapter.MissingSchemaAction = MissingSchemaAction.Ignore;

        //Act
        adapter.Fill(dataTable);
    }
}
