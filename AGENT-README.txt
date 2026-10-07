================================================================================
AGENT-README: CodeBrix.PostgresClient
A Guide for AI Coding Agents — CONSUMING the
CodeBrix.PostgresClient.PostgreSqlLicenseForever NuGet package
================================================================================

OVERVIEW
========
CodeBrix.PostgresClient is a fully managed ADO.NET data provider for
PostgreSQL, for .NET 10 or later. It speaks the PostgreSQL wire protocol
directly (no native client library) and provides:

  - the complete ADO.NET surface (PgSqlConnection, PgSqlCommand,
    PgSqlDataReader, PgSqlParameter, PgSqlTransaction, PgSqlBatch,
    PgSqlDataAdapter, PgSqlCommandBuilder, PgSqlFactory), sync and async;
  - PgSqlDataSource, a thread-safe connection factory that owns the pool,
    type mappings, logging, SSL and authentication setup;
  - multi-host failover/load balancing, multiplexing, prepared statements,
    batching, binary and text COPY, LISTEN/NOTIFY, logical (pgoutput,
    test_decoding) and physical replication, System.Transactions;
  - rich type mapping (arrays, ranges, multiranges, composites, enums,
    json/jsonb, hstore, ltree, cube, network, geometric, full-text, ...);
  - SCRAM/MD5 passwords, SSL/TLS with client certificates and channel
    binding, Kerberos/GSSAPI, .pgpass files;
  - tracing (ActivitySource) and metrics (System.Diagnostics.Metrics);
  - PgSqlMapper, a built-in lightweight object mapper: extension methods on
    PgSqlConnection that run SQL and map rows to objects (Query, QueryFirst,
    QuerySingle and their OrDefault forms, Execute, ExecuteScalar,
    ExecuteReader, QueryMultiple - sync and async).

PROVENANCE: the provider is ported from the Npgsql project; all Npgsql*
names are PgSql*. If you already know that library, translate your
knowledge with this table and everything else carries over:

    Npgsql (namespace)               -> CodeBrix.PostgresClient
    NpgsqlTypes (namespace)          -> CodeBrix.PostgresClient.PgSqlTypes
    Npgsql.Replication(.PgOutput...) -> CodeBrix.PostgresClient.Replication
                                          (.PgOutput, .TestDecoding, ...)
    Npgsql.Schema / .TypeMapping /   -> CodeBrix.PostgresClient.Schema /
      .NameTranslation /                .TypeMapping / .NameTranslation /
      .PostgresTypes                    .PostgresTypes
    NpgsqlConnection                 -> PgSqlConnection
    NpgsqlCommand / NpgsqlBatch      -> PgSqlCommand / PgSqlBatch
    NpgsqlDataSource(+Builder)       -> PgSqlDataSource(+Builder)
    NpgsqlSlimDataSourceBuilder      -> PgSqlSlimDataSourceBuilder
    NpgsqlMultiHostDataSource        -> PgSqlMultiHostDataSource
    NpgsqlParameter / NpgsqlParameter<T> -> PgSqlParameter / PgSqlParameter<T>
    NpgsqlDbType                     -> PgSqlDbType
    NpgsqlDataReader                 -> PgSqlDataReader
    NpgsqlTransaction                -> PgSqlTransaction
    NpgsqlException                  -> PgSqlException
    NpgsqlConnectionStringBuilder    -> PgSqlConnectionStringBuilder
    NpgsqlBinaryImporter / Exporter  -> PgSqlBinaryImporter / Exporter
    NpgsqlRange<T> / NpgsqlInterval  -> PgSqlRange<T> / PgSqlInterval
    NpgsqlPoint, NpgsqlTsVector, ... -> PgSqlPoint, PgSqlTsVector, ...
    NpgsqlLogSequenceNumber          -> PgSqlLogSequenceNumber
    ActivitySource / Meter "Npgsql"  -> "PgSql"
    logger "Npgsql.Connection" etc.  -> "PgSql.Connection" etc.
    AppContext "Npgsql.*" switches   -> "PgSql.*" switches
    EventSources "Npgsql", ".Sql"    -> "PgSql", "PgSql.Sql"

Names that never carried the old prefix are unchanged: PostgresException,
PostgresNotice, PostgresErrorCodes, PostgresType (and subclasses),
PgNameAttribute, LogicalReplicationConnection, PgOutputReplicationSlot,
InsertMessage and the other replication types, TargetSessionAttributes,
SslMode. Do NOT use any of the old names or namespaces - they do not exist
in this package.


INSTALLATION
============
PackageId:  CodeBrix.PostgresClient.PostgreSqlLicenseForever

    dotnet add package CodeBrix.PostgresClient.PostgreSqlLicenseForever

IMPORTANT: the namespace is CodeBrix.PostgresClient, WITHOUT the
".PostgreSqlLicenseForever" suffix - that suffix appears only in the NuGet
package id, to make the package's license obvious forever. There is no
package named plain "CodeBrix.PostgresClient".

NuGet dependencies (pulled in automatically):
  - Microsoft.Extensions.Logging.Abstractions

License: PostgreSQL License (SPDX: PostgreSQL)

Requirements:
  - .NET 10 or later; any OS .NET runs on (fully managed, no native assets).
  - A reachable PostgreSQL server. Nothing is installed or embedded - the
    package is a client only. Features such as logical replication need the
    matching server configuration (for example wal_level=logical and a
    publication).

XML documentation (IntelliSense) ships alongside the assembly; every public
member is documented.


KEY NAMESPACES / USINGS
=======================
    using CodeBrix.PostgresClient;      // connections, commands, data sources,
                                        //   parameters, readers, batches,
                                        //   exceptions, PgSqlMapper,
                                        //   TargetSessionAttributes, SslMode
    using CodeBrix.PostgresClient.PgSqlTypes;   // PgSqlDbType, PgSqlRange<T>,
                                        //   PgSqlInterval, geometric, network
                                        //   and full-text types, PgName
    using CodeBrix.PostgresClient.Replication;  // replication connections,
                                        //   ReplicationMessage, XLogDataMessage
    using CodeBrix.PostgresClient.Replication.PgOutput;  // pgoutput slots,
                                        //   options, ReplicationTuple/Value
    using CodeBrix.PostgresClient.Replication.PgOutput.Messages;
                                        // InsertMessage, UpdateMessage, ...
    using CodeBrix.PostgresClient.Replication.TestDecoding;  // test_decoding
    using CodeBrix.PostgresClient.NameTranslation;  // snake_case translator
    using CodeBrix.PostgresClient.Schema;         // PgSqlDbColumn
    using CodeBrix.PostgresClient.PostgresTypes;  // PostgresType & subclasses
    using CodeBrix.PostgresClient.TypeMapping;    // IPgSqlTypeMapper

The mapper methods (Query<T>, Execute, ...) are extension methods declared in
the root CodeBrix.PostgresClient namespace - `using CodeBrix.PostgresClient;`
is all they need. CodeBrix.PostgresClient.Internal and .BackendMessages hold
plumbing and extension points; ordinary application code never needs them.


================================================================================

CORE API REFERENCE
==================

PgSqlDataSource - THE RECOMMENDED ENTRY POINT (IDisposable, IAsyncDisposable)
----------------------------------------------------------------------------
A data source owns the connection pool and all configuration. Create ONE per
connection string for the application's lifetime (a singleton), and get
connections or commands from it.

    static PgSqlDataSource Create(string connectionString)
    static PgSqlDataSource Create(PgSqlConnectionStringBuilder builder)

    PgSqlConnection CreateConnection()               // not yet open
    PgSqlConnection OpenConnection()
    ValueTask<PgSqlConnection> OpenConnectionAsync(
        CancellationToken cancellationToken = default)
    PgSqlCommand CreateCommand(string commandText = null)
        // a command that runs on its own pooled connection - no connection
        //   handling needed; cannot take part in a transaction
    PgSqlBatch CreateBatch()
    string ConnectionString { get; }
    string Password { set; }        // next physical connection's password
    void Clear()                    // closes idle pooled connections
    void ReloadTypes()  /  Task ReloadTypesAsync(CancellationToken = default)
        // re-read the database's types (after CREATE TYPE / CREATE EXTENSION)

PgSqlDataSourceBuilder (sealed) - configure, then Build()
---------------------------------------------------------
    PgSqlDataSourceBuilder(string connectionString = null)
    PgSqlConnectionStringBuilder ConnectionStringBuilder { get; }
    string ConnectionString { get; }
    string Name { get; set; }       // shows up in metrics / tracing tags
    IPgSqlNameTranslator DefaultNameTranslator { get; set; }
                                    // default: snake_case translator

    Logging and tracing:
    UseLoggerFactory(ILoggerFactory loggerFactory)
    EnableParameterLogging(bool parameterLoggingEnabled = true)
    ConfigureTracing(Action<PgSqlTracingOptionsBuilder> configureAction)

    Type mapping:
    MapEnum<TEnum>(string pgName = null,
                   IPgSqlNameTranslator nameTranslator = null)
    MapComposite<T>(string pgName = null,
                    IPgSqlNameTranslator nameTranslator = null)
    (+ MapEnum(Type clrType, ...) / MapComposite(Type clrType, ...), and
     UnmapEnum / UnmapComposite, which return bool)
    ConfigureJsonOptions(JsonSerializerOptions serializerOptions)
    EnableDynamicJson(Type[] jsonbClrTypes = null, Type[] jsonClrTypes = null)
    EnableRecordsAsTuples()
    EnableUnmappedTypes()
    ConfigureTypeLoading(Action<PgSqlTypeLoadingOptionsBuilder> configureAction)

    Security / authentication (each returns the builder):
    UseUserCertificateValidationCallback, UseClientCertificate(s),
    UseClientCertificatesCallback, UseRootCertificate(s),
    UseRootCertificateCallback, UseRootCertificatesCallback,
    UseSslClientAuthenticationOptionsCallback, UseNegotiateOptionsCallback,
    UsePasswordProvider(
        Func<PgSqlConnectionStringBuilder, string> passwordProvider,
        Func<PgSqlConnectionStringBuilder, CancellationToken,
             ValueTask<string>> passwordProviderAsync)
    UsePeriodicPasswordProvider(
        Func<PgSqlConnectionStringBuilder, CancellationToken,
             ValueTask<string>> passwordProvider,
        TimeSpan successRefreshInterval, TimeSpan failureRefreshInterval)
        // rotating cloud tokens (IAM auth etc.): refreshed on a timer

    Session setup:
    UsePhysicalConnectionInitializer(Action<PgSqlConnection> init,
                                     Func<PgSqlConnection, Task> initAsync)
        // runs once per PHYSICAL connection (e.g. SET statements)

    PgSqlDataSource Build()
    PgSqlMultiHostDataSource BuildMultiHost()

