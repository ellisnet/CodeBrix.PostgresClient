using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;
using CodeBrix.PostgresClient.Internal.Postgres;
using CodeBrix.PostgresClient.NameTranslation;
using CodeBrix.PostgresClient.PostgresTypes;

namespace CodeBrix.PostgresClient.Internal; //was previously: Npgsql.Internal;

/// <summary>The configuration and type catalog used by converters and type info resolvers for one database: text encoding, time zone, resolver chain and the mapping between PostgreSQL type ids and type info.</summary>
[Experimental(PgSqlDiagnostics.ConvertersExperimental)]
public sealed class PgSerializerOptions
{
    /// <summary>
    /// Used by GetSchema to be able to attempt to resolve all type catalog types without exceptions.
    /// </summary>
    [field: ThreadStatic]
    internal static bool IntrospectionCaller { get; set; }

    readonly PgTypeInfoResolverChain _resolverChain;
    readonly Func<string> _timeZoneProvider;
    IPgTypeInfoResolver _typeInfoResolver;
    object _typeInfoCache;

    internal PgSerializerOptions(PgSqlDatabaseInfo databaseInfo, PgTypeInfoResolverChain? resolverChain = null, Func<string> timeZoneProvider = null)
    {
        _resolverChain = resolverChain ?? new();
        _timeZoneProvider = timeZoneProvider;
        DatabaseInfo = databaseInfo;
        UnspecifiedDBNullTypeInfo = new(this, new Converters.Internal.VoidConverter(), DataTypeName.Unspecified, unboxedType: typeof(DBNull));
    }

    internal PgTypeInfo UnspecifiedDBNullTypeInfo { get; }

    PostgresType _textPgType;
    internal PgTypeId TextPgTypeId => ToCanonicalTypeId(_textPgType ??= DatabaseInfo.GetPostgresType(DataTypeNames.Text));

    // Used purely for type mapping, where we don't have a full set of types but resolvers might know enough.
    readonly bool _introspectionInstance;
    internal bool IntrospectionMode
    {
        get => _introspectionInstance || IntrospectionCaller;
        init => _introspectionInstance = value;
    }

    /// Whether options should return a portable identifier (data type name) to prevent any generated id (oid) confusion across backends, this comes with a perf penalty.
    internal bool PortableTypeIds { get; init; }
    internal PgSqlDatabaseInfo DatabaseInfo { get; }

    /// <summary>The session time zone name; throws <see cref="NotSupportedException"/> if no time zone provider was configured.</summary>
    public string TimeZone => _timeZoneProvider?.Invoke() ?? throw new NotSupportedException("TimeZone was not configured.");
    /// <summary>The encoding used for text values; UTF-8 by default.</summary>
    public Encoding TextEncoding { get; init; } = Encoding.UTF8;
    /// <summary>The resolver that produces type info for CLR type and PostgreSQL type pairs, by default the configured resolver chain.</summary>
    public IPgTypeInfoResolver TypeInfoResolver
    {
        get => _typeInfoResolver ??= new ChainTypeInfoResolver(_resolverChain);
        internal init => _typeInfoResolver = value;
    }
    /// <summary>Whether <see cref="DateTime.MinValue"/>/<see cref="DateTime.MaxValue"/> (and similar) are converted to and from PostgreSQL <c>-infinity</c>/<c>infinity</c>; enabled by default.</summary>
    public bool EnableDateTimeInfinityConversions { get; init; } = true;

    /// <summary>How arrays with nullable value-type elements are returned when read as <see cref="object"/>; <see cref="ArrayNullabilityMode.Never"/> by default.</summary>
    public ArrayNullabilityMode ArrayNullabilityMode { get; init; } = ArrayNullabilityMode.Never;
    /// <summary>The translator used to map CLR member names to PostgreSQL names (e.g. for enums and composites); snake_case by default.</summary>
    public IPgSqlNameTranslator DefaultNameTranslator { get; init; } = PgSqlSnakeCaseNameTranslator.Instance;

    /// <summary>Determines whether a CLR type is one of the types that can be read from or written to any text-like PostgreSQL type (strings, chars, char and byte buffers, and streams).</summary>
    /// <param name="type">The type to test; a nullable value type is unwrapped first.</param>
    /// <returns><see langword="true"/> for a well-known text type.</returns>
    public static bool IsWellKnownTextType(Type type)
    {
        type = type.IsValueType ? Nullable.GetUnderlyingType(type) ?? type : type;
        return Array.IndexOf([
            typeof(string), typeof(char),
            typeof(char[]), typeof(ReadOnlyMemory<char>), typeof(ArraySegment<char>),
            typeof(byte[]), typeof(ReadOnlyMemory<byte>)
        ], type) != -1 || typeof(Stream).IsAssignableFrom(type);
    }

