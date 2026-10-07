namespace CodeBrix.PostgresClient; //was previously: Npgsql;

/// <summary>
/// Contains the <see cref="Microsoft.Extensions.Logging.EventId"/> numeric identifiers used by the log messages emitted by this library,
/// grouped by area (connection, command, transaction, copy and replication).
/// </summary>
public static class PgSqlEventId
{
    #region Connection

    /// <summary>Logged when a connection open is starting.</summary>
    public const int OpeningConnection    = 1000;
    /// <summary>Logged when a connection has been opened.</summary>
    public const int OpenedConnection     = 1001;
    /// <summary>Logged when a connection close is starting.</summary>
    public const int ClosingConnection    = 1003;
    /// <summary>Logged when a connection has been closed.</summary>
    public const int ClosedConnection     = 1004;

    /// <summary>Logged when a new physical (network) connection to the server is being opened.</summary>
    public const int OpeningPhysicalConnection = 1110;
    /// <summary>Logged when a new physical (network) connection to the server has been opened.</summary>
    public const int OpenedPhysicalConnection  = 1111;
    /// <summary>Logged when a physical (network) connection to the server is being closed.</summary>
    public const int ClosingPhysicalConnection = 1112;
    /// <summary>Logged when a physical (network) connection to the server has been closed.</summary>
    public const int ClosedPhysicalConnection  = 1113;

    /// <summary>Logged when the connection starts waiting for asynchronous messages (e.g. notifications).</summary>
    public const int StartingWait   = 1300;
    /// <summary>Logged when a notice message is received from the server.</summary>
    public const int ReceivedNotice = 1301;

    /// <summary>Logged when a pooled connection is closed because it exceeded its configured maximum lifetime.</summary>
    public const int ConnectionExceededMaximumLifetime = 1500;

    /// <summary>Logged when a keepalive query is being sent to the server.</summary>
    public const int SendingKeepalive   = 1600;
    /// <summary>Logged when a keepalive round trip has completed.</summary>
    public const int CompletedKeepalive = 1601;
    /// <summary>Logged when a keepalive round trip failed.</summary>
    public const int KeepaliveFailed    = 1602;

    /// <summary>Logged when a connection is broken because of an unrecoverable error.</summary>
    public const int BreakingConnection                            = 1900;
    /// <summary>Logged when a user-supplied notice event handler threw an exception.</summary>
    public const int CaughtUserExceptionInNoticeEventHandler       = 1901;
    /// <summary>Logged when a user-supplied notification event handler threw an exception.</summary>
    public const int CaughtUserExceptionInNotificationEventHandler = 1902;
    /// <summary>Logged when an exception occurs while closing a physical connection.</summary>
    public const int ExceptionWhenClosingPhysicalConnection        = 1903;
    /// <summary>Logged when an exception occurs while opening a connection for multiplexing.</summary>
    public const int ExceptionWhenOpeningConnectionForMultiplexing = 1904;

    #endregion Connection

    #region Command

    /// <summary>Logged when a command is about to be executed.</summary>
    public const int ExecutingCommand          = 2000;
    /// <summary>Logged when a command has finished executing.</summary>
    public const int CommandExecutionCompleted = 2001;
    /// <summary>Logged when cancellation of an in-progress command is requested.</summary>
    public const int CancellingCommand         = 2002;
    /// <summary>Logged when an internal command (issued by the driver itself) is executed.</summary>
    public const int ExecutingInternalCommand  = 2003;

    /// <summary>Logged when a command is being explicitly prepared.</summary>
    public const int PreparingCommandExplicitly = 2100;
    /// <summary>Logged when a command has been explicitly prepared.</summary>
    public const int CommandPreparedExplicitly  = 2101;
    /// <summary>Logged when a frequently used statement is automatically prepared.</summary>
    public const int AutoPreparingStatement     = 2102;
    /// <summary>Logged when a prepared command is being unprepared.</summary>
    public const int UnpreparingCommand         = 2103;

    /// <summary>Logged when command parameters are being derived from the server.</summary>
    public const int DerivingParameters = 2500;

    /// <summary>Logged when an exception occurs while writing multiplexed commands to a connection.</summary>
    public const int ExceptionWhenWritingMultiplexedCommands = 2600;

    #endregion Command

    #region Transaction

    /// <summary>Logged when a transaction has been started.</summary>
    public const int StartedTransaction    = 30000;
    /// <summary>Logged when a transaction has been committed.</summary>
    public const int CommittedTransaction  = 30001;
    /// <summary>Logged when a transaction has been rolled back.</summary>
    public const int RolledBackTransaction = 30002;

