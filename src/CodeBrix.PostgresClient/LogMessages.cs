using System;
using System.Collections.Generic;
using System.Data;
using CodeBrix.PostgresClient.PgSqlTypes;
using Microsoft.Extensions.Logging;

namespace CodeBrix.PostgresClient; //was previously: Npgsql;

// ReSharper disable InconsistentNaming

static partial class LogMessages
{
    #region Connection

    [LoggerMessage(
        EventId = PgSqlEventId.OpeningConnection,
        Level = LogLevel.Trace,
        Message = "Opening connection to {Host}:{Port}/{Database}...")]
    internal static partial void OpeningConnection(ILogger logger, string Host, int Port, string Database);

    [LoggerMessage(
        EventId = PgSqlEventId.OpenedConnection,
        Level = LogLevel.Debug,
        Message = "Opened connection to {Host}:{Port}/{Database} (connector {ConnectorId})")]
    internal static partial void OpenedConnection(ILogger logger, string Host, int Port, string Database, int ConnectorId);

    [LoggerMessage(
        EventId = PgSqlEventId.OpenedConnection,
        Level = LogLevel.Debug,
        Message = "Opened multiplexing connection to {Host}:{Port}/{Database}")]
    internal static partial void OpenedMultiplexingConnection(ILogger logger, string Host, int Port, string Database);

    [LoggerMessage(
        EventId = PgSqlEventId.ClosingConnection,
        Level = LogLevel.Trace,
        Message = "Closing connection to {Host}:{Port}/{Database} (connector {ConnectorId})...")]
    internal static partial void ClosingConnection(ILogger logger, string Host, int Port, string Database, int ConnectorId);

    [LoggerMessage(
        EventId = PgSqlEventId.ClosedConnection,
        Level = LogLevel.Debug,
        Message = "Closed connection to {Host}:{Port}/{Database} (connector {ConnectorId})")]
    internal static partial void ClosedConnection(ILogger logger, string Host, int Port, string Database, int ConnectorId);

    [LoggerMessage(
        EventId = PgSqlEventId.ClosedConnection,
        Level = LogLevel.Debug,
        Message = "Closed multiplexing connection to {Host}:{Port}/{Database}")]
    internal static partial void ClosedMultiplexingConnection(ILogger logger, string Host, int Port, string Database);

    [LoggerMessage(
        EventId = PgSqlEventId.OpeningPhysicalConnection,
        Level = LogLevel.Trace,
        Message = "Opening physical connection to {Host}:{Port}/{Database}...")]
    internal static partial void OpeningPhysicalConnection(ILogger logger, string Host, int Port, string Database);

    [LoggerMessage(
        EventId = PgSqlEventId.OpenedPhysicalConnection,
        Level = LogLevel.Debug,
        Message = "Opened physical connection to {Host}:{Port}/{Database} (in {DurationMs}ms, connector {ConnectorId})")]
    internal static partial void OpenedPhysicalConnection(ILogger logger, string Host, int Port, string Database, long DurationMs, int ConnectorId);

    [LoggerMessage(
        EventId = PgSqlEventId.ClosingPhysicalConnection,
        Level = LogLevel.Trace,
        Message = "Closing physical connection to {Host}:{Port}/{Database} (connector {ConnectorId})...")]
    internal static partial void ClosingPhysicalConnection(ILogger logger, string Host, int Port, string Database, int ConnectorId);

    [LoggerMessage(
        EventId = PgSqlEventId.ClosedPhysicalConnection,
        Level = LogLevel.Debug,
        Message = "Closed physical connection to {Host}:{Port}/{Database} (connector {ConnectorId})")]
    internal static partial void ClosedPhysicalConnection(ILogger logger, string Host, int Port, string Database, int ConnectorId);