Every configuration method returns the builder, so calls chain.

PgSqlSlimDataSourceBuilder is the trimming/NativeAOT-friendly variant: it
starts with only the basic types and you opt in to features (EnableArrays,
EnableRanges, EnableMultiranges, EnableRecords, EnableFullTextSearch,
EnableLTree, EnableCube, EnableExtraConversions, EnableTransportSecurity,
EnableIntegratedSecurity, EnableNetworkTypes, EnableGeometricTypes,
EnableJsonTypes, ...). Use the normal builder unless you publish trimmed.


PgSqlConnection (sealed, DbConnection)
--------------------------------------
    PgSqlConnection()
    PgSqlConnection(string connectionString)
        // a connection made this way still uses an internal, pooled data
        //   source keyed by the connection string - but type mapping and
        //   logging configuration need a PgSqlDataSourceBuilder

    void Open()  /  Task OpenAsync(CancellationToken cancellationToken)
    void Close() /  Task CloseAsync()
    void Dispose() / ValueTask DisposeAsync()   // returns it to the pool
    ConnectionState State { get; }  /  ConnectionState FullState { get; }

    PgSqlCommand CreateCommand()
    PgSqlBatch CreateBatch()
    PgSqlTransaction BeginTransaction([IsolationLevel level])
    ValueTask<PgSqlTransaction> BeginTransactionAsync([IsolationLevel level,]
        CancellationToken cancellationToken = default)
    void EnlistTransaction(System.Transactions.Transaction transaction)

    COPY (see the COPY section):
    PgSqlBinaryImporter BeginBinaryImport(string copyFromCommand)
    PgSqlBinaryExporter BeginBinaryExport(string copyToCommand)
    PgSqlCopyTextWriter BeginTextImport(string copyFromCommand)
    PgSqlCopyTextReader BeginTextExport(string copyToCommand)
    PgSqlRawCopyStream  BeginRawBinaryCopy(string copyCommand)
        (each has an ...Async(string, CancellationToken = default) form)

    Notifications (see NOTIFICATIONS):
    event NotificationEventHandler Notification   // LISTEN/NOTIFY
    event NoticeEventHandler Notice               // RAISE NOTICE etc.
    bool Wait(int timeout) / bool Wait(TimeSpan timeout) / void Wait()
    Task<bool> WaitAsync(int timeout | TimeSpan timeout,
                         CancellationToken cancellationToken = default)
    Task WaitAsync(CancellationToken cancellationToken = default)

    Server information (open connection): Version PostgreSqlVersion,
    string ServerVersion, int ProcessID, string Timezone,
    IReadOnlyDictionary<string, string> PostgresParameters, string Host,
    int Port, string Database, string UserName, int CommandTimeout

    Misc: GetSchema(...) / GetSchemaAsync(...), CloneWith(string),
    ChangeDatabase(string), UnprepareAll(), ReloadTypes() /
    ReloadTypesAsync(CancellationToken), static ClearPool(PgSqlConnection),
    static ClearAllPools(), const int DefaultPort = 5432

PgSqlCommand (DbCommand)
------------------------
    PgSqlCommand()
    PgSqlCommand(string cmdText)
    PgSqlCommand(string cmdText, PgSqlConnection connection)
    PgSqlCommand(string cmdText, PgSqlConnection connection,
                 PgSqlTransaction transaction)

    string CommandText { get; set; }
    int CommandTimeout { get; set; }        // seconds; default 30
    CommandType CommandType { get; set; }   // Text or StoredProcedure
    PgSqlConnection Connection { get; set; }
    PgSqlTransaction Transaction { get; set; }
    PgSqlParameterCollection Parameters { get; }
    PgSqlParameter CreateParameter()

    int ExecuteNonQuery()  /  Task<int> ExecuteNonQueryAsync(CancellationToken)
    object ExecuteScalar() /  Task<object> ExecuteScalarAsync(CancellationToken)
    PgSqlDataReader ExecuteReader(CommandBehavior behavior = Default)
    Task<PgSqlDataReader> ExecuteReaderAsync([CommandBehavior behavior,]
        CancellationToken cancellationToken = default)

    void Prepare()   / Task PrepareAsync(CancellationToken = default)
    void Unprepare() / Task UnprepareAsync(CancellationToken = default)
    bool IsPrepared { get; }
    void Cancel()
    bool AllResultTypesAreUnknown { get; set; }   // read every column as text
    PgSqlCommand Clone()

PARAMETER PLACEHOLDERS - two styles, never mixed in one command:
  - NAMED: "WHERE id = @id" (":id" is also accepted) with parameters that
    have a ParameterName. The SQL is parsed and rewritten to PostgreSQL's
    native $1, $2 form, and a command may contain several ';'-separated
    statements.
  - POSITIONAL: "WHERE id = $1" with parameters that have NO name, added in
    order. The SQL is sent as-is with no parsing (slightly faster); a
    positional command must hold exactly one statement - use PgSqlBatch for
    several.


PgSqlParameter / PgSqlParameter<T> / PgSqlParameterCollection
-------------------------------------------------------------
    PgSqlParameter()
    PgSqlParameter(string parameterName, object value)
    PgSqlParameter(string parameterName, PgSqlDbType parameterType)
    PgSqlParameter(string parameterName, DbType parameterType)
    (+ overloads adding int size, string sourceColumn)

    string ParameterName { get; set; }      // "" => positional
    object Value { get; set; }              // DBNull.Value => SQL NULL
                                            //   (a null Value throws)
    PgSqlDbType PgSqlDbType { get; set; }   // force the PostgreSQL type
    string DataTypeName { get; set; }       // ...or by name: "jsonb",
                                            //   "mood", "integer[]"
    DbType DbType { get; set; }
    ParameterDirection Direction { get; set; }
    byte Precision / byte Scale / int Size
    PostgresType PostgresType { get; }      // resolved after execution

    PgSqlParameter<T> : PgSqlParameter       // generic, no boxing
        T TypedValue { get; set; }
        PgSqlParameter<T>(string parameterName, T value)

Collection helpers (cmd.Parameters):
    PgSqlParameter Add(PgSqlParameter value)
    PgSqlParameter AddWithValue(string parameterName, object value)
    PgSqlParameter AddWithValue(string parameterName,
                                PgSqlDbType parameterType, object value)
    PgSqlParameter AddWithValue(object value)          // positional
    PgSqlParameter AddWithValue(PgSqlDbType parameterType, object value)
    PgSqlParameter Add(string parameterName, PgSqlDbType parameterType)
    bool TryGetValue(string parameterName, out PgSqlParameter parameter)
    PgSqlParameter this[string parameterName] / this[int index]

Arrays and ranges are combined flags: PgSqlDbType.Array | PgSqlDbType.Integer
is integer[]; PgSqlDbType.Range | PgSqlDbType.Integer is int4range (or use
PgSqlDbType.IntegerRange). An int[] / List<int> value infers integer[] with
no type needed.


PgSqlDataReader (sealed, DbDataReader)
--------------------------------------
    bool Read() / Task<bool> ReadAsync(CancellationToken)
    bool NextResult() / Task<bool> NextResultAsync(CancellationToken)
    T GetFieldValue<T>(int ordinal)
    Task<T> GetFieldValueAsync<T>(int ordinal, CancellationToken)
    object GetValue(int ordinal)       // DBNull.Value for NULL
    bool IsDBNull(int ordinal) / IsDBNullAsync(int, CancellationToken)
    GetInt16/32/64, GetString, GetDecimal, GetDouble, GetFloat, GetBoolean,
      GetDateTime, GetGuid, GetChar, GetByte, GetTimeSpan (int ordinal)
    GetStream / GetStreamAsync, GetTextReader / GetTextReaderAsync (int ...)
    PgSqlNestedDataReader GetData(int ordinal)   // records / composites
    GetOrdinal(string), GetName(int), FieldCount, HasRows, IsOnRow,
      RecordsAffected, ulong Rows, GetDataTypeName(int),
      GetDataTypeOID(int), PostgresType GetPostgresType(int),
      ReadOnlyCollection<PgSqlDbColumn> GetColumnSchema(), GetSchemaTable()
    void Close() / Task CloseAsync() / ValueTask DisposeAsync()

Prefer GetFieldValue<T> - it is the strongly typed path for every mapped
type (int[], PgSqlRange<int>, DateOnly, TimeOnly, Guid, JsonDocument,
Dictionary<string,string> for hstore, your mapped enums and composites...).
Use CommandBehavior.SequentialAccess for very large columns, and read them
with GetStream / GetTextReader.


PgSqlBatch (DbBatch) and PgSqlBatchCommand
------------------------------------------
Several statements in ONE round trip, each with its own parameters.

    PgSqlBatch(PgSqlConnection connection = null,
               PgSqlTransaction transaction = null)
    PgSqlBatchCommandCollection BatchCommands { get; }
    PgSqlConnection Connection { get; set; }
    PgSqlTransaction Transaction { get; set; }
    int Timeout { get; set; }
    bool EnableErrorBarriers { get; set; }
        // true: a failing command does not abort the ones after it
    PgSqlBatchCommand CreateBatchCommand()
    ExecuteReader / ExecuteReaderAsync / ExecuteNonQuery(Async) /
      ExecuteScalar(Async) / Prepare(Async) / Cancel()

    PgSqlBatchCommand()  /  PgSqlBatchCommand(string commandText)
    string CommandText { get; set; }
    PgSqlParameterCollection Parameters { get; }
    int RecordsAffected { get; }   ulong Rows { get; }
    StatementType StatementType { get; }   uint OID { get; }
    bool? AppendErrorBarrier { get; set; }

The reader returned by a batch walks the result sets of the commands that
produce rows (NextResult moves between them). Without error barriers, the
whole batch runs in an implicit transaction: a failure rolls all of it back.


