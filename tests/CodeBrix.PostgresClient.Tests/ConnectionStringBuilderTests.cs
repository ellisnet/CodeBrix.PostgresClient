using System;
using SilverAssertions;
using Xunit;

namespace CodeBrix.PostgresClient.Tests; //was previously: Npgsql.Tests;

public class ConnectionStringBuilderTests
{
    [Fact]
    public void basic()
    {
        var builder = new PgSqlConnectionStringBuilder();
        builder.Count.Should().Be(0);
        builder.ContainsKey("server").Should().BeTrue();
        builder.Host = "myhost";
        builder["host"].Should().Be("myhost");
        builder.Count.Should().Be(1);
        builder.ConnectionString.Should().Be("Host=myhost");
        builder.Remove("HOST");
        builder["host"].Should().Be("");
        builder.Count.Should().Be(0);
    }

    [Fact]
    public void TryGetValue()
    {
        //Arrange
        var builder = new PgSqlConnectionStringBuilder();
        builder.ConnectionString = "Host=myhost";

        //Assert
        builder.TryGetValue("Host", out var value).Should().BeTrue();
        value.Should().Be("myhost");
        builder.TryGetValue("SomethingUnknown", out value).Should().BeFalse();
    }

    [Fact]
    public void Remove()
    {
        var builder = new PgSqlConnectionStringBuilder();
        builder.SslMode = SslMode.Require;
        builder["SSL Mode"].Should().Be(SslMode.Require);
        builder.Remove("SSL Mode");
        builder.ConnectionString.Should().Be("");
        builder.CommandTimeout = 120;
        builder["Command Timeout"].Should().Be(120);
        builder.Remove("Command Timeout");
        builder.ConnectionString.Should().Be("");
    }

    [Fact]
    public void Clear()
    {
        //Arrange
        var builder = new PgSqlConnectionStringBuilder { Host = "myhost" };

        //Act
        builder.Clear();

        //Assert
        builder.Count.Should().Be(0);
        builder["host"].Should().Be("");
        builder.Host.Should().BeNull();
    }

    [Fact]
    public void removing_resets_to_default()
    {
        //Arrange
        var builder = new PgSqlConnectionStringBuilder();
        builder.Port.Should().Be(PgSqlConnection.DefaultPort);
        builder.Port = 8;

        //Act
        builder.Remove("Port");

        //Assert
        builder.Port.Should().Be(PgSqlConnection.DefaultPort);
    }

    [Fact]
    public void setting_to_null_resets_to_default()
    {
        //Arrange
        var builder = new PgSqlConnectionStringBuilder();
        builder.Port.Should().Be(PgSqlConnection.DefaultPort);
        builder.Port = 8;

        //Act
        builder["Port"] = null;

        //Assert
        builder.Port.Should().Be(PgSqlConnection.DefaultPort);
    }

    [Fact]
    public void @enum()
    {
        //Arrange
        var builder = new PgSqlConnectionStringBuilder();

        //Act
        builder.ConnectionString = "SslMode=Require";

        //Assert
        builder.SslMode.Should().Be(SslMode.Require);
        builder.Count.Should().Be(1);
    }

    [Fact]
    public void enum_insensitive()
    {
        //Arrange
        var builder = new PgSqlConnectionStringBuilder();

        //Act
        builder.ConnectionString = "SslMode=require";

        //Assert
        builder.SslMode.Should().Be(SslMode.Require);
        builder.Count.Should().Be(1);
    }

    [Fact]
    public void Clone()
    {
        //Arrange
        var builder = new PgSqlConnectionStringBuilder();
        builder.Host = "myhost";

        //Act
        var builder2 = builder.Clone();

        //Assert
        builder2.Host.Should().Be("myhost");
        builder2["Host"].Should().Be("myhost");
        builder.Port.Should().Be(PgSqlConnection.DefaultPort);
    }

    [Fact]
    public void conversion_error_throws()
    {
        //Arrange
        var builder = new PgSqlConnectionStringBuilder();

        //Act
        var act = () => builder["Port"] = "hello";

        //Assert
        act.Should().ThrowExactly<ArgumentException>().WithMessage("*Port*");
    }

    [Fact]
    public void invalid_connection_string_throws()
    {
        //Arrange
        var builder = new PgSqlConnectionStringBuilder();

        //Act
        var act = () => builder.ConnectionString = "Server=127.0.0.1;User Id=pgsql_tests;Pooling:false";

        //Assert
        act.Should().ThrowExactly<ArgumentException>();
    }
}
