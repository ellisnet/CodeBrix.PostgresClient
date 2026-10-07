using System;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using CodeBrix.PostgresClient.PgSqlTypes;
using SilverAssertions;
using Xunit;

namespace CodeBrix.PostgresClient.Tests.Types; //was previously: Npgsql.Tests.Types;

/// <summary>
/// Tests on PostgreSQL numeric types
/// </summary>
/// <remarks>
/// https://www.postgresql.org/docs/current/static/datatype-net-types.html
/// </remarks>
public abstract class NetworkTypeTests(MultiplexingMode multiplexingMode) : MultiplexingTestBase(multiplexingMode)
{
    [Fact]
    public Task inet_v4_as_IPAddress()
        => AssertType(IPAddress.Parse("192.168.1.1"), "192.168.1.1/32", "inet", PgSqlDbType.Inet, skipArrayCheck: true);

    [Fact]
    public Task inet_v4_array_as_IPAddress_array()
        => AssertType(
            new[]
            {
                IPAddress.Parse("192.168.1.1"),
                IPAddress.Parse("192.168.1.2")
            },
            "{192.168.1.1,192.168.1.2}", "inet[]", PgSqlDbType.Inet | PgSqlDbType.Array);

    [Fact]
    public Task inet_v6_as_IPAddress()
        => AssertType(
            IPAddress.Parse("2001:1db8:85a3:1142:1000:8a2e:1370:7334"),
            "2001:1db8:85a3:1142:1000:8a2e:1370:7334/128",
            "inet",
            PgSqlDbType.Inet,
            skipArrayCheck: true);

    [Fact]
    public Task inet_v6_array_as_IPAddress_array()
        => AssertType(
            new[]
            {
                IPAddress.Parse("2001:1db8:85a3:1142:1000:8a2e:1370:7334"),
                IPAddress.Parse("2001:1db8:85a3:1142:1000:8a2e:1370:7335")
            },
            "{2001:1db8:85a3:1142:1000:8a2e:1370:7334,2001:1db8:85a3:1142:1000:8a2e:1370:7335}", "inet[]", PgSqlDbType.Inet | PgSqlDbType.Array);

    [Fact, IssueLink("https://github.com/dotnet/corefx/issues/33373")]
    public Task IPAddress_Any()
        => AssertTypeWrite(IPAddress.Any, "0.0.0.0/32", "inet", PgSqlDbType.Inet, skipArrayCheck: true);

    [Fact]
    public Task IPNetwork_as_cidr()
        => AssertType(
            new IPNetwork(IPAddress.Parse("192.168.1.0"), 24),
            "192.168.1.0/24",
            "cidr",
            PgSqlDbType.Cidr);

    [Fact]
    public Task inet_v4_as_PgSqlInet()
        => AssertType(
            new PgSqlInet(IPAddress.Parse("192.168.1.1"), 24),
            "192.168.1.1/24",
            "inet",
            PgSqlDbType.Inet,
            isDefaultForReading: false);

    [Fact]
    public Task inet_v6_as_PgSqlInet()
        => AssertType(
            new PgSqlInet(IPAddress.Parse("2001:1db8:85a3:1142:1000:8a2e:1370:7334"), 24),
            "2001:1db8:85a3:1142:1000:8a2e:1370:7334/24",
            "inet",
            PgSqlDbType.Inet,
            isDefaultForReading: false);

    [Fact]
    public Task macaddr()
        => AssertType(PhysicalAddress.Parse("08-00-2B-01-02-03"), "08:00:2b:01:02:03", "macaddr", PgSqlDbType.MacAddr);

    [Fact]
    public async Task macaddr8()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        if (conn.PostgreSqlVersion < new Version(10, 0))
            Assert.Skip("macaddr8 only supported on PostgreSQL 10 and above");

        //Assert
        await AssertType(PhysicalAddress.Parse("08-00-2B-01-02-03-04-05"), "08:00:2b:01:02:03:04:05", "macaddr8", PgSqlDbType.MacAddr8,
            isDefaultForWriting: false);
    }

    [Fact]
    public async Task macaddr8_write_with_6_bytes()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        if (conn.PostgreSqlVersion < new Version(10, 0))
            Assert.Skip("macaddr8 only supported on PostgreSQL 10 and above");

        //Assert
        await AssertTypeWrite(PhysicalAddress.Parse("08-00-2B-01-02-03"), "08:00:2b:ff:fe:01:02:03", "macaddr8", PgSqlDbType.MacAddr8,
            isDefault: false);
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/835")]
    public async Task macaddr_multiple()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        await using var cmd = new PgSqlCommand("SELECT unnest(ARRAY['08-00-2B-01-02-03'::MACADDR, '08-00-2B-01-02-04'::MACADDR])", conn);

        //Act
        await using var r = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        r.Read();
        var p1 = (PhysicalAddress)r[0];
        r.Read();
        var p2 = (PhysicalAddress)r[0];

        //Assert
        p1.Should().Be(PhysicalAddress.Parse("08-00-2B-01-02-03"));
        p2.Should().Be(PhysicalAddress.Parse("08-00-2B-01-02-04"));
    }

    [Fact]
    public async Task macaddr_write_validation()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        if (conn.PostgreSqlVersion < new Version(10, 0))
            Assert.Skip("macaddr8 only supported on PostgreSQL 10 and above");

        //Assert
        await AssertTypeUnsupportedWrite<PhysicalAddress, ArgumentException>(PhysicalAddress.Parse("08-00-2B-01-02-03-04-05"), "macaddr");
    }
}

public sealed class NetworkTypeTests_NonMultiplexing() : NetworkTypeTests(MultiplexingMode.NonMultiplexing);
public sealed class NetworkTypeTests_Multiplexing() : NetworkTypeTests(MultiplexingMode.Multiplexing);
