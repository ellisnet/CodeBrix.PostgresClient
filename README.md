# CodeBrix.PostgresClient

A fully managed ADO.NET data provider for PostgreSQL, with a built-in lightweight object mapper.
CodeBrix.PostgresClient is provided as a .NET 10 library and associated `CodeBrix.PostgresClient.PostgreSqlLicenseForever` NuGet package.

CodeBrix.PostgresClient supports applications and assemblies that target Microsoft .NET version 10.0 and later.
Microsoft .NET version 10.0 is a Long-Term Supported (LTS) version of .NET, and was released on Nov 11, 2025; and will be actively supported by Microsoft until Nov 14, 2028.
Please update your C#/.NET code and projects to the latest LTS version of Microsoft .NET.

## Installation

```
dotnet add package CodeBrix.PostgresClient.PostgreSqlLicenseForever
```

Note that the NuGet package ID and the namespace are different - there is no package named plain `CodeBrix.PostgresClient`:

* NuGet package ID: `CodeBrix.PostgresClient.PostgreSqlLicenseForever`
* Assembly and primary namespace: `CodeBrix.PostgresClient` - i.e. `using CodeBrix.PostgresClient;`

PostgreSQL-specific value types (ranges, intervals, geometric types, full-text search types and the `PgSqlDbType` enumeration) live in `CodeBrix.PostgresClient.PgSqlTypes`.

XML documentation (IntelliSense) ships alongside the assembly.

The package pulls in one dependency automatically: `Microsoft.Extensions.Logging.Abstractions`, so that logging can flow to any `ILoggerFactory`.

## CodeBrix.PostgresClient supports:

* The full ADO.NET surface: `PgSqlConnection`, `PgSqlCommand`, `PgSqlDataReader`, `PgSqlTransaction`, `PgSqlParameter`, `PgSqlBatch`, `PgSqlDataAdapter`, `PgSqlCommandBuilder` and `PgSqlFactory`
* `PgSqlDataSource` - a thread-safe connection factory with connection pooling, and a builder for type mappings, logging, SSL and authentication callbacks
* Synchronous and asynchronous APIs throughout, with cancellation
* Multi-host connections with failover, load balancing and target session attributes (primary, standby, read-write, read-only)
* Multiplexing of commands from many callers over a small number of physical connections
* Prepared statements, both explicit and automatic
* Batching several statements into one round trip
* Binary and text COPY for high-speed bulk import and export
* Logical replication (pgoutput and test_decoding plugins) and physical replication
* LISTEN / NOTIFY notifications
* Large objects
* Rich type mapping: arrays, ranges and multiranges, composites, enums, domains, records, JSON and JSONB (including System.Text.Json serialization of POCOs), hstore, ltree, cube, network types, geometric types, bit strings, intervals, full-text search, date/time types with infinity handling, and more
* Authentication with cleartext, MD5 and SCRAM-SHA-256 passwords, SSL/TLS (including client certificates and channel binding), Kerberos/GSSAPI, and .pgpass files
* Two-phase commit and System.Transactions enlistment, including distributed transactions
* Tracing through `System.Diagnostics.ActivitySource` and metrics through `System.Diagnostics.Metrics`
* `PgSqlMapper` - query and command extension methods on `PgSqlConnection` that map results to objects: `Query`, `QueryFirst`, `QuerySingle` (and their `OrDefault` forms), `Execute`, `ExecuteScalar`, `ExecuteReader` and `QueryMultiple`, synchronous and asynchronous, with anonymous-object parameters and IN-list expansion

## Sample Code

### Run a Query with a Data Source

```csharp
using CodeBrix.PostgresClient;

await using var dataSource = PgSqlDataSource.Create(
    "Host=localhost;Username=app;Password=secret;Database=shop");

await using var command = dataSource.CreateCommand("SELECT name FROM products WHERE price < @max");
command.Parameters.AddWithValue("max", 20m);

await using var reader = await command.ExecuteReaderAsync();
while (await reader.ReadAsync())
{
    Console.WriteLine(reader.GetString(0));
}
```

### Map Rows to Objects with PgSqlMapper

```csharp
using CodeBrix.PostgresClient;

public class Product
{
    public int Id { get; set; }
    public string Name { get; set; }
    public decimal UnitPrice { get; set; }   // filled from a unit_price column
}

await using var connection = new PgSqlConnection(
    "Host=localhost;Username=app;Password=secret;Database=shop");

var cheap = await connection.QueryAsync<Product>(
    "SELECT id, name, unit_price FROM products WHERE unit_price < @max ORDER BY name",
    new { max = 20m });

var selected = await connection.QueryAsync<Product>(
    "SELECT id, name, unit_price FROM products WHERE id IN @ids",
    new { ids = new[] { 3, 5, 8 } });

int updated = await connection.ExecuteAsync(
    "UPDATE products SET unit_price = unit_price * 1.1 WHERE id = @id",
    new { id = 3 });
```

### Bulk Import with Binary COPY

```csharp
using CodeBrix.PostgresClient;
using CodeBrix.PostgresClient.PgSqlTypes;

await using var connection = await dataSource.OpenConnectionAsync();
await using var importer = await connection.BeginBinaryImportAsync(
    "COPY products (name, unit_price) FROM STDIN (FORMAT BINARY)");

foreach (var (name, price) in rows)
{
    await importer.StartRowAsync();
    await importer.WriteAsync(name, PgSqlDbType.Text);
    await importer.WriteAsync(price, PgSqlDbType.Numeric);
}

await importer.CompleteAsync();
```

## Documentation

The NuGet package includes `AGENT-README.txt`, a complete API reference and usage guide written for AI coding agents - point your agent at that file when it is writing code against this library.

Additional sample code and usage examples are available in the `CodeBrix.PostgresClient.Tests` project:
https://github.com/ellisnet/CodeBrix.PostgresClient/tree/main/tests/CodeBrix.PostgresClient.Tests

## License

CodeBrix.PostgresClient is licensed under the PostgreSQL License - see the
[LICENSE](https://github.com/ellisnet/CodeBrix.PostgresClient/blob/main/LICENSE) file.

For licensing and provenance information about the open source code included in
this package, see [THIRD-PARTY-NOTICES.txt](https://github.com/ellisnet/CodeBrix.PostgresClient/blob/main/THIRD-PARTY-NOTICES.txt).
