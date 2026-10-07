using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.PostgresClient.Internal;
using CodeBrix.PostgresClient.PgSqlTypes;
using CodeBrix.PostgresClient.Tests.Support;
using SilverAssertions;
using Xunit;
using static CodeBrix.PostgresClient.Tests.TestUtil;

namespace CodeBrix.PostgresClient.Tests; //was previously: Npgsql.Tests;

public abstract class CopyTests(MultiplexingMode multiplexingMode) : MultiplexingTestBase(multiplexingMode)
{
    #region Issue 2257

    // Reproduce #2257
    [Fact]
    public async Task issue_2257()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        var table1 = await GetTempTableName(conn);
        var table2 = await GetTempTableName(conn);

        const int rowCount = 1000000;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"CREATE TABLE {table1} AS SELECT * FROM generate_series(1, {rowCount}) id";
            await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
            cmd.CommandText = $"ALTER TABLE {table1} ADD CONSTRAINT {table1}_pk PRIMARY KEY (id)";
            await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
            cmd.CommandText = $"CREATE TABLE {table2} (master_id integer NOT NULL REFERENCES {table1} (id))";
            await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        await using var writer = conn.BeginBinaryImport($"COPY {table2} FROM STDIN BINARY");
        writer.Timeout = TimeSpan.FromMilliseconds(3);

        //Act
        var e = Assert.Throws<PgSqlException>(() =>
        {
            for (var i = 1; i <= rowCount; ++i)
            {
                writer.StartRow();
                writer.Write(i);
            }

            writer.Complete();
        });

