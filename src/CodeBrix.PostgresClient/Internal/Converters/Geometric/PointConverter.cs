using CodeBrix.PostgresClient.PgSqlTypes;

// ReSharper disable once CheckNamespace
namespace CodeBrix.PostgresClient.Internal.Converters; //was previously: Npgsql.Internal.Converters;

sealed class PointConverter : PgBufferedConverter<PgSqlPoint>
{
    public override bool CanConvert(DataFormat format, out BufferRequirements bufferRequirements)
    {
        bufferRequirements = BufferRequirements.CreateFixedSize(sizeof(double) * 2);
        return format is DataFormat.Binary;
    }

    protected override PgSqlPoint ReadCore(PgReader reader)
        => new(reader.ReadDouble(), reader.ReadDouble());

    protected override void WriteCore(PgWriter writer, PgSqlPoint value)
    {
        writer.WriteDouble(value.X);
        writer.WriteDouble(value.Y);
    }
}
