using System;
using System.Data;
using System.Threading.Tasks;
using CodeBrix.PostgresClient.Internal;
using CodeBrix.PostgresClient.Internal.Converters;
using CodeBrix.PostgresClient.Internal.Postgres;
using CodeBrix.PostgresClient.PgSqlTypes;
using CodeBrix.PostgresClient.Tests.Support;
using CodeBrix.PostgresClient.TypeMapping;
using SilverAssertions;
using Xunit;
using static CodeBrix.PostgresClient.Tests.TestUtil;

namespace CodeBrix.PostgresClient.Tests; //was previously: Npgsql.Tests;

public class TypeMapperTests : TestBase
{
    [Fact]
    public async Task ReloadTypes_across_connections_in_data_source()
    {
        await using var adminConnection = await OpenConnectionAsync();
        var type = await GetTempTypeName(adminConnection);
        // Note that we don't actually create the type in the database at this point; we want to exercise the type being created later,
        // via the data source.

        var dataSourceBuilder = CreateDataSourceBuilder();
        dataSourceBuilder.MapEnum<Mood>(type);
        await using var dataSource = dataSourceBuilder.Build();
        await using var connection1 = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using var connection2 = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        await connection1.ExecuteNonQueryAsync($"CREATE TYPE {type} AS ENUM ('sad', 'ok', 'happy')", cancellationToken: TestContext.Current.CancellationToken);
        await connection1.ReloadTypesAsync(TestContext.Current.CancellationToken);

        // The data source type mapper has been replaced and connection1 should have the new mapper, but connection2 should retain the older
        // type mapper - where there's no mapping - as long as it's still open
        await Assert.ThrowsAsync<InvalidCastException>(async () => await connection2.ExecuteScalarAsync($"SELECT 'happy'::{type}", cancellationToken: TestContext.Current.CancellationToken));
        await FluentActions.Awaiting(async () => await connection1.ExecuteScalarAsync($"SELECT 'happy'::{type}", cancellationToken: TestContext.Current.CancellationToken))
            .Should().NotThrowAsync();

        // Close connection2 and reopen to make sure it picks up the new type and mapping from the data source
        var connId = connection2.ProcessID;
        await connection2.CloseAsync();
        await connection2.OpenAsync(TestContext.Current.CancellationToken);
        connection2.ProcessID.Should().Be(connId, "the same connector should come back");

        await FluentActions.Awaiting(async () => await connection2.ExecuteScalarAsync($"SELECT 'happy'::{type}", cancellationToken: TestContext.Current.CancellationToken))
            .Should().NotThrowAsync();
    }

    [Fact]
    public async Task string_to_citext()
    {
        //Arrange
        await using var adminConnection = await OpenConnectionAsync();
        await EnsureExtensionAsync(adminConnection, "citext");

        var dataSourceBuilder = CreateDataSourceBuilder();
        dataSourceBuilder.AddTypeInfoResolverFactory(new CitextToStringTypeHandlerResolverFactory());
        await using var dataSource = dataSourceBuilder.Build();
        await using var connection = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        await using var command = new PgSqlCommand("SELECT @p = 'hello'::citext", connection);
        command.Parameters.AddWithValue("p", "HeLLo");
        //Assert
        command.ExecuteScalar().Should().Be(true);
    }

    [Fact]
    public async Task string_to_citext_with_db_type_string()
    {
        //Arrange
        await using var adminConnection = await OpenConnectionAsync();
        await EnsureExtensionAsync(adminConnection, "citext");

        var dataSourceBuilder = CreateDataSourceBuilder();
        ((IPgSqlTypeMapper)dataSourceBuilder).AddDbTypeResolverFactory(new ForceStringToCitextResolverFactory());
        await using var dataSource = dataSourceBuilder.Build();
        await using var connection = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        await using var command = new PgSqlCommand("SELECT @p = 'hello'::citext", connection);
        var parameter = new PgSqlParameter("p", DbType.String)
        {
            Value = "HeLLo"
        };
        command.Parameters.Add(parameter);

        //Assert
        command.ExecuteScalar().Should().Be(true);
        parameter.DbType.Should().Be(DbType.String);
        parameter.PgSqlDbType.Should().Be(PgSqlDbType.Citext);
        parameter.DataTypeName.Should().Be("citext");
    }

