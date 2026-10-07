using CodeBrix.PostgresClient.PgSqlTypes;

// ReSharper disable once CheckNamespace
namespace CodeBrix.PostgresClient.Internal.Converters; //was previously: Npgsql.Internal.Converters;

sealed class BoxConverter : PgBufferedConverter<PgSqlBox>
{
    public override bool CanConvert(DataFormat format, out BufferRequirements bufferRequirements)
    {
        bufferRequirements = BufferRequirements.CreateFixedSize(sizeof(double) * 4);
        return format is DataFormat.Binary;
    }

    protected override PgSqlBox ReadCore(PgReader reader)
        => new(
            new PgSqlPoint(reader.ReadDouble(), reader.ReadDouble()),
            new PgSqlPoint(reader.ReadDouble(), reader.ReadDouble()));

    protected override void WriteCore(PgWriter writer, PgSqlBox value)
    {
        writer.WriteDouble(value.Right);
        writer.WriteDouble(value.Top);
        writer.WriteDouble(value.Left);
        writer.WriteDouble(value.Bottom);
    }
}
