using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.PostgresClient.Properties;

namespace CodeBrix.PostgresClient; //was previously: Npgsql;

sealed class PgSqlDataSourceBatch : PgSqlBatch
{
    internal PgSqlDataSourceBatch(PgSqlConnection connection)
        : base(static (conn, batch) => new PgSqlDataSourceCommand(batch, DefaultBatchCommandsSize, conn), connection)
    {
    }

    // The below are incompatible with batches executed directly against DbDataSource, since no DbConnection
    // is involved at the user API level and the batch owns the DbConnection.
    public override void Prepare()
        => throw new NotSupportedException(PgSqlStrings.NotSupportedOnDataSourceBatch);

    public override Task PrepareAsync(CancellationToken cancellationToken = default)
        => throw new NotSupportedException(PgSqlStrings.NotSupportedOnDataSourceBatch);

    protected override DbConnection DbConnection
    {
        get => throw new NotSupportedException(PgSqlStrings.NotSupportedOnDataSourceBatch);
        set => throw new NotSupportedException(PgSqlStrings.NotSupportedOnDataSourceBatch);
    }

    protected override DbTransaction DbTransaction
    {
        get => throw new NotSupportedException(PgSqlStrings.NotSupportedOnDataSourceBatch);
        set => throw new NotSupportedException(PgSqlStrings.NotSupportedOnDataSourceBatch);
    }
}
