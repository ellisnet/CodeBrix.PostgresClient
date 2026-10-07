using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Transactions;
using CodeBrix.PostgresClient.Internal;
using CodeBrix.PostgresClient.Tests.Support;
using SilverAssertions;
using Xunit;
using IsolationLevel = System.Transactions.IsolationLevel;
using TransactionStatus = CodeBrix.PostgresClient.Internal.TransactionStatus;
using static CodeBrix.PostgresClient.Tests.Support.MockState;
using static CodeBrix.PostgresClient.Tests.TestUtil;

namespace CodeBrix.PostgresClient.Tests; //was previously: Npgsql.Tests;

public class MultipleHostsTests : TestBase
{
    public static readonly TheoryData<TargetSessionAttributes, MockState[], int> MyCases = new()
    {
        { TargetSessionAttributes.Standby,        new[] { Primary,         Standby         }, 1 },
        { TargetSessionAttributes.Standby,        new[] { PrimaryReadOnly, Standby         }, 1 },
        { TargetSessionAttributes.PreferStandby,  new[] { Primary,         Standby         }, 1 },
        { TargetSessionAttributes.PreferStandby,  new[] { PrimaryReadOnly, Standby         }, 1 },
        { TargetSessionAttributes.PreferStandby,  new[] { Primary,         Primary         }, 0 },
        { TargetSessionAttributes.Primary,        new[] { Standby,         Primary         }, 1 },
        { TargetSessionAttributes.Primary,        new[] { Standby,         PrimaryReadOnly }, 1 },
        { TargetSessionAttributes.PreferPrimary,  new[] { Standby,         Primary         }, 1 },
        { TargetSessionAttributes.PreferPrimary,  new[] { Standby,         PrimaryReadOnly }, 1 },
        { TargetSessionAttributes.PreferPrimary,  new[] { Standby,         Standby         }, 0 },
        { TargetSessionAttributes.Any,            new[] { Standby,         Primary         }, 0 },
        { TargetSessionAttributes.Any,            new[] { Primary,         Standby         }, 0 },
        { TargetSessionAttributes.Any,            new[] { PrimaryReadOnly, Standby         }, 0 },
        { TargetSessionAttributes.ReadWrite,      new[] { Standby,         Primary         }, 1 },
        { TargetSessionAttributes.ReadWrite,      new[] { PrimaryReadOnly, Primary         }, 1 },
        { TargetSessionAttributes.ReadOnly,       new[] { Primary,         Standby         }, 1 },
        { TargetSessionAttributes.ReadOnly,       new[] { PrimaryReadOnly, Standby         }, 0 }
    };

    public static readonly TheoryData<TargetSessionAttributes> AllTargetSessionAttributes =
        new(Enum.GetValues<TargetSessionAttributes>());

    [Theory]
    [MemberData(nameof(MyCases))]
    public async Task connect_to_correct_host_pooled(TargetSessionAttributes targetSessionAttributes, MockState[] servers, int expectedServer)
    {
        //Arrange
        var postmasters = servers.Select(s => PgPostmasterMock.Start(state: s)).ToArray();
        await using var __ = new DisposableWrapper(postmasters);

        var connectionStringBuilder = new PgSqlConnectionStringBuilder
        {
            Host = MultipleHosts(postmasters),
            ServerCompatibilityMode = ServerCompatibilityMode.NoTypeLoading,
            Pooling = true
        };

        await using var dataSource = new PgSqlDataSourceBuilder(connectionStringBuilder.ConnectionString)
            .BuildMultiHost();

        //Act
        await using var conn = await dataSource.OpenConnectionAsync(targetSessionAttributes, TestContext.Current.CancellationToken);

        //Assert
        conn.Port.Should().Be(postmasters[expectedServer].Port);

        for (var i = 0; i <= expectedServer; i++)
            _ = await postmasters[i].WaitForServerConnection();
    }

    [Theory]
    [MemberData(nameof(MyCases))]
    public async Task connect_to_correct_host_unpooled(TargetSessionAttributes targetSessionAttributes, MockState[] servers, int expectedServer)
    {
        //Arrange
        var postmasters = servers.Select(s => PgPostmasterMock.Start(state: s)).ToArray();
        await using var __ = new DisposableWrapper(postmasters);

        var connectionStringBuilder = new PgSqlConnectionStringBuilder
        {
            Host = MultipleHosts(postmasters),
            ServerCompatibilityMode = ServerCompatibilityMode.NoTypeLoading,
            Pooling = false
        };

        await using var dataSource = new PgSqlDataSourceBuilder(connectionStringBuilder.ConnectionString)
            .BuildMultiHost();

        //Act
        await using var conn = await dataSource.OpenConnectionAsync(targetSessionAttributes, TestContext.Current.CancellationToken);

        //Assert
        conn.Port.Should().Be(postmasters[expectedServer].Port);

        for (var i = 0; i <= expectedServer; i++)
            _ = await postmasters[i].WaitForServerConnection();
    }

    [Theory]
    [MemberData(nameof(MyCases))]
    public async Task connect_to_correct_host_legacy(TargetSessionAttributes targetSessionAttributes, MockState[] servers, int expectedServer)
    {
        //Arrange
        var postmasters = servers.Select(s => PgPostmasterMock.Start(state: s)).ToArray();
        await using var __ = new DisposableWrapper(postmasters);

        var connectionStringBuilder = new PgSqlConnectionStringBuilder
        {
            Host = MultipleHosts(postmasters),
            ServerCompatibilityMode = ServerCompatibilityMode.NoTypeLoading,
            TargetSessionAttributes = TargetSessionAttributesAsString(targetSessionAttributes)
        };

        using var pool = CreateTempPool(connectionStringBuilder, out var connectionString);
        await using var conn = new PgSqlConnection(connectionString);

        //Act
        await conn.OpenAsync(TestContext.Current.CancellationToken);

        //Assert
        conn.Port.Should().Be(postmasters[expectedServer].Port);

        for (var i = 0; i <= expectedServer; i++)
            _ = await postmasters[i].WaitForServerConnection();
    }

    [Theory]
    [MemberData(nameof(MyCases))]
    public async Task connect_to_correct_host_connection_string(TargetSessionAttributes targetSessionAttributes, MockState[] servers, int expectedServer)
    {
        //Arrange
        var postmasters = servers.Select(s => PgPostmasterMock.Start(state: s)).ToArray();
        await using var __ = new DisposableWrapper(postmasters);

        var connectionStringBuilder = new PgSqlConnectionStringBuilder
        {
            Host = MultipleHosts(postmasters),
            ServerCompatibilityMode = ServerCompatibilityMode.NoTypeLoading,
            TargetSessionAttributes = TargetSessionAttributesAsString(targetSessionAttributes)
        };

        await using var dataSource = new PgSqlDataSourceBuilder(connectionStringBuilder.ConnectionString)
            .Build();
        dataSource.Should().BeOfType<PgSqlMultiHostDataSource>();

        //Act
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        //Assert
        conn.Port.Should().Be(postmasters[expectedServer].Port);

        for (var i = 0; i <= expectedServer; i++)
            _ = await postmasters[i].WaitForServerConnection();
    }

