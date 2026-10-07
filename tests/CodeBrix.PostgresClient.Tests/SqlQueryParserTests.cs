using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using SilverAssertions;
using Xunit;

namespace CodeBrix.PostgresClient.Tests; //was previously: Npgsql.Tests;

public class SqlQueryParserTests
{
    [Fact]
    public void parameter_simple()
    {
        //Arrange
        var parameters = new PgSqlParameter[] { new(":p1", "foo"), new(":p2", "foo") };

        //Act
        var result = ParseCommand("SELECT :p1, :p2", parameters).Single();

        //Assert
        result.FinalCommandText.Should().Be("SELECT $1, $2");
        result.PositionalParameters.Should().Equal(parameters);
    }

    [Fact]
    public void parameter_name_with_dot()
    {
        //Arrange
        var p = new PgSqlParameter(":a.parameter", "foo");

        //Act
        var results = ParseCommand("INSERT INTO data (field_char5) VALUES (:a.parameter)", p);

        //Assert
        results.Single().PositionalParameters.Single().Should().BeSameAs(p);
    }

    // Checks several scenarios in which the SQL is supposed to pass untouched
    [Theory]
    [InlineData(@"SELECT to_tsvector('fat cats ate rats') @@ to_tsquery('cat & rat')")]
    [InlineData(@"SELECT 'cat'::tsquery @> 'cat & rat'::tsquery")]
    [InlineData(@"SELECT 'cat'::tsquery <@ 'cat & rat'::tsquery")]
    [InlineData(@"SELECT 'b''la'")]
    [InlineData(@"SELECT 'type(''m.response'')#''O''%'")]
    [InlineData(@"SELECT 'abc'':str''a:str'")]
    [InlineData(@"SELECT 1 FROM "":str""")]
    [InlineData(@"SELECT 1 FROM 'yo'::str")]
    [InlineData("SELECT $ÿabc0$literal string :str :int$ÿabc0 $ÿabc0$")]
    [InlineData("SELECT $$:str$$")]
    public void untouched(string sql)
    {
        //Act
        var results = ParseCommand(sql, new PgSqlParameter(":param", "foo"));

        //Assert
        results.Single().FinalCommandText.Should().Be(sql);
        results.Single().PositionalParameters.Should().BeEmpty();
    }

