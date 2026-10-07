using System;
using System.Data;
using System.Threading.Tasks;
using CodeBrix.PostgresClient.PgSqlTypes;
using SilverAssertions;
using Xunit;
using static CodeBrix.PostgresClient.Tests.TestUtil;

namespace CodeBrix.PostgresClient.Tests; //was previously: Npgsql.Tests;

public class DataAdapterTests : TestBase
{
    [Fact]
    public async Task data_adapter_SelectCommand()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        using var command = new PgSqlCommand("SELECT 1", conn);
        var da = new PgSqlDataAdapter();
        da.SelectCommand = command;
        var ds = new DataSet();
        //Act
        da.Fill(ds);
        //ds.WriteXml("TestUseDataAdapter.xml");
    }

    [Fact]
    public async Task data_adapter_PgSqlCommand_in_constructor()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        using var command = new PgSqlCommand("SELECT 1", conn);
        command.Connection = conn;
        var da = new PgSqlDataAdapter(command);
        var ds = new DataSet();
        //Act
        da.Fill(ds);
        //ds.WriteXml("TestUseDataAdapterPgSqlConnectionConstructor.xml");
    }

    [Fact]
    public async Task data_adapter_string_command_in_constructor()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        var da = new PgSqlDataAdapter("SELECT 1", conn);
        var ds = new DataSet();
        //Act
        da.Fill(ds);
        //ds.WriteXml("TestUseDataAdapterStringPgSqlConnectionConstructor.xml");
    }

    [Fact]
    public void data_adapter_connection_string_in_constructor()
    {
        //Arrange
        var da = new PgSqlDataAdapter("SELECT 1", ConnectionString);
        var ds = new DataSet();
        //Act
        da.Fill(ds);
        //ds.WriteXml("TestUseDataAdapterStringStringConstructor.xml");
    }

    [Fact]
    public async Task insert_with_DataSet()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        var table = await SetupTempTable(conn);
        var ds = new DataSet();
        var da = new PgSqlDataAdapter($"SELECT * FROM {table}", conn);

        da.InsertCommand = new PgSqlCommand($"INSERT INTO {table} (field_int2, field_timestamp, field_numeric) VALUES (:a, :b, :c)", conn);

        da.InsertCommand.Parameters.Add(new PgSqlParameter("a", DbType.Int16));
        da.InsertCommand.Parameters.Add(new PgSqlParameter("b", DbType.DateTime2));
        da.InsertCommand.Parameters.Add(new PgSqlParameter("c", DbType.Decimal));

        da.InsertCommand.Parameters[0].Direction = ParameterDirection.Input;
        da.InsertCommand.Parameters[1].Direction = ParameterDirection.Input;
        da.InsertCommand.Parameters[2].Direction = ParameterDirection.Input;

        da.InsertCommand.Parameters[0].SourceColumn = "field_int2";
        da.InsertCommand.Parameters[1].SourceColumn = "field_timestamp";
        da.InsertCommand.Parameters[2].SourceColumn = "field_numeric";

        da.Fill(ds);

        var dt = ds.Tables[0];
        var dr = dt.NewRow();
        dr["field_int2"] = 4;
        dr["field_timestamp"] = new DateTime(2003, 01, 30, 14, 0, 0);
        dr["field_numeric"] = 7.3M;
        dt.Rows.Add(dr);

        var ds2 = ds.GetChanges();
        da.Update(ds2);

        ds.Merge(ds2);
        ds.AcceptChanges();

        //Act
        var dr2 = new PgSqlCommand($"SELECT field_int2, field_numeric, field_timestamp FROM {table}", conn).ExecuteReader();
        dr2.Read();

        //Assert
        dr2[0].Should().Be((short)4);
        dr2[1].Should().Be(7.3000000M);
        dr2.Close();
    }

    [Fact]
    public async Task data_adapter_update_return_value()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        var table = await SetupTempTable(conn);
        var ds = new DataSet();
        var da = new PgSqlDataAdapter($"SELECT * FROM {table}", conn);

        da.InsertCommand = new PgSqlCommand($@"INSERT INTO {table} (field_int2, field_timestamp, field_numeric) VALUES (:a, :b, :c)", conn);

        da.InsertCommand.Parameters.Add(new PgSqlParameter("a", DbType.Int16));
        da.InsertCommand.Parameters.Add(new PgSqlParameter("b", DbType.DateTime2));
        da.InsertCommand.Parameters.Add(new PgSqlParameter("c", DbType.Decimal));

        da.InsertCommand.Parameters[0].Direction = ParameterDirection.Input;
        da.InsertCommand.Parameters[1].Direction = ParameterDirection.Input;
        da.InsertCommand.Parameters[2].Direction = ParameterDirection.Input;

        da.InsertCommand.Parameters[0].SourceColumn = "field_int2";
        da.InsertCommand.Parameters[1].SourceColumn = "field_timestamp";
        da.InsertCommand.Parameters[2].SourceColumn = "field_numeric";

        da.Fill(ds);

        var dt = ds.Tables[0];
        var dr = dt.NewRow();
        dr["field_int2"] = 4;
        dr["field_timestamp"] = new DateTime(2003, 01, 30, 14, 0, 0);
        dr["field_numeric"] = 7.3M;
        dt.Rows.Add(dr);

        dr = dt.NewRow();
        dr["field_int2"] = 4;
        dr["field_timestamp"] = new DateTime(2003, 01, 30, 14, 0, 0);
        dr["field_numeric"] = 7.3M;
        dt.Rows.Add(dr);

        var ds2 = ds.GetChanges();
        //Act
        var daupdate = da.Update(ds2);

        //Assert
        daupdate.Should().Be(2);
    }

    [Fact]
    public async Task data_adapter_update_return_value2()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        var table = await SetupTempTable(conn);

        var cmd = conn.CreateCommand();
        var da = new PgSqlDataAdapter($"select * from {table}", conn);
        var cb = new PgSqlCommandBuilder(da);
        var ds = new DataSet();
        da.Fill(ds);

        //## Insert a new row with id = 1
        ds.Tables[0].Rows.Add(0.4, 0.5);
        da.Update(ds);

        //## change id from 1 to 2
        cmd.CommandText = $"update {table} set field_numeric = 0.8";
        cmd.ExecuteNonQuery();

        //## change value to newvalue
        ds.Tables[0].Rows[0][1] = 0.7;
        //Act
        //## update should fail, and make a DBConcurrencyException
        var count = da.Update(ds);
        //Assert
        //## count is 1, even if the row isn't updated in the database
        count.Should().Be(1);
    }

    [Fact]
    public async Task Fill_with_empty_resultset()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        var table = await SetupTempTable(conn);

        var ds = new DataSet();
        var da = new PgSqlDataAdapter($"SELECT field_serial, field_int2, field_timestamp, field_numeric FROM {table} WHERE field_serial = -1", conn);

        //Act
        da.Fill(ds);

        //Assert
        ds.Tables.Count.Should().Be(1);
        ds.Tables[0].Columns.Count.Should().Be(4);
        ds.Tables[0].Columns[0].ColumnName.Should().Be("field_serial");
        ds.Tables[0].Columns[1].ColumnName.Should().Be("field_int2");
        ds.Tables[0].Columns[2].ColumnName.Should().Be("field_timestamp");
        ds.Tables[0].Columns[3].ColumnName.Should().Be("field_numeric");
    }

    [Fact]
    public async Task Fill_add_with_key()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        var table = await SetupTempTable(conn);

        var ds = new DataSet();
        var da = new PgSqlDataAdapter($"select field_serial, field_int2, field_timestamp, field_numeric from {table}", conn);

        da.MissingSchemaAction = MissingSchemaAction.AddWithKey;
        da.Fill(ds);

        var field_serial = ds.Tables[0].Columns[0];
        var field_int2 = ds.Tables[0].Columns[1];
        var field_timestamp = ds.Tables[0].Columns[2];
        var field_numeric = ds.Tables[0].Columns[3];

        //Assert
        field_serial.AllowDBNull.Should().BeFalse();
        field_serial.AutoIncrement.Should().BeTrue();
        field_serial.ColumnName.Should().Be("field_serial");
        field_serial.DataType.Should().Be(typeof(int));
        field_serial.Ordinal.Should().Be(0);
        field_serial.Unique.Should().BeFalse();

        field_int2.AllowDBNull.Should().BeTrue();
        field_int2.AutoIncrement.Should().BeFalse();
        field_int2.ColumnName.Should().Be("field_int2");
        field_int2.DataType.Should().Be(typeof(short));
        field_int2.Ordinal.Should().Be(1);
        field_int2.Unique.Should().BeFalse();

        field_timestamp.AllowDBNull.Should().BeTrue();
        field_timestamp.AutoIncrement.Should().BeFalse();
        field_timestamp.ColumnName.Should().Be("field_timestamp");
        field_timestamp.DataType.Should().Be(typeof(DateTime));
        field_timestamp.Ordinal.Should().Be(2);
        field_timestamp.Unique.Should().BeFalse();

        field_numeric.AllowDBNull.Should().BeTrue();
        field_numeric.AutoIncrement.Should().BeFalse();
        field_numeric.ColumnName.Should().Be("field_numeric");
        field_numeric.DataType.Should().Be(typeof(decimal));
        field_numeric.Ordinal.Should().Be(3);
        field_numeric.Unique.Should().BeFalse();
    }

    [Fact]
    public async Task Fill_add_columns()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        var table = await SetupTempTable(conn);

        var ds = new DataSet();
        var da = new PgSqlDataAdapter($"SELECT field_serial, field_int2, field_timestamp, field_numeric FROM {table}", conn);

        da.MissingSchemaAction = MissingSchemaAction.Add;
        da.Fill(ds);

        var field_serial = ds.Tables[0].Columns[0];
        var field_int2 = ds.Tables[0].Columns[1];
        var field_timestamp = ds.Tables[0].Columns[2];
        var field_numeric = ds.Tables[0].Columns[3];

        //Assert
        field_serial.ColumnName.Should().Be("field_serial");
        field_serial.DataType.Should().Be(typeof(int));
        field_serial.Ordinal.Should().Be(0);

        field_int2.ColumnName.Should().Be("field_int2");
        field_int2.DataType.Should().Be(typeof(short));
        field_int2.Ordinal.Should().Be(1);

        field_timestamp.ColumnName.Should().Be("field_timestamp");
        field_timestamp.DataType.Should().Be(typeof(DateTime));
        field_timestamp.Ordinal.Should().Be(2);

        field_numeric.ColumnName.Should().Be("field_numeric");
        field_numeric.DataType.Should().Be(typeof(decimal));
        field_numeric.Ordinal.Should().Be(3);
    }

    [Fact]
    public async Task Update_letting_null_field_falue()
    {
        using var conn = await OpenConnectionAsync();
        var table = await SetupTempTable(conn);

        var command = new PgSqlCommand($"INSERT INTO {table} (field_int2) VALUES (2)", conn);
        command.ExecuteNonQuery();

        var ds = new DataSet();

        var da = new PgSqlDataAdapter($"SELECT * FROM {table}", conn);
        da.InsertCommand = new PgSqlCommand(";", conn);
        da.UpdateCommand = new PgSqlCommand($"UPDATE {table} SET field_int2 = :a, field_timestamp = :b, field_numeric = :c WHERE field_serial = :d", conn);

        da.UpdateCommand.Parameters.Add(new PgSqlParameter("a", DbType.Int16));
        da.UpdateCommand.Parameters.Add(new PgSqlParameter("b", DbType.DateTime));
        da.UpdateCommand.Parameters.Add(new PgSqlParameter("c", DbType.Decimal));
        da.UpdateCommand.Parameters.Add(new PgSqlParameter("d", PgSqlDbType.Bigint));

        da.UpdateCommand.Parameters[0].Direction = ParameterDirection.Input;
        da.UpdateCommand.Parameters[1].Direction = ParameterDirection.Input;
        da.UpdateCommand.Parameters[2].Direction = ParameterDirection.Input;
        da.UpdateCommand.Parameters[3].Direction = ParameterDirection.Input;

        da.UpdateCommand.Parameters[0].SourceColumn = "field_int2";
        da.UpdateCommand.Parameters[1].SourceColumn = "field_timestamp";
        da.UpdateCommand.Parameters[2].SourceColumn = "field_numeric";
        da.UpdateCommand.Parameters[3].SourceColumn = "field_serial";

        da.Fill(ds);

        var dt = ds.Tables[0];
        dt.Should().NotBeNull();

        var dr = ds.Tables[0].Rows[^1];
        dr["field_int2"] = 4;

        var ds2 = ds.GetChanges();
        da.Update(ds2);
        ds.Merge(ds2);
        ds.AcceptChanges();

        using var dr2 = new PgSqlCommand($"SELECT field_int2 FROM {table}", conn).ExecuteReader();
        dr2.Read();
        dr2["field_int2"].Should().Be((short)4);
    }

    [Fact]
    public async Task Fill_with_duplicate_column_name()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        var table = await SetupTempTable(conn);

        var ds = new DataSet();
        var da = new PgSqlDataAdapter($"SELECT field_serial, field_serial FROM {table}", conn);
        //Act
        da.Fill(ds);
    }

    [Fact]
    public Task Update_with_DataSet() => DoUpdateWithDataSet();

    async Task DoUpdateWithDataSet()
    {
        using var conn = await OpenConnectionAsync();
        var table = await SetupTempTable(conn);

        var command = new PgSqlCommand($"insert into {table} (field_int2) values (2)", conn);
        command.ExecuteNonQuery();

        var ds = new DataSet();
        var da = new PgSqlDataAdapter($"select * from {table}", conn);
        var cb = new PgSqlCommandBuilder(da);
        cb.Should().NotBeNull();

        da.Fill(ds);

        var dt = ds.Tables[0];
        dt.Should().NotBeNull();

        var dr = ds.Tables[0].Rows[^1];

        dr["field_int2"] = 4;

        var ds2 = ds.GetChanges();
        da.Update(ds2);
        ds.Merge(ds2);
        ds.AcceptChanges();

        using var dr2 = new PgSqlCommand($"select * from {table}", conn).ExecuteReader();
        dr2.Read();
        dr2["field_int2"].Should().Be((short)4);
    }

    [Fact]
    public async Task insert_with_CommandBuilder_case_sensitive()
    {
        using var conn = await OpenConnectionAsync();
        var table = await SetupTempTable(conn);

        var ds = new DataSet();
        var da = new PgSqlDataAdapter($"select * from {table}", conn);
        var builder = new PgSqlCommandBuilder(da);
        builder.Should().NotBeNull();

        da.Fill(ds);

        var dt = ds.Tables[0];
        var dr = dt.NewRow();
        dr["Field_int4"] = 4;
        dt.Rows.Add(dr);

        var ds2 = ds.GetChanges();
        da.Update(ds2);
        ds.Merge(ds2);
        ds.AcceptChanges();

        using var dr2 = new PgSqlCommand($"select * from {table}", conn).ExecuteReader();
        dr2.Read();
        dr2["field_int4"].Should().Be(4);
    }

    [Fact]
    public async Task interval_as_TimeSpan()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        var table = await GetTempTableName(conn);
        await conn.ExecuteNonQueryAsync($@"
CREATE TABLE {table} (
    pk SERIAL PRIMARY KEY,
    interval INTERVAL
);
INSERT INTO {table} (interval) VALUES ('1 hour'::INTERVAL);", cancellationToken: TestContext.Current.CancellationToken);

        var dt = new DataTable("data");
        var command = new PgSqlCommand
        {
            CommandType = CommandType.Text,
            CommandText = $"SELECT interval FROM {table}",
            Connection = conn
        };
        var da = new PgSqlDataAdapter { SelectCommand = command };
        //Act
        da.Fill(dt);
    }

    [Fact]
    public async Task interval_as_TimeSpan2()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        var table = await GetTempTableName(conn);
        await conn.ExecuteNonQueryAsync($@"
CREATE TABLE {table} (
    pk SERIAL PRIMARY KEY,
    interval INTERVAL
);
INSERT INTO {table} (interval) VALUES ('1 hour'::INTERVAL);", cancellationToken: TestContext.Current.CancellationToken);

        var dt = new DataTable("data");
        //DataColumn c = dt.Columns.Add("dauer", typeof(TimeSpan));
        // DataColumn c = dt.Columns.Add("dauer", typeof(PgSqlInterval));
        //c.AllowDBNull = true;
        var command = new PgSqlCommand();
        command.CommandType = CommandType.Text;
        command.CommandText = $"SELECT interval FROM {table}";
        command.Connection = conn;
        var da = new PgSqlDataAdapter();
        da.SelectCommand = command;
        //Act
        da.Fill(dt);
    }

    [Fact]
    public async Task data_adapter_command_access()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        using var command = new PgSqlCommand("SELECT CAST('1 hour' AS interval) AS dauer", conn);
        var da = new PgSqlDataAdapter();
        da.SelectCommand = command;
        System.Data.Common.DbDataAdapter common = da;
        //Assert
        common.SelectCommand.Should().NotBeNull();
    }

    // Makes sure that the INSERT/UPDATE/DELETE commands are auto-populated on PgSqlDataAdapter
    [Fact]
    [IssueLink("https://github.com/npgsql/npgsql/issues/179")]
    public async Task auto_populate_adapter_commands()
    {
        using var conn = await OpenConnectionAsync();
        var table = await SetupTempTable(conn);

        var da = new PgSqlDataAdapter($"SELECT field_pk,field_int4 FROM {table}", conn);
        var builder = new PgSqlCommandBuilder(da);
        var ds = new DataSet();
        da.Fill(ds);

        var t = ds.Tables[0];
        var row = t.NewRow();
        row["field_pk"] = 1;
        row["field_int4"] = 8;
        t.Rows.Add(row);
        da.Update(ds);
        (await conn.ExecuteScalarAsync($"SELECT field_int4 FROM {table}", cancellationToken: TestContext.Current.CancellationToken)).Should().Be(8);

        row["field_int4"] = 9;
        da.Update(ds);
        (await conn.ExecuteScalarAsync($"SELECT field_int4 FROM {table}", cancellationToken: TestContext.Current.CancellationToken)).Should().Be(9);

        row.Delete();
        da.Update(ds);
        (await conn.ExecuteScalarAsync($"SELECT COUNT(*) FROM {table}", cancellationToken: TestContext.Current.CancellationToken)).Should().Be(0L);
    }

    [Fact]
    public void command_builder_quoting()
    {
        //Arrange
        var cb = new PgSqlCommandBuilder();
        const string orig = "some\"column";
        var quoted = cb.QuoteIdentifier(orig);
        //Assert
        quoted.Should().Be("\"some\"\"column\"");
        cb.UnquoteIdentifier(quoted).Should().Be(orig);
    }

    // Makes sure a correct SQL string is built with GetUpdateCommand(true) using correct parameter names and placeholders
    [Fact]
    [IssueLink("https://github.com/npgsql/npgsql/issues/397")]
    public async Task get_UpdateCommand()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        var table = await SetupTempTable(conn);

        using var da = new PgSqlDataAdapter($"SELECT field_pk, field_int4 FROM {table}", conn);
        using var cb = new PgSqlCommandBuilder(da);
        var updateCommand = cb.GetUpdateCommand(true);
        da.UpdateCommand = updateCommand;

        var ds = new DataSet();
        da.Fill(ds);

        var t = ds.Tables[0];
        var row = t.Rows.Add();
        row["field_pk"] = 1;
        row["field_int4"] = 1;
        da.Update(ds);

        row["field_int4"] = 2;
        da.Update(ds);

        row.Delete();
        //Act
        da.Update(ds);
    }

    [Fact]
    public async Task load_DataTable()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "char5 CHAR(5), varchar5 VARCHAR(5)");
        using var command = new PgSqlCommand($"SELECT char5, varchar5 FROM {table}", conn);
        using var dr = command.ExecuteReader();
        var dt = new DataTable();
        dt.Load(dr);
        dr.Close();

        //Assert
        dt.Columns[0].MaxLength.Should().Be(5);
        dt.Columns[1].MaxLength.Should().Be(5);
    }

    static Task<string> SetupTempTable(PgSqlConnection conn)
        => CreateTempTable(conn, @"
field_pk INTEGER PRIMARY KEY GENERATED ALWAYS AS IDENTITY,
field_serial SERIAL,
field_int2 SMALLINT,
field_int4 INTEGER,
field_numeric NUMERIC,
field_timestamp TIMESTAMP");
}