    [Theory]
    [MemberData(nameof(MyCases))]
    public async Task connect_to_correct_host_with_available_idle(
        TargetSessionAttributes targetSessionAttributes, MockState[] servers, int expectedServer)
    {
        //Arrange
        var postmasters = servers.Select(s => PgPostmasterMock.Start(state: s)).ToArray();
        await using var __ = new DisposableWrapper(postmasters);

        // First, open and close a connection with the TargetSessionAttributes matching the first server.
        // This ensures wew have an idle connection in the pool.
        var connectionStringBuilder = new PgSqlConnectionStringBuilder
        {
            Host = MultipleHosts(postmasters),
            ServerCompatibilityMode = ServerCompatibilityMode.NoTypeLoading,
        };

        await using var dataSource = new PgSqlDataSourceBuilder(connectionStringBuilder.ConnectionString)
            .BuildMultiHost();
        var idleConnTargetSessionAttributes = servers[0] switch
        {
            Primary => TargetSessionAttributes.ReadWrite,
            PrimaryReadOnly => TargetSessionAttributes.ReadOnly,
            Standby => TargetSessionAttributes.Standby,
            _ => throw new ArgumentOutOfRangeException()
        };
        await using (_ = await dataSource.OpenConnectionAsync(idleConnTargetSessionAttributes, TestContext.Current.CancellationToken))
        {
            // Do nothing, close to have an idle connection in the pool.
        }

        //Act
        // Now connect with the test TargetSessionAttributes
        await using var conn = await dataSource.OpenConnectionAsync(targetSessionAttributes, TestContext.Current.CancellationToken);

        //Assert
        conn.Port.Should().Be(postmasters[expectedServer].Port);

        for (var i = 0; i <= expectedServer; i++)
            _ = await postmasters[i].WaitForServerConnection();
    }

    [Fact]
    public async Task legacy_connection_shares_datasource()
    {
        //Arrange
        await using var primaryPostmaster = PgPostmasterMock.Start(state: Primary);
        await using var standbyPostmaster = PgPostmasterMock.Start(state: Standby);

        var builder1 = new PgSqlConnectionStringBuilder
        {
            Host = MultipleHosts(primaryPostmaster, standbyPostmaster),
            ServerCompatibilityMode = ServerCompatibilityMode.NoTypeLoading,
            TargetSessionAttributes = "Prefer-Primary"
        };

        // Use the exact same pool for both connections as CreateTempPool adds a unique `ApplicationName` to connection string
        using var pool = CreateTempPool(builder1, out var connectionString1);
        var connectionString2 = new PgSqlConnectionStringBuilder(connectionString1)
        {
            TargetSessionAttributes = "Prefer-Standby"
        }.ConnectionString;

        //Act
        await using var conn1 = new PgSqlConnection(connectionString1);
        await conn1.OpenAsync(TestContext.Current.CancellationToken);
        conn1.Port.Should().Be(primaryPostmaster.Port);

        await using var conn2 = new PgSqlConnection(connectionString2);
        await conn2.OpenAsync(TestContext.Current.CancellationToken);
        conn2.Port.Should().Be(standbyPostmaster.Port);

        //Assert
        conn1.PgSqlDataSource.Should().NotBeSameAs(conn2.PgSqlDataSource);
        conn1.PgSqlDataSource.Should().BeOfType<MultiHostDataSourceWrapper>();
        conn2.PgSqlDataSource.Should().BeOfType<MultiHostDataSourceWrapper>();
        ((MultiHostDataSourceWrapper)conn1.PgSqlDataSource).WrappedSource.Should().BeSameAs(((MultiHostDataSourceWrapper)conn2.PgSqlDataSource).WrappedSource);
    }

    [Theory]
    [InlineData(TargetSessionAttributes.Standby,   new[] { Primary,         Primary })]
    [InlineData(TargetSessionAttributes.Primary,   new[] { Standby,         Standby })]
    [InlineData(TargetSessionAttributes.ReadWrite, new[] { PrimaryReadOnly, Standby })]
    [InlineData(TargetSessionAttributes.ReadOnly,  new[] { Primary,         Primary })]
    public async Task valid_host_not_found(TargetSessionAttributes targetSessionAttributes, MockState[] servers)
    {
        //Arrange
        var postmasters = servers.Select(s => PgPostmasterMock.Start(state: s)).ToArray();
        await using var __ = new DisposableWrapper(postmasters);

        var connectionStringBuilder = new PgSqlConnectionStringBuilder
        {
            Host = MultipleHosts(postmasters),
            ServerCompatibilityMode = ServerCompatibilityMode.NoTypeLoading,
        };

        await using var dataSource = new PgSqlDataSourceBuilder(connectionStringBuilder.ConnectionString)
            .BuildMultiHost();

        //Act
        var exception = await Assert.ThrowsAsync<PgSqlException>(async () => await dataSource.OpenConnectionAsync(targetSessionAttributes, TestContext.Current.CancellationToken));

        //Assert
        exception.Message.Should().Be("No suitable host was found.");
        exception.InnerException.Should().BeNull();

        for (var i = 0; i < servers.Length; i++)
            _ = await postmasters[i].WaitForServerConnection();
    }

    [Fact]
    public async Task all_hosts_are_down()
    {
        if (OperatingSystem.IsMacOS())
            Assert.Skip("Excluded on macOS, see #3786");

        //Arrange
        var endpoint = new IPEndPoint(IPAddress.Loopback, 0);

        using var socket1 = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket1.Bind(endpoint);
        var localEndPoint1 = (IPEndPoint)socket1.LocalEndPoint;

        using var socket2 = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket2.Bind(endpoint);
        var localEndPoint2 = (IPEndPoint)socket2.LocalEndPoint;

        // Note that we Bind (to reserve the port), but do not Listen - connection attempts will fail.

        var connectionString = new PgSqlConnectionStringBuilder
        {
            Host = $"{localEndPoint1.Address}:{localEndPoint1.Port},{localEndPoint2.Address}:{localEndPoint2.Port}"
        }.ConnectionString;
        using var dataSource = new PgSqlDataSourceBuilder(connectionString).BuildMultiHost();

        //Act
        var exception = await Assert.ThrowsAsync<PgSqlException>(async () => await dataSource.OpenConnectionAsync(TargetSessionAttributes.Any, TestContext.Current.CancellationToken));

        //Assert
        var aggregateException = (AggregateException)exception.InnerException;
        aggregateException.InnerExceptions.Should().HaveCount(2);

        for (var i = 0; i < aggregateException.InnerExceptions.Count; i++)
        {
            aggregateException.InnerExceptions[i].Should().BeOfType<PgSqlException>();
            aggregateException.InnerExceptions[i].InnerException.Should().BeOfType<SocketException>()
                .Which.SocketErrorCode.Should().Be(SocketError.ConnectionRefused);
        }
    }

    [Theory]
    [InlineData(true, PostgresErrorCodes.InvalidCatalogName)]
    [InlineData(true, PostgresErrorCodes.CannotConnectNow)]
    [InlineData(false, PostgresErrorCodes.InvalidCatalogName)]
    [InlineData(false, PostgresErrorCodes.CannotConnectNow)]
    public async Task all_hosts_are_unavailable(bool pooling, string errorCode)
    {
        //Arrange
        await using var primaryPostmaster = PgPostmasterMock.Start(state: Primary, startupErrorCode: errorCode);
        await using var standbyPostmaster = PgPostmasterMock.Start(state: Standby, startupErrorCode: errorCode);

        var builder = new PgSqlConnectionStringBuilder
        {
            Host = MultipleHosts(primaryPostmaster, standbyPostmaster),
            ServerCompatibilityMode = ServerCompatibilityMode.NoTypeLoading,
            Pooling = pooling,
        };

        await using var dataSource = new PgSqlDataSourceBuilder(builder.ConnectionString).BuildMultiHost();

        //Act
        var ex = await Assert.ThrowsAsync<PostgresException>(async () => await dataSource.OpenConnectionAsync(TargetSessionAttributes.Any, TestContext.Current.CancellationToken));

        //Assert
        ex.SqlState.Should().Be(errorCode);
    }