PgSqlTransaction (sealed, DbTransaction)
----------------------------------------
    void Commit() / Task CommitAsync(CancellationToken = default)
    void Rollback() / Task RollbackAsync(CancellationToken = default)
    void Save(string name) / Task SaveAsync(string name, CancellationToken)
    void Rollback(string name) / Task RollbackAsync(string name, ...)
    void Release(string name) / Task ReleaseAsync(string name, ...)
    PgSqlConnection Connection { get; }
    IsolationLevel IsolationLevel { get; }
    bool SupportsSavepoints { get; }

Disposing an uncommitted transaction rolls it back. A command created
directly on the connection (new PgSqlCommand(sql, connection)) takes part in
the connection's active transaction automatically.


EXCEPTIONS
----------
    PgSqlException : DbException
        bool IsTransient { get; }       // network/timeout faults, plus the
                                        //   retryable server errors below
        PgSqlBatchCommand BatchCommand { get; }   // the batch command that
                                                  //   failed
    PostgresException : PgSqlException   // an error REPORTED BY THE SERVER
        string SqlState  string MessageText  string Detail  string Hint
        string Severity  string SchemaName  string TableName
        string ColumnName  string ConstraintName  string DataTypeName
        int Position  string Where  string Routine
    PgSqlOperationInProgressException    // a second command was started on
                                         //   a busy connection
    PostgresErrorCodes                   // string constants for SqlState:
        UniqueViolation ("23505"), ForeignKeyViolation ("23503"),
        SerializationFailure ("40001"), DeadlockDetected ("40P01"), ...

PostgresException.Detail is empty unless the connection string sets
"Include Error Detail=true" (it can contain row data). Pool exhaustion
surfaces as a PgSqlException whose message names 'Max Pool Size' and
'Timeout'.


PgSqlFactory
------------
PgSqlFactory.Instance is the DbProviderFactory for code written against the
provider-agnostic ADO.NET API (DbProviderFactories.RegisterFactory(
"CodeBrix.PostgresClient", PgSqlFactory.Instance)).


================================================================================

CONNECTION STRINGS
==================
Key=Value pairs separated by ';'. Keys are case-insensitive, and either the
key name below ("Maximum Pool Size") or the builder property name
("MaxPoolSize") is accepted. PgSqlConnectionStringBuilder exposes each key as
a typed property.

    Host=db1.example.com;Port=5432;Database=shop;Username=app;
    Password=secret;SSL Mode=VerifyFull;Maximum Pool Size=50

Connection:
    Host                  host name/IP, or a comma-separated list for
                          multi-host ("h1,h2:5433"); a path = Unix socket
    Port                  default 5432
    Database / Username / Password / Passfile
    Application Name      shows in pg_stat_activity
    Search Path           sets search_path for each connection
    Timezone              session time zone
    Options               raw server options ("-c statement_timeout=5s")
Security:
    SSL Mode              Disable, Allow, Prefer (default), Require,
                          VerifyCA, VerifyFull
    SSL Certificate / SSL Key / SSL Password / Root Certificate
    Channel Binding       Disable, Prefer (default), Require
    Require Auth          restrict allowed methods ("scram-sha-256")
    GSS Encryption Mode / Kerberos Service Name / Include Realm
    Include Error Detail  include PostgresException.Detail (default false)
    Persist Security Info keep Password readable in ConnectionString
Pooling:
    Pooling               default true
    Minimum Pool Size     default 0
    Maximum Pool Size     default 100
    Connection Idle Lifetime     seconds before idle connections are pruned
                                 (default 300)
    Connection Pruning Interval  default 10
    Connection Lifetime          max physical lifetime, seconds (default
                                 3600; 0 = unlimited)
Timeouts:
    Timeout               seconds to wait to open a connection (default 15)
    Command Timeout       default command timeout seconds (default 30;
                          0 = infinite)
    Cancellation Timeout  ms to wait for a cancel to land (default 2000)
    Keepalive             seconds between keepalive queries (0 = off)
    TCP Keepalive / TCP Keepalive Time / TCP Keepalive Interval
Performance:
    Max Auto Prepare      statements auto-prepared per connection (default
                          0 = off)
    Auto Prepare Min Usages      uses before auto-preparing (default 5)
    Read Buffer Size / Write Buffer Size     default 8192
    No Reset On Close     skip DISCARD ALL on return to pool
    Multiplexing          default false (see PERFORMANCE TIPS / PITFALLS)
Multi-host:
    Target Session Attributes    any, primary, standby, prefer-primary,
                                 prefer-standby, read-write, read-only
    Load Balance Hosts    round-robin among suitable hosts (default false)
    Host Recheck Seconds  cache lifetime of host state (default 10)
Other:
    Enlist                auto-enlist in TransactionScope (default true)
    Log Parameters        include parameter values in logs
    Array Nullability Mode  Never (default), Always, PerInstance
    Server Compatibility Mode  None or NoTypeLoading (for servers that cannot
                          answer the type-loading catalog queries)
    Load Table Composites  also load table row types as composites
                          (default false)

