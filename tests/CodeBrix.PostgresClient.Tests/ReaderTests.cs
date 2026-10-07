using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.PostgresClient.BackendMessages;
using CodeBrix.PostgresClient.Internal;
using CodeBrix.PostgresClient.Internal.Postgres;
using CodeBrix.PostgresClient.PgSqlTypes;
using CodeBrix.PostgresClient.PostgresTypes;
using CodeBrix.PostgresClient.Tests.Support;
using CodeBrix.PostgresClient.Util;
using SilverAssertions;
using Xunit;
using static CodeBrix.PostgresClient.Tests.TestUtil;

namespace CodeBrix.PostgresClient.Tests; //was previously: Npgsql.Tests;

public abstract class ReaderTests : MultiplexingTestBase
{
    static uint Int4Oid => PostgresMinimalDatabaseInfo.DefaultTypeCatalog.GetOid(DataTypeNames.Int4).Value;
    static uint ByteaOid => PostgresMinimalDatabaseInfo.DefaultTypeCatalog.GetOid(DataTypeNames.Bytea).Value;

    [Fact]
    public async Task resumable_non_consumed_to_non_resumable()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        await using var cmd = new PgSqlCommand( "SELECT 'aaaaaaaa', 1", conn);
        await using var reader = await cmd.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken);
        await reader.ReadAsync(TestContext.Current.CancellationToken);

        await reader.IsDBNullAsync(0, cancellationToken: TestContext.Current.CancellationToken); // resumable, no consumption
        _ = reader.IsDBNull(0); // resumable, no consumption

        //Act
        await using var stream = await reader.GetStreamAsync(0, cancellationToken: TestContext.Current.CancellationToken); // non-resumable

        //Assert
        if (IsSequential)
            Assert.Throws<InvalidOperationException>(() => reader.GetString(0));
    }

    [Fact]
    public async Task seek_columns()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        using var cmd = new PgSqlCommand("SELECT 1,2,3", conn);

        //Act
        using var reader = await cmd.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        reader.Read().Should().BeTrue();
        reader.GetInt32(0).Should().Be(1);
        if (IsSequential)
            Assert.Throws<InvalidOperationException>(() => reader.GetInt32(0));
        else
            reader.GetInt32(0).Should().Be(1);
        reader.GetInt32(1).Should().Be(2);
        if (IsSequential)
            Assert.Throws<InvalidOperationException>(() => reader.GetInt32(0));
        else
            reader.GetInt32(0).Should().Be(1);
    }

    [Fact]
    public async Task no_resultset()
    {
        using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "id INT");

        using (var cmd = new PgSqlCommand($"INSERT INTO {table} VALUES (8)", conn))
        using (var reader = await cmd.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken))
        {
            Assert.Throws<InvalidOperationException>(() => reader.GetOrdinal("foo"));
            reader.Read().Should().BeFalse();
            Assert.Throws<InvalidOperationException>(() => reader.GetOrdinal("foo"));
            reader.FieldCount.Should().Be(0);
            reader.NextResult().Should().BeFalse();
            Assert.Throws<InvalidOperationException>(() => reader.GetOrdinal("foo"));
        }

        using (var cmd = new PgSqlCommand($"SELECT 1; INSERT INTO {table} VALUES (8)", conn))
        using (var reader = await cmd.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken))
        {
            await reader.NextResultAsync(TestContext.Current.CancellationToken);
            Assert.Throws<InvalidOperationException>(() => reader.GetOrdinal("foo"));
            reader.Read().Should().BeFalse();
            Assert.Throws<InvalidOperationException>(() => reader.GetOrdinal("foo"));
            reader.FieldCount.Should().Be(0);
        }
    }

    [Fact]
    public async Task empty_resultset()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        using var cmd = new PgSqlCommand("SELECT 1 AS foo WHERE FALSE", conn);

        //Act
        using var reader = await cmd.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        reader.Read().Should().BeFalse();
        reader.FieldCount.Should().Be(1);
        reader.GetOrdinal("foo").Should().Be(0);
        Assert.Throws<InvalidOperationException>(() => reader[0]);
    }

    [Fact]
    public async Task FieldCount()
    {
        using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "int INT");

        using var cmd = new PgSqlCommand("SELECT 1; SELECT 2,3", conn);
        using (var reader = await cmd.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken))
        {
            reader.FieldCount.Should().Be(1);
            reader.Read().Should().BeTrue();
            reader.FieldCount.Should().Be(1);
            reader.Read().Should().BeFalse();
            reader.FieldCount.Should().Be(1);
            reader.NextResult().Should().BeTrue();
            reader.FieldCount.Should().Be(2);
            reader.NextResult().Should().BeFalse();
            reader.FieldCount.Should().Be(0);
        }

        cmd.CommandText = $"INSERT INTO {table} (int) VALUES (1)";
        using (var reader = await cmd.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken))
        {
            // Note MSDN docs that seem to say we should case -1 in this case: https://msdn.microsoft.com/en-us/library/system.data.idatarecord.fieldcount(v=vs.110).aspx
            // But SqlClient returns 0
            reader.FieldCount.Should().Be(0);

        }
    }

    [Fact]
    public async Task RecordsAffected()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "int INT");

        var sb = new StringBuilder();
        for (var i = 0; i < 10; i++)
            sb.Append($"INSERT INTO {table} (int) VALUES ({i});");
        sb.Append("SELECT 1;"); // Testing, that on close reader consumes all rows (as insert doesn't have a result set, but select does)
        for (var i = 10; i < 15; i++)
            sb.Append($"INSERT INTO {table} (int) VALUES ({i});");
        var cmd = new PgSqlCommand(sb.ToString(), conn);
        var reader = await cmd.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken);

        //Act
        reader.Close();

        //Assert
        reader.RecordsAffected.Should().Be(15);

        cmd = new PgSqlCommand($"SELECT * FROM {table}", conn);
        reader = await cmd.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken);
        reader.Close();
        reader.RecordsAffected.Should().Be(-1);

        cmd = new PgSqlCommand($"UPDATE {table} SET int=int+1 WHERE int > 10", conn);
        reader = await cmd.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken);
        reader.Close();
        reader.RecordsAffected.Should().Be(4);

        cmd = new PgSqlCommand($"UPDATE {table} SET int=8 WHERE int=666", conn);
        reader = await cmd.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken);
        reader.Close();
        reader.RecordsAffected.Should().Be(0);

        cmd = new PgSqlCommand($"DELETE FROM {table} WHERE int > 10", conn);
        reader = await cmd.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken);
        reader.Close();
        reader.RecordsAffected.Should().Be(4);

        if (conn.PostgreSqlVersion.IsGreaterOrEqual(15))
        {
            cmd = new PgSqlCommand($"MERGE INTO {table} S USING (SELECT 2 as int) T ON T.int = S.int WHEN MATCHED THEN UPDATE SET int = S.int", conn);
            reader = await cmd.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken);
            reader.Close();
            reader.RecordsAffected.Should().Be(1);
        }
    }

    [Fact]
    public async Task statement_oid_legacy_batching()
    {
        using var conn = await OpenConnectionAsync();

        MaximumPgVersionExclusive(conn, "12.0",
            "Support for 'CREATE TABLE ... WITH OIDS' has been removed in 12.0. See https://www.postgresql.org/docs/12/release-12.html#id-1.11.6.5.4");

        var table = await GetTempTableName(conn);

        var query = $@"
CREATE TABLE {table} (name TEXT) WITH OIDS;
INSERT INTO {table} (name) VALUES ('a');
UPDATE {table} SET name='b' WHERE name='doesnt_exist';";

        using (var cmd = new PgSqlCommand(query,conn))
        {
            using var reader = await cmd.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken);

            reader.Statements[0].OID.Should().Be(0);
            reader.Statements[1].OID.Should().NotBe(0);
            reader.Statements[0].OID.Should().Be(0);
        }

        using (var cmd = new PgSqlCommand($"SELECT name FROM {table}; DELETE FROM {table}", conn))
        {
            using var reader = await cmd.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken);

            await reader.NextResultAsync(TestContext.Current.CancellationToken); // Consume SELECT result set
            reader.Statements[0].OID.Should().Be(0);
            reader.Statements[1].OID.Should().Be(0);
        }
    }

    [Fact]
    public async Task get_string_with_parameter()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "name TEXT");
        const string text = "Random text";
        await conn.ExecuteNonQueryAsync($@"INSERT INTO {table} (name) VALUES ('{text}')", cancellationToken: TestContext.Current.CancellationToken);

        var command = new PgSqlCommand($"SELECT name FROM {table} WHERE name = :value;", conn);
        var param = new PgSqlParameter
        {
            ParameterName = "value",
            DbType = DbType.String,
            Size = text.Length,
            Value = text
        };
        //param.PgSqlDbType = PgSqlDbType.Text;
        command.Parameters.Add(param);

        using var dr = await command.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken);
        dr.Read();

        //Act
        var result = dr.GetString(0);

        //Assert
        result.Should().Be(text);
    }

    [Fact]
    public async Task get_string_with_quote_with_parameter()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        var table = await GetTempTableName(conn);
        await conn.ExecuteNonQueryAsync($@"
CREATE TABLE {table} (name TEXT);
INSERT INTO {table} (name) VALUES ('Text with '' single quote');", cancellationToken: TestContext.Current.CancellationToken);

        const string test = "Text with ' single quote";
        var command = new PgSqlCommand($"SELECT name FROM {table} WHERE name = :value;", conn);

        var param = new PgSqlParameter();
        param.ParameterName = "value";
        param.DbType = DbType.String;
        //param.PgSqlDbType = PgSqlDbType.Text;
        param.Size = test.Length;
        param.Value = test;
        command.Parameters.Add(param);

        using var dr = await command.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken);
        dr.Read();

        //Act
        var result = dr.GetString(0);

        //Assert
        result.Should().Be(test);
    }

    [Fact]
    public async Task get_value_by_name()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        using var command = new PgSqlCommand(@"SELECT 'Random text' AS real_column", conn);
        using var dr = await command.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken);

        //Act
        dr.Read();

        //Assert
        dr["real_column"].Should().Be("Random text");
        Assert.Throws<IndexOutOfRangeException>(() => dr["non_existing"]);
    }

    [Fact]
    [IssueLink("https://github.com/npgsql/npgsql/issues/794")]
    public async Task GetFieldType()
    {
        using var conn = await OpenConnectionAsync();
        using (var cmd = new PgSqlCommand(@"SELECT 1::INT4 AS some_column", conn))
        using (var reader = await cmd.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken))
        {
            reader.Read();
            reader.GetFieldType(0).Should().BeSameAs(typeof(int));
        }
        using (var cmd = new PgSqlCommand(@"SELECT 1::INT4 AS some_column", conn))
        {
            cmd.AllResultTypesAreUnknown = true;
            using (var reader = await cmd.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken))
            {
                reader.Read();
                reader.GetFieldType(0).Should().BeSameAs(typeof(string));
            }
        }
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/1096")]
    public async Task GetFieldType_SchemaOnly()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        await using var cmd = new PgSqlCommand(@"SELECT 1::INT4 AS some_column", conn);
        await using var reader = await cmd.ExecuteReaderAsync(Behavior | CommandBehavior.SchemaOnly, cancellationToken: TestContext.Current.CancellationToken);

        //Act
        reader.Read();

        //Assert
        reader.GetFieldType(0).Should().BeSameAs(typeof(int));
    }

    [Fact]
    public async Task GetPostgresType()
    {
        if (IsMultiplexing)
            Assert.Skip("Multiplexing: Fails");

        using var conn = await OpenConnectionAsync();
        PostgresType intType;
        using (var cmd = new PgSqlCommand(@"SELECT 1::INTEGER AS some_column", conn))
        using (var reader = await cmd.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken))
        {
            reader.Read();
            intType = (PostgresBaseType)reader.GetPostgresType(0);
            intType.Namespace.Should().Be("pg_catalog");
            intType.Name.Should().Be("integer");
            intType.FullName.Should().Be("pg_catalog.integer");
            intType.DisplayName.Should().Be("integer");
            intType.InternalName.Should().Be("int4");
        }

        using (var cmd = new PgSqlCommand(@"SELECT '{1}'::INTEGER[] AS some_column", conn))
        using (var reader = await cmd.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken))
        {
            reader.Read();
            var intArrayType = (PostgresArrayType)reader.GetPostgresType(0);
            intArrayType.Name.Should().Be("integer[]");
            intArrayType.Element.Should().BeSameAs(intType);
            intArrayType.DisplayName.Should().Be("integer[]");
            intArrayType.InternalName.Should().Be("_int4");
            intType.Array.Should().BeSameAs(intArrayType);
        }
    }

    /// <seealso cref="ReaderNewSchemaTests.DataTypeName"/>
    [Theory, IssueLink("https://github.com/npgsql/npgsql/issues/787")]
    [InlineData("integer")]
    [InlineData("real")]
    [InlineData("integer[]")]
    [InlineData("character varying(10)")]
    [InlineData("character varying")]
    [InlineData("character varying(10)[]")]
    [InlineData("character(10)")]
    [InlineData("character")]
    [InlineData("character(1)", "character")]
    [InlineData("numeric(1000, 2)")]
    [InlineData("numeric(1000)")]
    [InlineData("numeric")]
    [InlineData("timestamp without time zone")]
    [InlineData("timestamp(2) without time zone")]
    [InlineData("timestamp(2) with time zone")]
    [InlineData("time without time zone")]
    [InlineData("time(2) without time zone")]
    [InlineData("time(2) with time zone")]
    [InlineData("interval")]
    [InlineData("interval(2)")]
    [InlineData("bit", "bit(1)")]
    [InlineData("bit(3)")]
    [InlineData("bit varying")]
    [InlineData("bit varying(3)")]
    public async Task GetDataTypeName(string typeName, string normalizedName = null)
    {
        //Arrange
        if (normalizedName == null)
            normalizedName = typeName;
        using var conn = await OpenConnectionAsync();
        using var cmd = new PgSqlCommand($"SELECT NULL::{typeName} AS some_column", conn);
        using var reader = await cmd.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken);

        //Act
        reader.Read();

        //Assert
        reader.GetDataTypeName(0).Should().Be(normalizedName);
    }

    [Fact]
    public async Task GetDataTypeName_enum()
    {
        //Arrange
        await using var dataSource = CreateDataSource(csb => csb.MaxPoolSize = 1);
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        var typeName = await GetTempTypeName(conn);
        await conn.ExecuteNonQueryAsync($"CREATE TYPE {typeName} AS ENUM ('one')", cancellationToken: TestContext.Current.CancellationToken);
        await Task.Yield(); // TODO: fix multiplexing deadlock bug
        conn.ReloadTypes();
        await using var cmd = new PgSqlCommand($"SELECT 'one'::{typeName}", conn);
        await using var reader = await cmd.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken);

        //Act
        await reader.ReadAsync(TestContext.Current.CancellationToken);

        //Assert
        reader.GetDataTypeName(0).Should().Be($"public.{typeName}");
    }

    [Fact]
    public async Task GetDataTypeName_domain()
    {
        //Arrange
        await using var dataSource = CreateDataSource(csb => csb.MaxPoolSize = 1);
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        var typeName = await GetTempTypeName(conn);
        await conn.ExecuteNonQueryAsync($"CREATE DOMAIN {typeName} AS VARCHAR(10)", cancellationToken: TestContext.Current.CancellationToken);
        await Task.Yield(); // TODO: fix multiplexing deadlock bug
        conn.ReloadTypes();
        await using var cmd = new PgSqlCommand($"SELECT 'one'::{typeName}", conn);
        await using var reader = await cmd.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken);

        //Act
        await reader.ReadAsync(TestContext.Current.CancellationToken);

        //Assert
        // In the RowDescription, PostgreSQL sends the type OID of the underlying type and not of the domain.
        reader.GetDataTypeName(0).Should().Be("character varying(10)");
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/794")]
    public async Task GetDataTypeName_types_unknown()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        using var cmd = new PgSqlCommand(@"SELECT 1::INTEGER AS some_column", conn);
        cmd.AllResultTypesAreUnknown = true;
        using var reader = await cmd.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken);

        //Act
        reader.Read();

        //Assert
        reader.GetDataTypeName(0).Should().Be("integer");
    }

    [Fact]
    [IssueLink("https://github.com/npgsql/npgsql/issues/791")]
    [IssueLink("https://github.com/npgsql/npgsql/issues/794")]
    public async Task GetDataTypeOID()
    {
        using var conn = await OpenConnectionAsync();
        var int4OID = await conn.ExecuteScalarAsync("SELECT oid FROM pg_type WHERE typname = 'int4'", cancellationToken: TestContext.Current.CancellationToken);
        using (var cmd = new PgSqlCommand(@"SELECT 1::INT4 AS some_column", conn))
        using (var reader = await cmd.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken))
        {
            reader.Read();
            reader.GetDataTypeOID(0).Should().Be((uint)int4OID);
        }
        using (var cmd = new PgSqlCommand(@"SELECT 1::INT4 AS some_column", conn))
        {
            cmd.AllResultTypesAreUnknown = true;
            using (var reader = await cmd.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken))
            {
                reader.Read();
                reader.GetDataTypeOID(0).Should().Be((uint)int4OID);
            }
        }
    }

    [Fact]
    public async Task GetName()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        using var command = new PgSqlCommand(@"SELECT 1 AS some_column", conn);
        using var dr = await command.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken);

        //Act
        dr.Read();

        //Assert
        dr.GetName(0).Should().Be("some_column");
    }

    [Fact]
    public async Task GetFieldValue_as_object()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        using var cmd = new PgSqlCommand("SELECT 'foo'::TEXT", conn);
        using var reader = await cmd.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken);

        //Act
        reader.Read();

        //Assert
        reader.GetFieldValue<object>(0).Should().Be("foo");
    }

    [Fact]
    public async Task GetValues()
    {
        using var conn = await OpenConnectionAsync();
        using var command = new PgSqlCommand(@"SELECT 'hello', 1, '2014-01-01'::DATE", conn);
        using (var dr = await command.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken))
        {
            dr.Read();
            var values = new object[4];
            dr.GetValues(values).Should().Be(3);
            values.Should().Equal(new object[] { "hello", 1, new DateOnly(2014, 1, 1), null });
        }
        using (var dr = await command.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken))
        {
            dr.Read();
            var values = new object[2];
            dr.GetValues(values).Should().Be(2);
            values.Should().Equal(new object[] { "hello", 1 });
        }
    }

    [Fact]
    public async Task ExecuteReader_getting_empty_resultset_with_output_parameter()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "name TEXT");
        var command = new PgSqlCommand($"SELECT * FROM {table} WHERE name = NULL;", conn);
        var param = new PgSqlParameter("some_param", PgSqlDbType.Varchar);
        param.Direction = ParameterDirection.Output;
        command.Parameters.Add(param);

        //Act
        using var dr = await command.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        dr.NextResult().Should().BeFalse();
    }

    [Fact]
    public async Task get_value_from_empty_resultset()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "name TEXT");
        using var command = new PgSqlCommand($"SELECT * FROM {table} WHERE name = :value;", conn);
        const string test = "Text single quote";
        var param = new PgSqlParameter();
        param.ParameterName = "value";
        param.DbType = DbType.String;
        //param.PgSqlDbType = PgSqlDbType.Text;
        param.Size = test.Length;
        param.Value = test;
        command.Parameters.Add(param);

        using var dr = await command.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken);

        //Act
        dr.Read();

        //Assert
        // This line should throw the invalid operation exception as the datareader will
        // have an empty resultset.
        Assert.Throws<InvalidOperationException>(() => Console.WriteLine(dr.IsDBNull(1)));
    }

    [Fact]
    public async Task Read_past_reader_end()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        var command = new PgSqlCommand("SELECT 1", conn);
        using var dr = await command.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken);

        //Act
        while (dr.Read()) {}

        //Assert
        Assert.Throws<InvalidOperationException>(() => dr[0]);
    }

    [Fact]
    public async Task reader_dispose_state_does_not_leak()
    {
        //Arrange
        if (IsMultiplexing || Behavior != CommandBehavior.Default)
            return;

        var startReaderClosedTcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        var continueReaderClosedTcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var dataSource = CreateDataSource();
        await using var conn1 = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        var connID = conn1.Connector.Id;
        var readerCloseTask = Task.Run(async () =>
        {
            using var cmd = conn1.CreateCommand();
            cmd.CommandText = "SELECT 1";
            await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.CloseConnection);
            reader.ReaderClosed += (s, e) =>
            {
                startReaderClosedTcs.SetResult(new());
                continueReaderClosedTcs.Task.GetAwaiter().GetResult();
            };
        }, cancellationToken: TestContext.Current.CancellationToken);

        await startReaderClosedTcs.Task;

        //Act
        await using var conn2 = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        //Assert
        conn2.Connector.Id.Should().Be(connID);
        using var cmd = conn2.CreateCommand();
        cmd.CommandText = "SELECT 1";
        await using var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        reader.State.Should().Be(ReaderState.BeforeResult);
        continueReaderClosedTcs.SetResult(new());
        await readerCloseTask;
        reader.State.Should().Be(ReaderState.BeforeResult);
    }

    [Fact]
    public async Task single_result()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        await using var command = new PgSqlCommand(@"SELECT 1; SELECT 2", conn);

        //Act
        var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleResult | Behavior, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        reader.Read().Should().BeTrue();
        reader.GetInt32(0).Should().Be(1);
        reader.NextResult().Should().BeFalse();
    }

    [Theory, IssueLink("https://github.com/npgsql/npgsql/issues/400")]
    [InlineData(PrepareOrNot.Prepared)]
    [InlineData(PrepareOrNot.NotPrepared)]
    public async Task exception_thrown_from_ExecuteReaderAsync(PrepareOrNot prepare)
    {
        //Arrange
        if (prepare == PrepareOrNot.Prepared && IsMultiplexing)
            return;

        using var conn = await OpenConnectionAsync();
        var function = await GetTempFunctionName(conn);

        await conn.ExecuteNonQueryAsync($@"
CREATE OR REPLACE FUNCTION {function}() RETURNS VOID AS
   'BEGIN RAISE EXCEPTION ''testexception'' USING ERRCODE = ''12345''; END;'
LANGUAGE 'plpgsql';
                ", cancellationToken: TestContext.Current.CancellationToken);

        using var cmd = new PgSqlCommand($"SELECT {function}()", conn);

        //Act
        if (prepare == PrepareOrNot.Prepared)
            cmd.Prepare();

        //Assert
        await Assert.ThrowsAsync<PostgresException>(async () => await cmd.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Theory, IssueLink("https://github.com/npgsql/npgsql/issues/1032")]
    [InlineData(PrepareOrNot.Prepared)]
    [InlineData(PrepareOrNot.NotPrepared)]
    public async Task exception_thrown_from_NextResult(PrepareOrNot prepare)
    {
        //Arrange
        if (prepare == PrepareOrNot.Prepared && IsMultiplexing)
            return;

        using var conn = await OpenConnectionAsync();
        var function = await GetTempFunctionName(conn);

        await conn.ExecuteNonQueryAsync($@"
CREATE OR REPLACE FUNCTION {function}() RETURNS VOID AS
   'BEGIN RAISE EXCEPTION ''testexception'' USING ERRCODE = ''12345''; END;'
LANGUAGE 'plpgsql';
                ", cancellationToken: TestContext.Current.CancellationToken);

        using var cmd = new PgSqlCommand($"SELECT 1; SELECT {function}()", conn);
        if (prepare == PrepareOrNot.Prepared)
            cmd.Prepare();

        //Act
        using var reader = await cmd.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        Assert.Throws<PostgresException>(() => reader.NextResult());
    }

    [Theory, IssueLink("https://github.com/npgsql/npgsql/issues/967")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PgSqlException_references_BatchCommand_with_single_command(bool includeFailedBatchedCommand)
    {
        //Arrange
        await using var dataSource = CreateDataSource(x => x.IncludeFailedBatchedCommand = includeFailedBatchedCommand);
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        var function = await GetTempFunctionName(conn);

        await conn.ExecuteNonQueryAsync($@"
CREATE OR REPLACE FUNCTION {function}() RETURNS VOID AS
   'BEGIN RAISE EXCEPTION ''testexception'' USING ERRCODE = ''12345''; END;'
LANGUAGE 'plpgsql'", cancellationToken: TestContext.Current.CancellationToken);

        // We use PgSqlConnection.CreateCommand to test that the command isn't recycled when referenced in an exception
        var cmd = conn.CreateCommand();

        //Act
        cmd.CommandText = $"SELECT {function}()";

        //Assert
        var exception = await Assert.ThrowsAsync<PostgresException>(() => cmd.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken));
        if (includeFailedBatchedCommand)
            exception.BatchCommand.Should().BeSameAs(cmd.InternalBatchCommands[0]);
        else
            exception.BatchCommand.Should().BeNull();

        // Make sure the command isn't recycled by the connection when it's disposed - this is important since internal command
        // resources are referenced by the exception above, which is very likely to escape the using statement of the command.
        cmd.Dispose();
        var cmd2 = conn.CreateCommand();
        cmd.Should().NotBeSameAs(cmd2);
    }

    [Theory, IssueLink("https://github.com/npgsql/npgsql/issues/967")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PgSqlException_references_BatchCommand_with_multiple_commands(bool includeFailedBatchedCommand)
    {
        await using var dataSource = CreateDataSource(x => x.IncludeFailedBatchedCommand = includeFailedBatchedCommand);
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        var function = await GetTempFunctionName(conn);

        await conn.ExecuteNonQueryAsync($@"
CREATE OR REPLACE FUNCTION {function}() RETURNS VOID AS
   'BEGIN RAISE EXCEPTION ''testexception'' USING ERRCODE = ''12345''; END;'
LANGUAGE 'plpgsql'", cancellationToken: TestContext.Current.CancellationToken);

        // We use PgSqlConnection.CreateCommand to test that the command isn't recycled when referenced in an exception
        var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT 1; {function}()";

        await using (var reader = await cmd.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken))
        {
            var exception = await Assert.ThrowsAsync<PostgresException>(() => reader.NextResultAsync(TestContext.Current.CancellationToken));
            if (includeFailedBatchedCommand)
                exception.BatchCommand.Should().BeSameAs(cmd.InternalBatchCommands[1]);
            else
                exception.BatchCommand.Should().BeNull();
        }

        // Make sure the command isn't recycled by the connection when it's disposed - this is important since internal command
        // resources are referenced by the exception above, which is very likely to escape the using statement of the command.
        cmd.Dispose();
        var cmd2 = conn.CreateCommand();
        cmd.Should().NotBeSameAs(cmd2);
    }

    #region SchemaOnly

    [Fact]
    public async Task schema_only_returns_no_data()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        await using var cmd = new PgSqlCommand("SELECT 1", conn);

        //Act
        await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SchemaOnly, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        reader.Read().Should().BeFalse();
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/2827")]
    public async Task schema_only_next_result_beyond_end()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "id INT");

        using var cmd = new PgSqlCommand($"SELECT * FROM {table}", conn);

        //Act
        using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SchemaOnly, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        reader.NextResult().Should().BeFalse();
        reader.NextResult().Should().BeFalse();
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/4124")]
    public async Task schema_only_GetDataTypeName_with_unsupported_type()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        using var cmd = new PgSqlCommand(@"select aggfnoid from pg_aggregate", conn);

        //Act
        using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SchemaOnly, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        reader.GetDataTypeName(0).Should().Be("regproc");
    }

    #endregion SchemaOnly

    #region GetOrdinal

    [Fact]
    public async Task GetOrdinal()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        using var command = new PgSqlCommand(@"SELECT 0, 1 AS some_column WHERE 1=0", conn);

        //Act
        using var reader = await command.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        reader.GetOrdinal("some_column").Should().Be(1);
        Assert.Throws<IndexOutOfRangeException>(() => reader.GetOrdinal("doesn't_exist"));
    }

    [Fact]
    public async Task GetOrdinal_case_insensitive()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        using var command = new PgSqlCommand("select 123 as FIELD1", conn);
        using var reader = await command.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken);

        //Act
        reader.Read();

        //Assert
        reader.GetOrdinal("fieLd1").Should().Be(0);
    }

    [Fact]
    public async Task GetOrdinal_kana_insensitive()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        using var command = new PgSqlCommand("select 123 as ｦｧｨｩｪｫｬ", conn);
        using var reader = await command.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken);

        //Act
        reader.Read();

        //Assert
        reader["ヲァィゥェォャ"].Should().Be(123);
    }

    #endregion GetOrdinal

    [Fact]
    public async Task field_index_does_not_exist()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        using var command = new PgSqlCommand("SELECT 1", conn);
        using var dr = await command.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken);

        //Act
        dr.Read();

        //Assert
        Assert.Throws<IndexOutOfRangeException>(() => dr[5]);
    }

    // Performs some operations while a reader is still open and checks for exceptions
    [Fact]
    public async Task reader_is_still_open()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        // We might get the connection, on which the second command was already prepared, so prepare wouldn't start the UserAction
        if (!IsMultiplexing)
            conn.UnprepareAll();
        using var cmd1 = new PgSqlCommand("SELECT 1", conn);

        //Act
        await using var reader1 = await cmd1.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        Assert.Throws<PgSqlOperationInProgressException>(() => conn.ExecuteNonQuery("SELECT 1"));
        await Assert.ThrowsAsync<PgSqlOperationInProgressException>(async () => await conn.ExecuteScalarAsync("SELECT 1", cancellationToken: TestContext.Current.CancellationToken));

        using var cmd2 = new PgSqlCommand("SELECT 2", conn);
        Assert.Throws<PgSqlOperationInProgressException>(() => cmd2.ExecuteReader(Behavior));
        if (!IsMultiplexing)
            Assert.Throws<PgSqlOperationInProgressException>(() => cmd2.Prepare());
    }

    [Theory]
    [InlineData(PrepareOrNot.Prepared)]
    [InlineData(PrepareOrNot.NotPrepared)]
    public async Task cleans_up_ok_with_dispose_calls(PrepareOrNot prepare)
    {
        if (prepare == PrepareOrNot.Prepared && IsMultiplexing)
            return;

        using var conn = await OpenConnectionAsync();
        using var command = new PgSqlCommand("SELECT 1", conn);
        using var dr = await command.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken);
        dr.Read();
        dr.Close();

        using var upd = conn.CreateCommand();
        upd.CommandText = "SELECT 1";
        if (prepare == PrepareOrNot.Prepared)
            upd.Prepare();
    }

    [Fact]
    public async Task null_values()
    {
        using var conn = await OpenConnectionAsync();
        using var cmd = new PgSqlCommand("SELECT @p1, @p2::TEXT", conn);
        cmd.Parameters.Add(new PgSqlParameter("p1", DbType.String) { Value = DBNull.Value });
        cmd.Parameters.Add(new PgSqlParameter { ParameterName = "p2", Value = DBNull.Value });

        using var reader = await cmd.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken);
        reader.Read();

        for (var i = 0; i < cmd.Parameters.Count; i++)
        {
            reader.IsDBNull(i).Should().BeTrue();
            (await reader.IsDBNullAsync(i, cancellationToken: TestContext.Current.CancellationToken)).Should().BeTrue();
            reader.GetValue(i).Should().Be(DBNull.Value);
            reader.GetFieldValue<object>(i).Should().Be(DBNull.Value);
            reader.GetProviderSpecificValue(i).Should().Be(DBNull.Value);
            Assert.Throws<InvalidCastException>(() => reader.GetString(i));
            Assert.Throws<InvalidCastException>(() => reader.GetStream(i));
        }
    }

    [Theory]
    [InlineData(PrepareOrNot.NotPrepared)]
    [InlineData(PrepareOrNot.Prepared)]
    [IssueLink("https://github.com/npgsql/npgsql/issues/742")]
    [IssueLink("https://github.com/npgsql/npgsql/issues/800")]
    [IssueLink("https://github.com/npgsql/npgsql/issues/1234")]
    [IssueLink("https://github.com/npgsql/npgsql/issues/1898")]
    public async Task HasRows(PrepareOrNot prepare)
    {
        if (prepare == PrepareOrNot.Prepared && IsMultiplexing)
            return;

        using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "name TEXT");

        var command = new PgSqlCommand($"SELECT 1; SELECT * FROM {table} WHERE name='does_not_exist'", conn);
        if (prepare == PrepareOrNot.Prepared)
            command.Prepare();
        using (var reader = await command.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken))
        {
            reader.HasRows.Should().BeTrue();
            reader.HasRows.Should().BeTrue();
            reader.Read().Should().BeTrue();
            reader.HasRows.Should().BeTrue();
            reader.Read().Should().BeFalse();
            reader.HasRows.Should().BeTrue();
            await reader.NextResultAsync(TestContext.Current.CancellationToken);
            reader.HasRows.Should().BeFalse();
        }

        command.CommandText = $"SELECT * FROM {table}";
        if (prepare == PrepareOrNot.Prepared)
            command.Prepare();
        using (var reader = await command.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken))
        {
            reader.Read();
            reader.HasRows.Should().BeFalse();
        }

        command.CommandText = "SELECT 1";
        if (prepare == PrepareOrNot.Prepared)
            command.Prepare();
        using (var reader = await command.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken))
        {
            reader.Read();
            reader.Close();
            Assert.Throws<InvalidOperationException>(() => reader.HasRows);
        }

        command.CommandText = $"INSERT INTO {table} (name) VALUES ('foo'); SELECT * FROM {table}";
        if (prepare == PrepareOrNot.Prepared)
            command.Prepare();
        using (var reader = await command.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken))
        {
            reader.HasRows.Should().BeTrue();
            reader.Read();
            reader.GetString(0).Should().Be("foo");
        }

        (await conn.ExecuteScalarAsync("SELECT 1", cancellationToken: TestContext.Current.CancellationToken)).Should().Be(1);
    }

    [Fact]
    public async Task HasRows_without_resultset()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "name TEXT");
        using var command = new PgSqlCommand($"DELETE FROM {table} WHERE name = 'unknown'", conn);

        //Act
        using var reader = await command.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        reader.HasRows.Should().BeFalse();
    }

    [Fact]
    public async Task interval_as_TimeSpan()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        using var command = new PgSqlCommand("SELECT CAST('1 hour' AS interval) AS dauer", conn);

        //Act
        using var dr = await command.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        dr.HasRows.Should().BeTrue();
        dr.Read().Should().BeTrue();
        dr.HasRows.Should().BeTrue();
        var ts = dr.GetTimeSpan(0);
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/5439")]
    public async Task sequential_buffered_seek()
    {
        await using var conn = await OpenConnectionAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """select v.i, jsonb_build_object(), current_timestamp + make_interval(0, 0, 0, 0, 0, 0, v.i), null::jsonb, '{"value": 42}'::jsonb from generate_series(1, 1000) as v(i)""";
        var rdr = await cmd.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken);
        while (await rdr.ReadAsync(TestContext.Current.CancellationToken)) {
            var v1 = rdr[0];
            var v2 = rdr[1];
            //_ = rdr[2]; // uncomment line for successful execution
            var v3 = rdr[3];
            var v4 = rdr[4];
        }
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/5430")]
    public async Task sequential_buffered_seek_long()
    {
        await using var conn = await OpenConnectionAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """select v.i, repeat('1', 10), repeat('2', 10), repeat('3', 10), repeat('4', 10), 1, 2 from generate_series(1, 1000) as v(i)""";
        var rdr = await cmd.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken);
        while (await rdr.ReadAsync(TestContext.Current.CancellationToken))
        {
            _ = rdr[0];
            _ = rdr[1];
            //_ = rdr[2];
            //_ = rdr[3];
            //_ = rdr[4];
            //_ = rdr[5]; // uncomment lines for successful execution
            _ = rdr[6];
        }
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/5430")]
    public async Task sequential_buffered_seek_reread()
    {
        await using var conn = await OpenConnectionAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """select v.i, repeat('1', 10), repeat('2', 10), repeat('3', 10), repeat('4', 10), 1, NULL from generate_series(1, 1000) as v(i)""";
        var rdr = await cmd.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken);
        while (await rdr.ReadAsync(TestContext.Current.CancellationToken))
        {
            _ = rdr[0];
            _ = rdr[1];
            //_ = rdr[2];
            //_ = rdr[3];
            //_ = rdr[4];
            //_ = rdr[5]; // uncomment lines for successful execution
            _ = rdr.IsDBNull(6);
            _ = rdr[6];
            rdr.IsDBNull(6).Should().BeTrue();
        }
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/5484")]
    public async Task GetFieldValueAsync_async_read()
    {
        if (!IsSequential)
            return;

        await using var postmasterMock = PgPostmasterMock.Start(ConnectionString);
        await using var dataSource = CreateDataSource(postmasterMock.ConnectionString);
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        var expected = new byte[10000];
        expected.AsSpan().Fill(1);

        var pgMock = await postmasterMock.WaitForServerConnection();
        await pgMock
            .WriteParseComplete()
            .WriteBindComplete()
            .WriteRowDescription(new FieldDescription(ByteaOid))
            .WriteDataRowWithFlush(expected);

        using var cmd = new PgSqlCommand("irrelevant", conn);
        var reader = await cmd.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken);
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            var task = reader.GetFieldValueAsync<object>(0, cancellationToken: TestContext.Current.CancellationToken);
            await pgMock
                .WriteCommandComplete()
                .WriteReadyForQuery()
                .FlushAsync();
            var actual = await task;
            ValueEquality.AreEqual(expected, actual).Should().BeTrue(
                $"expected {ValueEquality.Format(expected)} but got {ValueEquality.Format(actual)}");
        }
    }

    [Fact]
    public async Task close_connection_in_middle_of_row()
    {
        using var conn = await OpenConnectionAsync();
        using var cmd = new PgSqlCommand("SELECT 1, 2", conn);
        using var reader = await cmd.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken);
        reader.Read();
    }

    // NextResult was throwing an ArgumentOutOfRangeException when trying to determine the statement to associate with the PostgresException
    [Fact, IssueLink("https://github.com/npgsql/npgsql/pull/1266")]
    public async Task reader_next_result_exception_handling()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        var table1 = await GetTempTableName(conn);
        var table2 = await GetTempTableName(conn);
        var function = await GetTempFunctionName(conn);

        var initializeTablesSql = $@"
