using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.PostgresClient.PgSqlTypes;

// ReSharper disable once CheckNamespace
namespace CodeBrix.PostgresClient.Internal.Converters; //was previously: Npgsql.Internal.Converters;

sealed class PathConverter : PgStreamingConverter<PgSqlPath>
{
    public override PgSqlPath Read(PgReader reader)
        => Read(async: false, reader, CancellationToken.None).GetAwaiter().GetResult();

    public override ValueTask<PgSqlPath> ReadAsync(PgReader reader, CancellationToken cancellationToken = default)
        => Read(async: true, reader, cancellationToken);

    async ValueTask<PgSqlPath> Read(bool async, PgReader reader, CancellationToken cancellationToken)
    {
        if (reader.ShouldBuffer(sizeof(byte) + sizeof(int)))
            await reader.Buffer(async, sizeof(byte) + sizeof(int), cancellationToken).ConfigureAwait(false);

        var open = reader.ReadByte() switch
        {
            1 => false,
            0 => true,
            _ => throw new UnreachableException("Error decoding binary geometric path: bad open byte")
        };

        var numPoints = reader.ReadInt32();
        var result = new PgSqlPath(numPoints, open);

        for (var i = 0; i < numPoints; i++)
        {
            if (reader.ShouldBuffer(sizeof(double) * 2))
                await reader.Buffer(async, sizeof(double) * 2, cancellationToken).ConfigureAwait(false);

            result.Add(new PgSqlPoint(reader.ReadDouble(), reader.ReadDouble()));
        }

        return result;
    }

    public override Size GetSize(SizeContext context, PgSqlPath value, ref object writeState)
        => 5 + value.Count * sizeof(double) * 2;

    public override void Write(PgWriter writer, PgSqlPath value)
        => Write(async: false, writer, value, CancellationToken.None).GetAwaiter().GetResult();

    public override ValueTask WriteAsync(PgWriter writer, PgSqlPath value, CancellationToken cancellationToken = default)
        => Write(async: true, writer, value, cancellationToken);

    async ValueTask Write(bool async, PgWriter writer, PgSqlPath value, CancellationToken cancellationToken)
    {
        if (writer.ShouldFlush(sizeof(byte) + sizeof(int)))
            await writer.Flush(async, cancellationToken).ConfigureAwait(false);

        writer.WriteByte((byte)(value.Open ? 0 : 1));
        writer.WriteInt32(value.Count);

        foreach (var p in value)
        {
            if (writer.ShouldFlush(sizeof(double) * 2))
                await writer.Flush(async, cancellationToken).ConfigureAwait(false);
            writer.WriteDouble(p.X);
            writer.WriteDouble(p.Y);
        }
    }
}