Some standard PostgreSQL environment variables fill settings the connection
string leaves out: PGUSER, PGPASSWORD, PGPASSFILE, PGAPPNAME, PGTZ,
PGOPTIONS, PGCLIENTENCODING, PGSSLCERT, PGSSLKEY, PGSSLROOTCERT,
PGTARGETSESSIONATTRS, PGSSLNEGOTIATION, PGGSSENCMODE, PGREQUIREAUTH. Host,
port and database are NOT read from the environment - put them in the
connection string. A .pgpass file (Passfile key or PGPASSFILE, else the
user's default location) is consulted when no password is given.


================================================================================

TYPE MAPPING
============
Default .NET <-> PostgreSQL mapping (no configuration needed):

    bool                      boolean
    short / int / long        smallint / integer / bigint
    float / double            real / double precision
    decimal                   numeric (also reads money)
    string / char[]           text (reads varchar, char, name, citext, xml)
    char                      "char" / text
    Guid                      uuid
    byte[] / ReadOnlyMemory<byte> / Stream   bytea
    DateTime (Kind=Utc)       timestamp with time zone (timestamptz)
    DateTime (Local/Unspec.)  timestamp without time zone
    DateTimeOffset (offset 0) timestamptz (reads back with offset 0)
    DateOnly                  date
    TimeOnly / TimeSpan       time / interval (TimeSpan writes interval)
    PgSqlInterval             interval with months/days preserved
    T[] / List<T>             PostgreSQL arrays of the element type
    PgSqlRange<T>             int4range, int8range, numrange, tsrange,
                              tstzrange, daterange
    PgSqlRange<T>[]           the matching multirange
    IPAddress / PgSqlInet / IPNetwork   inet / cidr
    PhysicalAddress           macaddr / macaddr8
    BitArray / bool           bit varying / bit(1)
    PgSqlPoint, PgSqlLSeg, PgSqlBox, PgSqlPath, PgSqlPolygon, PgSqlLine,
      PgSqlCircle             the geometric types
    PgSqlTsVector / PgSqlTsQuery   tsvector / tsquery
    Dictionary<string,string> hstore
    string                    json / jsonb / jsonpath / ltree / lquery
                              (set PgSqlDbType or DataTypeName on write)
    JsonDocument / JsonElement     json / jsonb
    PgSqlCube                 cube
    PgSqlLogSequenceNumber    pg_lsn
    uint                      oid, xid, cid, regtype
    BigInteger                numeric (read/write)

When a .NET value maps to several PostgreSQL types (a string to json, a
DateTime to date), set PgSqlParameter.PgSqlDbType or DataTypeName, or cast
in SQL ("@doc::jsonb").

PostgreSQL ENUMS: map a .NET enum on the data source builder, then use it as
a parameter value or read it with GetFieldValue<TEnum>:

    builder.MapEnum<Mood>("mood");     // labels translated by the default
                                       //   snake_case translator: Happy ->
                                       //   "happy", VeryHappy -> "very_happy"
    [PgName("so-so")] on an enum member overrides one label.

COMPOSITE TYPES: builder.MapComposite<Address>("address") maps the .NET
type's properties/fields to the composite's attributes (snake_case by
default; [PgName] overrides).

JSON: string, JsonDocument and JsonElement work out of the box. For your own
POCOs and JsonNode, call builder.EnableDynamicJson() (optionally restricted
to listed types) and set PgSqlDbType.Jsonb or Json on the parameter;
ConfigureJsonOptions(...) supplies the JsonSerializerOptions. Reading back:
reader.GetFieldValue<MyPoco>(i).

RECORDS: an anonymous ROW(...) reads as object[] by default; call
EnableRecordsAsTuples() to read it as ValueTuple/Tuple.

Types created after the data source loaded its type catalog (CREATE TYPE,
CREATE EXTENSION hstore) need dataSource.ReloadTypes() /
connection.ReloadTypes() before use.

PostgreSQL value types (namespace CodeBrix.PostgresClient.PgSqlTypes):
    PgSqlRange<T>(T lowerBound, T upperBound)          // [lower, upper]
    PgSqlRange<T>(T lowerBound, bool lowerBoundIsInclusive,
                  T upperBound, bool upperBoundIsInclusive)
    PgSqlRange<T>(T lowerBound, bool lowerBoundIsInclusive,
                  bool lowerBoundInfinite, T upperBound,
                  bool upperBoundIsInclusive, bool upperBoundInfinite)
        LowerBound, UpperBound, LowerBoundIsInclusive, IsEmpty,
        LowerBoundInfinite, UpperBoundInfinite, static Empty,
        static Parse(string)
    PgSqlInterval(int months, int days, long time)   // time in microseconds
    PgSqlPoint(double x, double y)   PgSqlLSeg(PgSqlPoint, PgSqlPoint)
    PgSqlBox(PgSqlPoint upperRight, PgSqlPoint lowerLeft)
    PgSqlPath(params PgSqlPoint[] points)   PgSqlPolygon   PgSqlCircle
    PgSqlLine(double a, double b, double c)
    PgSqlInet(IPAddress address, byte netmask)
    PgSqlLogSequenceNumber(ulong value), Parse/TryParse("16/B374D848")
    PgSqlTsVector, PgSqlTsQuery (+ PgSqlTsQueryLexeme, ...And, ...Or, ...)
    PgSqlCube, PgSqlTid

AppContext switches (set at startup, before the first connection):
    "PgSql.EnableLegacyTimestampBehavior"   old DateTime/timestamp rules;
                                            leave off for new code
    "PgSql.DisableDateTimeInfinityConversions"
                                            stop mapping DateTime.MinValue/
                                            MaxValue to -infinity/infinity
    "PgSql.EnableSqlRewriting"              true by default; false disables
                                            named parameters entirely
    "PgSql.EnableStoredProcedureCompatMode" CommandType.StoredProcedure
                                            calls functions, not procedures


================================================================================

COPY
====
COPY is the fastest way to move many rows. All COPY APIs hang off an OPEN
PgSqlConnection; while a COPY is in progress the connection can do nothing
else.

BINARY IMPORT - PgSqlBinaryImporter (sealed, IDisposable, IAsyncDisposable)
    var importer = connection.BeginBinaryImport(
        "COPY t (a, b) FROM STDIN (FORMAT BINARY)");
    void StartRow()            / Task StartRowAsync(CancellationToken)
    void Write<T>(T value)     / Task WriteAsync<T>(T value, CancellationToken)
    void Write<T>(T value, PgSqlDbType pgSqlDbType)     (+ Async)
    void Write<T>(T value, string dataTypeName)         (+ Async)
    void WriteNull()           / Task WriteNullAsync(CancellationToken)
    void WriteRow(params object[] values)
    ulong Complete()           / ValueTask<ulong> CompleteAsync(...)
    TimeSpan Timeout { get; set; }
  Write the columns of each row in the COPY column order. COMPLETE() COMMITS
  THE IMPORT; disposing without calling it cancels and discards every row.
  The types are not inferred from the table: pass PgSqlDbType where the .NET
  type is ambiguous (numeric vs. integer, timestamptz, jsonb).

BINARY EXPORT - PgSqlBinaryExporter (sealed, IDisposable, IAsyncDisposable)
    var exporter = connection.BeginBinaryExport(
        "COPY t (a, b) TO STDOUT (FORMAT BINARY)");
    int StartRow() / ValueTask<int> StartRowAsync(...)  // column count, -1
                                                        //   at the end
    T Read<T>() / ValueTask<T> ReadAsync<T>(CancellationToken = default)
    T Read<T>(PgSqlDbType type) / ReadAsync<T>(PgSqlDbType, ...)
    bool IsNull { get; }        // check BEFORE reading a nullable column
    void Skip() / Task SkipAsync(...)
    void Cancel() / Task CancelAsync()

TEXT / CSV:
    PgSqlCopyTextWriter BeginTextImport("COPY t FROM STDIN")   // a
        StreamWriter: write tab-separated (or CSV) lines; Dispose completes
    PgSqlCopyTextReader BeginTextExport("COPY t TO STDOUT")    // a
        StreamReader: read lines
    PgSqlRawCopyStream BeginRawBinaryCopy("COPY ... (FORMAT BINARY)") - raw
        bytes, for piping a COPY from one server straight into another.

Text imports and the raw stream have Cancel()/CancelAsync() to abort.


================================================================================

TRANSACTIONS
============
  - Explicit: BeginTransaction(Async) on an open connection; pass the
    transaction to PgSqlCommand / PgSqlBatch / the mapper's transaction
    argument, then Commit(Async). Default isolation is Read Committed; pass an
    IsolationLevel for RepeatableRead or Serializable.
  - Savepoints: transaction.Save("sp1"), Rollback("sp1"), Release("sp1").
  - System.Transactions: with Enlist=true (the default) a connection opened
    inside a TransactionScope enlists automatically. Use
    TransactionScopeAsyncFlowOption.Enabled for async code. Two connections
    in one scope escalate to a distributed (two-phase) transaction, which
    needs max_prepared_transactions > 0 on the server.
  - Serializable / RepeatableRead workloads must retry on
    PostgresErrorCodes.SerializationFailure and DeadlockDetected
    (PgSqlException.IsTransient is true for those).


================================================================================

NOTIFICATIONS
=============
LISTEN / NOTIFY delivery happens on a dedicated, open connection:

  1. Open a connection and keep it open for as long as you listen.
  2. Subscribe to connection.Notification - PgSqlNotificationEventArgs has
     string Channel, string Payload and int PID (the sender's backend).
  3. Execute "LISTEN channel_name".
  4. Loop on connection.Wait() / WaitAsync(cancellationToken). Notifications
     arrive only while the connection is waiting or executing a command;
     Wait(timeout) returns false if nothing arrived in time.

Send with "NOTIFY channel_name, 'payload'" or SELECT pg_notify($1, $2) from
any connection. A pooled connection's session is reset when it goes back to
the pool (DISCARD ALL, or RESET ALL + UNLISTEN * when it holds prepared
statements), so do not dispose the listener while listening.

connection.Notice (NoticeEventHandler, PgSqlNoticeEventArgs.Notice is a
PostgresNotice) receives server notices such as RAISE NOTICE output.


================================================================================

REPLICATION
===========
Replication uses its own connection types (not PgSqlConnection), in
CodeBrix.PostgresClient.Replication. Both take a normal connection string
and are IAsyncDisposable:

    LogicalReplicationConnection(string connectionString)
    PhysicalReplicationConnection(string connectionString)

ReplicationConnection (the shared base):
    Task Open(CancellationToken cancellationToken = default)  // NOT OpenAsync
    Task<ReplicationSystemIdentification> IdentifySystem(CancellationToken)
    Task<string> Show(string parameterName, CancellationToken = default)
    Task DropReplicationSlot(string slotName, bool wait = false,
                             CancellationToken cancellationToken = default)
    void SetReplicationStatus(PgSqlLogSequenceNumber lastAppliedAndFlushedLsn)
    Task SendStatusUpdate(CancellationToken cancellationToken = default)
    PgSqlLogSequenceNumber LastReceivedLsn / LastFlushedLsn / LastAppliedLsn
    TimeSpan WalReceiverStatusInterval { get; set; }  // default 10 s
    TimeSpan WalReceiverTimeout { get; set; }

LOGICAL, pgoutput plugin (namespace ...Replication.PgOutput):
    Task<PgOutputReplicationSlot> CreatePgOutputReplicationSlot(
        this LogicalReplicationConnection connection, string slotName,
        bool temporarySlot = false,
        LogicalSlotSnapshotInitMode? slotSnapshotInitMode = null,
        bool twoPhase = false, CancellationToken cancellationToken = default)
    new PgOutputReplicationSlot(string slotName)  // an EXISTING slot
    IAsyncEnumerable<PgOutputReplicationMessage> StartReplication(
        this LogicalReplicationConnection connection,
        PgOutputReplicationSlot slot, PgOutputReplicationOptions options,
        CancellationToken cancellationToken,
        PgSqlLogSequenceNumber? walLocation = null)
    PgOutputReplicationOptions(string publicationName,
        PgOutputProtocolVersion protocolVersion, bool? binary = null,
        PgOutputStreamingMode? streamingMode = null, bool? messages = null,
        bool? twoPhase = null)
        (an IEnumerable<string> publicationNames overload also exists)
    PgOutputProtocolVersion: V1, V2, V3, V4

  Messages (namespace ...Replication.PgOutput.Messages): BeginMessage,
  CommitMessage, RelationMessage, InsertMessage (Relation, NewRow),
  UpdateMessage (+ FullUpdateMessage / IndexUpdateMessage with OldRow),
  DeleteMessage (KeyDeleteMessage / FullDeleteMessage), TruncateMessage,
  TypeMessage, OriginMessage, LogicalDecodingMessage, and the stream/prepare
  messages for streaming and two-phase modes. Every message carries WalStart,
  WalEnd and ServerClock.

  A ReplicationTuple (NewRow, OldRow) is IAsyncEnumerable<ReplicationValue>:
    ReplicationValue.IsDBNull / IsUnchangedToastedValue / Kind
    ValueTask<T> Get<T>(CancellationToken = default)
    string GetFieldName()  string GetDataTypeName()
  Read every value of a row, in order, before moving to the next message -
  the values are streamed from the network.

LOGICAL, test_decoding plugin (namespace ...Replication.TestDecoding):
    CreateTestDecodingReplicationSlot(slotName, ...) and
    StartReplication(slot, cancellationToken, TestDecodingOptions options =
    default, walLocation = null) yielding TestDecodingData (string Data).

PHYSICAL:
    Task<PhysicalReplicationSlot> CreateReplicationSlot(string slotName,
        bool isTemporary = false, bool reserveWal = false,
        CancellationToken cancellationToken = default)
    IAsyncEnumerable<XLogDataMessage> StartReplication(
        PhysicalReplicationSlot slot, CancellationToken cancellationToken)
    IAsyncEnumerable<XLogDataMessage> StartReplication(
        PgSqlLogSequenceNumber walLocation,
        CancellationToken cancellationToken, uint timeline = default)
    XLogDataMessage.Data is a Stream of raw WAL bytes.

ACKNOWLEDGE PROGRESS: call SetReplicationStatus(message.WalEnd) after you
have durably processed a message. The status is sent every
WalReceiverStatusInterval (or immediately with SendStatusUpdate). If you
never acknowledge, the server retains WAL for the slot indefinitely and the
disk fills. Drop permanent slots you no longer need.


================================================================================

MULTI-HOST
==========
List several hosts in Host= and build with BuildMultiHost():

    var builder = new PgSqlDataSourceBuilder(
        "Host=pg1,pg2,pg3;Username=app;Password=secret;Database=shop;" +
        "Load Balance Hosts=true");
    await using PgSqlMultiHostDataSource cluster = builder.BuildMultiHost();

PgSqlMultiHostDataSource : PgSqlDataSource
    PgSqlConnection CreateConnection(TargetSessionAttributes attributes)
    PgSqlConnection OpenConnection(TargetSessionAttributes attributes)
    ValueTask<PgSqlConnection> OpenConnectionAsync(
        TargetSessionAttributes targetSessionAttributes,
        CancellationToken cancellationToken = default)
    PgSqlDataSource WithTargetSession(TargetSessionAttributes attributes)
        // a cached data-source view, handy to inject as "the read pool"
    void ClearDatabaseStates()   // forget cached primary/standby state

TargetSessionAttributes: Any, ReadWrite, ReadOnly, Primary, Standby,
PreferPrimary, PreferStandby.

Hosts are tried in order (round-robin when Load Balance Hosts=true); a host
that fails is marked offline and skipped until Host Recheck Seconds passes.
The provider does not promote standbys - failover means "connect to whichever
listed server currently satisfies the attributes".


================================================================================

LOGGING, TRACING AND METRICS
============================
LOGGING (Microsoft.Extensions.Logging): give the data source an
ILoggerFactory:

    builder.UseLoggerFactory(loggerFactory);
    builder.EnableParameterLogging();    // values in command logs (careful:
                                         //   secrets, personal data)

Categories: "PgSql.Connection", "PgSql.Command", "PgSql.Transaction",
"PgSql.Copy", "PgSql.Replication", "PgSql.Exception". For connections made
without a data source, call PgSqlLoggingConfiguration.InitializeLogging(
loggerFactory, parameterLoggingEnabled: false) once at startup, before any
connection is opened.

Connection events identify the server as {Host}:{Port}/{Database} and the
physical connection as "connector {ConnectorId}" (the backend process id); the
connection string is never passed to the logger, so it cannot leak into logs.

TRACING: commands, batches, COPY operations and (optionally) physical opens
emit System.Diagnostics Activities from the ActivitySource named "PgSql",
using the OpenTelemetry database semantic conventions (db.system.name =
"postgresql", db.query.text, db.namespace, server.address, server.port,
db.response.status_code, ...). Any ActivityListener - or any OpenTelemetry
SDK configured with AddSource("PgSql") - receives them. Tune with:

    builder.ConfigureTracing(o => o
        .ConfigureCommandFilter(cmd => ...)       // Func<PgSqlCommand, bool>
        .ConfigureBatchFilter(batch => ...)
        .ConfigureCommandEnrichmentCallback((activity, cmd) => ...)
        .ConfigureCommandSpanNameProvider(cmd => "...")
        .ConfigureCopyOperationFilter(copyCommand => ...)
        .EnableFirstResponseEvent()
        .EnablePhysicalOpenTracing());

METRICS: the Meter named "PgSql" publishes, tagged with the data source Name
(db.client.connection.pool.name):
    db.client.operation.duration               histogram, seconds
    db.client.operation.failed                 counter
    db.client.operation.pgsql.executing        up-down counter
    db.client.operation.pgsql.bytes_written / .bytes_read
    db.client.operation.pgsql.prepared_ratio
    db.client.connection.count                 (state = idle / used)
    db.client.connection.max
    db.client.connection.pgsql.pending_requests
    db.client.connection.pgsql.timeouts
    db.client.connection.pgsql.create_time     histogram, seconds
Listen with a MeterListener or an OpenTelemetry SDK's AddMeter("PgSql").
EventCounters are also available from the EventSources "PgSql" and
"PgSql.Sql" (dotnet-counters / dotnet-trace).


================================================================================

PGSQLMAPPER - THE BUILT-IN OBJECT MAPPER
========================================
Extension methods on PgSqlConnection, declared in the CodeBrix.PostgresClient
namespace (static class PgSqlMapper). They run SQL and turn rows into
objects, so most data-access code needs no command/reader boilerplate.

Every method has the same leading parameters:
    (this PgSqlConnection connection, string sql, object param = null,
     PgSqlTransaction transaction = null)
and every async form appends
    (..., CancellationToken cancellationToken = default)

Synchronous:
    IEnumerable<T> Query<T>(...)
    IEnumerable<dynamic> Query(...)          // ExpandoObject rows
    T QueryFirst<T>(...)                     // throws if no rows
    T QueryFirstOrDefault<T>(...)
    T QuerySingle<T>(...)                    // throws unless exactly 1 row
    T QuerySingleOrDefault<T>(...)           // throws if more than 1 row
    int Execute(...)                         // rows affected; -1 for DDL
    T ExecuteScalar<T>(...)                  // default(T) for no row / NULL
    PgSqlDataReader ExecuteReader(...)
    PgSqlGridReader QueryMultiple(...)

Asynchronous (identical semantics):
    Task<IEnumerable<T>> QueryAsync<T>(...)
    Task<IEnumerable<dynamic>> QueryAsync(...)
    Task<T> QueryFirstAsync<T>(...)    Task<T> QueryFirstOrDefaultAsync<T>(...)
    Task<T> QuerySingleAsync<T>(...)   Task<T> QuerySingleOrDefaultAsync<T>(...)
    Task<int> ExecuteAsync(...)
    Task<T> ExecuteScalarAsync<T>(...)
    Task<PgSqlDataReader> ExecuteReaderAsync(...)
    Task<PgSqlGridReader> QueryMultipleAsync(...)

PgSqlGridReader (sealed, IDisposable) - the result of QueryMultiple:
    bool IsConsumed { get; }
    IEnumerable<T> Read<T>()   // materializes the CURRENT result set
                               //   (buffered) and moves to the next one;
                               //   throws InvalidOperationException after
                               //   the last set
    void Dispose()             // disposes reader + command, closes the
                               //   connection if QueryMultiple opened it
  No public constructor and no async Read - instances come only from
  QueryMultiple / QueryMultipleAsync.

CONNECTION HANDLING: a CLOSED connection is opened for the call and closed
again afterwards (ExecuteReader and QueryMultiple close it when the reader /
grid reader is disposed). An already-OPEN connection is left open - open it
yourself to run several calls on one connection or inside a transaction.
Query<T> results are fully buffered before the method returns.

PARAMETERS: an anonymous object, a POCO, or IDictionary<string, object>.
  - Reference them in SQL as @name (case-insensitive). The mapper recognizes
    ONLY the @ form - ":name" is not bound.
  - Properties the SQL does not reference are skipped, so one object can
    serve several statements.
  - null binds SQL NULL.
  - A value that is a PgSqlParameter is added as-is (its ParameterName is
    set from the key) - use this for an explicit PgSqlDbType/DataTypeName,
    for array parameters, or for a mapped PostgreSQL enum.
  - Any other sequence (array, List<T>, ...; but not string or byte[]) is an
    IN-LIST: "WHERE id IN @ids" becomes "WHERE id IN (@ids1, @ids2, ...)";
    an empty sequence becomes "(NULL)", which matches no rows. Write
    "IN @ids", without parentheses.
  - Enum values bind as their underlying INTEGER.

RESULT MAPPING:
  - Simple types (primitives, string, decimal, Guid, DateTime,
    DateTimeOffset, DateOnly, TimeOnly, TimeSpan, BigInteger, enums, arrays,
    IPAddress, the PgSqlTypes value types, object, ...) take the FIRST
    column of each row.
  - Any other T is built as a POCO: it needs a public parameterless
    constructor; result columns bind to writable public properties
    case-insensitively AND ignoring underscores, so snake_case columns fill
    PascalCase properties with no aliases (unit_price -> UnitPrice,
    created_at -> CreatedAt). Unmatched columns are ignored; a NULL column
    leaves the property at its default.
  - Conversions: integral width changes (count(*) bigint -> int property),
    enums from integers or (case-insensitive) label text, text -> Guid,
    DateTime -> DateOnly / DateTimeOffset, DateTimeOffset -> DateTime (UTC),
    TimeSpan -> TimeOnly, then Convert.ChangeType (InvariantCulture).
  - dynamic rows are ExpandoObjects keyed by the exact column names; SQL NULL
    becomes null.
  - Not included: multi-mapping (splitting one row into several objects),
    output parameters, stored-procedure command type, buffered:false
    streaming. Use PgSqlCommand / PgSqlDataReader for those.

TRIMMING / NATIVEAOT: PgSqlMapper and PgSqlGridReader are annotated
[RequiresUnreferencedCode] and [RequiresDynamicCode] - they use reflection.
The rest of the provider is trim- and AOT-compatible.


================================================================================

COMPLETE EXAMPLES
=================
Examples 4 to 7 continue from Example 1: they assume a PgSqlDataSource named
dataSource (and, where used, a CancellationToken named cancellationToken)
is in scope.

Example 1: Data source, parameters, reader
------------------------------------------
    using System;
    using CodeBrix.PostgresClient;

    //One data source for the application lifetime
    await using var dataSource = PgSqlDataSource.Create(
        "Host=localhost;Username=app;Password=secret;Database=shop");

    //A data-source command needs no connection handling
    await using (var ddl = dataSource.CreateCommand(
        "CREATE TABLE IF NOT EXISTS products (id serial PRIMARY KEY, " +
        "name text NOT NULL, unit_price numeric(10,2) NOT NULL, " +
        "created_at timestamptz NOT NULL DEFAULT now())"))
    {
        await ddl.ExecuteNonQueryAsync();
    }

    await using var connection = await dataSource.OpenConnectionAsync();

    //Named parameters
    await using (var insert = new PgSqlCommand(
        "INSERT INTO products (name, unit_price) VALUES (@name, @price) " +
        "RETURNING id", connection))
    {
        insert.Parameters.AddWithValue("name", "Widget");
        insert.Parameters.AddWithValue("price", 9.99m);
        int newId = (int)await insert.ExecuteScalarAsync();
        Console.WriteLine($"inserted {newId}");
    }

    //Positional parameters ($1) with a typed, non-boxing parameter
    await using (var query = new PgSqlCommand(
        "SELECT id, name, unit_price, created_at FROM products " +
        "WHERE unit_price < $1", connection))
    {
        query.Parameters.Add(new PgSqlParameter<decimal> { TypedValue = 20m });

        await using var reader = await query.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            int id = reader.GetInt32(0);
            string name = reader.GetString(1);
            decimal price = reader.GetFieldValue<decimal>(2);
            DateTime created = reader.GetFieldValue<DateTime>(3); // Kind=Utc
            Console.WriteLine($"{id} {name} {price} {created:O}");
        }
    }


Example 2: PgSqlMapper - queries, IN-lists, arrays, multi-result, transaction
-----------------------------------------------------------------------------
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using CodeBrix.PostgresClient;

    public class Product
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public decimal UnitPrice { get; set; }   // from unit_price
        public DateTime CreatedAt { get; set; }  // from created_at
    }

    await using var connection = new PgSqlConnection(
        "Host=localhost;Username=app;Password=secret;Database=shop");

    //Each call opens and closes the (closed) connection itself
    int added = await connection.ExecuteAsync(
        "INSERT INTO products (name, unit_price) VALUES (@Name, @UnitPrice)",
        new { Name = "Gizmo", UnitPrice = 14.50m });

    List<Product> cheap = (await connection.QueryAsync<Product>(
        "SELECT id, name, unit_price, created_at FROM products " +
        "WHERE unit_price < @max ORDER BY id",
        new { max = 20m })).ToList();

    long count = await connection.ExecuteScalarAsync<long>(
        "SELECT count(*) FROM products");

    //IN-list expansion: a sequence becomes (@ids1, @ids2, @ids3)
    var some = connection.Query<Product>(
        "SELECT * FROM products WHERE id IN @ids",
        new { ids = new[] { 1, 2, 3 } }).ToList();

    //A real PostgreSQL array: wrap it in a PgSqlParameter
    var viaAny = connection.Query<string>(
        "SELECT name FROM products WHERE id = ANY(@ids)",
        new Dictionary<string, object>
        {
            ["ids"] = new PgSqlParameter { Value = new[] { 1, 2, 3 } }
        }).ToList();

    //Several result sets in one round trip
    using (PgSqlGridReader grid = await connection.QueryMultipleAsync(
        "SELECT * FROM products WHERE id = @id; " +
        "SELECT count(*) FROM products", new { id = 1 }))
    {
        Product first = grid.Read<Product>().SingleOrDefault();
        long total = grid.Read<long>().Single();
    }

    //A transaction: open the connection yourself and pass the transaction
    await connection.OpenAsync();
    await using PgSqlTransaction tx = await connection.BeginTransactionAsync();
    await connection.ExecuteAsync(
        "UPDATE products SET unit_price = unit_price * 1.1 WHERE id = @id",
        new { id = 1 }, tx);
    await connection.ExecuteAsync(
        "DELETE FROM products WHERE unit_price > @limit",
        new { limit = 1000m }, tx);
    await tx.CommitAsync();


