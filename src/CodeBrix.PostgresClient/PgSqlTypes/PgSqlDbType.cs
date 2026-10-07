using System;
using System.Data;
using CodeBrix.PostgresClient;
using CodeBrix.PostgresClient.Internal.Postgres;
using static CodeBrix.PostgresClient.Util.Statics;

// ReSharper disable once CheckNamespace
namespace CodeBrix.PostgresClient.PgSqlTypes; //was previously: NpgsqlTypes;

/// <summary>
/// Represents a PostgreSQL data type that can be written or read to the database.
/// Used in places such as <see cref="PgSqlParameter.PgSqlDbType"/> to unambiguously specify
/// how to encode or decode values.
/// </summary>
/// <remarks>
/// See https://www.postgresql.org/docs/current/static/datatype.html.
/// </remarks>
// Source for PG OIDs: <see href="https://github.com/postgres/postgres/blob/master/src/include/catalog/pg_type.dat" />
public enum PgSqlDbType
{
    // Note that it's important to never change the numeric values of this enum, since user applications
    // compile them in.

    #region Numeric Types

    /// <summary>
    /// Corresponds to the PostgreSQL 8-byte "bigint" type.
    /// </summary>
    /// <remarks>See https://www.postgresql.org/docs/current/static/datatype-numeric.html</remarks>
    Bigint = 1,

    /// <summary>
    /// Corresponds to the PostgreSQL 8-byte floating-point "double" type.
    /// </summary>
    /// <remarks>See https://www.postgresql.org/docs/current/static/datatype-numeric.html</remarks>
    Double = 8,

    /// <summary>
    /// Corresponds to the PostgreSQL 4-byte "integer" type.
    /// </summary>
    /// <remarks>See https://www.postgresql.org/docs/current/static/datatype-numeric.html</remarks>
    Integer = 9,

    /// <summary>
    /// Corresponds to the PostgreSQL arbitrary-precision "numeric" type.
    /// </summary>
    /// <remarks>See https://www.postgresql.org/docs/current/static/datatype-numeric.html</remarks>
    Numeric = 13,

    /// <summary>
    /// Corresponds to the PostgreSQL floating-point "real" type.
    /// </summary>
    /// <remarks>See https://www.postgresql.org/docs/current/static/datatype-numeric.html</remarks>
    Real = 17,

    /// <summary>
    /// Corresponds to the PostgreSQL 2-byte "smallint" type.
    /// </summary>
    /// <remarks>See https://www.postgresql.org/docs/current/static/datatype-numeric.html</remarks>
    Smallint = 18,

    /// <summary>
    /// Corresponds to the PostgreSQL "money" type.
    /// </summary>
    /// <remarks>See https://www.postgresql.org/docs/current/static/datatype-money.html</remarks>
    Money = 12,

    #endregion

    #region Boolean Type

    /// <summary>
    /// Corresponds to the PostgreSQL "boolean" type.
    /// </summary>
    /// <remarks>See https://www.postgresql.org/docs/current/static/datatype-boolean.html</remarks>
    Boolean = 2,

    #endregion

    #region Geometric types

    /// <summary>
    /// Corresponds to the PostgreSQL geometric "box" type.
    /// </summary>
    /// <remarks>See https://www.postgresql.org/docs/current/static/datatype-geometric.html</remarks>
    Box = 3,

    /// <summary>
    /// Corresponds to the PostgreSQL geometric "circle" type.
    /// </summary>
    /// <remarks>See https://www.postgresql.org/docs/current/static/datatype-geometric.html</remarks>
    Circle = 5,

    /// <summary>
    /// Corresponds to the PostgreSQL geometric "line" type.
    /// </summary>
    /// <remarks>See https://www.postgresql.org/docs/current/static/datatype-geometric.html</remarks>
    Line = 10,

    /// <summary>
    /// Corresponds to the PostgreSQL geometric "lseg" type.
    /// </summary>
    /// <remarks>See https://www.postgresql.org/docs/current/static/datatype-geometric.html</remarks>
    LSeg = 11,

    /// <summary>
    /// Corresponds to the PostgreSQL geometric "path" type.
    /// </summary>
    /// <remarks>See https://www.postgresql.org/docs/current/static/datatype-geometric.html</remarks>
    Path = 14,

    /// <summary>
    /// Corresponds to the PostgreSQL geometric "point" type.
    /// </summary>
    /// <remarks>See https://www.postgresql.org/docs/current/static/datatype-geometric.html</remarks>
    Point = 15,

    /// <summary>
    /// Corresponds to the PostgreSQL geometric "polygon" type.
    /// </summary>
    /// <remarks>See https://www.postgresql.org/docs/current/static/datatype-geometric.html</remarks>
    Polygon = 16,

    /// <summary>
    /// Corresponds to the PostgreSQL "cube" type, a geometric type representing multi-dimensional cubes.
    /// </summary>
    /// <remarks>See https://www.postgresql.org/docs/current/cube.html</remarks>
    Cube = 63, // Extension type

    #endregion

    #region Character Types

