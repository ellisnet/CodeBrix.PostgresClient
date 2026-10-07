using CodeBrix.PostgresClient.Internal;

namespace CodeBrix.PostgresClient.BackendMessages; //was previously: Npgsql.BackendMessages;

sealed class ReadyForQueryMessage : IBackendMessage
{
    public BackendMessageCode Code => BackendMessageCode.ReadyForQuery;

    internal TransactionStatus TransactionStatusIndicator { get; private set; }

    internal ReadyForQueryMessage Load(PgSqlReadBuffer buf) {
        TransactionStatusIndicator = (TransactionStatus)buf.ReadByte();
        return this;
    }
}
