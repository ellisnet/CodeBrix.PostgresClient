using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using CodeBrix.PostgresClient.Tests.Support;
using SilverAssertions;
using Xunit;

namespace CodeBrix.PostgresClient.Tests; //was previously: Npgsql.Tests;

public static class TestUtil
{
    /// <summary>
    /// The connection string that will be used when opening the connection to the tests database:
    /// the PGSQL_TEST_DB environment variable when it is set, otherwise the throw-away PostgreSQL
    /// container that <see cref="TestDatabase"/> starts on first use.
    /// May be overridden in fixtures, e.g. to set special connection parameters
    /// </summary>
    public static string ConnectionString => TestDatabase.ConnectionString;

    public static bool IsOnBuildServer =>
        Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != null ||
        Environment.GetEnvironmentVariable("CI") != null;

    /// <summary>
    /// Skips the test (Assert.Skip) unless we're on the build server, in which case calls
    /// Assert.Fail(). We don't to miss any regressions just because something was misconfigured
    /// at the build server and caused a test to be inconclusive.
    /// </summary>
    [DoesNotReturn]
    public static void IgnoreExceptOnBuildServer(string message)
    {
        if (IsOnBuildServer)
            Assert.Fail(message);
        else
            Assert.Skip(message);

        throw new Exception("Should not occur");
    }

    public static void IgnoreExceptOnBuildServer(string message, params object[] args)
        => IgnoreExceptOnBuildServer(string.Format(message, args));

    public static void MinimumPgVersion(PgSqlDataSource dataSource, string minVersion, string ignoreText = null)
    {
        using var connection = dataSource.OpenConnection();
        MinimumPgVersion(connection, minVersion, ignoreText);
    }

    public static bool MinimumPgVersion(PgSqlConnection conn, string minVersion, string ignoreText = null)
    {
        var min = new Version(minVersion);
        if (conn.PostgreSqlVersion < min)
        {
            var msg = $"Postgresql backend version {conn.PostgreSqlVersion} is less than the required {min}";
            if (ignoreText != null)
                msg += ": " + ignoreText;
            Assert.Skip(msg);
            return false;
        }

        return true;
    }

    public static void MaximumPgVersionExclusive(PgSqlConnection conn, string maxVersion, string ignoreText = null)
    {
        var max = new Version(maxVersion);
        if (conn.PostgreSqlVersion >= max)
        {
            var msg = $"Postgresql backend version {conn.PostgreSqlVersion} is greater than or equal to the required (exclusive) maximum of {maxVersion}";
            if (ignoreText != null)
                msg += ": " + ignoreText;
            Assert.Skip(msg);
        }
    }

    static readonly Version MinCreateExtensionVersion = new(9, 1);

    public static async Task IgnoreOnRedshift(PgSqlConnection conn, string ignoreText = null)
    {
        await using var command = conn.CreateCommand();
        command.CommandText = "SELECT version()";
        var version = (string)(await command.ExecuteScalarAsync());
        if (version.Contains("redshift", StringComparison.OrdinalIgnoreCase))
        {
            var msg = "Test ignored on Redshift";
            if (ignoreText != null)
                msg += ": " + ignoreText;
            Assert.Skip(msg);
        }
    }

    public static void EnsureExtension(PgSqlConnection conn, string extension, string minVersion = null)
        => EnsureExtension(conn, extension, minVersion, async: false).GetAwaiter().GetResult();

    public static Task EnsureExtensionAsync(PgSqlConnection conn, string extension, string minVersion = null)
        => EnsureExtension(conn, extension, minVersion, async: true);

