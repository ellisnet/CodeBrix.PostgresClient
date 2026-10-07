using System;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.PostgresClient.Properties;

namespace CodeBrix.PostgresClient; //was previously: Npgsql;

sealed class PgSqlDataSourceCommand : PgSqlCommand
{
    internal PgSqlDataSourceCommand(PgSqlConnection connection)
        : base(cmdText: null, connection)
    {
    }

    // For PgSqlBatch only
    internal PgSqlDataSourceCommand(PgSqlBatch batch, int batchCommandCapacity, PgSqlConnection connection)
        : base(batch, batchCommandCapacity, connection)
    {
    }

    internal override async ValueTask<PgSqlDataReader> ExecuteReader(
        bool async, CommandBehavior behavior,
        CancellationToken cancellationToken)
    {
        await InternalConnection.Open(async, cancellationToken).ConfigureAwait(false);

        try
        {
            return await base.ExecuteReader(
                    async,
                    behavior | CommandBehavior.CloseConnection,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            try
            {
                await InternalConnection.Close(async).ConfigureAwait(false);
            }
            catch
            {
                // Swallow to allow the original exception to bubble up
            }

            throw;
        }
    }

    // The below are incompatible with commands executed directly against DbDataSource, since no DbConnection
    // is involved at the user API level and the command owns the DbConnection.
    public override void Prepare()
        => throw new NotSupportedException(PgSqlStrings.NotSupportedOnDataSourceCommand);

    public override Task PrepareAsync(CancellationToken cancellationToken = default)
        => throw new NotSupportedException(PgSqlStrings.NotSupportedOnDataSourceCommand);

    protected override DbConnection DbConnection
    {
        get => throw new NotSupportedException(PgSqlStrings.NotSupportedOnDataSourceCommand);
        set => throw new NotSupportedException(PgSqlStrings.NotSupportedOnDataSourceCommand);
    }

    protected override DbTransaction DbTransaction
    {
        get => throw new NotSupportedException(PgSqlStrings.NotSupportedOnDataSourceCommand);
        set => throw new NotSupportedException(PgSqlStrings.NotSupportedOnDataSourceCommand);
    }
}
