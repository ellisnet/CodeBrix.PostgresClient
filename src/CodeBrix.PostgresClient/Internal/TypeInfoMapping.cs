using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;
using CodeBrix.PostgresClient.Internal.Converters;
using CodeBrix.PostgresClient.Internal.Postgres;
using CodeBrix.PostgresClient.PgSqlTypes;
using CodeBrix.PostgresClient.PostgresTypes;

namespace CodeBrix.PostgresClient.Internal; //was previously: Npgsql.Internal;

/// <summary>
///
/// </summary>
/// <param name="options"></param>
/// <param name="mapping"></param>
/// <param name="requiresDataTypeName">
/// Relevant for `PgResolverTypeInfo` only: whether the instance can be constructed without passing mapping.DataTypeName, an exception occurs otherwise.
/// </param>
[Experimental(PgSqlDiagnostics.ConvertersExperimental)]
public delegate PgTypeInfo TypeInfoFactory(PgSerializerOptions options, TypeInfoMapping mapping, bool requiresDataTypeName);

/// <summary>Controls which parts of a lookup (CLR type and/or data type name) must match for a <see cref="TypeInfoMapping"/> to be selected.</summary>
[Experimental(PgSqlDiagnostics.ConvertersExperimental)]
public enum MatchRequirement
{
    /// Match when the clr type and datatype name both match.
    /// It's also the only requirement that participates in clr type fallback matching.
    All,
    /// Match when the datatype name or CLR type matches while the other also matches or is absent.
    Single,
    /// Match when the datatype name matches and the clr type also matches or is absent.
    DataTypeName
}

/// A factory for well-known PgConverters.
[Experimental(PgSqlDiagnostics.ConvertersExperimental)]
public static class PgConverterFactory
{
    /// <summary>Creates a converter for a multirange that is represented as an array of ranges.</summary>
    /// <typeparam name="T">The CLR type of a single range.</typeparam>
    /// <param name="rangeConverter">The converter for the individual ranges.</param>
    /// <param name="options">The serializer options in effect.</param>
    /// <returns>A converter for arrays of ranges.</returns>
    public static PgConverter<T[]> CreateArrayMultirangeConverter<T>(PgConverter<T> rangeConverter, PgSerializerOptions options) where T : notnull
        => new MultirangeConverter<T[], T>(rangeConverter);

    /// <summary>Creates a converter for a multirange that is represented as a <see cref="List{T}"/> of ranges.</summary>
    /// <typeparam name="T">The CLR type of a single range.</typeparam>
    /// <param name="rangeConverter">The converter for the individual ranges.</param>
    /// <param name="options">The serializer options in effect.</param>
    /// <returns>A converter for lists of ranges.</returns>
    public static PgConverter<List<T>> CreateListMultirangeConverter<T>(PgConverter<T> rangeConverter, PgSerializerOptions options) where T : notnull
        => new MultirangeConverter<List<T>, T>(rangeConverter);

    /// <summary>Creates a converter for a PostgreSQL range over the given subtype.</summary>
    /// <typeparam name="T">The CLR type of the range's bounds.</typeparam>
    /// <param name="subTypeConverter">The converter for the range's subtype (bound values).</param>
    /// <param name="options">The serializer options in effect.</param>
    /// <returns>A converter for <see cref="PgSqlRange{T}"/>.</returns>
    public static PgConverter<PgSqlRange<T>> CreateRangeConverter<T>(PgConverter<T> subTypeConverter, PgSerializerOptions options)
        => new RangeConverter<T>(subTypeConverter);

    /// <summary>Creates an array converter whose element nullability handling follows <see cref="PgSerializerOptions.ArrayNullabilityMode"/>.</summary>
    /// <typeparam name="TBase">The common base type returned by both array converters.</typeparam>
    /// <param name="arrayConverterFactory">Creates the converter for arrays with non-nullable elements.</param>
    /// <param name="nullableArrayConverterFactory">Creates the converter for arrays with nullable elements.</param>
    /// <param name="options">The serializer options whose array nullability mode selects the converter.</param>
    /// <returns>The non-nullable converter, the nullable converter, or a converter choosing between them per instance.</returns>
    public static PgConverter<TBase> CreatePolymorphicArrayConverter<TBase>(Func<PgConverter<TBase>> arrayConverterFactory, Func<PgConverter<TBase>> nullableArrayConverterFactory, PgSerializerOptions options)
        => options.ArrayNullabilityMode switch
        {
            ArrayNullabilityMode.Never => arrayConverterFactory(),
            ArrayNullabilityMode.Always => nullableArrayConverterFactory(),
            ArrayNullabilityMode.PerInstance => new PolymorphicArrayConverter<TBase>(arrayConverterFactory(), nullableArrayConverterFactory()),
            _ => throw new ArgumentOutOfRangeException()
        };
}

/// <summary>Associates a CLR type with a PostgreSQL data type name and the factory that builds the <see cref="PgTypeInfo"/> for that pairing.</summary>
/// <param name="type">The CLR type handled by the mapping.</param>
/// <param name="dataTypeName">The PostgreSQL data type name, which is normalized on construction.</param>
/// <param name="factory">The factory that creates the type info for this mapping.</param>
[DebuggerDisplay("{DebuggerDisplay,nq}")]
[Experimental(PgSqlDiagnostics.ConvertersExperimental)]
public readonly struct TypeInfoMapping(Type type, string dataTypeName, TypeInfoFactory factory)
{
    // For objects it makes no sense to have clr type only matches by default, there are too many implementations.

    /// <summary>The factory invoked to create the <see cref="PgTypeInfo"/> when this mapping is selected.</summary>
    public TypeInfoFactory Factory { get; init; } = factory;
    /// <summary>The CLR type handled by this mapping.</summary>
    public Type Type { get; init; } = type;
    /// <summary>The normalized PostgreSQL data type name handled by this mapping; it may be fully qualified or unqualified.</summary>
    public string DataTypeName { get; init; } = Postgres.DataTypeName.NormalizeName(dataTypeName);

    /// <summary>Which parts of a lookup must match for this mapping to be selected; defaults to <see cref="MatchRequirement.DataTypeName"/> for <see cref="object"/> and <see cref="MatchRequirement.All"/> otherwise.</summary>
    public MatchRequirement MatchRequirement { get; init; } = type == typeof(object) ? MatchRequirement.DataTypeName : MatchRequirement.All;
    /// <summary>An optional predicate that replaces exact equality with <see cref="Type"/> when matching CLR types (used e.g. to match arrays of any rank).</summary>
    public Func<Type, bool> TypeMatchPredicate { get; init; }

    /// <summary>Determines whether a CLR type matches this mapping, using <see cref="TypeMatchPredicate"/> when set and exact equality with <see cref="Type"/> otherwise.</summary>
    /// <param name="type">The CLR type to test.</param>
    /// <returns><see langword="true"/> if the type matches.</returns>
    public bool TypeEquals(Type type) => TypeMatchPredicate?.Invoke(type) ?? Type == type;

    bool DataTypeNameEqualsCore(string dataTypeName)
    {
        var span = DataTypeName.AsSpan();
        return Postgres.DataTypeName.IsFullyQualified(span)
            ? span.Equals(dataTypeName.AsSpan(), StringComparison.Ordinal)
            : span.Equals(Postgres.DataTypeName.ValidatedName(dataTypeName).UnqualifiedNameSpan, StringComparison.Ordinal);
    }

    internal bool DataTypeNameEquals(DataTypeName dataTypeName)
    {
        var value = dataTypeName.Value;
        return DataTypeNameEqualsCore(value);
    }

    /// <summary>Determines whether a data type name matches this mapping's <see cref="DataTypeName"/>, comparing only the unqualified part when the mapping's name is unqualified.</summary>
    /// <param name="dataTypeName">The data type name to test; it is normalized before comparison.</param>
    /// <returns><see langword="true"/> if the name matches.</returns>
    public bool DataTypeNameEquals(string dataTypeName)
    {
        var normalized = Postgres.DataTypeName.NormalizeName(dataTypeName);
        return DataTypeNameEqualsCore(normalized);
    }

    string DebuggerDisplay
    {
        get
        {
            var builder = new StringBuilder()
                .Append(Type.Name)
                .Append(" <-> ")
                .Append(Postgres.DataTypeName.FromDisplayName(DataTypeName).DisplayName);

            if (MatchRequirement is not MatchRequirement.All)
                builder.Append($" ({MatchRequirement.ToString().ToLowerInvariant()})");

            return builder.ToString();
        }
    }
}

