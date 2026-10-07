using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.PostgresClient.Internal;
using CodeBrix.PostgresClient.PgSqlTypes;
using CodeBrix.PostgresClient.PostgresTypes;
using CodeBrix.PostgresClient.Tests.Support;
using CodeBrix.PostgresClient.Util;
using SilverAssertions;
using Xunit;
using static CodeBrix.PostgresClient.Tests.TestUtil;

namespace CodeBrix.PostgresClient.Tests; //was previously: Npgsql.Tests;

public abstract class ConnectionTests(MultiplexingMode multiplexingMode) : MultiplexingTestBase(multiplexingMode)
{
    // Makes sure the connection goes through the proper state lifecycle
    [Fact]
    public async Task basic_lifecycle()
    {
        await using var conn = CreateConnection();

        var eventOpen = false;
        var eventClosed = false;

        conn.StateChange += (s, e) =>
        {
            if (e is { OriginalState: ConnectionState.Closed, CurrentState: ConnectionState.Open })
                eventOpen = true;

            if (e is { OriginalState: ConnectionState.Open, CurrentState: ConnectionState.Closed })
                eventClosed = true;
        };

        conn.State.Should().Be(ConnectionState.Closed);
        conn.FullState.Should().Be(ConnectionState.Closed);

        await conn.OpenAsync(TestContext.Current.CancellationToken);

        conn.State.Should().Be(ConnectionState.Open);
        conn.FullState.Should().Be(ConnectionState.Open);
        eventOpen.Should().BeTrue();

        await using (var cmd = new PgSqlCommand("SELECT 1", conn))
        await using (var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken))
        {
            await reader.ReadAsync(TestContext.Current.CancellationToken);

            conn.FullState.Should().Be(ConnectionState.Open | ConnectionState.Fetching);
            conn.State.Should().Be(ConnectionState.Open);
        }

        conn.FullState.Should().Be(ConnectionState.Open);
        conn.State.Should().Be(ConnectionState.Open);

        await conn.CloseAsync();