    /// <summary>
    /// Corresponds to the PostgreSQL "char(n)" type.
    /// </summary>
    /// <remarks>See https://www.postgresql.org/docs/current/static/datatype-character.html</remarks>
    Char = 6,

    /// <summary>
    /// Corresponds to the PostgreSQL "text" type.
    /// </summary>
    /// <remarks>See https://www.postgresql.org/docs/current/static/datatype-character.html</remarks>
    Text = 19,

    /// <summary>
    /// Corresponds to the PostgreSQL "varchar" type.
    /// </summary>
    /// <remarks>See https://www.postgresql.org/docs/current/static/datatype-character.html</remarks>
    Varchar = 22,

    /// <summary>
    /// Corresponds to the PostgreSQL internal "name" type.
    /// </summary>
    /// <remarks>See https://www.postgresql.org/docs/current/static/datatype-character.html</remarks>
    Name = 32,

    /// <summary>
    /// Corresponds to the PostgreSQL "citext" type for the citext module.
    /// </summary>
    /// <remarks>See https://www.postgresql.org/docs/current/static/citext.html</remarks>
    Citext = 51,   // Extension type

    /// <summary>
    /// Corresponds to the PostgreSQL "char" type.
    /// </summary>
    /// <remarks>
    /// This is an internal field and should normally not be used for regular applications.
    ///
    /// See https://www.postgresql.org/docs/current/static/datatype-text.html
    /// </remarks>
    InternalChar = 38,

    #endregion

    #region Binary Data Types

    /// <summary>
    /// Corresponds to the PostgreSQL "bytea" type, holding a raw byte string.
    /// </summary>
    /// <remarks>See https://www.postgresql.org/docs/current/static/datatype-binary.html</remarks>
    Bytea = 4,

    #endregion

    #region Date/Time Types

    /// <summary>
    /// Corresponds to the PostgreSQL "date" type.
    /// </summary>
    /// <remarks>See https://www.postgresql.org/docs/current/static/datatype-datetime.html</remarks>
    Date = 7,

    /// <summary>
    /// Corresponds to the PostgreSQL "time" type.
    /// </summary>
    /// <remarks>See https://www.postgresql.org/docs/current/static/datatype-datetime.html</remarks>
    Time = 20,

    /// <summary>
    /// Corresponds to the PostgreSQL "timestamp" type.
    /// </summary>
    /// <remarks>See https://www.postgresql.org/docs/current/static/datatype-datetime.html</remarks>
    Timestamp = 21,

    /// <summary>
    /// Corresponds to the PostgreSQL "timestamp with time zone" type.
    /// </summary>
    /// <remarks>See https://www.postgresql.org/docs/current/static/datatype-datetime.html</remarks>
    TimestampTz = 26,

    /// <summary>
    /// Corresponds to the PostgreSQL "interval" type.
    /// </summary>
    /// <remarks>See https://www.postgresql.org/docs/current/static/datatype-datetime.html</remarks>
    Interval = 30,

    /// <summary>
    /// Corresponds to the PostgreSQL "time with time zone" type.
    /// </summary>
    /// <remarks>See https://www.postgresql.org/docs/current/static/datatype-datetime.html</remarks>
    TimeTz = 31,

    /// <summary>
    /// Corresponds to the obsolete PostgreSQL "abstime" type.
    /// </summary>
    /// <remarks>See https://www.postgresql.org/docs/current/static/datatype-datetime.html</remarks>
    [Obsolete("The PostgreSQL abstime time is obsolete.")]
    Abstime = 33,

    #endregion

    #region Network Address Types

    /// <summary>
    /// Corresponds to the PostgreSQL "inet" type.
    /// </summary>
    /// <remarks>See https://www.postgresql.org/docs/current/static/datatype-net-types.html</remarks>
    Inet = 24,

    /// <summary>
    /// Corresponds to the PostgreSQL "cidr" type, a field storing an IPv4 or IPv6 network.
    /// </summary>
    /// <remarks>See https://www.postgresql.org/docs/current/static/datatype-net-types.html</remarks>
    Cidr = 44,

    /// <summary>
    /// Corresponds to the PostgreSQL "macaddr" type, a field storing a 6-byte physical address.
    /// </summary>
    /// <remarks>See https://www.postgresql.org/docs/current/static/datatype-net-types.html</remarks>
    MacAddr = 34,

    /// <summary>
    /// Corresponds to the PostgreSQL "macaddr8" type, a field storing a 6-byte or 8-byte physical address.
    /// </summary>
    /// <remarks>See https://www.postgresql.org/docs/current/static/datatype-net-types.html</remarks>
    MacAddr8 = 54,

    #endregion

    #region Bit String Types

    /// <summary>
    /// Corresponds to the PostgreSQL "bit" type.
    /// </summary>
    /// <remarks>See https://www.postgresql.org/docs/current/static/datatype-bit.html</remarks>
    Bit = 25,

    /// <summary>
    /// Corresponds to the PostgreSQL "varbit" type, a field storing a variable-length string of bits.
    /// </summary>
    /// <remarks>See https://www.postgresql.org/docs/current/static/datatype-boolean.html</remarks>
    Varbit = 39,

    #endregion

