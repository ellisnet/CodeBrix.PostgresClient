using System;
using CodeBrix.PostgresClient.PgSqlTypes;

namespace CodeBrix.PostgresClient.Replication.PgOutput.Messages; //was previously: Npgsql.Replication.PgOutput.Messages;

/// <summary>
/// Logical Replication Protocol stream prepare message
/// </summary>
public sealed class StreamPrepareMessage : PrepareMessageBase
{
    /// <summary>
    /// Flags for the prepare; currently unused.
    /// </summary>
    public StreamPrepareFlags Flags { get; private set; }

    internal StreamPrepareMessage() {}

    internal StreamPrepareMessage Populate(
        PgSqlLogSequenceNumber walStart, PgSqlLogSequenceNumber walEnd, DateTime serverClock, StreamPrepareFlags flags,
        PgSqlLogSequenceNumber prepareLsn, PgSqlLogSequenceNumber prepareEndLsn, DateTime transactionPrepareTimestamp,
        uint transactionXid, string transactionGid)
    {
        base.Populate(walStart, walEnd, serverClock,
            prepareLsn: prepareLsn,
            prepareEndLsn: prepareEndLsn,
            transactionPrepareTimestamp: transactionPrepareTimestamp,
            transactionXid: transactionXid,
            transactionGid: transactionGid);
        Flags = flags;

        return this;
    }

    /// <summary>
    /// Flags for the prepare; currently unused.
    /// </summary>
    [Flags]
    public enum StreamPrepareFlags : byte
    {
        /// <summary>
        /// No flags.
        /// </summary>
        None = 0
    }
}
