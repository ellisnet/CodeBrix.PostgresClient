using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;
using CodeBrix.PostgresClient.PgSqlTypes;
using SilverAssertions;
using Xunit;

namespace CodeBrix.PostgresClient.Tests.Types; //was previously: Npgsql.Tests.Types;

/// <summary>
/// Tests on the PostgreSQL bytea type
/// </summary>
/// <summary>
/// https://www.postgresql.org/docs/current/static/datatype-binary.html
/// </summary>
public abstract class ByteaTests(MultiplexingMode multiplexingMode) : MultiplexingTestBase(multiplexingMode)
{
    [Theory]
    [InlineData(new byte[] { 1, 2, 3, 4, 5 }, "\\x0102030405")]
    [InlineData(new byte[] { }, "\\x")]
    public Task bytea(byte[] byteArray, string sqlLiteral)
        => AssertType(byteArray, sqlLiteral, "bytea", PgSqlDbType.Bytea, DbType.Binary);

    [Fact]
    public async Task bytea_long()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        var array = new byte[conn.Settings.WriteBufferSize + 100];
        var sqlLiteral = "\\x" + new string('1', (conn.Settings.WriteBufferSize + 100) * 2);
        for (var i = 0; i < array.Length; i++)
            array[i] = 17;

        //Assert
        await bytea(array, sqlLiteral);
    }

    [Fact]
    public Task as_Memory()
        => AssertType(
            new Memory<byte>([1, 2, 3]), "\\x010203", "bytea", PgSqlDbType.Bytea, DbType.Binary, isDefault: false,
            comparer: (left, right) => left.Span.SequenceEqual(right.Span));

    [Fact]
    public Task as_ReadOnlyMemory()
        => AssertType(
            new ReadOnlyMemory<byte>([1, 2, 3]), "\\x010203", "bytea", PgSqlDbType.Bytea, DbType.Binary, isDefault: false,
            comparer: (left, right) => left.Span.SequenceEqual(right.Span));

    [Fact]
    public Task as_ArraySegment()
        => AssertType(
            new ArraySegment<byte>([1, 2, 3]), "\\x010203", "bytea", PgSqlDbType.Bytea, DbType.Binary, isDefault: false);

    [Fact]
    public Task write_as_MemoryStream()
        => AssertTypeWrite(
            () => new MemoryStream([1, 2, 3]), "\\x010203", "bytea", PgSqlDbType.Bytea, DbType.Binary, isDefault: false);

    [Fact]
    public Task write_as_MemoryStream_truncated()
    {
        //Arrange
        var msFactory = () =>
        {
            var ms = new MemoryStream([1, 2, 3, 4]);
            ms.ReadByte();
            return ms;
        };

        //Assert
        return AssertTypeWrite(
            msFactory, "\\x020304", "bytea", PgSqlDbType.Bytea, DbType.Binary, isDefault: false);
    }

    [Fact]
    public Task write_as_MemoryStream_exposableArray()
    {
        //Arrange
        var msFactory = () =>
        {
            var ms = new MemoryStream(20);
            ms.WriteByte(1);
            ms.WriteByte(2);
            ms.WriteByte(3);
            ms.WriteByte(4);
            ms.Position = 1;
            return ms;
        };

        //Assert
        return AssertTypeWrite(
            msFactory, "\\x020304", "bytea", PgSqlDbType.Bytea, DbType.Binary, isDefault: false);
    }

    [Fact]
    public async Task write_as_MemoryStream_long()
    {
        //Arrange
        var rnd = new Random(1);
        var bytes = new byte[8192 * 4];
        rnd.NextBytes(bytes);
        var expectedSql = "\\x" + ToHex(bytes);

        //Assert
        await AssertTypeWrite(
            () => new MemoryStream(bytes), expectedSql, "bytea", PgSqlDbType.Bytea, DbType.Binary, isDefault: false);
    }

    [Fact]
    public async Task write_as_FileStream()
    {
        var filePath = Path.GetTempFileName();
        var fsList = new List<FileStream>();
        try
        {
            await File.WriteAllBytesAsync(filePath, [1, 2, 3], TestContext.Current.CancellationToken);

            await AssertTypeWrite(
                () => FileStreamFactory(filePath, fsList), "\\x010203", "bytea", PgSqlDbType.Bytea, DbType.Binary, isDefault: false);
        }
        finally
        {
            foreach (var fs in fsList)
                await fs.DisposeAsync();

            try
            {
                File.Delete(filePath);
            }
            catch {}
        }

        FileStream FileStreamFactory(string filePath, List<FileStream> fsList)
        {
            var fs = File.OpenRead(filePath);
            fsList.Add(fs);
            return fs;
        }
    }

    [Fact]
    public async Task write_as_FileStream_long()
    {
        var filePath = Path.GetTempFileName();
        var fsList = new List<FileStream>();
        var rnd = new Random(1);
        try
        {
            var bytes = new byte[8192 * 4];
            rnd.NextBytes(bytes);
            await File.WriteAllBytesAsync(filePath, bytes, TestContext.Current.CancellationToken);
            var expectedSql = "\\x" + ToHex(bytes);

            await AssertTypeWrite(
                () => FileStreamFactory(filePath, fsList), expectedSql, "bytea", PgSqlDbType.Bytea, DbType.Binary, isDefault: false);
        }
        finally
        {
            foreach (var fs in fsList)
                await fs.DisposeAsync();

            try
            {
                File.Delete(filePath);
            }
            catch {}
        }

        FileStream FileStreamFactory(string filePath, List<FileStream> fsList)
        {
            var fs = File.OpenRead(filePath);
            fsList.Add(fs);
            return fs;
        }
    }

    static string ToHex(ReadOnlySpan<byte> bytes)
    {
        var c = new char[bytes.Length * 2];

        byte b;

        for (int bx = 0, cx = 0; bx < bytes.Length; ++bx, ++cx)
        {
            b = (byte)(bytes[bx] >> 4);
            c[cx] = (char)(b > 9 ? b - 10 + 'a' : b + '0');

            b = (byte)(bytes[bx] & 0x0F);
            c[++cx] = (char)(b > 9 ? b - 10 + 'a' : b + '0');
        }

        return new string(c);
    }

    // Tests that bytea array values are truncated when the PgSqlParameter's Size is set
    [Fact]
    public async Task truncate_array()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        await using var cmd = new PgSqlCommand("SELECT @p", conn);
        byte[] data = [1, 2, 3, 4, 5, 6];
        var p = new PgSqlParameter("p", data) { Size = 4 };
        cmd.Parameters.Add(p);

        //Act
        (await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken)).Should().BeOfType<byte[]>().Which.Should().Equal(new byte[] { 1, 2, 3, 4 });
        p.Value.Should().BeOfType<byte[]>().Which.Should().Equal(new byte[] { 1, 2, 3, 4 }, "the truncated parameter value should be persisted on the parameter per DbParameter.Size docs");

        // PgSqlParameter.Size needs to persist when value is changed
        byte[] data2 = [11, 12, 13, 14, 15, 16];
        p.Value = data2;
        (await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken)).Should().BeOfType<byte[]>().Which.Should().Equal(new byte[] { 11, 12, 13, 14 });

        // PgSqlParameter.Size larger than the value size should mean the value size, as well as 0 and -1
        p.Value = data2;
        p.Size = data2.Length + 10;
        (await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken)).Should().BeOfType<byte[]>().Which.Should().Equal(data2);
        p.Size = 0;
        (await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken)).Should().BeOfType<byte[]>().Which.Should().Equal(data2);
        p.Size = -1;
        (await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken)).Should().BeOfType<byte[]>().Which.Should().Equal(data2);

        Assert.Throws<ArgumentException>(() => p.Size = -2);
    }

    // Tests that bytea stream values are truncated when the PgSqlParameter's Size is set
    [Fact]
    public async Task truncate_stream()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        await using var cmd = new PgSqlCommand("SELECT @p", conn);
        byte[] data = [1, 2, 3, 4, 5, 6];
        var p = new PgSqlParameter("p", new MemoryStream(data)) { Size = 4 };
        cmd.Parameters.Add(p);

        //Act
        (await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken)).Should().BeOfType<byte[]>().Which.Should().Equal(new byte[] { 1, 2, 3, 4 });

        // PgSqlParameter.Size needs to persist when value is changed
        byte[] data2 = [11, 12, 13, 14, 15, 16];
        p.Value = new MemoryStream(data2);
        (await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken)).Should().BeOfType<byte[]>().Which.Should().Equal(new byte[] { 11, 12, 13, 14 });

        // Handle with offset
        var data2ms = new MemoryStream(data2);
        data2ms.ReadByte();
        p.Value = data2ms;
        (await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken)).Should().BeOfType<byte[]>().Which.Should().Equal(new byte[] { 12, 13, 14, 15 });

        p.Size = 0;
        p.Value = new MemoryStream(data2);
        (await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken)).Should().BeOfType<byte[]>().Which.Should().Equal(data2);
        p.Size = -1;
        p.Value = new MemoryStream(data2);
        (await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken)).Should().BeOfType<byte[]>().Which.Should().Equal(data2);

        Assert.Throws<ArgumentException>(() => p.Size = -2);

        p.Value = new MemoryStream(data2);
        p.Size = data2.Length + 10;
        (await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken)).Should().BeOfType<byte[]>().Which.Should().Equal(data2);
    }

    [Fact]
    public async Task write_as_NonSeekable_stream()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        await using var cmd = new PgSqlCommand("SELECT @p", conn);
        byte[] data = [1, 2, 3, 4, 5, 6];
        var p = new PgSqlParameter("p", new NonSeekableStream(data)) { Size = 4 };
        cmd.Parameters.Add(p);

        //Act
        (await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken)).Should().BeOfType<byte[]>().Which.Should().Equal(new byte[] { 1, 2, 3, 4 });

        var streamWithOffset = new NonSeekableStream(data);
        streamWithOffset.ReadByte();
        p.Value = streamWithOffset;
        (await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken)).Should().BeOfType<byte[]>().Which.Should().Equal(new byte[] { 2, 3, 4, 5 });

        p.Value = new NonSeekableStream(data);
        p.Size = 0;
        (await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken)).Should().BeOfType<byte[]>().Which.Should().Equal(data);
        conn.State.Should().Be(ConnectionState.Open);
    }

    [Fact]
    public async Task array_of_bytea()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        await using var cmd = new PgSqlCommand("SELECT :p1", conn);
        var bytes = new byte[] { 1, 2, 3, 4, 5, 34, 39, 48, 49, 50, 51, 52, 92, 127, 128, 255, 254, 253, 252, 251 };
        var inVal = new[] { bytes, bytes };
        cmd.Parameters.AddWithValue("p1", PgSqlDbType.Bytea | PgSqlDbType.Array, inVal);

        //Act
        var retVal = (byte[][])await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken);

        //Assert
        retVal.Length.Should().Be(inVal.Length);
        retVal[0].Should().Equal(inVal[0]);
        retVal[1].Should().Equal(inVal[1]);
    }

    [Fact]
    public async Task invalid_cast_exception_unknown_stream_read()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        await using var cmd = new PgSqlCommand("SELECT :p1", conn);
        cmd.Parameters.AddWithValue("p1", PgSqlDbType.Bytea, new byte[] { 1 });

        //Act
        await using var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            Assert.Throws<InvalidCastException>(() => reader.GetFieldValue<NetworkStream>(0));
        }
    }

    sealed class NonSeekableStream(byte[] data) : MemoryStream(data)
    {
        public override bool CanSeek => false;
    }
}

public sealed class ByteaTests_NonMultiplexing() : ByteaTests(MultiplexingMode.NonMultiplexing);
public sealed class ByteaTests_Multiplexing() : ByteaTests(MultiplexingMode.Multiplexing);
