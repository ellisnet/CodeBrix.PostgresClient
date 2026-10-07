using System.Linq;
using System.Threading.Tasks;
using CodeBrix.PostgresClient.Internal;
using SilverAssertions;
using Xunit;

namespace CodeBrix.PostgresClient.Tests; //was previously: Npgsql.Tests;

public class PostgresTypeTests : TestBase
{
    [Fact]
    public async Task @base()
    {
        //Arrange
        var databaseInfo = await GetDatabaseInfo();

        //Act
        var text = databaseInfo.BaseTypes.Single(a => a.Name == "text");

        //Assert
        text.DisplayName.Should().Be("text");
        text.Namespace.Should().Be("pg_catalog");
        text.FullName.Should().Be("pg_catalog.text");
    }

    [Fact]
    public async Task array()
    {
        //Arrange
        var databaseInfo = await GetDatabaseInfo();

        //Act
        var textArray = databaseInfo.ArrayTypes.Single(a => a.Name == "text[]");
        var text = databaseInfo.BaseTypes.Single(a => a.Name == "text");

        //Assert
        textArray.DisplayName.Should().Be("text[]");
        textArray.Namespace.Should().Be("pg_catalog");
        textArray.FullName.Should().Be("pg_catalog.text[]");
        textArray.Element.Should().BeSameAs(text);
        text.Array.Should().BeSameAs(textArray);
    }

    [Fact]
    public async Task range()
    {
        //Arrange
        var databaseInfo = await GetDatabaseInfo();

        //Act
        var intRange = databaseInfo.RangeTypes.Single(a => a.Name == "int4range");
        var integer = databaseInfo.BaseTypes.Single(a => a.Name == "integer");

        //Assert
        intRange.DisplayName.Should().Be("int4range");
        intRange.Namespace.Should().Be("pg_catalog");
        intRange.FullName.Should().Be("pg_catalog.int4range");
        intRange.Subtype.Should().BeSameAs(integer);
        integer.Range.Should().BeSameAs(intRange);
    }

    [Fact]
    public async Task multirange()
    {
        //Arrange
        await using (var conn = await OpenConnectionAsync())
            TestUtil.MinimumPgVersion(conn, "14.0", "Multirange types were introduced in PostgreSQL 14");

        var databaseInfo = await GetDatabaseInfo();

        //Act
        var intMultirange = databaseInfo.MultirangeTypes.Single(a => a.Name == "int4multirange");
        var intRange = databaseInfo.RangeTypes.Single(a => a.Name == "int4range");

        //Assert
        intMultirange.DisplayName.Should().Be("int4multirange");
        intMultirange.Namespace.Should().Be("pg_catalog");
        intMultirange.FullName.Should().Be("pg_catalog.int4multirange");
        intMultirange.Subrange.Should().BeSameAs(intRange);
        intRange.Multirange.Should().BeSameAs(intMultirange);
    }

    async Task<PgSqlDatabaseInfo> GetDatabaseInfo()
    {
        await using var conn = await OpenConnectionAsync();
        return conn.PgSqlDataSource.CurrentReloadableState.DatabaseInfo;
    }
}