    [Fact]
    public async Task guid_to_custom_type()
    {
        //Arrange
        await using var adminConnection = await OpenConnectionAsync();
        var type = await GetTempTypeName(adminConnection);

        var dataSourceBuilder = CreateDataSourceBuilder();
        dataSourceBuilder.AddTypeInfoResolverFactory(new GuidTextConverterFactory(type));
        ((IPgSqlTypeMapper)dataSourceBuilder).AddDbTypeResolverFactory(new GuidTextDbTypeResolverFactory(type));
        await using var dataSource = dataSourceBuilder.Build();
        await using var connection = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        await connection.ExecuteNonQueryAsync($"CREATE TYPE {type}", cancellationToken: TestContext.Current.CancellationToken);
        await connection.ExecuteNonQueryAsync($"""
            -- Input: cstring -> Custom type
            CREATE FUNCTION {type}_in(cstring)
            RETURNS {type}
            AS 'textin'
            LANGUAGE internal IMMUTABLE STRICT;

            -- Output: Custom type -> cstring
            CREATE FUNCTION {type}_out({type})
            RETURNS cstring
            AS 'textout'
            LANGUAGE internal IMMUTABLE STRICT;

            -- 3️⃣ Create wrappers for binary I/O
            CREATE FUNCTION {type}_recv(internal)
            RETURNS {type}
            AS 'textrecv'
            LANGUAGE internal IMMUTABLE STRICT;

            CREATE FUNCTION {type}_send({type})
            RETURNS bytea
            AS 'textsend'
            LANGUAGE internal IMMUTABLE STRICT;
        """, cancellationToken: TestContext.Current.CancellationToken);

        await connection.ExecuteNonQueryAsync($"""
            CREATE TYPE {type} (
                internallength = variable,
                input = {type}_in,
                output = {type}_out,
                receive = {type}_recv,
                send = {type}_send,
                alignment = int4
            );
            CREATE CAST ({type} AS text) WITH INOUT AS IMPLICIT;
            """, cancellationToken: TestContext.Current.CancellationToken);
        await connection.ReloadTypesAsync(TestContext.Current.CancellationToken);

        var guid = Guid.NewGuid();
        await using var command = new PgSqlCommand($"SELECT @p::text = '{guid}'", connection);
        var parameter = new PgSqlParameter("p", DbType.Guid)
        {
            Value = guid
        };
        command.Parameters.Add(parameter);

        //Assert
        command.ExecuteScalar().Should().Be(true);
        parameter.DbType.Should().Be(DbType.Guid);
        parameter.PgSqlDbType.Should().Be(PgSqlDbType.Unknown);
        parameter.DataTypeName.Should().Be(type);
    }

    #region Support

    class CitextToStringTypeHandlerResolverFactory : PgTypeInfoResolverFactory
    {
        public override IPgTypeInfoResolver CreateResolver() => new Resolver();
        public override IPgTypeInfoResolver CreateArrayResolver() => null;

        sealed class Resolver : IPgTypeInfoResolver
        {
            public PgTypeInfo GetTypeInfo(Type type, DataTypeName? dataTypeName, PgSerializerOptions options)
            {
                if (type == typeof(string) || dataTypeName?.UnqualifiedName == "citext")
                    if (options.DatabaseInfo.TryGetPostgresTypeByName("citext", out var pgType))
                        return new(options, new StringTextConverter(options.TextEncoding), options.ToCanonicalTypeId(pgType));

                return null;
            }
        }

    }

    class ForceStringToCitextResolverFactory : DbTypeResolverFactory
    {
        public override IDbTypeResolver CreateDbTypeResolver(PgSqlDatabaseInfo databaseInfo) => new DbTypeResolver();

