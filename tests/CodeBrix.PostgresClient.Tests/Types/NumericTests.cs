using System;
using System.Data;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using CodeBrix.PostgresClient.PgSqlTypes;
using SilverAssertions;
using Xunit;

namespace CodeBrix.PostgresClient.Tests.Types; //was previously: Npgsql.Tests.Types;

public abstract class NumericTests(MultiplexingMode multiplexingMode) : MultiplexingTestBase(multiplexingMode)
{
    public static readonly TheoryData<string, decimal> ReadWriteCases = new()
    {
        { "0.0000000000000000000000000001::numeric", 0.0000000000000000000000000001M },
        { "0.000000000000000000000001::numeric", 0.000000000000000000000001M },
        { "0.00000000000000000001::numeric", 0.00000000000000000001M },
        { "0.0000000000000001::numeric", 0.0000000000000001M },
        { "0.000000000001::numeric", 0.000000000001M },
        { "0.00000001::numeric", 0.00000001M },
        { "0.0001::numeric", 0.0001M },
        { "0.123456000000000100000000::numeric", 0.123456000000000100000000M },
        { "1::numeric", 1M },
        { "10000::numeric", 10000M },
        { "100000000::numeric", 100000000M },
        { "1000000000000::numeric", 1000000000000M },
        { "10000000000000000::numeric", 10000000000000000M },
        { "100000000000000000000::numeric", 100000000000000000000M },
        { "1000000000000000000000000::numeric", 1000000000000000000000000M },
        { "10000000000000000000000000000::numeric", 10000000000000000000000000000M },

        { "1E-28::numeric", 0.0000000000000000000000000001M },
        { "1E-24::numeric", 0.000000000000000000000001M },
        { "1E-20::numeric", 0.00000000000000000001M },
        { "1E-16::numeric", 0.0000000000000001M },
        { "1E-12::numeric", 0.000000000001M },
        { "1E-8::numeric", 0.00000001M },
        { "1E-4::numeric", 0.0001M },
        { "1E+0::numeric", 1M },
        { "1E+4::numeric", 10000M },
        { "1E+8::numeric", 100000000M },
        { "1E+12::numeric", 1000000000000M },
        { "1E+16::numeric", 10000000000000000M },
        { "1E+20::numeric", 100000000000000000000M },
        { "1E+24::numeric", 1000000000000000000000000M },
        { "1E+28::numeric", 10000000000000000000000000000M },

        { "1.2222333344445555666677778888::numeric", 1.2222333344445555666677778888M },
        { "11.222233334444555566667777888::numeric", 11.222233334444555566667777888M },
        { "111.22223333444455556666777788::numeric", 111.22223333444455556666777788M },
        { "1111.2222333344445555666677778::numeric", 1111.2222333344445555666677778M },

        { "+79228162514264337593543950335::numeric", +79228162514264337593543950335M },
        { "-79228162514264337593543950335::numeric", -79228162514264337593543950335M },

        // It is important to test rounding on both even and odd
        // numbers to make sure midpoint rounding is away from zero.
        { "1::numeric(10,2)", 1.00M },
        { "2::numeric(10,2)", 2.00M },

        { "1.2::numeric(10,1)", 1.2M },
        { "1.2::numeric(10,2)", 1.20M },
        { "1.2::numeric(10,3)", 1.200M },
        { "1.2::numeric(10,4)", 1.2000M },
        { "1.2::numeric(10,5)", 1.20000M },

        { "1.4::numeric(10,0)", 1M },
        { "1.5::numeric(10,0)", 2M },
        { "2.4::numeric(10,0)", 2M },
        { "2.5::numeric(10,0)", 3M },

        { "-1.4::numeric(10,0)", -1M },
        { "-1.5::numeric(10,0)", -2M },
        { "-2.4::numeric(10,0)", -2M },
        { "-2.5::numeric(10,0)", -3M },

        // Bug 2033
        { "0.0036882500000000000000000000", 0.0036882500000000000000000000M },
        // Bug 5848
        { "10836968.715000000000000000000000", 10836968.715000000000000000000000M },

        { "936490726837837729197", 936490726837837729197M },
        { "9364907268378377291970000", 9364907268378377291970000M },
        { "3649072683783772919700000000", 3649072683783772919700000000M },
        { "1234567844445555.000000000", 1234567844445555.000000000M },
        { "11112222000000000000", 11112222000000000000M },
        { "0::numeric", 0M }
    };

    [Theory]
    [MemberData(nameof(ReadWriteCases))]
    public async Task read(string query, decimal expected)
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        using var cmd = new PgSqlCommand("SELECT " + query, conn);