Example 3: Data source builder - enums, composites, JSON, tracing filter
------------------------------------------------------------------------
    using System.Collections.Generic;
    using CodeBrix.PostgresClient;
    using CodeBrix.PostgresClient.PgSqlTypes;
    using Microsoft.Extensions.Logging.Abstractions;

    //SQL: CREATE TYPE mood AS ENUM ('sad', 'ok', 'happy');
    //     CREATE TYPE address AS (street text, city text);
    //     CREATE TABLE people (name text, mood mood, home address,
    //                          prefs jsonb);
    public enum Mood { Sad, Ok, Happy }

    public class Address
    {
        public string Street { get; set; }
        public string City { get; set; }
    }

    var builder = new PgSqlDataSourceBuilder(
        "Host=localhost;Username=app;Password=secret;Database=shop");
    builder.Name = "shop-db";
    builder.ConnectionStringBuilder.MaxPoolSize = 50;
    builder.UseLoggerFactory(NullLoggerFactory.Instance); // your factory here
    builder.MapEnum<Mood>("mood");
    builder.MapComposite<Address>("address");
    builder.EnableDynamicJson();
    builder.ConfigureTracing(o => o.ConfigureCommandFilter(
        cmd => !cmd.CommandText.StartsWith("SELECT 1")));
    await using PgSqlDataSource dataSource = builder.Build();

    await using var connection = await dataSource.OpenConnectionAsync();
    await using var cmd = new PgSqlCommand(
        "INSERT INTO people (name, mood, home, prefs) VALUES ($1, $2, $3, $4)",
        connection);
    cmd.Parameters.Add(new PgSqlParameter { Value = "Ada" });
    cmd.Parameters.Add(new PgSqlParameter { Value = Mood.Happy });
    cmd.Parameters.Add(new PgSqlParameter
    {
        Value = new Address { Street = "1 Main", City = "Eugene" }
    });
    cmd.Parameters.Add(new PgSqlParameter
    {
        Value = new Dictionary<string, object> { ["theme"] = "dark" },
        PgSqlDbType = PgSqlDbType.Jsonb
    });
    await cmd.ExecuteNonQueryAsync();

    //Reading back: reader.GetFieldValue<Mood>(i), GetFieldValue<Address>(i);
    //  the mapper also reads mapped enums: conn.Query<Mood>("SELECT mood ...")


