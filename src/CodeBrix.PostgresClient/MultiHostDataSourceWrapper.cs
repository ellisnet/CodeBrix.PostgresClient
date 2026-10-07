using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using System.Transactions;
using CodeBrix.PostgresClient.Internal;
using CodeBrix.PostgresClient.Util;

namespace CodeBrix.PostgresClient; //was previously: Npgsql;

sealed class MultiHostDataSourceWrapper(PgSqlMultiHostDataSource wrappedSource, TargetSessionAttributes targetSessionAttributes)
    : PgSqlDataSource(CloneSettingsForTargetSessionAttributes(wrappedSource.Settings, targetSessionAttributes), wrappedSource.Configuration, reportMetrics: false)
{
    internal PgSqlMultiHostDataSource WrappedSource { get; } = wrappedSource;

    internal override bool OwnsConnectors => false;

    public override void Clear() => WrappedSource.Clear();

    static PgSqlConnectionStringBuilder CloneSettingsForTargetSessionAttributes(
        PgSqlConnectionStringBuilder settings,
        TargetSessionAttributes targetSessionAttributes)
    {
        var clonedSettings = settings.Clone();
        clonedSettings.TargetSessionAttributesParsed = targetSessionAttributes;
        return clonedSettings;
    }

    internal override (int Total, int Idle, int Busy) Statistics => WrappedSource.Statistics;

    internal override ValueTask<PgSqlConnector> Get(PgSqlConnection conn, PgSqlTimeout timeout, bool async, CancellationToken cancellationToken)
        => WrappedSource.Get(conn, timeout, async, cancellationToken);
    internal override bool TryGetIdleConnector([NotNullWhen(true)] out PgSqlConnector connector)
        => throw new PgSqlException("PgSql bug: trying to get an idle connector from " + nameof(MultiHostDataSourceWrapper));
    internal override ValueTask<PgSqlConnector> OpenNewConnector(PgSqlConnection conn, PgSqlTimeout timeout, bool async, CancellationToken cancellationToken)
        => throw new PgSqlException("PgSql bug: trying to open a new connector from " + nameof(MultiHostDataSourceWrapper));
    internal override void Return(PgSqlConnector connector)
        => WrappedSource.Return(connector);

    internal override void AddPendingEnlistedConnector(PgSqlConnector connector, Transaction transaction)
        => WrappedSource.AddPendingEnlistedConnector(connector, transaction);
    internal override bool TryRemovePendingEnlistedConnector(PgSqlConnector connector, Transaction transaction)
        => WrappedSource.TryRemovePendingEnlistedConnector(connector, transaction);
    internal override bool TryRentEnlistedPending(Transaction transaction, PgSqlConnection connection,
        [NotNullWhen(true)] out PgSqlConnector connector)
        => WrappedSource.TryRentEnlistedPending(transaction, connection, out connector);
}
