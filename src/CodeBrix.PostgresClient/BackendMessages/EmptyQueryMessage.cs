namespace CodeBrix.PostgresClient.BackendMessages; //was previously: Npgsql.BackendMessages;

sealed class EmptyQueryMessage : IBackendMessage
{
    public BackendMessageCode Code => BackendMessageCode.EmptyQueryResponse;
    internal static readonly EmptyQueryMessage Instance = new();
    EmptyQueryMessage() { }
}
