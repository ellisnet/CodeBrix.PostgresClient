using System;
using System.Diagnostics.CodeAnalysis;
using CodeBrix.PostgresClient.Internal.Postgres;
using CodeBrix.PostgresClient.PostgresTypes;

namespace CodeBrix.PostgresClient.Internal; //was previously: Npgsql.Internal;

/// <summary>Base class for type info resolvers that create mappings on demand for CLR types only known at run time (e.g. user enums or composites), using reflection to call the generic <see cref="TypeInfoMappingCollection"/> methods.</summary>
[Experimental(PgSqlDiagnostics.ConvertersExperimental)]
[RequiresDynamicCode("A dynamic type info resolver may need to construct a generic converter for a statically unknown type.")]
public abstract class DynamicTypeInfoResolver : IPgTypeInfoResolver
{
    /// <inheritdoc />
    public PgTypeInfo GetTypeInfo(Type type, DataTypeName? dataTypeName, PgSerializerOptions options)
    {
        if (dataTypeName is null)
            return null;

        var context = GetMappings(type, dataTypeName.GetValueOrDefault(), options);
        return context?.Find(type, dataTypeName.GetValueOrDefault(), options);
    }

    /// <summary>Creates an empty dynamic mapping collection.</summary>
    /// <param name="baseCollection">An optional collection searched for existing element mappings when array mappings are added.</param>
    /// <returns>The new collection.</returns>
    protected static DynamicMappingCollection CreateCollection(TypeInfoMappingCollection baseCollection = null) => new(baseCollection);

    /// <summary>Determines whether a type, or the underlying type of a nullable value type, satisfies a predicate.</summary>
    /// <param name="type">The type to test.</param>
    /// <param name="predicate">The test applied to the (unwrapped) type.</param>
    /// <param name="matchedType">Receives the unwrapped type.</param>
    /// <returns>The result of <paramref name="predicate"/>.</returns>
    protected static bool IsTypeOrNullableOfType(Type type, Func<Type, bool> predicate, out Type matchedType)
    {
        matchedType = Nullable.GetUnderlyingType(type) ?? type;
        return predicate(matchedType);
    }

    /// <summary>Determines whether a type is an array or an <see cref="System.Collections.Generic.IList{T}"/> implementation, and returns its element type.</summary>
    /// <param name="type">The type to test.</param>
    /// <param name="elementType">Receives the element type when the result is <see langword="true"/>.</param>
    /// <returns><see langword="true"/> for an array-like type.</returns>
    protected static bool IsArrayLikeType(Type type, [NotNullWhen(true)]out Type elementType) => TypeInfoMappingCollection.IsArrayLikeType(type, out elementType);

    /// <summary>Determines whether a data type name refers to an array type in the database's type catalog, and returns its element type name.</summary>
    /// <param name="dataTypeName">The data type name to test.</param>
    /// <param name="options">The options holding the type catalog.</param>
    /// <param name="elementDataTypeName">Receives the element type name when the result is <see langword="true"/>.</param>
    /// <returns><see langword="true"/> if the name refers to an array type.</returns>
    protected static bool IsArrayDataTypeName(DataTypeName dataTypeName, PgSerializerOptions options, out DataTypeName elementDataTypeName)
    {
        if (options.DatabaseInfo.GetPostgresType(dataTypeName) is PostgresArrayType arrayType)
        {
            elementDataTypeName = arrayType.Element.DataTypeName;
            return true;
        }

        elementDataTypeName = default;
        return false;
    }

    /// <summary>Creates the mappings that can handle the requested CLR type and data type name, if this resolver supports them.</summary>
    /// <param name="type">The requested CLR type, or <see langword="null"/>.</param>
    /// <param name="dataTypeName">The requested data type name.</param>
    /// <param name="options">The serializer options in effect.</param>
    /// <returns>The mappings, or <see langword="null"/> if the request is not handled.</returns>
    protected abstract DynamicMappingCollection GetMappings(Type type, DataTypeName dataTypeName, PgSerializerOptions options);

    /// <summary>A builder of mappings for types that are only known at run time; each add method dispatches to the matching generic <see cref="TypeInfoMappingCollection"/> method via reflection.</summary>
    [RequiresDynamicCode("A dynamic type info resolver may need to construct a generic converter for a statically unknown type.")]
    protected class DynamicMappingCollection
    {
        TypeInfoMappingCollection _mappings;

        internal DynamicMappingCollection(TypeInfoMappingCollection baseCollection = null)
        {
            if (baseCollection is not null)
                _mappings = new(baseCollection);
        }

        /// <summary>Adds a mapping for a type (and, for value types, its nullable form).</summary>
        /// <param name="type">The CLR type; nullable value types are not accepted, map the underlying type instead.</param>
        /// <param name="dataTypeName">The PostgreSQL data type name.</param>
        /// <param name="factory">The factory creating the type info.</param>
        /// <param name="configureMapping">An optional callback adjusting the mapping before it is added.</param>
        /// <returns>This collection, for chaining.</returns>
        public DynamicMappingCollection AddMapping([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]Type type, string dataTypeName, TypeInfoFactory factory, Func<TypeInfoMapping, TypeInfoMapping> configureMapping = null)
        {
            if (type.IsValueType)
            {
                if (Nullable.GetUnderlyingType(type) is not null)
                    throw new NotSupportedException("Mapping nullable types is not supported, map its underlying type instead to get both.");

                typeof(TypeInfoMappingCollection)
                    .GetMethod(nameof(TypeInfoMappingCollection.AddStructType), [typeof(string), typeof(TypeInfoFactory), typeof(Func<TypeInfoMapping, TypeInfoMapping>)])
                    .MakeGenericMethod(type).Invoke(_mappings ??= new(),
                    [
                        dataTypeName,
                        factory,
                        configureMapping
                    ]);
            }
            else
            {
                typeof(TypeInfoMappingCollection)
                    .GetMethod(nameof(TypeInfoMappingCollection.AddType), [typeof(string), typeof(TypeInfoFactory), typeof(Func<TypeInfoMapping, TypeInfoMapping>)])
                    .MakeGenericMethod(type).Invoke(_mappings ??= new(),
                    [
                        dataTypeName,
                        factory,
                        configureMapping
                    ]);
            }
            return this;
        }