/// <summary>An ordered collection of <see cref="TypeInfoMapping"/> entries used by type info resolvers, with helpers that register a type together with its nullable, array and list variants.</summary>
[Experimental(PgSqlDiagnostics.ConvertersExperimental)]
public sealed class TypeInfoMappingCollection
{
    readonly TypeInfoMappingCollection _baseCollection;
    readonly List<TypeInfoMapping> _items;

    /// <summary>Creates an empty collection with the given initial capacity.</summary>
    /// <param name="capacity">The initial capacity of the underlying list.</param>
    public TypeInfoMappingCollection(int capacity = 0)
        => _items = new(capacity);

    /// <summary>Creates an empty collection.</summary>
    public TypeInfoMappingCollection() : this(0) { }

    // Not used for resolving, only for composing (arrays that need to find the element mapping etc).
    /// <summary>Creates an empty collection that looks up element mappings in another collection when composing array mappings.</summary>
    /// <param name="baseCollection">The collection searched for existing (element) mappings.</param>
    public TypeInfoMappingCollection(TypeInfoMappingCollection baseCollection) : this(0)
        => _baseCollection = baseCollection;

    /// <summary>Creates a collection containing a copy of the given mappings.</summary>
    /// <param name="items">The mappings to copy.</param>
    public TypeInfoMappingCollection(IEnumerable<TypeInfoMapping> items)
        => _items = [..items];

    /// <summary>The mappings in this collection, in registration (and therefore matching) order.</summary>
    public IReadOnlyList<TypeInfoMapping> Items => _items;

    /// Returns the first default converter or the first converter that matches both type and dataTypeName.
    /// If just a type was passed and no default was found we return the first converter with a type match.
    public PgTypeInfo Find(Type type, DataTypeName? dataTypeName, PgSerializerOptions options)
    {
        TypeInfoMapping? fallback = null;
        foreach (var mapping in _items)
        {
            var looseTypeMatch = mapping.TypeMatchPredicate is { } pred ? pred(type) : type is null || mapping.Type == type;
            var typeMatch = type is not null && looseTypeMatch;
            var dataTypeMatch = dataTypeName is not null && mapping.DataTypeNameEquals(dataTypeName.Value);

            var matchRequirement = mapping.MatchRequirement;
            if (dataTypeMatch && typeMatch
                || matchRequirement is not MatchRequirement.All && dataTypeMatch && looseTypeMatch
                || matchRequirement is MatchRequirement.Single && dataTypeName is null && typeMatch)
            {
                var resolvedDataTypeName = ResolveFullyQualifiedDataTypeName(dataTypeName, mapping.DataTypeName, options);
                return mapping.Factory(options, mapping with { Type = type ?? mapping.Type, DataTypeName = resolvedDataTypeName }, dataTypeName is not null);
            }

            // DataTypeName is explicitly requiring dataTypeName so it won't be used for a fallback, Single would have matched above already.
            if (matchRequirement is MatchRequirement.All && fallback is null && dataTypeName is null && typeMatch)
                fallback = mapping;
        }

        if (fallback is { } fbMapping)
        {
            var resolvedDataTypeName = ResolveFullyQualifiedDataTypeName(dataTypeName, fbMapping.DataTypeName, options);
            return fbMapping.Factory(options, fbMapping with { Type = type, DataTypeName = resolvedDataTypeName }, dataTypeName is not null);
        }

        return null;

        static string ResolveFullyQualifiedDataTypeName(DataTypeName? dataTypeName, string mappingDataTypeName, PgSerializerOptions options)
        {
            // Make sure plugins (which match on unqualified names) and converter resolvers get the fully qualified name to canonicalize.
            if (dataTypeName is not null)
                return dataTypeName.GetValueOrDefault().Value;

            if (TypeInfoMappingHelpers.TryResolveFullyQualifiedName(options, mappingDataTypeName, out var fqDataTypeName))
                return fqDataTypeName.Value;

            throw new NotSupportedException($"Cannot resolve '{mappingDataTypeName}' to a fully qualified datatype name. The datatype was not found in the current database info.");
        }
    }