Example 4: Batching several statements into one round trip
----------------------------------------------------------
    using System;
    using CodeBrix.PostgresClient;

    await using var connection = await dataSource.OpenConnectionAsync();
    await using var batch = new PgSqlBatch(connection)
    {
        BatchCommands =
        {
            new PgSqlBatchCommand("INSERT INTO audit (what) VALUES ($1)")
            {
                Parameters = { new() { Value = "start" } }
            },
            new PgSqlBatchCommand("SELECT count(*) FROM products"),
            new PgSqlBatchCommand(
                "SELECT name FROM products ORDER BY id LIMIT 5")
        }
    };

    await using (var reader = await batch.ExecuteReaderAsync())
    {
        //The INSERT returns no rows: the reader starts on the count
        await reader.ReadAsync();
        long count = reader.GetInt64(0);

        await reader.NextResultAsync();
        while (await reader.ReadAsync())
        {
            Console.WriteLine(reader.GetString(0));
        }
    }

    int inserted = batch.BatchCommands[0].RecordsAffected;   // 1


Example 5: Bulk COPY - binary import and export
-----------------------------------------------
    using System;
    using System.Collections.Generic;
    using CodeBrix.PostgresClient;
    using CodeBrix.PostgresClient.PgSqlTypes;

    await using var connection = await dataSource.OpenConnectionAsync();

    await using (var importer = await connection.BeginBinaryImportAsync(
        "COPY products (name, unit_price, created_at) " +
        "FROM STDIN (FORMAT BINARY)"))
    {
        foreach (Product p in productsToLoad)
        {
            await importer.StartRowAsync();
            await importer.WriteAsync(p.Name, PgSqlDbType.Text);
            await importer.WriteAsync(p.UnitPrice, PgSqlDbType.Numeric);
            await importer.WriteAsync(p.CreatedAt, PgSqlDbType.TimestampTz);
        }
        ulong rows = await importer.CompleteAsync();   // REQUIRED to commit
        Console.WriteLine($"{rows} rows imported");
    }

    await using (var exporter = await connection.BeginBinaryExportAsync(
        "COPY products (id, name, unit_price) TO STDOUT (FORMAT BINARY)"))
    {
        while (await exporter.StartRowAsync() != -1)
        {
            int id = await exporter.ReadAsync<int>();
            string name = await exporter.ReadAsync<string>();
            decimal price = await exporter.ReadAsync<decimal>();
            Console.WriteLine($"{id} {name} {price}");
        }
    }



Example 6: LISTEN / NOTIFY
--------------------------
    using System;
    using System.Threading;
    using CodeBrix.PostgresClient;

    //A dedicated connection, kept open while listening
    await using var listener = await dataSource.OpenConnectionAsync(
        cancellationToken);
    listener.Notification += (sender, e) =>
        Console.WriteLine($"[{e.Channel}] from pid {e.PID}: {e.Payload}");

    await using (var listen = new PgSqlCommand("LISTEN order_events", listener))
    {
        await listen.ExecuteNonQueryAsync(cancellationToken);
    }

    //Anyone can notify - here through a data-source command
    await using (var notify = dataSource.CreateCommand(
        "SELECT pg_notify('order_events', $1)"))
    {
        notify.Parameters.Add(
            new PgSqlParameter { Value = "order 42 shipped" });
        await notify.ExecuteNonQueryAsync(cancellationToken);
    }

    //The event fires while the connection waits
    while (!cancellationToken.IsCancellationRequested)
    {
        await listener.WaitAsync(cancellationToken);
    }


Example 7: Logical replication with pgoutput
--------------------------------------------
    using System;
    using System.Threading;
    using CodeBrix.PostgresClient.Replication;
    using CodeBrix.PostgresClient.Replication.PgOutput;
    using CodeBrix.PostgresClient.Replication.PgOutput.Messages;

    //Server side: wal_level=logical, and
    //  CREATE PUBLICATION orders_pub FOR TABLE orders;
    await using var replication = new LogicalReplicationConnection(
        "Host=localhost;Username=replicator;Password=secret;Database=shop");
    await replication.Open(cancellationToken);

    PgOutputReplicationSlot slot =
        await replication.CreatePgOutputReplicationSlot(
            "orders_slot", temporarySlot: true,
            cancellationToken: cancellationToken);

    var options = new PgOutputReplicationOptions(
        "orders_pub", PgOutputProtocolVersion.V1);

    await foreach (PgOutputReplicationMessage message in
                   replication.StartReplication(slot, options,
                                                cancellationToken))
    {
        switch (message)
        {
            case InsertMessage insert:
                Console.Write($"INSERT {insert.Relation.RelationName}:");
                await foreach (ReplicationValue value in insert.NewRow)
                {
                    Console.Write(value.IsDBNull
                        ? " NULL"
                        : $" {await value.Get<object>(cancellationToken)}");
                }
                Console.WriteLine();
                break;
            case UpdateMessage update:
                Console.WriteLine($"UPDATE {update.Relation.RelationName}");
                break;
            case DeleteMessage delete:
                Console.WriteLine($"DELETE {delete.Relation.RelationName}");
                break;
        }

        //Acknowledge, so the server can recycle WAL
        replication.SetReplicationStatus(message.WalEnd);
    }


Example 8: Tracing and metrics with in-box listeners
----------------------------------------------------
    using System;
    using System.Diagnostics;
    using System.Diagnostics.Metrics;

    using var activityListener = new ActivityListener
    {
        ShouldListenTo = source => source.Name == "PgSql",
        Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
            ActivitySamplingResult.AllDataAndRecorded,
        ActivityStopped = activity => Console.WriteLine(
            $"{activity.DisplayName} {activity.Duration.TotalMilliseconds:F1}" +
            $" ms {activity.GetTagItem("db.query.text")}")
    };
    ActivitySource.AddActivityListener(activityListener);

    using var meterListener = new MeterListener
    {
        InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == "PgSql")
            {
                listener.EnableMeasurementEvents(instrument);
            }
        }
    };
    meterListener.SetMeasurementEventCallback<double>(
        (instrument, value, tags, state) =>
            Console.WriteLine($"{instrument.Name} = {value}"));
    meterListener.Start();


================================================================================

MINIMUM VIABLE PROJECT TEMPLATE
===============================
A console application that connects, creates a table, writes a row and
reads it back through PgSqlMapper.

