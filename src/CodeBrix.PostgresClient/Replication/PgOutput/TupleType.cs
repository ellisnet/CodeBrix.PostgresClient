namespace CodeBrix.PostgresClient.Replication.PgOutput; //was previously: Npgsql.Replication.PgOutput;

enum TupleType : byte
{
    Key = (byte)'K',
    NewTuple = (byte)'N',
    OldTuple = (byte)'O',
}