    bool TryGetMapping(Type type, string dataTypeName, out TypeInfoMapping value)
    {
        foreach (var mapping in _baseCollection?._items ?? _items)
        {
            // During mapping we just use look for the declared type, regardless of TypeMatchPredicate.
            if (mapping.Type == type && mapping.DataTypeNameEquals(dataTypeName))
            {
                value = mapping;
                return true;
            }
        }

        value = default;
        return false;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    TypeInfoMapping GetMapping(Type type, string dataTypeName)
        => TryGetMapping(type, dataTypeName, out var info) ? info : throw new InvalidOperationException($"Could not find mapping for {type} <-> {dataTypeName}");

    // Helper to eliminate generic display class duplication.
    static TypeInfoFactory CreateComposedFactory(Type mappingType, TypeInfoMapping innerMapping, Func<TypeInfoMapping, PgTypeInfo, PgConverter> mapper, bool copyPreferredFormat = false, bool? supportsReading = null, bool? supportsWriting = null)
        => (options, mapping, requiresDataTypeName) =>
        {
            var resolvedInnerMapping = innerMapping;
            if (!DataTypeName.IsFullyQualified(innerMapping.DataTypeName.AsSpan()))
                resolvedInnerMapping = innerMapping with { DataTypeName = new DataTypeName(mapping.DataTypeName).Schema + "." + innerMapping.DataTypeName };

            var innerInfo = innerMapping.Factory(options, resolvedInnerMapping, requiresDataTypeName);
            var converter = mapper(mapping, innerInfo);
            var preferredFormat = copyPreferredFormat ? innerInfo.PreferredFormat : null;
            var unboxedType = ComputeUnboxedType(defaultType: mappingType, converter.TypeToConvert, mapping.Type);
            var readingSupported = innerInfo.SupportsReading && (supportsReading ?? PgTypeInfo.GetDefaultSupportsReading(converter.TypeToConvert, unboxedType));
            var writingSupported = innerInfo.SupportsWriting && (supportsWriting ?? true);

            return new PgTypeInfo(options, converter, options.GetCanonicalTypeId(new DataTypeName(mapping.DataTypeName)), unboxedType)
            {
                PreferredFormat = preferredFormat,
                SupportsReading = readingSupported,
                SupportsWriting = writingSupported
            };
        };

    // Helper to eliminate generic display class duplication.
    static TypeInfoFactory CreateComposedFactory(Type mappingType, TypeInfoMapping innerMapping, Func<TypeInfoMapping, PgResolverTypeInfo, PgConverterResolver> mapper, bool copyPreferredFormat = false, bool? supportsReading = null, bool? supportsWriting = null)
        => (options, mapping, requiresDataTypeName) =>
        {
            var resolvedInnerMapping = innerMapping;
            if (!DataTypeName.IsFullyQualified(innerMapping.DataTypeName.AsSpan()))
                resolvedInnerMapping = innerMapping with { DataTypeName = new DataTypeName(mapping.DataTypeName).Schema + "." + innerMapping.DataTypeName };

            var innerInfo = (PgResolverTypeInfo)innerMapping.Factory(options, resolvedInnerMapping, requiresDataTypeName);
            var resolver = mapper(mapping, innerInfo);
            var preferredFormat = copyPreferredFormat ? innerInfo.PreferredFormat : null;
            var unboxedType = ComputeUnboxedType(defaultType: mappingType, resolver.TypeToConvert, mapping.Type);
            var readingSupported = innerInfo.SupportsReading && (supportsReading ?? PgTypeInfo.GetDefaultSupportsReading(resolver.TypeToConvert, unboxedType));
            var writingSupported = innerInfo.SupportsWriting && (supportsWriting ?? true);
            // We include the data type name if the inner info did so as well.
            // This way we can rely on its logic around resolvedDataTypeName, including when it ignores that flag.
            PgTypeId? pgTypeId = innerInfo.PgTypeId is not null
                ? options.GetCanonicalTypeId(new DataTypeName(mapping.DataTypeName))
                : null;
            return new PgResolverTypeInfo(options, resolver, pgTypeId, unboxedType)
            {
                PreferredFormat = preferredFormat,
                SupportsReading = readingSupported,
                SupportsWriting = writingSupported
            };
        };

    static Type ComputeUnboxedType(Type defaultType, Type converterType, Type matchedType)
    {
        // The minimal hierarchy that should hold for things to work is object < converterType < matchedType.
        // Though these types could often be seen in a hierarchy: object < converterType < defaultType < matchedType.
        // Some caveats with the latter being for instance Array being the matchedType while the defaultType is int[].
        Debug.Assert(converterType.IsAssignableFrom(matchedType) || matchedType == typeof(object));
        Debug.Assert(converterType.IsAssignableFrom(defaultType));

        // A special case for object matches, where we return a more specific type than was matched.
        // This is to report e.g. Array converters as Array when their matched type was object.
        if (matchedType == typeof(object))
            return converterType;

        // This is to report e.g. Array converters as int[,,,] when their matched type was such.
        if (matchedType != defaultType)
            return matchedType;

        // If defaultType does not equal converterType we take defaultType as it's more specific.
        // This is to report e.g. Array converters as int[] when their matched type was their default type.
        if (defaultType != converterType)
            return defaultType;

        // Keep the converter type.
        return null;
    }

    /// <summary>Appends a mapping to the collection.</summary>
    /// <param name="mapping">The mapping to add.</param>
    public void Add(TypeInfoMapping mapping) => _items.Add(mapping);

    /// <summary>Appends all mappings of another collection to this one.</summary>
    /// <param name="collection">The collection whose mappings are appended.</param>
    public void AddRange(TypeInfoMappingCollection collection) => _items.AddRange(collection._items);

    Func<TypeInfoMapping, TypeInfoMapping> GetDefaultConfigure(bool isDefault)
        => GetDefaultConfigure(isDefault ? MatchRequirement.Single : MatchRequirement.All);
    Func<TypeInfoMapping, TypeInfoMapping> GetDefaultConfigure(MatchRequirement matchRequirement)
        => matchRequirement switch
        {
            MatchRequirement.All => static mapping => mapping with { MatchRequirement = MatchRequirement.All },
            MatchRequirement.DataTypeName => static mapping => mapping with { MatchRequirement = MatchRequirement.DataTypeName },
            MatchRequirement.Single => static mapping => mapping with { MatchRequirement = MatchRequirement.Single },
            _ => throw new ArgumentOutOfRangeException(nameof(matchRequirement), matchRequirement, null)
        };

    Func<Type, bool> GetArrayTypeMatchPredicate(Func<Type, bool> elementTypeMatchPredicate)
        => type => type is null ? elementTypeMatchPredicate(null) : type.IsArray && elementTypeMatchPredicate(type.GetElementType());
    Func<Type, bool> GetListTypeMatchPredicate<TElement>(Func<Type, bool> elementTypeMatchPredicate)
        => type => type is null ? elementTypeMatchPredicate(null)
            // We anti-constrain on IsArray to avoid matching byte/sbyte, short/ushort int/uint
            // with the list mapping of the earlier type when an exact match is probably available.
            : !type.IsArray && typeof(IList<TElement>).IsAssignableFrom(type) && elementTypeMatchPredicate(typeof(TElement));

    /// <summary>Registers a mapping for a reference type, plus an <see cref="object"/> mapping for the same data type name when the mapping is a default.</summary>
    /// <typeparam name="T">The reference type to map.</typeparam>
    /// <param name="dataTypeName">The PostgreSQL data type name.</param>
    /// <param name="createInfo">The factory creating the type info.</param>
    /// <param name="isDefault">Whether this is the default CLR type for the data type name (<see cref="MatchRequirement.Single"/>) rather than requiring both to match (<see cref="MatchRequirement.All"/>).</param>
    public void AddType<T>(string dataTypeName, TypeInfoFactory createInfo, bool isDefault = false) where T : class
        => AddType<T>(dataTypeName, createInfo, GetDefaultConfigure(isDefault));

    /// <summary>Registers a mapping for a reference type with an explicit match requirement.</summary>
    /// <typeparam name="T">The reference type to map.</typeparam>
    /// <param name="dataTypeName">The PostgreSQL data type name.</param>
    /// <param name="createInfo">The factory creating the type info.</param>
    /// <param name="matchRequirement">The match requirement for the new mapping.</param>
    public void AddType<T>(string dataTypeName, TypeInfoFactory createInfo, MatchRequirement matchRequirement) where T : class
        => AddType<T>(dataTypeName, createInfo, GetDefaultConfigure(matchRequirement));

    /// <summary>Registers a mapping for a reference type, letting the caller adjust the mapping before it is added.</summary>
    /// <typeparam name="T">The reference type to map.</typeparam>
    /// <param name="dataTypeName">The PostgreSQL data type name.</param>
    /// <param name="createInfo">The factory creating the type info.</param>
    /// <param name="configure">A callback that returns the (possibly modified) mapping to register.</param>
    public void AddType<T>(string dataTypeName, TypeInfoFactory createInfo, Func<TypeInfoMapping, TypeInfoMapping> configure) where T : class
    {
        var mapping = new TypeInfoMapping(typeof(T), dataTypeName, createInfo);
        mapping = configure?.Invoke(mapping) ?? mapping;
        if (typeof(T) != typeof(object) && mapping.MatchRequirement is MatchRequirement.DataTypeName or MatchRequirement.Single && !TryGetMapping(typeof(object), mapping.DataTypeName, out _))
            _items.Add(new TypeInfoMapping(typeof(object), dataTypeName,
                CreateComposedFactory(typeof(T), mapping, static (_, info) => info.GetResolution().Converter, copyPreferredFormat: true))
            {
                MatchRequirement = mapping.MatchRequirement
            });
        _items.Add(mapping);
    }

    /// <summary>Registers a resolver-based mapping (whose type info is a <see cref="PgResolverTypeInfo"/>) for a reference type.</summary>
    /// <typeparam name="T">The reference type to map.</typeparam>
    /// <param name="dataTypeName">The PostgreSQL data type name.</param>
    /// <param name="createInfo">The factory creating the resolver type info.</param>
    /// <param name="isDefault">Whether this is the default CLR type for the data type name.</param>
    public void AddResolverType<T>(string dataTypeName, TypeInfoFactory createInfo, bool isDefault = false) where T : class
        => AddResolverType<T>(dataTypeName, createInfo, GetDefaultConfigure(isDefault));

    /// <summary>Registers a resolver-based mapping for a reference type with an explicit match requirement.</summary>
    /// <typeparam name="T">The reference type to map.</typeparam>
    /// <param name="dataTypeName">The PostgreSQL data type name.</param>
    /// <param name="createInfo">The factory creating the resolver type info.</param>
    /// <param name="matchRequirement">The match requirement for the new mapping.</param>
    public void AddResolverType<T>(string dataTypeName, TypeInfoFactory createInfo, MatchRequirement matchRequirement) where T : class
        => AddResolverType<T>(dataTypeName, createInfo, GetDefaultConfigure(matchRequirement));

    /// <summary>Registers a resolver-based mapping for a reference type, letting the caller adjust the mapping before it is added.</summary>
    /// <typeparam name="T">The reference type to map.</typeparam>
    /// <param name="dataTypeName">The PostgreSQL data type name.</param>
    /// <param name="createInfo">The factory creating the resolver type info.</param>
    /// <param name="configure">A callback that returns the (possibly modified) mapping to register.</param>
    public void AddResolverType<T>(string dataTypeName, TypeInfoFactory createInfo, Func<TypeInfoMapping, TypeInfoMapping> configure) where T : class
    {
        var mapping = new TypeInfoMapping(typeof(T), dataTypeName, createInfo);
        mapping = configure?.Invoke(mapping) ?? mapping;
        if (typeof(T) != typeof(object) && mapping.MatchRequirement is MatchRequirement.DataTypeName or MatchRequirement.Single && !TryGetMapping(typeof(object), mapping.DataTypeName, out _))
            _items.Add(new TypeInfoMapping(typeof(object), dataTypeName,
                CreateComposedFactory(typeof(T), mapping, static (_, info) => info.GetConverterResolver(), copyPreferredFormat: true))
            {
                MatchRequirement = mapping.MatchRequirement
            });
        _items.Add(mapping);
    }

    /// <summary>Registers array (<c>TElement[]</c>, any rank) and <see cref="IList{T}"/> mappings for the PostgreSQL array of an already registered element mapping.</summary>
    /// <typeparam name="TElement">The reference element type.</typeparam>
    /// <param name="elementDataTypeName">The data type name of the registered element mapping.</param>
    public void AddArrayType<TElement>(string elementDataTypeName) where TElement : class
        => AddArrayType<TElement>(GetMapping(typeof(TElement), elementDataTypeName), suppressObjectMapping: false);

    /// <summary>Registers array and list mappings for the PostgreSQL array of an already registered element mapping.</summary>
    /// <typeparam name="TElement">The reference element type.</typeparam>
    /// <param name="elementDataTypeName">The data type name of the registered element mapping.</param>
    /// <param name="suppressObjectMapping">Whether to skip registering an <see cref="object"/> mapping for the array data type.</param>
    public void AddArrayType<TElement>(string elementDataTypeName, bool suppressObjectMapping) where TElement : class
        => AddArrayType<TElement>(GetMapping(typeof(TElement), elementDataTypeName), suppressObjectMapping);

    /// <summary>Registers array and list mappings for the PostgreSQL array of the given element mapping.</summary>
    /// <typeparam name="TElement">The reference element type.</typeparam>
    /// <param name="elementMapping">The element mapping.</param>
    public void AddArrayType<TElement>(TypeInfoMapping elementMapping) where TElement : class
        => AddArrayType<TElement>(elementMapping, suppressObjectMapping: false);

    /// <summary>Registers array and list mappings for the PostgreSQL array of the given element mapping.</summary>
    /// <typeparam name="TElement">The reference element type.</typeparam>
    /// <param name="elementMapping">The element mapping.</param>
    /// <param name="suppressObjectMapping">Whether to skip registering an <see cref="object"/> mapping for the array data type.</param>
    public void AddArrayType<TElement>(TypeInfoMapping elementMapping, bool suppressObjectMapping) where TElement : class
    {
        // Always use a predicate to match all dimensions.
        var arrayTypeMatchPredicate = GetArrayTypeMatchPredicate(elementMapping.TypeMatchPredicate ?? (static type => type is null || type == typeof(TElement)));
        var listTypeMatchPredicate = GetListTypeMatchPredicate<TElement>(elementMapping.TypeMatchPredicate ?? (static type => type is null || type == typeof(TElement)));

        var arrayDataTypeName = GetArrayDataTypeName(elementMapping.DataTypeName);

        AddArrayType(elementMapping, typeof(TElement[]), CreateArrayBasedConverter<TElement>, arrayTypeMatchPredicate, suppressObjectMapping: suppressObjectMapping || TryGetMapping(typeof(object), arrayDataTypeName, out _));
        AddArrayType(elementMapping, typeof(IList<TElement>), CreateListBasedConverter<TElement>, listTypeMatchPredicate, suppressObjectMapping: true);

        void AddArrayType(TypeInfoMapping elementMapping, Type type, Func<TypeInfoMapping, PgTypeInfo, PgConverter> converter, Func<Type, bool> typeMatchPredicate = null, bool suppressObjectMapping = false)
        {
            var arrayMapping = new TypeInfoMapping(type, arrayDataTypeName, CreateComposedFactory(type, elementMapping, converter, supportsReading: true))
            {
                MatchRequirement = elementMapping.MatchRequirement,
                TypeMatchPredicate = typeMatchPredicate
            };
            _items.Add(arrayMapping);
            suppressObjectMapping = suppressObjectMapping || arrayMapping.TypeEquals(typeof(object));
            if (!suppressObjectMapping && arrayMapping.MatchRequirement is MatchRequirement.DataTypeName or MatchRequirement.Single)
                _items.Add(new TypeInfoMapping(typeof(object), arrayDataTypeName, (options, mapping, requiresDataTypeName) =>
                {
                    if (!requiresDataTypeName)
                        throw new InvalidOperationException("Should not happen, please file a bug.");

                    return arrayMapping.Factory(options, mapping, requiresDataTypeName);
                }));
        }
    }

    /// <summary>Registers resolver-based array and list mappings for the PostgreSQL array of an already registered element mapping.</summary>
    /// <typeparam name="TElement">The reference element type.</typeparam>
    /// <param name="elementDataTypeName">The data type name of the registered element mapping.</param>
    public void AddResolverArrayType<TElement>(string elementDataTypeName) where TElement : class
        => AddResolverArrayType<TElement>(GetMapping(typeof(TElement), elementDataTypeName), suppressObjectMapping: false);

    /// <summary>Registers resolver-based array and list mappings for the PostgreSQL array of an already registered element mapping.</summary>
    /// <typeparam name="TElement">The reference element type.</typeparam>
    /// <param name="elementDataTypeName">The data type name of the registered element mapping.</param>
    /// <param name="suppressObjectMapping">Whether to skip registering an <see cref="object"/> mapping for the array data type.</param>
    public void AddResolverArrayType<TElement>(string elementDataTypeName, bool suppressObjectMapping) where TElement : class
        => AddResolverArrayType<TElement>(GetMapping(typeof(TElement), elementDataTypeName), suppressObjectMapping);

    /// <summary>Registers resolver-based array and list mappings for the PostgreSQL array of the given element mapping.</summary>
    /// <typeparam name="TElement">The reference element type.</typeparam>
    /// <param name="elementMapping">The element mapping.</param>
    public void AddResolverArrayType<TElement>(TypeInfoMapping elementMapping) where TElement : class
        => AddResolverArrayType<TElement>(elementMapping, suppressObjectMapping: false);

    /// <summary>Registers resolver-based array and list mappings for the PostgreSQL array of the given element mapping.</summary>
    /// <typeparam name="TElement">The reference element type.</typeparam>
    /// <param name="elementMapping">The element mapping.</param>
    /// <param name="suppressObjectMapping">Whether to skip registering an <see cref="object"/> mapping for the array data type.</param>
    public void AddResolverArrayType<TElement>(TypeInfoMapping elementMapping, bool suppressObjectMapping) where TElement : class
    {
        // Always use a predicate to match all dimensions.
        var arrayTypeMatchPredicate = GetArrayTypeMatchPredicate(elementMapping.TypeMatchPredicate ?? (static type => type is null || type == typeof(TElement)));
        var listTypeMatchPredicate = GetListTypeMatchPredicate<TElement>(elementMapping.TypeMatchPredicate ?? (static type => type is null || type == typeof(TElement)));

        var arrayDataTypeName = GetArrayDataTypeName(elementMapping.DataTypeName);

        AddResolverArrayType(elementMapping, typeof(TElement[]), CreateArrayBasedConverterResolver<TElement>, arrayTypeMatchPredicate, suppressObjectMapping: suppressObjectMapping || TryGetMapping(typeof(object), arrayDataTypeName, out _));
        AddResolverArrayType(elementMapping, typeof(IList<TElement>), CreateListBasedConverterResolver<TElement>, listTypeMatchPredicate, suppressObjectMapping: true);

        void AddResolverArrayType(TypeInfoMapping elementMapping, Type type, Func<TypeInfoMapping, PgResolverTypeInfo, PgConverterResolver> converter, Func<Type, bool> typeMatchPredicate = null, bool suppressObjectMapping = false)
        {
            var arrayMapping = new TypeInfoMapping(type, arrayDataTypeName, CreateComposedFactory(type, elementMapping, converter, supportsReading: true))
            {
                MatchRequirement = elementMapping.MatchRequirement,
                TypeMatchPredicate = typeMatchPredicate
            };
            _items.Add(arrayMapping);
            suppressObjectMapping = suppressObjectMapping || arrayMapping.TypeEquals(typeof(object));
            if (!suppressObjectMapping && arrayMapping.MatchRequirement is MatchRequirement.DataTypeName or MatchRequirement.Single)
                _items.Add(new TypeInfoMapping(typeof(object), arrayDataTypeName, (options, mapping, requiresDataTypeName) =>
                {
                    if (!requiresDataTypeName)
                        throw new InvalidOperationException("Should not happen, please file a bug.");

                    return arrayMapping.Factory(options, mapping, requiresDataTypeName);
                }));
        }
    }

    /// <summary>Registers mappings for a value type and its <see cref="Nullable{T}"/> counterpart.</summary>
    /// <typeparam name="T">The value type to map.</typeparam>
    /// <param name="dataTypeName">The PostgreSQL data type name.</param>
    /// <param name="createInfo">The factory creating the type info.</param>
    /// <param name="isDefault">Whether this is the default CLR type for the data type name.</param>
    public void AddStructType<T>(string dataTypeName, TypeInfoFactory createInfo, bool isDefault = false) where T : struct
        => AddStructType(typeof(T), typeof(T?), dataTypeName, createInfo,
            static (_, innerInfo) => new NullableConverter<T>(innerInfo.GetResolution().GetConverter<T>()), GetDefaultConfigure(isDefault));

    /// <summary>Registers mappings for a value type and its <see cref="Nullable{T}"/> counterpart with an explicit match requirement.</summary>
    /// <typeparam name="T">The value type to map.</typeparam>
    /// <param name="dataTypeName">The PostgreSQL data type name.</param>
    /// <param name="createInfo">The factory creating the type info.</param>
    /// <param name="matchRequirement">The match requirement for the new mappings.</param>
    public void AddStructType<T>(string dataTypeName, TypeInfoFactory createInfo, MatchRequirement matchRequirement) where T : struct
        => AddStructType(typeof(T), typeof(T?), dataTypeName, createInfo,
            static (_, innerInfo) => new NullableConverter<T>(innerInfo.GetResolution().GetConverter<T>()), GetDefaultConfigure(matchRequirement));

    /// <summary>Registers mappings for a value type and its <see cref="Nullable{T}"/> counterpart, letting the caller adjust the mapping before it is added.</summary>
    /// <typeparam name="T">The value type to map.</typeparam>
    /// <param name="dataTypeName">The PostgreSQL data type name.</param>
    /// <param name="createInfo">The factory creating the type info.</param>
    /// <param name="configure">A callback that returns the (possibly modified) mapping to register.</param>
    public void AddStructType<T>(string dataTypeName, TypeInfoFactory createInfo, Func<TypeInfoMapping, TypeInfoMapping> configure) where T : struct
        => AddStructType(typeof(T), typeof(T?), dataTypeName, createInfo,
            static (_, innerInfo) => new NullableConverter<T>(innerInfo.GetResolution().GetConverter<T>()), configure);

    // Lives outside to prevent capture of T.
    void AddStructType(Type type, Type nullableType, string dataTypeName, TypeInfoFactory createInfo,
        Func<TypeInfoMapping, PgTypeInfo, PgConverter> nullableConverter, Func<TypeInfoMapping, TypeInfoMapping> configure)
    {
        var mapping = new TypeInfoMapping(type, dataTypeName, createInfo);
        mapping = configure?.Invoke(mapping) ?? mapping;
        if (type != typeof(object) && mapping.MatchRequirement is MatchRequirement.DataTypeName or MatchRequirement.Single && !TryGetMapping(typeof(object), mapping.DataTypeName, out _))
            _items.Add(new TypeInfoMapping(typeof(object), dataTypeName,
                CreateComposedFactory(type, mapping, static (_, info) => info.GetResolution().Converter, copyPreferredFormat: true))
            {
                MatchRequirement = mapping.MatchRequirement
            });
        _items.Add(mapping);
        _items.Add(new TypeInfoMapping(nullableType, dataTypeName,
            CreateComposedFactory(nullableType, mapping, nullableConverter, copyPreferredFormat: true))
            {
                MatchRequirement = mapping.MatchRequirement,
                TypeMatchPredicate = mapping.TypeMatchPredicate is not null
                    ? matchType => matchType is null
                        ? mapping.TypeMatchPredicate(null)
                        : matchType == nullableType && mapping.TypeMatchPredicate(type)
                    : null
            });
    }

    /// <summary>Registers array and list mappings (for both <typeparamref name="TElement"/> and its nullable form) for the PostgreSQL array of an already registered value-type element mapping.</summary>
    /// <typeparam name="TElement">The value element type.</typeparam>
    /// <param name="elementDataTypeName">The data type name of the registered element mapping.</param>
    public void AddStructArrayType<TElement>(string elementDataTypeName) where TElement : struct
        => AddStructArrayType<TElement>(GetMapping(typeof(TElement), elementDataTypeName), GetMapping(typeof(TElement?), elementDataTypeName), suppressObjectMapping: false);

    /// <summary>Registers array and list mappings (non-nullable and nullable elements) for the PostgreSQL array of an already registered value-type element mapping.</summary>
    /// <typeparam name="TElement">The value element type.</typeparam>
    /// <param name="elementDataTypeName">The data type name of the registered element mapping.</param>
    /// <param name="suppressObjectMapping">Whether to skip registering an <see cref="object"/> mapping for the array data type.</param>
    public void AddStructArrayType<TElement>(string elementDataTypeName, bool suppressObjectMapping) where TElement : struct
        => AddStructArrayType<TElement>(GetMapping(typeof(TElement), elementDataTypeName), GetMapping(typeof(TElement?), elementDataTypeName), suppressObjectMapping);

    /// <summary>Registers array and list mappings (non-nullable and nullable elements) for the PostgreSQL array of the given value-type element mappings.</summary>
    /// <typeparam name="TElement">The value element type.</typeparam>
    /// <param name="elementMapping">The mapping for the non-nullable element type.</param>
    /// <param name="nullableElementMapping">The mapping for the nullable element type.</param>
    public void AddStructArrayType<TElement>(TypeInfoMapping elementMapping, TypeInfoMapping nullableElementMapping) where TElement : struct
        => AddStructArrayType<TElement>(elementMapping, nullableElementMapping, suppressObjectMapping: false);

    /// <summary>Registers array and list mappings (non-nullable and nullable elements) for the PostgreSQL array of the given value-type element mappings.</summary>
    /// <typeparam name="TElement">The value element type.</typeparam>
    /// <param name="elementMapping">The mapping for the non-nullable element type.</param>
    /// <param name="nullableElementMapping">The mapping for the nullable element type.</param>
    /// <param name="suppressObjectMapping">Whether to skip registering an <see cref="object"/> mapping for the array data type.</param>
    public void AddStructArrayType<TElement>(TypeInfoMapping elementMapping, TypeInfoMapping nullableElementMapping, bool suppressObjectMapping) where TElement : struct
    {
        // Always use a predicate to match all dimensions.
        var arrayTypeMatchPredicate = GetArrayTypeMatchPredicate(elementMapping.TypeMatchPredicate ?? (static type => type is null || type == typeof(TElement)));
        var nullableArrayTypeMatchPredicate = GetArrayTypeMatchPredicate(nullableElementMapping.TypeMatchPredicate ?? (static type =>
            type is null || type == typeof(TElement?)));
        var listTypeMatchPredicate = GetListTypeMatchPredicate<TElement>(elementMapping.TypeMatchPredicate  ?? (static type => type is null || type == typeof(TElement)));
        var nullableListTypeMatchPredicate = GetListTypeMatchPredicate<TElement?>(nullableElementMapping.TypeMatchPredicate ?? (static type =>
            type is null || type == typeof(TElement?)));

        var arrayDataTypeName = GetArrayDataTypeName(elementMapping.DataTypeName);

        AddStructArrayType(elementMapping, nullableElementMapping, typeof(TElement[]), typeof(TElement?[]),
            CreateArrayBasedConverter<TElement>, CreateArrayBasedConverter<TElement?>,
            arrayTypeMatchPredicate, nullableArrayTypeMatchPredicate, suppressObjectMapping: suppressObjectMapping || TryGetMapping(typeof(object), arrayDataTypeName, out _));

        // Don't add the object converter for the list based converter.
        AddStructArrayType(elementMapping, nullableElementMapping, typeof(IList<TElement>), typeof(IList<TElement?>),
            CreateListBasedConverter<TElement>, CreateListBasedConverter<TElement?>,
            listTypeMatchPredicate, nullableListTypeMatchPredicate, suppressObjectMapping: true);
    }

    // Lives outside to prevent capture of TElement.
    void AddStructArrayType(TypeInfoMapping elementMapping, TypeInfoMapping nullableElementMapping, Type type, Type nullableType,
        Func<TypeInfoMapping, PgTypeInfo, PgConverter> converter, Func<TypeInfoMapping, PgTypeInfo, PgConverter> nullableConverter,
        Func<Type, bool> typeMatchPredicate, Func<Type, bool> nullableTypeMatchPredicate, bool suppressObjectMapping)
    {
        var arrayDataTypeName = GetArrayDataTypeName(elementMapping.DataTypeName);
        var arrayMapping = new TypeInfoMapping(type, arrayDataTypeName, CreateComposedFactory(type, elementMapping, converter, supportsReading: true))
        {
            MatchRequirement = elementMapping.MatchRequirement,
            TypeMatchPredicate = typeMatchPredicate
        };
        var nullableArrayMapping = new TypeInfoMapping(nullableType, arrayDataTypeName, CreateComposedFactory(nullableType, nullableElementMapping, nullableConverter, supportsReading: true))
        {
            MatchRequirement = arrayMapping.MatchRequirement,
            TypeMatchPredicate = nullableTypeMatchPredicate
        };

        _items.Add(arrayMapping);
        _items.Add(nullableArrayMapping);
        suppressObjectMapping = suppressObjectMapping || arrayMapping.TypeEquals(typeof(object));
        if (!suppressObjectMapping && arrayMapping.MatchRequirement is MatchRequirement.DataTypeName or MatchRequirement.Single)
            _items.Add(new TypeInfoMapping(typeof(object), arrayDataTypeName, (options, mapping, requiresDataTypeName) =>
            {
                return options.ArrayNullabilityMode switch
                {
                    _ when !requiresDataTypeName => throw new InvalidOperationException("Should not happen, please file a bug."),
                    ArrayNullabilityMode.Never => arrayMapping.Factory(options, mapping, requiresDataTypeName),
                    ArrayNullabilityMode.Always => nullableArrayMapping.Factory(options, mapping, requiresDataTypeName),
                    ArrayNullabilityMode.PerInstance => CreateComposedPerInstance(
                        arrayMapping.Factory(options, mapping, requiresDataTypeName),
                        nullableArrayMapping.Factory(options, mapping, requiresDataTypeName),
                        mapping.DataTypeName
                    ),
                    _ => throw new ArgumentOutOfRangeException()
                };
            }) { MatchRequirement = MatchRequirement.DataTypeName });

        PgTypeInfo CreateComposedPerInstance(PgTypeInfo innerTypeInfo, PgTypeInfo nullableInnerTypeInfo, string dataTypeName)
        {
            var converter =
                new PolymorphicArrayConverter<Array>(
                    innerTypeInfo.GetResolution().GetConverter<Array>(),
                    nullableInnerTypeInfo.GetResolution().GetConverter<Array>());

            return new PgTypeInfo(innerTypeInfo.Options, converter,
                innerTypeInfo.Options.GetCanonicalTypeId(new DataTypeName(dataTypeName)), unboxedType: typeof(Array)) { SupportsWriting = false };
        }
    }

    /// <summary>Registers resolver-based mappings for a value type and its <see cref="Nullable{T}"/> counterpart.</summary>
    /// <typeparam name="T">The value type to map.</typeparam>
    /// <param name="dataTypeName">The PostgreSQL data type name.</param>
    /// <param name="createInfo">The factory creating the resolver type info.</param>
    /// <param name="isDefault">Whether this is the default CLR type for the data type name.</param>
    public void AddResolverStructType<T>(string dataTypeName, TypeInfoFactory createInfo, bool isDefault = false) where T : struct
        => AddResolverStructType(typeof(T), typeof(T?), dataTypeName, createInfo,
            static (_, innerInfo) => new NullableConverterResolver<T>(innerInfo), GetDefaultConfigure(isDefault));

    /// <summary>Registers resolver-based mappings for a value type and its <see cref="Nullable{T}"/> counterpart with an explicit match requirement.</summary>
    /// <typeparam name="T">The value type to map.</typeparam>
    /// <param name="dataTypeName">The PostgreSQL data type name.</param>
    /// <param name="createInfo">The factory creating the resolver type info.</param>
    /// <param name="matchRequirement">The match requirement for the new mappings.</param>
    public void AddResolverStructType<T>(string dataTypeName, TypeInfoFactory createInfo, MatchRequirement matchRequirement) where T : struct
        => AddResolverStructType(typeof(T), typeof(T?), dataTypeName, createInfo,
            static (_, innerInfo) => new NullableConverterResolver<T>(innerInfo), GetDefaultConfigure(matchRequirement));

    /// <summary>Registers resolver-based mappings for a value type and its <see cref="Nullable{T}"/> counterpart, letting the caller adjust the mapping before it is added.</summary>
    /// <typeparam name="T">The value type to map.</typeparam>
    /// <param name="dataTypeName">The PostgreSQL data type name.</param>
    /// <param name="createInfo">The factory creating the resolver type info.</param>
    /// <param name="configure">A callback that returns the (possibly modified) mapping to register.</param>
    public void AddResolverStructType<T>(string dataTypeName, TypeInfoFactory createInfo, Func<TypeInfoMapping, TypeInfoMapping> configure) where T : struct
        => AddResolverStructType(typeof(T), typeof(T?), dataTypeName, createInfo,
            static (_, innerInfo) => new NullableConverterResolver<T>(innerInfo), configure);

    // Lives outside to prevent capture of T.
    void AddResolverStructType(Type type, Type nullableType, string dataTypeName, TypeInfoFactory createInfo,
        Func<TypeInfoMapping, PgResolverTypeInfo, PgConverterResolver> nullableConverter, Func<TypeInfoMapping, TypeInfoMapping> configure)
    {
        var mapping = new TypeInfoMapping(type, dataTypeName, createInfo);
        mapping = configure?.Invoke(mapping) ?? mapping;
        if (type != typeof(object) && mapping.MatchRequirement is MatchRequirement.DataTypeName or MatchRequirement.Single && !TryGetMapping(typeof(object), mapping.DataTypeName, out _))
            _items.Add(new TypeInfoMapping(typeof(object), dataTypeName,
                CreateComposedFactory(type, mapping, static (_, info) => info.GetConverterResolver(), copyPreferredFormat: true))
            {
                MatchRequirement = mapping.MatchRequirement
            });
        _items.Add(mapping);
        _items.Add(new TypeInfoMapping(nullableType, dataTypeName,
            CreateComposedFactory(nullableType, mapping, nullableConverter, copyPreferredFormat: true))
            {
                MatchRequirement = mapping.MatchRequirement,
                TypeMatchPredicate = mapping.TypeMatchPredicate is not null
                    ? matchType => matchType is null
                        ? mapping.TypeMatchPredicate(null)
                        : matchType == nullableType && mapping.TypeMatchPredicate(type)
                    : null
            });
    }

    /// <summary>Registers resolver-based array and list mappings (non-nullable and nullable elements) for the PostgreSQL array of an already registered value-type element mapping.</summary>
    /// <typeparam name="TElement">The value element type.</typeparam>
    /// <param name="elementDataTypeName">The data type name of the registered element mapping.</param>
    public void AddResolverStructArrayType<TElement>(string elementDataTypeName) where TElement : struct
        => AddResolverStructArrayType<TElement>(GetMapping(typeof(TElement), elementDataTypeName), GetMapping(typeof(TElement?), elementDataTypeName), suppressObjectMapping: false);

    /// <summary>Registers resolver-based array and list mappings (non-nullable and nullable elements) for the PostgreSQL array of an already registered value-type element mapping.</summary>
    /// <typeparam name="TElement">The value element type.</typeparam>
    /// <param name="elementDataTypeName">The data type name of the registered element mapping.</param>
    /// <param name="suppressObjectMapping">Whether to skip registering an <see cref="object"/> mapping for the array data type.</param>
    public void AddResolverStructArrayType<TElement>(string elementDataTypeName, bool suppressObjectMapping) where TElement : struct
        => AddResolverStructArrayType<TElement>(GetMapping(typeof(TElement), elementDataTypeName), GetMapping(typeof(TElement?), elementDataTypeName), suppressObjectMapping);

    /// <summary>Registers resolver-based array and list mappings (non-nullable and nullable elements) for the PostgreSQL array of the given value-type element mappings.</summary>
    /// <typeparam name="TElement">The value element type.</typeparam>
    /// <param name="elementMapping">The mapping for the non-nullable element type.</param>
    /// <param name="nullableElementMapping">The mapping for the nullable element type.</param>
    public void AddResolverStructArrayType<TElement>(TypeInfoMapping elementMapping, TypeInfoMapping nullableElementMapping) where TElement : struct
        => AddResolverStructArrayType<TElement>(elementMapping, nullableElementMapping, suppressObjectMapping: false);

    /// <summary>Registers resolver-based array and list mappings (non-nullable and nullable elements) for the PostgreSQL array of the given value-type element mappings.</summary>
    /// <typeparam name="TElement">The value element type.</typeparam>
    /// <param name="elementMapping">The mapping for the non-nullable element type.</param>
    /// <param name="nullableElementMapping">The mapping for the nullable element type.</param>
    /// <param name="suppressObjectMapping">Whether to skip registering an <see cref="object"/> mapping for the array data type.</param>
    public void AddResolverStructArrayType<TElement>(TypeInfoMapping elementMapping, TypeInfoMapping nullableElementMapping, bool suppressObjectMapping) where TElement : struct
    {
        // Always use a predicate to match all dimensions.
        var arrayTypeMatchPredicate = GetArrayTypeMatchPredicate(elementMapping.TypeMatchPredicate ?? (static type => type is null || type == typeof(TElement)));
        var nullableArrayTypeMatchPredicate = GetArrayTypeMatchPredicate(nullableElementMapping.TypeMatchPredicate ?? (static type =>
            type is null || type == typeof(TElement?)));
        var listTypeMatchPredicate = GetListTypeMatchPredicate<TElement>(elementMapping.TypeMatchPredicate  ?? (static type => type is null || type == typeof(TElement)));
        var nullableListTypeMatchPredicate = GetListTypeMatchPredicate<TElement?>(nullableElementMapping.TypeMatchPredicate ?? (static type =>
            type is null || type == typeof(TElement?)));

        var arrayDataTypeName = GetArrayDataTypeName(elementMapping.DataTypeName);

        AddResolverStructArrayType(elementMapping, nullableElementMapping, typeof(TElement[]), typeof(TElement?[]),
            CreateArrayBasedConverterResolver<TElement>,
            CreateArrayBasedConverterResolver<TElement?>, suppressObjectMapping: suppressObjectMapping || TryGetMapping(typeof(object), arrayDataTypeName, out _), arrayTypeMatchPredicate, nullableArrayTypeMatchPredicate);

        // Don't add the object converter for the list based converter.
        AddResolverStructArrayType(elementMapping, nullableElementMapping, typeof(IList<TElement>), typeof(IList<TElement?>),
            CreateListBasedConverterResolver<TElement>,
            CreateListBasedConverterResolver<TElement?>, suppressObjectMapping: true, listTypeMatchPredicate, nullableListTypeMatchPredicate);
    }

    // Lives outside to prevent capture of TElement.
    void AddResolverStructArrayType(TypeInfoMapping elementMapping, TypeInfoMapping nullableElementMapping, Type type, Type nullableType,
            Func<TypeInfoMapping, PgResolverTypeInfo, PgConverterResolver> converter, Func<TypeInfoMapping, PgResolverTypeInfo, PgConverterResolver> nullableConverter,
            bool suppressObjectMapping, Func<Type, bool> typeMatchPredicate, Func<Type, bool> nullableTypeMatchPredicate)
        {
            var arrayDataTypeName = GetArrayDataTypeName(elementMapping.DataTypeName);

            var arrayMapping = new TypeInfoMapping(type, arrayDataTypeName, CreateComposedFactory(type, elementMapping, converter, supportsReading: true))
            {
                MatchRequirement = elementMapping.MatchRequirement,
                TypeMatchPredicate = typeMatchPredicate
            };
            var nullableArrayMapping = new TypeInfoMapping(nullableType, arrayDataTypeName, CreateComposedFactory(nullableType, nullableElementMapping, nullableConverter, supportsReading: true))
            {
                MatchRequirement = elementMapping.MatchRequirement,
                TypeMatchPredicate = nullableTypeMatchPredicate
            };

            _items.Add(arrayMapping);
            _items.Add(nullableArrayMapping);
            suppressObjectMapping = suppressObjectMapping || arrayMapping.TypeEquals(typeof(object));
            if (!suppressObjectMapping && arrayMapping.MatchRequirement is MatchRequirement.DataTypeName or MatchRequirement.Single)
                _items.Add(new TypeInfoMapping(typeof(object), arrayDataTypeName, (options, mapping, requiresDataTypeName) => options.ArrayNullabilityMode switch
                {
                    _ when !requiresDataTypeName => throw new InvalidOperationException("Should not happen, please file a bug."),
                    ArrayNullabilityMode.Never => arrayMapping.Factory(options, mapping, requiresDataTypeName),
                    ArrayNullabilityMode.Always => nullableArrayMapping.Factory(options, mapping, requiresDataTypeName),
                    ArrayNullabilityMode.PerInstance => CreateComposedPerInstance(
                        arrayMapping.Factory(options, mapping, requiresDataTypeName),
                        nullableArrayMapping.Factory(options, mapping, requiresDataTypeName),
                        mapping.DataTypeName
                    ),
                    _ => throw new ArgumentOutOfRangeException()
                }) { MatchRequirement = MatchRequirement.DataTypeName });

            PgTypeInfo CreateComposedPerInstance(PgTypeInfo innerTypeInfo, PgTypeInfo nullableInnerTypeInfo, string dataTypeName)
            {
                var resolver =
                    new PolymorphicArrayConverterResolver<Array>((PgResolverTypeInfo)innerTypeInfo,
                        (PgResolverTypeInfo)nullableInnerTypeInfo);

                return new PgResolverTypeInfo(innerTypeInfo.Options, resolver,
                    innerTypeInfo.Options.GetCanonicalTypeId(new DataTypeName(dataTypeName)), unboxedType: typeof(Array)) { SupportsWriting = false };
            }
        }

    /// <summary>Registers a read-only <see cref="object"/> mapping for the PostgreSQL array of an already registered <see cref="object"/> element mapping, where the array's CLR type is chosen per element resolution.</summary>
    /// <param name="elementDataTypeName">The data type name of the registered <see cref="object"/> element mapping.</param>
    /// <param name="elementToArrayConverterFactory">Given the options, returns a function that builds the array converter for a resolved element converter.</param>
    public void AddPolymorphicResolverArrayType(string elementDataTypeName, Func<PgSerializerOptions, Func<PgConverterResolution, PgConverter>> elementToArrayConverterFactory)
        => AddPolymorphicResolverArrayType(GetMapping(typeof(object), elementDataTypeName), elementToArrayConverterFactory);

    /// <summary>Registers a read-only <see cref="object"/> mapping for the PostgreSQL array of the given element mapping, where the array's CLR type is chosen per element resolution.</summary>
    /// <param name="elementMapping">The element mapping.</param>
    /// <param name="elementToArrayConverterFactory">Given the options, returns a function that builds the array converter for a resolved element converter.</param>
    public void AddPolymorphicResolverArrayType(TypeInfoMapping elementMapping, Func<PgSerializerOptions, Func<PgConverterResolution, PgConverter>> elementToArrayConverterFactory)
    {
        AddPolymorphicResolverArrayType(elementMapping, typeof(object),
            (mapping, elemInfo) => new ArrayPolymorphicConverterResolver(
                elemInfo.Options.GetCanonicalTypeId(new DataTypeName(mapping.DataTypeName)), elemInfo, elementToArrayConverterFactory(elemInfo.Options))
        , null);

        void AddPolymorphicResolverArrayType(TypeInfoMapping elementMapping, Type type, Func<TypeInfoMapping, PgResolverTypeInfo, PgConverterResolver> converter, Func<Type, bool> typeMatchPredicate)
        {
            var arrayDataTypeName = GetArrayDataTypeName(elementMapping.DataTypeName);
            var mapping = new TypeInfoMapping(type, arrayDataTypeName,
                CreateComposedFactory(typeof(Array), elementMapping, converter, supportsReading: true, supportsWriting: false))
            {
                MatchRequirement = elementMapping.MatchRequirement,
                TypeMatchPredicate = typeMatchPredicate
            };
            _items.Add(mapping);
        }
    }

    /// Returns whether type matches any of the types we register pg arrays as.
    [UnconditionalSuppressMessage("Trimming", "IL2070",
        Justification = "Checking for IList<T> implementing types requires interface list enumeration which isn't compatible with trimming. " +
                        "However as long as a concrete IList<T> is rooted somewhere in the app, for instance through an `AddArrayType<T>(...)` mapping, every implementation must keep it.")]
    // We care about IList<T> implementations if the instantiation is actually rooted by us through an Array mapping.
    // Dynamic resolvers are a notable counterexample, but they are all correctly marked with RequiresUnreferencedCode.
    public static bool IsArrayLikeType(Type type, [NotNullWhen(true)] out Type elementType)
    {
        if (type.GetElementType() is { } t)
        {
            elementType = t;
            return true;
        }

        if (type.IsConstructedGenericType && type.GetGenericTypeDefinition() is var def && (def == typeof(List<>) || def == typeof(IList<>)))
        {
            elementType = type.GetGenericArguments()[0];
            return true;
        }

        foreach (var inf in type.GetInterfaces())
        {
            if (inf.IsConstructedGenericType && inf.GetGenericTypeDefinition() == typeof(IList<>))
            {
                elementType = inf.GetGenericArguments()[0];
                return true;
            }
        }

        elementType = null;
        return false;
    }

    static string GetArrayDataTypeName(string dataTypeName)
        => DataTypeName.IsFullyQualified(dataTypeName.AsSpan())
            ? DataTypeName.ValidatedName(dataTypeName).ToArrayName().Value
            : "_" + DataTypeName.FromDisplayName(dataTypeName).UnqualifiedName;

    static ArrayBasedArrayConverter<Array, TElement> CreateArrayBasedConverter<TElement>(TypeInfoMapping mapping, PgTypeInfo elemInfo)
    {
        if (!elemInfo.IsBoxing)
            return new ArrayBasedArrayConverter<Array, TElement>(elemInfo.GetResolution(), mapping.Type);

        ThrowBoxingNotSupported(resolver: false);
        return default;
    }

    static ListBasedArrayConverter<IList<TElement>, TElement> CreateListBasedConverter<TElement>(TypeInfoMapping mapping, PgTypeInfo elemInfo)
    {
        if (!elemInfo.IsBoxing)
            return new ListBasedArrayConverter<IList<TElement>, TElement>(elemInfo.GetResolution());

        ThrowBoxingNotSupported(resolver: false);
        return default;
    }

    static ArrayConverterResolver<Array, TElement> CreateArrayBasedConverterResolver<TElement>(TypeInfoMapping mapping, PgResolverTypeInfo elemInfo)
    {
        if (!elemInfo.IsBoxing)
            return new ArrayConverterResolver<Array, TElement>(elemInfo, mapping.Type);

        ThrowBoxingNotSupported(resolver: true);
        return default;
    }

    static ArrayConverterResolver<IList<TElement>, TElement> CreateListBasedConverterResolver<TElement>(TypeInfoMapping mapping, PgResolverTypeInfo elemInfo)
    {
        if (!elemInfo.IsBoxing)
            return new ArrayConverterResolver<IList<TElement>, TElement>(elemInfo, mapping.Type);

        ThrowBoxingNotSupported(resolver: true);
        return default;
    }

    [DoesNotReturn]
    static void ThrowBoxingNotSupported(bool resolver)
        => throw new InvalidOperationException($"Boxing converters are not supported, manually construct a mapping over a casting converter{(resolver ? " resolver" : "")} instead.");
}

/// <summary>Extension helpers for building <see cref="PgTypeInfo"/> and <see cref="PgResolverTypeInfo"/> instances from a <see cref="TypeInfoMapping"/>.</summary>
[Experimental(PgSqlDiagnostics.ConvertersExperimental)]
public static class TypeInfoMappingHelpers
{
    internal static bool TryResolveFullyQualifiedName(PgSerializerOptions options, string dataTypeName, out DataTypeName fqDataTypeName)
    {
        if (DataTypeName.IsFullyQualified(dataTypeName.AsSpan()))
        {
            fqDataTypeName = new DataTypeName(dataTypeName);
            return true;
        }

        if (options.DatabaseInfo.TryGetPostgresTypeByName(dataTypeName, out var pgType))
        {
            fqDataTypeName = pgType.DataTypeName;
            return true;
        }

        fqDataTypeName = default;
        return false;
    }

