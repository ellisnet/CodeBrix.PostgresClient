using System;
using CodeBrix.PostgresClient.PgSqlTypes;

namespace CodeBrix.PostgresClient.Replication.PgOutput.Messages; //was previously: Npgsql.Replication.PgOutput.Messages;

/// <summary>
/// Abstract base class for the logical replication protocol begin prepare and prepare message
/// </summary>
public abstract class PrepareMessageBase : PreparedTransactionControlMessage
{
    /// <summary>
    /// The LSN of the prepare.
    /// </summary>
    public PgSqlLogSequenceNumber PrepareLsn => FirstLsn;

    /// <summary>
    /// The end LSN of the prepared transaction.
    /// </summary>
    public PgSqlLogSequenceNumber PrepareEndLsn => SecondLsn;

    /// <summary>
    /// Prepare timestamp of the transaction.
    /// </summary>
    public DateTime TransactionPrepareTimestamp => Timestamp;

    private protected PrepareMessageBase() {}

    internal new PrepareMessageBase Populate(
        PgSqlLogSequenceNumber walStart, PgSqlLogSequenceNumber walEnd, DateTime serverClock,
        PgSqlLogSequenceNumber prepareLsn, PgSqlLogSequenceNumber prepareEndLsn, DateTime transactionPrepareTimestamp,
        uint transactionXid, string transactionGid)
    {
        base.Populate(walStart, walEnd, serverClock,
            firstLsn: prepareLsn,
            secondLsn: prepareEndLsn,
            timestamp: transactionPrepareTimestamp,
            transactionXid: transactionXid,
            transactionGid: transactionGid);
        return this;
    }
}