        sealed class DbTypeResolver : IDbTypeResolver
        {
            public string GetDataTypeName(DbType dbType, Type type)
            {
                if (dbType == DbType.String)
                    return "citext";

                return null;
            }

            public DbType? GetDbType(DataTypeName dataTypeName)
            {
                if (dataTypeName.UnqualifiedName == "citext")
                    return DbType.String;

                return null;
            }
        }
    }

    class GuidTextConverterFactory(string typeName) : PgTypeInfoResolverFactory
    {
        public override IPgTypeInfoResolver CreateArrayResolver() => null;
        public override IPgTypeInfoResolver CreateResolver() => new GuidTextTypeInfoResolver(typeName);

        sealed class GuidTextTypeInfoResolver(string typeName) : IPgTypeInfoResolver
        {
            public PgTypeInfo GetTypeInfo(Type type, DataTypeName? dataTypeName, PgSerializerOptions options)
            {
                if (type == typeof(Guid) || dataTypeName?.UnqualifiedName == typeName)
                    if (options.DatabaseInfo.TryGetPostgresTypeByName(typeName, out var pgType))
                        return new(options, new GuidTextConverter(options.TextEncoding), options.ToCanonicalTypeId(pgType));

                return null;
            }
        }

        sealed class GuidTextConverter(System.Text.Encoding encoding) : StringBasedTextConverter<Guid>(encoding)
        {
            public override bool CanConvert(DataFormat format, out BufferRequirements bufferRequirements)
            {
                bufferRequirements = BufferRequirements.None;
                return format is DataFormat.Text;
            }
            protected override Guid ConvertFrom(string value) => Guid.Parse(value);
            protected override ReadOnlyMemory<char> ConvertTo(Guid value) => value.ToString().AsMemory();
        }
    }

    class GuidTextDbTypeResolverFactory(string typeName) : DbTypeResolverFactory
    {
        public override IDbTypeResolver CreateDbTypeResolver(PgSqlDatabaseInfo databaseInfo) => new DbTypeResolver(typeName);

        sealed class DbTypeResolver(string typeName) : IDbTypeResolver
        {
            public string GetDataTypeName(DbType dbType, Type type)
            {
                if (dbType == DbType.Guid)
                    return typeName;
                return null;
            }

            public DbType? GetDbType(DataTypeName dataTypeName)
            {
                if (dataTypeName == typeName)
                    return DbType.Guid;
                return null;
            }
        }
    }

    enum Mood { Sad, Ok, Happy }

    #endregion Support
}

[Collection(NonParallelCollection.Name)] // Drops global citext extension.
public class TypeMapperTestsNonParallel : TestBase
{
    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/4582")]
    public async Task type_in_non_default_schema()
    {
        await using var conn = await OpenConnectionAsync();

        var schemaName = await CreateTempSchema(conn);

        await conn.ExecuteNonQueryAsync(@$"
DROP EXTENSION IF EXISTS citext;
CREATE EXTENSION citext SCHEMA ""{schemaName}""", cancellationToken: TestContext.Current.CancellationToken);

        try
        {
            await conn.ReloadTypesAsync(TestContext.Current.CancellationToken);

            var tableName = await CreateTempTable(conn, $"created_by {schemaName}.citext NOT NULL");

            const string expected = "SomeValue";
            await conn.ExecuteNonQueryAsync($"INSERT INTO \"{tableName}\" VALUES('{expected}')", cancellationToken: TestContext.Current.CancellationToken);

            var value = (string)await conn.ExecuteScalarAsync($"SELECT created_by FROM \"{tableName}\" LIMIT 1", cancellationToken: TestContext.Current.CancellationToken);
            value.Should().Be(expected);
        }
        finally
        {
            await conn.ExecuteNonQueryAsync(@"DROP EXTENSION citext CASCADE", cancellationToken: TestContext.Current.CancellationToken);
        }
    }
}
