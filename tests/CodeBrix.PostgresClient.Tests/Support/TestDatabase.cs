using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Docker;

namespace CodeBrix.PostgresClient.Tests.Support;

/// <summary>
/// The PostgreSQL server the database tests run against: a throw-away Docker container, started on
/// first use and removed when the test run ends (see <see cref="TestDatabaseLifetime"/>).
/// </summary>
/// <remarks>
/// Nothing is started until a test first reads <see cref="ConnectionString"/>, so tests that never
/// touch a database run without Docker. When the PGSQL_TEST_DB environment variable is set, it is
/// used as the connection string and no container is started.
///
/// When Docker cannot be reached, reading <see cref="ConnectionString"/> throws, so every database
/// test FAILS with a "Docker not available" message - a broken setup never looks like a green run.
///
/// The container is configured like the CI server the original test suite was written against:
/// SSL on (with the certificates in Certificates/), 500 connections, logical replication, prepared
/// transactions, and the pgsql_tests / pgsql_tests_scram / pgsql_tests_ssl / pgsql_tests_nossl
/// users with the matching pg_hba.conf rules. All configuration travels in the container
/// specification itself (environment variables plus a small entrypoint wrapper) - no bind mounts -
/// so it works against a remote daemon too.
/// </remarks>
public static class TestDatabase
{
    /// <summary>The PostgreSQL image the tests run against.</summary>
    public const string Image = "postgres:18";

    /// <summary>The user, password and database name of the main test account.</summary>
    public const string TestUser = "pgsql_tests";

    const string RunLabel = "codebrix.postgresclient.tests";
    const string PidLabel = "codebrix.postgresclient.tests.pid";
    const string HostLabel = "codebrix.postgresclient.tests.host";

    static readonly Lazy<Task<string>> Started = new(StartAsync, LazyThreadSafetyMode.ExecutionAndPublication);
    static readonly object StopLock = new();
    static string _containerId;
    static bool _stopped;

    /// <summary>
    /// The connection string for the test database. The first read starts the container (or uses
    /// PGSQL_TEST_DB); a failure to start is cached, so every later read fails the same way.
    /// </summary>
    public static string ConnectionString
    {
        get
        {
            var external = Environment.GetEnvironmentVariable("PGSQL_TEST_DB");
            if (!string.IsNullOrWhiteSpace(external))
                return external;
            return Started.Value.GetAwaiter().GetResult();
        }
    }

    static async Task<string> StartAsync()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        var ct = cts.Token;

        DockerClient client;
        try
        {
            client = DockerClient.Create();
        }
        catch (Exception e)
        {
            throw DockerNotAvailable(e.Message, e);
        }

