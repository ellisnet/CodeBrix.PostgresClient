using System;
using CodeBrix.PostgresClient.PgSqlTypes;

namespace CodeBrix.PostgresClient.Replication.PgOutput.Messages; //was previously: Npgsql.Replication.PgOutput.Messages;

/// <summary>
/// Logical Replication Protocol rollback prepared message
/// </summary>
public sealed class RollbackPreparedMessage : PreparedTransactionControlMessage
{
    /// <summary>
    /// Flags for the rollback prepared; currently unused.
    /// </summary>
    public RollbackPreparedFlags Flags { get; private set; }

    /// <summary>
    /// The end LSN of the prepared transaction.
    /// </summary>
    public PgSqlLogSequenceNumber PreparedTransactionEndLsn => FirstLsn;

    /// <summary>
    /// The end LSN of the rollback prepared transaction.
    /// </summary>
    public PgSqlLogSequenceNumber RollbackPreparedEndLsn => SecondLsn;

    /// <summary>
    /// Prepare timestamp of the transaction.
    /// </summary>
    public DateTime TransactionPrepareTimestamp => Timestamp;

    /// <summary>
    /// Rollback timestamp of the transaction.
    /// </summary>
    public DateTime TransactionRollbackTimestamp { get; private set; }

    internal RollbackPreparedMessage() {}

    internal RollbackPreparedMessage Populate(
        PgSqlLogSequenceNumber walStart, PgSqlLogSequenceNumber walEnd, DateTime serverClock, RollbackPreparedFlags flags,
        PgSqlLogSequenceNumber preparedTransactionEndLsn, PgSqlLogSequenceNumber rollbackPreparedEndLsn, DateTime transactionPrepareTimestamp, DateTime transactionRollbackTimestamp,
        uint transactionXid, string transactionGid)
    {
        base.Populate(walStart, walEnd, serverClock,
            firstLsn: preparedTransactionEndLsn,
            secondLsn: rollbackPreparedEndLsn,
            timestamp: transactionPrepareTimestamp,
            transactionXid: transactionXid,
            transactionGid: transactionGid);
        Flags = flags;
        TransactionRollbackTimestamp = transactionRollbackTimestamp;
        return this;
    }

    /// <summary>
    /// Flags for the rollback prepared; currently unused.
    /// </summary>
    [Flags]
    public enum RollbackPreparedFlags : byte
    {
        /// <summary>
        /// No flags.
        /// </summary>
        None = 0
    }
}
