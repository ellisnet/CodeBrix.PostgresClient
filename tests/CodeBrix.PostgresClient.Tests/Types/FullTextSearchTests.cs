using System;
using System.Threading.Tasks;
using CodeBrix.PostgresClient.PgSqlTypes;
using CodeBrix.PostgresClient.Properties;
using SilverAssertions;
using Xunit;

namespace CodeBrix.PostgresClient.Tests.Types; //was previously: Npgsql.Tests.Types;

public abstract class FullTextSearchTests(MultiplexingMode multiplexingMode) : MultiplexingTestBase(multiplexingMode)
{
    [Fact]
    public Task tsvector()
        => AssertType(
            PgSqlTsVector.Parse("'1' '2' 'a':24,25A,26B,27,28,12345C 'b' 'c' 'd'"),
            "'1' '2' 'a':24,25A,26B,27,28,12345C 'b' 'c' 'd'",
            "tsvector",
            PgSqlDbType.TsVector);

    public static TheoryData<string, PgSqlTsQuery> TsQueryTestCases() => new()
    {
        {
            "'a'",
            new PgSqlTsQueryLexeme("a")
        },
        {
            "!'a'",
            new PgSqlTsQueryNot(
                new PgSqlTsQueryLexeme("a"))
        },
        {
            "'a' | 'b'",
            new PgSqlTsQueryOr(
                new PgSqlTsQueryLexeme("a"),
                new PgSqlTsQueryLexeme("b"))
        },
        {
            "'a' & 'b'",
            new PgSqlTsQueryAnd(
                new PgSqlTsQueryLexeme("a"),
                new PgSqlTsQueryLexeme("b"))
        },
        {
            "'a' <-> 'b'",
            new PgSqlTsQueryFollowedBy(
                new PgSqlTsQueryLexeme("a"), 1, new PgSqlTsQueryLexeme("b"))
        }
    };

    [Theory]
    [MemberData(nameof(TsQueryTestCases))]
    public Task tsquery(string sqlLiteral, PgSqlTsQuery query)
        => AssertType(query, sqlLiteral, "tsquery", PgSqlDbType.TsQuery);

    [Fact]
    public async Task full_text_search_not_supported_by_default_on_PgSqlSlimSourceBuilder()
    {
        //Arrange
        var errorMessage = string.Format(
            PgSqlStrings.FullTextSearchNotEnabled,
            nameof(PgSqlSlimDataSourceBuilder.EnableFullTextSearch),
            nameof(PgSqlSlimDataSourceBuilder));

        var dataSourceBuilder = new PgSqlSlimDataSourceBuilder(ConnectionString);
        await using var dataSource = dataSourceBuilder.Build();

        //Act
        var exception = await AssertTypeUnsupportedRead<PgSqlTsQuery, InvalidCastException>("a", "tsquery", dataSource);
        exception.InnerException.Should().BeAssignableTo<NotSupportedException>();
        exception.InnerException.Message.Should().Be(errorMessage);

        exception = await AssertTypeUnsupportedWrite<PgSqlTsQuery, InvalidCastException>(new PgSqlTsQueryLexeme("a"), pgTypeName: null, dataSource);
        exception.InnerException.Should().BeAssignableTo<NotSupportedException>();
        exception.InnerException.Message.Should().Be(errorMessage);

        exception = await AssertTypeUnsupportedRead<PgSqlTsVector, InvalidCastException>("1", "tsvector", dataSource);
        exception.InnerException.Should().BeAssignableTo<NotSupportedException>();
        exception.InnerException.Message.Should().Be(errorMessage);

        exception = await AssertTypeUnsupportedWrite<PgSqlTsVector, InvalidCastException>(PgSqlTsVector.Parse("'1'"), pgTypeName: null, dataSource);
        exception.InnerException.Should().BeAssignableTo<NotSupportedException>();
        exception.InnerException.Message.Should().Be(errorMessage);
    }

    [Fact]
    public async Task PgSqlSlimSourceBuilder_EnableFullTextSearch()
    {
        //Arrange
        var dataSourceBuilder = new PgSqlSlimDataSourceBuilder(ConnectionString);
        dataSourceBuilder.EnableFullTextSearch();
        await using var dataSource = dataSourceBuilder.Build();

        //Assert
        await AssertType<PgSqlTsQuery>(new PgSqlTsQueryLexeme("a"), "'a'", "tsquery", PgSqlDbType.TsQuery);
        await AssertType(PgSqlTsVector.Parse("'1'"), "'1'", "tsvector", PgSqlDbType.TsVector);
    }
}

public sealed class FullTextSearchTests_NonMultiplexing() : FullTextSearchTests(MultiplexingMode.NonMultiplexing);
public sealed class FullTextSearchTests_Multiplexing() : FullTextSearchTests(MultiplexingMode.Multiplexing);
