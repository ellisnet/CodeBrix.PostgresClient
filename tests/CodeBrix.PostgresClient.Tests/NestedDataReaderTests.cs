using System;
using System.Threading.Tasks;
using SilverAssertions;
using Xunit;
using static CodeBrix.PostgresClient.Tests.TestUtil;

namespace CodeBrix.PostgresClient.Tests; //was previously: Npgsql.Tests;

public class NestedDataReaderTests : TestBase
{
    [Fact]
    public async Task basic()
    {
        await using var conn = await OpenConnectionAsync();
        await using var command = new PgSqlCommand(@"SELECT ARRAY[ROW(1, 2, 3), ROW(4, 5, 6)]
                                                    UNION ALL
                                                    SELECT ARRAY[ROW(7, 8, 9), ROW(10, 11, 12)]", conn);
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        for (var i = 0; i < 2; i++)
        {
            await reader.ReadAsync(TestContext.Current.CancellationToken);
            using var nestedReader = reader.GetData(0);
            nestedReader.HasRows.Should().BeTrue();

            for (var j = 0; j < 2; j++)
            {
                nestedReader.Read().Should().BeTrue();
                nestedReader.FieldCount.Should().Be(3);
                nestedReader.GetFieldType(0).Should().Be(typeof(int));
                nestedReader.GetDataTypeName(0).Should().Be("integer");
                nestedReader.GetName(0).Should().Be("?column?");
                Assert.Throws<NotSupportedException>(() => nestedReader.GetOrdinal("c0"));
                for (var k = 0; k < 3; k++)
                {
                    nestedReader.GetInt32(k).Should().Be(1 + 6 * i + j * 3 + k);
                    nestedReader.GetValue(k).Should().Be(1 + 6 * i + j * 3 + k);
                }
            }
            if (i == 0)
                nestedReader.Read().Should().BeFalse();

            nestedReader.NextResult().Should().BeFalse();
            nestedReader.HasRows.Should().BeFalse();
        }
    }

    [Fact]
    public async Task different_field_count()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        await using var command = new PgSqlCommand(@"SELECT ARRAY[ROW(1), ROW(), ROW('2'::TEXT, 3), ROW(4)]", conn);

        //Act
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);

