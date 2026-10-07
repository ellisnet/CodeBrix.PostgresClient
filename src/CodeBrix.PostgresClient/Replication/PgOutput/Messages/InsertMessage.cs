using System;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.PostgresClient.Internal;
using CodeBrix.PostgresClient.PgSqlTypes;

namespace CodeBrix.PostgresClient.Replication.PgOutput.Messages; //was previously: Npgsql.Replication.PgOutput.Messages;

/// <summary>
/// Logical Replication Protocol insert message
/// </summary>
public sealed class InsertMessage : TransactionalMessage
{
    readonly ReplicationTuple _tupleEnumerable;

    /// <summary>
    /// The relation for this <see cref="InsertMessage" />.
    /// </summary>
    public RelationMessage Relation { get; private set; } = null;

    /// <summary>
    /// Columns representing the new row.
    /// </summary>
    public ReplicationTuple NewRow => _tupleEnumerable;

    internal InsertMessage(PgSqlConnector connector)
        => _tupleEnumerable = new(connector);

    internal InsertMessage Populate(
        PgSqlLogSequenceNumber walStart, PgSqlLogSequenceNumber walEnd, DateTime serverClock, uint? transactionXid,
        RelationMessage relation, ushort numColumns)
    {
        base.Populate(walStart, walEnd, serverClock, transactionXid);

        Relation = relation;
        _tupleEnumerable.Reset(numColumns, relation.RowDescription);

        return this;
    }

    internal Task Consume(CancellationToken cancellationToken)
        => _tupleEnumerable.Consume(cancellationToken);
}
