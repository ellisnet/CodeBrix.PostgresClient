using System;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using CodeBrix.PostgresClient.PgSqlTypes;
using CodeBrix.PostgresClient.Tests.Support;
using SilverAssertions;
using Xunit;

namespace CodeBrix.PostgresClient.Tests; //was previously: Npgsql.Tests;

public abstract class PgSqlParameterCollectionTests : IDisposable
{
    readonly CompatMode _compatMode;
    const int LookupThreshold = PgSqlParameterCollection.LookupThreshold;
#if DEBUG
    readonly bool _previousTwoPassCompatMode;
#endif

    [Fact]
    public void can_only_add_PgSqlParameter()
    {
        //Arrange
        using var command = new PgSqlCommand();

        //Assert
        Assert.Throws<InvalidCastException>(() => command.Parameters.Add("hello"));
        Assert.Throws<InvalidCastException>(() => command.Parameters.Add(new SomeOtherDbParameter()));
        Assert.Throws<ArgumentNullException>(() => command.Parameters.Add(null));
    }

    /// <summary>
    /// Test which validates that Clear() indeed cleans up the parameters in a command so they can be added to other commands safely.
    /// </summary>
    [Fact]
    public void Clear()
    {
        //Arrange
        var p = new PgSqlParameter();
        var c1 = new PgSqlCommand();
        var c2 = new PgSqlCommand();
        c1.Parameters.Add(p);
        c1.Parameters.Count.Should().Be(1);
        c2.Parameters.Count.Should().Be(0);

        //Act
        c1.Parameters.Clear();
        c1.Parameters.Count.Should().Be(0);
        c2.Parameters.Add(p);

        //Assert
        c1.Parameters.Count.Should().Be(0);
        c2.Parameters.Count.Should().Be(1);
    }

