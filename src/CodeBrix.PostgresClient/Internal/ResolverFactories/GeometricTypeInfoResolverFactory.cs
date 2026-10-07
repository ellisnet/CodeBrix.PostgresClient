using System;
using CodeBrix.PostgresClient.Internal.Converters;
using CodeBrix.PostgresClient.Internal.Postgres;
using CodeBrix.PostgresClient.PgSqlTypes;

namespace CodeBrix.PostgresClient.Internal.ResolverFactories; //was previously: Npgsql.Internal.ResolverFactories;

sealed class GeometricTypeInfoResolverFactory : PgTypeInfoResolverFactory
{
    public override IPgTypeInfoResolver CreateResolver() => new Resolver();
    public override IPgTypeInfoResolver CreateArrayResolver() => new ArrayResolver();

    class Resolver : IPgTypeInfoResolver
    {
        TypeInfoMappingCollection _mappings;
        protected TypeInfoMappingCollection Mappings => _mappings ??= AddMappings(new());

        public PgTypeInfo GetTypeInfo(Type type, DataTypeName? dataTypeName, PgSerializerOptions options)
            => Mappings.Find(type, dataTypeName, options);

        static TypeInfoMappingCollection AddMappings(TypeInfoMappingCollection mappings)
        {
            mappings.AddStructType<PgSqlPoint>(DataTypeNames.Point,
                static (options, mapping, _) => mapping.CreateInfo(options, new PointConverter()), isDefault: true);
            mappings.AddStructType<PgSqlBox>(DataTypeNames.Box,
                static (options, mapping, _) => mapping.CreateInfo(options, new BoxConverter()), isDefault: true);
            mappings.AddStructType<PgSqlPolygon>(DataTypeNames.Polygon,
                static (options, mapping, _) => mapping.CreateInfo(options, new PolygonConverter()), isDefault: true);
            mappings.AddStructType<PgSqlLine>(DataTypeNames.Line,
                static (options, mapping, _) => mapping.CreateInfo(options, new LineConverter()), isDefault: true);
            mappings.AddStructType<PgSqlLSeg>(DataTypeNames.LSeg,
                static (options, mapping, _) => mapping.CreateInfo(options, new LineSegmentConverter()), isDefault: true);
            mappings.AddStructType<PgSqlPath>(DataTypeNames.Path,
                static (options, mapping, _) => mapping.CreateInfo(options, new PathConverter()), isDefault: true);
            mappings.AddStructType<PgSqlCircle>(DataTypeNames.Circle,
                static (options, mapping, _) => mapping.CreateInfo(options, new CircleConverter()), isDefault: true);

            return mappings;
        }
    }

    sealed class ArrayResolver : Resolver, IPgTypeInfoResolver
    {
        TypeInfoMappingCollection _mappings;
        new TypeInfoMappingCollection Mappings => _mappings ??= AddMappings(new(base.Mappings));

        public new PgTypeInfo GetTypeInfo(Type type, DataTypeName? dataTypeName, PgSerializerOptions options)
            => Mappings.Find(type, dataTypeName, options);

        static TypeInfoMappingCollection AddMappings(TypeInfoMappingCollection mappings)
        {
            mappings.AddStructArrayType<PgSqlPoint>(DataTypeNames.Point);
            mappings.AddStructArrayType<PgSqlBox>(DataTypeNames.Box);
            mappings.AddStructArrayType<PgSqlPolygon>(DataTypeNames.Polygon);
            mappings.AddStructArrayType<PgSqlLine>(DataTypeNames.Line);
            mappings.AddStructArrayType<PgSqlLSeg>(DataTypeNames.LSeg);
            mappings.AddStructArrayType<PgSqlPath>(DataTypeNames.Path);
            mappings.AddStructArrayType<PgSqlCircle>(DataTypeNames.Circle);

            return mappings;
        }
    }
}
