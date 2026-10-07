using System;
using System.Threading.Tasks;
using SilverAssertions;
using Xunit;

namespace CodeBrix.PostgresClient.Tests.Types; //was previously: Npgsql.Tests.Types;

public partial class CompositeHandlerTests
{
    async Task Write<T>(Action<PgSqlDataReader, T> assert, string schema = null)
        where T : IComposite, IInitializable, new()
    {
        var composite = new T();
        composite.Initialize();
        await Write(composite, assert, schema);
    }

    async Task Write<T>(T composite, Action<PgSqlDataReader, T> assert = null, string schema = null)
        where T : IComposite
    {
        await using var dataSource = await OpenAndMapComposite(composite, schema, nameof(Write), out var _);
        await using var connection = await dataSource.OpenConnectionAsync();
        {
            await using var command = new PgSqlCommand("SELECT (@c).*", connection);

            command.Parameters.AddWithValue("c", composite);
            await using var reader = await command.ExecuteReaderAsync();
            await reader.ReadAsync();

            if (assert is not null)
                assert(reader, composite);
        }

        {
            await using var command = new PgSqlCommand("SELECT (@arrayc)[1].*", connection);

            command.Parameters.AddWithValue("arrayc", new[] { composite });
            await using var reader = await command.ExecuteReaderAsync();
            await reader.ReadAsync();

            if (assert is not null)
                assert(reader, composite);
        }
    }

    [Fact]
    public Task Write_class_with_property()
        => Write<ClassWithProperty>((reader, expected) => reader.GetString(0).Should().Be(expected.Value));

    [Fact]
    public Task Write_class_with_field()
        => Write<ClassWithField>((reader, expected) => reader.GetString(0).Should().Be(expected.Value));

    [Fact]
    public Task Write_struct_with_property()
        => Write<StructWithProperty>((reader, expected) => reader.GetString(0).Should().Be(expected.Value));

    [Fact]
    public Task Write_struct_with_field()
        => Write<StructWithField>((reader, expected) => reader.GetString(0).Should().Be(expected.Value));

    [Fact]
    public Task Write_type_with_two_properties()
        => Write<TypeWithTwoProperties>((reader, expected) =>
        {
            reader.GetInt32(0).Should().Be(expected.IntValue);
            reader.GetString(1).Should().Be(expected.StringValue);
        });

    [Fact]
    public Task Write_type_with_two_properties_inverted()
        => Write<TypeWithTwoPropertiesReversed>((reader, expected) =>
        {
            reader.GetInt32(1).Should().Be(expected.IntValue);
            reader.GetString(0).Should().Be(expected.StringValue);
        });

    [Fact]
    public async Task Write_type_with_private_property_throws()
        => (await FluentActions.Awaiting(() => Write(new TypeWithPrivateProperty())).Should().ThrowExactlyAsync<InvalidCastException>())
            .WithInnerExceptionExactly<InvalidOperationException>();

    [Fact]
    public async Task Write_type_with_private_getter_throws()
        => await Assert.ThrowsAsync<InvalidOperationException>(() => Write(new TypeWithPrivateGetter()));

    [Fact]
    public Task Write_type_with_private_setter()
        => Write(new TypeWithPrivateSetter());

    [Fact]
    public async Task Write_type_without_getter_throws()
        => await Assert.ThrowsAsync<InvalidOperationException>(() => Write(new TypeWithoutGetter()));

    [Fact]
    public Task Write_type_without_setter() =>
        Write(new TypeWithoutSetter());

    [Fact]
    public Task Write_type_with_explicit_property_name()
        => Write(new TypeWithExplicitPropertyName { MyValue = HelloSlonik }, (reader, expected) => reader.GetString(0).Should().Be(expected.MyValue));

    [Fact]
    public Task Write_type_with_explicit_parameter_name()
        => Write(new TypeWithExplicitParameterName(HelloSlonik), (reader, expected) => reader.GetString(0).Should().Be(expected.Value));

    [Fact]
    public Task Write_type_with_more_properties_than_attributes()
        => Write(new TypeWithMorePropertiesThanAttributes());

    [Fact]
    public async Task Write_type_with_less_properties_than_attributes_throws()
        => (await FluentActions.Awaiting(() => Write(new TypeWithLessPropertiesThanAttributes())).Should().ThrowExactlyAsync<InvalidCastException>())
            .WithInnerExceptionExactly<InvalidOperationException>();

    [Fact]
    public async Task Write_type_with_less_parameters_than_attributes_throws()
        => (await FluentActions.Awaiting(() => Write(new TypeWithMoreParametersThanAttributes(TheAnswer, HelloSlonik))).Should().ThrowExactlyAsync<InvalidCastException>())
            .WithInnerExceptionExactly<InvalidOperationException>();

    [Fact]
    public async Task Write_type_with_more_parameters_than_attributes_throws()
        => (await FluentActions.Awaiting(() => Write(new TypeWithLessParametersThanAttributes(TheAnswer))).Should().ThrowExactlyAsync<InvalidCastException>())
            .WithInnerExceptionExactly<InvalidOperationException>();
}