        conn.State.Should().Be(ConnectionState.Closed);
        conn.FullState.Should().Be(ConnectionState.Closed);
        eventClosed.Should().BeTrue();
    }

    // Makes sure the connection goes through the proper state lifecycle
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task broken_lifecycle(bool openFromClose)
    {
        if (IsMultiplexing)
            return;

        await using var dataSource = CreateDataSource();
        await using var conn = dataSource.CreateConnection();

        var eventOpen = false;
        var eventClosed = false;

        conn.StateChange += (s, e) =>
        {
            if (e is { OriginalState: ConnectionState.Closed, CurrentState: ConnectionState.Open })
                eventOpen = true;

            if (e is { OriginalState: ConnectionState.Open, CurrentState: ConnectionState.Closed })
                eventClosed = true;
        };

        conn.State.Should().Be(ConnectionState.Closed);
        conn.FullState.Should().Be(ConnectionState.Closed);

        await conn.OpenAsync(TestContext.Current.CancellationToken);
        await using var transaction = await conn.BeginTransactionAsync(TestContext.Current.CancellationToken);

        conn.State.Should().Be(ConnectionState.Open);
        conn.FullState.Should().Be(ConnectionState.Open);
        eventOpen.Should().BeTrue();

        var sleep = conn.ExecuteNonQueryAsync("SELECT pg_sleep(5)", cancellationToken: TestContext.Current.CancellationToken);

        // Wait for a query
        await Task.Delay(1000, cancellationToken: TestContext.Current.CancellationToken);
        await using (var killingConn = await OpenConnectionAsync())
            killingConn.ExecuteNonQuery($"SELECT pg_terminate_backend({conn.ProcessID})");

        await Assert.ThrowsAsync<PostgresException>(() => sleep);

        conn.FullState.Should().Be(ConnectionState.Broken);
        conn.State.Should().Be(ConnectionState.Closed);
        eventClosed.Should().BeTrue();
        (conn.Connector is null).Should().BeTrue();
        conn.PgSqlDataSource.Statistics.Total.Should().Be(0);

        if (openFromClose)
        {
            await conn.CloseAsync();

            conn.State.Should().Be(ConnectionState.Closed);
            conn.FullState.Should().Be(ConnectionState.Closed);
            eventClosed.Should().BeTrue();
        }

        await conn.Awaiting(c => c.OpenAsync()).Should().NotThrowAsync();
        (await conn.ExecuteScalarAsync("SELECT 1", cancellationToken: TestContext.Current.CancellationToken)).Should().Be(1);
        conn.PgSqlDataSource.Statistics.Total.Should().Be(1);
        await conn.Awaiting(c => c.CloseAsync()).Should().NotThrowAsync();
    }

    [Fact]
    public async Task break_while_open()
    {
        if (IsMultiplexing)
            return;

        await using var dataSource = CreateDataSource();
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        using (var conn2 = await OpenConnectionAsync())
            conn2.ExecuteNonQuery($"SELECT pg_terminate_backend({conn.ProcessID})");

        // Allow some time for the pg_terminate to kill our connection
        using (var cmd = CreateSleepCommand(conn, 10))
            cmd.Invoking(c => c.ExecuteNonQuery()).Should().Throw<PgSqlException>();

        conn.State.Should().Be(ConnectionState.Closed);
        conn.FullState.Should().Be(ConnectionState.Broken);
    }

    #region Connection Errors

    [Fact]
    public void invalid_Username()
    {
        if (IsMultiplexing)
            Assert.Skip("Not run with multiplexing");

        var connString = new PgSqlConnectionStringBuilder(ConnectionString)
        {
            Username = "unknown", Pooling = false
        }.ToString();
        using var conn = new PgSqlConnection(connString);
        conn.Invoking(c => c.Open()).Should().ThrowExactly<PostgresException>()
            .Where(e => e.SqlState == PostgresErrorCodes.InvalidPassword);
        conn.FullState.Should().Be(ConnectionState.Closed);
    }

    [Fact]
    public void bad_database()
    {
        //Arrange
        using var dataSource = CreateDataSource(csb => csb.Database = "does_not_exist");
        using var conn = dataSource.CreateConnection();

        //Act
        var act = () => conn.Open();

        //Assert
        act.Should().ThrowExactly<PostgresException>()
            .Where(e => e.SqlState == PostgresErrorCodes.InvalidCatalogName);
    }

    // Tests that mandatory connection string parameters are indeed mandatory
    [Fact]
    public void mandatory_connection_string_params()
        => Assert.Throws<ArgumentNullException>(() =>
            new PgSqlConnection("User ID=pgsql_tests;Password=pgsql_tests;Database=pgsql_tests"));

    // Reuses the same connection instance for a failed connection, then a successful one
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task fail_connect_then_succeed(bool pooling)
    {
        if (IsMultiplexing && !pooling) // Multiplexing doesn't work without pooling
            return;

        var dbName = GetUniqueIdentifier(nameof(fail_connect_then_succeed));
        await using var conn1 = await OpenConnectionAsync();
        await conn1.ExecuteNonQueryAsync($"DROP DATABASE IF EXISTS \"{dbName}\"", cancellationToken: TestContext.Current.CancellationToken);
        try
        {
            await using var dataSource = CreateDataSource(csb =>
            {
                csb.Database = dbName;
                csb.Pooling = pooling;
            });

            await using var conn2 = dataSource.CreateConnection();
            var pgEx = await Assert.ThrowsAsync<PostgresException>(() => conn2.OpenAsync(TestContext.Current.CancellationToken));
            pgEx.SqlState.Should().Be(PostgresErrorCodes.InvalidCatalogName); // database doesn't exist
            conn2.FullState.Should().Be(ConnectionState.Closed);

            await conn1.ExecuteNonQueryAsync($"CREATE DATABASE \"{dbName}\" TEMPLATE template0", cancellationToken: TestContext.Current.CancellationToken);

            await conn2.Awaiting(c => c.OpenAsync()).Should().NotThrowAsync();
            await conn2.Awaiting(c => c.CloseAsync()).Should().NotThrowAsync();
        }
        finally
        {
            await conn1.ExecuteNonQueryAsync($"DROP DATABASE IF EXISTS \"{dbName}\"", cancellationToken: TestContext.Current.CancellationToken);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task open_timeout_unknown_ip(bool async)
    {
        const int timeoutSeconds = 2;

        var unknownIp = Environment.GetEnvironmentVariable("PGSQL_UNKNOWN_IP");
        if (unknownIp is null)
        {
            Assert.Skip("PGSQL_UNKNOWN_IP isn't defined and is required for connection timeout tests");
            return;
        }

        using var dataSource = CreateDataSource(csb =>
        {
            csb.Host = unknownIp;
            csb.Timeout = timeoutSeconds;
        });
        using var conn = dataSource.CreateConnection();

        var sw = Stopwatch.StartNew();
        if (async)
        {
            await conn.Awaiting(c => c.OpenAsync()).Should().ThrowExactlyAsync<PgSqlException>()
                .WithInnerExceptionExactly(typeof(TimeoutException));
        }
        else
        {
            conn.Invoking(c => c.Open()).Should().ThrowExactly<PgSqlException>()
                .WithInnerExceptionExactly<TimeoutException>();
        }

        sw.Elapsed.TotalMilliseconds.Should().BeGreaterThanOrEqualTo(timeoutSeconds * 1000 - 100, $"Timeout was supposed to happen after {timeoutSeconds} seconds, but fired after {sw.Elapsed.TotalSeconds}");
        conn.State.Should().Be(ConnectionState.Closed);
    }

    [Fact]
    public async Task connect_timeout_cancel()
    {
        var unknownIp = Environment.GetEnvironmentVariable("PGSQL_UNKNOWN_IP");
        if (unknownIp is null)
        {
            Assert.Skip("PGSQL_UNKNOWN_IP isn't defined and is required for connection cancellation tests");
            return;
        }

        var connString = new PgSqlConnectionStringBuilder(ConnectionString)
        {
            Host = unknownIp,
            Pooling = false,
            Timeout = 30
        }.ToString();
        using var conn = new PgSqlConnection(connString);
        var cts = new CancellationTokenSource(1000);
        await conn.Awaiting(c => c.OpenAsync(cts.Token)).Should().ThrowExactlyAsync<OperationCanceledException>();
        conn.State.Should().Be(ConnectionState.Closed);
    }

    [Fact]
    public void bad_hostname()
    {
        //Arrange
        using var dataSource = CreateDataSource(csb => csb.Host = "hostname.that.does.not.exist");
        using var conn = dataSource.CreateConnection();

        //Act
        var act = () => conn.Open();

        //Assert
        act.Should().ThrowExactly<PgSqlException>()
            .WithInnerExceptionExactly<SocketException>();
    }

    [Fact]
    public async Task bad_hostname_async()
    {
        //Arrange
        using var dataSource = CreateDataSource(csb => csb.Host = "hostname.that.does.not.exist");
        using var conn = dataSource.CreateConnection();

        //Act
        var act = async () => await conn.OpenAsync();

        //Assert
        await act.Should().ThrowExactlyAsync<PgSqlException>()
            .WithInnerExceptionExactly(typeof(SocketException));
    }

    #endregion

    #region Client Encoding

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/1065")]
    public async Task client_encoding_is_UTF8_by_default()
    {
        using var conn = await OpenConnectionAsync();
        (await conn.ExecuteScalarAsync("SHOW client_encoding", cancellationToken: TestContext.Current.CancellationToken)).Should().Be("UTF8");
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/1065")]
    public async Task client_encoding_connection_param()
    {
        using (var conn = await OpenConnectionAsync())
            (await conn.ExecuteScalarAsync("SHOW client_encoding", cancellationToken: TestContext.Current.CancellationToken)).Should().NotBe("SQL_ASCII");
        await using var dataSource = CreateDataSource(csb => csb.ClientEncoding = "SQL_ASCII");
        using (var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken))
            (await conn.ExecuteScalarAsync("SHOW client_encoding", cancellationToken: TestContext.Current.CancellationToken)).Should().Be("SQL_ASCII");
    }

    #endregion Client Encoding

    #region Timezone

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/1634")]
    public async Task timezone_connection_param()
    {
        string newTimezone;
        using (var conn = await OpenConnectionAsync())
        {
            newTimezone = (string)await conn.ExecuteScalarAsync("SHOW TIMEZONE", cancellationToken: TestContext.Current.CancellationToken) == "Africa/Bamako"
                ? "Africa/Lagos"
                : "Africa/Bamako";
        }

        await using var dataSource = CreateDataSource(csb => csb.Timezone = newTimezone);
        using (var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken))
            (await conn.ExecuteScalarAsync("SHOW TIMEZONE", cancellationToken: TestContext.Current.CancellationToken)).Should().Be(newTimezone);
    }

    #endregion Timezone

    #region Application Name

    [Fact]
    public async Task application_name_connection_param()
    {
        const string testAppName = "MyTestApp2";

        await using var dataSource = CreateDataSource(csb => csb.ApplicationName = testAppName);
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        conn.PostgresParameters["application_name"].Should().Be(testAppName);
    }

    #endregion Application Name

    #region ConnectionString - Host

    [Theory, IssueLink("https://github.com/npgsql/npgsql/issues/3802")]
    [InlineData("127.0.0.1", new [] { "127.0.0.1:5432" })]
    [InlineData("127.0.0.1:5432", new [] { "127.0.0.1:5432" })]
    [InlineData("::1", new [] { "::1:5432" })]
    [InlineData("[::1]", new [] { "[::1]:5432" })]
    [InlineData("[::1]:5432", new [] { "[::1]:5432" })]
    [InlineData("localhost", new [] { "localhost:5432" })]
    [InlineData("localhost:5432", new [] { "localhost:5432" })]
    [InlineData("127.0.0.1,127.0.0.1:5432,::1,[::1],[::1]:5432,localhost,localhost:5432",
        new []
        {
            "127.0.0.1:5432",
            "127.0.0.1:5432",
            "::1:5432",
            "[::1]:5432",
            "[::1]:5432",
            "localhost:5432",
            "localhost:5432"
        })]
    public void ConnectionString_Host(string host, string[] expected)
    {
        //Arrange
        var dataSourceBuilder = new PgSqlDataSourceBuilder
        {
            ConnectionStringBuilder = { Host = host }
        };

        //Act
        using var dataSource = dataSourceBuilder.BuildMultiHost();

        //Assert
        dataSource.Pools.Select(ds => $"{ds.Settings.Host}:{ds.Settings.Port}").ToArray().Should().Equal(expected);
    }

    #endregion ConnectionString - Host

    [Fact]
    public async Task unix_domain_socket()
    {
        Assert.Skip("Unix-socket connections are not available against the Docker test server");

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            if (Environment.OSVersion.Version.Major < 10 || Environment.OSVersion.Version.Build < 17093)
                Assert.Skip("Unix-domain sockets support was introduced in Windows build 17093");

            // On Windows we first need a classic IP connection to make sure we're running against the
            // right backend version
            using var versionConnection = await OpenConnectionAsync();
            MinimumPgVersion(versionConnection, "13.0", "Unix-domain sockets support on Windows was introduced in PostgreSQL 13");
        }

        var port = new PgSqlConnectionStringBuilder(ConnectionString).Port;
        var candidateDirectories = new[] { "/var/run/postgresql", "/tmp", Environment.GetEnvironmentVariable("TMP") ?? "C:\\" };
        var dir = candidateDirectories.FirstOrDefault(d => File.Exists(Path.Combine(d, $".s.PGSQL.{port}")));
        if (dir == null)
        {
            IgnoreExceptOnBuildServer("No PostgreSQL unix domain socket was found");
            return;
        }

        try
        {
            await using var dataSource = CreateDataSource(csb => csb.Host = dir);
            await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
            await using var tx = await conn.BeginTransactionAsync(TestContext.Current.CancellationToken);
            (await conn.ExecuteScalarAsync("SELECT 1", tx, cancellationToken: TestContext.Current.CancellationToken)).Should().Be(1);
            conn.DataSource.Should().Be(Path.Combine(dir, $".s.PGSQL.{port}"));
        }
        catch (Exception ex)
        {
            IgnoreExceptOnBuildServer($"Connection via unix domain socket failed: {ex}");
        }
    }

    [Fact]
    public async Task unix_abstract_domain_socket()
    {
        Assert.Skip("Unix-socket connections are not available against the Docker test server");

        if (OperatingSystem.IsMacOS())
            Assert.Skip("Fails only on mac, needs to be investigated");

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.Skip("Abstract unix-domain sockets are not supported on windows");
        }

        // We first need a classic IP connection to make sure we're running against the
        // right backend version
        using var versionConnection = await OpenConnectionAsync();
        MinimumPgVersion(versionConnection, "14.0", "Abstract unix-domain sockets support was introduced in PostgreSQL 14");

        var csb = new PgSqlConnectionStringBuilder(ConnectionString)
        {
            Host = "@/pgsql_unix"
        };

        try
        {
            await using var dataSource = CreateDataSource(csb.ToString());
            await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
            await using var tx = await conn.BeginTransactionAsync(TestContext.Current.CancellationToken);
            (await conn.ExecuteScalarAsync("SELECT 1", tx, cancellationToken: TestContext.Current.CancellationToken)).Should().Be(1);
            conn.DataSource.Should().Be(Path.Combine(csb.Host, $".s.PGSQL.{csb.Port}"));
        }
        catch (Exception ex)
        {
            IgnoreExceptOnBuildServer($"Connection via abstract unix domain socket failed: {ex}");
        }
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/903")]
    public void DataSource_property()
    {
        using var conn = new PgSqlConnection();
        conn.DataSource.Should().Be(string.Empty);

        var csb = new PgSqlConnectionStringBuilder(ConnectionString);

        conn.ConnectionString = csb.ConnectionString;
        conn.DataSource.Should().Be($"tcp://{csb.Host}:{csb.Port}");

        // Multiplexing isn't supported with multiple hosts
        if (IsMultiplexing)
            return;

        csb.Host = "127.0.0.1, 127.0.0.2";
        conn.ConnectionString = csb.ConnectionString;
        conn.DataSource.Should().Be(string.Empty);
    }

    #region Server version

    [Fact]
    public async Task PostgreSqlVersion_ServerVersion()
    {
        await using var c = new PgSqlConnection(ConnectionString);

        c.Invoking(x => x.PostgreSqlVersion).Should().ThrowExactly<InvalidOperationException>()
            .Which.Message.Should().Be("Connection is not open");

        c.Invoking(x => x.ServerVersion).Should().ThrowExactly<InvalidOperationException>()
            .Which.Message.Should().Be("Connection is not open");

        await c.OpenAsync(TestContext.Current.CancellationToken);
        var backendVersionString = (string)(await c.ExecuteScalarAsync("SHOW server_version", cancellationToken: TestContext.Current.CancellationToken));

        backendVersionString.Should().Be(c.ServerVersion);

        backendVersionString.Should().Contain(
            new[] { "rc", "beta", "devel" }.Any(x => backendVersionString.Contains(x))
                ? c.PostgreSqlVersion.Major.ToString()
                : c.PostgreSqlVersion.ToString());
    }

    [Theory]
    [InlineData("X13.0")]
    [InlineData("13.")]
    [InlineData("13.1.")]
    [InlineData("13.1.1.")]
    [InlineData("13.1.1.1.")]
    [InlineData("13.1.1.1.1")]
    public void parse_version_fails(string versionString)
        => ((Func<Version>)(() => TestDbInfo.ParseServerVersion(versionString))).Should().Throw<Exception>();

    [Theory]
    [InlineData("13.3", "13.3")]
    [InlineData("13.3X", "13.3")]
    [InlineData("9.6.4", "9.6.4")]
    [InlineData("9.6.4X", "9.6.4")]
    [InlineData("9.5alpha2", "9.5")]
    [InlineData("9.5alpha2X", "9.5")]
    [InlineData("9.5devel", "9.5")]
    [InlineData("9.5develX", "9.5")]
    [InlineData("9.5deveX", "9.5")]
    [InlineData("9.4beta3", "9.4")]
    [InlineData("9.4rc1", "9.4")]
    [InlineData("9.4rc1X", "9.4")]
    [InlineData("13devel", "13.0")]
    [InlineData("13beta1", "13.0")]
    // The following should not occur as PostgreSQL version string in the wild these days but we support it.
    [InlineData("13", "13.0")]
    [InlineData("13X", "13.0")]
    [InlineData("13alpha1", "13.0")]
    [InlineData("13alpha", "13.0")]
    [InlineData("13alphX", "13.0")]
    [InlineData("13beta", "13.0")]
    [InlineData("13betX", "13.0")]
    [InlineData("13rc1", "13.0")]
    [InlineData("13rc", "13.0")]
    [InlineData("13rX", "13.0")]
    [InlineData("99999.99999.99999.99999", "99999.99999.99999.99999")]
    [InlineData("99999.99999.99999.99999X", "99999.99999.99999.99999")]
    [InlineData("99999.99999.99999.99999devel", "99999.99999.99999.99999")]
    [InlineData("99999.99999.99999.99999alpha99999", "99999.99999.99999.99999")]
    [InlineData("99999.99999.99999alpha99999", "99999.99999.99999")]
    [InlineData("99999.99999.99999.99999beta99999", "99999.99999.99999.99999")]
    [InlineData("99999.99999.99999beta99999", "99999.99999.99999")]
    [InlineData("99999.99999.99999.99999rc99999", "99999.99999.99999.99999")]
    [InlineData("99999.99999.99999rc99999", "99999.99999.99999")]
    public void parse_version_succeeds(string versionString, string expected)
        => TestDbInfo.ParseServerVersion(versionString).ToString().Should().Be(expected);

    class TestDbInfo : PgSqlDatabaseInfo
    {
        public TestDbInfo(string host, int port, string databaseName, Version version) : base(host, port, databaseName, version)
            => throw new NotImplementedException();

        protected override IEnumerable<PostgresType> GetTypes()
            => throw new NotImplementedException();

        public new static Version ParseServerVersion(string versionString)
            => PgSqlDatabaseInfo.ParseServerVersion(versionString);
    }

    #endregion Server version

    [Fact]
    public void setting_connection_string_while_open_throws()
    {
        using var conn = new PgSqlConnection();
        conn.ConnectionString = ConnectionString;
        conn.Open();
        conn.Invoking(c => c.ConnectionString = "").Should().ThrowExactly<InvalidOperationException>();
    }

    [Fact]
    public void empty_constructor()
    {
        var conn = new PgSqlConnection();
        conn.ConnectionTimeout.Should().Be(PgSqlConnectionStringBuilder.DefaultTimeout);
        conn.ConnectionString.Should().BeSameAs(string.Empty);
        conn.Invoking(c => c.Open()).Should().ThrowExactly<InvalidOperationException>();
    }

    [Fact]
    public void constructor_with_null_connection_string()
    {
        var conn = new PgSqlConnection(null);
        conn.ConnectionString.Should().BeSameAs(string.Empty);
        conn.Invoking(c => c.Open()).Should().ThrowExactly<InvalidOperationException>();
    }

    [Fact]
    public void constructor_with_empty_connection_string()
    {
        var conn = new PgSqlConnection("");
        conn.ConnectionString.Should().BeSameAs(string.Empty);
        conn.Invoking(c => c.Open()).Should().ThrowExactly<InvalidOperationException>();
    }

    [Fact]
    public void set_connection_string_to_null()
    {
        var conn = new PgSqlConnection(ConnectionString);
        conn.ConnectionString = null;
        conn.ConnectionString.Should().BeSameAs(string.Empty);
        conn.Settings.Host.Should().BeNull();
        conn.Invoking(c => c.Open()).Should().ThrowExactly<InvalidOperationException>();
    }

    [Fact]
    public void set_connection_string_to_empty()
    {
        var conn = new PgSqlConnection(ConnectionString);
        conn.ConnectionString = "";
        conn.ConnectionString.Should().BeSameAs(string.Empty);
        conn.Settings.Host.Should().BeNull();
        conn.Invoking(c => c.Open()).Should().ThrowExactly<InvalidOperationException>();
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/703")]
    public async Task no_database_defaults_to_username()
    {
        var csb = new PgSqlConnectionStringBuilder(ConnectionString) { Database = null };
        using var conn = new PgSqlConnection(csb.ToString());
        conn.Database.Should().Be(csb.Username);
        conn.Open();
        (await conn.ExecuteScalarAsync("SELECT current_database()", cancellationToken: TestContext.Current.CancellationToken)).Should().Be(csb.Username);
        conn.Database.Should().Be(csb.Username);
    }

    // Breaks a connector while it's in the pool, with a keepalive and without
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task break_connector_in_pool(bool keepAlive)
    {
        if (IsMultiplexing)
            Assert.Skip("Multiplexing, hanging");

        var dataSourceBuilder = CreateDataSourceBuilder();
        dataSourceBuilder.ConnectionStringBuilder.MaxPoolSize = 1;
        if (keepAlive)
            dataSourceBuilder.ConnectionStringBuilder.KeepAlive = 1;
        await using var dataSource = dataSourceBuilder.Build();
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        var connector = conn.Connector;
        connector.Should().NotBeNull();
        await conn.CloseAsync();

        // Use another connection to kill the connector currently in the pool
        await using (var conn2 = await OpenConnectionAsync())
            await conn2.ExecuteNonQueryAsync($"SELECT pg_terminate_backend({connector.BackendProcessId})", cancellationToken: TestContext.Current.CancellationToken);

        // Allow some time for the terminate to occur
        await Task.Delay(3000, cancellationToken: TestContext.Current.CancellationToken);

        await conn.OpenAsync(TestContext.Current.CancellationToken);
        conn.FullState.Should().Be(ConnectionState.Open);
        if (keepAlive)
        {
            conn.Connector.Should().NotBeSameAs(connector);
            (await conn.ExecuteScalarAsync("SELECT 1", cancellationToken: TestContext.Current.CancellationToken)).Should().Be(1);
        }
        else
        {
            conn.Connector.Should().BeSameAs(connector);
            await conn.Awaiting(c => c.ExecuteScalarAsync("SELECT 1")).Should().ThrowAsync<PgSqlException>();
        }
    }

    [Fact]
    [IssueLink("https://github.com/npgsql/npgsql/issues/4603")]
    public async Task reload_types_keepalive_concurrent()
    {
        if (IsMultiplexing)
            Assert.Skip("Multiplexing doesn't support keepalive");

        await using var dataSource = CreateDataSource(csb => csb.KeepAlive = 1);
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        var startTimestamp = Stopwatch.GetTimestamp();
        // Give a few seconds for a KeepAlive to possibly perform
        while (Stopwatch.GetElapsedTime(startTimestamp).TotalSeconds < 2)
            conn.Invoking(c => c.ReloadTypes()).Should().NotThrow();
    }

    #region ChangeDatabase

    [Fact]
    public async Task ChangeDatabase()
    {
        using var conn = await OpenConnectionAsync();
        conn.ChangeDatabase("template1");
        using var cmd = new PgSqlCommand("select current_database()", conn);
        (await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken)).Should().Be("template1");
    }

    [Fact]
    public async Task ChangeDatabase_does_not_affect_other_connections()
    {
        using var conn1 = new PgSqlConnection(ConnectionString);
        using var conn2 = new PgSqlConnection(ConnectionString);
        // Connection 1 changes database
        conn1.Open();
        conn1.ChangeDatabase("template1");
        (await conn1.ExecuteScalarAsync("SELECT current_database()", cancellationToken: TestContext.Current.CancellationToken)).Should().Be("template1");

        // Connection 2's database should not changed
        conn2.Open();
        (await conn2.ExecuteScalarAsync("SELECT current_database()", cancellationToken: TestContext.Current.CancellationToken)).Should().NotBe(conn1.Database);
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/1331")]
    public void ChangeDatabase_connection_on_closed_connection_throws()
    {
        using var conn = new PgSqlConnection(ConnectionString);
        conn.Invoking(c => c.ChangeDatabase("template1")).Should().ThrowExactly<InvalidOperationException>()
            .Which.Message.Should().Be("Connection is not open");
    }

    #endregion

    // Tests closing a connector while a reader is open
    [Theory]
    [InlineData(PooledOrNot.Pooled)]
    [InlineData(PooledOrNot.Unpooled)]
    public async Task Close_during_read(PooledOrNot pooled)
    {
        if (IsMultiplexing && pooled == PooledOrNot.Unpooled)
            return; // Multiplexing requires pooling

        await using var dataSource = CreateDataSource(csb => csb.Pooling = pooled == PooledOrNot.Pooled);
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using (var cmd = new PgSqlCommand("SELECT 1", conn))
        await using (var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken))
        {
            reader.Read();
            conn.Close();
            conn.State.Should().Be(ConnectionState.Closed);
            reader.IsClosed.Should().BeTrue();
        }

        conn.Open();
        conn.FullState.Should().Be(ConnectionState.Open);
        (await conn.ExecuteScalarAsync("SELECT 1", cancellationToken: TestContext.Current.CancellationToken)).Should().Be(1);
    }

    [Fact]
    public async Task search_path()
    {
        await using var dataSource = CreateDataSource(csb => csb.SearchPath = "foo");
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        ((string)await conn.ExecuteScalarAsync("SHOW search_path", cancellationToken: TestContext.Current.CancellationToken)).Should().Contain("foo");
    }

    [Fact]
    public async Task set_options()
    {
        await using var dataSource = CreateDataSource(csb =>
            csb.Options =
                "-c default_transaction_isolation=serializable -c default_transaction_deferrable=on -c foo.bar=My\\ Famous\\\\Thing");
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        (await conn.ExecuteScalarAsync("SHOW default_transaction_isolation", cancellationToken: TestContext.Current.CancellationToken)).Should().Be("serializable");
        (await conn.ExecuteScalarAsync("SHOW default_transaction_deferrable", cancellationToken: TestContext.Current.CancellationToken)).Should().Be("on");
        (await conn.ExecuteScalarAsync("SHOW foo.bar", cancellationToken: TestContext.Current.CancellationToken)).Should().Be("My Famous\\Thing");
    }

    [Fact]
    public async Task connector_not_initialized_exception()
    {
        var command = new PgSqlCommand();
        command.CommandText = @"SELECT 123";

        for (var i = 0; i < 2; i++)
        {
            await using var connection = await OpenConnectionAsync();
            command.Connection = connection;
            await using var tx = await connection.BeginTransactionAsync(TestContext.Current.CancellationToken);
            await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
            await tx.CommitAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public void bug_1011001()
    {
        //[#1011001] Bug in PgSqlConnectionStringBuilder affects on cache and connection pool

        var csb1 = new PgSqlConnectionStringBuilder(@"Server=server;Port=5432;User Id=user;Password=passwor;Database=database;");
        var cs1 = csb1.ToString();
        var csb2 = new PgSqlConnectionStringBuilder(cs1);
        var cs2 = csb2.ToString();
        (cs1 == cs2).Should().BeTrue();
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/pull/164")]
    public void connection_State_is_Closed_when_disposed()
    {
        var c = new PgSqlConnection();
        c.Dispose();
        c.State.Should().Be(ConnectionState.Closed);
    }

    [Fact]
    public void change_ApplicationName_with_connection_string_builder()
    {
        // Test for issue #165 on github.
        var builder = new PgSqlConnectionStringBuilder();
        builder.ApplicationName = "test";
    }

    // Makes sure notices are probably received and emitted as events
    [Fact]
    public async Task notice()
    {
        // Make sure messages are in English
        await using var dataSource = CreateDataSource(csb => csb.Options = "-c lc_messages=en_US.UTF-8");
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        var function = await GetTempFunctionName(conn);
        await conn.ExecuteNonQueryAsync($@"
CREATE OR REPLACE FUNCTION {function}() RETURNS VOID AS
'BEGIN RAISE NOTICE ''testnotice''; END;'
LANGUAGE 'plpgsql'", cancellationToken: TestContext.Current.CancellationToken);

        var mre = new ManualResetEvent(false);
        PostgresNotice notice = null;
        NoticeEventHandler action = (sender, args) =>
        {
            notice = args.Notice;
            mre.Set();
        };
        conn.Notice += action;
        try
        {
            // See docs for CreateSleepCommand
            await conn.ExecuteNonQueryAsync($"SELECT {function}()::TEXT", cancellationToken: TestContext.Current.CancellationToken);
            mre.WaitOne(5000);
            notice.Should().NotBeNull("No notice was emitted");
            notice.MessageText.Should().Be("testnotice");
            notice.Severity.Should().Be("NOTICE");
        }
        finally
        {
            conn.Notice -= action;
        }
    }

    // Makes sure that concurrent use of the connection throws an exception
    [Fact]
    public async Task concurrent_use_throws()
    {
        if (IsMultiplexing)
            Assert.Skip("Multiplexing: fails");
        using var conn = await OpenConnectionAsync();
        using (var cmd = new PgSqlCommand("SELECT 1", conn))
        using (await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken))
            await conn.Awaiting(c => c.ExecuteScalarAsync("SELECT 2")).Should().ThrowExactlyAsync<PgSqlOperationInProgressException>()
                .Where(e => ReferenceEquals(e.CommandInProgress, cmd));

        await conn.ExecuteNonQueryAsync("CREATE TEMP TABLE foo (bar INT)", cancellationToken: TestContext.Current.CancellationToken);
        using (conn.BeginBinaryImport("COPY foo (bar) FROM STDIN BINARY"))
        {
            await conn.Awaiting(c => c.ExecuteScalarAsync("SELECT 2")).Should().ThrowExactlyAsync<PgSqlOperationInProgressException>()
                .WithMessage("*Copy*");
        }
    }

    #region PersistSecurityInfo

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [IssueLink("https://github.com/npgsql/npgsql/issues/783")]
    public void PersistSecurityInfo_is_true(bool pooling)
    {
        if (IsMultiplexing && !pooling)
            return;

        var connString = new PgSqlConnectionStringBuilder(ConnectionString)
        {
            PersistSecurityInfo = true,
            Pooling = pooling
        }.ToString();
        using var conn = new PgSqlConnection(connString);
        var passwd = new PgSqlConnectionStringBuilder(conn.ConnectionString).Password;
        passwd.Should().NotBeNull();
        conn.Open();
        new PgSqlConnectionStringBuilder(conn.ConnectionString).Password.Should().Be(passwd);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [IssueLink("https://github.com/npgsql/npgsql/issues/783")]
    public void no_password_without_PersistSecurityInfo(bool pooling)
    {
        if (IsMultiplexing && !pooling)
            return;

        var connString = new PgSqlConnectionStringBuilder(ConnectionString)
        {
            Pooling = pooling
        }.ToString();
        using var conn = new PgSqlConnection(connString);
        var csb = new PgSqlConnectionStringBuilder(conn.ConnectionString);
        csb.PersistSecurityInfo.Should().BeFalse();
        csb.Password.Should().NotBeNull();
        conn.Open();
        new PgSqlConnectionStringBuilder(conn.ConnectionString).Password.Should().BeNull();
    }

    [Theory, IssueLink("https://github.com/npgsql/npgsql/issues/2725")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Clone_with_PersistSecurityInfo(bool async)
    {
        var builder = new PgSqlConnectionStringBuilder(ConnectionString)
        {
            PersistSecurityInfo = true
        };
        using var _ = CreateTempPool(builder, out var connStringWithPersist);

        using var connWithPersist = new PgSqlConnection(connStringWithPersist);

        // First un-persist, should work
        builder.PersistSecurityInfo = false;
        var connStringWithoutPersist = builder.ToString();
        using var clonedWithoutPersist = async
            ? await connWithPersist.CloneWithAsync(connStringWithoutPersist, cancellationToken: TestContext.Current.CancellationToken)
            : connWithPersist.CloneWith(connStringWithoutPersist);
        clonedWithoutPersist.Open();

        clonedWithoutPersist.ConnectionString.Should().NotContain("Password=");

        // Then attempt to re-persist, should not work
        using var clonedConn = async
            ? await clonedWithoutPersist.CloneWithAsync(connStringWithPersist, cancellationToken: TestContext.Current.CancellationToken)
            : clonedWithoutPersist.CloneWith(connStringWithPersist);
        clonedConn.Open();

        clonedConn.ConnectionString.Should().NotContain("Password=");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CloneWith_and_data_source_with_password(bool async)
    {
        var dataSourceBuilder = new PgSqlDataSourceBuilder(ConnectionString);
        // Set the password via the data source property later to make sure that's picked up by CloneWith
        var password = dataSourceBuilder.ConnectionStringBuilder.Password;
        dataSourceBuilder.ConnectionStringBuilder.Password = null;
        await using var dataSource = dataSourceBuilder.Build();

        await using var connection = dataSource.CreateConnection();
        dataSource.Password = password;

        // Test that the up-to-date password gets copied to the clone, as if we opened the original connection instead of cloning it
        using var _ = CreateTempPool(new PgSqlConnectionStringBuilder(ConnectionString) { Password = null }, out var tempConnectionString);
        await using var clonedConnection = async
            ? await connection.CloneWithAsync(tempConnectionString, cancellationToken: TestContext.Current.CancellationToken)
            : connection.CloneWith(tempConnectionString);
        await clonedConnection.OpenAsync(TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CloneWith_and_data_source_with_auth_callbacks(bool async)
    {
        var (userCertificateValidationCallbackCalled, clientCertificatesCallbackCalled) = (false, false);

        var dataSourceBuilder = CreateDataSourceBuilder();
        dataSourceBuilder.UseSslClientAuthenticationOptionsCallback(options =>
        {
            ClientCertificatesCallback(options.ClientCertificates);
            options.RemoteCertificateValidationCallback = UserCertificateValidationCallback;
        });
        await using var dataSource = dataSourceBuilder.Build();
        await using var connection = dataSource.CreateConnection();

        using var _ = CreateTempPool(ConnectionString, out var tempConnectionString);
        await using var clonedConnection = async
            ? await connection.CloneWithAsync(tempConnectionString, cancellationToken: TestContext.Current.CancellationToken)
            : connection.CloneWith(tempConnectionString);

        var sslClientAuthenticationOptions = new SslClientAuthenticationOptions();
        clonedConnection.SslClientAuthenticationOptionsCallback(sslClientAuthenticationOptions);
        clientCertificatesCallbackCalled.Should().BeTrue();
        sslClientAuthenticationOptions.RemoteCertificateValidationCallback(null, null, null, SslPolicyErrors.None);
        userCertificateValidationCallbackCalled.Should().BeTrue();

        bool UserCertificateValidationCallback(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors errors)
            => userCertificateValidationCallbackCalled = true;

        void ClientCertificatesCallback(X509CertificateCollection certs)
            => clientCertificatesCallbackCalled = true;
    }

    #endregion PersistSecurityInfo

    [Fact]
    [IssueLink("https://github.com/npgsql/npgsql/issues/743")]
    [IssueLink("https://github.com/npgsql/npgsql/issues/783")]
    public async Task Clone()
    {
        using var pool = CreateTempPool(ConnectionString, out var connectionString);
        using var conn = new PgSqlConnection(connectionString);
        Action<SslClientAuthenticationOptions> callback = _ => { };
        conn.SslClientAuthenticationOptionsCallback = callback;

        conn.Open();
        (await conn.ExecuteScalarAsync("SELECT 1", cancellationToken: TestContext.Current.CancellationToken)).Should().Be(1);

        using var conn2 = (PgSqlConnection)((ICloneable)conn).Clone();
        conn2.ConnectionString.Should().Be(conn.ConnectionString);
        conn2.SslClientAuthenticationOptionsCallback.Should().BeSameAs(callback);
        conn2.Open();
        (await conn2.ExecuteScalarAsync("SELECT 1", cancellationToken: TestContext.Current.CancellationToken)).Should().Be(1);
    }

    [Fact]
    public async Task Clone_with_data_source()
    {
        await using var connection = await DataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using var clonedConnection = (PgSqlConnection)((ICloneable)connection).Clone();

        clonedConnection.PgSqlDataSource.Should().BeSameAs(DataSource);
        await clonedConnection.Awaiting(c => c.OpenAsync()).Should().NotThrowAsync();
    }

    [Fact]
    public async Task DatabaseInfo_is_shared()
    {
        if (IsMultiplexing)
            return;
        // Create a temp pool to make sure the second connection will be new and not idle
        await using var dataSource = CreateDataSource();
        await using var conn1 = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        // Call RealoadTypes to force reload DatabaseInfo
        conn1.ReloadTypes();
        await using var conn2 = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        conn1.Connector.DatabaseInfo.Should().BeSameAs(conn2.Connector.DatabaseInfo);
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/736")]
    public async Task many_open_close()
    {
        await using var dataSource = CreateDataSource();
        // The connector's _sentRfqPrependedMessages is a byte, too many open/closes made it overflow
        for (var i = 0; i < 255; i++)
        {
            await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        }
        await using (var conn = dataSource.CreateConnection())
        {
            await conn.OpenAsync(TestContext.Current.CancellationToken);
        }
        await using (var conn = dataSource.CreateConnection())
        {
            await conn.OpenAsync(TestContext.Current.CancellationToken);
            (await conn.ExecuteScalarAsync("SELECT 1", cancellationToken: TestContext.Current.CancellationToken)).Should().Be(1);
        }
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/736")]
    public async Task many_open_close_with_transaction()
    {
        await using var dataSource = CreateDataSource();
        // The connector's _sentRfqPrependedMessages is a byte, too many open/closes made it overflow
        for (var i = 0; i < 255; i++)
        {
            await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
            await conn.BeginTransactionAsync(TestContext.Current.CancellationToken);
        }
        await using (var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken))
            (await conn.ExecuteScalarAsync("SELECT 1", cancellationToken: TestContext.Current.CancellationToken)).Should().Be(1);
    }

    [Fact]
    [IssueLink("https://github.com/npgsql/npgsql/issues/927")]
    [IssueLink("https://github.com/npgsql/npgsql/issues/736")]
    public async Task Rollback_on_close()
    {
        if (IsMultiplexing)
            Assert.Skip("Not run with multiplexing");

        // CodeBrix.PostgresClient 3.0.0 to 3.0.4 prepended a rollback for the next time the connector is used, as an optimization.
        // This caused some issues (#927) and was removed.

        await using var dataSource = CreateDataSource();

        int processId;
        await using (var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken))
        {
            processId = conn.Connector.BackendProcessId;
            await conn.BeginTransactionAsync(TestContext.Current.CancellationToken);
            await conn.ExecuteNonQueryAsync("SELECT 1", cancellationToken: TestContext.Current.CancellationToken);
            conn.Connector.TransactionStatus.Should().Be(TransactionStatus.InTransactionBlock);
        }

        await using (var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken))
        {
            conn.Connector.BackendProcessId.Should().Be(processId);
            conn.Connector.TransactionStatus.Should().Be(TransactionStatus.Idle);
        }
    }

    // Tests an exception happening when sending the Terminate message while closing a ready connector
    [Fact]
    [IssueLink("https://github.com/npgsql/npgsql/issues/777")]
    public async Task exception_during_close()
    {
        // Pooling must be on to use multiplexing
        if (IsMultiplexing)
            return;

        await using var dataSource = CreateDataSource(csb => csb.Pooling = false);
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        var connectorId = conn.ProcessID;

        using (var conn2 = await OpenConnectionAsync())
            await conn2.ExecuteNonQueryAsync($"SELECT pg_terminate_backend({connectorId})", cancellationToken: TestContext.Current.CancellationToken);

        conn.Close();
    }

    // Some pseudo-PG database don't support pg_type loading, we have a minimal DatabaseInfo for this
    [Fact]
    public async Task no_type_loading()
    {
        await using var dataSource = CreateDataSource(builder => builder.ConfigureTypeLoading(builder => builder.EnableTypeLoading()));
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        (await conn.ExecuteScalarAsync("SELECT 8", cancellationToken: TestContext.Current.CancellationToken)).Should().Be(8);
        (await conn.ExecuteScalarAsync("SELECT 'foo'", cancellationToken: TestContext.Current.CancellationToken)).Should().Be("foo");
        (await conn.ExecuteScalarAsync("SELECT TRUE", cancellationToken: TestContext.Current.CancellationToken)).Should().Be(true);
        (await conn.ExecuteScalarAsync("SELECT INET '192.168.1.1'", cancellationToken: TestContext.Current.CancellationToken)).Should().Be(IPAddress.Parse("192.168.1.1"));

        ((int[])await conn.ExecuteScalarAsync("SELECT '{1,2,3}'::int[]", cancellationToken: TestContext.Current.CancellationToken)).Should().Equal(1, 2, 3);
        (await conn.ExecuteScalarAsync("SELECT '[1,10)'::int4range", cancellationToken: TestContext.Current.CancellationToken)).Should().Be(new PgSqlRange<int>(1, true, 10, false));

        if (conn.PostgreSqlVersion >= new Version(14, 0))
        {
            var multirangeArray = (PgSqlRange<int>[])(await conn.ExecuteScalarAsync("SELECT '{[3,7), (8,]}'::int4multirange", cancellationToken: TestContext.Current.CancellationToken));
            multirangeArray.Length.Should().Be(2);
            multirangeArray[0].Should().Be(new PgSqlRange<int>(3, true, false, 7, false, false));
            multirangeArray[1].Should().Be(new PgSqlRange<int>(9, true, false, 0, false, true));
        }
        else
        {
            using var cmd = new PgSqlCommand("SELECT $1", conn)
            {
                Parameters = { new() { Value = DBNull.Value, PgSqlDbType = PgSqlDbType.IntegerMultirange } }
            };

            (await cmd.Awaiting(c => c.ExecuteScalarAsync()).Should().ThrowExactlyAsync<NotSupportedException>())
                .Which.Message.Should().Be("The PgSqlDbType 'IntegerMultirange' isn't present in your database. You may need to install an extension or upgrade to a newer version.");
        }
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/1158")]
    public async Task table_named_record()
    {
        if (IsMultiplexing)
            Assert.Skip("Multiplexing, ReloadTypes");

        using var conn = await OpenConnectionAsync();
        await conn.ExecuteNonQueryAsync(@"

DROP TABLE IF EXISTS record;
CREATE TABLE record ()", cancellationToken: TestContext.Current.CancellationToken);
        try
        {
            conn.ReloadTypes();
            (await conn.ExecuteScalarAsync("SELECT COUNT(*) FROM record", cancellationToken: TestContext.Current.CancellationToken)).Should().Be(0L);
        }
        finally
        {
            await conn.ExecuteNonQueryAsync("DROP TABLE record", cancellationToken: TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task oversize_buffer()
    {
        if (IsMultiplexing)
            return;

        await using var dataSource = CreateDataSource();
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        var csb = new PgSqlConnectionStringBuilder(ConnectionString);

        conn.Connector.ReadBuffer.Size.Should().Be(csb.ReadBufferSize);

        // Read a big row, we should now be using an oversize buffer
        var bigString1 = new string('x', conn.Connector.ReadBuffer.Size + 1);
        using (var cmd = new PgSqlCommand($"SELECT '{bigString1}'", conn))
        using (var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken))
        {
            reader.Read();
            reader.GetString(0).Should().Be(bigString1);
        }
        var size1 = conn.Connector.ReadBuffer.Size;
        conn.Connector.ReadBuffer.Size.Should().BeGreaterThan(csb.ReadBufferSize);

        // Even bigger oversize buffer
        var bigString2 = new string('x', conn.Connector.ReadBuffer.Size + 1);
        using (var cmd = new PgSqlCommand($"SELECT '{bigString2}'", conn))
        using (var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken))
        {
            reader.Read();
            reader.GetString(0).Should().Be(bigString2);
        }
        conn.Connector.ReadBuffer.Size.Should().BeGreaterThan(size1);

        var processId = conn.ProcessID;
        conn.Close();
        conn.Open();
        conn.ProcessID.Should().Be(processId);
        conn.Connector.ReadBuffer.Size.Should().Be(csb.ReadBufferSize);
    }

    #region Keepalive

    // Turns on TCP keepalive and sleeps forever, good for wiresharking
    [Fact(Explicit = true)]
    public async Task tcp_keepalive_time()
    {
        await using var dataSource = CreateDataSource(csb => csb.TcpKeepAliveTime = 2);
        using (await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken))
            Thread.Sleep(Timeout.Infinite);
    }

    // Turns on TCP keepalive and sleeps forever, good for wiresharking
    [Fact(Explicit = true)]
    public async Task tcp_keepalive()
    {
        await using var dataSource = CreateDataSource(csb => csb.TcpKeepAlive = true);
        await using (await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken))
            Thread.Sleep(Timeout.Infinite);
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/3511")]
    public async Task keepalive_with_failed_transaction()
    {
        if (IsMultiplexing)
            return;

        await using var dataSource = CreateDataSource(csb => csb.KeepAlive = 1);
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using var tx = await conn.BeginTransactionAsync(TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<PostgresException>(async () => await conn.ExecuteScalarAsync("SELECT non_existent_table", cancellationToken: TestContext.Current.CancellationToken));
        // Connection is now in a failed transaction state. Wait a bit to allow for the keepalive to execute.
        Thread.Sleep(3000);

        await tx.RollbackAsync(TestContext.Current.CancellationToken);

        // Confirm that the connection is still open and usable
        (await conn.ExecuteScalarAsync("SELECT 1", cancellationToken: TestContext.Current.CancellationToken)).Should().Be(1);
    }

    #endregion Keepalive

    [Fact]
    public async Task change_parameter()
    {
        if (IsMultiplexing)
            return;

        using var conn = await OpenConnectionAsync();
        var defaultApplicationName = conn.PostgresParameters["application_name"];
        await conn.ExecuteNonQueryAsync("SET application_name = 'some_test_value'", cancellationToken: TestContext.Current.CancellationToken);
        conn.PostgresParameters["application_name"].Should().Be("some_test_value");
        await conn.ExecuteNonQueryAsync("SET application_name = 'some_test_value2'", cancellationToken: TestContext.Current.CancellationToken);
        conn.PostgresParameters["application_name"].Should().Be("some_test_value2");
        await conn.ExecuteNonQueryAsync($"SET application_name = '{defaultApplicationName}'", cancellationToken: TestContext.Current.CancellationToken);
        conn.PostgresParameters["application_name"].Should().Be(defaultApplicationName);
    }

    [Theory, IssueLink("https://github.com/npgsql/npgsql/issues/3030")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NoResetOnClose(bool noResetOnClose)
    {
        var originalApplicationName = new PgSqlConnectionStringBuilder(ConnectionString).ApplicationName ?? "";

        await using var dataSource = CreateDataSource(csb =>
        {
            csb.MaxPoolSize = 1;
            csb.NoResetOnClose = noResetOnClose;
        });

        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await conn.ExecuteNonQueryAsync("SET application_name = 'modified'", cancellationToken: TestContext.Current.CancellationToken);
        await conn.CloseAsync();
        await conn.OpenAsync(TestContext.Current.CancellationToken);
        (await conn.ExecuteScalarAsync("SHOW application_name", cancellationToken: TestContext.Current.CancellationToken)).Should().Be(
            noResetOnClose || IsMultiplexing
                ? "modified"
                : originalApplicationName);
    }

    // Test whether the internal PgSqlConnection.Open method stays on the same thread with async=false
    [Fact]
    public async Task sync_open_blocked_same_thread()
    {
        if (IsMultiplexing)
            return;

        await using var dataSource = CreateDataSource(csb =>
        {
            csb.MaxPoolSize = 1;
        });

        await using var openConnection = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        // 2 tasks are usually enough to reproduce the issue
        const int taskCount = 2;

        var tcs = new TaskCompletionSource<object>[taskCount];
        for (var i = 0; i < tcs.Length; i++)
        {
            tcs[i] = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        var sameThreadTasks = Enumerable.Range(0, taskCount).Select(x => Task.Run(async () =>
        {
            var beforeOpenThread = Thread.CurrentThread;
            tcs[x].SetResult(null);
            using var conn = dataSource.CreateConnection();
            // even though we await it should complete synchronously due to async = false
            await conn.Open(async: false, TestContext.Current.CancellationToken);
            return beforeOpenThread == Thread.CurrentThread;
        })).ToList();

        await Task.WhenAll(tcs.Select(x => x.Task));
        // Just in case give them a second to block on getting a connection from the pool
        await Task.Delay(1000, cancellationToken: TestContext.Current.CancellationToken);
        await openConnection.CloseAsync();

        foreach (var sameThreadTask in sameThreadTasks)
        {
            (await sameThreadTask).Should().BeTrue("Synchronous open completed on different thread");
        }
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/6427")]
    public async Task gss_encryption_retry_does_not_clear_pool()
    {
        // Hangs on linux and mac (probably because of missing kerberos token)
        if (!OperatingSystem.IsWindows())
            Assert.Skip("Windows-only");

        if (IsMultiplexing)
            return;

        var csb = new PgSqlConnectionStringBuilder(ConnectionString)
        {
            GssEncryptionMode = GssEncryptionMode.Prefer,
            NoResetOnClose = false
        };
        // Break connection on gss encryption request to force the client to create a new connection and retry again
        // This emulates the behavior of older versions of PostgreSQL or its forks, like Supabase
        await using var postmaster = PgPostmasterMock.Start(csb.ConnectionString, breakOnGssEncryptionRequest: true);
        await using var dataSource = CreateDataSource(builder =>
        {
            builder.ConnectionStringBuilder.ConnectionString = postmaster.ConnectionString;
            // We use kerberos by default, which requires specific credentials to work
            // Change it negotiate so SSPI on windows can use NTLM credentials
            builder.UseNegotiateOptionsCallback(options => options.Package = "Negotiate");
        });

        PgServerMock server;

        int processID;
        await using (var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken))
        {
            processID = conn.ProcessID;

            // The next connection request isn't valid because it was retried
            await postmaster.SkipNextConnection();

            var queryTask = conn.ExecuteNonQueryAsync("SELECT 1", cancellationToken: TestContext.Current.CancellationToken);

            server = await postmaster.WaitForServerConnection();
            await server.ExpectExtendedQuery();
            await server.WriteScalarResponseAndFlush(1);
            await queryTask;
        }

        // The second time we get a connection from the pool we should ge the exact same connection
        await using (var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken))
        {
            conn.ProcessID.Should().Be(processID);

            var queryTask = conn.ExecuteNonQueryAsync("SELECT 1", cancellationToken: TestContext.Current.CancellationToken);

            // We do not set NoResetOnClose=true on connection string to test query behavior after connection retry
            await server.ExpectSimpleQuery("DISCARD ALL");
            await server.ExpectExtendedQuery();
            server
                .WriteCommandComplete()
                .WriteReadyForQuery();
            await server.WriteScalarResponseAndFlush(1);
            await queryTask;
        }
    }

    #region Physical connection initialization

    [Fact]
    public async Task PhysicalConnectionInitializer_sync()
    {
        if (IsMultiplexing) // Sync I/O
            return;

        await using var adminConn = await OpenConnectionAsync();
        var table = await CreateTempTable(adminConn, "ID INTEGER");

        var dataSourceBuilder = CreateDataSourceBuilder();
        dataSourceBuilder.UsePhysicalConnectionInitializer(
            conn => conn.ExecuteNonQuery($"INSERT INTO {table} VALUES (1)"),
            _ => throw new NotSupportedException());
        await using var dataSource = dataSourceBuilder.Build();

        await using (var conn = dataSource.OpenConnection())
        {
            (await conn.ExecuteScalarAsync($"SELECT COUNT(*) FROM \"{table}\"", cancellationToken: TestContext.Current.CancellationToken)).Should().Be(1L);
        }

        // Opening a second time should get us an idle connection, which should not cause the initializer to get executed
        await using (var conn = dataSource.OpenConnection())
        {
            (await conn.ExecuteScalarAsync($"SELECT COUNT(*) FROM \"{table}\"", cancellationToken: TestContext.Current.CancellationToken)).Should().Be(1L);
        }
    }

    [Fact]
    public async Task PhysicalConnectionInitializer_async()
    {
        // With multiplexing the connector might become idle at undetermined point after the query is executed.
        // Which is why we ignore it.
        if (IsMultiplexing)
            return;

        await using var adminConn = await OpenConnectionAsync();
        var table = await CreateTempTable(adminConn, "ID INTEGER");

        var dataSourceBuilder = CreateDataSourceBuilder();
        dataSourceBuilder.UsePhysicalConnectionInitializer(
            _ => throw new NotSupportedException(),
            async conn => await conn.ExecuteNonQueryAsync($"INSERT INTO {table} VALUES (1)"));
        await using var dataSource = dataSourceBuilder.Build();

        await using (var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken))
        {
            (await conn.ExecuteScalarAsync($"SELECT COUNT(*) FROM \"{table}\"", cancellationToken: TestContext.Current.CancellationToken)).Should().Be(1L);
        }

        // Opening a second time should get us an idle connection, which should not cause the initializer to get executed
        await using (var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken))
        {
            (await conn.ExecuteScalarAsync($"SELECT COUNT(*) FROM \"{table}\"", cancellationToken: TestContext.Current.CancellationToken)).Should().Be(1L);
        }
    }

    [Fact]
    public async Task PhysicalConnectionInitializer_sync_with_break()
    {
        if (IsMultiplexing) // Sync I/O
            return;

        var dataSourceBuilder = CreateDataSourceBuilder();
        dataSourceBuilder.UsePhysicalConnectionInitializer(
            conn =>
            {
                // Use another connection to kill the connector currently in the pool
                using (var conn2 = OpenConnection())
                    conn2.ExecuteNonQuery($"SELECT pg_terminate_backend({conn.ProcessID})");

                conn.ExecuteScalar("SELECT 1");
            },
            _ => throw new NotSupportedException());
        await using var dataSource = dataSourceBuilder.Build();

        dataSource.Invoking(d => d.OpenConnection()).Should().Throw<PgSqlException>();
        dataSource.Statistics.Should().Be((0, 0, 0));
    }

    [Fact]
    public async Task PhysicalConnectionInitializer_async_with_break()
    {
        var dataSourceBuilder = CreateDataSourceBuilder();
        dataSourceBuilder.UsePhysicalConnectionInitializer(
            _ => throw new NotSupportedException(),
            async conn =>
            {
                // Use another connection to kill the connector currently in the pool
                await using (var conn2 = await OpenConnectionAsync())
                    await conn2.ExecuteNonQueryAsync($"SELECT pg_terminate_backend({conn.ProcessID})");

                await conn.ExecuteScalarAsync("SELECT 1");
            });
        await using var dataSource = dataSourceBuilder.Build();

        await dataSource.Awaiting(d => d.OpenConnectionAsync()).Should().ThrowAsync<PgSqlException>();
        dataSource.Statistics.Should().Be((0, 0, 0));
    }

    [Fact]
    public async Task PhysicalConnectionInitializer_async_throws_on_second_open()
    {
        // With multiplexing a physical connection might open on PgSqlConnection.OpenAsync (if there was no completed bootstrap beforehand)
        // or on PgSqlCommand.ExecuteReaderAsync.
        // We've already tested the first case in PhysicalConnectionInitializer_async_throws above, testing the second one below.
        var count = 0;
        var dataSourceBuilder = CreateDataSourceBuilder();
        dataSourceBuilder.UsePhysicalConnectionInitializer(
            _ => throw new NotSupportedException(),
            _ =>
            {
                if (++count == 1)
                    return Task.CompletedTask;
                throw new Exception("INTENTIONAL FAILURE");
            });
        await using var dataSource = dataSourceBuilder.Build();

        await using var conn1 = dataSource.CreateConnection();
        await conn1.Awaiting(c => c.OpenAsync()).Should().NotThrowAsync();

        // We start a transaction specifically for multiplexing (to bind a connector to the connection)
        await using var tx = await conn1.BeginTransactionAsync(TestContext.Current.CancellationToken);

        await using var conn2 = dataSource.CreateConnection();
        Exception exception;
        if (IsMultiplexing)
        {
            await conn2.OpenAsync(TestContext.Current.CancellationToken);
            exception = await Assert.ThrowsAsync<Exception>(async () => await conn2.BeginTransactionAsync(TestContext.Current.CancellationToken));
        }
        else
            exception = await Assert.ThrowsAsync<Exception>(async () => await conn2.OpenAsync(TestContext.Current.CancellationToken));
        exception.Message.Should().Be("INTENTIONAL FAILURE");
    }

    [Fact]
    public async Task PhysicalConnectionInitializer_disposes_connection()
    {
        PgSqlConnection initializerConnection = null;

        var dataSourceBuilder = CreateDataSourceBuilder();
        dataSourceBuilder.UsePhysicalConnectionInitializer(
            _ => throw new NotSupportedException(),
            conn =>
            {
                initializerConnection = conn;
                return Task.CompletedTask;
            });
        await using var dataSource = dataSourceBuilder.Build();

        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        initializerConnection.Should().NotBeNull();
        conn.Should().NotBeSameAs(initializerConnection);
        initializerConnection.Invoking(c => c.Open()).Should().ThrowExactly<ObjectDisposedException>();
    }

    #endregion Physical connection initialization

    #region Require auth

    [Fact]
    public async Task connect_with_any_auth()
    {
        await using var dataSource = CreateDataSource(csb =>
        {
            csb.RequireAuth = $"{RequireAuthMode.Password},{RequireAuthMode.MD5},{RequireAuthMode.GSS},{RequireAuthMode.SSPI},{RequireAuthMode.ScramSHA256},{RequireAuthMode.None}";
        });
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task connect_with_any_except_none_auth()
    {
        await using var dataSource = CreateDataSource(csb =>
        {
            csb.RequireAuth = $"!{RequireAuthMode.None}";
        });
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task fail_connect_with_none_auth()
    {
        await using var dataSource = CreateDataSource(csb =>
        {
            csb.RequireAuth = $"{RequireAuthMode.None}";
        });
        var ex = await Assert.ThrowsAsync<PgSqlException>(async () => await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken));
        ex.Message.Should().Contain("authentication method is not allowed");
    }

    [Fact]
    public async Task connect_with_md5_auth()
    {
        await using var dataSource = CreateDataSource(csb =>
        {
            csb.RequireAuth = $"{RequireAuthMode.MD5}";
        });
        try
        {
            await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        }
        catch (Exception e) when (!IsOnBuildServer)
        {
            Console.WriteLine(e);
            Assert.Skip("MD5 authentication doesn't seem to be set up");
        }
    }

    [Theory]
    [InlineData($"{nameof(RequireAuthMode.ScramSHA256)},!{nameof(RequireAuthMode.None)}")]
    [InlineData($"!{nameof(RequireAuthMode.ScramSHA256)},{nameof(RequireAuthMode.None)}")]
    public void mixed_auth_methods_not_supported(string authMethods)
    {
        //Arrange
        var csb = new PgSqlConnectionStringBuilder();

        //Assert
        Assert.Throws<ArgumentException>(() => csb.RequireAuth = authMethods);
    }

    [Fact]
    public void remove_all_auth_methods_throws()
    {
        //Arrange
        var csb = new PgSqlConnectionStringBuilder();

        //Assert
        Assert.Throws<ArgumentException>(() =>
            csb.RequireAuth = $"!{RequireAuthMode.Password},!{RequireAuthMode.MD5},!{RequireAuthMode.GSS},!{RequireAuthMode.SSPI},!{RequireAuthMode.ScramSHA256},!{RequireAuthMode.None}");
    }

    [Fact]
    public void unknown_auth_method_throws()
    {
        //Arrange
        var csb = new PgSqlConnectionStringBuilder();

        //Assert
        Assert.Throws<ArgumentException>(() => csb.RequireAuth = "SuperSecure");
    }

    [Fact]
    public void auth_methods_are_trimmed()
    {
        //Act
        var csb = new PgSqlConnectionStringBuilder
        {
            RequireAuth = $"{RequireAuthMode.Password} , {RequireAuthMode.MD5}"
        };

        //Assert
        csb.RequireAuthModes.Should().Be(RequireAuthMode.Password | RequireAuthMode.MD5);
    }

    #endregion Require auth

    #region Logging tests

    [Fact]
    public async Task log_Open_Close_pooled()
    {
        await using var dataSource = CreateLoggingDataSource(out var listLoggerProvider);
        await using var conn = dataSource.CreateConnection();

        // Open and close to have an idle connection in the pool - we don't want to test physical open/close
        await conn.OpenAsync(TestContext.Current.CancellationToken);
        await conn.CloseAsync();

        int processId, port;
        string host, database;
        using (listLoggerProvider.Record())
        {
            await conn.OpenAsync(TestContext.Current.CancellationToken);

            var tx = await conn.BeginTransactionAsync(TestContext.Current.CancellationToken);
            (processId, host, port, database) = (conn.ProcessID, conn.Host, conn.Port, conn.Database);
            await tx.CommitAsync(TestContext.Current.CancellationToken);

            await conn.CloseAsync();
        }

        var openingConnectionEvent = listLoggerProvider.Log.Single(l => l.Id == PgSqlEventId.OpeningConnection);
        AssertConnectionStringNotLogged(openingConnectionEvent.State);
        AssertLoggingStateContains(openingConnectionEvent, "Host", host);
        AssertLoggingStateContains(openingConnectionEvent, "Port", port);
        AssertLoggingStateContains(openingConnectionEvent, "Database", database);

        var openedConnectionEvent = listLoggerProvider.Log.Single(l => l.Id == PgSqlEventId.OpenedConnection);
        AssertConnectionStringNotLogged(openedConnectionEvent.State);
        AssertLoggingStateContains(openedConnectionEvent, "Host", host);
        AssertLoggingStateContains(openedConnectionEvent, "Port", port);
        AssertLoggingStateContains(openedConnectionEvent, "Database", database);

        var closingConnectionEvent = listLoggerProvider.Log.Single(l => l.Id == PgSqlEventId.ClosingConnection);
        AssertConnectionStringNotLogged(closingConnectionEvent.State);
        AssertLoggingStateContains(closingConnectionEvent, "Host", host);
        AssertLoggingStateContains(closingConnectionEvent, "Port", port);
        AssertLoggingStateContains(closingConnectionEvent, "Database", database);

        var closedConnectionEvent = listLoggerProvider.Log.Single(l => l.Id == PgSqlEventId.ClosedConnection);
        AssertConnectionStringNotLogged(closedConnectionEvent.State);
        AssertLoggingStateContains(closedConnectionEvent, "Host", host);
        AssertLoggingStateContains(closedConnectionEvent, "Port", port);
        AssertLoggingStateContains(closedConnectionEvent, "Database", database);

        if (!IsMultiplexing)
        {
            AssertLoggingStateContains(openedConnectionEvent, "ConnectorId", processId);
            AssertLoggingStateContains(closingConnectionEvent, "ConnectorId", processId);
            AssertLoggingStateContains(closedConnectionEvent, "ConnectorId", processId);
        }

        var ids = new[]
        {
            PgSqlEventId.OpeningPhysicalConnection,
            PgSqlEventId.OpenedPhysicalConnection,
            PgSqlEventId.ClosingPhysicalConnection,
            PgSqlEventId.ClosedPhysicalConnection
        };

        foreach (var id in ids)
            listLoggerProvider.Log.Count(l => l.Id == id).Should().Be(0);
    }

    [Fact]
    public async Task log_Open_Close_physical()
    {
        if (IsMultiplexing)
            return;

        var csb = new PgSqlConnectionStringBuilder(ConnectionString) { Pooling = false };
        await using var dataSource = CreateLoggingDataSource(out var listLoggerProvider, csb.ToString());
        await using var conn = dataSource.CreateConnection();

        int processId, port;
        string host, database;
        using (listLoggerProvider.Record())
        {
            await conn.OpenAsync(TestContext.Current.CancellationToken);
            (processId, host, port, database) = (conn.ProcessID, conn.Host, conn.Port, conn.Database);
            await conn.CloseAsync();
        }

        var openingConnectionEvent = listLoggerProvider.Log.Single(l => l.Id == PgSqlEventId.OpeningPhysicalConnection);
        AssertConnectionStringNotLogged(openingConnectionEvent.State);
        AssertLoggingStateContains(openingConnectionEvent, "Host", host);
        AssertLoggingStateContains(openingConnectionEvent, "Port", port);
        AssertLoggingStateContains(openingConnectionEvent, "Database", database);

        var openedConnectionEvent = listLoggerProvider.Log.Single(l => l.Id == PgSqlEventId.OpenedPhysicalConnection);
        AssertConnectionStringNotLogged(openedConnectionEvent.State);
        AssertLoggingStateContains(openedConnectionEvent, "ConnectorId", processId);
        AssertLoggingStateContains(openingConnectionEvent, "Host", host);
        AssertLoggingStateContains(openingConnectionEvent, "Port", port);
        AssertLoggingStateContains(openingConnectionEvent, "Database", database);
        AssertLoggingStateContains(openedConnectionEvent, "DurationMs");

        var closingConnectionEvent = listLoggerProvider.Log.Single(l => l.Id == PgSqlEventId.ClosingPhysicalConnection);
        AssertConnectionStringNotLogged(closingConnectionEvent.State);
        AssertLoggingStateContains(closingConnectionEvent, "ConnectorId", processId);
        AssertLoggingStateContains(closingConnectionEvent, "Host", host);
        AssertLoggingStateContains(closingConnectionEvent, "Port", port);
        AssertLoggingStateContains(closingConnectionEvent, "Database", database);

        var closededConnectionEvent = listLoggerProvider.Log.Single(l => l.Id == PgSqlEventId.ClosedPhysicalConnection);
        AssertConnectionStringNotLogged(closededConnectionEvent.State);
        AssertLoggingStateContains(closededConnectionEvent, "ConnectorId", processId);
        AssertLoggingStateContains(closededConnectionEvent, "Host", host);
        AssertLoggingStateContains(closededConnectionEvent, "Port", port);
        AssertLoggingStateContains(closededConnectionEvent, "Database", database);
    }

    // CodeBrix.PostgresClient deliberately never passes the connection string to the logger (upstream logged it
    // as a structured value with the password removed); the connection events identify the server by
    // host, port and database, and the connector by its id.
    static void AssertConnectionStringNotLogged(object logState)
    {
        var keyValuePairs = (IEnumerable<KeyValuePair<string, object>>)logState;
        keyValuePairs.Select(kvp => kvp.Key).Should().NotContain("ConnectionString");
    }

    #endregion Logging tests
}

public sealed class ConnectionTests_NonMultiplexing() : ConnectionTests(MultiplexingMode.NonMultiplexing);
public sealed class ConnectionTests_Multiplexing() : ConnectionTests(MultiplexingMode.Multiplexing);

// These tests set environment variables, change server-wide objects or global factories, so they must
// not run in parallel with any other test
[Collection(NonParallelCollection.Name)]
public abstract class ConnectionTestsNonParallel(MultiplexingMode multiplexingMode) : MultiplexingTestBase(multiplexingMode)
{
    // Sets environment variable
    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/1065")]
    public async Task client_encoding_env_var()
    {
        using (var testConn = await OpenConnectionAsync())
            (await testConn.ExecuteScalarAsync("SHOW client_encoding", cancellationToken: TestContext.Current.CancellationToken)).Should().NotBe("SQL_ASCII");

        // Note that the pool is unaware of the environment variable, so if a connection is
        // returned from the pool it may contain the wrong client_encoding
        using var _ = SetEnvironmentVariable("PGCLIENTENCODING", "SQL_ASCII");
        await using var dataSource = CreateDataSource();
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        (await conn.ExecuteScalarAsync("SHOW client_encoding", cancellationToken: TestContext.Current.CancellationToken)).Should().Be("SQL_ASCII");
    }

    // Sets environment variable
    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/1634")]
    public async Task timezone_env_var()
    {
        string newTimezone;
        using (var conn1 = await OpenConnectionAsync())
        {
            newTimezone = (string)await conn1.ExecuteScalarAsync("SHOW TIMEZONE", cancellationToken: TestContext.Current.CancellationToken) == "Africa/Bamako"
                ? "Africa/Lagos"
                : "Africa/Bamako";
        }

        // Note that the pool is unaware of the environment variable, so if a connection is
        // returned from the pool it may contain the wrong timezone
        using var _ = SetEnvironmentVariable("PGTZ", newTimezone);
        await using var dataSource = CreateDataSource();
        using var conn2 = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        (await conn2.ExecuteScalarAsync("SHOW TIMEZONE", cancellationToken: TestContext.Current.CancellationToken)).Should().Be(newTimezone);
    }

    // Sets environment variable
    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/6133")]
    public async Task application_name_env_var()
    {
        const string testAppName = "MyTestApp";

        // Note that the pool is unaware of the environment variable, so if a connection is
        // returned from the pool it may contain the wrong application name
        using var _ = SetEnvironmentVariable("PGAPPNAME", testAppName);
        await using var dataSource = CreateDataSource();
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        conn.PostgresParameters["application_name"].Should().Be(testAppName);
    }

    // Sets environment variable
    [Fact]
    public async Task application_name_connection_param_overrides_env_var()
    {
        const string envAppName = "EnvApp";
        const string connAppName = "ConnApp";

        using var _ = SetEnvironmentVariable("PGAPPNAME", envAppName);
        await using var dataSource = CreateDataSource(csb => csb.ApplicationName = connAppName);
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        conn.PostgresParameters["application_name"].Should().Be(connAppName);
    }

    [Theory]
    [InlineData("test_schema_1", "public", true)]
    [InlineData("test_schema_1", "test_schema_2", true)]
    [InlineData("test_schema_2", "test_schema_3", true)]
    [InlineData("test_schema_1", "public", false)]
    [InlineData("test_schema_1", "test_schema_2", false)]
    [InlineData("test_schema_2", "test_schema_3", false)]
    [InlineData("'DROP TABLE X", "'COMMIT;  ", false)]
    public async Task set_schemas_and_load_relevant_types(string testSchema, string otherSchema, bool enabled)
    {
        if (IsMultiplexing)
            return;

        await using var conn1 = await OpenConnectionAsync();
        try
        {
            await conn1.ExecuteNonQueryAsync("DROP TYPE IF EXISTS public.test_type_1", cancellationToken: TestContext.Current.CancellationToken);
            await conn1.ExecuteNonQueryAsync("DROP TYPE IF EXISTS public.test_type_2", cancellationToken: TestContext.Current.CancellationToken);
            await conn1.ExecuteNonQueryAsync("DROP TYPE IF EXISTS public.test_type_3", cancellationToken: TestContext.Current.CancellationToken);
            await conn1.ExecuteNonQueryAsync("CREATE TYPE public.test_type_3 AS (id int, name text)", cancellationToken: TestContext.Current.CancellationToken);

            if (testSchema != "public")
            {
                await conn1.ExecuteNonQueryAsync($"DROP SCHEMA IF EXISTS \"{testSchema}\" CASCADE", cancellationToken: TestContext.Current.CancellationToken);
                await conn1.ExecuteNonQueryAsync($"CREATE SCHEMA \"{testSchema}\"", cancellationToken: TestContext.Current.CancellationToken);
            }

            if (otherSchema != "public")
            {
                await conn1.ExecuteNonQueryAsync($"DROP SCHEMA IF EXISTS \"{otherSchema}\" CASCADE", cancellationToken: TestContext.Current.CancellationToken);
                await conn1.ExecuteNonQueryAsync($"CREATE SCHEMA \"{otherSchema}\"", cancellationToken: TestContext.Current.CancellationToken);
            }

            await conn1.ExecuteNonQueryAsync($"DROP TYPE IF EXISTS \"{testSchema}\".test_type_1", cancellationToken: TestContext.Current.CancellationToken);
            await conn1.ExecuteNonQueryAsync($"CREATE TYPE \"{testSchema}\".test_type_1 AS (id int)", cancellationToken: TestContext.Current.CancellationToken);
            await conn1.ExecuteNonQueryAsync($"DROP TYPE IF EXISTS \"{otherSchema}\".test_type_2", cancellationToken: TestContext.Current.CancellationToken);
            await conn1.ExecuteNonQueryAsync($"CREATE TYPE \"{otherSchema}\".test_type_2 AS (id int, name text)", cancellationToken: TestContext.Current.CancellationToken);

            using var dataSource = CreateDataSource(builder =>
            {
                builder.ConfigureTypeLoading(builder =>
                {
                    if (enabled)
                        builder.SetTypeLoadingSchemas(testSchema, otherSchema);
                });
            });
            using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
            var databaseInfo = dataSource.CurrentReloadableState.DatabaseInfo;
            if (enabled)
            {
                databaseInfo.CompositeTypes.Any(x => x.Name == "test_type_1").Should().BeTrue();
                if (testSchema == "public" || otherSchema == "public")
                {
                    databaseInfo.CompositeTypes.Any(x => x.Name == "test_type_2").Should().BeTrue();
                    databaseInfo.CompositeTypes.Any(x => x.Name == "test_type_3").Should().BeTrue();
                }
                else
                {
                    databaseInfo.CompositeTypes.Any(x => x.Name == "test_type_2").Should().BeTrue();
                    databaseInfo.CompositeTypes.Any(x => x.Name == "test_type_3").Should().BeFalse();
                }
            }
            else
            {
                databaseInfo.CompositeTypes.Any(x => x.Name == "test_type_1").Should().BeTrue();
                databaseInfo.CompositeTypes.Any(x => x.Name == "test_type_2").Should().BeTrue();
                databaseInfo.CompositeTypes.Any(x => x.Name == "test_type_3").Should().BeTrue();
            }
        }
        finally
        {
            if (testSchema != "public")
                await conn1.ExecuteNonQueryAsync($"DROP SCHEMA IF EXISTS \"{testSchema}\" CASCADE", cancellationToken: TestContext.Current.CancellationToken);
            if (otherSchema != "public")
                await conn1.ExecuteNonQueryAsync($"DROP SCHEMA IF EXISTS \"{otherSchema}\" CASCADE", cancellationToken: TestContext.Current.CancellationToken);
        }

    }

    // Drops and creates same database across modes
    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/392")]
    public async Task non_UTF8_encoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        await using var adminConn = await OpenConnectionAsync();

        // Create the database with server encoding sql-ascii
        // Starting with PG16, the default locale provider is icu, which does not support encoding sql_ascii. Specify libc explicitly as the
        // locale provider (except for older versions where specifying explicitly isn't supported, and libc is the only possibility).
        await adminConn.ExecuteNonQueryAsync("DROP DATABASE IF EXISTS sqlascii", cancellationToken: TestContext.Current.CancellationToken);
        await adminConn.ExecuteNonQueryAsync(
            adminConn.PostgreSqlVersion >= new Version(15, 0)
                ? "CREATE DATABASE sqlascii ENCODING 'sql_ascii' LOCALE_PROVIDER libc TEMPLATE template0"
                : "CREATE DATABASE sqlascii ENCODING 'sql_ascii' TEMPLATE template0", cancellationToken: TestContext.Current.CancellationToken);

        try
        {
            // Insert some win1252 data
            await using var goodDataSource = CreateDataSource(csb =>
            {
                csb.Database = "sqlascii";
                csb.Encoding = "windows-1252";
                csb.ClientEncoding = "sql-ascii";
            });

            await using (var conn = await goodDataSource.OpenConnectionAsync(TestContext.Current.CancellationToken))
            {
                const string value = "éàç";
                await conn.ExecuteNonQueryAsync("CREATE TABLE foo (bar TEXT)", cancellationToken: TestContext.Current.CancellationToken);
                await conn.ExecuteNonQueryAsync($"INSERT INTO foo (bar) VALUES ('{value}')", cancellationToken: TestContext.Current.CancellationToken);

                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT * FROM foo";
                await using var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
                (await reader.ReadAsync(TestContext.Current.CancellationToken)).Should().BeTrue();

                using (var textReader = await reader.GetTextReaderAsync(0, cancellationToken: TestContext.Current.CancellationToken))
                    textReader.ReadToEnd().Should().Be(value);
                reader.GetString(0).Should().Be(value);
            }

            // A normal connection with the default UTF8 encoding and client_encoding should fail
            await using var badDataSource = CreateDataSource(csb => csb.Database = "sqlascii");
            await using (var conn = await badDataSource.OpenConnectionAsync(TestContext.Current.CancellationToken))
            {
                var ex = await Record.ExceptionAsync(() => conn.ExecuteScalarAsync("SELECT * FROM foo", cancellationToken: TestContext.Current.CancellationToken));
                (ex is PostgresException { SqlState: PostgresErrorCodes.CharacterNotInRepertoire } || ex?.GetType() == typeof(DecoderFallbackException))
                    .Should().BeTrue($"a PostgresException with SqlState {PostgresErrorCodes.CharacterNotInRepertoire} or a DecoderFallbackException was expected, but got {ex}");
            }
        }
        finally
        {
            await adminConn.ExecuteNonQueryAsync("DROP DATABASE IF EXISTS sqlascii", cancellationToken: TestContext.Current.CancellationToken);
        }
    }

    // Sets environment variable
    [Fact]
    public async Task connect_options_from_environment_succeeds()
    {
        using (SetEnvironmentVariable("PGOPTIONS", "-c default_transaction_isolation=serializable -c default_transaction_deferrable=on -c foo.bar=My\\ Famous\\\\Thing"))
        {
            await using var dataSource = CreateDataSource();
            await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
            (await conn.ExecuteScalarAsync("SHOW default_transaction_isolation", cancellationToken: TestContext.Current.CancellationToken)).Should().Be("serializable");
            (await conn.ExecuteScalarAsync("SHOW default_transaction_deferrable", cancellationToken: TestContext.Current.CancellationToken)).Should().Be("on");
            (await conn.ExecuteScalarAsync("SHOW foo.bar", cancellationToken: TestContext.Current.CancellationToken)).Should().Be("My Famous\\Thing");
        }
    }

    // Sets environment variable
    [Fact]
    public async Task connect_with_any_auth_env()
    {
        using var _ = SetEnvironmentVariable("PGREQUIREAUTH", $"{RequireAuthMode.Password},{RequireAuthMode.MD5},{RequireAuthMode.GSS},{RequireAuthMode.SSPI},{RequireAuthMode.ScramSHA256},{RequireAuthMode.None}");
        await using var dataSource = CreateDataSource();
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
    }

    // Sets environment variable
    [Fact]
    public async Task connect_with_any_except_none_auth_env()
    {
        using var _ = SetEnvironmentVariable("PGREQUIREAUTH", $"!{RequireAuthMode.None}");
        await using var dataSource = CreateDataSource();
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
    }

    // Sets environment variable
    [Fact]
    public async Task fail_connect_with_none_auth_env()
    {
        using var _ = SetEnvironmentVariable("PGREQUIREAUTH", $"{RequireAuthMode.None}");
        await using var dataSource = CreateDataSource();
        var ex = await Assert.ThrowsAsync<PgSqlException>(async () => await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken));
        ex.Message.Should().Contain("authentication method is not allowed");
    }

    // Sets environment variable
    [Fact]
    public async Task connect_with_md5_auth_env()
    {
        using var _ = SetEnvironmentVariable("PGREQUIREAUTH", $"{RequireAuthMode.MD5}");
        await using var dataSource = CreateDataSource();
        try
        {
            await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        }
        catch (Exception e) when (!IsOnBuildServer)
        {
            Console.WriteLine(e);
            Assert.Skip("MD5 authentication doesn't seem to be set up");
        }
    }

    // Modifies global database info factories
    [Fact]
    [IssueLink("https://github.com/npgsql/npgsql/issues/4425")]
    public async Task breaking_connection_while_loading_database_info()
    {
        if (IsMultiplexing)
            return;

        await using var dataSource = CreateDataSource();

        await using var firstConn = dataSource.CreateConnection();
        PgSqlDatabaseInfo.RegisterFactory(new BreakingDatabaseInfoFactory());
        try
        {
            // Test the first time we load the database info
            await Assert.ThrowsAsync<IOException>(() => firstConn.OpenAsync(TestContext.Current.CancellationToken));
        }
        finally
        {
            PgSqlDatabaseInfo.ResetFactories();
        }

        await firstConn.OpenAsync(TestContext.Current.CancellationToken);
        await using var secondConn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await secondConn.CloseAsync();
        await firstConn.ReloadTypesAsync(TestContext.Current.CancellationToken);

        PgSqlDatabaseInfo.RegisterFactory(new BreakingDatabaseInfoFactory());
        try
        {
            // Make sure that the database info is now cached and won't be reloaded
            await secondConn.Awaiting(c => c.OpenAsync()).Should().NotThrowAsync();
        }
        finally
        {
            PgSqlDatabaseInfo.ResetFactories();
        }
    }

    class BreakingDatabaseInfoFactory : IPgSqlDatabaseInfoFactory
    {
        public Task<PgSqlDatabaseInfo> Load(PgSqlConnector conn, PgSqlTimeout timeout, bool async)
            => throw conn.Break(new IOException());
    }
}

public sealed class ConnectionTestsNonParallel_NonMultiplexing() : ConnectionTestsNonParallel(MultiplexingMode.NonMultiplexing);
public sealed class ConnectionTestsNonParallel_Multiplexing() : ConnectionTestsNonParallel(MultiplexingMode.Multiplexing);
