using System;
using System.Data;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.PostgresClient.Properties;
using CodeBrix.PostgresClient.Tests.Support;
using SilverAssertions;
using Xunit;
using static CodeBrix.PostgresClient.Tests.TestUtil;

namespace CodeBrix.PostgresClient.Tests; //was previously: Npgsql.Tests;

public abstract class AuthenticationTests(MultiplexingMode multiplexingMode) : MultiplexingTestBase(multiplexingMode)
{
    [Fact]
    public async Task set_Password_on_PgSqlDataSource()
    {
        //Arrange
        var dataSourceBuilder = GetPasswordlessDataSourceBuilder();
        await using var dataSource = dataSourceBuilder.Build();

        // No password provided
        var act = async () => await dataSource.OpenConnectionAsync();
        await act.Should().ThrowExactlyAsync<PgSqlException>();

        var connectionStringBuilder = new PgSqlConnectionStringBuilder(TestUtil.ConnectionString);

        //Act
        dataSource.Password = connectionStringBuilder.Password;

        //Assert
        await using var connection1 = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using var connection2 = dataSource.OpenConnection();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task password_provider(bool async)
    {
        //Arrange
        var dataSourceBuilder = GetPasswordlessDataSourceBuilder();
        var password = new PgSqlConnectionStringBuilder(TestUtil.ConnectionString).Password;
        var syncProviderCalled = false;
        var asyncProviderCalled = false;
        dataSourceBuilder.UsePasswordProvider(_ =>
        {
            syncProviderCalled = true;
            return password;
        }, (_,_) =>
        {
            asyncProviderCalled = true;
            return new(password);
        });

        //Act
        using var dataSource = dataSourceBuilder.Build();
        using var conn = async ? await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken) : dataSource.OpenConnection();

        //Assert
        (async ? asyncProviderCalled : syncProviderCalled).Should().BeTrue("Password_provider not used");
    }

