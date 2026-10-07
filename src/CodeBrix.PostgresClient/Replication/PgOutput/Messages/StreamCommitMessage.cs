using System;
using CodeBrix.PostgresClient.PgSqlTypes;

namespace CodeBrix.PostgresClient.Replication.PgOutput.Messages; //was previously: Npgsql.Replication.PgOutput.Messages;

/// <summary>
/// Logical Replication Protocol stream commit message
/// </summary>
public sealed class StreamCommitMessage : TransactionControlMessage
{
    /// <summary>
    /// Flags; currently unused (must be 0).
    /// </summary>
    public byte Flags { get; private set; }

    /// <summary>
    /// The LSN of the commit.
    /// </summary>
    public PgSqlLogSequenceNumber CommitLsn { get; private set; }

    /// <summary>
    /// The end LSN of the transaction.
    /// </summary>
    public PgSqlLogSequenceNumber TransactionEndLsn { get; private set; }

    /// <summary>
    /// Commit timestamp of the transaction.
    /// </summary>
    public DateTime TransactionCommitTimestamp { get; private set; }

    internal StreamCommitMessage() {}

    internal StreamCommitMessage Populate(PgSqlLogSequenceNumber walStart, PgSqlLogSequenceNumber walEnd, DateTime serverClock,
        uint transactionXid, byte flags, PgSqlLogSequenceNumber commitLsn, PgSqlLogSequenceNumber transactionEndLsn, DateTime transactionCommitTimestamp)
    {
        base.Populate(walStart, walEnd, serverClock, transactionXid);
        Flags = flags;
        CommitLsn = commitLsn;
        TransactionEndLsn = transactionEndLsn;
        TransactionCommitTimestamp = transactionCommitTimestamp;
        return this;
    }
}
