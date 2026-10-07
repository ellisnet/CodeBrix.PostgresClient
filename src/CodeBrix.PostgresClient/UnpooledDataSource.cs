using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.PostgresClient.Internal;
using CodeBrix.PostgresClient.Util;

namespace CodeBrix.PostgresClient; //was previously: Npgsql;

sealed class UnpooledDataSource(PgSqlConnectionStringBuilder settings, PgSqlDataSourceConfiguration dataSourceConfig)
    : PgSqlDataSource(settings, dataSourceConfig, reportMetrics: true)
{
    volatile int _numConnectors;

    internal override (int Total, int Idle, int Busy) Statistics => (_numConnectors, 0, _numConnectors);

    internal override bool OwnsConnectors => true;

    internal override async ValueTask<PgSqlConnector> Get(
        PgSqlConnection conn, PgSqlTimeout timeout, bool async, CancellationToken cancellationToken)
    {
        CheckDisposed();

        var connector = new PgSqlConnector(this, conn);
        await connector.Open(timeout, async, cancellationToken).ConfigureAwait(false);
        Interlocked.Increment(ref _numConnectors);
        return connector;
    }

    internal override bool TryGetIdleConnector([NotNullWhen(true)] out PgSqlConnector connector)
    {
        connector = null;
        return false;
    }

    internal override ValueTask<PgSqlConnector> OpenNewConnector(
        PgSqlConnection conn, PgSqlTimeout timeout, bool async, CancellationToken cancellationToken)
        => new((PgSqlConnector)null);

    internal override void Return(PgSqlConnector connector)
    {
        Interlocked.Decrement(ref _numConnectors);
        connector.Close();
    }

    public override void Clear()
    {
    }
}