    /// <summary>Logged when a savepoint is being created.</summary>
    public const int CreatingSavepoint      = 30100;
    /// <summary>Logged when a transaction has been rolled back to a savepoint.</summary>
    public const int RolledBackToSavepoint  = 30101;
    /// <summary>Logged when a savepoint has been released.</summary>
    public const int ReleasedSavepoint      = 30102;

    /// <summary>Logged when an exception occurs while disposing a transaction.</summary>
    public const int ExceptionDuringTransactionDispose = 30200;

    /// <summary>Logged when the connection enlists as a volatile resource manager in an ambient System.Transactions transaction.</summary>
    public const int EnlistedVolatileResourceManager      = 31000;
    /// <summary>Logged when an enlisted transaction is being committed with a single-phase commit.</summary>
    public const int CommittingSinglePhaseTransaction     = 31001;
    /// <summary>Logged when an enlisted single-phase transaction is being rolled back.</summary>
    public const int RollingBackSinglePhaseTransaction    = 31002;
    /// <summary>Logged when rolling back an enlisted single-phase transaction failed.</summary>
    public const int SinglePhaseTransactionRollbackFailed = 31003;
    /// <summary>Logged when an enlisted transaction is being prepared for a two-phase commit.</summary>
    public const int PreparingTwoPhaseTransaction         = 31004;
    /// <summary>Logged when a prepared two-phase transaction is being committed.</summary>
    public const int CommittingTwoPhaseTransaction        = 31005;
    /// <summary>Logged when committing a prepared two-phase transaction failed.</summary>
    public const int TwoPhaseTransactionCommitFailed      = 31006;
    /// <summary>Logged when a prepared two-phase transaction is being rolled back.</summary>
    public const int RollingBackTwoPhaseTransaction       = 31007;
    /// <summary>Logged when rolling back a prepared two-phase transaction failed.</summary>
    public const int TwoPhaseTransactionRollbackFailed    = 31008;
    /// <summary>Logged when the outcome of a two-phase transaction is in doubt.</summary>
    public const int TwoPhaseTransactionInDoubt           = 31009;
    /// <summary>Logged when the connection is still in use while an enlisted transaction is being rolled back.</summary>
    public const int ConnectionInUseWhenRollingBack       = 31010;
    /// <summary>Logged when the volatile resource manager of an enlisted transaction is being cleaned up.</summary>
    public const int CleaningUpResourceManager            = 31011;

    #endregion Transaction

    #region Copy

    /// <summary>Logged when a binary COPY TO export is starting.</summary>
    public const int StartingBinaryExport = 40000;
    /// <summary>Logged when a binary COPY FROM import is starting.</summary>
    public const int StartingBinaryImport = 40001;
    /// <summary>Logged when a text COPY TO export is starting.</summary>
    public const int StartingTextExport   = 40002;
    /// <summary>Logged when a text COPY FROM import is starting.</summary>
    public const int StartingTextImport   = 40003;
    /// <summary>Logged when a raw binary COPY operation is starting.</summary>
    public const int StartingRawCopy      = 40004;

    /// <summary>Logged when a COPY operation has completed.</summary>
    public const int CopyOperationCompleted              = 40100;
    /// <summary>Logged when a COPY operation has been cancelled.</summary>
    public const int CopyOperationCancelled              = 40101;
    /// <summary>Logged when an exception occurs while disposing a COPY operation.</summary>
    public const int ExceptionWhenDisposingCopyOperation = 40102;

    #endregion Copy

    #region Replication

    /// <summary>Logged when a replication slot is being created.</summary>
    public const int CreatingReplicationSlot     = 50000;
    /// <summary>Logged when a replication slot is being dropped.</summary>
    public const int DroppingReplicationSlot     = 50001;
    /// <summary>Logged when logical replication is starting.</summary>
    public const int StartingLogicalReplication  = 50002;
    /// <summary>Logged when physical replication is starting.</summary>
    public const int StartingPhysicalReplication = 50003;
    /// <summary>Logged when a replication protocol command is being executed.</summary>
    public const int ExecutingReplicationCommand = 50004;

    /// <summary>Logged when a primary keepalive message is received on a replication connection.</summary>
    public const int ReceivedReplicationPrimaryKeepalive     = 50100;
    /// <summary>Logged when a standby status update is being sent on a replication connection.</summary>
    public const int SendingReplicationStandbyStatusUpdate   = 50101;
    /// <summary>Logged when a replication feedback message has been sent to the server.</summary>
    public const int SentReplicationFeedbackMessage          = 50102;
    /// <summary>Logged when sending a replication feedback message to the server failed.</summary>
    public const int ReplicationFeedbackMessageSendingFailed = 50103;

    #endregion Replication
}