    internal static PostgresType GetPgType(this TypeInfoMapping mapping, PgSerializerOptions options)
        => options.DatabaseInfo.GetPostgresType(new DataTypeName(mapping.DataTypeName));

    // NOTE: This method exists since 9.0 to be able to deprecate the method below that has optional arguments in 10.0 (potentially removing it directly or in 11.0).
    // It reduces how binary breaking that change will be if this method would not be there to be picked for the most common invocations.
    /// <summary>
    /// Creates a PgTypeInfo from a mapping, optins, and a converter.
    /// </summary>
    /// <param name="mapping">The mapping to create an info for.</param>
    /// <param name="options">The options to use.</param>
    /// <param name="converter">The converter to create a PgTypeInfo for.</param>
    /// <returns>The created info instance.</returns>
    public static PgTypeInfo CreateInfo(this TypeInfoMapping mapping, PgSerializerOptions options, PgConverter converter)
        => new(options, converter, new DataTypeName(mapping.DataTypeName))
        {
            PreferredFormat = null,
            SupportsWriting = true
        };

    /// <summary>
    /// Creates a PgTypeInfo from a mapping, options, and a converter.
    /// </summary>
    /// <param name="mapping">The mapping to create an info for.</param>
    /// <param name="options">The options to use.</param>
    /// <param name="converter">The converter to create a PgTypeInfo for.</param>
    /// <param name="preferredFormat">Whether to prefer a specific data format for this info, when null it defaults to the most suitable format.</param>
    /// <param name="supportsWriting">Whether the converters returned from the given converter resolver support writing.</param>
    /// <returns>The created info instance.</returns>
    public static PgTypeInfo CreateInfo(this TypeInfoMapping mapping, PgSerializerOptions options, PgConverter converter, DataFormat? preferredFormat = null, bool supportsWriting = true)
        => new(options, converter, new DataTypeName(mapping.DataTypeName))
        {
            PreferredFormat = preferredFormat,
            SupportsWriting = supportsWriting
        };