    #region Text Search Types

    /// <summary>
    /// Corresponds to the PostgreSQL "tsvector" type.
    /// </summary>
    /// <remarks>See https://www.postgresql.org/docs/current/static/datatype-textsearch.html</remarks>
    TsVector = 45,

    /// <summary>
    /// Corresponds to the PostgreSQL "tsquery" type.
    /// </summary>
    /// <remarks>See https://www.postgresql.org/docs/current/static/datatype-textsearch.html</remarks>
    TsQuery = 46,

    /// <summary>
    /// Corresponds to the PostgreSQL "regconfig" type.
    /// </summary>
    /// <remarks>See https://www.postgresql.org/docs/current/static/datatype-textsearch.html</remarks>
    Regconfig = 56,

    #endregion

    #region UUID Type

    /// <summary>
    /// Corresponds to the PostgreSQL "uuid" type.
    /// </summary>
    /// <remarks>See https://www.postgresql.org/docs/current/static/datatype-uuid.html</remarks>
    Uuid = 27,

    #endregion

    #region XML Type

    /// <summary>
    /// Corresponds to the PostgreSQL "xml" type.
    /// </summary>
    /// <remarks>See https://www.postgresql.org/docs/current/static/datatype-xml.html</remarks>
    Xml = 28,

    #endregion

    #region JSON Types

    /// <summary>
    /// Corresponds to the PostgreSQL "json" type, a field storing JSON in text format.
    /// </summary>
    /// <remarks>See https://www.postgresql.org/docs/current/static/datatype-json.html</remarks>
    /// <seealso cref="Jsonb"/>
    Json = 35,

    /// <summary>
    /// Corresponds to the PostgreSQL "jsonb" type, a field storing JSON in an optimized binary.
    /// format.
    /// </summary>
    /// <remarks>
    /// Supported since PostgreSQL 9.4.
    /// See https://www.postgresql.org/docs/current/static/datatype-json.html
    /// </remarks>
    Jsonb = 36,

    /// <summary>
    /// Corresponds to the PostgreSQL "jsonpath" type, a field storing JSON path in text format.
    /// format.
    /// </summary>
    /// <remarks>
    /// Supported since PostgreSQL 12.
    /// See https://www.postgresql.org/docs/current/datatype-json.html#DATATYPE-JSONPATH
    /// </remarks>
    JsonPath = 57,

    #endregion

    #region HSTORE Type

    /// <summary>
    /// Corresponds to the PostgreSQL "hstore" type, a dictionary of string key-value pairs.
    /// </summary>
    /// <remarks>See https://www.postgresql.org/docs/current/static/hstore.html</remarks>
    Hstore = 37, // Extension type

    #endregion

    #region Internal Types

    /// <summary>
    /// Corresponds to the PostgreSQL "refcursor" type.
    /// </summary>
    Refcursor = 23,

    /// <summary>
    /// Corresponds to the PostgreSQL internal "oidvector" type.
    /// </summary>
    /// <remarks>See https://www.postgresql.org/docs/current/static/datatype-oid.html</remarks>
    Oidvector = 29,

    /// <summary>
    /// Corresponds to the PostgreSQL internal "int2vector" type.
    /// </summary>
    Int2Vector = 52,

    /// <summary>
    /// Corresponds to the PostgreSQL "oid" type.
    /// </summary>
    /// <remarks>See https://www.postgresql.org/docs/current/static/datatype-oid.html</remarks>
    Oid = 41,

    /// <summary>
    /// Corresponds to the PostgreSQL "xid" type, an internal transaction identifier.
    /// </summary>
    /// <remarks>See https://www.postgresql.org/docs/current/static/datatype-oid.html</remarks>
    Xid = 42,

    /// <summary>
    /// Corresponds to the PostgreSQL "xid8" type, an internal transaction identifier.
    /// </summary>
    /// <remarks>See https://www.postgresql.org/docs/current/static/datatype-oid.html</remarks>
    Xid8 = 64,

    /// <summary>
    /// Corresponds to the PostgreSQL "cid" type, an internal command identifier.
    /// </summary>
    /// <remarks>See https://www.postgresql.org/docs/current/static/datatype-oid.html</remarks>
    Cid = 43,

    /// <summary>
    /// Corresponds to the PostgreSQL "regtype" type, a numeric (OID) ID of a type in the pg_type table.
    /// </summary>
    Regtype = 49,

    /// <summary>
    /// Corresponds to the PostgreSQL "tid" type, a tuple id identifying the physical location of a row within its table.
    /// </summary>
    Tid = 53,

    /// <summary>
    /// Corresponds to the PostgreSQL "pg_lsn" type, which can be used to store LSN (Log Sequence Number) data which
    /// is a pointer to a location in the WAL.
    /// </summary>
    /// <remarks>
    /// See: https://www.postgresql.org/docs/current/datatype-pg-lsn.html and
    /// https://git.postgresql.org/gitweb/?p=postgresql.git;a=commit;h=7d03a83f4d0736ba869fa6f93973f7623a27038a
    /// </remarks>
    PgLsn = 59,

    #endregion

    #region Special