        /// <summary>Adds array and list mappings for an element type that was already mapped.</summary>
        /// <param name="elementType">The element CLR type; nullable value types are not accepted.</param>
        /// <param name="dataTypeName">The data type name of the element mapping.</param>
        /// <returns>This collection, for chaining.</returns>
        public DynamicMappingCollection AddArrayMapping([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]Type elementType, string dataTypeName)
        {
            if (elementType.IsValueType)
            {
                if (Nullable.GetUnderlyingType(elementType) is not null)
                    throw new NotSupportedException("Mapping nullable types is not supported, map its underlying type instead to get both.");

                typeof(TypeInfoMappingCollection)
                    .GetMethod(nameof(TypeInfoMappingCollection.AddStructArrayType), [typeof(string)])
                    .MakeGenericMethod(elementType).Invoke(_mappings ??= new(), [dataTypeName]);
            }
            else
            {
                typeof(TypeInfoMappingCollection)
                    .GetMethod(nameof(TypeInfoMappingCollection.AddArrayType), [typeof(string)])
                    .MakeGenericMethod(elementType).Invoke(_mappings ??= new(), [dataTypeName]);
            }
            return this;
        }

        /// <summary>Adds a resolver-based mapping for a type (and, for value types, its nullable form).</summary>
        /// <param name="type">The CLR type; nullable value types are not accepted, map the underlying type instead.</param>
        /// <param name="dataTypeName">The PostgreSQL data type name.</param>
        /// <param name="factory">The factory creating the resolver type info.</param>
        /// <param name="configureMapping">An optional callback adjusting the mapping before it is added.</param>
        /// <returns>This collection, for chaining.</returns>
        public DynamicMappingCollection AddResolverMapping([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]Type type, string dataTypeName, TypeInfoFactory factory, Func<TypeInfoMapping, TypeInfoMapping> configureMapping = null)
        {
            if (type.IsValueType)
            {
                if (Nullable.GetUnderlyingType(type) is not null)
                    throw new NotSupportedException("Mapping nullable types is not supported, map its underlying type instead to get both.");

                typeof(TypeInfoMappingCollection)
                    .GetMethod(nameof(TypeInfoMappingCollection.AddResolverStructType), [typeof(string), typeof(TypeInfoFactory), typeof(Func<TypeInfoMapping, TypeInfoMapping>)])
                    .MakeGenericMethod(type).Invoke(_mappings ??= new(),
                    [
                        dataTypeName,
                        factory,
                        configureMapping
                    ]);
            }
            else
            {
                typeof(TypeInfoMappingCollection)
                    .GetMethod(nameof(TypeInfoMappingCollection.AddResolverType), [typeof(string), typeof(TypeInfoFactory), typeof(Func<TypeInfoMapping, TypeInfoMapping>)])
                    .MakeGenericMethod(type).Invoke(_mappings ??= new(),
                    [
                        dataTypeName,
                        factory,
                        configureMapping
                    ]);
            }
            return this;
        }

        /// <summary>Adds resolver-based array and list mappings for an element type that was already mapped.</summary>
        /// <param name="elementType">The element CLR type; nullable value types are not accepted.</param>
        /// <param name="dataTypeName">The data type name of the element mapping.</param>
        /// <returns>This collection, for chaining.</returns>
        public DynamicMappingCollection AddResolverArrayMapping([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]Type elementType, string dataTypeName)
        {
            if (elementType.IsValueType)
            {
                if (Nullable.GetUnderlyingType(elementType) is not null)
                    throw new NotSupportedException("Mapping nullable types is not supported, map its underlying type instead to get both.");

                typeof(TypeInfoMappingCollection)
                    .GetMethod(nameof(TypeInfoMappingCollection.AddResolverStructArrayType), [typeof(string)])
                    .MakeGenericMethod(elementType).Invoke(_mappings ??= new(), [dataTypeName]);
            }
            else
            {
                typeof(TypeInfoMappingCollection)
                    .GetMethod(nameof(TypeInfoMappingCollection.AddResolverArrayType), [typeof(string)])
                    .MakeGenericMethod(elementType).Invoke(_mappings ??= new(), [dataTypeName]);
            }
            return this;
        }

        internal PgTypeInfo Find(Type type, DataTypeName dataTypeName, PgSerializerOptions options)
            => _mappings?.Find(type, dataTypeName, options);

        /// <summary>Copies the mappings added so far into a new <see cref="TypeInfoMappingCollection"/>.</summary>
        /// <returns>The new collection.</returns>
        public TypeInfoMappingCollection ToTypeInfoMappingCollection()
            => new(_mappings?.Items ?? Array.Empty<TypeInfoMapping>());
    }
}