    internal bool RangesEnabled => _resolverChain.RangesEnabled;
    internal bool MultirangesEnabled => _resolverChain.MultirangesEnabled;
    internal bool ArraysEnabled => _resolverChain.ArraysEnabled;

    // We don't verify the kind of pgTypeId we get, it'll throw if it's incorrect.
    // It's up to the caller to call GetCanonicalTypeId if they want to use an oid instead of a DataTypeName.
    // This also makes it easier to realize it should be a cached value if infos for different CLR types are requested for the same
    // pgTypeId. Effectively it should be 'impossible' to get the wrong kind via any PgConverterOptions api which is what this is mainly
    // for.
    PgTypeInfo GetTypeInfoCore(Type type, PgTypeId? pgTypeId)
        => PortableTypeIds
            ? ((TypeInfoCache<DataTypeName>)(_typeInfoCache ??= new TypeInfoCache<DataTypeName>(this))).GetOrAddInfo(type, pgTypeId?.DataTypeName)
            : ((TypeInfoCache<Oid>)(_typeInfoCache ??= new TypeInfoCache<Oid>(this))).GetOrAddInfo(type, pgTypeId?.Oid);

    internal PgTypeInfo GetTypeInfoInternal(Type type, PgTypeId? pgTypeId)
        => GetTypeInfoCore(type, pgTypeId);

    /// <summary>Returns the default type info for a CLR type, using the PostgreSQL type it maps to by default.</summary>
    /// <param name="type">The CLR type.</param>
    /// <returns>The type info, or <see langword="null"/> if the type is not supported.</returns>
    public PgTypeInfo GetDefaultTypeInfo(Type type)
        => GetTypeInfoCore(type, null);

    /// <summary>Returns the default type info for a PostgreSQL type, using the CLR type it maps to by default.</summary>
    /// <param name="pgTypeId">The PostgreSQL type id, in either form.</param>
    /// <returns>The type info, or <see langword="null"/> if the type is not supported.</returns>
    public PgTypeInfo GetDefaultTypeInfo(PgTypeId pgTypeId)
        => GetTypeInfoCore(null, GetCanonicalTypeId(pgTypeId));

    /// <summary>Returns the type info for converting between a specific CLR type and a specific PostgreSQL type.</summary>
    /// <param name="type">The CLR type.</param>
    /// <param name="pgTypeId">The PostgreSQL type id, in either form.</param>
    /// <returns>The type info, or <see langword="null"/> if the combination is not supported.</returns>
    public PgTypeInfo GetTypeInfo(Type type, PgTypeId pgTypeId)
        => GetTypeInfoCore(type, GetCanonicalTypeId(pgTypeId));

    // If a given type id is in the opposite form than what was expected it will be mapped according to the requirement.
    internal PgTypeId GetCanonicalTypeId(PgTypeId pgTypeId)
        => PortableTypeIds ? DatabaseInfo.GetDataTypeName(pgTypeId) : DatabaseInfo.GetOid(pgTypeId);

    // If a given type id is in the opposite form than what was expected it will be mapped according to the requirement.
    internal PgTypeId ToCanonicalTypeId(PostgresType pgType)
        => PortableTypeIds ? pgType.DataTypeName : (Oid)pgType.OID;

    /// <summary>Returns the id of the array type whose elements are of the given type.</summary>
    /// <param name="elementTypeId">The element type id.</param>
    /// <returns>The array type id, in canonical form.</returns>
    /// <exception cref="NotSupportedException">The element type has no array type.</exception>
    public PgTypeId GetArrayTypeId(PgTypeId elementTypeId)
    {
        // Static affordance to help the global type mapper.
        if (PortableTypeIds && elementTypeId.IsDataTypeName)
            return elementTypeId.DataTypeName.ToArrayName();

        return ToCanonicalTypeId(DatabaseInfo.GetPostgresType(elementTypeId).Array
                                 ?? throw new NotSupportedException("Cannot resolve array type id"));
    }