    [LoggerMessage(
        EventId = PgSqlEventId.StartingWait,
        Level = LogLevel.Information,
        Message = "Starting to wait (timeout={TimeoutMs}ms, connector {ConnectorId})...")]
    internal static partial void StartingWait(ILogger logger, int TimeoutMs, int ConnectorId);

    [LoggerMessage(
        EventId = PgSqlEventId.ReceivedNotice,
        Level = LogLevel.Debug,
        Message = "Received notice: {NoticeText} (connector {ConnectorId})")]
    internal static partial void ReceivedNotice(ILogger logger, string NoticeText, int ConnectorId);

    [LoggerMessage(
        EventId = PgSqlEventId.ConnectionExceededMaximumLifetime,
        Level = LogLevel.Debug,
        Message = "Connection has exceeded its maximum lifetime ('{ConnectionMaximumLifeTime}') and will be closed. (connector {ConnectorId})")]
    internal static partial void ConnectionExceededMaximumLifetime(ILogger logger, TimeSpan ConnectionMaximumLifeTime, int ConnectorId);

    [LoggerMessage(
        EventId = PgSqlEventId.SendingKeepalive,
        Level = LogLevel.Trace,
        Message = "Sending keepalive (connector {ConnectorId})...")]
    internal static partial void SendingKeepalive(ILogger logger, int ConnectorId);

    [LoggerMessage(
        EventId = PgSqlEventId.CompletedKeepalive,
        Level = LogLevel.Trace,
        Message = "Completed keepalive (connector {ConnectorId})")]
    internal static partial void CompletedKeepalive(ILogger logger, int ConnectorId);

    [LoggerMessage(
        EventId = PgSqlEventId.KeepaliveFailed,
        Level = LogLevel.Trace,
        Message = "Keepalive failed (connector {ConnectorId})")]
    internal static partial void KeepaliveFailed(ILogger logger, int ConnectorId, Exception exception);

    [LoggerMessage(
        EventId = PgSqlEventId.BreakingConnection,
        Level = LogLevel.Trace,
        Message = "Breaking connection (connector {ConnectorId})")]
    internal static partial void BreakingConnection(ILogger logger, int ConnectorId, Exception exception);