        //Act
        var value = (decimal)(await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken));

        //Assert
        decimal.GetBits(value).Should().Equal(decimal.GetBits(expected));
    }

    [Theory]
    [MemberData(nameof(ReadWriteCases))]
    public async Task write(string query, decimal expected)
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        using var cmd = new PgSqlCommand("SELECT @p, @p = " + query, conn);
        cmd.Parameters.AddWithValue("p", expected);

        //Act
        using var rdr = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        rdr.Read();

        //Assert
        decimal.GetBits(rdr.GetFieldValue<decimal>(0)).Should().Equal(decimal.GetBits(expected));
        rdr.GetFieldValue<bool>(1).Should().BeTrue();
    }

    [Fact]
    public async Task numeric()
    {
        await AssertType(5.5m, "5.5", "numeric", PgSqlDbType.Numeric, DbType.Decimal);
        await AssertTypeWrite(5.5m, "5.5", "numeric", PgSqlDbType.Numeric, DbType.VarNumeric, inferredDbType: DbType.Decimal);

        await AssertType((short)8, "8", "numeric", PgSqlDbType.Numeric, DbType.Decimal, isDefault: false);
        await AssertType(8,        "8", "numeric", PgSqlDbType.Numeric, DbType.Decimal, isDefault: false);
        await AssertType((byte)8,  "8", "numeric", PgSqlDbType.Numeric, DbType.Decimal, isDefault: false);
        await AssertType(8F,       "8", "numeric", PgSqlDbType.Numeric, DbType.Decimal, isDefault: false);
        await AssertType(8D,       "8", "numeric", PgSqlDbType.Numeric, DbType.Decimal, isDefault: false);
        await AssertType(8M,       "8", "numeric", PgSqlDbType.Numeric, DbType.Decimal, isDefault: false);
    }

    // Tests that when Numeric value does not fit in a System.Decimal and reader is in ReaderState.InResult, the value was read wholly and it is safe to continue reading
    [Fact]
    public async Task read_overflow_is_safe()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        //This 29-digit number causes OverflowException. Here it is important to have unread column after failing one to leave it ReaderState.InResult
        using var cmd = new PgSqlCommand(@"SELECT (0.20285714285714285714285714285)::numeric, generate_series FROM generate_series(1, 2)", conn);

        //Act
        using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess, TestContext.Current.CancellationToken);
        var i = 1;

        while (reader.Read())
        {
            Assert.Throws<OverflowException>(() => reader.GetDecimal(0))
                .Message.Should().Be("Numeric value does not fit in a System.Decimal");
            var intValue = reader.GetInt32(1);

            intValue.Should().Be(i++);
            conn.FullState.Should().Be(ConnectionState.Open | ConnectionState.Fetching);
            conn.State.Should().Be(ConnectionState.Open);
            reader.State.Should().Be(ReaderState.InResult);
        }
    }

    [Theory]
    [MemberData(nameof(ReadWriteCases))]
    public async Task read_BigInteger(string query, decimal expected)
    {
        //Arrange
        var bigInt = new BigInteger(expected);
        using var conn = await OpenConnectionAsync();
        using var cmd = new PgSqlCommand("SELECT " + query, conn);

        //Act
        using var rdr = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        await rdr.ReadAsync(TestContext.Current.CancellationToken);

        //Assert
        if (decimal.Floor(expected) == expected)
            rdr.GetFieldValue<BigInteger>(0).Should().Be(bigInt);
        else
            Assert.Throws<InvalidCastException>(() => rdr.GetFieldValue<BigInteger>(0))
                .Message.Should().Be("Numeric value with non-zero fractional digits not supported by BigInteger");
    }

    [Theory]
    [MemberData(nameof(ReadWriteCases))]
    public async Task write_BigInteger(string query, decimal expected)
    {
        if (decimal.Floor(expected) == expected)
        {
            var bigInt = new BigInteger(expected);
            using var conn = await OpenConnectionAsync();
            using var cmd = new PgSqlCommand("SELECT @p, @p = " + query, conn);
            cmd.Parameters.AddWithValue("p", bigInt);
            using var rdr = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
            await rdr.ReadAsync(TestContext.Current.CancellationToken);
            rdr.GetFieldValue<BigInteger>(0).Should().Be(bigInt);
            rdr.GetFieldValue<bool>(1).Should().BeTrue();
        }
    }

    [Fact]
    public async Task BigInteger_large()
    {
        //Arrange
        var num = BigInteger.Parse(string.Join("", Enumerable.Range(0, 17000).Select(i => ((i + 1) % 10).ToString())));
        using var conn = await OpenConnectionAsync();
        using var cmd = new PgSqlCommand("SELECT '0.1'::numeric, @p", conn);
        cmd.Parameters.AddWithValue("p", num);

        //Act
        using var rdr = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess, TestContext.Current.CancellationToken);
        await rdr.ReadAsync(TestContext.Current.CancellationToken);

        //Assert
        Assert.Throws<InvalidCastException>(() => rdr.GetFieldValue<BigInteger>(0));
        rdr.GetFieldValue<BigInteger>(1).Should().Be(num);
    }

    [Fact]
    public async Task numeric_zero_with_scale()
    {
        //Arrange
        // Scale should not be lost when dealing with 0
        using var conn = await OpenConnectionAsync();
        using var cmd = new PgSqlCommand("SELECT @p", conn);
        var param = new PgSqlParameter("p", DbType.Decimal, 10, null, ParameterDirection.Input, false, 10, 2, DataRowVersion.Default, 0.00M);
        cmd.Parameters.Add(param);

        //Act
        using var rdr = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        await rdr.ReadAsync(TestContext.Current.CancellationToken);
        var value = rdr.GetFieldValue<decimal>(0);

        //Assert
        value.Scale.Should().Be(2);
    }

    [Theory, IssueLink("https://github.com/npgsql/npgsql/issues/6383")]
    [InlineData(CommandBehavior.Default)]
    [InlineData(CommandBehavior.SequentialAccess)]
    public async Task read_many_numerics_as_BigInteger(CommandBehavior behavior)
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1234567890::numeric FROM generate_series(1, 8000)";

        //Act
        await using var reader = await cmd.ExecuteReaderAsync(behavior, TestContext.Current.CancellationToken);
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            var act = async () => await reader.GetFieldValueAsync<BigInteger>(0, TestContext.Current.CancellationToken);
            await act.Should().NotThrowAsync();
        }
    }
}

public sealed class NumericTests_NonMultiplexing() : NumericTests(MultiplexingMode.NonMultiplexing);
public sealed class NumericTests_Multiplexing() : NumericTests(MultiplexingMode.Multiplexing);
