================================================================================
MAINTAINER-README: CodeBrix.PostgresClient
Notes for people and agents MAINTAINING this repository - not for package
consumers
================================================================================

If you are CONSUMING the NuGet package, stop reading and open AGENT-README.txt
instead. Everything below is about the repository itself: how it is laid out,
how it builds, how it is tested, how it is packaged, where its ported code came
from, and the conventions the source follows.


PURPOSE AND SCOPE
=================
This repository produces exactly one NuGet package:

    PackageId:  CodeBrix.PostgresClient.PostgreSqlLicenseForever
    Assembly:   CodeBrix.PostgresClient
    Namespaces: CodeBrix.PostgresClient, CodeBrix.PostgresClient.PgSqlTypes
                (plus the sub-namespaces beneath CodeBrix.PostgresClient)
    Project:    src/CodeBrix.PostgresClient/CodeBrix.PostgresClient.csproj
    License:    PostgreSQL License (SPDX: PostgreSQL)
    Consumer documentation: AGENT-README.txt (repo root)

It is a port of the core Npgsql provider (see PROVENANCE below), with every
"Npgsql" in an identifier, file name or runtime-visible string renamed to
"PgSql", plus one CodeBrix addition: PgSqlMapper, a lightweight object mapper
(src/CodeBrix.PostgresClient/Mapper/).


REPOSITORY LAYOUT
=================
    CodeBrix.PostgresClient.slnx     Solution. Its Solution Items folder carries
                                     .gitignore, AGENT-README.txt,
                                     EXTRAS-README.txt, global.json,
                                     icon-codebrix-128.png, LICENSE,
                                     MAINTAINER-README.txt, README-INDEX.txt,
                                     README.md and THIRD-PARTY-NOTICES.txt; its
                                     Tests folder carries the test project; the
                                     two src/ projects sit at the solution root.
                                     Add a new root document to Solution Items
                                     when you add one.

    global.json                      Selects the Microsoft.Testing.Platform test
                                     runner. Does NOT pin an SDK version.

    src/CodeBrix.PostgresClient/     the library; the only packable project
      (root)                         the ADO.NET surface: PgSqlConnection,
                                     PgSqlCommand, PgSqlDataReader,
                                     PgSqlDataSource and its builders,
                                     PgSqlParameter, PgSqlBatch, COPY,
                                     PgSqlConnectionStringBuilder, logging,
                                     tracing and metrics
      BackendMessages/               protocol messages from the server
      Internal/                      the connector, buffers, converters, type
                                     resolution (public but experimental)
      Mapper/                        PgSqlMapper and PgSqlGridReader (CodeBrix)
      NameTranslation/               CLR <-> PostgreSQL name translators
      PgSqlTypes/                    PostgreSQL value types and PgSqlDbType
      PostgresTypes/                 the database type catalogue model
      Properties/                    AssemblyInfo.cs and the PgSqlStrings
                                     resources (resx + hand-maintained
                                     Designer.cs)
      Replication/                   logical and physical replication
      Schema/                        GetSchema / GetColumnSchema support
      TypeMapping/                   global and per-data-source type mappers
      Util/                          small helpers
      InternalsVisibleTo.cs          grants CodeBrix.PostgresClient.Tests only

    src/CodeBrix.PostgresClient.SourceGenerators/
                                     the build-time source generator; see THE
                                     SOURCE GENERATOR

    tests/CodeBrix.PostgresClient.Tests/
                                     the test suite; see TESTING

The source layout inside src/CodeBrix.PostgresClient MIRRORS THE UPSTREAM
LAYOUT, file for file (with Npgsql -> PgSql in file names). That is a
deliberate departure from the usual family preference for regrouping a heavy
project root into sub-folders: for a port, upstream's tree is the map that makes
an upstream fix locatable and a future re-port diffable. Do not reorganize it.
Mapper/ is the only CodeBrix-original folder.


