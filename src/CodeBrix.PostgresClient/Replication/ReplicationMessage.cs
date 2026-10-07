using System;
using CodeBrix.PostgresClient.PgSqlTypes;

namespace CodeBrix.PostgresClient.Replication; //was previously: Npgsql.Replication;

/// <summary>
/// The common base class for all streaming replication messages
/// </summary>
public abstract class ReplicationMessage
{
    /// <summary>
    /// The starting point of the WAL data in this message.
    /// </summary>
    public PgSqlLogSequenceNumber WalStart { get; private set; }

    /// <summary>
    /// The current end of WAL on the server.
    /// </summary>
    public PgSqlLogSequenceNumber WalEnd { get; private set; }

    /// <summary>
    /// The server's system clock at the time this message was transmitted, as microseconds since midnight on 2000-01-01.
    /// </summary>
    /// <remarks>
    /// Since the client using CodeBrix.PostgresClient and the server may be located in different time zones,
    /// as of CodeBrix.PostgresClient 7.0 this value is no longer converted to local time but keeps its original value in UTC.
    /// You can check <see cref="DateTime.Kind"/> if you don't want to introduce behavior depending on CodeBrix.PostgresClient versions.
    /// </remarks>
    public DateTime ServerClock { get; private set; }

    private protected void Populate(PgSqlLogSequenceNumber walStart, PgSqlLogSequenceNumber walEnd, DateTime serverClock)
    {
        WalStart = walStart;
        WalEnd = walEnd;
        ServerClock = serverClock;
    }
}
