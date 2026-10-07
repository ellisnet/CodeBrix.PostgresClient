using System;
using CodeBrix.PostgresClient.PgSqlTypes;

namespace CodeBrix.PostgresClient.Replication.PgOutput.Messages; //was previously: Npgsql.Replication.PgOutput.Messages;

/// <summary>
/// Logical Replication Protocol stream stop message
/// </summary>
public sealed class StreamStopMessage : PgOutputReplicationMessage
{
    internal StreamStopMessage() {}

    internal new StreamStopMessage Populate(PgSqlLogSequenceNumber walStart, PgSqlLogSequenceNumber walEnd, DateTime serverClock)
    {
        base.Populate(walStart, walEnd, serverClock);
        return this;
    }
}
