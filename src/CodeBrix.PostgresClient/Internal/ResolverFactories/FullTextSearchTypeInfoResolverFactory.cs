using System;
using CodeBrix.PostgresClient.Internal.Converters;
using CodeBrix.PostgresClient.Internal.Postgres;
using CodeBrix.PostgresClient.PgSqlTypes;
using CodeBrix.PostgresClient.Properties;

namespace CodeBrix.PostgresClient.Internal.ResolverFactories; //was previously: Npgsql.Internal.ResolverFactories;

sealed class FullTextSearchTypeInfoResolverFactory : PgTypeInfoResolverFactory
{
    public override IPgTypeInfoResolver CreateResolver() => new Resolver();
    public override IPgTypeInfoResolver CreateArrayResolver() => new ArrayResolver();

    public static void ThrowIfUnsupported<TBuilder>(Type type, DataTypeName? dataTypeName, PgSerializerOptions options)
    {
        if (dataTypeName is { SchemaSpan: "pg_catalog", UnqualifiedNameSpan: "tsquery" or "_tsquery" or "tsvector" or "_tsvector" })
            throw new NotSupportedException(
                string.Format(PgSqlStrings.FullTextSearchNotEnabled, nameof(PgSqlSlimDataSourceBuilder.EnableFullTextSearch), typeof(TBuilder).Name));

        if (type is null)
            return;

        if (TypeInfoMappingCollection.IsArrayLikeType(type, out var elementType))
            type = elementType;

        if (Nullable.GetUnderlyingType(type) is { } underlyingType)
            type = underlyingType;

        if (type == typeof(PgSqlTsVector) || typeof(PgSqlTsQuery).IsAssignableFrom(type))
            throw new NotSupportedException(
                string.Format(PgSqlStrings.FullTextSearchNotEnabled, nameof(PgSqlSlimDataSourceBuilder.EnableFullTextSearch), typeof(TBuilder).Name));
    }

    class Resolver : IPgTypeInfoResolver
    {
        TypeInfoMappingCollection _mappings;
        protected TypeInfoMappingCollection Mappings => _mappings ??= AddMappings(new());

        public PgTypeInfo GetTypeInfo(Type type, DataTypeName? dataTypeName, PgSerializerOptions options)
            => Mappings.Find(type, dataTypeName, options);

        static TypeInfoMappingCollection AddMappings(TypeInfoMappingCollection mappings)
        {
            // tsvector
            mappings.AddType<PgSqlTsVector>(DataTypeNames.TsVector,
                static (options, mapping, _) => mapping.CreateInfo(options, new TsVectorConverter(options.TextEncoding)), isDefault: true);

            // tsquery
            mappings.AddType<PgSqlTsQuery>(DataTypeNames.TsQuery,
                static (options, mapping, _) => mapping.CreateInfo(options, new TsQueryConverter<PgSqlTsQuery>(options.TextEncoding)), isDefault: true);
            mappings.AddType<PgSqlTsQueryEmpty>(DataTypeNames.TsQuery,
                static (options, mapping, _) => mapping.CreateInfo(options, new TsQueryConverter<PgSqlTsQueryEmpty>(options.TextEncoding)));
            mappings.AddType<PgSqlTsQueryLexeme>(DataTypeNames.TsQuery,
                static (options, mapping, _) => mapping.CreateInfo(options, new TsQueryConverter<PgSqlTsQueryLexeme>(options.TextEncoding)));
            mappings.AddType<PgSqlTsQueryNot>(DataTypeNames.TsQuery,
                static (options, mapping, _) => mapping.CreateInfo(options, new TsQueryConverter<PgSqlTsQueryNot>(options.TextEncoding)));
            mappings.AddType<PgSqlTsQueryAnd>(DataTypeNames.TsQuery,
                static (options, mapping, _) => mapping.CreateInfo(options, new TsQueryConverter<PgSqlTsQueryAnd>(options.TextEncoding)));
            mappings.AddType<PgSqlTsQueryOr>(DataTypeNames.TsQuery,
                static (options, mapping, _) => mapping.CreateInfo(options, new TsQueryConverter<PgSqlTsQueryOr>(options.TextEncoding)));
            mappings.AddType<PgSqlTsQueryFollowedBy>(DataTypeNames.TsQuery,
                static (options, mapping, _) => mapping.CreateInfo(options, new TsQueryConverter<PgSqlTsQueryFollowedBy>(options.TextEncoding)));

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
            // tsvector
            mappings.AddArrayType<PgSqlTsVector>(DataTypeNames.TsVector);

            // tsquery
            mappings.AddArrayType<PgSqlTsQuery>(DataTypeNames.TsQuery);
            mappings.AddArrayType<PgSqlTsQueryEmpty>(DataTypeNames.TsQuery);
            mappings.AddArrayType<PgSqlTsQueryLexeme>(DataTypeNames.TsQuery);
            mappings.AddArrayType<PgSqlTsQueryNot>(DataTypeNames.TsQuery);
            mappings.AddArrayType<PgSqlTsQueryAnd>(DataTypeNames.TsQuery);
            mappings.AddArrayType<PgSqlTsQueryOr>(DataTypeNames.TsQuery);
            mappings.AddArrayType<PgSqlTsQueryFollowedBy>(DataTypeNames.TsQuery);

            return mappings;
        }
    }
}
