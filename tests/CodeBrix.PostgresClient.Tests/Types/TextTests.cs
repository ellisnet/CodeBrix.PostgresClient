using System;
using System.Data;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using CodeBrix.PostgresClient.PgSqlTypes;
using SilverAssertions;
using Xunit;
using static CodeBrix.PostgresClient.Tests.TestUtil;

namespace CodeBrix.PostgresClient.Tests.Types; //was previously: Npgsql.Tests.Types;

/// <summary>
/// Tests on PostgreSQL text
/// </summary>
/// <remarks>
/// https://www.postgresql.org/docs/current/static/datatype-character.html
/// </remarks>
public abstract class TextTests(MultiplexingMode multiplexingMode) : MultiplexingTestBase(multiplexingMode)
{
    [Fact]
    public Task text_as_string()
        => AssertType("foo", "foo", "text", PgSqlDbType.Text, DbType.String);

    [Fact]
    public Task text_as_array_of_chars()
        => AssertType("foo".ToCharArray(), "foo", "text", PgSqlDbType.Text, DbType.String, isDefaultForReading: false);

    [Fact]
    public Task text_as_ArraySegment_of_chars()
        => AssertTypeWrite(new ArraySegment<char>("foo".ToCharArray()), "foo", "text", PgSqlDbType.Text, DbType.String,
            isDefault: false);

    [Fact]
    public Task text_as_array_of_bytes()
        => AssertType(Encoding.UTF8.GetBytes("foo"), "foo", "text", PgSqlDbType.Text, DbType.String, isDefault: false);

    [Fact]
    public Task text_as_ReadOnlyMemory_of_bytes()
        => AssertTypeWrite(new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("foo")), "foo", "text", PgSqlDbType.Text, DbType.String,
            isDefault: false);

    [Fact]
    public Task char_as_char()
        => AssertType('f', "f", "character", PgSqlDbType.Char, inferredDbType: DbType.String, isDefault: false);

    [Fact]
    public async Task citext_as_string()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        await EnsureExtensionAsync(conn, "citext");

        //Assert
        await AssertType("foo", "foo", "citext", PgSqlDbType.Citext, inferredDbType: DbType.String, isDefaultForWriting: false);
    }

    [Fact]
    public Task text_as_MemoryStream()
        => AssertTypeWrite(() => new MemoryStream("foo"u8.ToArray()), "foo", "text", PgSqlDbType.Text, DbType.String, isDefault: false);

    [Fact]
    public async Task text_long()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        var builder = new StringBuilder("ABCDEééé", conn.Settings.WriteBufferSize);
        builder.Append('X', conn.Settings.WriteBufferSize);
        var value = builder.ToString();

        //Assert
        await AssertType(value, value, "text", PgSqlDbType.Text, DbType.String);
    }

    // Tests that strings are truncated when the PgSqlParameter's Size is set
    [Fact]
    public async Task truncate()
    {
        //Arrange
        const string data = "SomeText";
        using var conn = await OpenConnectionAsync();
        using var cmd = new PgSqlCommand("SELECT @p::TEXT", conn);
        var p = new PgSqlParameter("p", data) { Size = 4 };
        cmd.Parameters.Add(p);

        //Act
        (await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken)).Should().Be(data.Substring(0, 4));

        // PgSqlParameter.Size needs to persist when value is changed
        const string data2 = "AnotherValue";
        p.Value = data2;
        (await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken)).Should().Be(data2.Substring(0, 4));

        // PgSqlParameter.Size larger than the value size should mean the value size, as well as 0 and -1
        p.Value = data2;
        p.Size = data2.Length + 10;
        (await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken)).Should().Be(data2);
        p.Size = 0;
        (await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken)).Should().Be(data2);
        p.Size = -1;
        (await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken)).Should().Be(data2);

        Assert.Throws<ArgumentException>(() => p.Size = -2);
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/488")]
    public async Task null_character()
    {
        //Act
        var exception = await AssertTypeUnsupportedWrite<string, PostgresException>("string with \0\0\0 null \0bytes");

        //Assert
        exception.SqlState.Should().Be(PostgresErrorCodes.CharacterNotInRepertoire);
    }

    // Tests some types which are aliased to strings
    [Theory]
    [InlineData("character varying", PgSqlDbType.Varchar)]
    [InlineData("name", PgSqlDbType.Name)]
    public Task aliased_postgres_types(string pgTypeName, PgSqlDbType pgSqlDbType)
        => AssertType("foo", "foo", pgTypeName, pgSqlDbType, inferredDbType: DbType.String, isDefaultForWriting: false);

    [Theory]
    [InlineData(DbType.AnsiString)]
    [InlineData(DbType.AnsiStringFixedLength)]
    public async Task aliased_DbTypes(DbType dbType)
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        await using var command = new PgSqlCommand("SELECT @p", conn);
        command.Parameters.Add(new PgSqlParameter("p", dbType) { Value = "SomeString" });

        //Act
        (await command.ExecuteScalarAsync(TestContext.Current.CancellationToken)).Should().Be("SomeString"); // Inferred DbType...
    }

    // Tests the PostgreSQL internal "char" type
    [Fact]
    public async Task internal_char()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        using var cmd = conn.CreateCommand();
        var testArr = new byte[] { (byte)'}', (byte)'"', 3 };
        var testArr2 = new char[] { '}', '"', (char)3 };

        cmd.CommandText = "Select 'a'::\"char\", (-3)::\"char\", :p1, :p2, :p3, :p4, :p5";
        cmd.Parameters.Add(new PgSqlParameter("p1", PgSqlDbType.InternalChar) { Value = 'b' });
        cmd.Parameters.Add(new PgSqlParameter("p2", PgSqlDbType.InternalChar) { Value = (byte)66 });
        cmd.Parameters.Add(new PgSqlParameter("p3", PgSqlDbType.InternalChar) { Value = (byte)230 });
        cmd.Parameters.Add(new PgSqlParameter("p4", PgSqlDbType.InternalChar | PgSqlDbType.Array) { Value = testArr });
        cmd.Parameters.Add(new PgSqlParameter("p5", PgSqlDbType.InternalChar | PgSqlDbType.Array) { Value = testArr2 });

        //Act
        using var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        reader.Read();

        //Assert
        var expected = new char[] { 'a', (char)(256 - 3), 'b', (char)66, (char)230 };
        for (var i = 0; i < expected.Length; i++)
        {
            reader.GetChar(i).Should().Be(expected[i]);
        }
        var arr = (char[])reader.GetValue(5);
        var arr2 = (char[])reader.GetValue(6);
        arr.Length.Should().Be(testArr.Length);
        for (var i = 0; i < arr.Length; i++)
        {
            arr[i].Should().Be((char)testArr[i]);
            arr2[i].Should().Be(testArr2[i]);
        }
    }
}

public sealed class TextTests_NonMultiplexing() : TextTests(MultiplexingMode.NonMultiplexing);
public sealed class TextTests_Multiplexing() : TextTests(MultiplexingMode.Multiplexing);