        using (client)
        {
            if (!await client.System.PingAsync(ct))
                throw DockerNotAvailable($"the daemon at '{client.Endpoint}' did not answer a ping", null);

            await RemoveOrphanedContainersAsync(client, ct);

            try
            {
                await client.Images.InspectAsync(Image, ct);
            }
            catch (DockerImageNotFoundException)
            {
                await client.Images.PullAsync(Image, progress: null, ct);
            }

            var hostPort = GetFreeTcpPort();
            var spec = BuildContainerSpec(hostPort);
            var id = await client.Containers.CreateAsync(spec, ct);
            lock (StopLock)
                _containerId = id;
            AppDomain.CurrentDomain.ProcessExit += (_, _) => Stop();

            await client.Containers.StartAsync(id, ct);
            try
            {
                await client.Diagnostics.WaitForHealthyAsync(id, TimeSpan.FromMinutes(3), ct);
            }
            catch (Exception e)
            {
                var logs = await client.Containers.GetLogsAsync(id, tail: 50, cancellationToken: ct);
                throw new InvalidOperationException(
                    $"The PostgreSQL test container did not become healthy: {e.Message}{Environment.NewLine}" +
                    $"Last container log lines:{Environment.NewLine}{logs.Combined}", e);
            }

            var connectionString =
                $"Host=localhost;Port={hostPort};Username={TestUser};Password={TestUser};Database={TestUser};" +
                "Timeout=0;Command Timeout=0;SSL Mode=Disable;Multiplexing=False";
            await WaitUntilConnectableAsync(connectionString, ct);
            return connectionString;
        }
    }

    /// <summary>
    /// Removes the test container, if this process started one. Safe to call more than once.
    /// </summary>
    public static void Stop()
    {
        string id;
        lock (StopLock)
        {
            if (_stopped || _containerId == null)
                return;
            _stopped = true;
            id = _containerId;
        }

        using var client = DockerClient.Create();
        client.Containers.RemoveAsync(id, force: true, removeVolumes: true).GetAwaiter().GetResult();
    }

    static Exception DockerNotAvailable(string detail, Exception inner)
        => new InvalidOperationException(
            "Docker not available: " + detail + ". The database tests in CodeBrix.PostgresClient.Tests run against a " +
            $"PostgreSQL container ({Image}) started through CodeBrix.Docker. Start the Docker daemon (and make sure this " +
            "user may use it), or set PGSQL_TEST_DB to the connection string of an existing test database, then re-run.",
            inner);

    static ContainerSpec BuildContainerSpec(int hostPort)
    {
        var certificates = Path.Combine(AppContext.BaseDirectory, "Certificates");
        string Base64(string file) => Convert.ToBase64String(File.ReadAllBytes(Path.Combine(certificates, file)));

        // The wrapper runs as root before the image's own entrypoint: it writes the certificates, the
        // pg_hba.conf and the init SQL from the environment, hands them to the postgres user, and then
        // execs the stock entrypoint with the server settings as -c arguments.
        const string wrapper =
            "set -e; mkdir -p /pgsql-tests; " +
            "printf '%s' \"$PGSQL_CA_CRT\" | base64 -d > /pgsql-tests/ca.crt; " +
            "printf '%s' \"$PGSQL_SERVER_CRT\" | base64 -d > /pgsql-tests/server.crt; " +
            "printf '%s' \"$PGSQL_SERVER_KEY\" | base64 -d > /pgsql-tests/server.key; " +
            "printf '%s\\n' \"$PGSQL_HBA\" > /pgsql-tests/pg_hba.conf; " +
            "printf '%s\\n' \"$PGSQL_INIT_SQL\" > /docker-entrypoint-initdb.d/10-pgsql-tests.sql; " +
            "chown -R postgres:postgres /pgsql-tests; chmod 600 /pgsql-tests/server.key; " +
            "exec docker-entrypoint.sh postgres " +
            "-c max_connections=500 " +
            "-c ssl=on -c ssl_ca_file=/pgsql-tests/ca.crt " +
            "-c ssl_cert_file=/pgsql-tests/server.crt -c ssl_key_file=/pgsql-tests/server.key " +
            "-c hba_file=/pgsql-tests/pg_hba.conf " +
            "-c password_encryption=scram-sha-256 " +
            "-c wal_level=logical -c max_wal_senders=50 -c logical_decoding_work_mem=64kB -c wal_sender_timeout=3s " +
            "-c synchronous_standby_names=pgsql_test_sync_standby -c synchronous_commit=local " +
            "-c max_prepared_transactions=100";

        var hba = string.Join("\n",
            "local all all trust",
            "host all pgsql_tests_scram all scram-sha-256",
            "hostssl all pgsql_tests_ssl all md5",
            "hostnossl all pgsql_tests_ssl all reject",
            "hostnossl all pgsql_tests_nossl all md5",
            "hostssl all pgsql_tests_nossl all reject",
            "host all all all md5",
            "host replication all all md5");

        // pgsql_tests gets a pre-hashed md5 password (the md5 authentication tests need one); the
        // scram user is created after switching the session to scram-sha-256.
        var initSql = string.Join("\n",
            $"CREATE USER {TestUser} SUPERUSER PASSWORD '{Md5Password(TestUser, TestUser)}';",
            "CREATE USER pgsql_tests_ssl SUPERUSER PASSWORD 'pgsql_tests_ssl';",
            "CREATE USER pgsql_tests_nossl SUPERUSER PASSWORD 'pgsql_tests_nossl';",
            "SET password_encryption = 'scram-sha-256';",
            "CREATE USER pgsql_tests_scram SUPERUSER PASSWORD 'pgsql_tests_scram';",
            $"CREATE DATABASE {TestUser} OWNER {TestUser};");

        return new ContainerSpec
        {
            Image = Image,
            Name = $"codebrix-postgresclient-tests-{Environment.ProcessId}-{Guid.NewGuid():N}"[..60],
            Entrypoint = ["bash", "-c", wrapper],
            Env =
            {
                "POSTGRES_PASSWORD=postgres",
                "PGSQL_CA_CRT=" + Base64("ca.crt"),
                "PGSQL_SERVER_CRT=" + Base64("server.crt"),
                "PGSQL_SERVER_KEY=" + Base64("server.key"),
                "PGSQL_HBA=" + hba,
                "PGSQL_INIT_SQL=" + initSql,
            },
            Labels =
            {
                [RunLabel] = "1",
                [PidLabel] = Environment.ProcessId.ToString(),
                [HostLabel] = Environment.MachineName,
            },
            PortBindings = { new PortBinding(5432, hostPort) },
            // pg_isready over TCP: the image's init phase runs a temporary server that listens on the
            // Unix socket only, so this turns healthy only once the real server is up.
            Healthcheck = new HealthcheckSpec
            {
                Test = ["CMD-SHELL", "pg_isready -h 127.0.0.1 -p 5432 -U postgres"],
                Interval = TimeSpan.FromSeconds(1),
                Timeout = TimeSpan.FromSeconds(5),
                Retries = 180,
            },
        };
    }

    /// <summary>
    /// Removes test containers left behind by earlier runs whose process is gone (a crashed or killed
    /// test host). Containers of other LIVE test processes - parallel runs - are left alone.
    /// </summary>
    static async Task RemoveOrphanedContainersAsync(DockerClient client, CancellationToken ct)
    {
        var containers = await client.Containers.ListAsync(all: true, new Dictionary<string, string> { [RunLabel] = "1" }, ct);
        foreach (var container in containers)
        {
            if (container.Labels == null
                || !container.Labels.TryGetValue(HostLabel, out var host) || host != Environment.MachineName
                || !container.Labels.TryGetValue(PidLabel, out var pidText) || !int.TryParse(pidText, out var pid)
                || IsProcessAlive(pid))
            {
                continue;
            }

            await client.Containers.RemoveAsync(container.Id, force: true, removeVolumes: true, ct);
        }
    }

    static bool IsProcessAlive(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    static async Task WaitUntilConnectableAsync(string connectionString, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(60);
        while (true)
        {
            try
            {
                await using var connection = new PgSqlConnection(connectionString + ";Pooling=false");
                await connection.OpenAsync(ct);
                return;
            }
            catch (Exception) when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(500, ct);
            }
        }
    }

    static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    static string Md5Password(string user, string password)
        => "md5" + Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(password + user)));
}

/// <summary>
/// Assembly fixture whose only job is to remove the test container when the test run ends.
/// Constructing it starts nothing.
/// </summary>
public sealed class TestDatabaseLifetime : IAsyncDisposable
{
    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        TestDatabase.Stop();
        return ValueTask.CompletedTask;
    }
}