    static async Task EnsureExtension(PgSqlConnection conn, string extension, string minVersion, bool async)
    {
        if (minVersion != null && !MinimumPgVersion(conn, minVersion, $"The extension '{extension}' only works for PostgreSQL {minVersion} and higher."))
            return;

        if (conn.PostgreSqlVersion < MinCreateExtensionVersion)
            Assert.Skip($"The 'CREATE EXTENSION' command only works for PostgreSQL {MinCreateExtensionVersion} and higher.");

        try
        {
            if (async)
                await conn.ExecuteNonQueryAsync($"CREATE EXTENSION IF NOT EXISTS {extension}");
            else
                conn.ExecuteNonQuery($"CREATE EXTENSION IF NOT EXISTS {extension}");
        }
        catch (PostgresException ex) when (ex.ConstraintName == "pg_extension_name_index")
        {
            // The extension is already installed, but we can race across threads.
            // https://stackoverflow.com/questions/63104126/create-extention-if-not-exists-doesnt-really-check-if-extention-does-not-exis
        }

        conn.ReloadTypes();
    }

    /// <summary>
    /// Causes the test to be ignored if the supplied query fails with SqlState 0A000 (feature_not_supported)
    /// </summary>
    /// <param name="conn">The connection to execute the test query. The connection needs to be open.</param>
    /// <param name="testQuery">The query to test for the feature.
    /// This query needs to fail with SqlState 0A000 (feature_not_supported) if the feature isn't present.</param>
    public static void IgnoreIfFeatureNotSupported(PgSqlConnection conn, string testQuery)
        => IgnoreIfFeatureNotSupported(conn, testQuery, async: false).GetAwaiter().GetResult();

    /// <summary>
    /// Causes the test to be ignored if the supplied query fails with SqlState 0A000 (feature_not_supported)
    /// </summary>
    /// <param name="conn">The connection to execute the test query. The connection needs to be open.</param>
    /// <param name="testQuery">The query to test for the feature.
    /// This query needs to fail with SqlState 0A000 (feature_not_supported) if the feature isn't present.</param>
    public static Task IgnoreIfFeatureNotSupportedAsync(PgSqlConnection conn, string testQuery)
        => IgnoreIfFeatureNotSupported(conn, testQuery, async: true);

