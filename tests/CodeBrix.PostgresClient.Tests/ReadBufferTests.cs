using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.PostgresClient.Internal;
using SilverAssertions;
using Xunit;

namespace CodeBrix.PostgresClient.Tests; //was previously: Npgsql.Tests;

// xUnit creates a new instance per test, so each test gets its own buffer
public class ReadBufferTests
{
    [Fact]
    public void Skip()
    {
        //Arrange
        for (byte i = 0; i < 50; i++)
            Writer.WriteByte(i);

        //Assert
        ReadBuffer.Ensure(10);
        ReadBuffer.Skip(7);
        ReadBuffer.ReadByte().Should().Be(7);
        ReadBuffer.Skip(10);
        ReadBuffer.Ensure(1);
        ReadBuffer.ReadByte().Should().Be(18);
        ReadBuffer.Skip(20);
        ReadBuffer.Ensure(1);
        ReadBuffer.ReadByte().Should().Be(39);
    }

    [Fact]
    public void ReadSingle()
    {
        //Arrange
        const float expected = 8.7f;
        var bytes = BitConverter.GetBytes(expected);
        Array.Reverse(bytes);
        Writer.Write(bytes);

        //Act
        ReadBuffer.Ensure(4);

        //Assert
        ReadBuffer.ReadSingle().Should().Be(expected);
    }

    [Fact]
    public void ReadDouble()
    {
        //Arrange
        const double expected = 8.7;
        var bytes = BitConverter.GetBytes(expected);
        Array.Reverse(bytes);
        Writer.Write(bytes);

        //Act
        ReadBuffer.Ensure(8);

        //Assert
        ReadBuffer.ReadDouble().Should().Be(expected);
    }

    [Fact]
    public void ReadNullTerminatedString_buffered_only()
    {
        //Arrange
        Writer
            .Write(PgSqlWriteBuffer.UTF8Encoding.GetBytes(new string("foo")))
            .WriteByte(0)
            .Write(PgSqlWriteBuffer.UTF8Encoding.GetBytes(new string("bar")))
            .WriteByte(0);

        //Act
        ReadBuffer.Ensure(1);

        //Assert
        ReadBuffer.ReadNullTerminatedString().Should().Be("foo");
        ReadBuffer.ReadNullTerminatedString().Should().Be("bar");
    }

    [Fact]
    public async Task ReadNullTerminatedString_with_io()
    {
        //Arrange
        Writer.Write(PgSqlWriteBuffer.UTF8Encoding.GetBytes(new string("Chunked ")));
        await ReadBuffer.Ensure(1, async: true);

        //Act
        var task = ReadBuffer.ReadNullTerminatedString(async: true, cancellationToken: TestContext.Current.CancellationToken);
        task.IsCompleted.Should().BeFalse();

        Writer
            .Write(PgSqlWriteBuffer.UTF8Encoding.GetBytes(new string("string")))
            .WriteByte(0)
            .Write(PgSqlWriteBuffer.UTF8Encoding.GetBytes(new string("bar")))
            .WriteByte(0);

        //Assert
        task.IsCompleted.Should().BeTrue();
        (await task).Should().Be("Chunked string");
        ReadBuffer.ReadNullTerminatedString().Should().Be("bar");
    }

    public ReadBufferTests()
    {
        var stream = new MockStream();
        ReadBuffer = new PgSqlReadBuffer(null, stream, null, PgSqlReadBuffer.DefaultSize, PgSqlWriteBuffer.UTF8Encoding, PgSqlWriteBuffer.RelaxedUTF8Encoding);
        Writer = stream.Writer;
    }

    // ReSharper disable once InconsistentNaming
    readonly PgSqlReadBuffer ReadBuffer;
    // ReSharper disable once InconsistentNaming
    readonly MockStream.MockStreamWriter Writer;

    class MockStream : Stream
    {
        const int Size = 8192;

        internal MockStreamWriter Writer { get; }

        public MockStream() => Writer = new MockStreamWriter(this);

        TaskCompletionSource<object> _tcs = new();
        readonly byte[] _data = new byte[Size];
        int _filled;

        public override int Read(byte[] buffer, int offset, int count)
            => Read(buffer, offset, count, async: false).GetAwaiter().GetResult();

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => Read(buffer, offset, count, async: true);

        async Task<int> Read(byte[] buffer, int offset, int count, bool async)
        {
            if (_filled == 0)
            {
                _tcs = new TaskCompletionSource<object>();
                if (async)
                    await _tcs.Task;
                else
                    _tcs.Task.Wait();
            }

            count = Math.Min(count, _filled);
            new Span<byte>(_data, 0, count).CopyTo(new Span<byte>(buffer, offset, count));
            new Span<byte>(_data, count, _filled - count).CopyTo(_data);
            _filled -= count;
            return count;
        }

        internal class MockStreamWriter(MockStream stream)
        {
            public MockStreamWriter WriteByte(byte b)
            {
                Span<byte> bytes = stackalloc byte[1];
                bytes[0] = b;
                Write(bytes);
                return this;
            }

            public MockStreamWriter Write(ReadOnlySpan<byte> bytes)
            {
                if (stream._filled + bytes.Length > Size)
                    throw new Exception("Mock stream overrun");
                bytes.CopyTo(new Span<byte>(stream._data, stream._filled, bytes.Length));
                stream._filled += bytes.Length;
                stream._tcs.TrySetResult(new());
                return this;
            }
        }

        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }
    }
}
