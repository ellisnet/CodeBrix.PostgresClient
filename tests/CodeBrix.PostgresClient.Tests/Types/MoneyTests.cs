using System.Data;
using System.Threading.Tasks;
using CodeBrix.PostgresClient.PgSqlTypes;
using SilverAssertions;
using Xunit;

namespace CodeBrix.PostgresClient.Tests.Types; //was previously: Npgsql.Tests.Types;

public class MoneyTests : TestBase
{
    public static readonly TheoryData<string, decimal> MoneyValues = new()
    {
        { "$1.22", 1.22M },
        { "$1,000.22", 1000.22M },
        { "$1,000,000.22", 1000000.22M },
        { "$1,000,000,000.22", 1000000000.22M },
        { "$1,000,000,000,000.22", 1000000000000.22M },
        { "$1,000,000,000,000,000.22", 1000000000000000.22M },

        { "$92,233,720,368,547,758.07", +92233720368547758.07M },
        { "-$92,233,720,368,547,758.08", -92233720368547758.08M },
        { "-$92,233,720,368,547,758.08", -92233720368547758.08M }
    };

    [Theory]
    [MemberData(nameof(MoneyValues))]
    public async Task money(string sqlLiteral, decimal money)
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        await conn.ExecuteNonQueryAsync("SET lc_monetary='C'", cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        await AssertType(conn, money, sqlLiteral, "money", PgSqlDbType.Money, DbType.Currency, isDefault: false);
    }

    [Fact]
    public async Task non_decimal_types_are_not_supported()
    {
        await AssertTypeUnsupportedRead<byte>("8", "money");
        await AssertTypeUnsupportedRead<short>("8", "money");
        await AssertTypeUnsupportedRead<int>("8", "money");
        await AssertTypeUnsupportedRead<long>("8", "money");
        await AssertTypeUnsupportedRead<float>("8", "money");
        await AssertTypeUnsupportedRead<double>("8", "money");
    }

    public static readonly TheoryData<string, decimal, decimal> WriteWithLargeScaleCases = new()
    {
        { "0.004::money", 0.004M, 0.00M },
        { "0.005::money", 0.005M, 0.01M }
    };

    [Theory]
    [MemberData(nameof(WriteWithLargeScaleCases))]
    public async Task write_with_large_scale(string query, decimal parameter, decimal expected)
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        using var cmd = new PgSqlCommand("SELECT @p, @p = " + query, conn);
        cmd.Parameters.Add(new PgSqlParameter("p", PgSqlDbType.Money) { Value = parameter });

        //Act
        using var rdr = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        rdr.Read();

        //Assert
        decimal.GetBits(rdr.GetFieldValue<decimal>(0)).Should().Equal(decimal.GetBits(expected));
        rdr.GetFieldValue<bool>(1).Should().BeTrue();
    }
}