        //Assert
        e.InnerException.Should().BeOfType<TimeoutException>();
    }

    #endregion

    #region Raw

    // Exports data in binary format (raw mode) and then loads it back in
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task raw_binary_roundtrip(bool async)
    {
        using var conn = await OpenConnectionAsync();
        //var iterations = Conn.BufferSize / 10 + 100;
        //var iterations = Conn.BufferSize / 10 - 100;
        const int iterations = 500;

        var table = await GetTempTableName(conn);
        await conn.ExecuteNonQueryAsync($@"CREATE TABLE {table} (field_text TEXT, field_int2 SMALLINT, field_int4 INTEGER)", cancellationToken: TestContext.Current.CancellationToken);
        using (var tx = conn.BeginTransaction())
        {

            // Preload some data into the table
            using (var cmd =
                   new PgSqlCommand($"INSERT INTO {table} (field_text, field_int4) VALUES (@p1, @p2)", conn))
            {
                cmd.Parameters.AddWithValue("p1", PgSqlDbType.Text, "HELLO");
                cmd.Parameters.AddWithValue("p2", PgSqlDbType.Integer, 8);
                for (var i = 0; i < iterations; i++)
                {
                    await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
                }
            }

            await tx.CommitAsync(TestContext.Current.CancellationToken);
        }

        var data = new byte[10000];
        var len = 0;
        using (var outStream = async
                   ? await conn.BeginRawBinaryCopyAsync($"COPY {table} (field_text, field_int4) TO STDIN BINARY", TestContext.Current.CancellationToken)
                   : conn.BeginRawBinaryCopy($"COPY {table} (field_text, field_int4) TO STDIN BINARY"))
        {
            await StateAssertions(conn);

            while (true)
            {
                var read = outStream.Read(data, len, data.Length - len);
                if (read == 0)
                    break;
                len += read;
            }

            len.Should().BeGreaterThan(conn.Settings.ReadBufferSize).And.BeLessThan(data.Length);
        }

        await conn.ExecuteNonQueryAsync($"TRUNCATE {table}", cancellationToken: TestContext.Current.CancellationToken);

        using (var inStream = async
                   ? await conn.BeginRawBinaryCopyAsync($"COPY {table} (field_text, field_int4) FROM STDIN BINARY", TestContext.Current.CancellationToken)
                   : conn.BeginRawBinaryCopy($"COPY {table} (field_text, field_int4) FROM STDIN BINARY"))
        {
            await StateAssertions(conn);

            inStream.Write(data, 0, len);
        }

        (await conn.ExecuteScalarAsync($"SELECT COUNT(*) FROM {table}", cancellationToken: TestContext.Current.CancellationToken)).Should().Be((long)iterations);
    }

    // Disposes a raw binary stream in the middle of an export
    [Fact]
    public async Task Dispose_in_middle_of_raw_binary_export()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        var table = await GetTempTableName(conn);
        await conn.ExecuteNonQueryAsync($@"
CREATE TABLE {table} (field_text TEXT, field_int2 SMALLINT, field_int4 INTEGER);
INSERT INTO {table} (field_text, field_int4) VALUES ('HELLO', 8)", cancellationToken: TestContext.Current.CancellationToken);
        var data = new byte[3];

        //Act
        using (var inStream = conn.BeginRawBinaryCopy($"COPY {table} (field_text, field_int4) TO STDIN BINARY"))
        {
            // Read some bytes
            var len = inStream.Read(data, 0, data.Length);
            len.Should().Be(data.Length);
        }

        //Assert
        (await conn.ExecuteScalarAsync("SELECT 1", cancellationToken: TestContext.Current.CancellationToken)).Should().Be(1);
    }

    // Disposes a raw binary stream in the middle of an import
    [Fact]
    public async Task Dispose_in_middle_of_raw_binary_import()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        var table = await GetTempTableName(conn);
        await conn.ExecuteNonQueryAsync($@"CREATE TABLE {table} (field_text TEXT, field_int2 SMALLINT, field_int4 INTEGER)", cancellationToken: TestContext.Current.CancellationToken);
        var inStream = conn.BeginRawBinaryCopy($"COPY {table} (field_text, field_int4) FROM STDIN BINARY");
        inStream.Write(PgSqlRawCopyStream.BinarySignature, 0, PgSqlRawCopyStream.BinarySignature.Length);

        //Act
        var act = () => inStream.Dispose();

        //Assert
        act.Should().ThrowExactly<PostgresException>()
            .Where(e => e.SqlState == PostgresErrorCodes.BadCopyFileFormat);
        (await conn.ExecuteScalarAsync("SELECT 1", cancellationToken: TestContext.Current.CancellationToken)).Should().Be(1);
    }

    // Cancels a binary write
    [Fact]
    public async Task Cancel_raw_binary_import()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        var table = await GetTempTableName(conn);
        await conn.ExecuteNonQueryAsync($@"CREATE TABLE {table} (field_text TEXT, field_int2 SMALLINT, field_int4 INTEGER)", cancellationToken: TestContext.Current.CancellationToken);

        //Act
        await using (var tx = await conn.BeginTransactionAsync(TestContext.Current.CancellationToken))
        {
            var garbage = new byte[] {1, 2, 3, 4};
            using (var s = conn.BeginRawBinaryCopy($"COPY {table} (field_text, field_int4) FROM STDIN BINARY"))
            {
                s.Write(garbage, 0, garbage.Length);
                s.Cancel();
            }
        }

        //Assert
        (await conn.ExecuteScalarAsync($"SELECT COUNT(*) FROM {table}", cancellationToken: TestContext.Current.CancellationToken)).Should().Be(0L);
    }

    [Fact]
    public async Task import_large_value_raw()
    {
        using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "blob BYTEA");

        var data = new byte[conn.Settings.WriteBufferSize + 10];
        var dump = new byte[conn.Settings.WriteBufferSize + 200];
        var len = 0;

        // Insert a blob with a regular insert
        using (var cmd = new PgSqlCommand($"INSERT INTO {table} (blob) VALUES (@p)", conn))
        {
            cmd.Parameters.AddWithValue("p", data);
            await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        // Raw dump out
        using (var outStream = conn.BeginRawBinaryCopy($"COPY {table} (blob) TO STDIN BINARY"))
        {
            while (true)
            {
                var read = outStream.Read(dump, len, dump.Length - len);
                if (read == 0)
                    break;
                len += read;
            }
            len.Should().BeLessThan(dump.Length);
        }

        await conn.ExecuteNonQueryAsync($"TRUNCATE {table}", cancellationToken: TestContext.Current.CancellationToken);

        // And raw dump back in
        using (var inStream = conn.BeginRawBinaryCopy($"COPY {table} (blob) FROM STDIN BINARY"))
        {
            inStream.Write(dump, 0, len);
        }
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/2330")]
    public async Task wrong_table_definition_raw_binary_copy()
    {
        using var conn = await OpenConnectionAsync();
        Assert.Throws<PostgresException>(() => conn.BeginRawBinaryCopy("COPY table_is_not_exist (blob) TO STDOUT BINARY"));
        conn.FullState.Should().Be(ConnectionState.Open);
        (await conn.ExecuteScalarAsync("SELECT 1", cancellationToken: TestContext.Current.CancellationToken)).Should().Be(1);

        Assert.Throws<PostgresException>(() => conn.BeginRawBinaryCopy("COPY table_is_not_exist (blob) FROM STDIN BINARY"));
        conn.FullState.Should().Be(ConnectionState.Open);
        (await conn.ExecuteScalarAsync("SELECT 1", cancellationToken: TestContext.Current.CancellationToken)).Should().Be(1);
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/2330")]
    public async Task wrong_format_raw_binary_copy()
    {
        if (IsMultiplexing)
            Assert.Skip("Multiplexing: fails");
        using (var conn = await OpenConnectionAsync())
        {
            var table = await CreateTempTable(conn, "blob BYTEA");
            Assert.Throws<ArgumentException>(() => conn.BeginRawBinaryCopy($"COPY {table} (blob) TO STDOUT"));
            conn.FullState.Should().Be(ConnectionState.Broken);
        }

        using (var conn = await OpenConnectionAsync())
        {
            var table = await CreateTempTable(conn, "blob BYTEA");
            Assert.Throws<ArgumentException>(() => conn.BeginRawBinaryCopy($"COPY {table} (blob) FROM STDIN"));
            conn.FullState.Should().Be(ConnectionState.Broken);
        }
    }

    #endregion

    #region Binary

    // Roundtrips some data
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task binary_roundtrip(bool async)
    {
        using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "field_text TEXT, field_int2 SMALLINT");

        var longString = new StringBuilder(conn.Settings.WriteBufferSize + 50).Append('a').ToString();

        using (var writer = async
                   ? await conn.BeginBinaryImportAsync($"COPY {table} (field_text, field_int2) FROM STDIN BINARY", TestContext.Current.CancellationToken)
                   : conn.BeginBinaryImport($"COPY {table} (field_text, field_int2) FROM STDIN BINARY"))
        {
            await StateAssertions(conn);

            writer.StartRow();
            writer.Write("Hello");
            writer.Write((short)8, PgSqlDbType.Smallint);

            writer.WriteRow("Something", (short)9);

            writer.StartRow();
            writer.Write(longString, "text");
            writer.WriteNull();

            var rowsWritten = writer.Complete();
            rowsWritten.Should().Be(3UL);
        }

        (await conn.ExecuteScalarAsync("SELECT 1", cancellationToken: TestContext.Current.CancellationToken)).Should().Be(1);

        using (var reader = async
                   ? await conn.BeginBinaryExportAsync($"COPY {table} (field_text, field_int2) TO STDIN BINARY", TestContext.Current.CancellationToken)
                   : conn.BeginBinaryExport($"COPY {table} (field_text, field_int2) TO STDIN BINARY"))
        {
            await StateAssertions(conn);

            reader.StartRow().Should().Be(2);
            reader.Read<string>().Should().Be("Hello");
            reader.Read<int>(PgSqlDbType.Smallint).Should().Be(8);

            reader.StartRow().Should().Be(2);
            reader.IsNull.Should().BeFalse();
            reader.Read<string>().Should().Be("Something");
            reader.Skip();

            reader.StartRow().Should().Be(2);
            reader.Read<string>().Should().Be(longString);
            reader.IsNull.Should().BeTrue();
            reader.IsNull.Should().BeTrue();
            reader.Skip();

            reader.StartRow().Should().Be(-1);
        }

        (await conn.ExecuteScalarAsync("SELECT 1", cancellationToken: TestContext.Current.CancellationToken)).Should().Be(1);
    }

    [Fact]
    public async Task Cancel_binary_import()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "field_text TEXT, field_int2 SMALLINT, field_int4 INTEGER");

        //Act
        await using (var tx = await conn.BeginTransactionAsync(TestContext.Current.CancellationToken))
        {
            using (var writer = conn.BeginBinaryImport($"COPY {table} (field_text, field_int4) FROM STDIN BINARY"))
            {
                writer.StartRow();
                writer.Write("Hello");
                writer.Write(8);
                // No commit should rollback
            }
        }

        //Assert
        (await conn.ExecuteScalarAsync($"SELECT COUNT(*) FROM {table}", cancellationToken: TestContext.Current.CancellationToken)).Should().Be(0L);
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/657")]
    public async Task import_bytea()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "field BYTEA");
        var data = new byte[] {1, 5, 8};

        //Act
        using (var writer = conn.BeginBinaryImport($"COPY {table} (field) FROM STDIN BINARY"))
        {
            writer.StartRow();
            writer.Write(data, PgSqlDbType.Bytea);
            var rowsWritten = writer.Complete();
            rowsWritten.Should().Be(1UL);
        }

        //Assert
        ((byte[])await conn.ExecuteScalarAsync($"SELECT field FROM {table}", cancellationToken: TestContext.Current.CancellationToken)).Should().Equal(data);
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/4693")]
    public async Task import_numeric()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "field NUMERIC(1000)");

        //Act
        await using (var writer = await conn.BeginBinaryImportAsync($"COPY {table} (field) FROM STDIN BINARY", TestContext.Current.CancellationToken))
        {
            await writer.StartRowAsync(TestContext.Current.CancellationToken);
            await writer.WriteAsync(new BigInteger(1234), PgSqlDbType.Numeric, TestContext.Current.CancellationToken);
            await writer.StartRowAsync(TestContext.Current.CancellationToken);
            await writer.WriteAsync(new BigInteger(5678), PgSqlDbType.Numeric, TestContext.Current.CancellationToken);

            var rowsWritten = await writer.CompleteAsync(TestContext.Current.CancellationToken);
            rowsWritten.Should().Be(2UL);
        }

        //Assert
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT field FROM {table}";
        await using var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        (await reader.ReadAsync(TestContext.Current.CancellationToken)).Should().BeTrue();
        reader.GetValue(0).Should().Be(1234m);
        (await reader.ReadAsync(TestContext.Current.CancellationToken)).Should().BeTrue();
        reader.GetValue(0).Should().Be(5678m);
    }

    [Fact]
    public async Task import_string_array()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "field TEXT[]");
        var data = new[] {"foo", "a", "bar"};

        //Act
        using (var writer = conn.BeginBinaryImport($"COPY {table} (field) FROM STDIN BINARY"))
        {
            writer.StartRow();
            writer.Write(data, PgSqlDbType.Array | PgSqlDbType.Text);
            var rowsWritten = writer.Complete();
            rowsWritten.Should().Be(1UL);
        }

        //Assert
        ((string[])await conn.ExecuteScalarAsync($"SELECT field FROM {table}", cancellationToken: TestContext.Current.CancellationToken)).Should().Equal(data);
    }

    [Fact]
    public async Task import_DBNull_then_other_object()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "field TEXT");
        object data = "foo";

        //Act
        using (var writer = conn.BeginBinaryImport($"COPY {table} (field) FROM STDIN BINARY"))
        {
            writer.StartRow();
            writer.Write((object)DBNull.Value);
            writer.StartRow();
            writer.Write(data);
            var rowsWritten = writer.Complete();
            rowsWritten.Should().Be(2UL);
        }

        //Assert
        (await conn.ExecuteScalarAsync($"SELECT field FROM {table} OFFSET 1", cancellationToken: TestContext.Current.CancellationToken)).Should().Be(data);
    }

    [Fact]
    public async Task import_reused_instance_mapping_info_identical_or_throws()
    {
        using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "field int4");

        var data = 8;
        using (var writer = conn.BeginBinaryImport($"COPY {table} (field) FROM STDIN BINARY"))
        {
            writer.StartRow();
            writer.Write(data, PgSqlDbType.Integer);
            writer.StartRow();
            FluentActions.Invoking(() => writer.Write(data, "int2")).Should().ThrowExactly<InvalidOperationException>()
                .WithMessage("Write for column 0 resolves to a different PostgreSQL type*");
            // Should be recoverable by using the same type again.
            writer.Write(data, "int4");
            writer.Complete();
        }
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/816")]
    public async Task import_string_with_buffer_length()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "field TEXT");
        var data = new string('a', conn.Settings.WriteBufferSize);

        //Act
        using (var writer = conn.BeginBinaryImport($"COPY {table} (field) FROM STDIN BINARY"))
        {
            writer.StartRow();
            writer.Write(data, PgSqlDbType.Text);
            var rowsWritten = writer.Complete();
            rowsWritten.Should().Be(1UL);
        }

        //Assert
        (await conn.ExecuteScalarAsync($"SELECT field FROM {table}", cancellationToken: TestContext.Current.CancellationToken)).Should().Be(data);
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/662")]
    public async Task import_direct_buffer()
    {
        using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "blob BYTEA");

        using var writer = conn.BeginBinaryImport($"COPY {table} (blob) FROM STDIN BINARY");
        // Big value - triggers use of the direct write optimization
        var data = new byte[conn.Settings.WriteBufferSize + 10];

        writer.StartRow();
        writer.Write(data);
        writer.StartRow();
        writer.Write(data);
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/5330")]
    public async Task import_object_null()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "field TEXT[]");

        //Act
        using (var writer = conn.BeginBinaryImport($"COPY {table} (field) FROM STDIN BINARY"))
        {
            writer.StartRow();
            writer.Write<object>(null, PgSqlDbType.Boolean);
            var rowsWritten = writer.Complete();
            rowsWritten.Should().Be(1UL);
        }

        //Assert
        (await conn.ExecuteScalarAsync($"SELECT field FROM {table}", cancellationToken: TestContext.Current.CancellationToken)).Should().Be(DBNull.Value);
    }

    public static readonly TheoryData<DBNull> DBNullValues = new()
    {
        DBNull.Value,
        null
    };

    [Theory]
    [MemberData(nameof(DBNullValues))]
    public async Task import_dbnull(DBNull value)
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "field TEXT[]");

        //Act
        using (var writer = conn.BeginBinaryImport($"COPY {table} (field) FROM STDIN BINARY"))
        {
            writer.StartRow();
            writer.Write(value, PgSqlDbType.Boolean);
            var rowsWritten = writer.Complete();
            rowsWritten.Should().Be(1UL);
        }

        //Assert
        (await conn.ExecuteScalarAsync($"SELECT field FROM {table}", cancellationToken: TestContext.Current.CancellationToken)).Should().Be(DBNull.Value);
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/2330")]
    public async Task wrong_table_definition_binary_import()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();

        //Act
        // Connection should be kept alive after PostgresException was triggered
        Assert.Throws<PostgresException>(() => conn.BeginBinaryImport("COPY table_is_not_exist (blob) FROM STDIN BINARY"));

        //Assert
        conn.FullState.Should().Be(ConnectionState.Open);
        (await conn.ExecuteScalarAsync("SELECT 1", cancellationToken: TestContext.Current.CancellationToken)).Should().Be(1);
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/2330")]
    public async Task wrong_format_binary_import()
    {
        if (IsMultiplexing)
            Assert.Skip("Multiplexing: fails");
        using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "blob BYTEA");
        Assert.Throws<ArgumentException>(() => conn.BeginBinaryImport($"COPY {table} (blob) FROM STDIN"));
        conn.FullState.Should().Be(ConnectionState.Broken);
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/2330")]
    public async Task wrong_table_definition_binary_export()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();

        //Act
        // Connection should be kept alive after PostgresException was triggered
        Assert.Throws<PostgresException>(() => conn.BeginBinaryExport("COPY table_is_not_exist (blob) TO STDOUT BINARY"));

        //Assert
        conn.FullState.Should().Be(ConnectionState.Open);
        (await conn.ExecuteScalarAsync("SELECT 1", cancellationToken: TestContext.Current.CancellationToken)).Should().Be(1);
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/5457")]
    public async Task mixed_operations()
    {
        if (IsMultiplexing)
            Assert.Skip("Multiplexing: fails");
        using var conn = await OpenConnectionAsync();

        using var reader = conn.BeginBinaryExport("""
            COPY (values ('foo', 1), ('bar', null), (null, 2)) TO STDOUT BINARY
            """);
        while(reader.StartRow() != -1)
        {
            string col1 = null;
            if (reader.IsNull)
                reader.Skip();
            else
                col1 = reader.Read<string>();
            int? col2 = null;
            if (reader.IsNull)
                reader.Skip();
            else
                col2 = reader.Read<int>();
        }
    }

    [Fact]
    public async Task read_more_columns_than_exist()
    {
        if (IsMultiplexing)
            Assert.Skip("Multiplexing: fails");
        using var conn = await OpenConnectionAsync();

        using var reader = conn.BeginBinaryExport("""
            COPY (values ('foo', 1), ('bar', null), (null, 2)) TO STDOUT BINARY
            """);
        while(reader.StartRow() != -1)
        {
            string col1 = null;
            if (reader.IsNull)
                reader.Skip();
            else
                col1 = reader.Read<string>();
            int? col2 = null;
            if (reader.IsNull)
                reader.Skip();
            else
                col2 = reader.Read<int>();

            Assert.Throws<InvalidOperationException>(() => _ = reader.IsNull);
        }
    }

    [Fact]
    public async Task read_zero_sized_columns()
    {
        if (IsMultiplexing)
            Assert.Skip("Multiplexing: fails");
        using var conn = await OpenConnectionAsync();

        using var reader = conn.BeginBinaryExport("""
            COPY (values (1, '', ''), (2, null, ''), (3, '', null)) TO STDOUT BINARY
            """);
        while(reader.StartRow() != -1)
        {
            int? col1 = null;
            if (reader.IsNull)
                reader.Skip();
            else
                col1 = reader.Read<int>();

            string col2 = null;
            if (reader.IsNull)
                reader.Skip();
            else
                col2 = reader.Read<string>();

            string col3 = null;
            if (reader.IsNull)
                reader.Skip();
            else
                col3 = reader.Read<string>();
        }
    }

    [Fact]
    public async Task read_converter_resolver_type()
    {
        if (IsMultiplexing)
            Assert.Skip("Multiplexing: fails");
        using var conn = await OpenConnectionAsync();

        using (var reader = conn.BeginBinaryExport("""
                   COPY (values (NOW()), (NULL)) TO STDOUT BINARY
                   """))
        {
            while (reader.StartRow() != -1)
            {
                DateTime? col1 = null;
                if (reader.IsNull)
                    reader.Skip();
                else
                    col1 = reader.Read<DateTime>();
            }
        }

        using (var reader = conn.BeginBinaryExport("""
                   COPY (values (NOW()), (NULL)) TO STDOUT BINARY
                   """))
        {
            while (reader.StartRow() != -1)
            {
                DateTimeOffset? col1 = null;
                if (reader.IsNull)
                    reader.Skip();
                else
                    col1 = reader.Read<DateTimeOffset>();
            }
        }
    }

    [Fact]
    public async Task streaming_read()
    {
        if (IsMultiplexing)
            Assert.Skip("Multiplexing: fails");
        using var conn = await OpenConnectionAsync();

        var str = new string('a', PgReader.MaxPreparedTextReaderSize + 1);
        var reader = conn.BeginBinaryExport($"""COPY (values ('{str}')) TO STDOUT BINARY""");
        while (reader.StartRow() != -1)
        {
            using var _ = reader.Read<TextReader>(PgSqlDbType.Text);
        }
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/2330")]
    public async Task wrong_format_binary_export()
    {
        if (IsMultiplexing)
            Assert.Skip("Multiplexing: fails");
        using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "blob BYTEA");
        Assert.Throws<ArgumentException>(() => conn.BeginBinaryExport($"COPY {table} (blob) TO STDOUT"));
        conn.FullState.Should().Be(ConnectionState.Broken);
    }

    [Fact(Explicit = true), IssueLink("https://github.com/npgsql/npgsql/issues/657")]
    public async Task import_bytea_massive()
    {
        using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "field BYTEA");

        const int iterations = 10000;
        var data = new byte[1024*1024];

        using (var writer = conn.BeginBinaryImport($"COPY {table} (field) FROM STDIN BINARY"))
        {
            for (var i = 0; i < iterations; i++)
            {
                if (i%100 == 0)
                    Console.WriteLine("Iteration " + i);
                writer.StartRow();
                writer.Write(data, PgSqlDbType.Bytea);
            }
        }

        (await conn.ExecuteScalarAsync($"SELECT COUNT(*) FROM {table}", cancellationToken: TestContext.Current.CancellationToken)).Should().Be((long)iterations);
    }

    [Fact]
    public async Task export_long_string()
    {
        const int iterations = 100;
        using var conn = await OpenConnectionAsync();
        var len = conn.Settings.WriteBufferSize;
        var table = await CreateTempTable(conn, "foo1 TEXT, foo2 TEXT, foo3 TEXT, foo4 TEXT, foo5 TEXT");
        using (var cmd = new PgSqlCommand($"INSERT INTO {table} VALUES (@p, @p, @p, @p, @p)", conn))
        {
            cmd.Parameters.AddWithValue("p", new string('x', len));
            for (var i = 0; i < iterations; i++)
                await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        using (var reader = conn.BeginBinaryExport($"COPY {table} (foo1, foo2, foo3, foo4, foo5) TO STDIN BINARY"))
        {
            int row, col = 0;
            for (row = 0; row < iterations; row++)
            {
                reader.StartRow().Should().Be(5);
                for (col = 0; col < 5; col++)
                {
                    var str = reader.Read<string>();
                    str.Length.Should().Be(len);
                    (str.AsSpan().IndexOfAnyExcept('x') is -1).Should().BeTrue();
                }
            }
            row.Should().Be(100);
            col.Should().Be(5);
        }
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/1134")]
    public async Task Read_bit_string()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        var table = await GetTempTableName(conn);
        await conn.ExecuteNonQueryAsync($@"
CREATE TABLE {table} (bits BIT(11), bitvector BIT(11), bitarray BIT(3)[]);
INSERT INTO {table} (bits, bitvector, bitarray) VALUES (B'00000001101', B'00000001101', ARRAY[B'101', B'111'])", cancellationToken: TestContext.Current.CancellationToken);

        //Act
        using var reader = conn.BeginBinaryExport($"COPY {table} (bits, bitvector, bitarray) TO STDIN BINARY");
        reader.StartRow();
        var bits = reader.Read<BitArray>();
        var bitVector = reader.Read<BitVector32>();
        var bitArrays = reader.Read<BitArray[]>();

        //Assert
        var expectedBits = new BitArray([false, false, false, false, false, false, false, true, true, false, true]);
        ValueEquality.AreEqual(expectedBits, bits).Should().BeTrue(
            $"expected {ValueEquality.Format(expectedBits)} but got {ValueEquality.Format(bits)}");
        bitVector.Should().Be(new BitVector32(0b00000001101000000000000000000000));
        var expectedBitArrays = new[]
        {
            new BitArray([true, false, true]),
            new BitArray([true, true, true])
        };
        ValueEquality.AreEqual(expectedBitArrays, bitArrays).Should().BeTrue(
            $"expected {ValueEquality.Format(expectedBitArrays)} but got {ValueEquality.Format(bitArrays)}");
    }

    [Fact]
    public async Task array()
    {
        //Arrange
        var expected = new[] { 8 };
        using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "arr INTEGER[]");

        //Act
        using (var writer = conn.BeginBinaryImport($"COPY {table} (arr) FROM STDIN BINARY"))
        {
            writer.StartRow();
            writer.Write(expected);
            var rowsWritten = writer.Complete();
            rowsWritten.Should().Be(1UL);
        }

        //Assert
        using (var reader = conn.BeginBinaryExport($"COPY {table} (arr) TO STDIN BINARY"))
        {
            reader.StartRow();
            reader.Read<int[]>().Should().Equal(expected);
        }
    }

    [Fact]
    public async Task @enum()
    {
        //Arrange
        await using var adminConnection = await OpenConnectionAsync();
        var type = await GetTempTypeName(adminConnection);
        await adminConnection.ExecuteNonQueryAsync($"CREATE TYPE {type} AS ENUM ('sad', 'ok', 'happy')", cancellationToken: TestContext.Current.CancellationToken);

        var dataSourceBuilder = CreateDataSourceBuilder();
        dataSourceBuilder.MapEnum<Mood>(type);
        await using var dataSource = dataSourceBuilder.Build();
        await using var connection = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        var table = await CreateTempTable(connection, $"mymood {type}, mymoodarr {type}[]");

        //Act
        await using (var writer = await connection.BeginBinaryImportAsync($"COPY {table} (mymood, mymoodarr) FROM STDIN BINARY", TestContext.Current.CancellationToken))
        {
            await writer.StartRowAsync(TestContext.Current.CancellationToken);
            await writer.WriteAsync(Mood.Happy, TestContext.Current.CancellationToken);
            await writer.WriteAsync(new[] { Mood.Happy }, TestContext.Current.CancellationToken);
            var rowsWritten = await writer.CompleteAsync(TestContext.Current.CancellationToken);
            rowsWritten.Should().Be(1UL);
        }

        //Assert
        await using (var reader = await connection.BeginBinaryExportAsync($"COPY {table} (mymood, mymoodarr) TO STDIN BINARY", TestContext.Current.CancellationToken))
        {
            await reader.StartRowAsync(TestContext.Current.CancellationToken);
            reader.Read<Mood>().Should().Be(Mood.Happy);
            reader.Read<Mood[]>().Should().Equal(new[] { Mood.Happy });
        }
    }

    enum Mood { Sad, Ok, Happy };

    [Fact]
    public async Task Read_null_as_nullable()
    {
        //Arrange
        using var connection = await OpenConnectionAsync();
        using var exporter = connection.BeginBinaryExport("COPY (SELECT NULL::int) TO STDOUT BINARY");
        exporter.StartRow();

        //Act
        var value = exporter.Read<int?>();

        //Assert
        value.Should().BeNull();
    }

    [Fact]
    public async Task Read_null_as_non_nullable_throws()
    {
        //Arrange
        using var connection = await OpenConnectionAsync();
        using var exporter = connection.BeginBinaryExport("COPY (SELECT NULL::int) TO STDOUT BINARY");
        exporter.StartRow();

        //Act
        //Assert
        Assert.Throws<InvalidCastException>(() => exporter.Read<int>());
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/1440")]
    public async Task error_during_import()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "foo INT UNIQUE");
        var writer = conn.BeginBinaryImport($"COPY {table} (foo) FROM STDIN BINARY");
        writer.StartRow();
        writer.Write(8);
        writer.StartRow();
        writer.Write(8);

        //Act
        var act = () => writer.Complete();

        //Assert
        act.Should().ThrowExactly<PostgresException>()
            .Where(e => e.SqlState == PostgresErrorCodes.UniqueViolation);
        (await conn.ExecuteScalarAsync("SELECT 1", cancellationToken: TestContext.Current.CancellationToken)).Should().Be(1);
    }

    [Fact]
    public async Task import_cannot_write_after_commit()
    {
        using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "foo INT");
        try
        {
            using var writer = conn.BeginBinaryImport($"COPY {table} (foo) FROM STDIN BINARY");
            writer.StartRow();
            writer.Write(8);
            var rowsWritten = writer.Complete();
            rowsWritten.Should().Be(1UL);
            writer.StartRow();
            Assert.Fail("StartRow should have thrown");
        }
        catch (InvalidOperationException)
        {
            (await conn.ExecuteScalarAsync($"SELECT COUNT(*) FROM {table}", cancellationToken: TestContext.Current.CancellationToken)).Should().Be(1L);
        }
    }

    [Fact]
    public async Task import_commit_in_middle_of_row()
    {
        using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "foo INT, bar TEXT");

        try
        {
            using var writer = conn.BeginBinaryImport($"COPY {table} (foo, bar) FROM STDIN BINARY");
            writer.StartRow();
            writer.Write(8);
            writer.Write("hello");
            writer.StartRow();
            writer.Write(9);
            writer.Complete();
            Assert.Fail("Commit should have thrown");
        }
        catch (InvalidOperationException)
        {
            (await conn.ExecuteScalarAsync($"SELECT COUNT(*) FROM {table}", cancellationToken: TestContext.Current.CancellationToken)).Should().Be(0L);
        }
    }

    [Fact]
    public async Task import_exception_does_not_commit()
    {
        using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "foo INT");

        try
        {
            using var writer = conn.BeginBinaryImport($"COPY {table} (foo) FROM STDIN BINARY");
            writer.StartRow();
            writer.Write(8);
            throw new Exception("FOO");
        }
        catch (Exception e) when (e.Message == "FOO")
        {
            (await conn.ExecuteScalarAsync($"SELECT COUNT(*) FROM {table}", cancellationToken: TestContext.Current.CancellationToken)).Should().Be(0L);
        }
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/2347")]
    public async Task Write_column_out_of_bounds_throws()
    {
        using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "field_text TEXT, field_int2 INTEGER");

        using var writer = conn.BeginBinaryImport($"COPY {table} (field_text, field_int2) FROM STDIN BINARY");
        await StateAssertions(conn);

        writer.StartRow();
        writer.Write("Hello");
        writer.Write(8, PgSqlDbType.Smallint);

        Assert.Throws<InvalidOperationException>(() => writer.Write("I should not be here"));

        writer.StartRow();
        writer.Write("Hello");
        writer.Write(8, PgSqlDbType.Smallint);

        Assert.Throws<InvalidOperationException>(() => writer.Write("I should not be here", PgSqlDbType.Text));

        writer.StartRow();
        writer.Write("Hello");
        writer.Write(8, PgSqlDbType.Smallint);

        Assert.Throws<InvalidOperationException>(() => writer.Write("I should not be here", "text"));
        Assert.Throws<InvalidOperationException>(() => writer.WriteRow("Hello", 8, "I should not be here"));
    }

    [Fact]
    public async Task Cancel_raw_binary_export_when_not_consumed_and_then_Dispose()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();

        //Act
        await using (var tx = await conn.BeginTransactionAsync(TestContext.Current.CancellationToken))
        {
            // This must be large enough to cause Postgres to queue up CopyData messages.
            var stream = conn.BeginRawBinaryCopy("COPY (select md5(random()::text) as id from generate_series(1, 100000)) TO STDOUT BINARY");
            var buffer = new byte[32];
            await stream.ReadExactlyAsync(buffer, 0, buffer.Length, TestContext.Current.CancellationToken);
            stream.Cancel();
            await FluentActions.Awaiting(async () => await stream.DisposeAsync()).Should().NotThrowAsync();
        }

        //Assert
        (await conn.ExecuteScalarAsync("SELECT 1", cancellationToken: TestContext.Current.CancellationToken)).Should().Be(1, "the connection is still OK");
    }

    [Fact]
    public async Task Cancel_binary_export_when_not_consumed_and_then_Dispose()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();

        //Act
        await using (var tx = await conn.BeginTransactionAsync(TestContext.Current.CancellationToken))
        {
            // This must be large enough to cause Postgres to queue up CopyData messages.
            var exporter = conn.BeginBinaryExport("COPY (select md5(random()::text) as id from generate_series(1, 100000)) TO STDOUT BINARY");
            await exporter.StartRowAsync(TestContext.Current.CancellationToken);
            await exporter.ReadAsync<string>(TestContext.Current.CancellationToken);
            exporter.Cancel();
            await FluentActions.Awaiting(async () => await exporter.DisposeAsync()).Should().NotThrowAsync();
        }

        //Assert
        (await conn.ExecuteScalarAsync("SELECT 1", cancellationToken: TestContext.Current.CancellationToken)).Should().Be(1, "the connection is still OK");
    }

    [Fact]
    [IssueLink("https://github.com/npgsql/npgsql/issues/5110")]
    public async Task binary_copy_read_char_column()
    {
        await using var conn = await OpenConnectionAsync();
        var tableName = await CreateTempTable(conn, "id serial, value char");

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"INSERT INTO {tableName}(value) VALUES ('d'), ('s')";
        await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);

        await using var export = await conn.BeginBinaryExportAsync($"COPY {tableName}(id, value) TO STDOUT (FORMAT BINARY)", TestContext.Current.CancellationToken);
        while (await export.StartRowAsync(TestContext.Current.CancellationToken) != -1)
        {
            var id = export.Read<int>();
            var value = export.Read<char>();
        }
    }

    #endregion

    #region Text

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task text_import(bool async)
    {
        using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "field_text TEXT, field_int2 SMALLINT, field_int4 INTEGER");
        const string line = "HELLO\t1\n";

        // Short write
        var writer = async
            ? await conn.BeginTextImportAsync($"COPY {table} (field_text, field_int4) FROM STDIN", TestContext.Current.CancellationToken)
            : conn.BeginTextImport($"COPY {table} (field_text, field_int4) FROM STDIN");
        await StateAssertions(conn);
        writer.Write(line);
        writer.Dispose();
        (await conn.ExecuteScalarAsync($"SELECT COUNT(*) FROM {table} WHERE field_int4=1", cancellationToken: TestContext.Current.CancellationToken)).Should().Be(1L);
        FluentActions.Invoking(() => writer.Write(line)).Should().ThrowExactly<ObjectDisposedException>();
        await conn.ExecuteNonQueryAsync($"TRUNCATE {table}", cancellationToken: TestContext.Current.CancellationToken);

        // Long (multi-buffer) write
        var iterations = PgSqlWriteBuffer.MinimumSize/line.Length + 100;
        writer = async
            ? await conn.BeginTextImportAsync($"COPY {table} (field_text, field_int4) FROM STDIN", TestContext.Current.CancellationToken)
            : conn.BeginTextImport($"COPY {table} (field_text, field_int4) FROM STDIN");
        for (var i = 0; i < iterations; i++)
            writer.Write(line);
        writer.Dispose();
        (await conn.ExecuteScalarAsync($"SELECT COUNT(*) FROM {table} WHERE field_int4=1", cancellationToken: TestContext.Current.CancellationToken)).Should().Be((long)iterations);
    }

    [Fact]
    public async Task Cancel_text_import()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "field_text TEXT, field_int2 SMALLINT, field_int4 INTEGER");

        //Act
        await using (var tx = await conn.BeginTransactionAsync(TestContext.Current.CancellationToken))
        {
            var writer = (PgSqlCopyTextWriter)conn.BeginTextImport($"COPY {table} (field_text, field_int4) FROM STDIN");
            writer.Write("HELLO\t1\n");
            writer.Cancel();
        }

        //Assert
        (await conn.ExecuteScalarAsync($"SELECT COUNT(*) FROM {table}", cancellationToken: TestContext.Current.CancellationToken)).Should().Be(0L);
    }

    [Fact]
    public async Task text_import_empty()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "field_text TEXT, field_int2 SMALLINT, field_int4 INTEGER");

        //Act
        using (conn.BeginTextImport($"COPY {table} (field_text, field_int4) FROM STDIN"))
        {
        }

        //Assert
        (await conn.ExecuteScalarAsync($"SELECT COUNT(*) FROM {table}", cancellationToken: TestContext.Current.CancellationToken)).Should().Be(0L);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task text_export(bool async)
    {
        using var conn = await OpenConnectionAsync();
        var table = await GetTempTableName(conn);

        await conn.ExecuteNonQueryAsync($@"
CREATE  TABLE {table} (field_text TEXT, field_int2 SMALLINT, field_int4 INTEGER);
INSERT INTO {table} (field_text, field_int4) VALUES ('HELLO', 1)", cancellationToken: TestContext.Current.CancellationToken);

        var chars = new char[30];

        // Short read
        var reader = async
            ? await conn.BeginTextExportAsync($"COPY {table} (field_text, field_int4) TO STDIN", TestContext.Current.CancellationToken)
            : conn.BeginTextExport($"COPY {table} (field_text, field_int4) TO STDIN");
        await StateAssertions(conn);
        reader.Read(chars, 0, chars.Length).Should().Be(8);
        new string(chars, 0, 8).Should().Be("HELLO\t1\n");
        reader.Read(chars, 0, chars.Length).Should().Be(0);
        reader.Read(chars, 0, chars.Length).Should().Be(0);
        reader.Dispose();
        FluentActions.Invoking(() => reader.Read(chars, 0, chars.Length)).Should().ThrowExactly<ObjectDisposedException>();
        await conn.ExecuteNonQueryAsync($"TRUNCATE {table}", cancellationToken: TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Dispose_in_middle_of_text_export()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        var table = await GetTempTableName(conn);
        await conn.ExecuteNonQueryAsync($@"
CREATE TABLE {table} (field_text TEXT, field_int2 SMALLINT, field_int4 INTEGER);
INSERT INTO {table} (field_text, field_int4) VALUES ('HELLO', 1)", cancellationToken: TestContext.Current.CancellationToken);
        var reader = conn.BeginTextExport($"COPY {table} (field_text, field_int4) TO STDIN");

        //Act
        reader.Dispose();

        //Assert
        // Make sure the connection is still OK
        (await conn.ExecuteScalarAsync("SELECT 1", cancellationToken: TestContext.Current.CancellationToken)).Should().Be(1);
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/2330")]
    public async Task wrong_table_definition_text_import()
    {
        if (IsMultiplexing)
            Assert.Skip("Multiplexing: fails");
        using var conn = await OpenConnectionAsync();
        Assert.Throws<PostgresException>(() => conn.BeginTextImport("COPY table_is_not_exist (blob) FROM STDIN"));
        conn.FullState.Should().Be(ConnectionState.Open);
        (await conn.ExecuteScalarAsync("SELECT 1", cancellationToken: TestContext.Current.CancellationToken)).Should().Be(1);
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/2330")]
    public async Task wrong_format_text_import()
    {
        if (IsMultiplexing)
            Assert.Skip("Multiplexing: fails");
        using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "blob BYTEA");
        Assert.Throws<Exception>(() => conn.BeginTextImport($"COPY {table} (blob) FROM STDIN BINARY"));
        conn.FullState.Should().Be(ConnectionState.Broken);
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/2330")]
    public async Task wrong_table_definition_text_export()
    {
        if (IsMultiplexing)
            Assert.Skip("Multiplexing: fails");
        using var conn = await OpenConnectionAsync();
        Assert.Throws<PostgresException>(() => conn.BeginTextExport("COPY table_is_not_exist (blob) TO STDOUT"));
        conn.FullState.Should().Be(ConnectionState.Open);
        (await conn.ExecuteScalarAsync("SELECT 1", cancellationToken: TestContext.Current.CancellationToken)).Should().Be(1);
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/2330")]
    public async Task wrong_format_text_export()
    {
        if (IsMultiplexing)
            Assert.Skip("Multiplexing: fails");
        using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "blob BYTEA");
        Assert.Throws<Exception>(() => conn.BeginTextExport($"COPY {table} (blob) TO STDOUT BINARY"));
        conn.FullState.Should().Be(ConnectionState.Broken);
    }

    [Fact]
    public async Task Cancel_text_export_when_not_consumed_and_then_Dispose()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();

        //Act
        await using (var tx = await conn.BeginTransactionAsync(TestContext.Current.CancellationToken))
        {
            // This must be large enough to cause Postgres to queue up CopyData messages.
            var reader = (PgSqlCopyTextReader) conn.BeginTextExport("COPY (select md5(random()::text) as id from generate_series(1, 100000)) TO STDOUT");
            var buffer = new char[32];
            await reader.ReadAsync(buffer, 0, buffer.Length);
            reader.Cancel();
            FluentActions.Invoking(reader.Dispose).Should().NotThrow();
        }

        //Assert
        (await conn.ExecuteScalarAsync("SELECT 1", cancellationToken: TestContext.Current.CancellationToken)).Should().Be(1, "the connection is still OK");
    }

    #endregion

    #region Other

    // Starts a transaction before a COPY, testing that prepended messages are handled well
    [Fact]
    public async Task prepended_messages()
    {
        using var conn = await OpenConnectionAsync();
        conn.BeginTransaction();
        await text_import(async: false);
    }

    [Fact]
    public async Task undefined_table_throws()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();

        //Act
        var act = () => conn.BeginBinaryImport("COPY undefined_table (field_text, field_int2) FROM STDIN BINARY");

        //Assert
        act.Should().ThrowExactly<PostgresException>()
            .Where(e => e.SqlState == PostgresErrorCodes.UndefinedTable);
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/621")]
    public async Task Close_during_copy_throws()
    {
        // TODO: Check no broken connections were returned to the pool
        using (var conn = await OpenConnectionAsync()) {
            var table = await CreateTempTable(conn, "field_text TEXT, field_int2 SMALLINT, field_int4 INTEGER");
            conn.BeginBinaryImport($"COPY {table} (field_text, field_int4) FROM STDIN BINARY");
        }

        using (var conn = await OpenConnectionAsync()) {
            var table = await CreateTempTable(conn, "field_text TEXT, field_int2 SMALLINT, field_int4 INTEGER");
            conn.BeginBinaryExport($"COPY {table} (field_text, field_int2) TO STDIN BINARY");
        }

        using (var conn = await OpenConnectionAsync()) {
            var table = await CreateTempTable(conn, "field_text TEXT, field_int2 SMALLINT, field_int4 INTEGER");
            conn.BeginRawBinaryCopy($"COPY {table} (field_text, field_int4) FROM STDIN BINARY");
        }

        using (var conn = await OpenConnectionAsync()) {
            var table = await CreateTempTable(conn, "field_text TEXT, field_int2 SMALLINT, field_int4 INTEGER");
            conn.BeginRawBinaryCopy($"COPY {table} (field_text, field_int4) TO STDIN BINARY");
        }

        using (var conn = await OpenConnectionAsync()) {
            var table = await CreateTempTable(conn, "field_text TEXT, field_int2 SMALLINT, field_int4 INTEGER");
            conn.BeginTextImport($"COPY {table} (field_text, field_int4) FROM STDIN");
        }

        using (var conn = await OpenConnectionAsync()) {
            var table = await CreateTempTable(conn, "field_text TEXT, field_int2 SMALLINT, field_int4 INTEGER");
            conn.BeginTextExport($"COPY {table} (field_text, field_int4) TO STDIN");
        }
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/994")]
    public async Task non_ascii_column_name()
    {
        using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "non_ascii_éè TEXT");
        using (conn.BeginBinaryImport($"COPY {table} (non_ascii_éè) FROM STDIN BINARY")) { }
    }

    [Fact, IssueLink("https://stackoverflow.com/questions/37431054/08p01-insufficient-data-left-in-message-for-nullable-datetime/37431464")]
    public async Task Write_null_values()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "foo1 INT, foo2 UUID, foo3 INT, foo4 UUID");

        //Act
        using (var writer = conn.BeginBinaryImport($"COPY {table} (foo1, foo2, foo3, foo4) FROM STDIN BINARY"))
        {
            writer.StartRow();
            writer.Write(DBNull.Value, PgSqlDbType.Integer);
            writer.Write<Guid?>(null, PgSqlDbType.Uuid);
            writer.Write(DBNull.Value);
            writer.Write((string)null);
            var rowsWritten = writer.Complete();
            rowsWritten.Should().Be(1UL);
        }

        //Assert
        using (var cmd = new PgSqlCommand($"SELECT foo1,foo2,foo3,foo4 FROM {table}", conn))
        using (var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken))
        {
            reader.Read().Should().BeTrue();
            for (var i = 0; i < reader.FieldCount; i++)
                reader.IsDBNull(i).Should().BeTrue();
        }
    }

    [Fact]
    public async Task Write_different_types()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "foo INT, bar INT[]");

        //Act
        using (var writer = conn.BeginBinaryImport($"COPY {table} (foo, bar) FROM STDIN BINARY"))
        {
            writer.StartRow();
            writer.Write(3.0, PgSqlDbType.Integer);
            writer.Write(new[] { 1, 2, 3 });
            writer.StartRow();
            writer.Write(3, PgSqlDbType.Integer);
            writer.Write((object)new List<int> { 4, 5, 6 });
            var rowsWritten = writer.Complete();
            rowsWritten.Should().Be(2UL);
        }

        //Assert
        (await conn.ExecuteScalarAsync($"SELECT COUNT(*) FROM {table}", cancellationToken: TestContext.Current.CancellationToken)).Should().Be(2L);
    }

    // Tests nested binding scopes in multiplexing
    [Fact]
    public async Task within_transaction()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "foo INT");

        //Act
        using (var tx = conn.BeginTransaction())
        using (var writer = conn.BeginBinaryImport($"COPY {table} (foo) FROM STDIN BINARY"))
        {
            writer.StartRow();
            writer.Write(1);
            writer.Dispose();
            // Don't complete
            await tx.CommitAsync(TestContext.Current.CancellationToken);
        }

        using (var tx = conn.BeginTransaction())
        using (var writer = conn.BeginBinaryImport($"COPY {table} (foo) FROM STDIN BINARY"))
        {
            writer.StartRow();
            writer.Write(2);
            writer.Complete();
            // Don't commit
        }

        using (var tx = conn.BeginTransaction())
        {
            using (var writer = conn.BeginBinaryImport($"COPY {table} (foo) FROM STDIN BINARY"))
            {
                writer.StartRow();
                writer.Write(3);
                writer.Complete();
            }
            await tx.CommitAsync(TestContext.Current.CancellationToken);
        }

        //Assert
        (await conn.ExecuteScalarAsync($"SELECT COUNT(*) FROM {table}", cancellationToken: TestContext.Current.CancellationToken)).Should().Be(1L);
        (await conn.ExecuteScalarAsync($"SELECT foo FROM {table}", cancellationToken: TestContext.Current.CancellationToken)).Should().Be(3);
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/4199")]
    public async Task copy_from_is_not_supported_in_regular_command_execution()
    {
        //Arrange
        // Run in a separate pool to protect other queries in multiplexing
        // because we're going to break the connection on CopyInResponse
        await using var dataSource = CreateDataSource();
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        var table = await CreateTempTable(conn, "foo INT");

        //Act
        var act = () => conn.ExecuteNonQuery($@"COPY {table} (foo) FROM stdin");

        //Assert
        act.Should().ThrowExactly<NotSupportedException>();
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/4974")]
    public async Task copy_to_is_not_supported_in_regular_command_execution()
    {
        //Arrange
        // Run in a separate pool to protect other queries in multiplexing
        // because we're going to break the connection on CopyInResponse
        await using var dataSource = CreateDataSource();
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        var table = await CreateTempTable(conn, "foo INT");

        //Act
        var act = () => conn.ExecuteNonQuery($@"COPY {table} (foo) TO stdin");

        //Assert
        act.Should().ThrowExactly<NotSupportedException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [IssueLink("https://github.com/npgsql/npgsql/issues/5209")]
    public async Task raw_binary_copy_write_nre(bool async)
    {
        if (OperatingSystem.IsMacOS())
            Assert.Skip("Write might not throw an exception on macOS");

        await using var postmasterMock = PgPostmasterMock.Start(ConnectionString);
        await using var dataSource = CreateDataSource(postmasterMock.ConnectionString);
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        var server = await postmasterMock.WaitForServerConnection();
        await server
            .WriteCopyInResponse(isBinary: true)
            .FlushAsync();

        await using var stream = await conn.BeginRawBinaryCopyAsync("COPY SomeTable (field_text, field_int4) FROM STDIN", TestContext.Current.CancellationToken);
        server.Close();
        var value = Encoding.UTF8.GetBytes(new string('a', conn.Settings.WriteBufferSize * 2));
        if (async)
            await Assert.ThrowsAsync<PgSqlException>(async () => await stream.WriteAsync(value, TestContext.Current.CancellationToken));
        else
            Assert.Throws<PgSqlException>(() => stream.Write(value));
        conn.FullState.Should().Be(ConnectionState.Broken);
    }

    #endregion

    #region Utils

    /// <summary>
    /// Checks that the connector state is properly managed for COPY operations
    /// </summary>
    async Task StateAssertions(PgSqlConnection conn)
    {
        conn.Connector.State.Should().Be(ConnectorState.Copy);
        conn.State.Should().Be(ConnectionState.Open);
        conn.FullState.Should().Be(ConnectionState.Open | ConnectionState.Fetching);
        await FluentActions.Awaiting(async () => await conn.ExecuteScalarAsync("SELECT 1", cancellationToken: TestContext.Current.CancellationToken))
            .Should().ThrowExactlyAsync<PgSqlOperationInProgressException>();
    }

    #endregion
}

public sealed class CopyTests_NonMultiplexing() : CopyTests(MultiplexingMode.NonMultiplexing);
public sealed class CopyTests_Multiplexing() : CopyTests(MultiplexingMode.Multiplexing);

[Collection(NonParallelCollection.Name)]
public abstract class CopyTestsNonParallel(MultiplexingMode multiplexingMode) : MultiplexingTestBase(multiplexingMode)
{
    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/661")]
    public async Task unexpected_exception_binary_import()
    {
        if (IsMultiplexing)
            return;

        // Use a private data source since we terminate the connection below (affects database state)
        await using var dataSource = CreateDataSource();
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        var table = await CreateTempTable(conn, "blob BYTEA");

        var data = new byte[conn.Settings.WriteBufferSize + 10];

        var writer = conn.BeginBinaryImport($"COPY {table} (blob) FROM STDIN BINARY");

        using (var conn2 = await OpenConnectionAsync())
            conn2.ExecuteNonQuery($"SELECT pg_terminate_backend({conn.ProcessID})");

        Thread.Sleep(50);
        FluentActions.Invoking(() =>
        {
            writer.StartRow();
            writer.Write(data);
            writer.Dispose();
        }).Should().Throw<PgSqlException>();
        conn.FullState.Should().Be(ConnectionState.Broken);
    }
}

[Collection(NonParallelCollection.Name)]
public sealed class CopyTestsNonParallel_NonMultiplexing() : CopyTestsNonParallel(MultiplexingMode.NonMultiplexing);
[Collection(NonParallelCollection.Name)]
public sealed class CopyTestsNonParallel_Multiplexing() : CopyTestsNonParallel(MultiplexingMode.Multiplexing);