    /// <summary>
    /// A special value that can be used to send parameter values to the database without
    /// specifying their type, allowing the database to cast them to another value based on context.
    /// The value will be converted to a string and send as text.
    /// </summary>
    /// <remarks>
    /// This value shouldn't ordinarily be used, and makes sense only when sending a data type
    /// unsupported by CodeBrix.PostgresClient.
    /// </remarks>
    Unknown = 40,

    #endregion

    #region PostGIS

    /// <summary>
    /// The geometry type for PostgreSQL spatial extension PostGIS.
    /// </summary>
    Geometry = 50,  // Extension type

    /// <summary>
    /// The geography (geodetic) type for PostgreSQL spatial extension PostGIS.
    /// </summary>
    Geography = 55, // Extension type

    #endregion

    #region Label tree types

    /// <summary>
    /// The PostgreSQL ltree type, each value is a label path "a.label.tree.value", forming a tree in a set.
    /// </summary>
    /// <remarks>See https://www.postgresql.org/docs/current/static/ltree.html</remarks>
    LTree = 60, // Extension type

    /// <summary>
    /// The PostgreSQL lquery type for PostgreSQL extension ltree
    /// </summary>
    /// <remarks>See https://www.postgresql.org/docs/current/static/ltree.html</remarks>
    LQuery = 61, // Extension type

    /// <summary>
    /// The PostgreSQL ltxtquery type for PostgreSQL extension ltree
    /// </summary>
    /// <remarks>See https://www.postgresql.org/docs/current/static/ltree.html</remarks>
    LTxtQuery = 62, // Extension type

    #endregion

    #region Range types

    /// <summary>
    /// Corresponds to the PostgreSQL "int4range" type.
    /// </summary>
    IntegerRange = Range | Integer,

    /// <summary>
    /// Corresponds to the PostgreSQL "int8range" type.
    /// </summary>
    BigIntRange = Range | Bigint,

    /// <summary>
    /// Corresponds to the PostgreSQL "numrange" type.
    /// </summary>
    NumericRange = Range | Numeric,

    /// <summary>
    /// Corresponds to the PostgreSQL "tsrange" type.
    /// </summary>
    TimestampRange = Range | Timestamp,

    /// <summary>
    /// Corresponds to the PostgreSQL "tstzrange" type.
    /// </summary>
    TimestampTzRange = Range | TimestampTz,

    /// <summary>
    /// Corresponds to the PostgreSQL "daterange" type.
    /// </summary>
    DateRange = Range | Date,

    #endregion Range types

    #region Multirange types

    /// <summary>
    /// Corresponds to the PostgreSQL "int4multirange" type.
    /// </summary>
    IntegerMultirange = Multirange | Integer,

    /// <summary>
    /// Corresponds to the PostgreSQL "int8multirange" type.
    /// </summary>
    BigIntMultirange = Multirange | Bigint,

    /// <summary>
    /// Corresponds to the PostgreSQL "nummultirange" type.
    /// </summary>
    NumericMultirange = Multirange | Numeric,

    /// <summary>
    /// Corresponds to the PostgreSQL "tsmultirange" type.
    /// </summary>
    TimestampMultirange = Multirange | Timestamp,

    /// <summary>
    /// Corresponds to the PostgreSQL "tstzmultirange" type.
    /// </summary>
    TimestampTzMultirange = Multirange | TimestampTz,

    /// <summary>
    /// Corresponds to the PostgreSQL "datemultirange" type.
    /// </summary>
    DateMultirange = Multirange | Date,

    #endregion Multirange types

    #region Composables

    /// <summary>
    /// Corresponds to the PostgreSQL "array" type, a variable-length multidimensional array of
    /// another type. This value must be combined with another value from <see cref="PgSqlDbType"/>
    /// via a bit OR (e.g. PgSqlDbType.Array | PgSqlDbType.Integer)
    /// </summary>
    /// <remarks>See https://www.postgresql.org/docs/current/static/arrays.html</remarks>
    Array = int.MinValue,

    /// <summary>
    /// Corresponds to the PostgreSQL "range" type, continuous range of values of specific type.
    /// This value must be combined with another value from <see cref="PgSqlDbType"/>
    /// via a bit OR (e.g. PgSqlDbType.Range | PgSqlDbType.Integer)
    /// </summary>
    /// <remarks>
    /// Supported since PostgreSQL 9.2.
    /// See https://www.postgresql.org/docs/current/static/rangetypes.html
    /// </remarks>
    Range = 0x40000000,

    /// <summary>
    /// Corresponds to the PostgreSQL "multirange" type, continuous range of values of specific type.
    /// This value must be combined with another value from <see cref="PgSqlDbType"/>
    /// via a bit OR (e.g. PgSqlDbType.Multirange | PgSqlDbType.Integer)
    /// </summary>
    /// <remarks>
    /// Supported since PostgreSQL 14.
    /// See https://www.postgresql.org/docs/current/static/rangetypes.html
    /// </remarks>
    Multirange = 0x20000000,

    #endregion
}