    [LoggerMessage(
        EventId = PgSqlEventId.CaughtUserExceptionInNoticeEventHandler,
        Level = LogLevel.Error,
        Message = "User exception caught when emitting notice event")]
    internal static partial void CaughtUserExceptionInNoticeEventHandler(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = PgSqlEventId.CaughtUserExceptionInNotificationEventHandler,
        Level = LogLevel.Error,
        Message = "User exception caught when emitting notification event")]
    internal static partial void CaughtUserExceptionInNotificationEventHandler(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = PgSqlEventId.ExceptionWhenClosingPhysicalConnection,
        Level = LogLevel.Warning,
        Message = "Exception while closing connector (connector {ConnectorId})")]
    internal static partial void ExceptionWhenClosingPhysicalConnection(ILogger logger, int ConnectorId, Exception exception);

    [LoggerMessage(
        EventId = PgSqlEventId.ExceptionWhenOpeningConnectionForMultiplexing,
        Level = LogLevel.Error,
        Message = "Exception opening a connection for multiplexing")]
    internal static partial void ExceptionWhenOpeningConnectionForMultiplexing(ILogger logger, Exception exception);

    [LoggerMessage(
        Level = LogLevel.Trace,
        Message = "Start user action (connector {ConnectorId})")]
    internal static partial void StartUserAction(ILogger logger, int ConnectorId);

    [LoggerMessage(
        Level = LogLevel.Trace,
        Message = "End user action (connector {ConnectorId})")]
    internal static partial void EndUserAction(ILogger logger, int ConnectorId);

    #endregion Connection

    #region Command

    [LoggerMessage(
        EventId = PgSqlEventId.ExecutingCommand,
        Level = LogLevel.Debug,
        Message = "Executing command: {CommandText} (connector {ConnectorId})",
        SkipEnabledCheck = true)]
    internal static partial void ExecutingCommand(ILogger logger, string CommandText, int ConnectorId);

    [LoggerMessage(
        EventId = PgSqlEventId.ExecutingCommand,
        Level = LogLevel.Debug,
        Message = "Executing command: {CommandText} (connector {ConnectorId})\n  Parameters: {Parameters}",
        SkipEnabledCheck = true)]
    internal static partial void ExecutingCommandWithParameters(ILogger logger, string CommandText, IEnumerable<object> Parameters, int ConnectorId);

    [LoggerMessage(
        EventId = PgSqlEventId.ExecutingCommand,
        Level = LogLevel.Debug,
        Message = "Executing batch: {BatchCommands} (connector {ConnectorId})",
        SkipEnabledCheck = true)]
    internal static partial void ExecutingBatch(ILogger logger, string[] BatchCommands, int ConnectorId);

    [LoggerMessage(
        EventId = PgSqlEventId.ExecutingCommand,
        Level = LogLevel.Debug,
        Message = "Executing batch: {BatchCommands} (connector {ConnectorId})",
        SkipEnabledCheck = true)]
    internal static partial void ExecutingBatchWithParameters(ILogger logger, (string CommandText, IEnumerable<object> Parameters)[] BatchCommands, int ConnectorId);

    [LoggerMessage(
        EventId = PgSqlEventId.CommandExecutionCompleted,
        Level = LogLevel.Information,
        Message = "Command execution completed (duration={DurationMs}ms): {CommandText} (connector {ConnectorId})",
        SkipEnabledCheck = true)]
    internal static partial void CommandExecutionCompleted(ILogger logger, string CommandText, long DurationMs, int ConnectorId);

    [LoggerMessage(
        EventId = PgSqlEventId.CommandExecutionCompleted,
        Level = LogLevel.Information,
        Message = "Command execution completed (duration={DurationMs}ms): {CommandText} (connector {ConnectorId})\n  Parameters: {Parameters}",
        SkipEnabledCheck = true)]
    internal static partial void CommandExecutionCompletedWithParameters(ILogger logger, string CommandText, IEnumerable<object> Parameters, long DurationMs, int ConnectorId);

    [LoggerMessage(
        EventId = PgSqlEventId.CommandExecutionCompleted,
        Level = LogLevel.Information,
        Message = "Batch execution completed (duration={DurationMs}ms): {BatchCommands} (connector {ConnectorId})",
        SkipEnabledCheck = true)]
    internal static partial void BatchExecutionCompleted(ILogger logger, string[] BatchCommands, long DurationMs, int ConnectorId);

    [LoggerMessage(
        EventId = PgSqlEventId.CommandExecutionCompleted,
        Level = LogLevel.Information,
        Message = "Batch execution completed (duration={DurationMs}ms): {BatchCommands} (connector {ConnectorId})",
        SkipEnabledCheck = true)]
    internal static partial void BatchExecutionCompletedWithParameters(
        ILogger logger, (string CommandText, IEnumerable<object> Parameters)[] BatchCommands, long DurationMs, int ConnectorId);

    [LoggerMessage(
        EventId = PgSqlEventId.CancellingCommand,
        Level = LogLevel.Debug,
        Message = "Sending PostgreSQL cancellation (connector {ConnectorId})...")]
    internal static partial void CancellingCommand(ILogger logger, int ConnectorId);

    [LoggerMessage(
        EventId = PgSqlEventId.ExecutingInternalCommand,
        Level = LogLevel.Debug,
        Message = "Executing internal command: {CommandText} (connector {ConnectorId})")]
    internal static partial void ExecutingInternalCommand(ILogger logger, string CommandText, int ConnectorId);

    [LoggerMessage(
        EventId = PgSqlEventId.PreparingCommandExplicitly,
        Level = LogLevel.Debug,
        Message = "Preparing command explicitly: {CommandText} (connector {ConnectorId})",
        SkipEnabledCheck = true)]
    internal static partial void PreparingCommandExplicitly(ILogger logger, string CommandText, int ConnectorId);

    [LoggerMessage(
        EventId = PgSqlEventId.CommandPreparedExplicitly,
        Level = LogLevel.Information,
        Message = "Prepared command explicitly (connector {ConnectorId})")]
    internal static partial void CommandPreparedExplicitly(ILogger logger, int ConnectorId);

    [LoggerMessage(
        EventId = PgSqlEventId.AutoPreparingStatement,
        Level = LogLevel.Debug,
        Message = "Auto-preparing statement: {CommandText} (connector {ConnectorId})")]
    internal static partial void AutoPreparingStatement(ILogger logger, string CommandText, int ConnectorId);

    [LoggerMessage(
        EventId = PgSqlEventId.UnpreparingCommand,
        Level = LogLevel.Debug,
        Message = "Prepared command explicitly (connector {ConnectorId})")]
    internal static partial void UnpreparingCommand(ILogger logger, int ConnectorId);

    [LoggerMessage(
        EventId = PgSqlEventId.DerivingParameters,
        Level = LogLevel.Debug,
        Message = "Deriving Parameters for query: {CommandText} (connector {ConnectorId})")]
    internal static partial void DerivingParameters(ILogger logger, string CommandText, int ConnectorId);

    [LoggerMessage(
        EventId = PgSqlEventId.ExceptionWhenWritingMultiplexedCommands,
        Level = LogLevel.Error,
        Message = "Exception while writing multiplexed commands (connector {ConnectorId})")]
    internal static partial void ExceptionWhenWritingMultiplexedCommands(ILogger logger, int ConnectorId, Exception exception);

    [LoggerMessage(
        Level = LogLevel.Trace,
        Message = "Cleaning up reader (connector {ConnectorId})")]
    internal static partial void ReaderCleanup(ILogger logger, int ConnectorId);

    #endregion Command

    #region Transaction

    [LoggerMessage(
        EventId = PgSqlEventId.StartedTransaction,
        Level = LogLevel.Debug,
        Message = "Starting transaction (isolation level {IsolationLevel}, connector {ConnectorId})")]
    internal static partial void StartedTransaction(ILogger logger, IsolationLevel IsolationLevel, int ConnectorId);

    [LoggerMessage(
        EventId = PgSqlEventId.CommittedTransaction,
        Level = LogLevel.Debug,
        Message = "Committed transaction (connector {ConnectorId})")]
    internal static partial void CommittedTransaction(ILogger logger, int ConnectorId);

    [LoggerMessage(
        EventId = PgSqlEventId.RolledBackTransaction,
        Level = LogLevel.Debug,
        Message = "Rolled back transaction (connector {ConnectorId})")]
    internal static partial void RolledBackTransaction(ILogger logger, int ConnectorId);

    [LoggerMessage(
        EventId = PgSqlEventId.CreatingSavepoint,
        Level = LogLevel.Debug,
        Message = "Creating savepoint '{SavepointName}' (connector {ConnectorId})")]
    internal static partial void CreatingSavepoint(ILogger logger, string SavepointName, int ConnectorId);

    [LoggerMessage(
        EventId = PgSqlEventId.RolledBackToSavepoint,
        Level = LogLevel.Debug,
        Message = "Rolled back to savepoint '{SavepointName}' (connector {ConnectorId})")]
    internal static partial void RolledBackToSavepoint(ILogger logger, string SavepointName, int ConnectorId);

    [LoggerMessage(
        EventId = PgSqlEventId.ReleasedSavepoint,
        Level = LogLevel.Debug,
        Message = "Released savepoint '{SavepointName}' (connector {ConnectorId})")]
    internal static partial void ReleasedSavepoint(ILogger logger, string SavepointName, int ConnectorId);

    [LoggerMessage(
        EventId = PgSqlEventId.ExceptionDuringTransactionDispose,
        Level = LogLevel.Error,
        Message = "Exception while disposing transaction (connector {ConnectorId})")]
    internal static partial void ExceptionDuringTransactionDispose(ILogger logger, int ConnectorId, Exception exception);

    [LoggerMessage(
        EventId = PgSqlEventId.EnlistedVolatileResourceManager,
        Level = LogLevel.Debug,
        Message = "Enlisted volatile resource manager (local transaction ID={LocalTransactionIdentifier}, connector {ConnectorId})")]
    internal static partial void EnlistedVolatileResourceManager(ILogger logger, string LocalTransactionIdentifier, int ConnectorId);

    [LoggerMessage(
        EventId = PgSqlEventId.CommittingSinglePhaseTransaction,
        Level = LogLevel.Debug,
        Message = "Committing single-phase transaction (local ID={LocalTransactionIdentifier}, connector {ConnectorId})")]
    internal static partial void CommittingSinglePhaseTransaction(ILogger logger, string LocalTransactionIdentifier, int ConnectorId);

    [LoggerMessage(
        EventId = PgSqlEventId.RollingBackSinglePhaseTransaction,
        Level = LogLevel.Debug,
        Message = "Rolling back single-phase transaction (local ID={LocalTransactionIdentifier}, connector {ConnectorId})")]
    internal static partial void RollingBackSinglePhaseTransaction(ILogger logger, string LocalTransactionIdentifier, int ConnectorId);

    [LoggerMessage(
        EventId = PgSqlEventId.SinglePhaseTransactionRollbackFailed,
        Level = LogLevel.Error,
        Message = "Exception during single-phase transaction rollback (local ID={LocalTransactionIdentifier}, connector {ConnectorId})")]
    internal static partial void SinglePhaseTransactionRollbackFailed(ILogger logger, string LocalTransactionIdentifier, int ConnectorId);

    [LoggerMessage(
        EventId = PgSqlEventId.PreparingTwoPhaseTransaction,
        Level = LogLevel.Debug,
        Message = "Preparing two-phase transaction (local ID={LocalTransactionIdentifier}, connector {ConnectorId})")]
    internal static partial void PreparingTwoPhaseTransaction(ILogger logger, string LocalTransactionIdentifier, int ConnectorId);

    [LoggerMessage(
        EventId = PgSqlEventId.CommittingTwoPhaseTransaction,
        Level = LogLevel.Debug,
        Message = "Committing two-phase transaction (local ID={LocalTransactionIdentifier}, connector {ConnectorId})")]
    internal static partial void CommittingTwoPhaseTransaction(ILogger logger, string LocalTransactionIdentifier, int ConnectorId);

    [LoggerMessage(
        EventId = PgSqlEventId.TwoPhaseTransactionCommitFailed,
        Level = LogLevel.Error,
        Message = "Exception during two-phase transaction commit (local ID={LocalTransactionIdentifier}, connector {ConnectorId})")]
    internal static partial void TwoPhaseTransactionCommitFailed(ILogger logger, string LocalTransactionIdentifier, int ConnectorId, Exception exception);

    [LoggerMessage(
        EventId = PgSqlEventId.RollingBackTwoPhaseTransaction,
        Level = LogLevel.Debug,
        Message = "Rolling back two-phase transaction (local ID={LocalTransactionIdentifier}, connector {ConnectorId})")]
    internal static partial void RollingBackTwoPhaseTransaction(ILogger logger, string LocalTransactionIdentifier, int ConnectorId);

    [LoggerMessage(
        EventId = PgSqlEventId.TwoPhaseTransactionRollbackFailed,
        Level = LogLevel.Debug,
        Message = "Exception during two-phase transaction rollback (local ID={LocalTransactionIdentifier}, connector {ConnectorId})")]
    internal static partial void TwoPhaseTransactionRollbackFailed(ILogger logger, string LocalTransactionIdentifier, int ConnectorId, Exception exception);

    [LoggerMessage(
        EventId = PgSqlEventId.TwoPhaseTransactionInDoubt,
        Level = LogLevel.Warning,
        Message = "Two-phase transaction in doubt (local ID={LocalTransactionIdentifier}, connector {ConnectorId})")]
    internal static partial void TwoPhaseTransactionInDoubt(ILogger logger, string LocalTransactionIdentifier, int ConnectorId);

    [LoggerMessage(
        EventId = PgSqlEventId.ConnectionInUseWhenRollingBack,
        Level = LogLevel.Warning,
        Message = "Connection in use while trying to rollback, will cancel and retry (local ID={LocalTransactionIdentifier} (connector {ConnectorId})")]
    internal static partial void ConnectionInUseWhenRollingBack(ILogger logger, string LocalTransactionIdentifier, int ConnectorId);

    [LoggerMessage(
        EventId = PgSqlEventId.CleaningUpResourceManager,
        Level = LogLevel.Trace,
        Message = "Cleaning up resource manager (local ID={LocalTransactionIdentifier}, connector {ConnectorId})")]
    internal static partial void CleaningUpResourceManager(ILogger logger, string LocalTransactionIdentifier, int ConnectorId);

    #endregion Transaction

    #region Copy

    [LoggerMessage(
        EventId = PgSqlEventId.StartingBinaryExport,
        Level = LogLevel.Information,
        Message = "Starting binary export (connector {ConnectorId})")]
    internal static partial void StartingBinaryExport(ILogger logger, int ConnectorId);

    [LoggerMessage(
        EventId = PgSqlEventId.StartingBinaryImport,
        Level = LogLevel.Information,
        Message = "Starting binary import (connector {ConnectorId})")]
    internal static partial void StartingBinaryImport(ILogger logger, int ConnectorId);

    [LoggerMessage(
        EventId = PgSqlEventId.StartingTextExport,
        Level = LogLevel.Information,
        Message = "Starting text export (connector {ConnectorId})")]
    internal static partial void StartingTextExport(ILogger logger, int ConnectorId);

    [LoggerMessage(
        EventId = PgSqlEventId.StartingTextImport,
        Level = LogLevel.Information,
        Message = "Starting text import (connector {ConnectorId})")]
    internal static partial void StartingTextImport(ILogger logger, int ConnectorId);

    [LoggerMessage(
        EventId = PgSqlEventId.StartingRawCopy,
        Level = LogLevel.Information,
        Message = "Starting raw COPY operation (connector {ConnectorId})")]
    internal static partial void StartingRawCopy(ILogger logger, int ConnectorId);

    [LoggerMessage(
        EventId = PgSqlEventId.CopyOperationCompleted,
        Level = LogLevel.Information,
        Message = "Binary COPY operation completed ({Rows} rows transferred, connector {ConnectorId})")]
    internal static partial void BinaryCopyOperationCompleted(ILogger logger, ulong Rows, int ConnectorId);

    [LoggerMessage(
        EventId = PgSqlEventId.CopyOperationCompleted,
        Level = LogLevel.Information,
        Message = "COPY operation completed (connector {ConnectorId})")]
    internal static partial void CopyOperationCompleted(ILogger logger, int ConnectorId);

    [LoggerMessage(
        EventId = PgSqlEventId.CopyOperationCancelled,
        Level = LogLevel.Information,
        Message = "COPY operation was cancelled (connector {ConnectorId})")]
    internal static partial void CopyOperationCancelled(ILogger logger, int ConnectorId);

    [LoggerMessage(
        EventId = PgSqlEventId.ExceptionWhenDisposingCopyOperation,
        Level = LogLevel.Debug,
        Message = "Exception when disposing a COPY operation (connector {ConnectorId})")]
    internal static partial void ExceptionWhenDisposingCopyOperation(ILogger logger, int ConnectorId, Exception exception);

    #endregion Copy

    #region Replication

    [LoggerMessage(
        EventId = PgSqlEventId.CreatingReplicationSlot,
        Level = LogLevel.Information,
        Message = "Creating replication slot '{SlotName}': {CommandText} (connector {ConnectorId})")]
    internal static partial void CreatingReplicationSlot(ILogger logger, string SlotName, string CommandText, int ConnectorId);

    [LoggerMessage(
        EventId = PgSqlEventId.DroppingReplicationSlot,
        Level = LogLevel.Information,
        Message = "Dropping replication slot '{SlotName}': {CommandText} (connector {ConnectorId})")]
    internal static partial void DroppingReplicationSlot(ILogger logger, string SlotName, string CommandText, int ConnectorId);

    [LoggerMessage(
        EventId = PgSqlEventId.StartingLogicalReplication,
        Level = LogLevel.Information,
        Message = "Starting logical replication on slot '{SlotName}': {CommandText} (connector {ConnectorId})")]
    internal static partial void StartingLogicalReplication(ILogger logger, string SlotName, string CommandText, int ConnectorId);

    [LoggerMessage(
        EventId = PgSqlEventId.StartingPhysicalReplication,
        Level = LogLevel.Information,
        Message = "Starting physical replication on slot: '{SlotName}': {CommandText} (connector {ConnectorId})")]
    internal static partial void StartingPhysicalReplication(ILogger logger, string SlotName, string CommandText, int ConnectorId);

    [LoggerMessage(
        EventId = PgSqlEventId.ExecutingReplicationCommand,
        Level = LogLevel.Debug,
        Message = "Executing replication command: {CommandText} (connector {ConnectorId})")]
    internal static partial void ExecutingReplicationCommand(ILogger logger, string CommandText, int ConnectorId);

    [LoggerMessage(
        EventId = PgSqlEventId.ReceivedReplicationPrimaryKeepalive,
        Level = LogLevel.Trace,
        Message = "Received replication primary keepalive message from the server with current end of WAL of {EndLsn} and timestamp of {Timestamp} (connector {ConnectorId})")]
    internal static partial void ReceivedReplicationPrimaryKeepalive(ILogger logger, PgSqlLogSequenceNumber EndLsn, DateTime Timestamp, int ConnectorId);

    [LoggerMessage(
        EventId = PgSqlEventId.SendingReplicationStandbyStatusUpdate,
        Level = LogLevel.Trace,
        Message = "Sending a replication standby status update because {Reason} (connector {ConnectorId})")]
    internal static partial void SendingReplicationStandbyStatusUpdate(ILogger logger, string Reason, int ConnectorId);

    [LoggerMessage(
        EventId = PgSqlEventId.SentReplicationFeedbackMessage,
        Level = LogLevel.Trace,
        Message = "Feedback message sent with LastReceivedLsn={LastReceivedLsn}, LastFlushedLsn={LastFlushedLsn}, LastAppliedLsn={LastAppliedLsn}, Timestamp={Timestamp} (connector {ConnectorId})",
        SkipEnabledCheck = true)]
    internal static partial void SentReplicationFeedbackMessage(
        ILogger logger,
        PgSqlLogSequenceNumber LastReceivedLsn,
        PgSqlLogSequenceNumber LastFlushedLsn,
        PgSqlLogSequenceNumber LastAppliedLsn,
        DateTime Timestamp,
        int ConnectorId);

    [LoggerMessage(
        EventId = PgSqlEventId.ReplicationFeedbackMessageSendingFailed,
        Level = LogLevel.Error,
        Message = "An exception occurred while sending a feedback message (connector {ConnectorId})")]
    internal static partial void ReplicationFeedbackMessageSendingFailed(ILogger logger, int? ConnectorId, Exception exception);

    #endregion Replication
}
