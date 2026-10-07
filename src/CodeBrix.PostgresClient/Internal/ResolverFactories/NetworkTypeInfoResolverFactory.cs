using System;
using System.Net;
using System.Net.NetworkInformation;
using CodeBrix.PostgresClient.Internal.Converters;
using CodeBrix.PostgresClient.Internal.Postgres;
using CodeBrix.PostgresClient.PgSqlTypes;

namespace CodeBrix.PostgresClient.Internal.ResolverFactories; //was previously: Npgsql.Internal.ResolverFactories;

sealed class NetworkTypeInfoResolverFactory : PgTypeInfoResolverFactory
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
            // macaddr
            mappings.AddType<PhysicalAddress>(DataTypeNames.MacAddr,
                static (options, mapping, _) => mapping.CreateInfo(options, new MacaddrConverter(macaddr8: false)), isDefault: true);
            mappings.AddType<PhysicalAddress>(DataTypeNames.MacAddr8,
                static (options, mapping, _) => mapping.CreateInfo(options, new MacaddrConverter(macaddr8: true)),
                mapping => mapping with { MatchRequirement = MatchRequirement.DataTypeName });

            // inet
            // There are certain IPAddress values like Loopback or Any that return a *private* derived type (see https://github.com/dotnet/runtime/issues/27870).
            mappings.AddType<IPAddress>(DataTypeNames.Inet,
                static (options, mapping, _) => new PgTypeInfo(options, new IPAddressConverter(), new DataTypeName(mapping.DataTypeName),
                    unboxedType: mapping.Type != typeof(IPAddress) ? mapping.Type : null),
                mapping => mapping with
                {
                    MatchRequirement = MatchRequirement.Single,
                    TypeMatchPredicate = type => type is null || typeof(IPAddress).IsAssignableFrom(type)
                });
            mappings.AddStructType<PgSqlInet>(DataTypeNames.Inet,
                static (options, mapping, _) => mapping.CreateInfo(options, new PgSqlInetConverter()));

            // cidr
            mappings.AddStructType<IPNetwork>(DataTypeNames.Cidr,
                static (options, mapping, _) => mapping.CreateInfo(options, new IPNetworkConverter()), isDefault: true);

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
            // macaddr
            mappings.AddArrayType<PhysicalAddress>(DataTypeNames.MacAddr);
            mappings.AddArrayType<PhysicalAddress>(DataTypeNames.MacAddr8);

            // inet
            mappings.AddArrayType<IPAddress>(DataTypeNames.Inet);
            mappings.AddStructArrayType<PgSqlInet>(DataTypeNames.Inet);

            // cidr
            mappings.AddStructArrayType<IPNetwork>(DataTypeNames.Cidr);

            return mappings;
        }
    }
}