static class PgSqlDbTypeExtensions
{
    internal static PgSqlDbType? ToPgSqlDbType(this DbType dbType)
        => dbType switch
        {
            DbType.AnsiString => PgSqlDbType.Text,
            DbType.Binary => PgSqlDbType.Bytea,
            DbType.Byte => PgSqlDbType.Smallint,
            DbType.Boolean => PgSqlDbType.Boolean,
            DbType.Currency => PgSqlDbType.Money,
            DbType.Date => PgSqlDbType.Date,
            DbType.DateTime => LegacyTimestampBehavior ? PgSqlDbType.Timestamp : PgSqlDbType.TimestampTz,
            DbType.Decimal => PgSqlDbType.Numeric,
            DbType.VarNumeric => PgSqlDbType.Numeric,
            DbType.Double => PgSqlDbType.Double,
            DbType.Guid => PgSqlDbType.Uuid,
            DbType.Int16 => PgSqlDbType.Smallint,
            DbType.Int32 => PgSqlDbType.Integer,
            DbType.Int64 => PgSqlDbType.Bigint,
            DbType.Single => PgSqlDbType.Real,
            DbType.String => PgSqlDbType.Text,
            DbType.Time => PgSqlDbType.Time,
            DbType.AnsiStringFixedLength => PgSqlDbType.Text,
            DbType.StringFixedLength => PgSqlDbType.Text,
            DbType.Xml => PgSqlDbType.Xml,
            DbType.DateTime2 => PgSqlDbType.Timestamp,
            DbType.DateTimeOffset => PgSqlDbType.TimestampTz,

            DbType.Object => null,
            DbType.SByte => null,
            DbType.UInt16 => null,
            DbType.UInt32 => null,
            DbType.UInt64 => null,

            _ => throw new ArgumentOutOfRangeException(nameof(dbType), dbType, null)
        };

    public static DbType ToDbType(this PgSqlDbType pgSqlDbType)
        => pgSqlDbType switch
        {
            // Numeric types
            PgSqlDbType.Smallint => DbType.Int16,
            PgSqlDbType.Integer => DbType.Int32,
            PgSqlDbType.Bigint => DbType.Int64,
            PgSqlDbType.Real => DbType.Single,
            PgSqlDbType.Double => DbType.Double,
            PgSqlDbType.Numeric => DbType.Decimal,
            PgSqlDbType.Money => DbType.Currency,

            // Text types
            PgSqlDbType.Text => DbType.String,
            PgSqlDbType.Xml => DbType.Xml,
            PgSqlDbType.Varchar => DbType.String,
            PgSqlDbType.Char => DbType.String,
            PgSqlDbType.Name => DbType.String,
            PgSqlDbType.Citext => DbType.String,
            PgSqlDbType.Refcursor => DbType.Object,
            PgSqlDbType.Jsonb => DbType.Object,
            PgSqlDbType.Json => DbType.Object,
            PgSqlDbType.JsonPath => DbType.Object,

            // Date/time types
            PgSqlDbType.Timestamp => LegacyTimestampBehavior ? DbType.DateTime : DbType.DateTime2,
            PgSqlDbType.TimestampTz => LegacyTimestampBehavior ? DbType.DateTimeOffset : DbType.DateTime,
            PgSqlDbType.Date => DbType.Date,
            PgSqlDbType.Time => DbType.Time,

            // Misc data types
            PgSqlDbType.Bytea => DbType.Binary,
            PgSqlDbType.Boolean => DbType.Boolean,
            PgSqlDbType.Uuid => DbType.Guid,

            PgSqlDbType.Unknown => DbType.Object,

            _ => DbType.Object
        };

