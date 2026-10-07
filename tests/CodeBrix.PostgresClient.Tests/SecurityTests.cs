using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.PostgresClient.Properties;
using CodeBrix.PostgresClient.Tests.Support;
using SilverAssertions;
using Xunit;
using static CodeBrix.PostgresClient.Tests.TestUtil;

namespace CodeBrix.PostgresClient.Tests; //was previously: Npgsql.Tests;

public class SecurityTests : TestBase, IClassFixture<SecurityTestsFixture>
{
    public SecurityTests(SecurityTestsFixture fixture) => fixture.CheckSslSupport();

    // Establishes an SSL connection, assuming a self-signed server certificate
    [Fact]
    public async Task basic_ssl()
    {
        //Arrange
        await using var dataSource = CreateDataSource(csb =>
        {
            csb.SslMode = SslMode.Require;
        });

        //Act
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        //Assert
        conn.IsSslEncrypted.Should().BeTrue();
    }

    // Default user must run with md5 password encryption
    [Fact]
    public async Task default_user_uses_md5_password()
    {
        //Arrange
        if (!SecurityTestsFixture.IsOnCiConfiguredServer)
            Assert.Skip(SecurityTestsFixture.CiOnlyReason);

        await using var dataSource = CreateDataSource(csb =>
        {
            csb.SslMode = SslMode.Require;
        });

        //Act
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        //Assert
        conn.IsScram.Should().BeFalse();
        conn.IsScramPlus.Should().BeFalse();
    }

    // Makes sure a certificate whose root CA isn't known isn't accepted
    [Theory]
    [InlineData(SslMode.VerifyCA)]
    [InlineData(SslMode.VerifyFull)]
    public void reject_self_signed_certificate(SslMode sslMode)
    {
        //Arrange
        var csb = new PgSqlConnectionStringBuilder(ConnectionString)
        {
            SslMode = sslMode,
            CheckCertificateRevocation = false,
        };

        using var _ = CreateTempPool(csb, out var connString);
        using var conn = new PgSqlConnection(connString);

        //Act
        var ex = Assert.Throws<PgSqlException>(conn.Open);

        //Assert
        ex.InnerException.Should().BeOfType<AuthenticationException>();
    }

    // Makes sure that ssl_renegotiation_limit is always 0, renegotiation is buggy
    [Fact]
    public void no_ssl_renegotiation()
    {
        //Arrange
        using var dataSource = CreateDataSource(csb =>
        {
            csb.SslMode = SslMode.Require;
        });
        using var conn = dataSource.OpenConnection();
        conn.ExecuteScalar("SHOW ssl_renegotiation_limit").Should().Be("0");

        //Act
        conn.ExecuteNonQuery("DISCARD ALL");

        //Assert
        conn.ExecuteScalar("SHOW ssl_renegotiation_limit").Should().Be("0");
    }

    // Makes sure that when SSL is disabled IsSecure returns false
    [Fact]
    public void is_secure_without_ssl()
    {
        //Arrange
        using var dataSource = CreateDataSource(csb => csb.SslMode = SslMode.Disable);

        //Act
        using var conn = dataSource.OpenConnection();

        //Assert
        conn.IsSslEncrypted.Should().BeFalse();
    }

    // Needs to be set up (and run with with Kerberos credentials on Linux)
    [Fact(Explicit = true)]
    public void integrated_security_with_Username()
    {
        var username = Environment.UserName;
        if (username == null)
            throw new Exception("Could find username");

        var connString = new PgSqlConnectionStringBuilder(ConnectionString)
        {
            Username = username,
            Password = null
        }.ToString();
        using var conn = new PgSqlConnection(connString);
        try
        {
            conn.Open();
        }
        catch (Exception e)
        {
            if (IsOnBuildServer)
                throw;
            Console.WriteLine(e);
            Assert.Skip("Integrated security (GSS/SSPI) doesn't seem to be set up");
        }
    }

