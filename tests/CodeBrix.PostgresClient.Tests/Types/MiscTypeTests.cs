using System;
using System.Data;
using System.Threading.Tasks;
using CodeBrix.PostgresClient.PgSqlTypes;
using SilverAssertions;
using Xunit;

namespace CodeBrix.PostgresClient.Tests.Types; //was previously: Npgsql.Tests.Types;

/// <summary>
/// Tests on PostgreSQL types which don't fit elsewhere
/// </summary>
public abstract class MiscTypeTests(MultiplexingMode multiplexingMode) : MultiplexingTestBase(multiplexingMode)
{
    [Fact]
    public async Task boolean()
    {
        await AssertType(true, "true", "boolean", PgSqlDbType.Boolean, DbType.Boolean, skipArrayCheck: true);
        await AssertType(false, "false", "boolean", PgSqlDbType.Boolean, DbType.Boolean, skipArrayCheck: true);

        // The literal representations for bools inside array are different ({t,f} instead of true/false, so we check separately.
        await AssertType(new[] { true, false }, "{t,f}", "boolean[]", PgSqlDbType.Boolean | PgSqlDbType.Array);
    }

    [Fact]
    public Task uuid()
        => AssertType(
            new Guid("a0eebc99-9c0b-4ef8-bb6d-6bb9bd380a11"),
            "a0eebc99-9c0b-4ef8-bb6d-6bb9bd380a11",
            "uuid", PgSqlDbType.Uuid, DbType.Guid);

    // Makes sure that the PostgreSQL 'unknown' type (OID 705) is read properly
    [Fact]
    public async Task read_unknown()
    {
        //Arrange
        const string expected = "some_text";
        await using var conn = await OpenConnectionAsync();
        await using var cmd = new PgSqlCommand($"SELECT '{expected}'", conn);

        //Act
        await using var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        reader.Read();

        //Assert
        reader.GetString(0).Should().Be(expected);
        reader.GetValue(0).Should().Be(expected);
        reader.GetFieldValue<char[]>(0).Should().Equal(expected.ToCharArray());
        reader.GetFieldType(0).Should().Be(typeof(string));
    }

