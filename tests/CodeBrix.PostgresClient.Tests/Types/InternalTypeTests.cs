using System.Threading.Tasks;
using CodeBrix.PostgresClient.PgSqlTypes;
using SilverAssertions;
using Xunit;

namespace CodeBrix.PostgresClient.Tests.Types; //was previously: Npgsql.Tests.Types;

public abstract class InternalTypeTests(MultiplexingMode multiplexingMode) : MultiplexingTestBase(multiplexingMode)
{
    [Fact]
    public async Task read_internal_char()
    {
        //Arrange
        using var conn = await OpenConnectionAsync();
        using var cmd = new PgSqlCommand("SELECT typdelim FROM pg_type WHERE typname='int4'", conn);

        //Act
        using var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        reader.Read();

        //Assert
        reader.GetChar(0).Should().Be(',');
        reader.GetValue(0).Should().Be(',');
        reader.GetProviderSpecificValue(0).Should().Be(',');
        reader.GetFieldType(0).Should().Be(typeof(char));
    }

    [Theory]
    [InlineData(PgSqlDbType.Oid)]
    [InlineData(PgSqlDbType.Regtype)]
    [InlineData(PgSqlDbType.Regconfig)]
    public async Task internal_uint_types(PgSqlDbType pgSqlDbType)
    {
        //Arrange
        var postgresType = pgSqlDbType.ToString().ToLowerInvariant();
        using var conn = await OpenConnectionAsync();
        using var cmd = new PgSqlCommand($"SELECT @max, 4294967295::{postgresType}, @eight, 8::{postgresType}", conn);
        cmd.Parameters.AddWithValue("max", pgSqlDbType, uint.MaxValue);
        cmd.Parameters.AddWithValue("eight", pgSqlDbType, 8u);

        //Act
        using var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        reader.Read();

        //Assert
        for (var i = 0; i < reader.FieldCount; i++)
            reader.GetFieldType(i).Should().Be(typeof(uint));

        reader.GetValue(0).Should().Be(uint.MaxValue);
        reader.GetValue(1).Should().Be(uint.MaxValue);
        reader.GetValue(2).Should().Be(8u);
        reader.GetValue(3).Should().Be(8u);
    }

    [Fact]
    public async Task tid()
    {
        //Arrange
        var expected = new PgSqlTid(3, 5);
        using var conn = await OpenConnectionAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT '(1234,40000)'::tid, @p::tid";
        cmd.Parameters.AddWithValue("p", PgSqlDbType.Tid, expected);

        //Act
        using var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        reader.Read();

        //Assert
        reader.GetFieldValue<PgSqlTid>(0).BlockNumber.Should().Be(1234u);
        reader.GetFieldValue<PgSqlTid>(0).OffsetNumber.Should().Be((ushort)40000);
        reader.GetFieldValue<PgSqlTid>(1).BlockNumber.Should().Be(expected.BlockNumber);
        reader.GetFieldValue<PgSqlTid>(1).OffsetNumber.Should().Be(expected.OffsetNumber);
    }

    #region PgSqlLogSequenceNumber / PgLsn

    public static readonly TheoryData<PgSqlLogSequenceNumber, object, bool> EqualsObjectCases = new()
    {
        { new PgSqlLogSequenceNumber(1ul), null, false },
        { new PgSqlLogSequenceNumber(1ul), new object(), false },
        { new PgSqlLogSequenceNumber(1ul), 1ul, false }, // no implicit cast
        { new PgSqlLogSequenceNumber(1ul), "0/0", false }, // no implicit cast/parsing
        { new PgSqlLogSequenceNumber(1ul), new PgSqlLogSequenceNumber(1ul), true }
    };

    [Theory]
    [MemberData(nameof(EqualsObjectCases))]
    public void PgSqlLogSequenceNumber_equals(PgSqlLogSequenceNumber lsn, object obj, bool expected)
        => lsn.Equals(obj).Should().Be(expected);

    [Fact]
    public async Task PgSqlLogSequenceNumber()
    {
        //Arrange
        var expected1 = new PgSqlLogSequenceNumber(42949672971ul);
        CodeBrix.PostgresClient.PgSqlTypes.PgSqlLogSequenceNumber.Parse("A/B").Should().Be(expected1);
        await using var conn = await OpenConnectionAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 'A/B'::pg_lsn, @p::pg_lsn";
        cmd.Parameters.AddWithValue("p", PgSqlDbType.PgLsn, expected1);

        //Act
        await using var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        reader.Read();
        var result1 = reader.GetFieldValue<PgSqlLogSequenceNumber>(0);
        var result2 = reader.GetFieldValue<PgSqlLogSequenceNumber>(1);

        //Assert
        result1.Should().Be(expected1);
        ((ulong)result1).Should().Be(42949672971ul);
        result1.ToString().Should().Be("A/B");
        result2.Should().Be(expected1);
        ((ulong)result2).Should().Be(42949672971ul);
        result2.ToString().Should().Be("A/B");
    }

    #endregion PgSqlLogSequenceNumber / PgLsn
}

public sealed class InternalTypeTests_NonMultiplexing() : InternalTypeTests(MultiplexingMode.NonMultiplexing);
public sealed class InternalTypeTests_Multiplexing() : InternalTypeTests(MultiplexingMode.Multiplexing);
