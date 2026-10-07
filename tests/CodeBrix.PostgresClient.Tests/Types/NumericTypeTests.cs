using System;
using System.Data;
using System.Globalization;
using System.Threading.Tasks;
using CodeBrix.PostgresClient.PgSqlTypes;
using Xunit;
using static CodeBrix.PostgresClient.Tests.TestUtil;

namespace CodeBrix.PostgresClient.Tests.Types; //was previously: Npgsql.Tests.Types;

/// <summary>
/// Tests on PostgreSQL numeric types
/// </summary>
/// <summary>
/// https://www.postgresql.org/docs/current/static/datatype-numeric.html
/// </summary>
public abstract class NumericTypeTests(MultiplexingMode multiplexingMode) : MultiplexingTestBase(multiplexingMode)
{
    [Fact]
    public async Task Int16()
    {
        await AssertType((short)8, "8", "smallint", PgSqlDbType.Smallint, DbType.Int16);
        // Clr byte/sbyte maps to 'int2' as there is no byte type in PostgreSQL, byte[] maps to bytea however.
        await AssertType((byte)8, "8", "smallint", PgSqlDbType.Smallint, DbType.Int16, isDefaultForReading: false, skipArrayCheck: true);
        await AssertType((sbyte)8, "8", "smallint", PgSqlDbType.Smallint, DbType.Int16, isDefaultForReading: false);

        await AssertType(8,       "8", "smallint", PgSqlDbType.Smallint, DbType.Int16, isDefault: false);
        await AssertType(8L,      "8", "smallint", PgSqlDbType.Smallint, DbType.Int16, isDefault: false);
        await AssertType(8F,      "8", "smallint", PgSqlDbType.Smallint, DbType.Int16, isDefault: false);
        await AssertType(8D,      "8", "smallint", PgSqlDbType.Smallint, DbType.Int16, isDefault: false);
        await AssertType(8M,      "8", "smallint", PgSqlDbType.Smallint, DbType.Int16, isDefault: false);
    }

    [Fact]
    public async Task Int32()
    {
        await AssertType(8, "8", "integer", PgSqlDbType.Integer, DbType.Int32);

        await AssertType((short)8, "8", "integer", PgSqlDbType.Integer, DbType.Int32, isDefault: false);
        await AssertType(8L,       "8", "integer", PgSqlDbType.Integer, DbType.Int32, isDefault: false);
        await AssertType((byte)8,  "8", "integer", PgSqlDbType.Integer, DbType.Int32, isDefault: false);
        await AssertType(8F,       "8", "integer", PgSqlDbType.Integer, DbType.Int32, isDefault: false);
        await AssertType(8D,       "8", "integer", PgSqlDbType.Integer, DbType.Int32, isDefault: false);
        await AssertType(8M,       "8", "integer", PgSqlDbType.Integer, DbType.Int32, isDefault: false);
    }

    // Tests some types which are aliased to UInt32
    [Theory]
    [InlineData("oid", PgSqlDbType.Oid)]
    [InlineData("xid", PgSqlDbType.Xid)]
    [InlineData("cid", PgSqlDbType.Cid)]
    public Task UInt32(string pgTypeName, PgSqlDbType pgSqlDbType)
        => AssertType(8u, "8", pgTypeName, pgSqlDbType, isDefaultForWriting: false);

    [Theory]
    [InlineData("xid8", PgSqlDbType.Xid8)]
    public async Task UInt64(string pgTypeName, PgSqlDbType pgSqlDbType)
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        MinimumPgVersion(conn, "13.0", "The xid8 type was introduced in PostgreSQL 13");

        //Assert
        await AssertType(8ul, "8", pgTypeName, pgSqlDbType, isDefaultForWriting: false);
    }

    [Fact]
    public async Task Int64()
    {
        await AssertType(8L, "8", "bigint", PgSqlDbType.Bigint, DbType.Int64);

        await AssertType((short)8, "8", "bigint", PgSqlDbType.Bigint, DbType.Int64, isDefault: false);
        await AssertType(8,        "8", "bigint", PgSqlDbType.Bigint, DbType.Int64, isDefault: false);
        await AssertType((byte)8,  "8", "bigint", PgSqlDbType.Bigint, DbType.Int64, isDefault: false);
        await AssertType(8F,       "8", "bigint", PgSqlDbType.Bigint, DbType.Int64, isDefault: false);
        await AssertType(8D,       "8", "bigint", PgSqlDbType.Bigint, DbType.Int64, isDefault: false);
        await AssertType(8M,       "8", "bigint", PgSqlDbType.Bigint, DbType.Int64, isDefault: false);
    }

    [Theory]
    [InlineData(4.123456789012345, "4.123456789012345")]
    [InlineData(double.NaN, "NaN")]
    [InlineData(double.PositiveInfinity, "Infinity")]
    [InlineData(double.NegativeInfinity, "-Infinity")]
    public async Task Double(double value, string sqlLiteral)
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        MinimumPgVersion(conn, "12.0");

        //Assert
        await AssertType(value, sqlLiteral, "double precision", PgSqlDbType.Double, DbType.Double);
    }

    [Theory]
    [InlineData(0.123456F, "0.123456")]
    [InlineData(float.NaN, "NaN")]
    [InlineData(float.PositiveInfinity, "Infinity")]
    [InlineData(float.NegativeInfinity, "-Infinity")]
    public Task @float(float value, string sqlLiteral)
        => AssertType(value, sqlLiteral, "real", PgSqlDbType.Real, DbType.Single);

    [Theory]
    [InlineData(short.MaxValue + 1, "smallint")]
    [InlineData(int.MaxValue + 1L, "integer")]
    [InlineData(long.MaxValue + 1D, "bigint")]
    public Task write_overflow<T>(T value, string pgTypeName)
        => AssertTypeUnsupportedWrite<T, OverflowException>(value, pgTypeName);

    [Theory]
    [InlineData((short)0, short.MaxValue + 1D, "int")]
    [InlineData(0, int.MaxValue + 1D, "bigint")]
    [InlineData(0L, long.MaxValue + 1D, "decimal")]
    public Task read_overflow<T>(T _, double value, string pgTypeName)
        => AssertTypeUnsupportedRead<T, OverflowException>(value.ToString(CultureInfo.InvariantCulture), pgTypeName);
}

public sealed class NumericTypeTests_NonMultiplexing() : NumericTypeTests(MultiplexingMode.NonMultiplexing);
public sealed class NumericTypeTests_Multiplexing() : NumericTypeTests(MultiplexingMode.Multiplexing);
