using System;
using CodeBrix.PostgresClient.Internal.Postgres;
using SilverAssertions;
using Xunit;

namespace CodeBrix.PostgresClient.Tests; //was previously: Npgsql.Tests;

public class DataTypeNameTests
{
    [Fact]
    public void max_length_data_type_name()
    {
        //Arrange
        var name = new string('a', DataTypeName.NAMEDATALEN);
        var fullyQualifiedDataTypeName= $"public.{name}";

        //Act
        var act = () => new DataTypeName(fullyQualifiedDataTypeName);

        //Assert
        act.Should().NotThrow();
        fullyQualifiedDataTypeName.Should().Be(new DataTypeName(fullyQualifiedDataTypeName).Value);
    }

    [Fact]
    public void too_long_data_type_name()
    {
        //Arrange
        var name = new string('a', DataTypeName.NAMEDATALEN + 1);
        var fullyQualifiedDataTypeName= $"public.{name}";

        //Act
        var exception = Assert.Throws<ArgumentException>(() => new DataTypeName(fullyQualifiedDataTypeName));

        //Assert
        exception.Message.Should().EndWith($": public.{new string('a', DataTypeName.NAMEDATALEN)}");
    }

    [Theory]
    [InlineData("public.name", "public._name")]
    [InlineData("public._name", "public._name")]
    [InlineData("public.zzzaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa123", "public._zzzaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa12")]
    public void ToArrayName(string name, string expected)
        => new DataTypeName(name).ToArrayName().Value.Should().Be(expected);

    [Theory]
    [InlineData("public.multirange", "public.multirange")]
    [InlineData("public.abcmultirange123", "public.abcmultirange123")]
    [InlineData("public.multiRANGE", "public.multiRANGE_multirange")]
    public void ToDefaultMultirangeName_has_multi_range(string name, string expected)
        => new DataTypeName(name).ToDefaultMultirangeName().Value.Should().Be(expected);

    [Theory]
    [InlineData("public.range", "public.multirange")]
    [InlineData("public.abcrange123", "public.abcmultirange123")]
    [InlineData("public.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaarange", "public.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaamultirange")] // Replace goes to max length
    [InlineData("public.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaarange1", "public.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaamultir")] // Replace goes over max length
    [InlineData("public.RANGE", "public.RANGE_multirange")]
    public void ToDefaultMultirangeName_has_range(string name, string expected)
        => new DataTypeName(name).ToDefaultMultirangeName().Value.Should().Be(expected);

    [Theory]
    [InlineData("public.name", null, "public.name")]
    [InlineData("public._name", null, "public._name")]
    [InlineData("public.name[]", null, "public._name")]
    [InlineData("public.integer", null, "public.integer")]
    [InlineData("name", null, "pg_catalog.name")]
    [InlineData("_name", null, "pg_catalog._name")]
    [InlineData("name[]", null, "pg_catalog._name")]
    [InlineData("mytype", null, "-.mytype")]
    [InlineData("_mytype", null, "-._mytype")]
    [InlineData("mytype[]", null, "-._mytype")]
    [InlineData("character varying", null, "pg_catalog.varchar")]
    [InlineData("decimal(facet_name)", null, "pg_catalog.numeric")]
    [InlineData("name", "public", "public.name")]
    [InlineData("name ", "public", "public.name")]
    [InlineData("_name", "public", "public._name")]
    [InlineData("name[]", "public", "public._name")]
    [InlineData("timestamp with time zone", "public", "public.timestamp with time zone")]
    [InlineData("timestamp with time zone", "pg_catalog", "pg_catalog.timestamptz")]
    [InlineData("timestamp with time zone", null, "pg_catalog.timestamptz")]
    [InlineData("boolean(facet_name)", "public", "public.boolean(facet_name)")]
    [InlineData("boolean(facet_name)", "pg_catalog", "pg_catalog.bool")]
    [InlineData("boolean(facet_name)", null, "pg_catalog.bool")]
    [InlineData(" public.name ", null, "public.name")]
    [InlineData("decimal", "public", "public.decimal")]
    [InlineData("numeric", "public", "public.numeric")]
    public void FromDisplayName(string name, string schema, string expected)
        => DataTypeName.FromDisplayName(schema is null or "pg_catalog" ? name : schema + "." + name).Value.Should().Be(expected);

    [Theory]
    [InlineData("pg_catalog.bool", "boolean")]
    [InlineData("public.bool", "bool")]
    [InlineData("pg_catalog.numeric", "numeric")]
    [InlineData("pg_catalog._numeric", "numeric[]")]
    [InlineData("pg_catalog.decimal", "numeric")]
    [InlineData("public.numeric", "numeric")]
    [InlineData("public._numeric", "numeric[]")]
    [InlineData("public.decimal", "decimal")]
    [InlineData("public._decimal", "decimal[]")]
    public void UnqualifiedDisplayName(string fullyQualifiedName, string expected)
        => new DataTypeName(fullyQualifiedName).UnqualifiedDisplayName.Should().Be(expected);
}