CREATE TABLE {table1} (value int NOT NULL);
CREATE TABLE {table2} (value int UNIQUE);
ALTER TABLE ONLY {table1} ADD CONSTRAINT {table1}_{table2}_fk FOREIGN KEY (value) REFERENCES {table2}(value) DEFERRABLE INITIALLY DEFERRED;
CREATE OR REPLACE FUNCTION {function}(_value int) RETURNS int AS $BODY$
BEGIN
    INSERT INTO {table1}(value) VALUES(_value);
    RETURN _value;
END;
$BODY$
LANGUAGE plpgsql VOLATILE";

        await conn.ExecuteNonQueryAsync(initializeTablesSql, cancellationToken: TestContext.Current.CancellationToken);
        using var cmd = new PgSqlCommand($"SELECT {function}(1)", conn);

        //Act
        using var reader = await cmd.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        Assert.Throws<PostgresException>(() => reader.NextResult())
            .SqlState.Should().Be(PostgresErrorCodes.ForeignKeyViolation);
    }

    [Fact]
    public async Task invalid_cast()
    {
        using var conn = await OpenConnectionAsync();
        // Chunking type handler
        using (var cmd = new PgSqlCommand("SELECT 'foo'", conn))
        using (var reader = await cmd.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken))
        {
            reader.Read();
            Assert.Throws<InvalidCastException>(() => reader.GetInt32(0));
        }
        // Simple type handler
        using (var cmd = new PgSqlCommand("SELECT 1", conn))
        using (var reader = await cmd.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken))
        {
            reader.Read();
            Assert.Throws<InvalidCastException>(() => reader.GetDateTime(0));
        }
        (await conn.ExecuteScalarAsync("SELECT 1", cancellationToken: TestContext.Current.CancellationToken)).Should().Be(1);
    }

    // Reads a lot of rows to make sure the long unoptimized path for Read() works
    [Fact]
    public async Task many_reads()
    {
        using var conn = await OpenConnectionAsync();
        using var cmd = new PgSqlCommand($"SELECT generate_series(1, {conn.Settings.ReadBufferSize})", conn);
        using var reader = await cmd.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken);
        for (var i = 1; i <= conn.Settings.ReadBufferSize; i++)
        {
            reader.Read().Should().BeTrue();
            reader.GetInt32(0).Should().Be(i);
        }
        reader.Read().Should().BeFalse();
    }

    [Fact]
    public async Task nullable_scalar()
    {
        // We read the same column multiple times
        if (IsSequential)
            return;

        using var conn = await OpenConnectionAsync();
        using var cmd = new PgSqlCommand("SELECT @p1, @p2", conn);
        var p1 = new PgSqlParameter { ParameterName = "p1", Value = DBNull.Value, PgSqlDbType = PgSqlDbType.Smallint };
        var p2 = new PgSqlParameter { ParameterName = "p2", Value = (short)8 };
        p2.PgSqlDbType.Should().Be(PgSqlDbType.Smallint);
        p2.DbType.Should().Be(DbType.Int16);
        cmd.Parameters.Add(p1);
        cmd.Parameters.Add(p2);
        using var reader = await cmd.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken);
        reader.Read();

        for (var i = 0; i < cmd.Parameters.Count; i++)
        {
            reader.GetFieldType(i).Should().Be(typeof(short));
            reader.GetDataTypeName(i).Should().Be("smallint");
        }

        reader.GetFieldValue<object>(0).Should().Be(DBNull.Value);
        Assert.Throws<InvalidCastException>(() => reader.GetFieldValue<int>(0));
        FluentActions.Invoking(() => reader.GetFieldValue<int?>(0)).Should().NotThrow();
        reader.GetFieldValue<int?>(0).Should().BeNull();

        FluentActions.Invoking(() => reader.GetFieldValue<object>(1)).Should().NotThrow();
        FluentActions.Invoking(() => reader.GetFieldValue<int>(1)).Should().NotThrow();
        FluentActions.Invoking(() => reader.GetFieldValue<int?>(1)).Should().NotThrow();
        reader.GetFieldValue<object>(1).Should().Be(8);
        reader.GetFieldValue<int>(1).Should().Be(8);
        reader.GetFieldValue<int?>(1).Should().Be(8);
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/2913")]
    public async Task bug_2913_reading_previous_query_messages()
    {
        // No point in testing for multiplexing, as every query may use another connection
        if (IsMultiplexing)
            return;

        var firstMrs = new ManualResetEventSlim(false);
        var secondMrs = new ManualResetEventSlim(false);

        var secondQuery = Task.Run(async () =>
        {
            firstMrs.Wait();
            await using var secondConn = await OpenConnectionAsync();
            using var secondCmd = new PgSqlCommand(@"SELECT 1; SELECT 2;", secondConn);
            await using var secondReader = await secondCmd.ExecuteReaderAsync(Behavior | CommandBehavior.CloseConnection);

            // Check, that StatementIndex is equals to default value
            secondReader.StatementIndex.Should().Be(0);
            secondMrs.Wait();
            // Check, that the first query didn't change StatementIndex
            secondReader.StatementIndex.Should().Be(0);
        }, TestContext.Current.CancellationToken);

        await using (var firstConn = await OpenConnectionAsync())
        {
            // Executing a query, which fails with PgSqlException on reader disposing, as NotExistingTable doesn't exist
            using var firstCmd = new PgSqlCommand(@"SELECT 1; SELECT * FROM NotExistingTable;", firstConn);
            await using var firstReader = await firstCmd.ExecuteReaderAsync(Behavior | CommandBehavior.CloseConnection, cancellationToken: TestContext.Current.CancellationToken);

            firstReader.StatementIndex.Should().Be(0);

            firstReader.ReaderClosed += (s, e) =>
            {
                // Starting a second query, which in case of a bug uses firstConn
                firstMrs.Set();
                // Waiting for the second query to start executing
                Thread.Sleep(100);
                // After waiting, reader is free to reset prepared statements, which also increments StatementIndex
            };

            await Assert.ThrowsAsync<PostgresException>(() => firstReader.NextResultAsync(TestContext.Current.CancellationToken));

            secondMrs.Set();
        }

        await secondQuery;

        // If we're here and a bug is still not fixed, we fail while executing reader, as we're reading skipped messages for the second query
        await using var thirdConn = OpenConnection();
        using var thirdCmd = new PgSqlCommand(@"SELECT 1; SELECT 2;", thirdConn);
        await using var thirdReader = await thirdCmd.ExecuteReaderAsync(Behavior | CommandBehavior.CloseConnection, cancellationToken: TestContext.Current.CancellationToken);
    }

    [Fact]
    [IssueLink("https://github.com/npgsql/npgsql/issues/2913")]
    [IssueLink("https://github.com/npgsql/npgsql/issues/3289")]
    public async Task reader_close_and_dispose()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        using var cmd1 = conn.CreateCommand();
        cmd1.CommandText = "SELECT 1";

        var reader1 = await cmd1.ExecuteReaderAsync(Behavior | CommandBehavior.CloseConnection, cancellationToken: TestContext.Current.CancellationToken);
        await reader1.CloseAsync();

        await conn.OpenAsync(TestContext.Current.CancellationToken);
        cmd1.Connection = conn;

        //Act
        var reader2 = await cmd1.ExecuteReaderAsync(Behavior | CommandBehavior.CloseConnection, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        reader1.Should().NotBeSameAs(reader2);
        reader2.State.Should().Be(ReaderState.BeforeResult);

        await reader1.DisposeAsync();

        reader2.State.Should().Be(ReaderState.BeforeResult);
    }

    [Fact]
    [IssueLink("https://github.com/npgsql/npgsql/issues/2964")]
    public async Task bug_2964_connection_close_and_reader_dispose()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        using var cmd1 = conn.CreateCommand();
        cmd1.CommandText = "SELECT 1";

        var reader1 = await cmd1.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken);
        await conn.CloseAsync();
        await conn.OpenAsync(TestContext.Current.CancellationToken);

        //Act
        var reader2 = await cmd1.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        reader1.Should().NotBeSameAs(reader2);
        reader2.State.Should().Be(ReaderState.BeforeResult);

        await reader1.DisposeAsync();

        reader2.State.Should().Be(ReaderState.BeforeResult);
    }

    [Fact]
    public async Task reader_reuse_on_dispose()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync(TestContext.Current.CancellationToken);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1";

        var reader1 = await cmd.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken);
        await reader1.ReadAsync(TestContext.Current.CancellationToken);
        await reader1.DisposeAsync();

        //Act
        var reader2 = await cmd.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        reader1.Should().BeSameAs(reader2);
        await reader2.DisposeAsync();
    }

    [Fact]
    public async Task unbound_reader_reuse()
    {
        await using var dataSource = CreateDataSource(csb =>
        {
            csb.MinPoolSize = 1;
            csb.MaxPoolSize = 1;
        });
        await using var conn1 = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        using var cmd1 = conn1.CreateCommand();
        cmd1.CommandText = "SELECT 1";
        var reader1 = await cmd1.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken);
        await using (var __ = reader1)
        {
            (await reader1.ReadAsync(TestContext.Current.CancellationToken)).Should().BeTrue();
            reader1.GetInt32(0).Should().Be(1);

            await reader1.CloseAsync();
            await conn1.CloseAsync();
        }

        await using var conn2 = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        using var cmd2 = conn2.CreateCommand();
        cmd2.CommandText = "SELECT 2";
        var reader2 = await cmd2.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken);
        await using (var __ = reader2)
        {
            (await reader2.ReadAsync(TestContext.Current.CancellationToken)).Should().BeTrue();
            reader2.GetInt32(0).Should().Be(2);
            reader1.Should().NotBeSameAs(reader2);

            await reader2.CloseAsync();
            await conn2.CloseAsync();
        }

        await using var conn3 = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        using var cmd3 = conn3.CreateCommand();
        cmd3.CommandText = "SELECT 3";
        var reader3 = await cmd3.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken);
        await using (var __ = reader3)
        {
            (await reader3.ReadAsync(TestContext.Current.CancellationToken)).Should().BeTrue();
            reader3.GetInt32(0).Should().Be(3);
            reader1.Should().BeSameAs(reader3);

            await reader3.CloseAsync();
            await conn3.CloseAsync();
        }
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/3772")]
    public async Task bug_3772()
    {
        //Arrange
        if (!IsSequential)
            return;

        await using var postmasterMock = PgPostmasterMock.Start(ConnectionString);
        await using var dataSource = CreateDataSource(postmasterMock.ConnectionString);
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        var pgMock = await postmasterMock.WaitForServerConnection();
        pgMock
            .WriteParseComplete()
            .WriteBindComplete()
            .WriteRowDescription(new FieldDescription(Int4Oid), new FieldDescription(ByteaOid));

        var intValue = new byte[] { 0, 0, 0, 1 };
        var byteValue = new byte[] { 1, 2, 3, 4 };

        var writeBuffer = pgMock.WriteBuffer;
        writeBuffer.WriteByte((byte)BackendMessageCode.DataRow);
        writeBuffer.WriteInt32(4 + 2 + intValue.Length + byteValue.Length + 8);
        writeBuffer.WriteInt16(2);
        writeBuffer.WriteInt32(intValue.Length);
        writeBuffer.WriteBytes(intValue);
        await pgMock.FlushAsync();

        using var cmd = new PgSqlCommand("SELECT some_int, some_byte FROM some_table", conn);
        await using var reader = await cmd.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken);

        await reader.ReadAsync(TestContext.Current.CancellationToken);

        //Act
        reader.GetInt32(0);

        //Assert
        reader.Connector.ReadBuffer.ReadBytesLeft.Should().Be(0);
        reader.Connector.ReadBuffer.ReadPosition.Should().NotBe(0);

        writeBuffer.WriteInt32(byteValue.Length);
        writeBuffer.WriteBytes(byteValue);
        await pgMock
            .WriteDataRow(intValue, Enumerable.Range(1, 100).Select(x => (byte)x).ToArray())
            .WriteCommandComplete()
            .WriteReadyForQuery()
            .FlushAsync();

        await reader.GetFieldValueAsync<byte[]>(1, cancellationToken: TestContext.Current.CancellationToken);

        await FluentActions.Awaiting(() => reader.ReadAsync()).Should().NotThrowAsync();
    }

    [Theory] // #4377
    [InlineData(true)]
    [InlineData(false)]
    public async Task Dispose_does_not_swallow_exceptions(bool async)
    {
        //Arrange
        await using var postmasterMock = PgPostmasterMock.Start(ConnectionString);
        await using var dataSource = CreateDataSource(postmasterMock.ConnectionString);
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using var tx = IsMultiplexing ? await conn.BeginTransactionAsync(TestContext.Current.CancellationToken) : null;
        var pgMock = await postmasterMock.WaitForServerConnection();

        if (IsMultiplexing)
            pgMock
                .WriteEmptyQueryResponse()
                .WriteReadyForQuery(TransactionStatus.InTransactionBlock);

        // Write responses for the query, but break the connection before sending CommandComplete/ReadyForQuery
        await pgMock
            .WriteParseComplete()
            .WriteBindComplete()
            .WriteRowDescription(new FieldDescription(Int4Oid))
            .WriteDataRow(BitConverter.GetBytes(BinaryPrimitives.ReverseEndianness(1)))
            .FlushAsync();

        using var cmd = new PgSqlCommand("SELECT 1", conn);
        using var reader = await cmd.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken);
        await reader.ReadAsync(TestContext.Current.CancellationToken);

        //Act
        pgMock.Close();

        //Assert
        if (async)
            Assert.Throws<PgSqlException>(() => reader.Dispose());
        else
            await Assert.ThrowsAsync<PgSqlException>(async () => await reader.DisposeAsync());
    }

    [Fact]
    public async Task Read_string_as_char()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 'abcdefgh', 'ijklmnop'";

        //Act
        await using var reader = await cmd.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        (await reader.ReadAsync(TestContext.Current.CancellationToken)).Should().BeTrue();
        reader.GetChar(0).Should().Be('a');
        if (Behavior == CommandBehavior.SequentialAccess)
            Assert.Throws<InvalidOperationException>(() => reader.GetChar(0));
        else
            reader.GetChar(0).Should().Be('a');
        reader.GetChar(1).Should().Be('i');
    }

    #region GetBytes / GetStream

    [Fact]
    public async Task GetBytes()
    {
        using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "bytes BYTEA");

        // TODO: This is too small to actually test any interesting sequential behavior
        byte[] expected = [1, 2, 3, 4, 5];
        var actual = new byte[expected.Length];
        await conn.ExecuteNonQueryAsync($"INSERT INTO {table} (bytes) VALUES ({EncodeByteaHex(expected)})", cancellationToken: TestContext.Current.CancellationToken);

        var query = $"SELECT bytes, 'foo', bytes, 'bar', bytes, bytes FROM {table}";
        using var cmd = new PgSqlCommand(query, conn);
        using var reader = await cmd.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken);
        reader.Read();

        reader.GetBytes(0, 0, actual, 0, 2).Should().Be(2);
        actual[0].Should().Be(expected[0]);
        actual[1].Should().Be(expected[1]);
        reader.GetBytes(0, 0, null, 0, 0).Should().Be(expected.Length, "Bad column length");
        if (IsSequential)
            Assert.Throws<InvalidOperationException>(() => reader.GetBytes(0, 0, actual, 4, 1));
        else
        {
            reader.GetBytes(0, 0, actual, 4, 1).Should().Be(1);
            actual[4].Should().Be(expected[0]);
        }
        reader.GetBytes(0, 2, actual, 2, 3).Should().Be(3);
        actual.Should().Equal(expected);
        reader.GetBytes(0, 0, null, 0, 0).Should().Be(expected.Length, "Bad column length");

        reader.GetString(1).Should().Be("foo");
        reader.GetBytes(2, 0, actual, 0, 2);
        // Jump to another column from the middle of the column
        reader.GetBytes(4, 0, actual, 0, 2);
        reader.GetBytes(4, expected.Length - 1, actual, 0, 2).Should().Be(1, "Length greater than data length");
        actual[0].Should().Be(expected[^1], "Length greater than data length");
        Assert.Throws<IndexOutOfRangeException>(() => reader.GetBytes(4, 0, actual, 0, actual.Length + 1));
        // Close in the middle of a column
        reader.GetBytes(5, 0, actual, 0, 2);

        //var result = (byte[]) cmd.ExecuteScalar();
        //Assert.AreEqual(2, result.Length);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GetStream_second_time_throws(bool isAsync)
    {
        //Arrange
        var expected = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var streamGetter = BuildStreamGetter(isAsync);

        using var conn = await OpenConnectionAsync();
        using var cmd = new PgSqlCommand($"SELECT {EncodeByteaHex(expected)}::bytea", conn);
        using var reader = await cmd.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken);

        await reader.ReadAsync(TestContext.Current.CancellationToken);

        //Act
        using var stream = await streamGetter(reader, 0);

        //Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await streamGetter(reader, 0));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GetBytes_before_getstream(bool isAsync)
    {
        //Arrange
        var expected = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var streamGetter = BuildStreamGetter(isAsync);

        using var conn = await OpenConnectionAsync();
        using var cmd = new PgSqlCommand($"SELECT {EncodeByteaHex(expected)}::bytea", conn);
        using var reader = await cmd.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken);

        //Act
        await reader.ReadAsync(TestContext.Current.CancellationToken);

        //Assert
        // GetBytes with null buffer won't consume column in any way
        reader.GetBytes(0, 0, null, 0, 0).Should().Be(expected.Length, "Bad column length");

        using var stream = await streamGetter(reader, 0);
        stream.Length.Should().Be(expected.Length);
    }

    static IEnumerable<(object Generic, byte[] Binary)> GetStreamValues()
    {
        var binary = MemoryMarshal
            .AsBytes<int>(Enumerable.Range(0, 1024).ToArray())
            .ToArray();
        yield return (binary, binary);

        var bigBinary = MemoryMarshal
            .AsBytes<int>(Enumerable.Range(0, 8193).ToArray())
            .ToArray();
        yield return (bigBinary, bigBinary);

        var bigint = 0xDEADBEEFL;
        var bigintBinary = BitConverter.GetBytes(
            BitConverter.IsLittleEndian
                ? BinaryPrimitives.ReverseEndianness(bigint)
                : bigint);
        yield return (bigint, bigintBinary);
    }

    public static TheoryData<bool, object, byte[]> GetStreamCases()
    {
        var data = new TheoryData<bool, object, byte[]>();
        foreach (var isAsync in new[] { true, false })
            foreach (var (generic, binary) in GetStreamValues())
                data.Add(isAsync, generic, binary);
        return data;
    }

    [Theory]
    [MemberData(nameof(GetStreamCases))]
    public async Task GetStream(bool isAsync, object generic, byte[] binary)
    {
        //Arrange
        var streamGetter = BuildStreamGetter(isAsync);
        var expected = binary;
        var actual = new byte[expected.Length];

        using var conn = await OpenConnectionAsync();
        using var cmd = new PgSqlCommand("SELECT @p, @p", conn) { Parameters = { new PgSqlParameter("p", generic) } };
        using var reader = await cmd.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken);

        await reader.ReadAsync(TestContext.Current.CancellationToken);

        //Act
        using var stream = await streamGetter(reader, 0);

        //Assert
        stream.CanSeek.Should().Be(Behavior == CommandBehavior.Default);
        stream.Length.Should().Be(expected.Length);

        var position = 0;
        while (position < actual.Length)
        {
            if (isAsync)
                position += await stream.ReadAsync(actual, position, actual.Length - position, cancellationToken: TestContext.Current.CancellationToken);
            else
                position += stream.Read(actual, position, actual.Length - position);
        }

        actual.Should().Equal(expected);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task open_stream_when_changing_columns(bool isAsync)
    {
        //Arrange
        var streamGetter = BuildStreamGetter(isAsync);

        using var conn = await OpenConnectionAsync();
        using var cmd = new PgSqlCommand(@"SELECT @p, @p", conn);
        var data = new byte[] { 1, 2, 3 };
        cmd.Parameters.Add(new PgSqlParameter("p", data));
        using var reader = await cmd.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken);
        reader.Read();
        var stream = await streamGetter(reader, 0);

        //Act
        // ReSharper disable once UnusedVariable
        var v = reader.GetValue(1);

        //Assert
        Assert.Throws<ObjectDisposedException>(() => stream.ReadByte());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task open_stream_when_changing_rows(bool isAsync)
    {
        //Arrange
        var streamGetter = BuildStreamGetter(isAsync);

        using var conn = await OpenConnectionAsync();
        using var cmd = new PgSqlCommand(@"SELECT @p", conn);
        var data = new byte[] { 1, 2, 3 };
        cmd.Parameters.Add(new PgSqlParameter("p", data));
        using var reader = await cmd.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken);
        reader.Read();
        var s1 = await streamGetter(reader, 0);

        //Act
        reader.Read();

        //Assert
        Assert.Throws<ObjectDisposedException>(() => s1.ReadByte());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GetBytes_with_null(bool isAsync)
    {
        //Arrange
        var streamGetter = BuildStreamGetter(isAsync);

        using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "bytes BYTEA");

        var buf = new byte[8];
        await conn.ExecuteNonQueryAsync($"INSERT INTO {table} (bytes) VALUES (NULL)", cancellationToken: TestContext.Current.CancellationToken);
        using var cmd = new PgSqlCommand($"SELECT bytes FROM {table}", conn);
        using var reader = await cmd.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken);

        //Act
        reader.Read();

        //Assert
        reader.IsDBNull(0).Should().BeTrue();
        Assert.Throws<InvalidCastException>(() => reader.GetBytes(0, 0, buf, 0, 1));
        await Assert.ThrowsAsync<InvalidCastException>(async () => await streamGetter(reader, 0));
        Assert.Throws<InvalidCastException>(() => reader.GetBytes(0, 0, null, 0, 0));
    }

    static Func<PgSqlDataReader, int, Task<Stream>> BuildStreamGetter(bool isAsync)
        => isAsync
            ? (r, index) => r.GetStreamAsync(index)
            : (r, index) => Task.FromResult(r.GetStream(index));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GetStream_after_consuming_column_throws(bool async)
    {
        if (!IsSequential)
            return;

        await using var conn = await OpenConnectionAsync();
        await using var cmd = new PgSqlCommand(@"SELECT '\xDEADBEEF'::bytea", conn);
        await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken: TestContext.Current.CancellationToken);
        await reader.ReadAsync(TestContext.Current.CancellationToken);

        _ = reader.GetFieldValue<byte[]>(0);

        if (async)
            Assert.Throws<InvalidOperationException>(() => { _ = reader.GetStreamAsync(0, cancellationToken: TestContext.Current.CancellationToken); });
        else
            Assert.Throws<InvalidOperationException>(() => reader.GetStream(0));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GetStream_in_middle_of_column_throws(bool async)
    {
        if (!IsSequential)
            return;

        await using var conn = await OpenConnectionAsync();
        await using var cmd = new PgSqlCommand(@"SELECT '\xDEADBEEF'::bytea", conn);
        await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken: TestContext.Current.CancellationToken);
        await reader.ReadAsync(TestContext.Current.CancellationToken);

        _ = reader.GetBytes(0, 0, new byte[2], 0, 2);

        if (async)
            Assert.Throws<InvalidOperationException>(() => { _ = reader.GetStreamAsync(0, cancellationToken: TestContext.Current.CancellationToken); });
        else
            Assert.Throws<InvalidOperationException>(() => reader.GetStream(0));
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/5223")]
    public async Task GetStream_seek()
    {
        //Arrange
        // Sequential doesn't allow to seek
        if (IsSequential)
            return;

        await using var conn = await OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 'abcdefgh'";
        await using var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        await reader.ReadAsync(TestContext.Current.CancellationToken);

        var buffer = new byte[4];

        //Act
        await using var stream = reader.GetStream(0);

        //Assert
        stream.CanSeek.Should().BeTrue();

        var seekPosition = stream.Seek(-1, SeekOrigin.End);
        seekPosition.Should().Be(stream.Length - 1);
        var read = stream.Read(buffer);
        read.Should().Be(1);
        Encoding.ASCII.GetString(buffer, 0, 1).Should().Be("h");
        read = stream.Read(buffer);
        read.Should().Be(0);

        seekPosition = stream.Seek(2, SeekOrigin.Begin);
        seekPosition.Should().Be(2);
        read = stream.Read(buffer);
        read.Should().Be(buffer.Length);
        Encoding.ASCII.GetString(buffer).Should().Be("cdef");

        seekPosition = stream.Seek(-3, SeekOrigin.Current);
        seekPosition.Should().Be(3);
        read = stream.Read(buffer);
        read.Should().Be(buffer.Length);
        Encoding.ASCII.GetString(buffer).Should().Be("defg");

        stream.Position = 1;
        read = stream.Read(buffer);
        read.Should().Be(buffer.Length);
        Encoding.ASCII.GetString(buffer).Should().Be("bcde");
    }

    #endregion GetBytes / GetStream

    #region GetChars / GetTextReader

    [Fact]
    public async Task GetChars()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        // TODO: This is too small to actually test any interesting sequential behavior
        const string str = "ABCDE";
        var expected = str.ToCharArray();
        var actual = new char[expected.Length];

        var queryText = $@"SELECT '{str}', 3, '{str}', 4, '{str}', '{str}', '{str}'";
        using var cmd = new PgSqlCommand(queryText, conn);
        using var reader = await cmd.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken);

        //Act
        reader.Read();

        //Assert
        reader.GetChars(0, 0, actual, 0, 2).Should().Be(2);
        actual[0].Should().Be(expected[0]);
        actual[1].Should().Be(expected[1]);
        if (!IsSequential)
            reader.GetChars(0, 0, null, 0, 0).Should().Be(expected.Length, "Bad column length");
        // Note: Unlike with bytea, finding out the length of the column consumes it (variable-width
        // UTF8 encoding)
        reader.GetChars(2, 0, actual, 0, 2).Should().Be(2);
        if (IsSequential)
            Assert.Throws<InvalidOperationException>(() => reader.GetChars(2, 0, actual, 4, 1));
        else
        {
            reader.GetChars(2, 0, actual, 4, 1).Should().Be(1);
            actual[4].Should().Be(expected[0]);
        }
        reader.GetChars(2, 2, actual, 2, 3).Should().Be(3);
        actual.Should().Equal(expected);
        //reader.GetChars(2, 0, null, 0, 0).Should().Be(expected.Length, "Bad column length");

        Assert.Throws<InvalidCastException>(() => reader.GetChars(3, 0, null, 0, 0));
        Assert.Throws<InvalidCastException>(() => reader.GetChars(3, 0, actual, 0, 1));
        reader.GetInt32(3).Should().Be(4);
        reader.GetChars(4, 0, actual, 0, 2);
        // Jump to another column from the middle of the column
        reader.GetChars(5, 0, actual, 0, 2);
        reader.GetChars(5, expected.Length - 1, actual, 0, 2).Should().Be(1, "Length greater than data length");
        actual[0].Should().Be(expected[^1], "Length greater than data length");
        Assert.Throws<IndexOutOfRangeException>(() => reader.GetChars(5, 0, actual, 0, actual.Length + 1));
        // Close in the middle of a column
        reader.GetChars(6, 0, actual, 0, 2);
    }

    [Fact]
    public async Task GetChars_advance_consumed()
    {
        const string value = "01234567";

        using var conn = await OpenConnectionAsync();
        using var cmd = new PgSqlCommand($"SELECT '{value}'", conn);
        using var reader = await cmd.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken);
        reader.Read();

        var buffer = new char[2];
        // Don't start at the beginning of the column.
        reader.GetChars(0, 2, buffer, 0, 2);
        reader.GetChars(0, 4, buffer, 0, 2);
        reader.GetChars(0, 6, buffer, 0, 2);

        // Ask for data past the start and the previous point, exercising restart logic.
        if (!IsSequential)
        {
            reader.GetChars(0, 4, buffer, 0, 2);
            reader.GetChars(0, 6, buffer, 0, 2);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GetTextReader(bool isAsync)
    {
        //Arrange
        Func<PgSqlDataReader, int, Task<TextReader>> textReaderGetter;
        if (isAsync)
            textReaderGetter = (r, index) => r.GetTextReaderAsync(index);
        else
            textReaderGetter = (r, index) => Task.FromResult(r.GetTextReader(index));

        using var conn = await OpenConnectionAsync();
        // TODO: This is too small to actually test any interesting sequential behavior
        const string str = "ABCDE";
        var expected = str.ToCharArray();
        var actual = new char[expected.Length];
        //ExecuteNonQuery(String.Format(@"INSERT INTO data (field_text) VALUES ('{0}')", str));

        var queryText = $@"SELECT '{str}', 'foo'";
        using var cmd = new PgSqlCommand(queryText, conn);
        using var reader = await cmd.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken);
        reader.Read();

        var textReader = await textReaderGetter(reader, 0);

        //Act
        textReader.Read(actual, 0, 2);

        //Assert
        actual[0].Should().Be(expected[0]);
        actual[1].Should().Be(expected[1]);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await textReaderGetter(reader, 0));
        textReader.Read(actual, 2, 1);
        actual[2].Should().Be(expected[2]);
        textReader.Dispose();

        if (IsSequential)
            Assert.Throws<InvalidOperationException>(() => reader.GetChars(0, 0, actual, 4, 1));
        else
        {
            reader.GetChars(0, 0, actual, 4, 1).Should().Be(1);
            actual[4].Should().Be(expected[0]);
        }
        reader.GetString(1).Should().Be("foo");
    }

    [Fact]
    public async Task text_reader_zero_length_column()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT ''";

        //Act
        await using var reader = await cmd.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        (await reader.ReadAsync(TestContext.Current.CancellationToken)).Should().BeTrue();

        using var textReader = reader.GetTextReader(0);
        textReader.Peek().Should().Be(-1);
        textReader.ReadToEnd().Should().Be(string.Empty);
    }

    [Fact]
    public async Task open_TextReader_when_changing_columns()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        using var cmd = new PgSqlCommand(@"SELECT 'some_text', 'some_text'", conn);
        using var reader = await cmd.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken);
        reader.Read();
        var textReader = reader.GetTextReader(0);

        //Act
        // ReSharper disable once UnusedVariable
        var v = reader.GetValue(1);

        //Assert
        Assert.Throws<ObjectDisposedException>(() => textReader.Peek());
    }

    [Fact]
    public async Task open_TextReader_when_changing_rows()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        using var cmd = new PgSqlCommand(@"SELECT 'some_text', 'some_text'", conn);
        using var reader = await cmd.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken);
        reader.Read();
        var tr1 = reader.GetTextReader(0);

        //Act
        reader.Read();

        //Assert
        Assert.Throws<ObjectDisposedException>(() => tr1.Peek());
    }

    [Fact]
    public async Task GetChars_when_null()
    {
        //Arrange
        var buf = new char[8];
        using var conn = await OpenConnectionAsync();
        using var cmd = new PgSqlCommand("SELECT NULL::TEXT", conn);
        using var reader = await cmd.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken);

        //Act
        reader.Read();

        //Assert
        reader.IsDBNull(0).Should().BeTrue();
        Assert.Throws<InvalidCastException>(() => reader.GetChars(0, 0, buf, 0, 1));
        Assert.Throws<InvalidCastException>(() => reader.GetTextReader(0));
        Assert.Throws<InvalidCastException>(() => reader.GetChars(0, 0, null, 0, 0));
    }

    [Fact]
    public async Task reader_is_reused()
    {
        if (IsMultiplexing)
            Assert.Skip("Multiplexing: Fails");

        using var conn = await OpenConnectionAsync();
        PgSqlDataReader reader1;

        using (var cmd = new PgSqlCommand("SELECT 8", conn))
        using (reader1 = await cmd.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken))
        {
            reader1.Read();
            reader1.GetInt32(0).Should().Be(8);
        }

        using (var cmd = new PgSqlCommand("SELECT 9", conn))
        using (var reader2 = await cmd.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken))
        {
            reader2.Should().BeSameAs(reader1);
            reader2.Read();
            reader2.GetInt32(0).Should().Be(9);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GetTextReader_after_consuming_column_throws(bool async)
    {
        if (!IsSequential)
            return;

        await using var conn = await OpenConnectionAsync();
        await using var cmd = new PgSqlCommand("SELECT 'foo'", conn);
        await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken: TestContext.Current.CancellationToken);
        await reader.ReadAsync(TestContext.Current.CancellationToken);

        _ = reader.GetString(0);

        if (async)
            Assert.Throws<InvalidOperationException>(() => { _ = reader.GetTextReaderAsync(0, cancellationToken: TestContext.Current.CancellationToken); });
        else
            Assert.Throws<InvalidOperationException>(() => reader.GetTextReader(0));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GetTextReader_in_middle_of_column_throws(bool async)
    {
        if (!IsSequential)
            return;

        await using var conn = await OpenConnectionAsync();
        await using var cmd = new PgSqlCommand("SELECT 'foo'", conn);
        await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken: TestContext.Current.CancellationToken);
        await reader.ReadAsync(TestContext.Current.CancellationToken);

        _ = reader.GetChars(0, 0, new char[2], 0, 2);

        if (async)
            Assert.Throws<InvalidOperationException>(() => { _ = reader.GetTextReaderAsync(0, cancellationToken: TestContext.Current.CancellationToken); });
        else
            Assert.Throws<InvalidOperationException>(() => reader.GetTextReader(0));
    }

    #endregion GetChars / GetTextReader

    [Theory, IssueLink("https://github.com/npgsql/npgsql/issues/5450")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EndRead_stream_active(bool async)
    {
        if (IsMultiplexing)
            return;

        const int columnLength = 1;

        await using var conn = await OpenConnectionAsync();
        var buffer = conn.Connector.ReadBuffer;
        buffer.FilledBytes += columnLength;
        var reader = buffer.PgReader;
        reader.Init(columnLength, DataFormat.Binary, resumable: false);
        if (async)
            await reader.StartReadAsync(Size.Unknown, TestContext.Current.CancellationToken);
        else
            reader.StartRead(Size.Unknown);

        await using (var _ = reader.GetStream())
        {
            if (async)
                await FluentActions.Awaiting(async () => await reader.EndReadAsync()).Should().NotThrowAsync();
            else
                FluentActions.Invoking(() => reader.EndRead()).Should().NotThrow();
        }

        reader.Commit();
    }

    // Tests that everything goes well when a type handler generates a PgSqlSafeReadException
    [Fact]
    public async Task safe_read_exception()
    {
        //Arrange
        var dataSourceBuilder = CreateDataSourceBuilder();
        // Temporarily reroute integer to go to a type handler which generates SafeReadExceptions
        dataSourceBuilder.AddTypeInfoResolverFactory(new ExplodingTypeHandlerResolverFactory(safe: true));
        await using var dataSource = dataSourceBuilder.Build();
        await using var connection = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        await using var cmd = new PgSqlCommand(@"SELECT 1, 'hello'", connection);
        await using var reader = await cmd.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken);

        //Act
        await reader.ReadAsync(TestContext.Current.CancellationToken);

        //Assert
        FluentActions.Invoking(() => reader.GetInt32(0)).Should().Throw<Exception>()
            .WithMessage("Safe read exception as requested");
        reader.GetString(1).Should().Be("hello");
    }

    // Tests that when a type handler generates an exception that isn't a PgSqlSafeReadException, the connection is properly broken
    [Fact]
    public async Task non_safe_read_exception()
    {
        //Arrange
        var dataSourceBuilder = CreateDataSourceBuilder();
        // Temporarily reroute integer to go to a type handler which generates some exception
        dataSourceBuilder.AddTypeInfoResolverFactory(new ExplodingTypeHandlerResolverFactory(safe: false));
        await using var dataSource = dataSourceBuilder.Build();
        await using var connection = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        await using var cmd = new PgSqlCommand(@"SELECT 1, 'hello'", connection);
        await using var reader = await cmd.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken);

        //Act
        await reader.ReadAsync(TestContext.Current.CancellationToken);

        //Assert
        FluentActions.Invoking(() => reader.GetInt32(0)).Should().Throw<Exception>().WithMessage("Broken");
        connection.FullState.Should().Be(ConnectionState.Broken);
        connection.State.Should().Be(ConnectionState.Closed);
    }

    #region Cancellation

    // Cancels ReadAsync via the PgSqlCommand.Cancel, with successful PG cancellation
    [Fact]
    public async Task ReadAsync_cancel_command_soft()
    {
        if (IsMultiplexing)
            return; // Multiplexing, cancellation

        await using var postmasterMock = PgPostmasterMock.Start(ConnectionString);
        await using var dataSource = CreateDataSource(postmasterMock.ConnectionString);
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        // Write responses to the query we're about to send, with a single data row (we'll attempt to read two)
        var pgMock = await postmasterMock.WaitForServerConnection();
        await pgMock
            .WriteParseComplete()
            .WriteBindComplete()
            .WriteRowDescription(new FieldDescription(Int4Oid))
            .WriteDataRow(BitConverter.GetBytes(BinaryPrimitives.ReverseEndianness(1)))
            .FlushAsync();

        using var cmd = new PgSqlCommand("SELECT some_int FROM some_table", conn);
        await using (var reader = await cmd.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken))
        {
            // Successfully read the first row
            (await reader.ReadAsync(TestContext.Current.CancellationToken)).Should().BeTrue();
            reader.GetInt32(0).Should().Be(1);

            // Attempt to read the second row - simulate blocking and cancellation
            var task = reader.ReadAsync(TestContext.Current.CancellationToken);
            cmd.Cancel();

            var processId = (await postmasterMock.WaitForCancellationRequest()).ProcessId;
            processId.Should().Be(conn.ProcessID);

            await pgMock
                .WriteErrorResponse(PostgresErrorCodes.QueryCanceled)
                .WriteReadyForQuery()
                .FlushAsync();

            var exception = await Assert.ThrowsAsync<OperationCanceledException>(async () => await task);
            exception.InnerException.Should().BeOfType<PostgresException>()
                .Which.SqlState.Should().Be(PostgresErrorCodes.QueryCanceled);

            conn.FullState.Should().Be(ConnectionState.Open | ConnectionState.Fetching);
        }

        await pgMock.WriteScalarResponseAndFlush(1);
        (await conn.ExecuteScalarAsync("SELECT 1", cancellationToken: TestContext.Current.CancellationToken)).Should().Be(1);
    }

    // Cancels ReadAsync via the cancellation token, with successful PG cancellation
    [Fact]
    public async Task ReadAsync_cancel_soft()
    {
        if (IsMultiplexing)
            return; // Multiplexing, cancellation

        await using var postmasterMock = PgPostmasterMock.Start(ConnectionString);
        await using var dataSource = CreateDataSource(postmasterMock.ConnectionString);
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        // Write responses to the query we're about to send, with a single data row (we'll attempt to read two)
        var pgMock = await postmasterMock.WaitForServerConnection();
        await pgMock
            .WriteParseComplete()
            .WriteBindComplete()
            .WriteRowDescription(new FieldDescription(Int4Oid))
            .WriteDataRow(BitConverter.GetBytes(BinaryPrimitives.ReverseEndianness(1)))
            .FlushAsync();

        using var cmd = new PgSqlCommand("SELECT some_int FROM some_table", conn);
        await using (var reader = await cmd.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken))
        {
            // Successfully read the first row
            (await reader.ReadAsync(TestContext.Current.CancellationToken)).Should().BeTrue();
            reader.GetInt32(0).Should().Be(1);

            // Attempt to read the second row - simulate blocking and cancellation
            var cancellationSource = new CancellationTokenSource();
            var task = reader.ReadAsync(cancellationSource.Token);
            cancellationSource.Cancel();

            var processId = (await postmasterMock.WaitForCancellationRequest()).ProcessId;
            processId.Should().Be(conn.ProcessID);

            await pgMock
                .WriteErrorResponse(PostgresErrorCodes.QueryCanceled)
                .WriteReadyForQuery()
                .FlushAsync();

            var exception = await Assert.ThrowsAsync<OperationCanceledException>(async () => await task);
            exception.InnerException.Should().BeOfType<PostgresException>()
                .Which.SqlState.Should().Be(PostgresErrorCodes.QueryCanceled);
            exception.CancellationToken.Should().Be(cancellationSource.Token);

            conn.FullState.Should().Be(ConnectionState.Open | ConnectionState.Fetching);
        }

        await pgMock.WriteScalarResponseAndFlush(1);
        (await conn.ExecuteScalarAsync("SELECT 1", cancellationToken: TestContext.Current.CancellationToken)).Should().Be(1);
    }

    // Cancels NextResultAsync via the cancellation token, with successful PG cancellation
    [Fact]
    public async Task NextResult_cancel_soft()
    {
        if (IsMultiplexing)
            return; // Multiplexing, cancellation

        await using var postmasterMock = PgPostmasterMock.Start(ConnectionString);
        await using var dataSource = CreateDataSource(postmasterMock.ConnectionString);
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        // Write responses to the query we're about to send, only for the first resultset (we'll attempt to read two)
        var pgMock = await postmasterMock.WaitForServerConnection();
        await pgMock
            .WriteParseComplete()
            .WriteBindComplete()
            .WriteRowDescription(new FieldDescription(Int4Oid))
            .WriteDataRow(BitConverter.GetBytes(BinaryPrimitives.ReverseEndianness(1)))
            .WriteCommandComplete()
            .FlushAsync();

        using var cmd = new PgSqlCommand("SELECT 1; SELECT 2", conn);
        await using (var reader = await cmd.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken))
        {
            // Successfully read the first resultset
            (await reader.ReadAsync(TestContext.Current.CancellationToken)).Should().BeTrue();
            reader.GetInt32(0).Should().Be(1);

            // Attempt to advance to the second resultset - simulate blocking and cancellation
            var cancellationSource = new CancellationTokenSource();
            var task = reader.NextResultAsync(cancellationSource.Token);
            cancellationSource.Cancel();

            var processId = (await postmasterMock.WaitForCancellationRequest()).ProcessId;
            processId.Should().Be(conn.ProcessID);

            await pgMock
                .WriteErrorResponse(PostgresErrorCodes.QueryCanceled)
                .WriteReadyForQuery()
                .FlushAsync();

            var exception = await Assert.ThrowsAsync<OperationCanceledException>(async () => await task);
            exception.InnerException.Should().BeOfType<PostgresException>()
                .Which.SqlState.Should().Be(PostgresErrorCodes.QueryCanceled);
            exception.CancellationToken.Should().Be(cancellationSource.Token);

            conn.FullState.Should().Be(ConnectionState.Open | ConnectionState.Fetching);
        }

        await pgMock.WriteScalarResponseAndFlush(1);
        (await conn.ExecuteScalarAsync("SELECT 1", cancellationToken: TestContext.Current.CancellationToken)).Should().Be(1);
    }

    // Cancels ReadAsync via the cancellation token, with unsuccessful PG cancellation (socket break)
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ReadAsync_cancel_hard(bool passCancelledToken)
    {
        //Arrange
        if (IsMultiplexing)
            return; // Multiplexing, cancellation

        await using var postmasterMock = PgPostmasterMock.Start(ConnectionString);
        await using var dataSource = CreateDataSource(postmasterMock.ConnectionString);
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        // Write responses to the query we're about to send, with a single data row (we'll attempt to read two)
        var pgMock = await postmasterMock.WaitForServerConnection();
        await pgMock
            .WriteParseComplete()
            .WriteBindComplete()
            .WriteRowDescription(new FieldDescription(Int4Oid))
            .WriteDataRow(BitConverter.GetBytes(BinaryPrimitives.ReverseEndianness(1)))
            .FlushAsync();

        using var cmd = new PgSqlCommand("SELECT some_int FROM some_table", conn);

        //Act
        await using var reader = await cmd.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        // Successfully read the first row
        (await reader.ReadAsync(TestContext.Current.CancellationToken)).Should().BeTrue();
        reader.GetInt32(0).Should().Be(1);

        // Attempt to read the second row - simulate blocking and cancellation
        var cancellationSource = new CancellationTokenSource();
        if (passCancelledToken)
            cancellationSource.Cancel();
        var task = reader.ReadAsync(cancellationSource.Token);
        cancellationSource.Cancel();

        var processId = (await postmasterMock.WaitForCancellationRequest()).ProcessId;
        processId.Should().Be(conn.ProcessID);

        // Send no response from server, wait for the cancellation attempt to time out
        var exception = await Assert.ThrowsAsync<OperationCanceledException>(async () => await task);
        exception.InnerException.Should().BeOfType<TimeoutException>();
        exception.CancellationToken.Should().Be(cancellationSource.Token);

        conn.FullState.Should().Be(ConnectionState.Broken);
    }

    // Cancels NextResultAsync via the cancellation token, with unsuccessful PG cancellation (socket break)
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NextResultAsync_cancel_hard(bool passCancelledToken)
    {
        //Arrange
        if (IsMultiplexing)
            return; // Multiplexing, cancellation

        await using var postmasterMock = PgPostmasterMock.Start(ConnectionString);
        await using var dataSource = CreateDataSource(postmasterMock.ConnectionString);
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        // Write responses to the query we're about to send, with a single data row (we'll attempt to read two)
        var pgMock = await postmasterMock.WaitForServerConnection();
        await pgMock
            .WriteParseComplete()
            .WriteBindComplete()
            .WriteRowDescription(new FieldDescription(Int4Oid))
            .WriteDataRow(BitConverter.GetBytes(BinaryPrimitives.ReverseEndianness(1)))
            .WriteCommandComplete()
            .FlushAsync();

        using var cmd = new PgSqlCommand("SELECT some_int FROM some_table", conn);

        //Act
        await using var reader = await cmd.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        // Successfully read the first resultset
        (await reader.ReadAsync(TestContext.Current.CancellationToken)).Should().BeTrue();
        reader.GetInt32(0).Should().Be(1);

        // Attempt to read the second row - simulate blocking and cancellation
        var cancellationSource = new CancellationTokenSource();
        if (passCancelledToken)
            cancellationSource.Cancel();
        var task = reader.NextResultAsync(cancellationSource.Token);
        cancellationSource.Cancel();

        var processId = (await postmasterMock.WaitForCancellationRequest()).ProcessId;
        processId.Should().Be(conn.ProcessID);

        // Send no response from server, wait for the cancellation attempt to time out
        var exception = await Assert.ThrowsAsync<OperationCanceledException>(async () => await task);
        exception.InnerException.Should().BeOfType<TimeoutException>();
        exception.CancellationToken.Should().Be(cancellationSource.Token);

        conn.FullState.Should().Be(ConnectionState.Broken);
    }

    // Cancels sequential ReadAsGetFieldValueAsync
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GetFieldValueAsync_sequential_cancel(bool passCancelledToken)
    {
        //Arrange
        if (IsMultiplexing)
            return; // Multiplexing, cancellation

        if (!IsSequential)
            return;

        await using var postmasterMock = PgPostmasterMock.Start(ConnectionString);
        await using var dataSource = CreateDataSource(postmasterMock.ConnectionString);
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        // Write responses to the query we're about to send, with a single data row (we'll attempt to read two)
        var pgMock = await postmasterMock.WaitForServerConnection();
        await pgMock
            .WriteParseComplete()
            .WriteBindComplete()
            .WriteRowDescription(new FieldDescription(ByteaOid))
            .WriteDataRowWithFlush(new byte[10000]);

        using var cmd = new PgSqlCommand("SELECT some_bytea FROM some_table", conn);
        await using var reader = await cmd.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken);

        await reader.ReadAsync(TestContext.Current.CancellationToken);

        using var cts = new CancellationTokenSource();
        if (passCancelledToken)
            cts.Cancel();
        var task = reader.GetFieldValueAsync<byte[]>(0, cts.Token);

        //Act
        cts.Cancel();

        //Assert
        var exception = await Assert.ThrowsAsync<OperationCanceledException>(async () => await task);
        exception.InnerException.Should().BeNull();

        conn.FullState.Should().Be(ConnectionState.Broken);
    }

    // Cancels sequential ReadAsGetFieldValueAsync
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task IsDBNullAsync_sequential_cancel(bool passCancelledToken)
    {
        //Arrange
        if (IsMultiplexing)
            return; // Multiplexing, cancellation

        if (!IsSequential)
            return;

        await using var postmasterMock = PgPostmasterMock.Start(ConnectionString);
        await using var dataSource = CreateDataSource(postmasterMock.ConnectionString);
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        // Write responses to the query we're about to send, with a single data row (we'll attempt to read two)
        var pgMock = await postmasterMock.WaitForServerConnection();
        await pgMock
            .WriteParseComplete()
            .WriteBindComplete()
            .WriteRowDescription(new FieldDescription(ByteaOid), new FieldDescription(Int4Oid))
            .WriteDataRowWithFlush(new byte[10000], new byte[4]);

        using var cmd = new PgSqlCommand("SELECT some_bytea, some_int FROM some_table", conn);
        await using var reader = await cmd.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken);

        await reader.ReadAsync(TestContext.Current.CancellationToken);

        using var cts = new CancellationTokenSource();
        if (passCancelledToken)
            cts.Cancel();
        var task = reader.IsDBNullAsync(1, cts.Token);

        //Act
        cts.Cancel();

        //Assert
        var exception = await Assert.ThrowsAsync<OperationCanceledException>(async () => await task);
        exception.InnerException.Should().BeNull();

        conn.FullState.Should().Be(ConnectionState.Broken);
    }

    // Cancellation does not work with the multiplexing
    [Fact]
    public async Task cancel_multiplexing_disabled()
    {
        //Arrange
        if (!IsMultiplexing)
            return;

        await using var dataSource = CreateDataSource();
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using var cmd = new PgSqlCommand("SELECT generate_series(1, 100); SELECT generate_series(1, 100)", conn);
        await using var reader = await cmd.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken);
        var cancelledToken = new CancellationToken(canceled: true);

        //Assert
        (await reader.ReadAsync(TestContext.Current.CancellationToken)).Should().BeTrue();
        while (await reader.ReadAsync(cancelledToken)) { }
        (await reader.NextResultAsync(cancelledToken)).Should().BeTrue();
        while (await reader.ReadAsync(cancelledToken)) { }
        conn.Connector.UserCancellationRequested.Should().BeFalse();
    }

    #endregion Cancellation

    #region Timeout

    // Timeouts sequential ReadAsGetFieldValueAsync
    [Fact]
    public async Task GetFieldValueAsync_sequential_timeout()
    {
        //Arrange
        if (IsMultiplexing)
            return; // Multiplexing, cancellation

        if (!IsSequential)
            return;

        var csb = new PgSqlConnectionStringBuilder(ConnectionString)
        {
            CommandTimeout = 3,
            CancellationTimeout = 15000
        };

        await using var postmasterMock = PgPostmasterMock.Start(csb.ToString());
        await using var dataSource = CreateDataSource(postmasterMock.ConnectionString);
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        // Write responses to the query we're about to send, with a single data row (we'll attempt to read two)
        var pgMock = await postmasterMock.WaitForServerConnection();
        await pgMock
            .WriteParseComplete()
            .WriteBindComplete()
            .WriteRowDescription(new FieldDescription(ByteaOid))
            .WriteDataRowWithFlush(new byte[10000]);

        using var cmd = new PgSqlCommand("SELECT some_bytea FROM some_table", conn);
        await using var reader = await cmd.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken);

        await reader.ReadAsync(TestContext.Current.CancellationToken);

        //Act
        var task = reader.GetFieldValueAsync<byte[]>(0, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        var exception = await Assert.ThrowsAsync<PgSqlException>(async () => await task);
        exception.InnerException.Should().BeOfType<TimeoutException>();

        conn.FullState.Should().Be(ConnectionState.Broken);
    }

    // Timeouts sequential IsDBNullAsync
    [Fact]
    public async Task IsDBNullAsync_sequential_timeout()
    {
        //Arrange
        if (IsMultiplexing)
            return; // Multiplexing, cancellation

        if (!IsSequential)
            return;

        var csb = new PgSqlConnectionStringBuilder(ConnectionString)
        {
            CommandTimeout = 3,
            CancellationTimeout = 15000
        };

        await using var postmasterMock = PgPostmasterMock.Start(csb.ToString());
        await using var dataSource = CreateDataSource(postmasterMock.ConnectionString);
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        // Write responses to the query we're about to send, with a single data row (we'll attempt to read two)
        var pgMock = await postmasterMock.WaitForServerConnection();
        await pgMock
            .WriteParseComplete()
            .WriteBindComplete()
            .WriteRowDescription(new FieldDescription(ByteaOid), new FieldDescription(Int4Oid))
            .WriteDataRowWithFlush(new byte[10000], new byte[4]);

        using var cmd = new PgSqlCommand("SELECT some_bytea, some_int FROM some_table", conn);
        await using var reader = await cmd.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken);

        await reader.ReadAsync(TestContext.Current.CancellationToken);

        //Act
        var task = reader.GetFieldValueAsync<byte[]>(0, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        var exception = await Assert.ThrowsAsync<PgSqlException>(async () => await task);
        exception.InnerException.Should().BeOfType<TimeoutException>();

        conn.FullState.Should().Be(ConnectionState.Broken);
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/3446")]
    public async Task bug_3446()
    {
        //Arrange
        if (IsMultiplexing)
            return; // Multiplexing, cancellation

        await using var postmasterMock = PgPostmasterMock.Start(ConnectionString);
        await using var dataSource = CreateDataSource(postmasterMock.ConnectionString);
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        var pgMock = await postmasterMock.WaitForServerConnection();
        await pgMock
            .WriteParseComplete()
            .WriteBindComplete()
            .WriteRowDescription(new FieldDescription(Int4Oid))
            .WriteDataRow(new byte[4])
            .FlushAsync();

        using var cmd = new PgSqlCommand("SELECT some_int FROM some_table", conn);

        //Act
        await using (var reader = await cmd.ExecuteReaderAsync(Behavior, cancellationToken: TestContext.Current.CancellationToken))
        {
            await reader.ReadAsync(TestContext.Current.CancellationToken);
            cmd.Cancel();
            await postmasterMock.WaitForCancellationRequest();
            await pgMock
                .WriteErrorResponse(PostgresErrorCodes.QueryCanceled)
                .WriteReadyForQuery()
                .FlushAsync();
        }

        //Assert
        conn.Connector.State.Should().Be(ConnectorState.Ready);
    }

    // Consuming result set shouldn't go infinite in case connection is broken
    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/6160")]
    public async Task bug_6160()
    {
        var csb = new PgSqlConnectionStringBuilder(ConnectionString)
        {
            // Set to -1 to trigger immediate connection break on timeout
            CancellationTimeout = -1,
            CommandTimeout = 1
        };
        await using var postmasterMock = PgPostmasterMock.Start(csb.ConnectionString);
        await using var dataSource = CreateDataSource(postmasterMock.ConnectionString);
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        var pgMock = await postmasterMock.WaitForServerConnection();
        await pgMock
            .WriteParseComplete()
            .WriteBindComplete()
            .WriteRowDescription(new FieldDescription(Int4Oid))
            .WriteDataRow(new byte[4])
            .FlushAsync();

        await using var cmd = new PgSqlCommand("SELECT 1", conn);
        await using (var reader = await cmd.ExecuteReaderAsync(Behavior | CommandBehavior.SingleRow, cancellationToken: TestContext.Current.CancellationToken))
        {
            await reader.ReadAsync(TestContext.Current.CancellationToken);
            // The second read will try to consume the whole resultset due to CommandBehavior.SingleRow
            // Which will fail with timeout (and immediate connection break) since we didn't send anything else beside the first row
            var ex = await Assert.ThrowsAsync<PgSqlException>(async () => await reader.ReadAsync(TestContext.Current.CancellationToken));
            ex.InnerException.Should().BeOfType<TimeoutException>();

            conn.State.Should().Be(ConnectionState.Closed);
        }
    }

    #endregion

    #region Initialization / setup / teardown

    // ReSharper disable InconsistentNaming
    readonly bool IsSequential;
    readonly CommandBehavior Behavior;
    // ReSharper restore InconsistentNaming

    protected ReaderTests(MultiplexingMode multiplexingMode, CommandBehavior behavior) : base(multiplexingMode)
    {
        Behavior = behavior;
        IsSequential = (Behavior & CommandBehavior.SequentialAccess) != 0;
    }

    #endregion
}

