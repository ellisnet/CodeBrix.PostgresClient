using System;
using SilverAssertions;
using Xunit;

namespace CodeBrix.PostgresClient.Tests; //was previously: Npgsql.Tests;

public class PgPassEntryTests
{
    [Fact]
    public void parses_well_formed_entry()
    {
        //Arrange
        var input = "test:1234:test2:test3:test4";

        //Act
        var entry = PgPassFile.Entry.Parse(input);

        //Assert
        entry.Should().NotBeNull();
        entry.Host.Should().Be("test");
        entry.Port.Should().Be(1234);
        entry.Database.Should().Be("test2");
        entry.Username.Should().Be("test3");
        entry.Password.Should().Be("test4");
    }

    [Theory]
    [InlineData("test:1234:test2:test3")]
    [InlineData("test:myport:test2:test3:test4")]
    public void bad_entry_throws(string input)
    {
        //Act
        Func<object> createDelegate = () => PgPassFile.Entry.Parse(input);

        //Assert
        createDelegate.Should().ThrowExactly<FormatException>();
    }

    [Fact]
    public void escaped_characters()
    {
        //Arrange
        var input = "t\\:est:1234:test2:test3:test\\\\4";

        //Act
        var entry = PgPassFile.Entry.Parse(input);

        //Assert
        entry.Should().NotBeNull();
        entry.Host.Should().Be("t:est");
        entry.Port.Should().Be(1234);
        entry.Database.Should().Be("test2");
        entry.Username.Should().Be("test3");
        entry.Password.Should().Be("test\\4");
    }

    [Fact]
    public void match_true_for_exact_match()
    {
        //Arrange
        var input = "test:1234:test2:test3:test4";
        var entry = PgPassFile.Entry.Parse(input);

        //Act
        var isMatch = entry.IsMatch("test", 1234, "test2", "test3");

        //Assert
        isMatch.Should().BeTrue();
    }

    [Fact]
    public void match_true_for_wildcard_entry()
    {
        //Arrange
        var input = "*:1234:test2:test3:test4";
        var entry = PgPassFile.Entry.Parse(input);

        //Act
        var isMatch = entry.IsMatch("test", 1234, "test2", "test3");

        //Assert
        isMatch.Should().BeTrue();
    }

    [Fact]
    public void match_true_for_wildcard_query()
    {
        //Arrange
        var input = "test:1234:test2:test3:test4";
        var entry = PgPassFile.Entry.Parse(input);

        //Act
        var isMatch = entry.IsMatch(null, 1234, "test2", "test3");

        //Assert
        isMatch.Should().BeTrue();
    }

    [Fact]
    public void match_false_for_bad_query()
    {
        //Arrange
        var input = "test:1234:test2:test3:test4";
        var entry = PgPassFile.Entry.Parse(input);

        //Act
        var isMatch = entry.IsMatch("notamatch", 1234, "test2", "test3");

        //Assert
        isMatch.Should().BeFalse();
    }

    [Fact]
    public void match_true_for_null_query()
    {
        //Arrange
        var input = "test:1234:test2:test3:test4";
        var entry = PgPassFile.Entry.Parse(input);

        //Act
        var isMatch = entry.IsMatch(null, 1234, "test2", "test3");

        //Assert
        isMatch.Should().BeTrue();
    }
}