    [Fact]
    public async Task first_host_is_down()
    {
        //Arrange
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        var endpoint = new IPEndPoint(IPAddress.Loopback, 0);
        socket.Bind(endpoint);
        var localEndPoint = (IPEndPoint)socket.LocalEndPoint;
        // Note that we Bind (to reserve the port), but do not Listen - connection attempts will fail.

        await using var postmaster = PgPostmasterMock.Start(state: Primary);

        var connectionString = new PgSqlConnectionStringBuilder
        {
            Host = $"{localEndPoint.Address}:{localEndPoint.Port},{postmaster.Host}:{postmaster.Port}",
            ServerCompatibilityMode = ServerCompatibilityMode.NoTypeLoading
        }.ConnectionString;

        await using var dataSource = new PgSqlDataSourceBuilder(connectionString).BuildMultiHost();

        //Act
        await using var conn = await dataSource.OpenConnectionAsync(TargetSessionAttributes.Any, TestContext.Current.CancellationToken);

        //Assert
        conn.Port.Should().Be(postmaster.Port);
    }

    [Theory]
    [InlineData("any")]
    [InlineData("primary")]
    [InlineData("standby")]
    [InlineData("prefer-primary")]
    [InlineData("prefer-standby")]
    [InlineData("read-write")]
    [InlineData("read-only")]
    public async Task TargetSessionAttributes_with_single_host(string targetSessionAttributes)
    {
        //Arrange
        var connectionString = new PgSqlConnectionStringBuilder(ConnectionString)
        {
            TargetSessionAttributes = targetSessionAttributes
        }.ConnectionString;

        //Act
        if (targetSessionAttributes == "any")
        {
            await using var postmasterMock = PgPostmasterMock.Start(connectionString);
            using var pool = CreateTempPool(postmasterMock.ConnectionString, out connectionString);
            await using var conn = new PgSqlConnection(connectionString);
            await conn.OpenAsync(TestContext.Current.CancellationToken);
            _ = await postmasterMock.WaitForServerConnection();
        }
        else
        {
            Assert.Throws<NotSupportedException>(() => new PgSqlConnection(connectionString));
        }
    }

    [Fact]
    public void TargetSessionAttributes_default_is_null()
        => new PgSqlConnectionStringBuilder().TargetSessionAttributes.Should().BeNull();

    [Fact]
    public void TargetSessionAttributes_invalid_throws()
        => Assert.Throws<ArgumentException>(() =>
            new PgSqlConnectionStringBuilder
            {
                TargetSessionAttributes = nameof(TargetSessionAttributes_invalid_throws)
            });

    [Fact]
    public void HostRecheckSeconds_default_value()
    {
        //Act
        var builder = new PgSqlConnectionStringBuilder();

        //Assert
        builder.HostRecheckSeconds.Should().Be(10);
        builder.HostRecheckSecondsTranslated.Should().Be(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void HostRecheckSeconds_zero_value()
    {
        //Act
        var builder = new PgSqlConnectionStringBuilder
        {
            HostRecheckSeconds = 0,
        };

        //Assert
        builder.HostRecheckSeconds.Should().Be(0);
        builder.HostRecheckSecondsTranslated.Should().Be(TimeSpan.FromSeconds(-1));
    }

    [Fact]
    public void HostRecheckSeconds_invalid_throws()
        => Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PgSqlConnectionStringBuilder
            {
                HostRecheckSeconds = -1
            });

    [Fact]
    public async Task connect_with_load_balancing()
    {
        //Arrange
        await using var primaryPostmaster = PgPostmasterMock.Start(state: Primary);
        await using var standbyPostmaster = PgPostmasterMock.Start(state: Standby);

        var defaultCsb = new PgSqlConnectionStringBuilder
        {
            Host = MultipleHosts(primaryPostmaster, standbyPostmaster),
            ServerCompatibilityMode = ServerCompatibilityMode.NoTypeLoading,
            MaxPoolSize = 1,
            LoadBalanceHosts = true,
        };

        await using var dataSource = new PgSqlDataSourceBuilder(defaultCsb.ConnectionString)
            .BuildMultiHost();

        PgSqlConnector firstConnector;
        PgSqlConnector secondConnector;

        //Act
        await using (var firstConnection = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken))
        {
            firstConnector = firstConnection.Connector;
        }

        await using (var secondConnection = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken))
        {
            secondConnector = secondConnection.Connector;
        }

        //Assert
        secondConnector.Should().NotBeSameAs(firstConnector);

