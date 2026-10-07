using System;
using CodeBrix.PostgresClient.Internal.Converters;
using CodeBrix.PostgresClient.Internal.Postgres;
using CodeBrix.PostgresClient.PgSqlTypes;
using CodeBrix.PostgresClient.Properties;

namespace CodeBrix.PostgresClient.Internal.ResolverFactories; //was previously: Npgsql.Internal.ResolverFactories;

sealed class CubeTypeInfoResolverFactory : PgTypeInfoResolverFactory
{
    const string CubeTypeName = "cube";

    public override IPgTypeInfoResolver CreateResolver() => new Resolver();
    public override IPgTypeInfoResolver CreateArrayResolver() => new ArrayResolver();

    public static void ThrowIfUnsupported<TBuilder>(Type type, DataTypeName? dataTypeName, PgSerializerOptions options)
    {
        if (dataTypeName is { UnqualifiedNameSpan: "cube" or "_cube" } || type == typeof(PgSqlCube))
            throw new NotSupportedException(
                string.Format(PgSqlStrings.CubeNotEnabled, nameof(PgSqlSlimDataSourceBuilder.EnableCube),
                    typeof(TBuilder).Name));
    }

    class Resolver : IPgTypeInfoResolver
    {
        TypeInfoMappingCollection _mappings;
        protected TypeInfoMappingCollection Mappings => _mappings ??= AddMappings(new());

        public PgTypeInfo GetTypeInfo(Type type, DataTypeName? dataTypeName, PgSerializerOptions options)
            => Mappings.Find(type, dataTypeName, options);

        static TypeInfoMappingCollection AddMappings(TypeInfoMappingCollection mappings)
        {
            mappings.AddStructType<PgSqlCube>(CubeTypeName,
                static (options, mapping, _) => mapping.CreateInfo(options, new CubeConverter()), isDefault: true);

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
            mappings.AddStructArrayType<PgSqlCube>(CubeTypeName);

            return mappings;
        }
    }
}