    static async Task IgnoreIfFeatureNotSupported(PgSqlConnection conn, string testQuery, bool async)
    {
        try
        {
            if (async)
                await conn.ExecuteNonQueryAsync(testQuery);
            else
                conn.ExecuteNonQuery(testQuery);
        }
        catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.FeatureNotSupported)
        {
            Assert.Skip(e.Message);
        }
    }

    public static async Task EnsurePostgis(PgSqlConnection conn)
    {
        try
        {
            await EnsureExtensionAsync(conn, "postgis");
        }
        catch (PostgresException)
        {
            if (Environment.GetEnvironmentVariable("PGSQL_TEST_POSTGIS")?.ToLower(CultureInfo.InvariantCulture) is "1" or "true")
            {
                throw;
            }
            else
            {
                Assert.Skip($"PostGIS isn't installed, skipping tests");
            }
        }
    }

    public static string GetUniqueIdentifier(string prefix)
        => prefix + Interlocked.Increment(ref _counter);

    static int _counter;

    /// <summary>
    /// Creates a table with a unique name, usable for a single test.
    /// </summary>
    internal static async Task<string> CreateTempTable(PgSqlConnection conn, string columns)
    {
        var tableName = "temp_table" + Interlocked.Increment(ref _tempTableCounter);

        await conn.ExecuteNonQueryAsync(@$"
START TRANSACTION;
SELECT pg_advisory_xact_lock(0);
DROP TABLE IF EXISTS {tableName} CASCADE;
COMMIT;
CREATE TABLE {tableName} ({columns});");

        return tableName;
    }

    /// <summary>
    /// Generates a unique table name, usable for a single test, and drops it if it already exists.
    /// Actual creation of the table is the responsibility of the caller.
    /// </summary>
    internal static async Task<string> GetTempTableName(PgSqlConnection conn)
    {
        var tableName = "temp_table" + Interlocked.Increment(ref _tempTableCounter);
        await conn.ExecuteNonQueryAsync(@$"
START TRANSACTION;
SELECT pg_advisory_xact_lock(0);
DROP TABLE IF EXISTS {tableName} CASCADE;
COMMIT");
        return tableName;
    }

    /// <summary>
    /// Creates a table with a unique name, usable for a single test, and returns an <see cref="IDisposable"/> to
    /// drop it at the end of the test.
    /// </summary>
    internal static async Task<string> CreateTempTable(PgSqlDataSource dataSource, string columns)
    {
        var tableName = "temp_table" + Interlocked.Increment(ref _tempTableCounter);
        await dataSource.ExecuteNonQueryAsync(@$"
START TRANSACTION;
SELECT pg_advisory_xact_lock(0);
DROP TABLE IF EXISTS {tableName} CASCADE;
COMMIT;
CREATE TABLE {tableName} ({columns});");
        return tableName;
    }

    /// <summary>
    /// Creates a schema with a unique name, usable for a single test.
    /// </summary>
    internal static async Task<string> CreateTempSchema(PgSqlConnection conn)
    {
        var schemaName = "temp_schema" + Interlocked.Increment(ref _tempSchemaCounter);
        await conn.ExecuteNonQueryAsync($"DROP SCHEMA IF EXISTS {schemaName} CASCADE; CREATE SCHEMA {schemaName}");
        return schemaName;
    }

    /// <summary>
    /// Generates a unique view name, usable for a single test, and drops it if it already exists.
    /// Actual creation of the view is the responsibility of the caller.
    /// </summary>
    internal static async Task<string> GetTempViewName(PgSqlConnection conn)
    {
        var viewName = "temp_view" + Interlocked.Increment(ref _tempViewCounter);
        await conn.ExecuteNonQueryAsync($"DROP VIEW IF EXISTS {viewName} CASCADE");
        return viewName;
    }

    /// <summary>
    /// Generates a unique materialized view name, usable for a single test, and drops it if it already exists.
    /// Actual creation of the materialized view is the responsibility of the caller.
    /// </summary>
    internal static async Task<string> GetTempMaterializedViewName(PgSqlConnection conn)
    {
        var viewName = "temp_materialized_view" + Interlocked.Increment(ref _tempViewCounter);
        await conn.ExecuteNonQueryAsync($"DROP MATERIALIZED VIEW IF EXISTS {viewName} CASCADE");
        return viewName;
    }

    /// <summary>
    /// Generates a unique function name, usable for a single test.
    /// Actual creation of the function is the responsibility of the caller.
    /// </summary>
    internal static async Task<string> GetTempFunctionName(PgSqlConnection conn)
    {
        var functionName = "temp_func" + Interlocked.Increment(ref _tempFunctionCounter);
        await conn.ExecuteNonQueryAsync($"DROP FUNCTION IF EXISTS {functionName} CASCADE");
        return functionName;
    }

    /// <summary>
    /// Generates a unique function name, usable for a single test.
    /// Actual creation of the function is the responsibility of the caller.
    /// </summary>
    /// <returns>
    /// An <see cref="IDisposable"/> to drop the function at the end of the test.
    /// </returns>
    internal static async Task<string> GetTempProcedureName(PgSqlDataSource dataSource)
    {
        var procedureName = "temp_procedure" + Interlocked.Increment(ref _tempProcedureCounter);
        await dataSource.ExecuteNonQueryAsync($"DROP PROCEDURE IF EXISTS {procedureName} CASCADE");
        return procedureName;
    }

    /// <summary>
    /// Generates a unique function name, usable for a single test.
    /// Actual creation of the function is the responsibility of the caller.
    /// </summary>
    /// <returns>
    /// An <see cref="IDisposable"/> to drop the function at the end of the test.
    /// </returns>
    internal static async Task<string> GetTempProcedureName(PgSqlConnection connection)
    {
        var procedureName = "temp_procedure" + Interlocked.Increment(ref _tempProcedureCounter);
        await connection.ExecuteNonQueryAsync($"DROP PROCEDURE IF EXISTS {procedureName} CASCADE");
        return procedureName;
    }

    /// <summary>
    /// Generates a unique type name, usable for a single test.
    /// Actual creation of the type is the responsibility of the caller.
    /// </summary>
    internal static async Task<string> GetTempTypeName(PgSqlConnection conn)
    {
        var typeName = "temp_type" + Interlocked.Increment(ref _tempTypeCounter);
        await conn.ExecuteNonQueryAsync($"DROP TYPE IF EXISTS {typeName} CASCADE");
        return typeName;
    }

    internal static volatile int _tempTableCounter;
    static volatile int _tempViewCounter;
    static volatile int _tempFunctionCounter;
    static volatile int _tempProcedureCounter;
    static volatile int _tempSchemaCounter;
    static volatile int _tempTypeCounter;

    /// <summary>
    /// Creates a pool with a unique application name, usable for a single test, and returns an
    /// <see cref="IDisposable"/> to drop it at the end of the test.
    /// </summary>
    internal static IDisposable CreateTempPool(string origConnectionString, out string tempConnectionString)
        => CreateTempPool(new PgSqlConnectionStringBuilder(origConnectionString), out tempConnectionString);

    /// <summary>
    /// Creates a pool with a unique application name, usable for a single test, and returns an
    /// <see cref="IDisposable"/> to drop it at the end of the test.
    /// </summary>
    internal static IDisposable CreateTempPool(PgSqlConnectionStringBuilder builder, out string tempConnectionString)
    {
        builder.ApplicationName = (builder.ApplicationName ?? "TempPool") + Interlocked.Increment(ref _tempPoolCounter);
        tempConnectionString = builder.ConnectionString;
        return new PoolDisposer(tempConnectionString);
    }

    static volatile int _tempPoolCounter;

    readonly struct PoolDisposer : IDisposable
    {
        readonly string _connectionString;

        internal PoolDisposer(string connectionString) => _connectionString = connectionString;

        public void Dispose()
        {
            var conn = new PgSqlConnection(_connectionString);
            PgSqlConnection.ClearPool(conn);
        }
    }

    /// <summary>
    /// Utility to generate a bytea literal in Postgresql hex format
    /// See https://www.postgresql.org/docs/current/static/datatype-binary.html
    /// </summary>
    internal static string EncodeByteaHex(ICollection<byte> buf)
    {
        var hex = new StringBuilder(@"E'\\x", buf.Count * 2 + 3);
        foreach (var b in buf)
            hex.Append($"{b:x2}");
        hex.Append("'");
        return hex.ToString();
    }

    internal static IDisposable SetEnvironmentVariable(string name, string value)
    {
        var oldValue = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable(name, value);
        return new DeferredExecutionDisposable(() => Environment.SetEnvironmentVariable(name, oldValue));
    }

    internal static IDisposable SetCurrentCulture(CultureInfo culture)
    {
        var oldCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = culture;

        return new DeferredExecutionDisposable(() => CultureInfo.CurrentCulture = oldCulture);
    }

    internal static IDisposable DisableSqlRewriting()
    {
#if DEBUG
        PgSqlCommand.EnableSqlRewriting = false;
        return new DeferredExecutionDisposable(() => PgSqlCommand.EnableSqlRewriting = true);
#else
        Assert.Skip("Cannot disable SQL rewriting in RELEASE builds");
        throw new NotSupportedException("Cannot disable SQL rewriting in RELEASE builds");
#endif
    }

    class DeferredExecutionDisposable : IDisposable
    {
        readonly Action _action;

        internal DeferredExecutionDisposable(Action action) => _action = action;

        public void Dispose()
            => _action();
    }

    internal static object AssertLoggingStateContains(
        (LogLevel Level, EventId Id, string Message, object State, Exception Exception) log,
        string key)
    {
        if (log.State is not IEnumerable<KeyValuePair<string, object>> keyValuePairs || keyValuePairs.All(kvp => kvp.Key != key))
        {
            Assert.Fail($@"Did not find logging state key ""{key}""");
            throw new Exception();
        }

        return keyValuePairs.Single(kvp => kvp.Key == key).Value;
    }

    internal static void AssertLoggingStateContains<T>(
        (LogLevel Level, EventId Id, string Message, object State, Exception Exception) log,
        string key,
        T value)
    {
        // Compared by value (NUnit-style), so that array values such as logged parameter lists match element-wise.
        var keyValuePairs = log.State as IEnumerable<KeyValuePair<string, object>>;
        keyValuePairs.Should().NotBeNull("the logging state should be a key/value list");
        keyValuePairs.Any(kvp => kvp.Key == key && ValueEquality.AreEqual(value, kvp.Value)).Should().BeTrue(
            $@"the logging state should contain (""{key}"", {ValueEquality.Format(value)}), but contained " +
            string.Join(", ", keyValuePairs.Select(kvp => $"({kvp.Key}, {ValueEquality.Format(kvp.Value)})")));
    }

    internal static void AssertLoggingStateDoesNotContain(
        (LogLevel Level, EventId Id, string Message, object State, Exception Exception) log,
        string key)
    {
        var value = log.State is IEnumerable<KeyValuePair<string, object>> keyValuePairs &&
                    keyValuePairs.FirstOrDefault(kvp => kvp.Key == key) is { } kvpPair
            ? kvpPair.Value
            : null;

        value.Should().BeNull($@"found logging state (""{key}"", {value})");
    }
}

public static class PgSqlConnectionExtensions
{
    public static int ExecuteNonQuery(this PgSqlConnection conn, string sql, PgSqlTransaction tx = null)
    {
        using var command = tx == null ? new PgSqlCommand(sql, conn) : new PgSqlCommand(sql, conn, tx);
        return command.ExecuteNonQuery();
    }

    public static object ExecuteScalar(this PgSqlConnection conn, string sql, PgSqlTransaction tx = null)
    {
        using var command = tx == null ? new PgSqlCommand(sql, conn) : new PgSqlCommand(sql, conn, tx);
        return command.ExecuteScalar();
    }

    public static async Task<int> ExecuteNonQueryAsync(
        this PgSqlConnection conn, string sql, PgSqlTransaction tx = null, CancellationToken cancellationToken = default)
    {
        await using var command = tx == null ? new PgSqlCommand(sql, conn) : new PgSqlCommand(sql, conn, tx);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public static async Task<object> ExecuteScalarAsync(
        this PgSqlConnection conn, string sql, PgSqlTransaction tx = null, CancellationToken cancellationToken = default)
    {
        await using var command = tx == null ? new PgSqlCommand(sql, conn) : new PgSqlCommand(sql, conn, tx);
        return await command.ExecuteScalarAsync(cancellationToken);
    }
}

public static class PgSqlDataSourceExtensions
{
    public static int ExecuteNonQuery(this PgSqlDataSource dataSource, string sql)
    {
        using var command = dataSource.CreateCommand(sql);
        return command.ExecuteNonQuery();
    }

    public static object ExecuteScalar(this PgSqlDataSource dataSource, string sql)
    {
        using var command = dataSource.CreateCommand(sql);
        return command.ExecuteScalar();
    }

    public static async Task<int> ExecuteNonQueryAsync(
        this PgSqlDataSource dataSource, string sql, CancellationToken cancellationToken = default)
    {
        await using var command = dataSource.CreateCommand(sql);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public static async Task<object> ExecuteScalarAsync(
        this PgSqlDataSource dataSource, string sql, CancellationToken cancellationToken = default)
    {
        await using var command = dataSource.CreateCommand(sql);
        return await command.ExecuteScalarAsync(cancellationToken);
    }
}

public static class CommandBehaviorExtensions
{
    public static bool IsSequential(this CommandBehavior behavior)
        => (behavior & CommandBehavior.SequentialAccess) != 0;
}

public static class PgSqlCommandExtensions
{
    public static void WaitUntilCommandIsInProgress(this PgSqlCommand command)
    {
        while (command.State != CommandState.InProgress)
            Thread.Sleep(50);
    }
}

/// <summary>
/// Semantic attribute that points to an issue linked with this test (e.g. this
/// test reproduces the issue)
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public class IssueLink(string linkAddress) : Attribute
{
    public string LinkAddress { get; private set; } = linkAddress;
}

public enum PrepareOrNot
{
    Prepared,
    NotPrepared
}

public enum PooledOrNot
{
    Pooled,
    Unpooled
}
