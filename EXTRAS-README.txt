================================================================================
EXTRAS-README: CodeBrix.PostgresClient
Samples, tools and other content in this repository that is not part of a NuGet
package
================================================================================

This repository ships no sample applications, demos or command-line tools.
Exactly one project is packable - src/CodeBrix.PostgresClient - and everything
else listed below exists only to build, test or document it. None of it is
included in the CodeBrix.PostgresClient.PostgreSqlLicenseForever package.

For runnable, compilable usage of the library, read the test project: the
"WORKING EXAMPLES ON GITHUB" section of AGENT-README.txt maps each feature area
to the test file that exercises it.


SOURCE GENERATOR PROJECT
========================
    src/CodeBrix.PostgresClient.SourceGenerators/

A Roslyn source generator that the library project loads as an analyzer at
build time. It writes the keyword-dispatch half of PgSqlConnectionStringBuilder
(PgSqlConnectionStringBuilder.Generated.cs) from the
PgSqlConnectionStringBuilder.snbtxt template, using CodeBrix.Templating. It is
not referenced at run time and is not packed. See MAINTAINER-README.txt
(THE SOURCE GENERATOR).


TEST PROJECT
============
    tests/CodeBrix.PostgresClient.Tests/

xUnit v3. The database tests run against a throw-away PostgreSQL Docker
container that the test run starts on first use and removes at the end; see
MAINTAINER-README.txt (TESTING).

    tests/CodeBrix.PostgresClient.Tests/Certificates/

Test-only SSL certificates (a CA, a server certificate and its private key)
that the test container's PostgreSQL server uses. They secure nothing real; do
not reuse them anywhere else.