        await using (var firstBalancedConnection = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken))
        {
            firstBalancedConnection.Connector.Should().BeSameAs(firstConnector);
        }

        await using (var secondBalancedConnection = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken))
        {
            secondBalancedConnection.Connector.Should().BeSameAs(secondConnector);
        }

        await using (var thirdBalancedConnection = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken))
        {
            thirdBalancedConnection.Connector.Should().BeSameAs(firstConnector);
        }
    }

    [Fact]
    public async Task connect_without_load_balancing()
    {
        //Arrange
        await using var primaryPostmaster = PgPostmasterMock.Start(state: Primary);
        await using var standbyPostmaster = PgPostmasterMock.Start(state: Standby);

        var defaultCsb = new PgSqlConnectionStringBuilder
        {
            Host = MultipleHosts(primaryPostmaster, standbyPostmaster),
            ServerCompatibilityMode = ServerCompatibilityMode.NoTypeLoading,
            MaxPoolSize = 1,
            LoadBalanceHosts = false,
        };

        await using var dataSource = new PgSqlDataSourceBuilder(defaultCsb.ConnectionString)
            .BuildMultiHost();

        PgSqlConnector firstConnector;
        PgSqlConnector secondConnector;

        //Act
        await using (var firstConnection = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken))
        {
            firstConnector = firstConnection.Connector;
        }
        await using (var secondConnection = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken))
        {
            secondConnection.Connector.Should().BeSameAs(firstConnector);
        }
        await using (var firstConnection = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken))
        await using (var secondConnection = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken))
        {
            secondConnector = secondConnection.Connector;
        }

        secondConnector.Should().NotBeSameAs(firstConnector);

        await using (var firstUnbalancedConnection = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken))
        {
            firstUnbalancedConnection.Connector.Should().BeSameAs(firstConnector);
        }

        await using (var secondUnbalancedConnection = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken))
        {
            secondUnbalancedConnection.Connector.Should().BeSameAs(firstConnector);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task connect_state_changing_hosts(bool alwaysCheckHostState)
    {
        //Arrange
        await using var primaryPostmaster = PgPostmasterMock.Start(state: Primary);
        await using var standbyPostmaster = PgPostmasterMock.Start(state: Standby);

        var defaultCsb = new PgSqlConnectionStringBuilder
        {
            Host = MultipleHosts(primaryPostmaster, standbyPostmaster),
            ServerCompatibilityMode = ServerCompatibilityMode.NoTypeLoading,
            MaxPoolSize = 1,
            HostRecheckSeconds = alwaysCheckHostState ? 0 : int.MaxValue,
            NoResetOnClose = true,
        };

        await using var dataSource = new PgSqlDataSourceBuilder(defaultCsb.ConnectionString)
            .BuildMultiHost();

        PgSqlConnector firstConnector;
        PgSqlConnector secondConnector;
        var firstServerTask = Task.Run(async () =>
        {
            var server = await primaryPostmaster.WaitForServerConnection();
            if (!alwaysCheckHostState)
                return;

            // If we always check the host, we will send the request for the state
            // even though we got one while opening the connection
            await server.SendMockState(Primary);

            // Update the state after a 'failover'
            await server.SendMockState(Standby);
        }, TestContext.Current.CancellationToken);
        var secondServerTask = Task.Run(async () =>
        {
            var server = await standbyPostmaster.WaitForServerConnection();
            if (!alwaysCheckHostState)
                return;

            // If we always check the host, we will send the request for the state
            // even though we got one while opening the connection
            await server.SendMockState(Standby);

            // As TargetSessionAttributes is 'prefer', it does another cycle for the 'unpreferred'
            await server.SendMockState(Standby);
            // Update the state after a 'failover'
            await server.SendMockState(Primary);
        }, TestContext.Current.CancellationToken);

        //Act
        await using (var firstConnection = await dataSource.OpenConnectionAsync(TargetSessionAttributes.PreferPrimary, TestContext.Current.CancellationToken))
        await using (var secondConnection = await dataSource.OpenConnectionAsync(TargetSessionAttributes.PreferPrimary, TestContext.Current.CancellationToken))
        {
            firstConnector = firstConnection.Connector;
            secondConnector = secondConnection.Connector;
        }

        await using var thirdConnection = await dataSource.OpenConnectionAsync(TargetSessionAttributes.PreferPrimary, TestContext.Current.CancellationToken);

        //Assert
        thirdConnection.Connector.Should().BeSameAs(alwaysCheckHostState ? secondConnector : firstConnector);

        await firstServerTask;
        await secondServerTask;
    }

    [Fact]
    public void database_state_cache_basic()
    {
        //Arrange
        using var dataSource = CreateDataSource();
        var timeStamp = DateTime.UtcNow;

        //Act
        dataSource.UpdateDatabaseState(DatabaseState.PrimaryReadWrite, timeStamp, TimeSpan.Zero);
        dataSource.GetDatabaseState().Should().Be(DatabaseState.PrimaryReadWrite);

        // Update with the same timestamp - shouldn't change anything
        dataSource.UpdateDatabaseState(DatabaseState.Standby, timeStamp, TimeSpan.Zero);
        dataSource.GetDatabaseState().Should().Be(DatabaseState.PrimaryReadWrite);

        // Update with a new timestamp
        timeStamp = timeStamp.AddSeconds(1);
        dataSource.UpdateDatabaseState(DatabaseState.PrimaryReadOnly, timeStamp, TimeSpan.Zero);
        dataSource.GetDatabaseState().Should().Be(DatabaseState.PrimaryReadOnly);

        // Expired state returns as Unknown (depending on ignoreExpiration)
        timeStamp = timeStamp.AddSeconds(1);
        dataSource.UpdateDatabaseState(DatabaseState.PrimaryReadWrite, timeStamp, TimeSpan.FromSeconds(-1));
        dataSource.GetDatabaseState(ignoreExpiration: false).Should().Be(DatabaseState.Unknown);
        dataSource.GetDatabaseState(ignoreExpiration: true).Should().Be(DatabaseState.PrimaryReadWrite);
    }

    [Fact]
    public async Task offline_state_on_connection_failure()
    {
        //Arrange
        await using var server = PgPostmasterMock.Start(ConnectionString, startupErrorCode: PostgresErrorCodes.ConnectionFailure);
        await using var dataSource = server.CreateDataSource();
        await using var conn = dataSource.CreateConnection();

        //Act
        var ex = await Assert.ThrowsAsync<PostgresException>(() => conn.OpenAsync(TestContext.Current.CancellationToken));

        //Assert
        ex.SqlState.Should().Be(PostgresErrorCodes.ConnectionFailure);

        var state = conn.PgSqlDataSource.GetDatabaseState();
        state.Should().Be(DatabaseState.Offline);
    }

    [Fact]
    public async Task unknown_state_on_connection_authentication_failure()
    {
        //Arrange
        await using var server = PgPostmasterMock.Start(ConnectionString, startupErrorCode: PostgresErrorCodes.InvalidAuthorizationSpecification);
        await using var dataSource = server.CreateDataSource();
        await using var conn = dataSource.CreateConnection();

        //Act
        var ex = await Assert.ThrowsAsync<PostgresException>(() => conn.OpenAsync(TestContext.Current.CancellationToken));

        //Assert
        ex.SqlState.Should().Be(PostgresErrorCodes.InvalidAuthorizationSpecification);

        var state = conn.PgSqlDataSource.GetDatabaseState();
        state.Should().Be(DatabaseState.Unknown);
    }

    [Fact]
    public async Task offline_state_on_query_execution_pg_critical_failure()
    {
        //Arrange
        await using var postmaster = PgPostmasterMock.Start(ConnectionString);
        await using var dataSource = postmaster.CreateDataSource();
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using var anotherConn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await anotherConn.CloseAsync();

        var state = conn.PgSqlDataSource.GetDatabaseState();
        state.Should().Be(DatabaseState.Unknown);
        conn.PgSqlDataSource.Statistics.Total.Should().Be(2);

        var server = await postmaster.WaitForServerConnection();
        await server.WriteErrorResponse(PostgresErrorCodes.CrashShutdown).FlushAsync();

        //Act
        var ex = await Assert.ThrowsAsync<PostgresException>(() => conn.ExecuteNonQueryAsync("SELECT 1", cancellationToken: TestContext.Current.CancellationToken));

        //Assert
        ex.SqlState.Should().Be(PostgresErrorCodes.CrashShutdown);
        conn.State.Should().Be(ConnectionState.Closed);

        state = conn.PgSqlDataSource.GetDatabaseState();
        state.Should().Be(DatabaseState.Offline);
        conn.PgSqlDataSource.Statistics.Total.Should().Be(0);
    }

    [Fact]
    public async Task offline_state_on_query_execution_io_exception()
    {
        //Arrange
        await using var postmaster = PgPostmasterMock.Start(ConnectionString);
        await using var dataSource = postmaster.CreateDataSource();
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using var anotherConn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await anotherConn.CloseAsync();

        var state = conn.PgSqlDataSource.GetDatabaseState();
        state.Should().Be(DatabaseState.Unknown);
        conn.PgSqlDataSource.Statistics.Total.Should().Be(2);

        var server = await postmaster.WaitForServerConnection();
        server.Close();

        //Act
        var ex = await Assert.ThrowsAsync<PgSqlException>(() => conn.ExecuteNonQueryAsync("SELECT 1", cancellationToken: TestContext.Current.CancellationToken));

        //Assert
        ex.InnerException.Should().BeAssignableTo<IOException>();
        conn.State.Should().Be(ConnectionState.Closed);

        state = conn.PgSqlDataSource.GetDatabaseState();
        state.Should().Be(DatabaseState.Offline);
        conn.PgSqlDataSource.Statistics.Total.Should().Be(0);
    }

    [Fact]
    public async Task offline_state_on_query_execution_timeout_exception()
    {
        //Arrange
        await using var postmaster = PgPostmasterMock.Start(ConnectionString);
        await using var dataSource = postmaster.CreateDataSource(builder =>
        {
            builder.ConnectionStringBuilder.CommandTimeout = 1;
            builder.ConnectionStringBuilder.CancellationTimeout = 1;
        });

        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using var anotherConn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await anotherConn.CloseAsync();

        var state = conn.PgSqlDataSource.GetDatabaseState();
        state.Should().Be(DatabaseState.Unknown);
        conn.PgSqlDataSource.Statistics.Total.Should().Be(2);

        //Act
        var ex = await Assert.ThrowsAsync<PgSqlException>(() => conn.ExecuteNonQueryAsync("SELECT 1", cancellationToken: TestContext.Current.CancellationToken));

        //Assert
        ex.InnerException.Should().BeOfType<TimeoutException>();
        conn.State.Should().Be(ConnectionState.Closed);

        state = conn.PgSqlDataSource.GetDatabaseState();
        state.Should().Be(DatabaseState.Offline);
        conn.PgSqlDataSource.Statistics.Total.Should().Be(0);
    }

    [Fact]
    public async Task unknown_state_on_query_execution_timeout_exception_with_disabled_cancellation()
    {
        //Arrange
        await using var postmaster = PgPostmasterMock.Start(ConnectionString);
        await using var dataSource = postmaster.CreateDataSource(builder =>
        {
            builder.ConnectionStringBuilder.CommandTimeout = 1;
            builder.ConnectionStringBuilder.CancellationTimeout = -1;
        });

        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using var anotherConn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await anotherConn.CloseAsync();

        var state = conn.PgSqlDataSource.GetDatabaseState();
        state.Should().Be(DatabaseState.Unknown);
        conn.PgSqlDataSource.Statistics.Total.Should().Be(2);

        //Act
        var ex = await Assert.ThrowsAsync<PgSqlException>(() => conn.ExecuteNonQueryAsync("SELECT 1", cancellationToken: TestContext.Current.CancellationToken));

        //Assert
        ex.InnerException.Should().BeOfType<TimeoutException>();
        conn.State.Should().Be(ConnectionState.Closed);

        state = conn.PgSqlDataSource.GetDatabaseState();
        state.Should().Be(DatabaseState.Unknown);
        conn.PgSqlDataSource.Statistics.Total.Should().Be(1);
    }

    [Fact]
    public async Task unknown_state_on_query_execution_cancellation_with_disabled_cancellation_timeout()
    {
        //Arrange
        await using var postmaster = PgPostmasterMock.Start(ConnectionString);
        await using var dataSource = postmaster.CreateDataSource(builder =>
        {
            builder.ConnectionStringBuilder.CommandTimeout = 30;
            builder.ConnectionStringBuilder.CancellationTimeout = -1;
        });

        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using var anotherConn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await anotherConn.CloseAsync();

        var state = conn.PgSqlDataSource.GetDatabaseState();
        state.Should().Be(DatabaseState.Unknown);
        conn.PgSqlDataSource.Statistics.Total.Should().Be(2);

        using var cts = new CancellationTokenSource();

        //Act
        var query = conn.ExecuteNonQueryAsync("SELECT 1", cancellationToken: cts.Token);
        cts.Cancel();
        var ex = await Assert.ThrowsAsync<OperationCanceledException>(async () => await query);

        //Assert
        ex.InnerException.Should().BeOfType<TimeoutException>();
        conn.State.Should().Be(ConnectionState.Closed);

        state = conn.PgSqlDataSource.GetDatabaseState();
        state.Should().Be(DatabaseState.Unknown);
        conn.PgSqlDataSource.Statistics.Total.Should().Be(1);
    }

    [Fact]
    public async Task unknown_state_on_query_execution_timeout_exception_with_cancellation_failure()
    {
        //Arrange
        await using var postmaster = PgPostmasterMock.Start(ConnectionString);
        await using var dataSource = postmaster.CreateDataSource(builder =>
        {
            builder.ConnectionStringBuilder.CommandTimeout = 1;
            builder.ConnectionStringBuilder.CancellationTimeout = 0;
        });

        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        var state = conn.PgSqlDataSource.GetDatabaseState();
        state.Should().Be(DatabaseState.Unknown);
        conn.PgSqlDataSource.Statistics.Total.Should().Be(1);

        var server = await postmaster.WaitForServerConnection();

        //Act
        var query = conn.ExecuteNonQueryAsync("SELECT 1", cancellationToken: TestContext.Current.CancellationToken);

        await postmaster.WaitForCancellationRequest();
        await server.WriteCancellationResponse().WriteReadyForQuery().FlushAsync();

        var ex = await Assert.ThrowsAsync<PgSqlException>(async () => await query);

        //Assert
        ex.InnerException.Should().BeOfType<TimeoutException>();
        conn.State.Should().Be(ConnectionState.Open);

        state = conn.PgSqlDataSource.GetDatabaseState();
        state.Should().Be(DatabaseState.Unknown);
        conn.PgSqlDataSource.Statistics.Total.Should().Be(1);
    }

    [Fact]
    public async Task clear_pool_one_host_only_on_admin_shutdown()
    {
        //Arrange
        await using var primaryPostmaster = PgPostmasterMock.Start(ConnectionString, state: Primary);
        await using var standbyPostmaster = PgPostmasterMock.Start(ConnectionString, state: Standby);
        var dataSourceBuilder = new PgSqlDataSourceBuilder
        {
            ConnectionStringBuilder =
            {
                Host = MultipleHosts(primaryPostmaster, standbyPostmaster),
                ServerCompatibilityMode = ServerCompatibilityMode.NoTypeLoading,
                MaxPoolSize = 2
            }
        };
        await using var multiHostDataSource = dataSourceBuilder.BuildMultiHost();
        await using var preferPrimaryDataSource = multiHostDataSource.WithTargetSession(TargetSessionAttributes.PreferPrimary);

        await using var primaryConn = await preferPrimaryDataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using var anotherPrimaryConn = await preferPrimaryDataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using var standbyConn = await preferPrimaryDataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        var primaryDataSource = primaryConn.Connector.DataSource;
        var standbyDataSource = standbyConn.Connector.DataSource;
        await anotherPrimaryConn.CloseAsync();
        await standbyConn.CloseAsync();

        primaryDataSource.GetDatabaseState().Should().Be(DatabaseState.PrimaryReadWrite);
        standbyDataSource.GetDatabaseState().Should().Be(DatabaseState.Standby);
        primaryConn.PgSqlDataSource.Statistics.Total.Should().Be(3);

        var server = await primaryPostmaster.WaitForServerConnection();
        await server.WriteErrorResponse(PostgresErrorCodes.AdminShutdown).FlushAsync();

        //Act
        var ex = await Assert.ThrowsAsync<PostgresException>(() => primaryConn.ExecuteNonQueryAsync("SELECT 1", cancellationToken: TestContext.Current.CancellationToken));

        //Assert
        ex.SqlState.Should().Be(PostgresErrorCodes.AdminShutdown);
        primaryConn.State.Should().Be(ConnectionState.Closed);

        primaryDataSource.GetDatabaseState().Should().Be(DatabaseState.Offline);
        standbyDataSource.GetDatabaseState().Should().Be(DatabaseState.Standby);
        primaryConn.PgSqlDataSource.Statistics.Total.Should().Be(1);

        multiHostDataSource.ClearDatabaseStates();
        primaryDataSource.GetDatabaseState().Should().Be(DatabaseState.Unknown);
        standbyDataSource.GetDatabaseState().Should().Be(DatabaseState.Unknown);
    }

    [Theory]
    [InlineData("any", true)]
    [InlineData("primary", true)]
    [InlineData("standby", false)]
    [InlineData("prefer-primary", true)]
    [InlineData("prefer-standby", false)]
    [InlineData("read-write", true)]
    [InlineData("read-only", false)]
    public async Task transaction_enlist_reuses_connection(string targetSessionAttributes, bool primary)
    {
        //Arrange
        await using var primaryPostmaster = PgPostmasterMock.Start(ConnectionString, state: Primary);
        await using var standbyPostmaster = PgPostmasterMock.Start(ConnectionString, state: Standby);
        var csb = new PgSqlConnectionStringBuilder
        {
            Host = MultipleHosts(primaryPostmaster, standbyPostmaster),
            TargetSessionAttributes = targetSessionAttributes,
            ServerCompatibilityMode = ServerCompatibilityMode.NoTypeLoading,
            MaxPoolSize = 10,
        };

        using var _ = CreateTempPool(csb, out var connString);

        using var scope = new TransactionScope(TransactionScopeOption.Required,
            new TransactionOptions { IsolationLevel = IsolationLevel.ReadCommitted }, TransactionScopeAsyncFlowOption.Enabled);

        //Act
        var query1Task = Query(connString);

        var server = primary
            ? await primaryPostmaster.WaitForServerConnection()
            : await standbyPostmaster.WaitForServerConnection();

        await server
            .WriteCommandComplete()
            .WriteReadyForQuery(TransactionStatus.InTransactionBlock)
            .WriteParseComplete()
            .WriteBindComplete()
            .WriteNoData()
            .WriteCommandComplete()
            .WriteReadyForQuery(TransactionStatus.InTransactionBlock)
            .FlushAsync();
        await query1Task;

        var query2Task = Query(connString);
        await server
            .WriteParseComplete()
            .WriteBindComplete()
            .WriteNoData()
            .WriteCommandComplete()
            .WriteReadyForQuery(TransactionStatus.InTransactionBlock)
            .FlushAsync();
        await query2Task;

        await server
            .WriteCommandComplete()
            .WriteReadyForQuery()
            .FlushAsync();
        scope.Complete();

        async Task Query(string connectionString)
        {
            await using var conn = new PgSqlConnection(connectionString);
            await conn.OpenAsync(TestContext.Current.CancellationToken);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1";
            await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task primary_host_failover_can_connect()
    {
        //Arrange
        await using var firstPostmaster = PgPostmasterMock.Start(ConnectionString, state: Primary);
        await using var secondPostmaster = PgPostmasterMock.Start(ConnectionString, state: Standby);
        var dataSourceBuilder = new PgSqlDataSourceBuilder
        {
            ConnectionStringBuilder =
            {
                Host = MultipleHosts(firstPostmaster, secondPostmaster),
                ServerCompatibilityMode = ServerCompatibilityMode.NoTypeLoading,
                HostRecheckSeconds = 5
            }
        };
        await using var multiHostDataSource = dataSourceBuilder.BuildMultiHost();
        var (firstDataSource, secondDataSource) = (multiHostDataSource.Pools[0], multiHostDataSource.Pools[1]);
        await using var primaryDataSource = multiHostDataSource.WithTargetSession(TargetSessionAttributes.Primary);

        await using var conn = await primaryDataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        conn.Port.Should().Be(firstPostmaster.Port);
        var firstServer = await firstPostmaster.WaitForServerConnection();
        await firstServer
            .WriteErrorResponse(PostgresErrorCodes.AdminShutdown)
            .FlushAsync();

        //Act
        var failoverEx = await Assert.ThrowsAsync<PostgresException>(async () => await conn.ExecuteNonQueryAsync("SELECT 1", cancellationToken: TestContext.Current.CancellationToken));
        failoverEx.SqlState.Should().Be(PostgresErrorCodes.AdminShutdown);

        var noHostFoundEx = await Assert.ThrowsAsync<PgSqlException>(async () => await conn.OpenAsync(TestContext.Current.CancellationToken));
        noHostFoundEx.Message.Should().Be("No suitable host was found.");

        firstDataSource.GetDatabaseState().Should().Be(DatabaseState.Offline);
        secondDataSource.GetDatabaseState().Should().Be(DatabaseState.Standby);

        firstPostmaster.State = Standby;
        secondPostmaster.State = Primary;
        var secondServer = await secondPostmaster.WaitForServerConnection();
        await secondServer.SendMockState(Primary);

        await Task.Delay(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        firstDataSource.GetDatabaseState().Should().Be(DatabaseState.Unknown);
        secondDataSource.GetDatabaseState().Should().Be(DatabaseState.Unknown);

        await conn.OpenAsync(TestContext.Current.CancellationToken);
        conn.Port.Should().Be(secondPostmaster.Port);
        firstDataSource.GetDatabaseState().Should().Be(DatabaseState.Standby);
        secondDataSource.GetDatabaseState().Should().Be(DatabaseState.PrimaryReadWrite);
    }

    [Fact]
    public async Task data_source_with_wrappers()
    {
        //Arrange
        await using var primaryPostmasterMock = PgPostmasterMock.Start(state: Primary);
        await using var standbyPostmasterMock = PgPostmasterMock.Start(state: Standby);

        var builder = new PgSqlDataSourceBuilder
        {
            ConnectionStringBuilder =
            {
                Host = MultipleHosts(primaryPostmasterMock, standbyPostmasterMock),
                ServerCompatibilityMode = ServerCompatibilityMode.NoTypeLoading,
            }
        };

        await using var dataSource = builder.BuildMultiHost();
        await using var primaryDataSource = dataSource.WithTargetSession(TargetSessionAttributes.Primary);
        await using var standbyDataSource = dataSource.WithTargetSession(TargetSessionAttributes.Standby);

        //Act
        await using var primaryConnection = await primaryDataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        primaryConnection.Port.Should().Be(primaryPostmasterMock.Port);

        await using var standbyConnection = await standbyDataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        standbyConnection.Port.Should().Be(standbyPostmasterMock.Port);
    }

    [Fact]
    public async Task data_source_without_wrappers()
    {
        //Arrange
        await using var primaryPostmasterMock = PgPostmasterMock.Start(state: Primary);
        await using var standbyPostmasterMock = PgPostmasterMock.Start(state: Standby);

        var builder = new PgSqlDataSourceBuilder
        {
            ConnectionStringBuilder =
            {
                Host = MultipleHosts(primaryPostmasterMock, standbyPostmasterMock),
                ServerCompatibilityMode = ServerCompatibilityMode.NoTypeLoading,
            }
        };

        await using var dataSource = builder.BuildMultiHost();

        //Act
        await using var primaryConnection = await dataSource.OpenConnectionAsync(TargetSessionAttributes.Primary, TestContext.Current.CancellationToken);
        primaryConnection.Port.Should().Be(primaryPostmasterMock.Port);

        await using var standbyConnection = await dataSource.OpenConnectionAsync(TargetSessionAttributes.Standby, TestContext.Current.CancellationToken);
        standbyConnection.Port.Should().Be(standbyPostmasterMock.Port);
    }

    [Fact]
    public async Task BuildMultiHost_with_single_host_is_supported()
    {
        //Arrange
        var builder = new PgSqlDataSourceBuilder(ConnectionString);

        //Act
        await using var dataSource = builder.BuildMultiHost();
        await using var connection = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        //Assert
        (await connection.ExecuteScalarAsync("SELECT 1", cancellationToken: TestContext.Current.CancellationToken)).Should().Be(1);
    }

    [Fact]
    public async Task Build_with_multiple_hosts_is_supported()
    {
        //Arrange
        await using var primaryPostmasterMock = PgPostmasterMock.Start(state: Primary);
        await using var standbyPostmasterMock = PgPostmasterMock.Start(state: Standby);

        var builder = new PgSqlDataSourceBuilder
        {
            ConnectionStringBuilder =
            {
                Host = MultipleHosts(primaryPostmasterMock, standbyPostmasterMock),
                ServerCompatibilityMode = ServerCompatibilityMode.NoTypeLoading,
            }
        };

        //Act
        await using var dataSource = builder.Build();
        await using var connection = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task OpenConnection_when_canceled_throws_TaskCanceledException()
    {
        //Arrange
        var builder = new PgSqlDataSourceBuilder(ConnectionString);
        await using var dataSource = builder.BuildMultiHost();
        var cancellationToken = new CancellationToken(true);

        //Act
        var ex = await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        });

        //Assert
        ex.CancellationToken.Should().Be(cancellationToken);
    }

    [Fact]
    public async Task OpenConnection_when_canceled_during_TryGet_throws_OperationCanceledException()
    {
        //Arrange
        await using var primary1 = PgPostmasterMock.Start(state: Primary);
        await using var primary2 = PgPostmasterMock.Start(state: Primary);

        var connectionString = new PgSqlConnectionStringBuilder($"Host={primary1.Host}:{primary1.Port},{primary2.Host}:{primary2.Port}")
            {
                ServerCompatibilityMode = ServerCompatibilityMode.NoTypeLoading,
                MaxPoolSize = 1
            }.ToString();
        await using var dataSource = new PgSqlDataSourceBuilder(connectionString).BuildMultiHost();

        // Exhaust the pool so that TryGetIdleOrNew returns null and we fall through to TryGet
        await using var conn1 = await dataSource.OpenConnectionAsync(TargetSessionAttributes.Primary, TestContext.Current.CancellationToken);
        await using var conn2 = await dataSource.OpenConnectionAsync(TargetSessionAttributes.Primary, TestContext.Current.CancellationToken);

        var cancellationToken = new CancellationToken(true);

        //Act
        var ex = await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await using var conn3 = await dataSource.OpenConnectionAsync(TargetSessionAttributes.Primary, cancellationToken);
        });

        //Assert
        ex.CancellationToken.Should().Be(cancellationToken);
    }

    // Fails until #4181 is fixed.
    [Theory(Explicit = true)]
    [IssueLink("https://github.com/npgsql/npgsql/issues/4181")]
    [MemberData(nameof(AllTargetSessionAttributes))]
    public async Task load_balancing_is_fair_if_first_host_is_down(TargetSessionAttributes targetSessionAttributes)
    {
        //Arrange
        await using var pDown = PgPostmasterMock.Start(state: Primary, startupErrorCode: PostgresErrorCodes.CannotConnectNow);
        await using var pRw1 = PgPostmasterMock.Start(state: Primary);
        await using var pR1 = PgPostmasterMock.Start(state: PrimaryReadOnly);
        await using var s1 = PgPostmasterMock.Start(state: Standby);
        await using var pRw2 = PgPostmasterMock.Start(state: Primary);
        await using var pR2 = PgPostmasterMock.Start(state: PrimaryReadOnly);
        await using var s2 = PgPostmasterMock.Start(state: Standby);

        var hostList = $"{pDown.Host}:{pDown.Port}," +
                       $"{pRw1.Host}:{pRw1.Port}," +
                       $"{pR1.Host}:{pR1.Port}," +
                       $"{s1.Host}:{s1.Port}," +
                       $"{pRw2.Host}:{pRw2.Port}," +
                       $"{pR2.Host}:{pR2.Port}," +
                       $"{s2.Host}:{s2.Port}";

        await using var dataSource = CreateDataSource(builder =>
        {
            builder.Host = hostList;
            builder.ServerCompatibilityMode = ServerCompatibilityMode.NoTypeLoading;
            builder.LoadBalanceHosts = true;
            builder.TargetSessionAttributesParsed = targetSessionAttributes;

        });

        //Act
        var connections = Enumerable.Repeat(0, 12).Select(_ => dataSource.OpenConnection()).ToArray();
        await using var __ = new DisposableWrapper(connections);

        //Assert
        switch (targetSessionAttributes)
        {
        case TargetSessionAttributes.Any:
            connections[0].Port.Should().Be(pRw1.Port);
            connections[1].Port.Should().Be(pR1.Port);
            connections[2].Port.Should().Be(s1.Port);
            connections[3].Port.Should().Be(pRw2.Port);
            connections[4].Port.Should().Be(pR2.Port);
            connections[5].Port.Should().Be(s2.Port);
            connections[6].Port.Should().Be(pRw1.Port);
            connections[7].Port.Should().Be(pR1.Port);
            connections[8].Port.Should().Be(s1.Port);
            connections[9].Port.Should().Be(pRw2.Port);
            connections[10].Port.Should().Be(pR2.Port);
            connections[11].Port.Should().Be(s2.Port);
            break;
        case TargetSessionAttributes.ReadWrite:
            connections[0].Port.Should().Be(pRw1.Port);
            connections[1].Port.Should().Be(pRw2.Port);
            connections[2].Port.Should().Be(pRw1.Port);
            connections[3].Port.Should().Be(pRw2.Port);
            connections[4].Port.Should().Be(pRw1.Port);
            connections[5].Port.Should().Be(pRw2.Port);
            connections[6].Port.Should().Be(pRw1.Port);
            connections[7].Port.Should().Be(pRw2.Port);
            connections[8].Port.Should().Be(pRw1.Port);
            connections[9].Port.Should().Be(pRw2.Port);
            connections[10].Port.Should().Be(pRw1.Port);
            connections[11].Port.Should().Be(pRw2.Port);
            break;
        case TargetSessionAttributes.ReadOnly:
            connections[0].Port.Should().Be(pR1.Port);
            connections[1].Port.Should().Be(s1.Port);
            connections[2].Port.Should().Be(pR2.Port);
            connections[3].Port.Should().Be(s2.Port);
            connections[4].Port.Should().Be(pR1.Port);
            connections[5].Port.Should().Be(s1.Port);
            connections[6].Port.Should().Be(pR2.Port);
            connections[7].Port.Should().Be(s2.Port);
            connections[8].Port.Should().Be(pR1.Port);
            connections[9].Port.Should().Be(s1.Port);
            connections[10].Port.Should().Be(pR2.Port);
            connections[11].Port.Should().Be(s2.Port);
            break;
        case TargetSessionAttributes.Primary:
        case TargetSessionAttributes.PreferPrimary:
            connections[0].Port.Should().Be(pRw1.Port);
            connections[1].Port.Should().Be(pR1.Port);
            connections[2].Port.Should().Be(pRw2.Port);
            connections[3].Port.Should().Be(pR2.Port);
            connections[4].Port.Should().Be(pRw1.Port);
            connections[5].Port.Should().Be(pR1.Port);
            connections[6].Port.Should().Be(pRw2.Port);
            connections[7].Port.Should().Be(pR2.Port);
            connections[8].Port.Should().Be(pRw1.Port);
            connections[9].Port.Should().Be(pR1.Port);
            connections[10].Port.Should().Be(pRw2.Port);
            connections[11].Port.Should().Be(pR2.Port);
            break;
        case TargetSessionAttributes.Standby:
        case TargetSessionAttributes.PreferStandby:
            connections[0].Port.Should().Be(s1.Port);
            connections[1].Port.Should().Be(s2.Port);
            connections[2].Port.Should().Be(s1.Port);
            connections[3].Port.Should().Be(s2.Port);
            connections[4].Port.Should().Be(s1.Port);
            connections[5].Port.Should().Be(s2.Port);
            connections[6].Port.Should().Be(s1.Port);
            connections[7].Port.Should().Be(s2.Port);
            connections[8].Port.Should().Be(s1.Port);
            connections[9].Port.Should().Be(s2.Port);
            connections[10].Port.Should().Be(s1.Port);
            connections[11].Port.Should().Be(s2.Port);
            break;
        }
    }

    internal static string MultipleHosts(params PgPostmasterMock[] postmasters)
        => string.Join(",", postmasters.Select(p => $"{p.Host}:{p.Port}"));

    static string TargetSessionAttributesAsString(TargetSessionAttributes targetSessionAttributes)
        => targetSessionAttributes switch
        {
            TargetSessionAttributes.Any => "Any",
            TargetSessionAttributes.Primary => "Primary",
            TargetSessionAttributes.Standby => "Standby",
            TargetSessionAttributes.PreferPrimary => "Prefer-Primary",
            TargetSessionAttributes.PreferStandby => "Prefer-Standby",
            TargetSessionAttributes.ReadOnly => "Read-Only",
            TargetSessionAttributes.ReadWrite => "Read-Write",
            _ => null
        };

    sealed class DisposableWrapper(IEnumerable<IAsyncDisposable> disposables) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            foreach (var disposable in disposables)
                await disposable.DisposeAsync();
        }
    }
}