    // NOTE: This method exists since 9.0 to be able to deprecate the method below that has optional arguments in 10.0 (potentially removing it directly or in 11.0).
    // It reduces how binary breaking that change will be if this method would not be there to be picked for the most common invocations.
    /// <summary>
    /// Creates a PgResolverTypeInfo from a mapping, options, and a converter resolver.
    /// </summary>
    /// <param name="mapping">The mapping to create an info for.</param>
    /// <param name="options">The options to use.</param>
    /// <param name="resolver">The resolver to create a PgResolverTypeInfo for.</param>
    /// <param name="includeDataTypeName">Whether to pass mapping.DataTypeName to the PgResolverTypeInfo constructor, mandatory when TypeInfoFactory(..., requiresDataTypeName: true).</param>
    /// <returns>The created info instance.</returns>
    public static PgResolverTypeInfo CreateInfo(this TypeInfoMapping mapping, PgSerializerOptions options, PgConverterResolver resolver, bool includeDataTypeName)
        => new(options, resolver, includeDataTypeName ? new DataTypeName(mapping.DataTypeName) : null)
        {
            PreferredFormat = null
        };

    /// <summary>
    /// Creates a PgResolverTypeInfo from a mapping, options, and a converter resolver.
    /// </summary>
    /// <param name="mapping">The mapping to create an info for.</param>
    /// <param name="options">The options to use.</param>
    /// <param name="resolver">The converter resolver to create a PgResolverTypeInfo for.</param>
    /// <param name="includeDataTypeName">Whether to pass mapping.DataTypeName to the PgResolverTypeInfo constructor, mandatory when TypeInfoFactory(..., requiresDataTypeName: true).</param>
    /// <param name="preferredFormat">Whether to prefer a specific data format for this info, when null it defaults to the most suitable format.</param>
    /// <param name="supportsWriting">Whether the converters returned from the given converter resolver support writing.</param>
    /// <returns>The created info instance.</returns>
    public static PgResolverTypeInfo CreateInfo(this TypeInfoMapping mapping, PgSerializerOptions options, PgConverterResolver resolver, bool includeDataTypeName, DataFormat? preferredFormat = null, bool supportsWriting = true)
        => new(options, resolver, includeDataTypeName ? new DataTypeName(mapping.DataTypeName) : null)
        {
            PreferredFormat = preferredFormat,
            SupportsWriting = supportsWriting
        };
}
