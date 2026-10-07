using CodeBrix.PostgresClient.PgSqlTypes;

// ReSharper disable once CheckNamespace
namespace CodeBrix.PostgresClient.Internal.Converters; //was previously: Npgsql.Internal.Converters;

sealed class TidConverter : PgBufferedConverter<PgSqlTid>
{
    public override bool CanConvert(DataFormat format, out BufferRequirements bufferRequirements)
    {
        bufferRequirements = BufferRequirements.CreateFixedSize(sizeof(uint) + sizeof(ushort));
        return format is DataFormat.Binary;
    }
    protected override PgSqlTid ReadCore(PgReader reader) => new(reader.ReadUInt32(), reader.ReadUInt16());
    protected override void WriteCore(PgWriter writer, PgSqlTid value)
    {
        writer.WriteUInt32(value.BlockNumber);
        writer.WriteUInt16(value.OffsetNumber);
    }
}