THE SOURCE GENERATOR
====================
src/CodeBrix.PostgresClient.SourceGenerators is NOT optional: it generates
PgSqlConnectionStringBuilder.Generated.cs (the Init and GeneratedActions
partial methods - the per-keyword get / set / remove dispatch), and the library
does not compile without it. It reads the [PgSqlConnectionStringProperty],
[DisplayName], [DefaultValue] and [Obsolete] attributes on the builder's
properties and renders PgSqlConnectionStringBuilder.snbtxt (an embedded
resource) with CodeBrix.Templating.

    * It targets netstandard2.0 - the family-sanctioned exception to
      net10-only, because the compiler host loads analyzers as netstandard2.0
      - and sets LangVersion latest for the same reason (netstandard2.0
      otherwise means C# 7.3). It is IsPackable=false and is referenced with
      OutputItemType="Analyzer" ReferenceOutputAssembly="false": nothing from
      it is shipped.
    * An analyzer's own package dependencies are not resolved by the compiler
      host, so the GetDependencyTargetPaths target in its csproj hands the
      netstandard2.0 CodeBrix.Templating.dll to the host alongside the
      generator (GeneratePathProperty exposes the package folder). On the
      .NET SDK compiler, CodeBrix.Templating's own netstandard2.0 dependencies
      (System.Text.Json, Microsoft.CSharp, System.Threading.Tasks.Extensions)
      are already in the host. If an IDE whose compiler runs on .NET Framework
      fails to load the generator, those three are what it is missing.
    * When you add a connection-string property, give it
      [PgSqlConnectionStringProperty] and [DisplayName]; the generator picks it
      up. Look at obj/<Configuration>/net10.0/generated/... to see the output.


BUILDING
========
    dotnet restore CodeBrix.PostgresClient.slnx
    dotnet build   CodeBrix.PostgresClient.slnx

Building the library produces the .nupkg automatically
(GeneratePackageOnBuild), at:

    src/CodeBrix.PostgresClient/bin/<Configuration>/CodeBrix.PostgresClient.PostgreSqlLicenseForever.<version>.nupkg

The build must be clean: zero warnings, zero errors, in Debug and Release.
There is no WarningLevel override, no #pragma warning and no
[SuppressMessage] anywhere in this repository, and none may be added.
GenerateDocumentationFile is on, so CS1591 fires for any undocumented public
member - fix it by writing the comment, never by suppressing the warning.

There is exactly ONE NoWarn line in the library csproj: NPG9001;NPG9002;
NPG9003. The test csproj carries the same three IDs plus CS0618 (Jeremy's
decision, 2026-10-07): some tests deliberately exercise [Obsolete] APIs that
still ship - GlobalTypeMapper, PgSqlLargeObjectManager, PgSqlTsVector.Parse /
PgSqlTsQuery.Parse, PgSqlDataReader.Statements - and must keep doing so until
those APIs are removed. The library itself builds with zero CS0618; never
suppress it there. Those are not defect rules; they are the
diagnostic IDs of the library's own [Experimental] API gates
(PgSqlDiagnostics.cs), which C# raises as errors even inside the declaring
assembly. The line is the opt-in acknowledgement upstream also carries; every
[Experimental] attribute is still in place, so consumers see the same gates.
Nothing else may ever be added to it. The IDs keep their upstream values
because a diagnostic ID is a public contract.

IsAotCompatible is on, so the trimming and NativeAOT analyzers run. The
reflection-based PgSqlMapper and PgSqlGridReader carry
[RequiresUnreferencedCode] and [RequiresDynamicCode]; keep any new reflection
behind the same annotations rather than suppressing IL warnings.

AllowUnsafeBlocks is on: the buffers, converters and authentication code use
pointer and stackalloc code, as upstream does.


TESTING
=======
    dotnet test --solution CodeBrix.PostgresClient.slnx

THE TEST RUNNER IS Microsoft.Testing.Platform (MTP), selected by global.json at
the repository root. Do not delete global.json: without it, `dotnet test` falls
back to the VSTest bridge, which fails on the .NET 10 SDK with "Testing with
VSTest target is no longer supported by Microsoft.Testing.Platform".

The suite is xUnit v3 + SilverAssertions. No coverage collector is referenced
(the family dropped coverlet.collector). Known SDK quirk: `dotnet test
--solution` can report zero tests ran even though the suite is fine; in that
case run the test assembly directly - it is an executable:

    dotnet tests/CodeBrix.PostgresClient.Tests/bin/Debug/net10.0/CodeBrix.PostgresClient.Tests.dll
    dotnet tests/.../CodeBrix.PostgresClient.Tests.dll -class "CodeBrix.PostgresClient.Tests.CommandTests*"

THE TEST DATABASE IS A DOCKER CONTAINER. Support/TestDatabase.cs, using the
CodeBrix.Docker package, starts a postgres container the first time a test
reads TestUtil.ConnectionString / TestDatabase.ConnectionString, and the
TestDatabaseLifetime assembly fixture removes it when the run ends. Tests that
never touch the database never start it.

    * Requirements: a reachable Docker daemon (Linux containers) that the
      current user may use. The image (TestDatabase.Image) is pulled on first
      use.
    * The container is configured like upstream's CI server: ssl=on with the
      test certificates in tests/.../Certificates, max_connections=500,
      wal_level=logical, max_wal_senders=50, max_prepared_transactions=100,
      synchronous_standby_names='pgsql_test_sync_standby',
      password_encryption=scram-sha-256, the users pgsql_tests (md5 password),
      pgsql_tests_scram, pgsql_tests_ssl and pgsql_tests_nossl, and pg_hba.conf
      rules to match. Everything travels inside the container specification
      (environment variables plus a small entrypoint wrapper) - there are no
      bind mounts, so a remote daemon works too.
    * Each test process gets its own container on a free host port, so
      parallel test runs do not interfere. Containers are labelled with the
      owning process id and host name; a run removes containers left behind by
      DEAD test processes on the same host (a crashed or killed run) before
      starting its own.
    * NO DOCKER => THE DATABASE TESTS FAIL with "Docker not available: ...".
      That is deliberate (owner's decision): a missing database must never
      look like a green run. Non-database tests still pass.
    * To run against an existing server instead, set PGSQL_TEST_DB to its
      connection string (the server must be configured as above for the full
      suite to pass). No container is started then.
    * Tests that the container cannot support - Unix-domain-socket connections
      to the server, Kerberos/GSSAPI/SSPI, Windows-only behaviour - are
      skipped with a specific reason. Upstream's [Explicit] tests are
      [Fact(Explicit = true)] and do not run by default.

Fixture-parameterised upstream classes (multiplexing on/off, sync/async, and
similar) are abstract classes with one sealed subclass per parameter row, in
the same file (e.g. CommandTests_NonMultiplexing / CommandTests_Multiplexing).
Tests that change process-wide state run in the NonParallel collection
(Support/TestAssemblySetup.cs).


PACKAGING / PUBLISHING
======================
The version is the family's date-stamped scheme, computed at build time by the
canonical block in the library csproj: 1.<years since 2026>.<UTC day of
year>.<UTC minute of day>. Every build produces a new version; never hardcode
a <Version>.

The package contains the assembly, its XML documentation file, the icon,
README.md, AGENT-README.txt and THIRD-PARTY-NOTICES.txt. Its one dependency is
Microsoft.Extensions.Logging.Abstractions. The source generator is not packed.

The <Copyright> carries the upstream notice first ("Copyright (c) 2002-2026,
Npgsql.") and then the CodeBrix one, as the PostgreSQL License requires the
notice to travel with the code. Jeremy publishes.


CODING CONVENTIONS
==================
    * File layout: no leading blank line; any preserved upstream header, then
      one contiguous using block (System.* first, then the rest, alphabetical;
      aliases and `using static` last), then a file-scoped namespace, then the
      types - one blank line between each part.
    * File-scoped namespaces only. Ported files keep the
      "//was previously: <upstream namespace>;" comment on the namespace line;
      CodeBrix-original files (Mapper/, test Support/TestDatabase*.cs,
      Support/ValueEquality.cs, Support/TestAssemblySetup.cs, PgSqlMapperTests.cs)
      have none.
    * No global usings, no ImplicitUsings, no #nullable, no Nullable=enable.
      No "?" on reference types and no "!" null-forgiving operator;
      value-type nullables (int?, DateTime?) are fine.
    * Every public member carries a specific XML doc comment.
    * New names use "PgSql", never "Npgsql". Runtime names (ActivitySource,
      Meter, EventSource, logger categories, AppContext switches) start with
      "PgSql".
    * Tests: <Class>Tests.cs files; methods named <Member>_<snake_case> or
      plain snake_case; //Arrange //Act //Assert in multi-statement bodies,
      expression-bodied single-statement tests; SilverAssertions fluent form;
      TestContext.Current.CancellationToken passed to every cancellable call.


PROVENANCE / VENDORED SOURCES
=============================
Upstream: Npgsql, branch hotfix/10.0.4 (release v10.0.3 plus the SSPI
mutual-authentication fix), PostgreSQL License. The exact commits, the list of
ported paths, the projects that were NOT ported, and every modification made
during the port are recorded in THIRD-PARTY-NOTICES.txt - keep that file
current when you change ported code in a way it describes.

The port was produced mechanically by ~/ClaudeHome/port_codebrix_postgresclient.py
(rename + preprocessor resolution + file-scoped namespaces), then
~/ClaudeHome/strip_nrt_cs8632.py and ~/ClaudeHome/strip_null_forgiving.py, then
hand work. The decisions behind it are in
~/ClaudeHome/PLAN_codebrix_postgresclient_2026-10-07.txt. Nothing in this
repository depends on those files; read them before a re-port against a newer
upstream release.

PgSqlMapper is CodeBrix-original code. Its API is modeled on the same mapper
in CodeBrix.Sqlite (SqliteMapper); keep the two surfaces aligned.


NOTES
=====
    * Properties/PgSqlStrings.Designer.cs is maintained by hand (file-scoped,
      family layout). Do not let an IDE regenerate it from the .resx - the tool
      emits a block-scoped namespace. Add a resource string to both files.
    * Logging: every structured argument a [LoggerMessage] method takes must
      also appear in its Message template (SYSLIB1015), and plain ILogger calls
      must not pass arguments the template does not use (CA2017). The
      connection string is never logged. LogMessages.cs is the place to look.
    * Removed upstream-obsolete members (see THIRD-PARTY-NOTICES.txt) must not
      be reintroduced. ServerCompatibilityMode and LoadTableComposites were
      deliberately kept and un-obsoleted - they are the only connection-string
      route to the type-loading options, and the mock-server tests rely on
      ServerCompatibilityMode=NoTypeLoading.
    * Error and log message text says "PgSql" where upstream said "Npgsql".
      Links to the upstream documentation site were kept, because that
      documentation describes this library's behaviour; the one message that
      asked users to file bugs upstream points at this repository instead.
