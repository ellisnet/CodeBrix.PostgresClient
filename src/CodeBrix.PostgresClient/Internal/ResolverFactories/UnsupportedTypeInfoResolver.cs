using System;
using System.Collections;
using CodeBrix.PostgresClient.Internal.Postgres;
using CodeBrix.PostgresClient.PostgresTypes;
using CodeBrix.PostgresClient.Properties;

namespace CodeBrix.PostgresClient.Internal.ResolverFactories; //was previously: Npgsql.Internal.ResolverFactories;

sealed class UnsupportedTypeInfoResolver<TBuilder> : IPgTypeInfoResolver
{
    public PgTypeInfo GetTypeInfo(Type type, DataTypeName? dataTypeName, PgSerializerOptions options)
    {
        if (options.IntrospectionMode)
            return null;

        RecordTypeInfoResolverFactory.ThrowIfUnsupported<TBuilder>(type, dataTypeName, options);
        FullTextSearchTypeInfoResolverFactory.ThrowIfUnsupported<TBuilder>(type, dataTypeName, options);
        LTreeTypeInfoResolverFactory.ThrowIfUnsupported<TBuilder>(type, dataTypeName, options);
        CubeTypeInfoResolverFactory.ThrowIfUnsupported<TBuilder>(type, dataTypeName, options);

        JsonDynamicTypeInfoResolverFactory.Support.ThrowIfUnsupported<TBuilder>(type, dataTypeName);

        switch (dataTypeName is null ? null : options.DatabaseInfo.GetPostgresType(dataTypeName.GetValueOrDefault()))
        {
        case PostgresEnumType:
            // Unmapped enum types never work on object or default.
            if (type is not null && type != typeof(object))
                throw new NotSupportedException(
                    string.Format(
                        PgSqlStrings.UnmappedEnumsNotEnabled,
                        nameof(PgSqlSlimDataSourceBuilder.EnableUnmappedTypes),
                        typeof(TBuilder).Name));
            break;

        case PostgresRangeType when !options.RangesEnabled:
            throw new NotSupportedException(
                string.Format(PgSqlStrings.RangesNotEnabled, nameof(PgSqlSlimDataSourceBuilder.EnableRanges), typeof(TBuilder).Name));
        case PostgresRangeType:
            throw new NotSupportedException(
                string.Format(
                    PgSqlStrings.UnmappedRangesNotEnabled,
                    nameof(PgSqlSlimDataSourceBuilder.EnableUnmappedTypes),
                    typeof(TBuilder).Name));

        case PostgresMultirangeType when !options.MultirangesEnabled:
            throw new NotSupportedException(
                string.Format(PgSqlStrings.MultirangesNotEnabled, nameof(PgSqlSlimDataSourceBuilder.EnableMultiranges), typeof(TBuilder).Name));
        case PostgresMultirangeType:
            throw new NotSupportedException(
                string.Format(
                    PgSqlStrings.UnmappedRangesNotEnabled,
                    nameof(PgSqlSlimDataSourceBuilder.EnableUnmappedTypes),
                    typeof(TBuilder).Name));

        case PostgresArrayType when !options.ArraysEnabled:
            throw new NotSupportedException(
                string.Format(PgSqlStrings.ArraysNotEnabled, nameof(PgSqlSlimDataSourceBuilder.EnableArrays), typeof(TBuilder).Name));
        }

        if (type is not null)
        {
            if (TypeInfoMappingCollection.IsArrayLikeType(type, out var elementType) && TypeInfoMappingCollection.IsArrayLikeType(elementType, out _))
                throw new NotSupportedException("Writing is not supported for jagged collections, use a multidimensional array instead.");

            if (typeof(IEnumerable).IsAssignableFrom(type) && !typeof(IList).IsAssignableFrom(type) && type != typeof(string) && (dataTypeName is null || dataTypeName.Value.IsArray))
                throw new NotSupportedException("Writing is not supported for IEnumerable parameters, use an array or some implementation of IList<T> instead.");
        }

        return null;
    }
}