    /// Can return null when a custom range type is used.
    internal static string ToUnqualifiedDataTypeName(this PgSqlDbType pgSqlDbType)
        => pgSqlDbType switch
        {
            // Numeric types
            PgSqlDbType.Smallint => "int2",
            PgSqlDbType.Integer  => "int4",
            PgSqlDbType.Bigint   => "int8",
            PgSqlDbType.Real     => "float4",
            PgSqlDbType.Double   => "float8",
            PgSqlDbType.Numeric  => "numeric",
            PgSqlDbType.Money    => "money",

            // Text types
            PgSqlDbType.Text      => "text",
            PgSqlDbType.Xml       => "xml",
            PgSqlDbType.Varchar   => "varchar",
            PgSqlDbType.Char      => "bpchar",
            PgSqlDbType.Name      => "name",
            PgSqlDbType.Refcursor => "refcursor",
            PgSqlDbType.Jsonb     => "jsonb",
            PgSqlDbType.Json      => "json",
            PgSqlDbType.JsonPath  => "jsonpath",

            // Date/time types
            PgSqlDbType.Timestamp   => "timestamp",
            PgSqlDbType.TimestampTz => "timestamptz",
            PgSqlDbType.Date        => "date",
            PgSqlDbType.Time        => "time",
            PgSqlDbType.TimeTz      => "timetz",
            PgSqlDbType.Interval    => "interval",

            // Network types
            PgSqlDbType.Cidr     => "cidr",
            PgSqlDbType.Inet     => "inet",
            PgSqlDbType.MacAddr  => "macaddr",
            PgSqlDbType.MacAddr8 => "macaddr8",

            // Full-text search types
            PgSqlDbType.TsQuery   => "tsquery",
            PgSqlDbType.TsVector  => "tsvector",

            // Geometry types
            PgSqlDbType.Box     => "box",
            PgSqlDbType.Circle  => "circle",
            PgSqlDbType.Line    => "line",
            PgSqlDbType.LSeg    => "lseg",
            PgSqlDbType.Path    => "path",
            PgSqlDbType.Point   => "point",
            PgSqlDbType.Polygon => "polygon",

            // UInt types
            PgSqlDbType.Oid       => "oid",
            PgSqlDbType.Xid       => "xid",
            PgSqlDbType.Xid8      => "xid8",
            PgSqlDbType.Cid       => "cid",
            PgSqlDbType.Regtype   => "regtype",
            PgSqlDbType.Regconfig => "regconfig",

            // Misc types
            PgSqlDbType.Boolean => "bool",
            PgSqlDbType.Bytea   => "bytea",
            PgSqlDbType.Uuid    => "uuid",
            PgSqlDbType.Varbit  => "varbit",
            PgSqlDbType.Bit     => "bit",

            // Built-in range types
            PgSqlDbType.IntegerRange     => "int4range",
            PgSqlDbType.BigIntRange      => "int8range",
            PgSqlDbType.NumericRange     => "numrange",
            PgSqlDbType.TimestampRange   => "tsrange",
            PgSqlDbType.TimestampTzRange => "tstzrange",
            PgSqlDbType.DateRange        => "daterange",

            // Built-in multirange types
            PgSqlDbType.IntegerMultirange     => "int4multirange",
            PgSqlDbType.BigIntMultirange      => "int8multirange",
            PgSqlDbType.NumericMultirange     => "nummultirange",
            PgSqlDbType.TimestampMultirange   => "tsmultirange",
            PgSqlDbType.TimestampTzMultirange => "tstzmultirange",
            PgSqlDbType.DateMultirange        => "datemultirange",

            // Internal types
            PgSqlDbType.Int2Vector   => "int2vector",
            PgSqlDbType.Oidvector    => "oidvector",
            PgSqlDbType.PgLsn        => "pg_lsn",
            PgSqlDbType.Tid          => "tid",
            PgSqlDbType.InternalChar => "char",

            // Plugin types
            PgSqlDbType.Citext    => "citext",
            PgSqlDbType.Cube      => "cube",
            PgSqlDbType.LQuery    => "lquery",
            PgSqlDbType.LTree     => "ltree",
            PgSqlDbType.LTxtQuery => "ltxtquery",
            PgSqlDbType.Hstore    => "hstore",
            PgSqlDbType.Geometry  => "geometry",
            PgSqlDbType.Geography => "geography",

            PgSqlDbType.Unknown => "unknown",

            // Unknown cannot be composed
            _ when pgSqlDbType.HasFlag(PgSqlDbType.Array) && (pgSqlDbType & ~PgSqlDbType.Array) == PgSqlDbType.Unknown
                => "unknown",
            _ when pgSqlDbType.HasFlag(PgSqlDbType.Range) && (pgSqlDbType & ~PgSqlDbType.Range) == PgSqlDbType.Unknown
                => "unknown",
            _ when pgSqlDbType.HasFlag(PgSqlDbType.Multirange) && (pgSqlDbType & ~PgSqlDbType.Multirange) == PgSqlDbType.Unknown
                => "unknown",

            _ => pgSqlDbType.HasFlag(PgSqlDbType.Array)
                ? ToUnqualifiedDataTypeName(pgSqlDbType & ~PgSqlDbType.Array) is { } name ? "_" + name : null
                : null // e.g. ranges
        };

    internal static string ToUnqualifiedDataTypeNameOrThrow(this PgSqlDbType pgSqlDbType)
        => pgSqlDbType.ToUnqualifiedDataTypeName() ?? throw new ArgumentOutOfRangeException(nameof(pgSqlDbType), pgSqlDbType, "Cannot convert PgSqlDbType to DataTypeName");

