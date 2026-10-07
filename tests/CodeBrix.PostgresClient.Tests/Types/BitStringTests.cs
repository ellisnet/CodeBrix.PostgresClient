using System;
using System.Collections;
using System.Collections.Specialized;
using System.Threading.Tasks;
using CodeBrix.PostgresClient.PgSqlTypes;
using CodeBrix.PostgresClient.Tests.Support;
using SilverAssertions;
using Xunit;

namespace CodeBrix.PostgresClient.Tests.Types; //was previously: Npgsql.Tests.Types;

/// <summary>
/// Tests on the PostgreSQL BitString type
/// </summary>
/// <remarks>
/// https://www.postgresql.org/docs/current/static/datatype-bit.html
/// </remarks>
public abstract class BitStringTests(MultiplexingMode multiplexingMode) : MultiplexingTestBase(multiplexingMode)
{
    [Theory]
    [InlineData("10110110")]
    [InlineData("1011011000101111010110101101011011")]
    [InlineData("")]
    public async Task BitArray(string sqlLiteral)
    {
        //Arrange
        var len = sqlLiteral.Length;

        var bitArray = new BitArray(len);
        for (var i = 0; i < sqlLiteral.Length; i++)
            bitArray[i] = sqlLiteral[i] == '1';

        //Assert
        await AssertType(bitArray, sqlLiteral, "bit varying", PgSqlDbType.Varbit);

        if (len > 0)
            await AssertType(bitArray, sqlLiteral, $"bit({len})", PgSqlDbType.Bit, isDefaultForWriting: false);
    }

    [Fact]
    public async Task BitArray_long()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        var bitLen = (conn.Settings.WriteBufferSize + 10) * 8;
        var chars = new char[bitLen];
        for (var i = 0; i < bitLen; i++)
            chars[i] = i % 2 == 0 ? '0' : '1';

        //Assert
        await BitArray(new string(chars));
    }

    [Fact]
    public Task BitVector32()
        => AssertType(
            new BitVector32(4), "00000000000000000000000000000100", "bit varying", PgSqlDbType.Varbit, isDefaultForReading: false);

    [Fact]
    public Task BitVector32_too_long()
        => AssertTypeUnsupportedRead<BitVector32, InvalidCastException>(new string('0', 34), "bit varying");

    [Fact]
    public Task @bool()
        => AssertType(true, "1", "bit(1)", PgSqlDbType.Bit, isDefault: false);

    [Fact]
    public async Task bitstring_with_multiple_bits_as_bool_throws()
    {
        await AssertTypeUnsupportedRead<bool, InvalidCastException>("01", "varbit");
        await AssertTypeUnsupportedRead<bool, InvalidCastException>("01", "bit(2)");
    }

    [Fact]
    public async Task array()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        using var cmd = new PgSqlCommand("SELECT @p", conn);
        var expected = new[] { new BitArray([true, false, true]), new BitArray([false]) };
        var p = new PgSqlParameter("p", PgSqlDbType.Array | PgSqlDbType.Varbit) { Value = expected };
        cmd.Parameters.Add(p);
        p.Value = expected;

        //Act
        using var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        reader.Read();

        //Assert
        var value = reader.GetValue(0);
        ValueEquality.AreEqual(expected, value).Should().BeTrue(
            $"expected {ValueEquality.Format(expected)} but got {ValueEquality.Format(value)}");
        var fieldValue = reader.GetFieldValue<BitArray[]>(0);
        ValueEquality.AreEqual(expected, fieldValue).Should().BeTrue(
            $"expected {ValueEquality.Format(expected)} but got {ValueEquality.Format(fieldValue)}");
        reader.GetFieldType(0).Should().Be(typeof(Array));
    }

    [Fact]
    public async Task array_of_single_bits()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        using var cmd = new PgSqlCommand("SELECT @p::BIT(1)[]", conn);
        var expected = new[] { true, false };
        var p = new PgSqlParameter("p", PgSqlDbType.Array | PgSqlDbType.Bit) {Value = expected};
        cmd.Parameters.Add(p);
        p.Value = expected;

        //Act
        using var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        reader.Read();
        var x = reader.GetValue(0);

        //Assert
        reader.GetValue(0).Should().BeOfType<bool[]>().Which.Should().Equal(expected);
        reader.GetFieldValue<bool[]>(0).Should().Equal(expected);
        reader.GetFieldType(0).Should().Be(typeof(Array));
    }

    [Fact]
    public async Task array_of_single_bits_and_null()
    {
        //Arrange
        var dataSource = CreateDataSource(builder => builder.ArrayNullabilityMode = ArrayNullabilityMode.Always);
        using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        using var cmd = new PgSqlCommand("SELECT @p::BIT(1)[]", conn);
        var expected = new bool?[] { true, false, null };
        var p = new PgSqlParameter("p", PgSqlDbType.Array | PgSqlDbType.Bit) {Value = expected};
        cmd.Parameters.Add(p);
        p.Value = expected;

        //Act
        using var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        reader.Read();
        var x = reader.GetValue(0);

        //Assert
        reader.GetValue(0).Should().BeOfType<bool?[]>().Which.Should().Equal(expected);
        reader.GetFieldValue<bool?[]>(0).Should().Equal(expected);
        reader.GetFieldType(0).Should().Be(typeof(Array));
    }

    [Fact]
    public Task as_string()
        => AssertType("010101", "010101", "bit varying", PgSqlDbType.Varbit, isDefault: false);

    [Fact]
    public Task write_as_string_validation()
        => AssertTypeUnsupportedWrite<string, ArgumentException>("001q0", "bit varying");
}

public sealed class BitStringTests_NonMultiplexing() : BitStringTests(MultiplexingMode.NonMultiplexing);
public sealed class BitStringTests_Multiplexing() : BitStringTests(MultiplexingMode.Multiplexing);