MyPgApp.csproj:
    <Project Sdk="Microsoft.NET.Sdk">
      <PropertyGroup>
        <OutputType>Exe</OutputType>
        <TargetFramework>net10.0</TargetFramework>
        <Nullable>disable</Nullable>
        <ImplicitUsings>enable</ImplicitUsings>
      </PropertyGroup>
      <ItemGroup>
        <PackageReference
          Include="CodeBrix.PostgresClient.PostgreSqlLicenseForever" />
      </ItemGroup>
    </Project>

(Add the package with `dotnet add package
CodeBrix.PostgresClient.PostgreSqlLicenseForever` so the current version is
written into the csproj.)

Program.cs:
    using System;
    using System.Threading.Tasks;
    using CodeBrix.PostgresClient;

    public class Note
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public static class Program
    {
        public static async Task<int> Main()
        {
            await using var dataSource = PgSqlDataSource.Create(
                Environment.GetEnvironmentVariable("PG_CONNECTION")
                ?? "Host=localhost;Username=postgres;Password=postgres;" +
                   "Database=postgres");

            await using var connection = await dataSource.OpenConnectionAsync();

            string version = await connection.ExecuteScalarAsync<string>(
                "SELECT version()");
            Console.WriteLine(version);

            await connection.ExecuteAsync(
                "CREATE TEMP TABLE notes (id serial PRIMARY KEY, " +
                "title text NOT NULL, " +
                "created_at timestamptz NOT NULL DEFAULT now())");

            await connection.ExecuteAsync(
                "INSERT INTO notes (title) VALUES (@title)",
                new { title = "hello" });

            foreach (Note n in await connection.QueryAsync<Note>(
                "SELECT id, title, created_at FROM notes ORDER BY id"))
            {
                Console.WriteLine($"{n.Id}: {n.Title} ({n.CreatedAt:u})");
            }

            return 0;
        }
    }


================================================================================

PERFORMANCE TIPS
================

1.  Create ONE PgSqlDataSource per connection string and reuse it (register
    it as a singleton). Open a connection per unit of work and dispose it
    promptly - opening from the pool is cheap, holding connections is not.