    /// <summary>Returns the id of the element type of an array type.</summary>
    /// <param name="arrayTypeId">The array type id.</param>
    /// <returns>The element type id, in canonical form.</returns>
    /// <exception cref="NotSupportedException">The type is not a known array type.</exception>
    public PgTypeId GetArrayElementTypeId(PgTypeId arrayTypeId)
    {
        // Static affordance to help the global type mapper.
        if (PortableTypeIds && arrayTypeId.IsDataTypeName && arrayTypeId.DataTypeName.UnqualifiedNameSpan.StartsWith("_".AsSpan(), StringComparison.Ordinal))
            return new DataTypeName(arrayTypeId.DataTypeName.Schema + arrayTypeId.DataTypeName.UnqualifiedNameSpan.Slice(1).ToString());

        return ToCanonicalTypeId((DatabaseInfo.GetPostgresType(arrayTypeId) as PostgresArrayType)?.Element
                                 ?? throw new NotSupportedException("Cannot resolve array element type id"));
    }

    /// <summary>Returns the id of the range type over the given subtype.</summary>
    /// <param name="subtypeTypeId">The range subtype id.</param>
    /// <returns>The range type id, in canonical form.</returns>
    /// <exception cref="NotSupportedException">The subtype has no range type.</exception>
    public PgTypeId GetRangeTypeId(PgTypeId subtypeTypeId) =>
        ToCanonicalTypeId(DatabaseInfo.GetPostgresType(subtypeTypeId).Range
                          ?? throw new NotSupportedException("Cannot resolve range type id"));

    /// <summary>Returns the id of the subtype of a range type.</summary>
    /// <param name="rangeTypeId">The range type id.</param>
    /// <returns>The subtype id, in canonical form.</returns>
    /// <exception cref="NotSupportedException">The type is not a known range type.</exception>
    public PgTypeId GetRangeSubtypeTypeId(PgTypeId rangeTypeId) =>
        ToCanonicalTypeId((DatabaseInfo.GetPostgresType(rangeTypeId) as PostgresRangeType)?.Subtype
                          ?? throw new NotSupportedException("Cannot resolve range subtype type id"));

    /// <summary>Returns the id of the multirange type for a range type.</summary>
    /// <param name="rangeTypeId">The range type id.</param>
    /// <returns>The multirange type id, in canonical form.</returns>
    /// <exception cref="NotSupportedException">The range type has no multirange type.</exception>
    public PgTypeId GetMultirangeTypeId(PgTypeId rangeTypeId) =>
        ToCanonicalTypeId((DatabaseInfo.GetPostgresType(rangeTypeId) as PostgresRangeType)?.Multirange
                          ?? throw new NotSupportedException("Cannot resolve multirange type id"));

    /// <summary>Returns the id of the range type that makes up a multirange type.</summary>
    /// <param name="multirangeTypeId">The multirange type id.</param>
    /// <returns>The range type id, in canonical form.</returns>
    /// <exception cref="NotSupportedException">The type is not a known multirange type.</exception>
    public PgTypeId GetMultirangeElementTypeId(PgTypeId multirangeTypeId) =>
        ToCanonicalTypeId((DatabaseInfo.GetPostgresType(multirangeTypeId) as PostgresMultirangeType)?.Subrange
                          ?? throw new NotSupportedException("Cannot resolve multirange element type id"));

    /// <summary>Looks up the fully qualified data type name of a type in the database's type catalog.</summary>
    /// <param name="pgTypeId">The type id, in either form.</param>
    /// <param name="dataTypeName">Receives the data type name if found.</param>
    /// <returns><see langword="true"/> if the type exists in the catalog.</returns>
    public bool TryGetDataTypeName(PgTypeId pgTypeId, out DataTypeName dataTypeName)
    {
        if (DatabaseInfo.FindPostgresType(pgTypeId) is { } pgType)
        {
            dataTypeName = pgType.DataTypeName;
            return true;
        }

        dataTypeName = default;
        return false;
    }

    /// <summary>Returns the fully qualified data type name of a type from the database's type catalog.</summary>
    /// <param name="pgTypeId">The type id, in either form.</param>
    /// <returns>The data type name.</returns>
    /// <exception cref="ArgumentException">The type is not in the catalog.</exception>
    public DataTypeName GetDataTypeName(PgTypeId pgTypeId)
        => !TryGetDataTypeName(pgTypeId, out var name)
        ? throw new ArgumentException("Unknown type id", nameof(pgTypeId))
        : name;
}
