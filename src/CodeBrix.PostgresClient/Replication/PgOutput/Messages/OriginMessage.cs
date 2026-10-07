using System;
using CodeBrix.PostgresClient.PgSqlTypes;

namespace CodeBrix.PostgresClient.Replication.PgOutput.Messages; //was previously: Npgsql.Replication.PgOutput.Messages;

/// <summary>
/// Logical Replication Protocol origin message
/// </summary>
public sealed class OriginMessage : PgOutputReplicationMessage
{
    /// <summary>
    /// The LSN of the commit on the origin server.
    /// </summary>
    public PgSqlLogSequenceNumber OriginCommitLsn { get; private set; }

    /// <summary>
    /// Name of the origin.
    /// </summary>
    public string OriginName { get; private set; } = string.Empty;

    internal OriginMessage() {}

    internal OriginMessage Populate(
        PgSqlLogSequenceNumber walStart, PgSqlLogSequenceNumber walEnd, DateTime serverClock, PgSqlLogSequenceNumber originCommitLsn,
        string originName)
    {
        base.Populate(walStart, walEnd, serverClock);

        OriginCommitLsn = originCommitLsn;
        OriginName = originName;

        return this;
    }
}
