using System;
using CodeBrix.PostgresClient.PgSqlTypes;

namespace CodeBrix.PostgresClient.Replication.PgOutput.Messages; //was previously: Npgsql.Replication.PgOutput.Messages;

/// <summary>
/// Logical Replication Protocol commit prepared message
/// </summary>
public sealed class CommitPreparedMessage : PreparedTransactionControlMessage
{
    /// <summary>
    /// Flags for the commit prepared; currently unused.
    /// </summary>
    public CommitPreparedFlags Flags { get; private set; }

    /// <summary>
    /// The LSN of the commit prepared.
    /// </summary>
    public PgSqlLogSequenceNumber CommitPreparedLsn => FirstLsn;

    /// <summary>
    /// The end LSN of the commit prepared transaction.
    /// </summary>
    public PgSqlLogSequenceNumber CommitPreparedEndLsn => SecondLsn;

    /// <summary>
    /// Commit timestamp of the transaction.
    /// </summary>
    public DateTime TransactionCommitTimestamp => Timestamp;

    internal CommitPreparedMessage() {}

    internal CommitPreparedMessage Populate(
        PgSqlLogSequenceNumber walStart, PgSqlLogSequenceNumber walEnd, DateTime serverClock, CommitPreparedFlags flags,
        PgSqlLogSequenceNumber commitPreparedLsn, PgSqlLogSequenceNumber commitPreparedEndLsn, DateTime transactionCommitTimestamp,
        uint transactionXid, string transactionGid)
    {
        base.Populate(walStart, walEnd, serverClock,
            firstLsn: commitPreparedLsn,
            secondLsn: commitPreparedEndLsn,
            timestamp: transactionCommitTimestamp,
            transactionXid: transactionXid,
            transactionGid: transactionGid);
        Flags = flags;
        return this;
    }

    /// <summary>
    /// Flags for the commit prepared; currently unused.
    /// </summary>
    [Flags]
    public enum CommitPreparedFlags : byte
    {
        /// <summary>
        /// No flags.
        /// </summary>
        None = 0
    }
}
