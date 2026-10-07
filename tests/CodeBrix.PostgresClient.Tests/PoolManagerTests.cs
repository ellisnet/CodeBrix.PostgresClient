using System;
using CodeBrix.PostgresClient.Tests.Support;
using SilverAssertions;
using Xunit;

namespace CodeBrix.PostgresClient.Tests; //was previously: Npgsql.Tests;

[Collection(NonParallelCollection.Name)]
public class PoolManagerTests : TestBase, IDisposable
{
    public PoolManagerTests() => PoolManager.Reset();

    public void Dispose() => PoolManager.Reset();

    [Fact]
    public void with_canonical_connection_string()
    {
        var connString = new PgSqlConnectionStringBuilder(ConnectionString).ToString();
        using (var conn = new PgSqlConnection(connString))
            conn.Open();
        var connString2 = new PgSqlConnectionStringBuilder(ConnectionString)
        {
            ApplicationName = "Another connstring"
        }.ToString();
        using (var conn = new PgSqlConnection(connString2))
            conn.Open();
    }

#if DEBUG
    [Fact]
    public void many_pools()
    {
        PoolManager.Reset();
        for (var i = 0; i < 15; i++)
        {
            var connString = new PgSqlConnectionStringBuilder(ConnectionString)
            {
                ApplicationName = "App" + i
            }.ToString();
            using var conn = new PgSqlConnection(connString);
            conn.Open();
        }
        PoolManager.Reset();
    }
#endif

    [Fact]
    public void ClearAllPools()
    {
        //Arrange
        using (var conn = new PgSqlConnection(ConnectionString))
            conn.Open();
        // Now have one connection in the pool
        PoolManager.Pools.TryGetValue(ConnectionString, out var pool).Should().BeTrue();
        pool.Statistics.Idle.Should().Be(1);

        //Act
        PgSqlConnection.ClearAllPools();

        //Assert
        pool.Statistics.Idle.Should().Be(0);
        pool.Statistics.Total.Should().Be(0);
    }

    [Fact]
    public void ClearAllPools_with_busy()
    {
        PgSqlDataSource pool;
        using (var conn = new PgSqlConnection(ConnectionString))
        {
            conn.Open();
            using (var anotherConn = new PgSqlConnection(ConnectionString))
                anotherConn.Open();
            // We have one idle, one busy

            PgSqlConnection.ClearAllPools();
            PoolManager.Pools.TryGetValue(ConnectionString, out pool).Should().BeTrue();
            pool.Statistics.Idle.Should().Be(0);
            pool.Statistics.Total.Should().Be(1);
        }
        pool.Statistics.Idle.Should().Be(0);
        pool.Statistics.Total.Should().Be(0);
    }
}