public sealed class ReaderTests_NonMultiplexing_Default() : ReaderTests(MultiplexingMode.NonMultiplexing, CommandBehavior.Default);
public sealed class ReaderTests_Multiplexing_Default() : ReaderTests(MultiplexingMode.Multiplexing, CommandBehavior.Default);
public sealed class ReaderTests_NonMultiplexing_SequentialAccess() : ReaderTests(MultiplexingMode.NonMultiplexing, CommandBehavior.SequentialAccess);
public sealed class ReaderTests_Multiplexing_SequentialAccess() : ReaderTests(MultiplexingMode.Multiplexing, CommandBehavior.SequentialAccess);

#region Mock Type Handlers

sealed class ExplodingTypeHandlerResolverFactory(bool safe) : PgTypeInfoResolverFactory
{
    public override IPgTypeInfoResolver CreateResolver() => new Resolver(safe);
    public override IPgTypeInfoResolver CreateArrayResolver() => null;

    sealed class Resolver(bool safe) : IPgTypeInfoResolver
    {
        public PgTypeInfo GetTypeInfo(Type type, DataTypeName? dataTypeName, PgSerializerOptions options)
        {
            if (dataTypeName == DataTypeNames.Int4 && (type == typeof(int) || type is null))
                return new(options, new ExplodingTypeHandler(safe), DataTypeNames.Int4);

            return null;
        }
    }
}

class ExplodingTypeHandler : PgBufferedConverter<int>
{
    readonly bool _safe;

    internal ExplodingTypeHandler(bool safe) => _safe = safe;

    public override Size GetSize(SizeContext context, int value, ref object writeState)
        => throw new NotSupportedException();

    public override bool CanConvert(DataFormat format, out BufferRequirements bufferRequirements)
        => CanConvertBufferedDefault(format, out bufferRequirements);

    protected override void WriteCore(PgWriter writer, int value)
        => throw new NotSupportedException();

    protected override int ReadCore(PgReader reader)
    {
        if (_safe)
            throw new Exception("Safe read exception as requested");

        reader.BreakConnection();
        return default;
    }
}

#endregion