    [Theory]
    [InlineData(@"SELECT 1<:param")]
    [InlineData(@"SELECT 1>:param")]
    [InlineData(@"SELECT 1<>:param")]
    [InlineData("SELECT--comment\r:param")]
    public void parameter_gets_bound(string sql)
    {
        //Arrange
        var p = new PgSqlParameter(":param", "foo");

        //Act
        var results = ParseCommand(sql, p);

        //Assert
        results.Single().PositionalParameters.Single().Should().BeSameAs(p);
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/1177")]
    public void parameter_gets_bound_non_ascii()
    {
        //Arrange
        var p = new PgSqlParameter("漢字", "foo");

        //Act
        var results = ParseCommand("SELECT @漢字", p);

        //Assert
        results.Single().PositionalParameters.Single().Should().BeSameAs(p);
    }

    [Theory]
    [InlineData(@"SELECT e'ab\'c:param'")]
    [InlineData(@"SELECT/*/* -- nested comment :int /*/* *//*/ **/*/*/*/1")]
    [InlineData(@"SELECT 1,
-- Comment, @param and also :param
2")]
    public void parameter_does_not_get_bound(string sql)
    {
        //Arrange
        var p = new PgSqlParameter(":param", "foo");

        //Act
        var results = ParseCommand(sql, p);

        //Assert
        results.Single().PositionalParameters.Should().BeEmpty();
    }

    [Fact]
    public void non_conforming_string()
    {
        //Act
        var result = ParseCommand(@"SELECT 'abc\':str''a:str'").Single();

        //Assert
        result.FinalCommandText.Should().Be(@"SELECT 'abc\':str''a:str'");
        result.PositionalParameters.Should().BeEmpty();
    }

    [Fact]
    public void multiquery_with_parameters()
    {
        //Arrange
        var parameters = new PgSqlParameter[]
        {
            new("p1", DbType.String),
            new("p2", DbType.String),
            new("p3", DbType.String),
        };

        //Act
        var results = ParseCommand("SELECT @p3, @p1; SELECT @p2, @p3", parameters);

        //Assert
        results.Should().HaveCount(2);
        results[0].FinalCommandText.Should().Be("SELECT $1, $2");
        results[0].PositionalParameters[0].Should().BeSameAs(parameters[2]);
        results[0].PositionalParameters[1].Should().BeSameAs(parameters[0]);
        results[1].FinalCommandText.Should().Be("SELECT $1, $2");
        results[1].PositionalParameters[0].Should().BeSameAs(parameters[1]);
        results[1].PositionalParameters[1].Should().BeSameAs(parameters[2]);
    }

    [Fact]
    public void no_output_parameters()
    {
        //Arrange
        var p = new PgSqlParameter("p", DbType.String) { Direction = ParameterDirection.Output };

        //Act
        var act = () => ParseCommand("SELECT @p", p);

        //Assert
        act.Should().Throw<Exception>();
    }

    [Fact]
    public void missing_parameter_is_ignored()
    {
        //Act
        var results = ParseCommand("SELECT @p; SELECT 1");

        //Assert
        results[0].FinalCommandText.Should().Be("SELECT @p");
        results[1].FinalCommandText.Should().Be("SELECT 1");
        results[0].PositionalParameters.Should().BeEmpty();
        results[1].PositionalParameters.Should().BeEmpty();
    }

    [Fact]
    public void consecutive_semicolons()
    {
        //Act
        var results = ParseCommand(";;SELECT 1");

        //Assert
        results.Should().HaveCount(3);
        results[0].FinalCommandText.Should().BeEmpty();
        results[1].FinalCommandText.Should().BeEmpty();
        results[2].FinalCommandText.Should().Be("SELECT 1");
    }

    [Fact]
    public void trailing_semicolon()
    {
        //Act
        var results = ParseCommand("SELECT 1;");

        //Assert
        results.Should().HaveCount(1);
        results[0].FinalCommandText.Should().Be("SELECT 1");
    }

    [Fact]
    public void empty()
    {
        //Act
        var results = ParseCommand("");

        //Assert
        results.Should().HaveCount(1);
        results[0].FinalCommandText.Should().BeEmpty();
    }

    [Fact]
    public void semicolon_in_parentheses()
    {
        //Act
        var results = ParseCommand("CREATE OR REPLACE RULE test AS ON UPDATE TO test DO (SELECT 1; SELECT 1)");

        //Assert
        results.Should().HaveCount(1);
        results[0].FinalCommandText.Should().Be("CREATE OR REPLACE RULE test AS ON UPDATE TO test DO (SELECT 1; SELECT 1)");
    }

    [Fact]
    public void semicolon_after_parentheses()
    {
        //Act
        var results = ParseCommand("CREATE OR REPLACE RULE test AS ON UPDATE TO test DO (SELECT 1); SELECT 1");

        //Assert
        results.Should().HaveCount(2);
        results[0].FinalCommandText.Should().Be("CREATE OR REPLACE RULE test AS ON UPDATE TO test DO (SELECT 1)");
        results[1].FinalCommandText.Should().Be("SELECT 1");
    }

    [Fact]
    public void reduce_number_of_statements()
    {
        //Arrange
        var parser = new SqlQueryParser();
        var cmd = new PgSqlCommand("SELECT 1; SELECT 2");

        parser.ParseRawQuery(cmd);
        cmd.InternalBatchCommands.Should().HaveCount(2);

        cmd.CommandText = "SELECT 1";
        parser.ParseRawQuery(cmd);
        cmd.InternalBatchCommands.Should().HaveCount(1);
    }

    #region Setup / Teardown / Utils

    List<PgSqlBatchCommand> ParseCommand(string sql, params PgSqlParameter[] parameters)
        => ParseCommand(sql, parameters, standardConformingStrings: true);

    List<PgSqlBatchCommand> ParseCommand(string sql, PgSqlParameter[] parameters, bool standardConformingStrings)
    {
        var cmd = new PgSqlCommand(sql);
        cmd.Parameters.AddRange(parameters);
        var parser = new SqlQueryParser();
        parser.ParseRawQuery(cmd, standardConformingStrings);
        return cmd.InternalBatchCommands;
    }

    #endregion
}
