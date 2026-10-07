namespace CodeBrix.PostgresClient.BackendMessages; //was previously: Npgsql.BackendMessages;

sealed class ParseCompleteMessage : IBackendMessage
{
    public BackendMessageCode Code => BackendMessageCode.ParseComplete;
    internal static readonly ParseCompleteMessage Instance = new();
    ParseCompleteMessage() { }
}
