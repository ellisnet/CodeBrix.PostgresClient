using CodeBrix.PostgresClient.Internal;

namespace CodeBrix.PostgresClient.BackendMessages; //was previously: Npgsql.BackendMessages;

sealed class BackendKeyDataMessage : IBackendMessage
{
    public BackendMessageCode Code => BackendMessageCode.BackendKeyData;

    internal int BackendProcessId { get; }
    internal int BackendSecretKey { get; }

    internal BackendKeyDataMessage(PgSqlReadBuffer buf)
    {
        BackendProcessId = buf.ReadInt32();
        BackendSecretKey = buf.ReadInt32();
    }
}