    [Fact]
    public void hash_lookup_parameter_rename_bug()
    {
        //Arrange
        if (_compatMode == CompatMode.TwoPass)
            return;

        using var command = new PgSqlCommand();
        // Put plenty of parameters in the collection to turn on hash lookup functionality.
        for (var i = 0; i < LookupThreshold; i++)
        {
            command.Parameters.AddWithValue(string.Format("p{0:00}", i + 1), PgSqlDbType.Text, string.Format("String parameter value {0}", i + 1));
        }

        // Make sure hash lookup is generated.
        command.Parameters["p03"].ParameterName.Should().Be("p03");

        //Act
        // Rename the target parameter.
        command.Parameters["p03"].ParameterName = "a_new_name";

        //Assert
        // Try to exploit the hash lookup bug.
        // If the bug exists, the hash lookups will be out of sync with the list, and be unable
        // to find the parameter by its new name.
        command.Parameters.IndexOf("a_new_name").Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    [IssueLink("https://github.com/npgsql/npgsql/issues/6067")]
    public void hash_lookup_unnamed_parameter_rename_bug()
    {
        //Arrange
        if (_compatMode == CompatMode.TwoPass)
            return;

        using var command = new PgSqlCommand();

        //Act
        for (var i = 0; i < 3; i++)
        {
            // Put plenty of parameters in the collection to turn on hash lookup functionality.
            for (var j = 0; j < LookupThreshold; j++)
            {
                // Create and add an unnamed parameter before renaming it
                var parameter = command.CreateParameter();
                command.Parameters.Add(parameter);
                parameter.ParameterName = $"{j}";
            }

            //Assert
            // Make sure hash lookup is generated.
            command.Parameters["3"].ParameterName.Should().Be("3");

            // Remove all parameters to clear hash lookup
            command.Parameters.Clear();
        }
    }

    [Theory]
    [InlineData(LookupThreshold)]
    [InlineData(LookupThreshold - 2)]
    public void Remove_duplicate_parameter(int count)
    {
        //Arrange
        if (_compatMode == CompatMode.OnePass)
            return;

        using var command = new PgSqlCommand();
        // Put plenty of parameters in the collection to turn on hash lookup functionality.
        for (var i = 0; i < count; i++)
        {
            command.Parameters.AddWithValue(string.Format("p{0:00}", i + 1), PgSqlDbType.Text,
                string.Format("String parameter value {0}", i + 1));
        }

        // Make sure lookup is generated.
        command.Parameters["p02"].ParameterName.Should().Be("p02");

        //Act
        // Add uppercased version causing a list to be created.
        command.Parameters.AddWithValue("P02", PgSqlDbType.Text, "String parameter value 2");

        // Remove the original parameter by its name causing the multivalue to use a single value again.
        command.Parameters.Remove(command.Parameters["p02"]);

        //Assert
        // Test whether we can still find the last added parameter, and if its index is correctly shifted in the lookup.
        command.Parameters.IndexOf("p02").Should().Be(count - 1);
        command.Parameters.IndexOf("P02").Should().Be(count - 1);
        // And finally test whether other parameters were also correctly shifted.
        command.Parameters.IndexOf("p03").Should().Be(1);
    }

    [Theory]
    [InlineData(LookupThreshold)]
    [InlineData(LookupThreshold - 2)]
    public void Remove_parameter(int count)
    {
        //Arrange
        using var command = new PgSqlCommand();
        // Put plenty of parameters in the collection to turn on hash lookup functionality.
        for (var i = 0; i < count; i++)
        {
            command.Parameters.AddWithValue(string.Format("p{0:00}", i + 1), PgSqlDbType.Text,
                string.Format("String parameter value {0}", i + 1));
        }

        //Act
        // Remove the parameter by its name
        command.Parameters.Remove(command.Parameters["p02"]);

        //Assert
        // Make sure we cannot find it, also not case insensitively.
        command.Parameters.IndexOf("p02").Should().Be(-1);
        command.Parameters.IndexOf("P02").Should().Be(-1);
    }

    [Theory]
    [InlineData(LookupThreshold)]
    [InlineData(LookupThreshold - 2)]
    public void Remove_case_differing_parameter(int count)
    {
        //Arrange
        // We add two case-differing parameters which will match as well, before adding the others.
        using var command = new PgSqlCommand();
        command.Parameters.Add(new PgSqlParameter("PP0", 1));
        command.Parameters.Add(new PgSqlParameter("Pp0", 1));
        for (var i = 0; i < count - 2; i++)
            command.Parameters.Add(new PgSqlParameter($"pp{i}", i));

        //Act
        // Removing Pp0.
        command.Parameters.RemoveAt(1);

        //Assert
        // Exact match to pp0 or case insensitive match to PP0 depending on mode.
        command.Parameters.IndexOf("pp0").Should().Be(_compatMode == CompatMode.TwoPass ? 1 : 0);
        // Exact match to PP0.
        command.Parameters.IndexOf("PP0").Should().Be(0);
        // Case insensitive match to PP0.
        command.Parameters.IndexOf("Pp0").Should().Be(0);
    }

    [Theory]
    [InlineData(LookupThreshold)]
    [InlineData(LookupThreshold - 2)]
    public void correct_index_returned_for_duplicate_ParameterName(int count)
    {
        //Arrange
        if (_compatMode == CompatMode.OnePass)
            return;

        using var command = new PgSqlCommand();
        // Put plenty of parameters in the collection to turn on hash lookup functionality.
        for (var i = 0; i < count; i++)
        {
            command.Parameters.AddWithValue(string.Format("parameter{0:00}", i + 1), PgSqlDbType.Text, string.Format("String parameter value {0}", i + 1));
        }

        // Make sure lookup is generated.
        command.Parameters["parameter02"].ParameterName.Should().Be("parameter02");

        //Act
        // Add uppercased version.
        command.Parameters.AddWithValue("Parameter02", PgSqlDbType.Text, "String parameter value 2");

        // Insert another case insensitive before the original.
        command.Parameters.Insert(0, new PgSqlParameter("ParameteR02", PgSqlDbType.Text) { Value = "String parameter value 2" });

        //Assert
        // Try to find the exact index.
        command.Parameters.IndexOf("parameter02").Should().Be(2);
        command.Parameters.IndexOf("Parameter02").Should().Be(command.Parameters.Count - 1);
        command.Parameters.IndexOf("ParameteR02").Should().Be(0);
        // This name does not exist so we expect the first case insensitive match to be returned.
        command.Parameters.IndexOf("ParaMeteR02").Should().Be(0);

        // And finally test whether other parameters were also correctly shifted.
        command.Parameters.IndexOf("parameter03").Should().Be(3);
    }

    [Theory]
    [InlineData(LookupThreshold)]
    [InlineData(LookupThreshold - 2)]
    public void finds_case_insensitive_lookups(int count)
    {
        //Arrange
        using var command = new PgSqlCommand();
        var parameters = command.Parameters;
        for (var i = 0; i < count; i++)
            parameters.Add(new PgSqlParameter($"p{i}", i));

        //Assert
        command.Parameters.IndexOf("P1").Should().Be(1);
    }

    [Theory]
    [InlineData(LookupThreshold)]
    [InlineData(LookupThreshold - 2)]
    public void finds_case_sensitive_lookups(int count)
    {
        //Arrange
        using var command = new PgSqlCommand();
        var parameters = command.Parameters;
        for (var i = 0; i < count; i++)
            parameters.Add(new PgSqlParameter($"p{i}", i));

        //Assert
        command.Parameters.IndexOf("p1").Should().Be(1);
    }

    [Theory]
    [InlineData(LookupThreshold)]
    [InlineData(LookupThreshold - 2)]
    public void throws_on_indexer_mismatch(int count)
    {
        //Arrange
        using var command = new PgSqlCommand();
        var parameters = command.Parameters;
        for (var i = 0; i < count; i++)
            parameters.Add(new PgSqlParameter($"p{i}", i));

        //Act
        var act = () =>
        {
            command.Parameters["p1"] = new PgSqlParameter("p1", 1);
            command.Parameters["p1"] = new PgSqlParameter("P1", 1);
        };

        //Assert
        act.Should().NotThrow();

        Assert.Throws<ArgumentException>(() =>
        {
            command.Parameters["p1"] = new PgSqlParameter("p2", 1);
        });
    }

    [Theory]
    [InlineData(LookupThreshold)]
    [InlineData(LookupThreshold - 2)]
    public void positional_parameter_lookup_returns_first_match(int count)
    {
        //Arrange
        using var command = new PgSqlCommand();
        var parameters = command.Parameters;
        for (var i = 0; i < count; i++)
            parameters.Add(new PgSqlParameter(PgSqlParameter.PositionalName, i));

        //Assert
        command.Parameters.IndexOf("").Should().Be(0);
    }

    [Fact]
    public void throw_multiple_positions_same_instance()
    {
        //Arrange
        using var cmd = new PgSqlCommand("SELECT $1, $2");
        var p = new PgSqlParameter("", "Hello world");
        cmd.Parameters.Add(p);

        //Assert
        Assert.Throws<InvalidOperationException>(() => cmd.Parameters.Add(p));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void IndexOf_falls_back_to_first_insensitive_match(bool manyParams)
    {
        //Arrange
        if (_compatMode == CompatMode.OnePass)
            return;

        using var command = new PgSqlCommand();
        var parameters = command.Parameters;

        parameters.Add(new PgSqlParameter("foo", 8));
        parameters.Add(new PgSqlParameter("bar", 8));
        parameters.Add(new PgSqlParameter("BAR", 8));
        parameters.Count.Should().BeLessThan(LookupThreshold);

        if (manyParams)
            for (var i = 0; i < LookupThreshold; i++)
                parameters.Add(new PgSqlParameter($"p{i}", i));

        //Assert
        parameters.IndexOf("Bar").Should().Be(1);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void IndexOf_prefers_case_sensitive_match(bool manyParams)
    {
        //Arrange
        if (_compatMode == CompatMode.OnePass)
            return;

        using var command = new PgSqlCommand();
        var parameters = command.Parameters;

        parameters.Add(new PgSqlParameter("FOO", 8));
        parameters.Add(new PgSqlParameter("foo", 8));
        parameters.Count.Should().BeLessThan(LookupThreshold);

        if (manyParams)
            for (var i = 0; i < LookupThreshold; i++)
                parameters.Add(new PgSqlParameter($"p{i}", i));

        //Assert
        parameters.IndexOf("foo").Should().Be(1);
    }

    [Fact]
    public void IndexOf_matches_all_parameter_syntaxes()
    {
        //Arrange
        using var command = new PgSqlCommand();
        var parameters = command.Parameters;

        parameters.Add(new PgSqlParameter("@foo0", 8));
        parameters.Add(new PgSqlParameter(":foo1", 8));
        parameters.Add(new PgSqlParameter("foo2", 8));

        //Assert
        for (var i = 0; i < parameters.Count; i++)
        {
            parameters.IndexOf("foo" + i).Should().Be(i);
            parameters.IndexOf("@foo" + i).Should().Be(i);
            parameters.IndexOf(":foo" + i).Should().Be(i);
        }
    }

    [Theory]
    [InlineData(LookupThreshold)]
    [InlineData(LookupThreshold - 2)]
    public void cloning_succeeds(int count)
    {
        //Arrange
        var command = new PgSqlCommand();
        for (var i = 0; i < count; i++)
        {
            command.Parameters.Add(new PgSqlParameter());
        }

        //Act
        var act = () => command.Clone();

        //Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void clean_name()
    {
        //Arrange
        var param = new PgSqlParameter();
        var command = new PgSqlCommand();
        command.Parameters.Add(param);

        //Act
        param.ParameterName = null;

        //Assert
        // These should not throw exceptions
        command.Parameters.IndexOf(param.ParameterName).Should().Be(0);
        param.ParameterName.Should().Be(PgSqlParameter.PositionalName);
    }

    [Fact]
    public void Clone_sets_correct_collection()
    {
        //Arrange
        var cmd = new PgSqlCommand();
        cmd.Parameters.Add(new PgSqlParameter<int> { TypedValue = 42 });
        cmd.Parameters.Single().Collection.Should().BeSameAs(cmd.Parameters);

        //Act
        cmd = cmd.Clone();

        //Assert
        cmd.Parameters.Single().Collection.Should().BeSameAs(cmd.Parameters);
    }

    protected PgSqlParameterCollectionTests(CompatMode compatMode)
    {
        _compatMode = compatMode;

#if DEBUG
        _previousTwoPassCompatMode = PgSqlParameterCollection.TwoPassCompatMode;
        PgSqlParameterCollection.TwoPassCompatMode = compatMode == CompatMode.TwoPass;
#else
        if (compatMode == CompatMode.TwoPass)
            Assert.Skip("Cannot test case-insensitive PgSqlParameterCollection behavior in RELEASE");
#endif
    }

    // Restores the process-wide compat mode so later tests in other classes see the default behavior.
    public void Dispose()
    {
#if DEBUG
        PgSqlParameterCollection.TwoPassCompatMode = _previousTwoPassCompatMode;
#endif
    }

    class SomeOtherDbParameter : DbParameter
    {
        public override void ResetDbType() {}

        public override DbType DbType { get; set; }
        public override ParameterDirection Direction { get; set; }
        public override bool IsNullable { get; set; }
        [AllowNull] public override string ParameterName { get; set; } = "";
        [AllowNull] public override string SourceColumn { get; set; } = "";
        public override object Value { get; set; }
        public override bool SourceColumnNullMapping { get; set; }
        public override int Size { get; set; }
    }
}

#if DEBUG
// This test class has global effects on case sensitive matching in param collection.
[Collection(NonParallelCollection.Name)]
#endif
public sealed class PgSqlParameterCollectionTests_OnePass() : PgSqlParameterCollectionTests(CompatMode.OnePass);
#if DEBUG
// This test class has global effects on case sensitive matching in param collection.
[Collection(NonParallelCollection.Name)]
public sealed class PgSqlParameterCollectionTests_TwoPass() : PgSqlParameterCollectionTests(CompatMode.TwoPass);
#endif

public enum CompatMode
{
    TwoPass,
    OnePass
}
