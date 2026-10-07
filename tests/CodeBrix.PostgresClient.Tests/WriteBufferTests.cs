using System;
using System.IO;
using CodeBrix.PostgresClient.Internal;
using SilverAssertions;
using Xunit;

namespace CodeBrix.PostgresClient.Tests; //was previously: Npgsql.Tests;

// xUnit creates a new instance per test, so each test gets its own buffer
public class WriteBufferTests
{
    [Fact]
    public void buffered_full_buffer_no_flush()
    {
        //Arrange
        WriteBuffer.WritePosition += WriteBuffer.WriteSpaceLeft - sizeof(int);
        var writer = WriteBuffer.GetWriter(null, FlushMode.NonBlocking);

        //Assert
        writer.ShouldFlush(sizeof(int)).Should().BeFalse();

        var act = () =>
        {
            Span<byte> intBytes = stackalloc byte[4];
            writer.WriteBytes(intBytes);
        };
        act.Should().NotThrow();
    }

    [Fact]
    public void GetWriter_full_buffer()
    {
        //Arrange
        WriteBuffer.WritePosition += WriteBuffer.WriteSpaceLeft;
        var writer = WriteBuffer.GetWriter(null, FlushMode.Blocking);
        writer.ShouldFlush(sizeof(byte)).Should().BeTrue();

        //Act
        writer.Flush();

        //Assert
        writer.ShouldFlush(sizeof(byte)).Should().BeFalse();
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/1275")]
    public void chunked_string_with_full_buffer()
    {
        //Arrange
        // Fill up the buffer entirely
        WriteBuffer.WriteBytes(new byte[WriteBuffer.Size], 0, WriteBuffer.Size);
        WriteBuffer.WriteSpaceLeft.Should().Be(0);

        var data = new string('a', WriteBuffer.Size) + "hello";
        var byteLength = WriteBuffer.TextEncoding.GetByteCount(data);

        //Act
        WriteBuffer.WriteString(data, byteLength, false, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        WriteBuffer.WritePosition.Should().Be(5);
        WriteBuffer.Buffer.AsSpan(0, 5).ToArray().Should().Equal(new byte[] { (byte)'h', (byte)'e', (byte)'l', (byte)'l', (byte)'o' });
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/2849")]
    public void chunked_string_encoding_fits()
    {
        //Arrange
        WriteBuffer.WriteBytes(new byte[WriteBuffer.Size - 1], 0, WriteBuffer.Size - 1);
        WriteBuffer.WriteSpaceLeft.Should().Be(1);

        // This unicode character is three bytes when encoded in UTF8
        var data = "한" + new string('a', WriteBuffer.Size);
        var byteLength = WriteBuffer.TextEncoding.GetByteCount(data);

        //Act
        WriteBuffer.WriteString(data, byteLength, false, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        WriteBuffer.WritePosition.Should().Be(3);
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/3733")]
    public void chunked_string_encoding_fits_with_surrogates()
    {
        //Arrange
        WriteBuffer.WriteBytes(new byte[WriteBuffer.Size - 1]);
        WriteBuffer.WriteSpaceLeft.Should().Be(1);

        var cyclone = "🌀" + new string('a', WriteBuffer.Size);
        var byteLength = WriteBuffer.TextEncoding.GetByteCount(cyclone);

        //Act
        WriteBuffer.WriteString(cyclone, byteLength, false, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        WriteBuffer.WritePosition.Should().Be(4);
    }

    public WriteBufferTests()
    {
        Underlying = new MemoryStream();
        WriteBuffer = new PgSqlWriteBuffer(null, Underlying, null, PgSqlReadBuffer.DefaultSize, PgSqlWriteBuffer.UTF8Encoding);
        WriteBuffer.MessageLengthValidation = false;
    }

    // ReSharper disable once InconsistentNaming
    readonly PgSqlWriteBuffer WriteBuffer;
    // ReSharper disable once InconsistentNaming
    readonly MemoryStream Underlying;
}
