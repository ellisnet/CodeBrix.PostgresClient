using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CodeBrix.PostgresClient; //was previously: Npgsql;

/// <summary>
/// Configures CodeBrix.PostgresClient logging
/// </summary>
public class PgSqlLoggingConfiguration
{
    internal static readonly PgSqlLoggingConfiguration NullConfiguration
        = new(NullLoggerFactory.Instance, isParameterLoggingEnabled: false);

    internal static ILoggerFactory GlobalLoggerFactory = NullLoggerFactory.Instance;
    internal static bool GlobalIsParameterLoggingEnabled;

    internal PgSqlLoggingConfiguration(ILoggerFactory loggerFactory, bool isParameterLoggingEnabled)
    {
        ConnectionLogger = loggerFactory.CreateLogger("PgSql.Connection");
        CommandLogger = loggerFactory.CreateLogger("PgSql.Command");
        TransactionLogger = loggerFactory.CreateLogger("PgSql.Transaction");
        CopyLogger = loggerFactory.CreateLogger("PgSql.Copy");
        ReplicationLogger = loggerFactory.CreateLogger("PgSql.Replication");
        ExceptionLogger = loggerFactory.CreateLogger("PgSql.Exception");

        IsParameterLoggingEnabled = isParameterLoggingEnabled;
    }

    internal ILogger ConnectionLogger { get; }
    internal ILogger CommandLogger { get; }
    internal ILogger TransactionLogger { get; }
    internal ILogger CopyLogger { get; }
    internal ILogger ReplicationLogger { get; }
    internal ILogger ExceptionLogger { get; }

    /// <summary>
    /// Determines whether parameter contents will be logged alongside SQL statements - this may reveal sensitive information.
    /// Defaults to false.
    /// </summary>
    internal bool IsParameterLoggingEnabled { get; }

    /// <summary>
    /// <para>
    /// Globally initializes CodeBrix.PostgresClient logging to use the provided <paramref name="loggerFactory" />.
    /// Must be called before any CodeBrix.PostgresClient APIs are used.
    /// </para>
    /// <para>
    /// This is a legacy-only, backwards compatibility API. New applications should set the logger factory on
    /// <see cref="PgSqlDataSourceBuilder" /> and use the resulting <see cref="PgSqlDataSource "/> instead.
    /// </para>
    /// </summary>
    /// <param name="loggerFactory">The logging factory to use when logging from CodeBrix.PostgresClient.</param>
    /// <param name="parameterLoggingEnabled">
    /// Determines whether parameter contents will be logged alongside SQL statements - this may reveal sensitive information.
    /// Defaults to <see langword="false" />.
    /// </param>
    public static void InitializeLogging(ILoggerFactory loggerFactory, bool parameterLoggingEnabled = false)
        => (GlobalLoggerFactory, GlobalIsParameterLoggingEnabled) = (loggerFactory, parameterLoggingEnabled);
}