        //Assert
        (await reader.ReadAsync(TestContext.Current.CancellationToken)).Should().BeTrue();
        using var nestedReader = reader.GetData(0);
        nestedReader.Read().Should().BeTrue();
        nestedReader.FieldCount.Should().Be(1);
        nestedReader.GetFieldType(0).Should().Be(typeof(int));
        nestedReader.GetInt32(0).Should().Be(1);
        nestedReader.Read().Should().BeTrue();
        nestedReader.FieldCount.Should().Be(0);
        nestedReader.Read().Should().BeTrue();
        nestedReader.FieldCount.Should().Be(2);
        nestedReader.GetFieldType(0).Should().Be(typeof(string));
        nestedReader.GetFieldType(1).Should().Be(typeof(int));
        nestedReader.GetString(0).Should().Be("2");
        nestedReader.GetInt32(1).Should().Be(3);
        nestedReader.Read().Should().BeTrue();
        nestedReader.GetFieldType(0).Should().Be(typeof(int));
        nestedReader.GetInt32(0).Should().Be(4);
        nestedReader.Read().Should().BeFalse();
    }

    [Fact]
    public async Task nested()
    {
        await using var conn = await OpenConnectionAsync();
        await using var command = new PgSqlCommand(@"SELECT
                ARRAY[
                    ROW(
                        ARRAY[
                            ROW('row000'::TEXT, NULL::TEXT),
                            ROW('row010'::TEXT, 'row011'::TEXT)
                        ]
                    ),
                    ROW(
                        ARRAY[
                            ROW('row100'::TEXT, NULL::TEXT),
                            ROW('row110'::TEXT, 'row111'::TEXT)
                        ]
                    )
                ], 2", conn);
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);

        for (var i = 0; i < 1; i++)
        {
            await reader.ReadAsync(TestContext.Current.CancellationToken);
            using var nestedReader = reader.GetData(0);
            for (var j = 0; j < 2; j++)
            {
                nestedReader.Read().Should().BeTrue();
                var nestedReader2 = nestedReader.GetData(0);
                for (var k = 0; k < 2; k++)
                {
                    nestedReader2.Read().Should().BeTrue();
                    for (var l = 0; l < 2; l++)
                    {
                        if (k == 0 && l == 1)
                        {
                            nestedReader2.IsDBNull(l).Should().BeTrue();
                            nestedReader2.GetValue(l).Should().Be(DBNull.Value);
                            nestedReader2.GetProviderSpecificValue(l).Should().Be(DBNull.Value);
                        }
                        else
                        {
                            nestedReader2.GetString(l).Should().Be("row" + j + k + l);
                        }
                    }
                }
            }
            reader.GetInt32(1).Should().Be(2);
        }
    }

    [Fact]
    public async Task single_row()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        await using var command = new PgSqlCommand("SELECT ROW(1, ARRAY[ROW(2), ROW(3)])", conn);
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        await reader.ReadAsync(TestContext.Current.CancellationToken);

        //Act
        using var nestedReader = reader.GetData(0);

        //Assert
        nestedReader.Read().Should().BeTrue();
        nestedReader.FieldCount.Should().Be(2);
        nestedReader.GetInt32(0).Should().Be(1);
        using var nestedReader2 = nestedReader.GetData(1);
        for (var i = 0; i < 2; i++)
        {
            nestedReader2.Read().Should().BeTrue();
            nestedReader2.FieldCount.Should().Be(1);
            nestedReader2.GetInt32(0).Should().Be(2 + i);
        }
        nestedReader2.Read().Should().BeFalse();
    }

    [Fact]
    public async Task empty_array()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        await using var command = new PgSqlCommand("SELECT ARRAY[]::RECORD[]", conn);
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        await reader.ReadAsync(TestContext.Current.CancellationToken);

        //Act
        using var nestedReader = reader.GetData(0);

        //Assert
        nestedReader.Read().Should().BeFalse();
        nestedReader.NextResult().Should().BeFalse();
    }

    [Fact]
    public async Task composite()
    {
        await using var conn = await OpenConnectionAsync();
        var typeName = await GetTempTypeName(conn);
        await conn.ExecuteNonQueryAsync($"CREATE TYPE {typeName} AS (c0 integer, c1 text)", cancellationToken: TestContext.Current.CancellationToken);
        conn.ReloadTypes();
        var sqls = new string[]
        {
            $"SELECT ROW('1', '2')::{typeName}",
            $"SELECT ARRAY[ROW('1', '2')::{typeName}]"
        };
        foreach (var sql in sqls)
        {
            await using var command = new PgSqlCommand(sql, conn);
            await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
            await reader.ReadAsync(TestContext.Current.CancellationToken);
            using var nestedReader = reader.GetData(0);
            nestedReader.Read();
            nestedReader.GetDataTypeName(0).Should().Be("integer");
            nestedReader.GetDataTypeName(1).Should().Be("text");
            nestedReader.GetInt32(0).Should().Be(1);
            nestedReader.GetString(1).Should().Be("2");
            nestedReader.GetName(0).Should().Be("c0");
            nestedReader.GetName(1).Should().Be("c1");
            nestedReader.GetOrdinal("C1").Should().Be(1);
            nestedReader["C1"].Should().Be("2");
            Assert.Throws<IndexOutOfRangeException>(() => nestedReader.GetOrdinal("ABC"));
        }
    }

    [Fact]
    public void GetBytes()
    {
        //Arrange
        using var conn = OpenConnection();
        using var command = new PgSqlCommand(@"SELECT ROW('\x010203'::BYTEA, NULL::BYTEA)", conn);
        using var reader = command.ExecuteReader();
        reader.Read();
        using var nestedReader = reader.GetData(0);

        //Act
        nestedReader.Read();

        //Assert
        nestedReader.GetFieldType(0).Should().Be(typeof(byte[]));
        var buf = new byte[4];
        nestedReader.GetBytes(0, 0, null, 0, 3).Should().Be(3);
        nestedReader.GetBytes(0, 0, null, 0, 4).Should().Be(3);
        nestedReader.GetBytes(0, 0, buf, 0, 3).Should().Be(3);
        nestedReader.GetBytes(0, 0, buf, 0, 4).Should().Be(3);
        buf.Should().Equal(new byte[] { 1, 2, 3, 0 });
        buf = new byte[2];
        nestedReader.GetBytes(0, 0, buf, 0, 2).Should().Be(2);
        buf.Should().Equal(new byte[] { 1, 2 });
        buf = new byte[2];
        nestedReader.GetBytes(0, 1, buf, 1, 1).Should().Be(1);
        buf.Should().Equal(new byte[] { 0, 2 });
        nestedReader.GetBytes(0, 2, buf, 1, 1).Should().Be(1);
        buf.Should().Equal(new byte[] { 0, 3 });
        Assert.Throws<InvalidCastException>(() => nestedReader.GetBytes(1, 0, buf, 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => nestedReader.GetBytes(0, 4, buf, 0, 1));
    }

    [Fact]
    public async Task throw_after_next_row()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        await using var command = new PgSqlCommand(@"SELECT ROW(1) UNION ALL SELECT ROW(2) UNION ALL SELECT ROW(3)", conn);

        //Act
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);

        //Assert
        (await reader.ReadAsync(TestContext.Current.CancellationToken)).Should().BeTrue();
        var nestedReader = reader.GetData(0);
        nestedReader.Read();
        await reader.ReadAsync(TestContext.Current.CancellationToken);
        Assert.Throws<InvalidOperationException>(() => nestedReader.IsDBNull(0));
        nestedReader = reader.GetData(0);
        reader.Read();
        Assert.Throws<InvalidOperationException>(() => nestedReader.Read());
        nestedReader = reader.GetData(0);
        nestedReader.Read();
        nestedReader.IsDBNull(0).Should().BeFalse();
    }
}