    /// Can return null when a plugin type or custom range type is used.
    internal static DataTypeName? ToDataTypeName(this PgSqlDbType pgSqlDbType)
        => pgSqlDbType switch
        {
            // Numeric types
            PgSqlDbType.Smallint => DataTypeNames.Int2,
            PgSqlDbType.Integer => DataTypeNames.Int4,
            PgSqlDbType.Bigint => DataTypeNames.Int8,
            PgSqlDbType.Real => DataTypeNames.Float4,
            PgSqlDbType.Double => DataTypeNames.Float8,
            PgSqlDbType.Numeric => DataTypeNames.Numeric,
            PgSqlDbType.Money => DataTypeNames.Money,

            // Text types
            PgSqlDbType.Text => DataTypeNames.Text,
            PgSqlDbType.Xml => DataTypeNames.Xml,
            PgSqlDbType.Varchar => DataTypeNames.Varchar,
            PgSqlDbType.Char => DataTypeNames.Bpchar,
            PgSqlDbType.Name => DataTypeNames.Name,
            PgSqlDbType.Refcursor => DataTypeNames.RefCursor,
            PgSqlDbType.Jsonb => DataTypeNames.Jsonb,
            PgSqlDbType.Json => DataTypeNames.Json,
            PgSqlDbType.JsonPath => DataTypeNames.Jsonpath,

            // Date/time types
            PgSqlDbType.Timestamp => DataTypeNames.Timestamp,
            PgSqlDbType.TimestampTz => DataTypeNames.TimestampTz,
            PgSqlDbType.Date => DataTypeNames.Date,
            PgSqlDbType.Time => DataTypeNames.Time,
            PgSqlDbType.TimeTz => DataTypeNames.TimeTz,
            PgSqlDbType.Interval => DataTypeNames.Interval,

            // Network types
            PgSqlDbType.Cidr => DataTypeNames.Cidr,
            PgSqlDbType.Inet => DataTypeNames.Inet,
            PgSqlDbType.MacAddr => DataTypeNames.MacAddr,
            PgSqlDbType.MacAddr8 => DataTypeNames.MacAddr8,

            // Full-text search types
            PgSqlDbType.TsQuery => DataTypeNames.TsQuery,
            PgSqlDbType.TsVector => DataTypeNames.TsVector,

            // Geometry types
            PgSqlDbType.Box => DataTypeNames.Box,
            PgSqlDbType.Circle => DataTypeNames.Circle,
            PgSqlDbType.Line => DataTypeNames.Line,
            PgSqlDbType.LSeg => DataTypeNames.LSeg,
            PgSqlDbType.Path => DataTypeNames.Path,
            PgSqlDbType.Point => DataTypeNames.Point,
            PgSqlDbType.Polygon => DataTypeNames.Polygon,

            // UInt types
            PgSqlDbType.Oid => DataTypeNames.Oid,
            PgSqlDbType.Xid => DataTypeNames.Xid,
            PgSqlDbType.Xid8 => DataTypeNames.Xid8,
            PgSqlDbType.Cid => DataTypeNames.Cid,
            PgSqlDbType.Regtype => DataTypeNames.RegType,
            PgSqlDbType.Regconfig => DataTypeNames.RegConfig,

            // Misc types
            PgSqlDbType.Boolean => DataTypeNames.Bool,
            PgSqlDbType.Bytea => DataTypeNames.Bytea,
            PgSqlDbType.Uuid => DataTypeNames.Uuid,
            PgSqlDbType.Varbit => DataTypeNames.Varbit,
            PgSqlDbType.Bit => DataTypeNames.Bit,

            // Built-in range types
            PgSqlDbType.IntegerRange => DataTypeNames.Int4Range,
            PgSqlDbType.BigIntRange => DataTypeNames.Int8Range,
            PgSqlDbType.NumericRange => DataTypeNames.NumRange,
            PgSqlDbType.TimestampRange => DataTypeNames.TsRange,
            PgSqlDbType.TimestampTzRange => DataTypeNames.TsTzRange,
            PgSqlDbType.DateRange => DataTypeNames.DateRange,

            // Internal types
            PgSqlDbType.Int2Vector => DataTypeNames.Int2Vector,
            PgSqlDbType.Oidvector => DataTypeNames.OidVector,
            PgSqlDbType.PgLsn => DataTypeNames.PgLsn,
            PgSqlDbType.Tid => DataTypeNames.Tid,
            PgSqlDbType.InternalChar => DataTypeNames.Char,

            // Special types
            PgSqlDbType.Unknown => DataTypeNames.Unknown,

            // Unknown cannot be composed
            _ when pgSqlDbType.HasFlag(PgSqlDbType.Array) && (pgSqlDbType & ~PgSqlDbType.Array) == PgSqlDbType.Unknown
                => DataTypeNames.Unknown,
            _ when pgSqlDbType.HasFlag(PgSqlDbType.Range) && (pgSqlDbType & ~PgSqlDbType.Range) == PgSqlDbType.Unknown
                => DataTypeNames.Unknown,
            _ when pgSqlDbType.HasFlag(PgSqlDbType.Multirange) && (pgSqlDbType & ~PgSqlDbType.Multirange) == PgSqlDbType.Unknown
                 => DataTypeNames.Unknown,

            // If both multirange and array are set we first remove array, so array is added to the outermost datatypename.
            _ when pgSqlDbType.HasFlag(PgSqlDbType.Array)
                => ToDataTypeName(pgSqlDbType & ~PgSqlDbType.Array)?.ToArrayName(),
            _ when pgSqlDbType.HasFlag(PgSqlDbType.Multirange)
                => ToDataTypeName((pgSqlDbType | PgSqlDbType.Range) & ~PgSqlDbType.Multirange)?.ToDefaultMultirangeName(),

            // Plugin types don't have a stable fully qualified name.
            _ => null
        };