[Collection(NonParallelCollection.Name)]
public class MultipleHostsTestsNonParallel : TestBase
{
    [Fact]
    // Sets environment variable
    public async Task TargetSessionAttributes_uses_environment_variable()
    {
        //Arrange
        using var envVarResetter = SetEnvironmentVariable("PGTARGETSESSIONATTRS", "prefer-standby");

        await using var primaryPostmaster = PgPostmasterMock.Start(state: Primary);
        await using var standbyPostmaster = PgPostmasterMock.Start(state: Standby);

        var builder = new PgSqlConnectionStringBuilder
        {
            Host = MultipleHostsTests.MultipleHosts(primaryPostmaster, standbyPostmaster),
            ServerCompatibilityMode = ServerCompatibilityMode.NoTypeLoading
        };

        builder.TargetSessionAttributes.Should().BeNull();

        await using var dataSource = new PgSqlDataSourceBuilder(builder.ConnectionString)
            .BuildMultiHost();

        //Act
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        //Assert
        conn.Port.Should().Be(standbyPostmaster.Port);
    }

    [Fact]
    public async Task offline_state_on_query_execution_pg_non_critical_failure()
    {
        //Arrange
        await using var dataSource = CreateDataSource();
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        // Starting with PG14 we get the cluster's state from PG automatically
        var expectedState = conn.PostgreSqlVersion.Major > 13 ? DatabaseState.PrimaryReadWrite : DatabaseState.Unknown;

        var state = dataSource.GetDatabaseState();
        state.Should().Be(expectedState);
        dataSource.Statistics.Total.Should().Be(1);

        //Act
        var ex = await Assert.ThrowsAsync<PostgresException>(() => conn.ExecuteNonQueryAsync("SELECT abc", cancellationToken: TestContext.Current.CancellationToken));

        //Assert
        ex.SqlState.Should().Be(PostgresErrorCodes.UndefinedColumn);
        conn.State.Should().Be(ConnectionState.Open);

        state = dataSource.GetDatabaseState();
        state.Should().Be(expectedState);
        dataSource.Statistics.Total.Should().Be(1);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task integration_test(bool loadBalancing, bool alwaysCheckHostState)
    {
        //Arrange
        PoolManager.Reset();

        var dataSourceBuilder = new PgSqlDataSourceBuilder(ConnectionString)
        {
            ConnectionStringBuilder =
            {
                Host = "localhost,127.0.0.1",
                Pooling = true,
                MaxPoolSize = 2,
                LoadBalanceHosts = loadBalancing,
                HostRecheckSeconds = alwaysCheckHostState ? 0 : 10,
            }
        };
        using var dataSource = dataSourceBuilder.BuildMultiHost();

        var queriesDone = 0;

        //Act
        var clientsTask = Task.WhenAll(
            Client(dataSource, TargetSessionAttributes.Any),
            Client(dataSource, TargetSessionAttributes.Primary),
            Client(dataSource, TargetSessionAttributes.PreferPrimary),
            Client(dataSource, TargetSessionAttributes.PreferStandby),
            Client(dataSource, TargetSessionAttributes.ReadWrite));

        var onlyStandbyClient = Client(dataSource, TargetSessionAttributes.Standby);
        var readOnlyClient = Client(dataSource, TargetSessionAttributes.ReadOnly);

        //Assert
        await FluentActions.Awaiting(() => clientsTask).Should().NotThrowAsync();
        await Assert.ThrowsAsync<PgSqlException>(() => onlyStandbyClient);
        await Assert.ThrowsAsync<PgSqlException>(() => readOnlyClient);
        queriesDone.Should().Be(125);

        Task Client(PgSqlMultiHostDataSource multiHostDataSource, TargetSessionAttributes targetSessionAttributes)
        {
            var dataSource = multiHostDataSource.WithTargetSession(targetSessionAttributes);
            var tasks = new List<Task>(5);

            for (var i = 0; i < 5; i++)
            {
                tasks.Add(Task.Run(() => Query(dataSource), TestContext.Current.CancellationToken));
            }

            return Task.WhenAll(tasks);
        }

        async Task Query(PgSqlDataSource dataSource)
        {
            await using var conn = dataSource.CreateConnection();
            for (var i = 0; i < 5; i++)
            {
                await conn.OpenAsync(TestContext.Current.CancellationToken);
                await conn.ExecuteNonQueryAsync("SELECT 1", cancellationToken: TestContext.Current.CancellationToken);
                await conn.CloseAsync();
                Interlocked.Increment(ref queriesDone);
            }
        }
    }

    [Fact]
    [IssueLink("https://github.com/npgsql/npgsql/issues/5055")]
    // Disables sql rewriting
    public async Task multiple_hosts_with_disabled_sql_rewriting()
    {
        //Arrange
        using var _ = DisableSqlRewriting();

        var dataSourceBuilder = new PgSqlDataSourceBuilder(ConnectionString)
        {
            ConnectionStringBuilder =
            {
                Host = "localhost,127.0.0.1",
                Pooling = true,
                HostRecheckSeconds = 0
            }
        };

        //Act
        await using var dataSource = dataSourceBuilder.BuildMultiHost();
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
    }
}