    // Needs to be set up (and run with with Kerberos credentials on Linux)
    [Fact(Explicit = true)]
    public void integrated_security_without_Username()
    {
        var connString = new PgSqlConnectionStringBuilder(ConnectionString)
        {
            Username = null,
            Password = null
        }.ToString();
        using var conn = new PgSqlConnection(connString);
        try
        {
            conn.Open();
        }
        catch (Exception e)
        {
            if (IsOnBuildServer)
                throw;
            Console.WriteLine(e);
            Assert.Skip("Integrated security (GSS/SSPI) doesn't seem to be set up");
        }
    }

    // Needs to be set up (and run with with Kerberos credentials on Linux)
    [Fact(Explicit = true)]
    public void connection_database_is_populated_on_Open()
    {
        var connString = new PgSqlConnectionStringBuilder(ConnectionString)
        {
            Username = null,
            Password = null,
            Database = null
        }.ToString();
        using var conn = new PgSqlConnection(connString);
        try
        {
            conn.Open();
        }
        catch (Exception e)
        {
            if (IsOnBuildServer)
                throw;
            Console.WriteLine(e);
            Assert.Skip("Integrated security (GSS/SSPI) doesn't seem to be set up");
        }
        conn.Database.Should().NotBeNull();
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/1718")]
    public async Task bug_1718()
    {
        //Arrange
        using var dataSource = CreateDataSource(csb =>
        {
            csb.SslMode = SslMode.Require;
        });
        using var conn = dataSource.OpenConnection();
        using var tx = conn.BeginTransaction();
        using var cmd = CreateSleepCommand(conn, 10000);
        var cts = new CancellationTokenSource(1000).Token;

        //Act
        var act = async () => await cmd.ExecuteNonQueryAsync(cts);

        //Assert
        (await act.Should().ThrowExactlyAsync<OperationCanceledException>())
            .WithInnerExceptionExactly<PostgresException>()
            .Where(e => e.SqlState == PostgresErrorCodes.QueryCanceled);
    }

    [Fact]
    public void scram_plus()
    {
        try
        {
            using var dataSource = CreateDataSource(csb =>
            {
                csb.SslMode = SslMode.Require;
                csb.Username = "pgsql_tests_scram";
                csb.Password = "pgsql_tests_scram";
            });
            using var conn = dataSource.OpenConnection();
            // scram-sha-256-plus only works beginning from PostgreSQL 11
            if (conn.PostgreSqlVersion.Major >= 11)
            {
                conn.IsScram.Should().BeFalse();
                conn.IsScramPlus.Should().BeTrue();
            }
            else
            {
                conn.IsScram.Should().BeTrue();
                conn.IsScramPlus.Should().BeFalse();
            }
        }
        catch (Exception e) when (!IsOnBuildServer)
        {
            Console.WriteLine(e);
            Assert.Skip("scram-sha-256-plus doesn't seem to be set up");
        }
    }

    [Theory]
    [InlineData(ChannelBinding.Disable)]
    [InlineData(ChannelBinding.Prefer)]
    [InlineData(ChannelBinding.Require)]
    public void scram_plus_channel_binding(ChannelBinding channelBinding)
    {
        try
        {
            using var dataSource = CreateDataSource(csb =>
            {
                csb.SslMode = SslMode.Require;
                csb.Username = "pgsql_tests_scram";
                csb.Password = "pgsql_tests_scram";
                csb.ChannelBinding = channelBinding;
            });
            // scram-sha-256-plus only works beginning from PostgreSQL 11
            MinimumPgVersion(dataSource, "11.0");
            using var conn = dataSource.OpenConnection();

            if (channelBinding == ChannelBinding.Disable)
            {
                conn.IsScram.Should().BeTrue();
                conn.IsScramPlus.Should().BeFalse();
            }
            else
            {
                conn.IsScram.Should().BeFalse();
                conn.IsScramPlus.Should().BeTrue();
            }
        }
        catch (Exception e) when (!IsOnBuildServer)
        {
            Console.WriteLine(e);
            Assert.Skip("scram-sha-256-plus doesn't seem to be set up");
        }
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task connect_with_only_ssl_allowed_user(bool multiplexing, bool keepAlive)
    {
        if (multiplexing && keepAlive)
        {
            Assert.Skip("Multiplexing doesn't support keepalive");
        }

        try
        {
            await using var dataSource = CreateDataSource(csb =>
            {
                csb.SslMode = SslMode.Allow;
                csb.Username = "pgsql_tests_ssl";
                csb.Password = "pgsql_tests_ssl";
                csb.Multiplexing = multiplexing;
                csb.KeepAlive = keepAlive ? 10 : 0;
            });
            await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
            conn.IsSslEncrypted.Should().BeTrue();
        }
        catch (Exception e) when (!IsOnBuildServer)
        {
            Console.WriteLine(e);
            Assert.Skip("Only ssl user doesn't seem to be set up");
        }
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task connect_with_only_non_ssl_allowed_user(bool multiplexing, bool keepAlive)
    {
        if (OperatingSystem.IsWindows())
            Assert.Skip("Postgresql doesn't close connection correctly on windows which might result in missing error message");

        if (multiplexing && keepAlive)
        {
            Assert.Skip("Multiplexing doesn't support keepalive");
        }

        try
        {
            await using var dataSource = CreateDataSource(csb =>
            {
                csb.SslMode = SslMode.Prefer;
                csb.Username = "pgsql_tests_nossl";
                csb.Password = "pgsql_tests_nossl";
                csb.Multiplexing = multiplexing;
                csb.KeepAlive = keepAlive ? 10 : 0;
            });
            await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
            conn.IsSslEncrypted.Should().BeFalse();
        }
        catch (PgSqlException ex) when (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && ex.InnerException is IOException)
        {
            // Windows server to windows client invites races that can cause the socket to be reset before all data can be read.
            // https://www.postgresql.org/message-id/flat/90b34057-4176-7bb0-0dbb-9822a5f6425b%40greiz-reinsdorf.de
            // https://www.postgresql.org/message-id/flat/16678-253e48d34dc0c376@postgresql.org
            Assert.Skip("Windows server to windows client races can reset the socket before all data is read");
        }
        catch (Exception e) when (!IsOnBuildServer)
        {
            Console.WriteLine(e);
            Assert.Skip("Only nonssl user doesn't seem to be set up");
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task data_source_SslClientAuthenticationOptionsCallback_is_invoked(bool acceptCertificate)
    {
        //Arrange
        var callbackWasInvoked = false;

        var dataSourceBuilder = CreateDataSourceBuilder();
        dataSourceBuilder.ConnectionStringBuilder.SslMode = SslMode.Require;
        dataSourceBuilder.UseSslClientAuthenticationOptionsCallback(options =>
        {
            options.RemoteCertificateValidationCallback = (_, _, _, _) =>
            {
                callbackWasInvoked = true;
                return acceptCertificate;
            };
        });
        await using var dataSource = dataSourceBuilder.Build();
        await using var connection = dataSource.CreateConnection();

        //Assert
        if (acceptCertificate)
        {
            var act = async () => await connection.OpenAsync();
            await act.Should().NotThrowAsync();
        }
        else
        {
            var ex = await Assert.ThrowsAsync<PgSqlException>(async () => await connection.OpenAsync(TestContext.Current.CancellationToken));
            ex.InnerException.Should().BeOfType<AuthenticationException>();
        }

        callbackWasInvoked.Should().BeTrue();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task connection_SslClientAuthenticationOptionsCallback_is_invoked(bool acceptCertificate)
    {
        //Arrange
        var callbackWasInvoked = false;

        var dataSourceBuilder = CreateDataSourceBuilder();
        dataSourceBuilder.ConnectionStringBuilder.SslMode = SslMode.Require;
        await using var dataSource = dataSourceBuilder.Build();
        await using var connection = dataSource.CreateConnection();
        connection.SslClientAuthenticationOptionsCallback = options =>
        {
            options.RemoteCertificateValidationCallback = (_, _, _, _) =>
            {
                callbackWasInvoked = true;
                return acceptCertificate;
            };
        };

        //Assert
        if (acceptCertificate)
        {
            var act = async () => await connection.OpenAsync();
            await act.Should().NotThrowAsync();
        }
        else
        {
            var ex = await Assert.ThrowsAsync<PgSqlException>(async () => await connection.OpenAsync(TestContext.Current.CancellationToken));
            ex.InnerException.Should().BeOfType<AuthenticationException>();
        }

        callbackWasInvoked.Should().BeTrue();
    }

    [Theory]
    [InlineData(SslMode.VerifyCA)]
    [InlineData(SslMode.VerifyFull)]
    public async Task connect_with_Verify_and_callback_throws(SslMode sslMode)
    {
        //Arrange
        using var dataSource = CreateDataSource(csb => csb.SslMode = sslMode);
        using var connection = dataSource.CreateConnection();
        connection.SslClientAuthenticationOptionsCallback = options =>
        {
            options.RemoteCertificateValidationCallback = (_, _, _, _) => true;
        };

        //Act
        var ex = await Assert.ThrowsAsync<ArgumentException>(async () => await connection.OpenAsync(TestContext.Current.CancellationToken));

        //Assert
        ex.Message.Should().Be(string.Format(PgSqlStrings.CannotUseSslVerifyWithCustomValidationCallback, sslMode));
    }

    [Fact]
    public async Task connect_with_RootCertificate_and_callback_throws()
    {
        //Arrange
        using var dataSource = CreateDataSource(csb =>
        {
            csb.SslMode = SslMode.Require;
            csb.RootCertificate = "foo";
        });
        using var connection = dataSource.CreateConnection();
        connection.SslClientAuthenticationOptionsCallback = options =>
        {
            options.RemoteCertificateValidationCallback = (_, _, _, _) => true;
        };

        //Act
        var ex = await Assert.ThrowsAsync<ArgumentException>(async () => await connection.OpenAsync(TestContext.Current.CancellationToken));

        //Assert
        ex.Message.Should().Be(string.Format(PgSqlStrings.CannotUseSslRootCertificateWithCustomValidationCallback));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [IssueLink("https://github.com/npgsql/npgsql/issues/4305")]
    public async Task bug_4305_secure(bool async)
    {
        await using var dataSource = CreateDataSource(csb =>
        {
            csb.SslMode = SslMode.Require;
            csb.Username = "pgsql_tests_ssl";
            csb.Password = "pgsql_tests_ssl";
            csb.MaxPoolSize = 1;
        });

        PgSqlConnection conn = default;

        try
        {
            conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
            conn.IsSslEncrypted.Should().BeTrue();
        }
        catch (Exception e) when (!IsOnBuildServer)
        {
            Console.WriteLine(e);
            Assert.Skip("Only ssl user doesn't seem to be set up");
        }

        await using var __ = conn;
        await using var cmd = conn.CreateCommand();
        await using (var tx = await conn.BeginTransactionAsync(TestContext.Current.CancellationToken))
        {
            var originalConnector = conn.Connector;

            cmd.CommandText = "select pg_sleep(30)";
            cmd.CommandTimeout = 3;
            var ex = async
                ? await Assert.ThrowsAsync<PgSqlException>(() => cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken))
                : Assert.Throws<PgSqlException>(() => cmd.ExecuteNonQuery());
            ex.InnerException.Should().BeOfType<TimeoutException>();

            await conn.CloseAsync();
            await conn.OpenAsync(TestContext.Current.CancellationToken);

            conn.Connector.Should().BeSameAs(originalConnector);
        }

        cmd.CommandText = "SELECT 1";
        if (async)
            await cmd.Awaiting(c => c.ExecuteNonQueryAsync()).Should().NotThrowAsync();
        else
            cmd.Invoking(c => c.ExecuteNonQuery()).Should().NotThrow();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [IssueLink("https://github.com/npgsql/npgsql/issues/4305")]
    public async Task bug_4305_not_secure(bool async)
    {
        await using var dataSource = CreateDataSource(csb =>
        {
            csb.SslMode = SslMode.Disable;
            csb.Username = "pgsql_tests_nossl";
            csb.Password = "pgsql_tests_nossl";
            csb.MaxPoolSize = 1;
        });

        PgSqlConnection conn = default;

        try
        {
            conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
            conn.IsSslEncrypted.Should().BeFalse();
        }
        catch (Exception e) when (!IsOnBuildServer)
        {
            Console.WriteLine(e);
            Assert.Skip("Only nossl user doesn't seem to be set up");
        }

        await using var __ = conn;
        var originalConnector = conn.Connector;

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "select pg_sleep(30)";
        cmd.CommandTimeout = 3;
        var ex = async
            ? await Assert.ThrowsAsync<PgSqlException>(() => cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken))
            : Assert.Throws<PgSqlException>(() => cmd.ExecuteNonQuery());
        ex.InnerException.Should().BeOfType<TimeoutException>();

        await conn.CloseAsync();
        await conn.OpenAsync(TestContext.Current.CancellationToken);

        conn.Connector.Should().BeSameAs(originalConnector);

        cmd.CommandText = "SELECT 1";
        if (async)
            await cmd.Awaiting(c => c.ExecuteNonQueryAsync()).Should().NotThrowAsync();
        else
            cmd.Invoking(c => c.ExecuteNonQuery()).Should().NotThrow();
    }

    [Fact]
    public async Task direct_ssl_negotiation()
    {
        //Arrange
        await using var adminConn = await OpenConnectionAsync();
        MinimumPgVersion(adminConn, "17.0");

        await using var dataSource = CreateDataSource(csb =>
        {
            csb.SslMode = SslMode.Require;
            csb.SslNegotiation = SslNegotiation.Direct;
        });

        //Act
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        //Assert
        conn.IsSslEncrypted.Should().BeTrue();
    }

    [Theory]
    [InlineData(SslMode.Disable)]
    [InlineData(SslMode.Allow)]
    [InlineData(SslMode.Prefer)]
    [InlineData(SslMode.Require)]
    [InlineData(SslMode.VerifyCA)]
    [InlineData(SslMode.VerifyFull)]
    public void direct_ssl_requires_correct_sslmode(SslMode sslMode)
    {
        if (sslMode is SslMode.Disable or SslMode.Allow or SslMode.Prefer)
        {
            var ex = Assert.Throws<ArgumentException>(() =>
            {
                using var dataSource = CreateDataSource(csb =>
                {
                    csb.SslMode = sslMode;
                    csb.SslNegotiation = SslNegotiation.Direct;
                });
            });
            ex.Message.Should().Be("SSL Mode has to be Require or higher to be used with direct SSL Negotiation");
        }
        else
        {
            using var dataSource = CreateDataSource(csb =>
            {
                csb.SslMode = sslMode;
                csb.SslNegotiation = SslNegotiation.Direct;
            });
        }
    }

    [Theory]
    [InlineData(SslMode.VerifyCA)]
    [InlineData(SslMode.VerifyFull)]
    public async Task connect_with_verify_and_ca_cert(SslMode sslMode)
    {
        if (OperatingSystem.IsMacOS())
            Assert.Skip("Mac requires explicit opt-in to receive CA certificate in TLS handshake");
        if (!SecurityTestsFixture.IsOnCiConfiguredServer)
            Assert.Skip(SecurityTestsFixture.CiOnlyReason);

        await using var dataSource = CreateDataSource(csb =>
        {
            csb.SslMode = sslMode;
            csb.RootCertificate = SecurityTestsFixture.CaCertificatePath;
        });

        await using var _ = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData(SslMode.VerifyCA)]
    [InlineData(SslMode.VerifyFull)]
    public async Task connect_with_verify_check_host(SslMode sslMode)
    {
        if (OperatingSystem.IsMacOS())
            Assert.Skip("Mac requires explicit opt-in to receive CA certificate in TLS handshake");
        if (!SecurityTestsFixture.IsOnCiConfiguredServer)
            Assert.Skip(SecurityTestsFixture.CiOnlyReason);

        await using var dataSource = CreateDataSource(csb =>
        {
            csb.Host = "127.0.0.1";
            csb.SslMode = sslMode;
            csb.RootCertificate = SecurityTestsFixture.CaCertificatePath;
        });

        if (sslMode == SslMode.VerifyCA)
        {
            await using var _ = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        }
        else
        {
            var ex = await Assert.ThrowsAsync<PgSqlException>(async () => await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken));
            ex.InnerException.Should().BeOfType<AuthenticationException>();
        }
    }

    [Theory]
    [InlineData(SslMode.VerifyCA, true)]
    [InlineData(SslMode.VerifyCA, false)]
    [InlineData(SslMode.VerifyFull, true)]
    [InlineData(SslMode.VerifyFull, false)]
    public async Task connect_with_verify_and_multiple_ca_cert(SslMode sslMode, bool realCaFirst)
    {
        if (OperatingSystem.IsMacOS())
            Assert.Skip("Mac requires explicit opt-in to receive CA certificate in TLS handshake");
        if (!SecurityTestsFixture.IsOnCiConfiguredServer)
            Assert.Skip(SecurityTestsFixture.CiOnlyReason);

        var certificates = new X509Certificate2Collection();

        using var realCaCert = X509CertificateLoader.LoadCertificateFromFile(SecurityTestsFixture.CaCertificatePath);

        using var ecdsa = ECDsa.Create();
        var req = new CertificateRequest("cn=localhost", ecdsa, HashAlgorithmName.SHA256);
        using var unrelatedCaCert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));

        if (realCaFirst)
        {
            certificates.Add(realCaCert);
            certificates.Add(unrelatedCaCert);
        }
        else
        {
            certificates.Add(unrelatedCaCert);
            certificates.Add(realCaCert);
        }

        var dataSourceBuilder = CreateDataSourceBuilder();
        dataSourceBuilder.ConnectionStringBuilder.SslMode = sslMode;
        dataSourceBuilder.UseRootCertificates(certificates);

        await using var dataSource = dataSourceBuilder.Build();

        await using var _ = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
    }
}

// Sets environment variable
[Collection(NonParallelCollection.Name)]
public class SecurityTestsNonParallel : TestBase, IClassFixture<SecurityTestsFixture>
{
    public SecurityTestsNonParallel(SecurityTestsFixture fixture) => fixture.CheckSslSupport();

    [Fact]
    public async Task direct_ssl_via_env_requires_correct_sslmode()
    {
        await using var adminConn = await OpenConnectionAsync();
        MinimumPgVersion(adminConn, "17.0");

        // NonParallelizable attribute doesn't work with parameters that well
        foreach (var sslMode in new[] { SslMode.Disable, SslMode.Allow, SslMode.Prefer, SslMode.Require })
        {
            using var _ = SetEnvironmentVariable("PGSSLNEGOTIATION", nameof(SslNegotiation.Direct));
            await using var dataSource = CreateDataSource(csb =>
            {
                csb.SslMode = sslMode;
            });
            if (sslMode is SslMode.Disable or SslMode.Allow or SslMode.Prefer)
            {
                var ex = await Assert.ThrowsAsync<ArgumentException>(async () => await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken));
                ex.Message.Should().Be("SSL Mode has to be Require or higher to be used with direct SSL Negotiation");
            }
            else
            {
                await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
            }
        }
    }
}

/// <summary>
/// Shared setup for <see cref="SecurityTests"/>: checks once whether the backend has SSL enabled.
/// </summary>
public sealed class SecurityTestsFixture
{
    readonly Lazy<string> _sslSupport = new(() =>
    {
        using var conn = new PgSqlConnection(TestUtil.ConnectionString);
        conn.Open();
        return (string)conn.ExecuteScalar("SHOW ssl");
    });

    /// <summary>The CA certificate that signed the test server's certificate (copied to the output's Certificates folder).</summary>
    public static string CaCertificatePath => Path.Combine(AppContext.BaseDirectory, "Certificates", "ca.crt");

    /// <summary>
    /// Whether the tests run against a server configured like the original CI server: the Docker test
    /// container (used whenever PGSQL_TEST_DB is not set), or a CI build.
    /// </summary>
    public static bool IsOnCiConfiguredServer
        => IsOnBuildServer || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PGSQL_TEST_DB"));

    public const string CiOnlyReason = "Only executed against the CI-configured test server (the Docker test container or a CI build)";

    public void CheckSslSupport()
    {
        if (_sslSupport.Value == "off")
            IgnoreExceptOnBuildServer("SSL support isn't enabled at the backend");
    }
}
