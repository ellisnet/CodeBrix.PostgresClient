using System;
using CodeBrix.PostgresClient.PgSqlTypes;

namespace CodeBrix.PostgresClient.Replication.PgOutput.Messages; //was previously: Npgsql.Replication.PgOutput.Messages;

/// <summary>
/// Abstract base class for Logical Replication Protocol prepare and begin prepare message
/// </summary>
public abstract class PreparedTransactionControlMessage : TransactionControlMessage
{
    private protected PgSqlLogSequenceNumber FirstLsn;
    private protected PgSqlLogSequenceNumber SecondLsn;
    private protected DateTime Timestamp;

    /// <summary>
    /// The user defined GID of the two-phase transaction.
    /// </summary>
    public string TransactionGid { get; private set; } = null;

    private protected PreparedTransactionControlMessage() {}

    private protected PreparedTransactionControlMessage Populate(
        PgSqlLogSequenceNumber walStart, PgSqlLogSequenceNumber walEnd, DateTime serverClock,
        PgSqlLogSequenceNumber firstLsn, PgSqlLogSequenceNumber secondLsn, DateTime timestamp,
        uint transactionXid, string transactionGid)
    {
        base.Populate(walStart, walEnd, serverClock, transactionXid);

        FirstLsn = firstLsn;
        SecondLsn = secondLsn;
        Timestamp = timestamp;
        TransactionGid = transactionGid;

        return this;
    }
}