    [Fact]
    public async Task password_provider_exception()
    {
        //Arrange
        var dataSourceBuilder = GetPasswordlessDataSourceBuilder();
        dataSourceBuilder.UsePasswordProvider(_ => throw new Exception(), (_,_) => throw new Exception());

        //Act
        using var dataSource = dataSourceBuilder.Build();

        //Assert
        await Assert.ThrowsAsync<PgSqlException>(async () => await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task periodic_password_provider()
    {
        //Arrange
        var dataSourceBuilder = GetPasswordlessDataSourceBuilder();
        var password = new PgSqlConnectionStringBuilder(TestUtil.ConnectionString).Password;

        var mre = new ManualResetEvent(false);
        dataSourceBuilder.UsePeriodicPasswordProvider((_, _) =>
        {
            mre.Set();
            return new(password);
        }, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(10));

        //Assert
        await using (var dataSource = dataSourceBuilder.Build())
        {
            await using var connection1 = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
            await using var connection2 = dataSource.OpenConnection();

            mre.Reset();
            if (!mre.WaitOne(TimeSpan.FromSeconds(30)))
                Assert.Fail("Periodic password refresh did not occur");
        }

        mre.Reset();
        if (mre.WaitOne(TimeSpan.FromSeconds(1)))
            Assert.Fail("Periodic password refresh occurred after disposal of the data source");
    }

    [Fact]
    public async Task periodic_password_provider_with_first_time_exception()
    {
        //Arrange
        var dataSourceBuilder = GetPasswordlessDataSourceBuilder();
        dataSourceBuilder.UsePeriodicPasswordProvider(
            (_, _) => throw new Exception("FOO"), TimeSpan.FromDays(30), TimeSpan.FromSeconds(10));
        await using var dataSource = dataSourceBuilder.Build();

        //Act
        var actAsync = async () => await dataSource.OpenConnectionAsync();
        var actSync = () => dataSource.OpenConnection();

        //Assert
        (await actAsync.Should().ThrowExactlyAsync<PgSqlException>())
            .Which.InnerException.Message.Should().Be("FOO");
        actSync.Should().ThrowExactly<PgSqlException>()
            .Which.InnerException.Message.Should().Be("FOO");
    }

    [Fact]
    public async Task periodic_password_provider_with_second_time_exception()
    {
        //Arrange
        var dataSourceBuilder = GetPasswordlessDataSourceBuilder();
        var password = new PgSqlConnectionStringBuilder(TestUtil.ConnectionString).Password;

        var times = 0;
        var mre = new ManualResetEvent(false);

        dataSourceBuilder.UsePeriodicPasswordProvider(
            (_, _) =>
            {
                if (times++ > 1)
                {
                    mre.Set();
                    throw new Exception("FOO");
                }

                return new(password);
            },
            TimeSpan.FromMilliseconds(100),
            TimeSpan.FromMilliseconds(10));
        await using var dataSource = dataSourceBuilder.Build();

        //Act
        mre.WaitOne();

        //Assert
        // The periodic timer threw, but previously returned a password. Make sure we keep using that last known one.
        using (await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken)) {}
        using (dataSource.OpenConnection()) {}
    }

    [Fact]
    public void both_password_and_password_provider_is_not_supported()
    {
        //Arrange
        var dataSourceBuilder = new PgSqlDataSourceBuilder(TestUtil.ConnectionString);
        dataSourceBuilder.UsePeriodicPasswordProvider((_, _) => new("foo"), TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(10));

        //Act
        var act = () => dataSourceBuilder.Build();

        //Assert
        act.Should().ThrowExactly<NotSupportedException>()
            .Which.Message.Should().Be(PgSqlStrings.CannotSetBothPasswordProviderAndPassword);
    }

    [Fact]
    public void multiple_password_providers_is_not_supported()
    {
        //Arrange
        var dataSourceBuilder = new PgSqlDataSourceBuilder(TestUtil.ConnectionString);
        dataSourceBuilder
            .UsePeriodicPasswordProvider((_, _) => new("foo"), TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(10))
            .UsePasswordProvider(_ => "foo", (_,_) => new("foo"));

        //Act
        var act = () => dataSourceBuilder.Build();

        //Assert
        act.Should().ThrowExactly<NotSupportedException>()
            .Which.Message.Should().Be(PgSqlStrings.CannotSetMultiplePasswordProviderKinds);
    }

    // Connects with a bad password to ensure the proper error is thrown
    [Fact]
    public async Task authentication_failure()
    {
        //Arrange
        using var dataSource = CreateDataSource(csb => csb.Password = "bad");
        using var conn = dataSource.CreateConnection();

        //Act
        var act = () => conn.OpenAsync();

        //Assert
        await act.Should().ThrowExactlyAsync<PostgresException>().Where(e => e.SqlState.StartsWith("28"));
        conn.FullState.Should().Be(ConnectionState.Closed);
    }

    // Simulates a timeout during the authentication phase
    [Fact]
    [IssueLink("https://github.com/npgsql/npgsql/issues/3227")]
    public async Task timeout_during_authentication()
    {
        //Arrange
        var builder = new PgSqlConnectionStringBuilder(ConnectionString) { Timeout = 1 };
        await using var postmasterMock = new PgPostmasterMock(builder.ConnectionString);
        _ = postmasterMock.AcceptServer();

        // The server will accept a connection from the client, but will not respond to the client's authentication
        // request. This should trigger a timeout
        await using var dataSource = CreateDataSource(postmasterMock.ConnectionString);
        await using var connection = dataSource.CreateConnection();

        //Act
        var ex = await Assert.ThrowsAsync<PgSqlException>(async () => await connection.OpenAsync(TestContext.Current.CancellationToken));

        //Assert
        ex.InnerException.Should().BeOfType<TimeoutException>();
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/1180")]
    public void pool_by_password()
    {
        //Arrange
        using var _ = CreateTempPool(ConnectionString, out var connectionString);
        using (var goodConn = new PgSqlConnection(connectionString))
            goodConn.Open();

        var badConnectionString = new PgSqlConnectionStringBuilder(connectionString)
        {
            Password = "badpasswd"
        }.ConnectionString;

        //Assert
        using (var conn = new PgSqlConnection(badConnectionString))
            conn.Invoking(c => c.Open()).Should().ThrowExactly<PostgresException>();
    }

    // Requires user specific local setup
    [Fact(Explicit = true)]
    public async Task authenticate_integrated_security()
    {
        //Arrange
        await using var dataSource = PgSqlDataSource.Create(new PgSqlConnectionStringBuilder(ConnectionString)
        {
            Username = null,
            Password = null
        });

        //Act
        await using var c = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        //Assert
        c.State.Should().Be(ConnectionState.Open);
    }

    PgSqlDataSourceBuilder GetPasswordlessDataSourceBuilder()
        => new(TestUtil.ConnectionString)
        {
            ConnectionStringBuilder =
            {
                Password = null
            }
        };
}

public sealed class AuthenticationTests_NonMultiplexing() : AuthenticationTests(MultiplexingMode.NonMultiplexing);
public sealed class AuthenticationTests_Multiplexing() : AuthenticationTests(MultiplexingMode.Multiplexing);

// These tests set environment variables, so they must not run in parallel with any other test
[Collection(NonParallelCollection.Name)]
public abstract class AuthenticationTestsNonParallel(MultiplexingMode multiplexingMode) : MultiplexingTestBase(multiplexingMode)
{
    [Fact]
    public async Task connect_user_name_from_environment_succeeds()
    {
        using var _ = SetEnvironmentVariable("PGUSER", new PgSqlConnectionStringBuilder(ConnectionString).Username);
        await using var dataSource = CreateDataSource(csb => csb.Username = null);
        await using var __ = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task connect_password_from_environment_succeeds()
    {
        using var _ = SetEnvironmentVariable("PGPASSWORD", new PgSqlConnectionStringBuilder(ConnectionString).Password);
        await using var dataSource = CreateDataSource(csb => csb.Passfile = null);
        await using var __ = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
    }

    #region pgpass

    [Fact]
    public async Task use_pgpass_from_connection_string()
    {
        using var resetPassword = SetEnvironmentVariable("PGPASSWORD", null);
        var builder = new PgSqlConnectionStringBuilder(ConnectionString);
        var passFile = Path.GetTempFileName();
        File.WriteAllText(passFile, $"*:*:*:{builder.Username}:{builder.Password}");

        try
        {
            await using var dataSource = CreateDataSource(csb =>
            {
                csb.Passfile = null;
                csb.Passfile = passFile;
            });
            await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        }
        finally
        {
            File.Delete(passFile);
        }
    }

    [Fact]
    public async Task use_pgpass_from_environment_variable()
    {
        using var resetPassword = SetEnvironmentVariable("PGPASSWORD", null);
        var builder = new PgSqlConnectionStringBuilder(ConnectionString);
        var passFile = Path.GetTempFileName();
        File.WriteAllText(passFile, $"*:*:*:{builder.Username}:{builder.Password}");
        using var passFileVariable = SetEnvironmentVariable("PGPASSFILE", passFile);

        try
        {
            await using var dataSource = CreateDataSource(csb => csb.Password = null);
            await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        }
        finally
        {
            File.Delete(passFile);
        }
    }

    [Fact]
    public async Task use_pgpass_from_homedir()
    {
        using var resetPassword = SetEnvironmentVariable("PGPASSWORD", null);

        string dirToDelete = null;
        string passFile;
        string previousPassFile = null;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var dir = Path.Combine(Environment.GetEnvironmentVariable("APPDATA"), "postgresql");
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
                dirToDelete = dir;
            }
            passFile = Path.Combine(dir, "pgpass.conf");
        }
        else
        {
            passFile = Path.Combine(Environment.GetEnvironmentVariable("HOME"), ".pgpass");
        }

        if (File.Exists(passFile))
        {
            previousPassFile = Path.GetTempFileName();
            File.Move(passFile, previousPassFile);
        }

        try
        {
            var builder = new PgSqlConnectionStringBuilder(ConnectionString);
            File.WriteAllText(passFile, $"*:*:*:{builder.Username}:{builder.Password}");
            await using var dataSource = CreateDataSource(csb => csb.Passfile = null);
            await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        }
        finally
        {
            File.Delete(passFile);
            if (dirToDelete is not null)
                Directory.Delete(dirToDelete);
            if (previousPassFile is not null)
                File.Move(previousPassFile, passFile);
        }
    }

    #endregion pgpass

    [Fact]
    public void password_source_precedence()
    {
        using var resetPassword = SetEnvironmentVariable("PGPASSWORD", null);

        var builder = new PgSqlConnectionStringBuilder(ConnectionString);
        var password = builder.Password;
        var passwordBad = password + "_bad";

        var passFile = Path.GetTempFileName();
        var passFileBad = passFile + "_bad";

        using var deletePassFile = Defer(() => File.Delete(passFile));
        using var deletePassFileBad = Defer(() => File.Delete(passFileBad));

        File.WriteAllText(passFile, $"*:*:*:{builder.Username}:{password}");
        File.WriteAllText(passFileBad, $"*:*:*:{builder.Username}:{passwordBad}");

        using (SetEnvironmentVariable("PGPASSFILE", passFileBad))
        {
            // Password from the connection string goes first
            using (SetEnvironmentVariable("PGPASSWORD", passwordBad))
            {
                using var dataSource1 = CreateDataSource(csb =>
                {
                    csb.Password = password;
                    csb.Passfile = passFileBad;
                });

                dataSource1.Invoking(d => d.OpenConnection()).Should().NotThrow();
            }

            // Password from the environment variable goes second
            using (SetEnvironmentVariable("PGPASSWORD", password))
            {
                using var dataSource2 = CreateDataSource(csb =>
                {
                    csb.Password = null;
                    csb.Passfile = passFileBad;
                });

                dataSource2.Invoking(d => d.OpenConnection()).Should().NotThrow();
            }

            // Passfile from the connection string goes third
            using var dataSource3 = CreateDataSource(csb =>
            {
                csb.Password = null;
                csb.Passfile = passFile;
            });

            dataSource3.Invoking(d => d.OpenConnection()).Should().NotThrow();
        }

        // Passfile from the environment variable goes fourth
        using (SetEnvironmentVariable("PGPASSFILE", passFile))
        {
            using var dataSource4 = CreateDataSource(csb =>
            {
                csb.Password = null;
                csb.Passfile = null;
            });

            dataSource4.Invoking(d => d.OpenConnection()).Should().NotThrow();
        }

        static DeferDisposable Defer(Action action) => new(action);
    }

    readonly struct DeferDisposable(Action action) : IDisposable
    {
        public void Dispose() => action();
    }
}

public sealed class AuthenticationTestsNonParallel_NonMultiplexing() : AuthenticationTestsNonParallel(MultiplexingMode.NonMultiplexing);
public sealed class AuthenticationTestsNonParallel_Multiplexing() : AuthenticationTestsNonParallel(MultiplexingMode.Multiplexing);