    [Fact]
    public async Task @null()
    {
        await using var conn = await OpenConnectionAsync();
        await using (var cmd = new PgSqlCommand("SELECT @p1::TEXT, @p2::TEXT, @p3::TEXT", conn))
        {
            cmd.Parameters.AddWithValue("p1", DBNull.Value);
            cmd.Parameters.Add(new PgSqlParameter<string>("p2", null));
            cmd.Parameters.Add(new PgSqlParameter<object>("p3", DBNull.Value));

            await using var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
            reader.Read();
            for (var i = 0; i < cmd.Parameters.Count; i++)
            {
                reader.IsDBNull(i).Should().BeTrue();
                reader.GetFieldType(i).Should().Be(typeof(string));
            }
        }

        // Setting non-generic PgSqlParameter.Value to null is not allowed, only DBNull.Value
        await using (var cmd = new PgSqlCommand("SELECT @p4::TEXT", conn))
        {
            cmd.Parameters.AddWithValue("p4", PgSqlDbType.Text, null);
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken));
        }

        // Setting generic PgSqlParameter<object>.Value to null is not allowed, only DBNull.Value
        await using (var cmd = new PgSqlCommand("SELECT @p4::TEXT", conn))
        {
            cmd.Parameters.Add(new PgSqlParameter<object>("p4", PgSqlDbType.Text) { Value = null });
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken));
        }
    }

    // Makes sure that setting DbType.Object makes PgSql infer the type
    [Fact]
    [IssueLink("https://github.com/npgsql/npgsql/issues/694")]
    public async Task DbType_causes_inference()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        await using var cmd = new PgSqlCommand("SELECT @p", conn);
        cmd.Parameters.Add(new PgSqlParameter { ParameterName="p", DbType = DbType.Object, Value = 3 });

        //Act
        var result = await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken);

        //Assert
        result.Should().Be(3);
    }

    #region Unrecognized types

    // Retrieves a type as an unknown type, i.e. untreated string
    [Fact]
    public async Task AllResultTypesAreUnknown()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        await using var cmd = new PgSqlCommand("SELECT TRUE", conn);
        cmd.AllResultTypesAreUnknown = true;

        //Act
        await using var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        reader.Read();

        //Assert
        reader.GetFieldType(0).Should().Be(typeof(string));
        reader.GetString(0).Should().Be("t");
    }

    // Mixes and matches an unknown type with a known type
    [Fact]
    public async Task UnknownResultTypeList()
    {
        if (IsMultiplexing)
            return;

        //Arrange
        await using var conn = await OpenConnectionAsync();
        await using var cmd = new PgSqlCommand("SELECT TRUE, 8", conn);
        cmd.UnknownResultTypeList = [true, false];

        //Act
        await using var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        reader.Read();

        //Assert
        reader.GetFieldType(0).Should().Be(typeof(string));
        reader.GetString(0).Should().Be("t");
        reader.GetValue(0).Should().Be("t");
        reader.GetFieldValue<object>(0).Should().Be("t");

        // Try some alternative text types
        reader.GetFieldValue<byte[]>(0).Should().Equal("t"u8.ToArray());
        reader.GetFieldValue<char[]>(0).Should().Equal('t');

        // Try as async
        (await reader.GetFieldValueAsync<string>(0, TestContext.Current.CancellationToken)).Should().Be("t");
        (await reader.GetFieldValueAsync<object>(0, TestContext.Current.CancellationToken)).Should().Be("t");
        (await reader.GetFieldValueAsync<byte[]>(0, TestContext.Current.CancellationToken)).Should().Equal("t"u8.ToArray());
        (await reader.GetFieldValueAsync<char[]>(0, TestContext.Current.CancellationToken)).Should().Equal('t');

        // Normal binary column
        reader.GetInt32(1).Should().Be(8);
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/711")]
    public async Task known_type_as_unknown()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        await using var cmd = new PgSqlCommand("SELECT 8", conn);
        cmd.AllResultTypesAreUnknown = true;

        //Act
        var result = await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken);

        //Assert
        result.Should().Be("8");
    }

    // Sends a null value parameter with no PgSqlDbType or DbType, but with context for the backend to handle it
    [Fact]
    public async Task unrecognized_null()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        await using var cmd = new PgSqlCommand("SELECT @p::TEXT", conn);
        var p = new PgSqlParameter("p", DBNull.Value);
        cmd.Parameters.Add(p);

        //Act
        await using var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        reader.Read();

        //Assert
        reader.IsDBNull(0).Should().BeTrue();
        reader.GetFieldType(0).Should().Be(typeof(string));
    }

    // Sends a value parameter with an explicit PgSqlDbType.Unknown, but with context for the backend to handle it
    [Fact]
    public async Task send_unknown()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        await using var cmd = new PgSqlCommand("SELECT @p::INT4", conn);
        var p = new PgSqlParameter("p", "8");
        cmd.Parameters.Add(p);

        //Act
        await using var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        reader.Read();

        //Assert
        reader.GetFieldType(0).Should().Be(typeof(int));
        reader.GetInt32(0).Should().Be(8);
    }

    #endregion

    [Fact]
    public async Task object_array()
    {
        await AssertTypeWrite(new object[] { (short)4, null, (long)5, 6 }, "{4,NULL,5,6}", "integer[]", PgSqlDbType.Integer | PgSqlDbType.Array, isDefault: false);
        await AssertTypeWrite(new object[] { "text", null, DBNull.Value, "chars".ToCharArray(), 'c' }, "{text,NULL,NULL,chars,c}", "text[]", PgSqlDbType.Text | PgSqlDbType.Array, isDefault: false);

        await using var dataSource = CreateDataSource(b => b.ConnectionStringBuilder.Timezone = "Europe/Berlin");
        await AssertTypeWrite(dataSource, new object[] { DateTime.UnixEpoch, null, DBNull.Value, DateTime.UnixEpoch.AddDays(1) }, "{\"1970-01-01 01:00:00+01\",NULL,NULL,\"1970-01-02 01:00:00+01\"}", "timestamp with time zone[]", PgSqlDbType.TimestampTz | PgSqlDbType.Array, isDefault: false);
        await Assert.ThrowsAsync<ArgumentException>(() => AssertTypeWrite(dataSource, new object[]
            {
                DateTime.Now, null, DBNull.Value, DateTime.UnixEpoch.AddDays(1)
            }, "{\"1970-01-01 01:00:00+01\",NULL,NULL,\"1970-01-02 01:00:00+01\"}", "timestamp with time zone[]",
            PgSqlDbType.TimestampTz | PgSqlDbType.Array, isDefault: false));
    }

    [Fact]
    public Task int2vector()
        => AssertType(new short[] { 4, 5, 6 }, "4 5 6", "int2vector", PgSqlDbType.Int2Vector, isDefault: false);

    [Fact]
    public Task oidvector()
        => AssertType(new uint[] { 4, 5, 6 }, "4 5 6", "oidvector", PgSqlDbType.Oidvector, isDefault: false);

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/1138")]
    public async Task @void()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();

        //Act
        var result = await conn.ExecuteScalarAsync("SELECT pg_sleep(0)", cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        result.Should().BeNull();
    }
}

public sealed class MiscTypeTests_NonMultiplexing() : MiscTypeTests(MultiplexingMode.NonMultiplexing);
public sealed class MiscTypeTests_Multiplexing() : MiscTypeTests(MultiplexingMode.Multiplexing);