2.  Use async APIs end to end in servers (OpenConnectionAsync,
    ExecuteReaderAsync, ReadAsync, the mapper's ...Async forms).

3.  Prepare hot statements. Either call Prepare()/PrepareAsync() on a
    command you execute many times on the same connection, or set
    "Max Auto Prepare=20" (with "Auto Prepare Min Usages") so frequently
    repeated SQL is prepared automatically - this also benefits the mapper
    and data-source commands. Prepared statements survive the connection's
    return to the pool.

4.  Bulk loads: COPY (BeginBinaryImport) is an order of magnitude faster than
    INSERTs. For many small statements, use PgSqlBatch for one round trip.

5.  Send sets as ONE array parameter ("WHERE id = ANY($1)" with an int[])
    instead of an IN-list: the SQL text stays constant (so it can be
    prepared) and the list can be any length.

6.  Positional parameters ($1) skip SQL parsing and rewriting; prefer them
    in hot paths and batches.

7.  Use PgSqlParameter<T> (TypedValue) to avoid boxing, and GetFieldValue<T>
    for reads.

8.  Stream big results: ExecuteReader + ReadAsync processes rows as they
    arrive, while the mapper's Query<T> buffers the whole result. Add
    CommandBehavior.SequentialAccess for very large column values.

9.  Multiplexing ("Multiplexing=true") lets many concurrent async callers
    share few physical connections - useful for very high request counts
    against a connection-limited server. It requires async execution and has
    limitations (see PITFALLS); measure before enabling.

10. Tune Maximum Pool Size to the server's max_connections across ALL app
    instances; Minimum Pool Size keeps warm connections for bursty traffic.

11. Keep EnableParameterLogging off in production.

================================================================================

COMMON PITFALLS TO AVOID
========================

- The package id and the namespace differ: package
  CodeBrix.PostgresClient.PostgreSqlLicenseForever, namespace
  CodeBrix.PostgresClient. The types are PgSql* - PgSqlConnection,
  PgSqlCommand, PgSqlDbType - never the old Npgsql* names.

- DateTime and timestamptz. A DateTime with Kind=Utc writes as timestamptz;
  Kind=Local or Unspecified writes as timestamp WITHOUT time zone. Explicitly
  typing a non-UTC DateTime as TimestampTz throws ArgumentException ("only UTC
  is supported"), and so does a UTC DateTime typed as Timestamp. The quiet
  failure is worse: a Local/Unspecified value inserted into a timestamptz
  column is converted by the SERVER using the session TimeZone, shifting the
  instant. FIX: store instants as DateTime.UtcNow / Kind=Utc (or a
  DateTimeOffset with offset 0 - other offsets throw), use timestamp only for
  "wall-clock" values, and expect timestamptz to read back as Kind=Utc.

- Dispose connections, commands, readers and data sources (await using).
  A connection that is not disposed never returns to the pool, and the next
  OpenConnection eventually fails with "The connection pool has been
  exhausted" (that message says 'Max Pool Size'; the key is Maximum Pool
  Size or MaxPoolSize). Do not cache an open PgSqlConnection in a field or
  singleton - cache the PgSqlDataSource instead.

- One active command per connection. A PgSqlConnection is not thread-safe
  and runs one command at a time: starting a second command while a reader
  is open throws PgSqlOperationInProgressException. FIX: finish or dispose
  the first reader, buffer it, use a second connection, or use PgSqlBatch.
  Never share one connection across parallel tasks.

- Multiplexing limitations. With "Multiplexing=true": synchronous execution
  throws NotSupportedException (including the mapper's sync methods - use
  the ...Async forms); explicit Prepare() is not supported; transactions must
  start with BeginTransaction (not a raw "BEGIN"); Keepalive is not
  implemented; it cannot be combined with multiple hosts.

- Named vs positional parameters. "@name" placeholders need named
  parameters; "$1" placeholders need UNNAMED parameters added in order.
  Mixing the two in one command throws. A positional command may contain only
  ONE statement - "SELECT $1; SELECT 2" fails on the server; use PgSqlBatch.
  The mapper always uses @name (":name" is not recognized by the mapper).

- IN-lists vs arrays in PgSqlMapper. The mapper expands EVERY sequence value
  into an IN-list, so "= ANY(@ids)" with new { ids = int[] } becomes invalid
  SQL ("op ANY/ALL (array) requires array on right side"). FIX: write
  "IN @ids" for an expanded list, or keep "= ANY(@ids)" and pass
  new PgSqlParameter { Value = ids } as the value (in an anonymous object or
  a dictionary). With plain PgSqlCommand, an int[] always binds as one array:
  write "= ANY(@ids)", never "IN @ids". "IN (@ids)" is wrong in both.

- Enums bind as integers in PgSqlMapper. Passing a .NET enum value through
  the mapper binds its underlying integer - fine for an integer column, an
  error for a PostgreSQL enum column ("column is of type mood but expression
  is of type integer"). FIX: pass new PgSqlParameter { Value = mood } (needs
  MapEnum on the data source), or pass mood.ToString().ToLowerInvariant()
  and cast in SQL ("@mood::mood").

- Unmapped PostgreSQL enum columns cannot be read as object (GetValue, the
  mapper, dynamic rows) - InvalidCastException. FIX: map the enum with
  builder.MapEnum<T>() and use connections from that data source, or select
  "mood::text" (the mapper then parses the label case-insensitively).

- Type mapping lives on the data source. MapEnum/MapComposite/
  EnableDynamicJson apply only to connections from the PgSqlDataSource built
  with them - a separate new PgSqlConnection(connectionString) does not see
  them. Connection-level and global type mappers are obsolete.

- PgSqlMapper is not for trimmed or NativeAOT apps. It is marked
  [RequiresUnreferencedCode] and [RequiresDynamicCode]; with PublishTrimmed
  or PublishAot the compiler warns (IL2026 / IL3050) and POCO members can be
  trimmed away. FIX: use PgSqlCommand + PgSqlDataReader there, and
  PgSqlSlimDataSourceBuilder for a lean, AOT-safe data source. MapComposite,
  EnableDynamicJson, EnableRecordsAsTuples and EnableUnmappedTypes carry the
  same annotations.

- PgSqlMapper POCOs need a public parameterless constructor and writable
  public properties (fields and constructor parameters are not bound). Do not
  add "AS UnitPrice" aliases for snake_case columns - underscores and case are
  already ignored.

- A PgSqlParameter whose Value is null throws at execution ("must have
  either its DbType, PgSqlDbType, DataTypeName or its Value set" / "cannot
  be null, DBNull.Value should be used instead"). FIX: Value =
  (object)x ?? DBNull.Value. (The mapper converts null to DBNull itself.)

- QuerySingle/QueryFirst throw InvalidOperationException on an empty result;
  use the OrDefault forms when "no row" is a normal outcome. Execute returns
  -1 for statements with no row count (DDL).

- Binary COPY must end with Complete()/CompleteAsync(). Disposing the
  importer without it silently discards the rows. Write values in COPY
  column order with explicit PgSqlDbType where the .NET type is ambiguous
  (decimal -> numeric, DateTime -> timestamptz, string -> jsonb).

- ReplicationConnection opens with Open(cancellationToken) - there is no
  OpenAsync. Always call SetReplicationStatus(message.WalEnd) as you process
  messages, and drop permanent slots you abandon, or the server keeps WAL
  forever.

- The converter/type-resolver extension points (AddTypeInfoResolverFactory,
  the Internal.* converter types) are [Experimental]: using them raises
  diagnostics NPG9001 / NPG9002 / NPG9003 as errors until you opt in with
  <NoWarn>. Ordinary type mapping never needs them.

- PgSqlLargeObjectManager and PgSqlTsVector.Parse are [Obsolete]. Call the
  server's lo_* functions and to_tsvector() in SQL instead.


================================================================================

WHAT THIS PACKAGE DOES NOT DO
=============================
Do NOT reach for this package for:

  - Entity Framework Core: there is no EF Core provider. Use PgSqlCommand /
    PgSqlDataReader, or PgSqlMapper, for data access.
  - Plugin type mappings for third-party libraries: there is no GeoJSON,
    NetTopologySuite (PostGIS geometry objects), NodaTime or Json.NET plugin.
    PostGIS values can still be read/written as text or WKB bytes with SQL
    functions (ST_AsBinary, ST_GeomFromWKB); JSON goes through
    System.Text.Json.
  - An OpenTelemetry package: there is no AddPgSql... instrumentation
    helper. The provider emits plain ActivitySource / Meter data named
    "PgSql" - listen with ActivityListener / MeterListener, or with any
    OpenTelemetry SDK via AddSource("PgSql") / AddMeter("PgSql").
  - Dependency-injection registration helpers: register the
    PgSqlDataSource yourself (services.AddSingleton(dataSource)).
  - Running a PostgreSQL server, migrations, schema management or an
    embedded database: it is a client only.
  - A full ORM: PgSqlMapper has no change tracking, LINQ provider,
    relationships, multi-mapping or SQL generation - you write the SQL.
  - Other database engines (it speaks only the PostgreSQL protocol; servers
    with that protocol, such as Redshift, work with the documented
    compatibility settings).
  - Promoting standbys or managing a cluster: multi-host support only
    chooses which listed server to connect to.

CodeBrix.PostgresClient IS for: any .NET 10 application that talks to
PostgreSQL - from simple queries through bulk COPY, notifications, logical
replication and multi-host deployments - with a lightweight mapper for
everyday row-to-object code.


================================================================================

WORKING EXAMPLES ON GITHUB
==========================
The test project is the executable documentation for this package. Browse it
at:

    https://github.com/ellisnet/CodeBrix.PostgresClient/tree/main/tests/CodeBrix.PostgresClient.Tests

Feature-to-test-file map:

  PgSqlMapper - every method, parameter binding, IN-lists, arrays,
  QueryMultiple, connection handling:
    https://github.com/ellisnet/CodeBrix.PostgresClient/blob/main/tests/CodeBrix.PostgresClient.Tests/PgSqlMapperTests.cs

  Connections, opening, pooling, state, server information:
    https://github.com/ellisnet/CodeBrix.PostgresClient/blob/main/tests/CodeBrix.PostgresClient.Tests/ConnectionTests.cs

  Data sources and the builder:
    https://github.com/ellisnet/CodeBrix.PostgresClient/blob/main/tests/CodeBrix.PostgresClient.Tests/DataSourceTests.cs

  Commands, parameters, timeouts, cancellation:
    https://github.com/ellisnet/CodeBrix.PostgresClient/blob/main/tests/CodeBrix.PostgresClient.Tests/CommandTests.cs

  Data readers, column access, schema:
    https://github.com/ellisnet/CodeBrix.PostgresClient/blob/main/tests/CodeBrix.PostgresClient.Tests/ReaderTests.cs

  Batching:
    https://github.com/ellisnet/CodeBrix.PostgresClient/blob/main/tests/CodeBrix.PostgresClient.Tests/BatchTests.cs

  Prepared statements:
    https://github.com/ellisnet/CodeBrix.PostgresClient/blob/main/tests/CodeBrix.PostgresClient.Tests/PrepareTests.cs

  Transactions and savepoints:
    https://github.com/ellisnet/CodeBrix.PostgresClient/blob/main/tests/CodeBrix.PostgresClient.Tests/TransactionTests.cs

  COPY (binary, text, raw):
    https://github.com/ellisnet/CodeBrix.PostgresClient/blob/main/tests/CodeBrix.PostgresClient.Tests/CopyTests.cs

  LISTEN / NOTIFY:
    https://github.com/ellisnet/CodeBrix.PostgresClient/blob/main/tests/CodeBrix.PostgresClient.Tests/NotificationTests.cs

  Multi-host, failover, load balancing:
    https://github.com/ellisnet/CodeBrix.PostgresClient/blob/main/tests/CodeBrix.PostgresClient.Tests/MultipleHostsTests.cs

  Logical replication with pgoutput:
    https://github.com/ellisnet/CodeBrix.PostgresClient/blob/main/tests/CodeBrix.PostgresClient.Tests/Replication/PgOutputReplicationTests.cs

  Type mapping (one file per type family in Types/ - for example):
    https://github.com/ellisnet/CodeBrix.PostgresClient/blob/main/tests/CodeBrix.PostgresClient.Tests/Types/DateTimeTests.cs
    https://github.com/ellisnet/CodeBrix.PostgresClient/blob/main/tests/CodeBrix.PostgresClient.Tests/Types/ArrayTests.cs
    https://github.com/ellisnet/CodeBrix.PostgresClient/blob/main/tests/CodeBrix.PostgresClient.Tests/Types/RangeTests.cs
    https://github.com/ellisnet/CodeBrix.PostgresClient/blob/main/tests/CodeBrix.PostgresClient.Tests/Types/EnumTests.cs
    https://github.com/ellisnet/CodeBrix.PostgresClient/blob/main/tests/CodeBrix.PostgresClient.Tests/Types/CompositeTests.cs
    https://github.com/ellisnet/CodeBrix.PostgresClient/blob/main/tests/CodeBrix.PostgresClient.Tests/Types/JsonTests.cs

  Tracing (ActivitySource "PgSql") and metrics (Meter "PgSql"):
    https://github.com/ellisnet/CodeBrix.PostgresClient/blob/main/tests/CodeBrix.PostgresClient.Tests/TracingTests.cs
    https://github.com/ellisnet/CodeBrix.PostgresClient/blob/main/tests/CodeBrix.PostgresClient.Tests/MetricTests.cs

To read a file's source directly, fetch the raw URL:
    https://raw.githubusercontent.com/ellisnet/CodeBrix.PostgresClient/main/tests/CodeBrix.PostgresClient.Tests/PgSqlMapperTests.cs


================================================================================

QUICK REFERENCE CARD
====================

--- Install ---
dotnet add package CodeBrix.PostgresClient.PostgreSqlLicenseForever

--- Namespaces ---
using CodeBrix.PostgresClient;               // everything core + PgSqlMapper
using CodeBrix.PostgresClient.PgSqlTypes;    // PgSqlDbType, ranges, ...
using CodeBrix.PostgresClient.Replication;   // replication connections
using CodeBrix.PostgresClient.Replication.PgOutput;           // pgoutput
using CodeBrix.PostgresClient.Replication.PgOutput.Messages;  // messages

--- Data source (one per app) ---
Simple:             var ds = PgSqlDataSource.Create(connString);
Configured:         var b = new PgSqlDataSourceBuilder(connString);
                    b.MapEnum<Mood>("mood"); b.UseLoggerFactory(lf);
                    var ds = b.Build();
Multi-host:         var cluster = b.BuildMultiHost();
                    cluster.WithTargetSession(TargetSessionAttributes.Primary)
Connection:         await using var conn = await ds.OpenConnectionAsync();
One-off command:    await using var cmd = ds.CreateCommand(sql);

--- Commands ---
Named:              new PgSqlCommand("... WHERE id = @id", conn);
                    cmd.Parameters.AddWithValue("id", 42);
Positional:         new PgSqlCommand("... WHERE id = $1", conn);
                    cmd.Parameters.Add(
                        new PgSqlParameter<int> { TypedValue = 42 });
Typed:              new PgSqlParameter { Value = json,
                                         PgSqlDbType = PgSqlDbType.Jsonb }
Array param:        "WHERE id = ANY($1)" + Value = new[] { 1, 2, 3 }
Execute:            ExecuteNonQueryAsync / ExecuteScalarAsync /
                    ExecuteReaderAsync
Read:               while (await r.ReadAsync()) r.GetFieldValue<T>(i)
Prepare:            await cmd.PrepareAsync()   or  Max Auto Prepare=20
Batch:              new PgSqlBatch(conn) { BatchCommands = { ... } }
Transaction:        await using var tx = await conn.BeginTransactionAsync();
                    ... await tx.CommitAsync();

--- PgSqlMapper (on PgSqlConnection) ---
Query:              conn.Query<T>(sql, new { id })        / QueryAsync<T>
First/Single:       QueryFirst<T> / QueryFirstOrDefault<T> /
                    QuerySingle<T> / QuerySingleOrDefault<T>
Dynamic:            conn.Query(sql)  -> IEnumerable<dynamic>
Execute:            conn.Execute(sql, param)              -> rows affected
Scalar:             conn.ExecuteScalar<long>("SELECT count(*) FROM t")
Reader:             conn.ExecuteReader(sql, param)
Multi-result:       using var g = conn.QueryMultiple(sql); g.Read<T>()
In a transaction:   conn.Execute(sql, param, tx)
IN-list:            "WHERE id IN @ids", new { ids = new[] { 1, 2 } }
Real array:         "= ANY(@ids)", new { ids = new PgSqlParameter
                                          { Value = new[] { 1, 2 } } }
Columns:            snake_case -> PascalCase automatically
Not for:            trimmed / NativeAOT apps

--- COPY ---
Import:             conn.BeginBinaryImport(
                        "COPY t (a,b) FROM STDIN (FORMAT BINARY)")
                    StartRow(); Write(v, PgSqlDbType.X); ... Complete();
Export:             conn.BeginBinaryExport(
                        "COPY t (a,b) TO STDOUT (FORMAT BINARY)")
                    while (StartRow() != -1) Read<T>()
Text:               BeginTextImport / BeginTextExport

--- Notifications ---
conn.Notification += (s, e) => e.Channel / e.Payload / e.PID
"LISTEN ch" then loop: await conn.WaitAsync(ct)

--- Replication ---
await using var rc = new LogicalReplicationConnection(cs); await rc.Open();
var slot = await rc.CreatePgOutputReplicationSlot("slot");
await foreach (var m in rc.StartReplication(slot,
    new PgOutputReplicationOptions("pub", PgOutputProtocolVersion.V1), ct))
    { ...; rc.SetReplicationStatus(m.WalEnd); }

--- Observability ---
Logging:            b.UseLoggerFactory(lf)   categories "PgSql.*"
Tracing:            ActivitySource "PgSql"   b.ConfigureTracing(...)
Metrics:            Meter "PgSql"

--- Errors ---
catch (PostgresException ex) when (ex.SqlState ==
                                   PostgresErrorCodes.UniqueViolation)
catch (PgSqlException ex) when (ex.IsTransient)

--- DateTime rule ---
DateTime Kind=Utc <-> timestamptz;  Local/Unspecified <-> timestamp

Target: .NET 10 or later
License: PostgreSQL License


================================================================================
END OF AGENT-README