    internal static PgSqlDbType? ToPgSqlDbType(this DataTypeName dataTypeName) => ToPgSqlDbType(dataTypeName.UnqualifiedName);
    /// Should not be used with display names, first normalize it instead.
    internal static PgSqlDbType? ToPgSqlDbType(string normalizedDataTypeName)
    {
        var unqualifiedName = normalizedDataTypeName.AsSpan();
        if (unqualifiedName.IndexOf('.') is not -1 and var index)
            unqualifiedName = unqualifiedName.Slice(index + 1);

        return unqualifiedName switch
            {
                // Numeric types
                "int2" => PgSqlDbType.Smallint,
                "int4" => PgSqlDbType.Integer,
                "int8" => PgSqlDbType.Bigint,
                "float4" => PgSqlDbType.Real,
                "float8" => PgSqlDbType.Double,
                "numeric" => PgSqlDbType.Numeric,
                "money" => PgSqlDbType.Money,

                // Text types
                "text" => PgSqlDbType.Text,
                "xml" => PgSqlDbType.Xml,
                "varchar" => PgSqlDbType.Varchar,
                "bpchar" => PgSqlDbType.Char,
                "name" => PgSqlDbType.Name,
                "refcursor" => PgSqlDbType.Refcursor,
                "jsonb" => PgSqlDbType.Jsonb,
                "json" => PgSqlDbType.Json,
                "jsonpath" => PgSqlDbType.JsonPath,

                // Date/time types
                "timestamp" => PgSqlDbType.Timestamp,
                "timestamptz" => PgSqlDbType.TimestampTz,
                "date" => PgSqlDbType.Date,
                "time" => PgSqlDbType.Time,
                "timetz" => PgSqlDbType.TimeTz,
                "interval" => PgSqlDbType.Interval,

                // Network types
                "cidr" => PgSqlDbType.Cidr,
                "inet" => PgSqlDbType.Inet,
                "macaddr" => PgSqlDbType.MacAddr,
                "macaddr8" => PgSqlDbType.MacAddr8,

                // Full-text search types
                "tsquery" => PgSqlDbType.TsQuery,
                "tsvector" => PgSqlDbType.TsVector,

                // Geometry types
                "box" => PgSqlDbType.Box,
                "circle" => PgSqlDbType.Circle,
                "line" => PgSqlDbType.Line,
                "lseg" => PgSqlDbType.LSeg,
                "path" => PgSqlDbType.Path,
                "point" => PgSqlDbType.Point,
                "polygon" => PgSqlDbType.Polygon,

                // UInt types
                "oid" => PgSqlDbType.Oid,
                "xid" => PgSqlDbType.Xid,
                "xid8" => PgSqlDbType.Xid8,
                "cid" => PgSqlDbType.Cid,
                "regtype" => PgSqlDbType.Regtype,
                "regconfig" => PgSqlDbType.Regconfig,

                // Misc types
                "bool" => PgSqlDbType.Boolean,
                "bytea" => PgSqlDbType.Bytea,
                "uuid" => PgSqlDbType.Uuid,
                "varbit" => PgSqlDbType.Varbit,
                "bit" => PgSqlDbType.Bit,

                // Built-in range types
                "int4range" => PgSqlDbType.IntegerRange,
                "int8range" => PgSqlDbType.BigIntRange,
                "numrange" => PgSqlDbType.NumericRange,
                "tsrange" => PgSqlDbType.TimestampRange,
                "tstzrange" => PgSqlDbType.TimestampTzRange,
                "daterange" => PgSqlDbType.DateRange,

                // Built-in multirange types
                "int4multirange" => PgSqlDbType.IntegerMultirange,
                "int8multirange" => PgSqlDbType.BigIntMultirange,
                "nummultirange" => PgSqlDbType.NumericMultirange,
                "tsmultirange" => PgSqlDbType.TimestampMultirange,
                "tstzmultirange" => PgSqlDbType.TimestampTzMultirange,
                "datemultirange" => PgSqlDbType.DateMultirange,

                // Internal types
                "int2vector" => PgSqlDbType.Int2Vector,
                "oidvector" => PgSqlDbType.Oidvector,
                "pg_lsn" => PgSqlDbType.PgLsn,
                "tid" => PgSqlDbType.Tid,
                "char" => PgSqlDbType.InternalChar,

                // Plugin types
                "citext" => PgSqlDbType.Citext,
                "cube" => PgSqlDbType.Cube,
                "lquery" => PgSqlDbType.LQuery,
                "ltree" => PgSqlDbType.LTree,
                "ltxtquery" => PgSqlDbType.LTxtQuery,
                "hstore" => PgSqlDbType.Hstore,
                "geometry" => PgSqlDbType.Geometry,
                "geography" => PgSqlDbType.Geography,

                _ when unqualifiedName.IndexOf("unknown") != -1
                    => !unqualifiedName.StartsWith("_", StringComparison.Ordinal)
                        ? PgSqlDbType.Unknown
                        : null,
                _ when unqualifiedName.StartsWith("_", StringComparison.Ordinal)
                    => ToPgSqlDbType(unqualifiedName.Slice(1).ToString()) is { } elementPgSqlDbType
                        ? elementPgSqlDbType | PgSqlDbType.Array
                        : null,
                // e.g. custom ranges, plugin types etc.
                _ => null
            };
    }
}
