using System;
using CodeBrix.PostgresClient.PgSqlTypes;

namespace CodeBrix.PostgresClient.Replication.PgOutput.Messages; //was previously: Npgsql.Replication.PgOutput.Messages;

/// <summary>
/// Logical Replication Protocol begin prepare message
/// </summary>
public sealed class BeginPrepareMessage : PrepareMessageBase
{
    internal BeginPrepareMessage() {}

    internal new BeginPrepareMessage Populate(
        PgSqlLogSequenceNumber walStart, PgSqlLogSequenceNumber walEnd, DateTime serverClock,
        PgSqlLogSequenceNumber prepareLsn, PgSqlLogSequenceNumber prepareEndLsn, DateTime transactionPrepareTimestamp,
        uint transactionXid, string transactionGid)
    {
        base.Populate(walStart, walEnd, serverClock,
            prepareLsn: prepareLsn,
            prepareEndLsn: prepareEndLsn,
            transactionPrepareTimestamp: transactionPrepareTimestamp,
            transactionXid: transactionXid,
            transactionGid: transactionGid);
        return this;
    }
}
