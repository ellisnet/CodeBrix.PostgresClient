using System;
using System.IO;
using System.Linq;
using SilverAssertions;
using Xunit;

namespace CodeBrix.PostgresClient.Tests; //was previously: Npgsql.Tests;

public class PgPassFileTests : IClassFixture<PgPassFileTests.PgPassFileFixture>
{
    readonly string _pgpassFile;

    public PgPassFileTests(PgPassFileFixture fixture) => _pgpassFile = fixture.PgPassFile;

    [Fact]
    public void should_parse_all_entries()
    {
        //Arrange
        var file = new PgPassFile(_pgpassFile);

        //Act
        var entries = file.Entries.ToList();

        //Assert
        entries.Count.Should().Be(3);
    }

    [Fact]
    public void should_find_first_entry_when_multiple_match()
    {
        //Arrange
        var file = new PgPassFile(_pgpassFile);

        //Act
        var entry = file.GetFirstMatchingEntry("testhost");

        //Assert
        entry.Password.Should().Be("testpass");
    }

    [Fact]
    public void should_find_default_for_no_matches()
    {
        //Arrange
        var file = new PgPassFile(_pgpassFile);

        //Act
        var entry = file.GetFirstMatchingEntry("notarealhost");

        //Assert
        entry.Password.Should().Be("defaultpass");
    }

    public sealed class PgPassFileFixture : IDisposable
    {
        public string PgPassFile { get; } = Path.GetTempFileName();

        public PgPassFileFixture()
        {
            // set up pgpass file with fake content that can be used for this test
            const string content = @"testhost:1234:testdatabase:testuser:testpass
testhost:*:*:*:testdefaultpass
# helpful comment goes here
*:*:*:*:defaultpass";

            File.WriteAllText(PgPassFile, content);
        }

        public void Dispose()
        {
            if (File.Exists(PgPassFile))
                File.Delete(PgPassFile);
        }
    }
}
