using System;
using System.Threading.Tasks;
using SilverAssertions;
using Xunit;

namespace CodeBrix.PostgresClient.Tests.Types; //was previously: Npgsql.Tests.Types;

public partial class CompositeHandlerTests
{
    async Task Read<T>(Action<Func<T>, T> assert, string schema = null)
        where T : IComposite, IInitializable, new()
    {
        var composite = new T();
        composite.Initialize();
        await Read(composite, assert, schema);
    }

    async Task Read<T>(T composite, Action<Func<T>, T> assert, string schema = null)
        where T : IComposite
    {
        await using var dataSource = await OpenAndMapComposite(composite, schema, nameof(Read), out var name);
        await using var connection = await dataSource.OpenConnectionAsync();

        var literal = $"ROW({composite.GetValues()})::{name}";
        var arrayLiteral = $"ARRAY[{literal}]::{name}[]";
        await using var command = new PgSqlCommand($"SELECT {literal}, {arrayLiteral}", connection);
        await using var reader = command.ExecuteReader();

        await reader.ReadAsync();
        assert(() => reader.GetFieldValue<T>(0), composite);
        assert(() => reader.GetFieldValue<T[]>(1)[0], composite);
    }

    [Fact]
    public Task Read_class_with_property() =>
        Read<ClassWithProperty>((execute, expected) => execute().Value.Should().Be(expected.Value));

    [Fact]
    public Task Read_class_with_field() =>
        Read<ClassWithField>((execute, expected) => execute().Value.Should().Be(expected.Value));

    [Fact]
    public Task Read_struct_with_property() =>
        Read<StructWithProperty>((execute, expected) => execute().Value.Should().Be(expected.Value));

    [Fact]
    public Task Read_struct_with_field() =>
        Read<StructWithField>((execute, expected) => execute().Value.Should().Be(expected.Value));

    [Fact]
    public Task Read_type_with_two_properties() =>
        Read<TypeWithTwoProperties>((execute, expected) =>
        {
            var actual = execute();
            actual.IntValue.Should().Be(expected.IntValue);
            actual.StringValue.Should().Be(expected.StringValue);
        });

    [Fact]
    public Task Read_type_with_two_properties_inverted() =>
        Read<TypeWithTwoPropertiesReversed>((execute, expected) =>
        {
            var actual = execute();
            actual.IntValue.Should().Be(expected.IntValue);
            actual.StringValue.Should().Be(expected.StringValue);
        });

    [Fact]
    public Task Read_type_with_private_property_throws() =>
        Read(new TypeWithPrivateProperty(), (execute, expected) =>
            execute.Should().ThrowExactly<InvalidCastException>().WithInnerExceptionExactly<InvalidOperationException>());

    [Fact]
    public Task Read_type_with_private_getter() =>
        Read(new TypeWithPrivateGetter(), (execute, expected) => execute());

    [Fact]
    public Task Read_type_with_private_setter_throws() =>
        Read(new TypeWithPrivateSetter(), (execute, expected) => execute.Should().ThrowExactly<InvalidOperationException>());

    [Fact]
    public Task Read_type_without_getter() =>
        Read(new TypeWithoutGetter(), (execute, expected) => execute());

    [Fact]
    public Task Read_type_without_setter_throws() =>
        Read(new TypeWithoutSetter(), (execute, expected) => execute.Should().ThrowExactly<InvalidOperationException>());

    [Fact]
    public Task Read_type_with_explicit_property_name() =>
        Read(new TypeWithExplicitPropertyName { MyValue = HelloSlonik }, (execute, expected) => execute().MyValue.Should().Be(expected.MyValue));

    [Fact]
    public Task Read_type_with_explicit_parameter_name() =>
        Read(new TypeWithExplicitParameterName(HelloSlonik), (execute, expected) => execute().Value.Should().Be(expected.Value));

    [Fact]
    public Task Read_type_with_more_properties_than_attributes() =>
        Read(new TypeWithMorePropertiesThanAttributes(), (execute, expected) =>
        {
            var actual = execute();
            ((int?)actual.IntValue).Should().NotBeNull();
            actual.StringValue.Should().BeNull();
        });

    [Fact]
    public Task Read_type_with_less_properties_than_attributes_throws() =>
        Read(new TypeWithLessPropertiesThanAttributes(), (execute, expected) =>
            execute.Should().ThrowExactly<InvalidCastException>().WithInnerExceptionExactly<InvalidOperationException>());

    [Fact]
    public Task Read_type_with_less_parameters_than_attributes_throws() =>
        Read(new TypeWithLessParametersThanAttributes(TheAnswer), (execute, expected) =>
            execute.Should().ThrowExactly<InvalidCastException>().WithInnerExceptionExactly<InvalidOperationException>());

    [Fact]
    public Task Read_type_with_more_parameters_than_attributes_throws() =>
        Read(new TypeWithMoreParametersThanAttributes(TheAnswer, HelloSlonik), (execute, expected) =>
            execute.Should().ThrowExactly<InvalidCastException>().WithInnerExceptionExactly<InvalidOperationException>());

    [Fact]
    public Task Read_type_with_one_parameter() =>
        Read(new TypeWithOneParameter(1), (execute, expected) => execute().Value1.Should().Be(expected.Value1));

    [Fact]
    public Task Read_type_with_two_parameters() =>
        Read(new TypeWithTwoParameters(TheAnswer, HelloSlonik), (execute, expected) =>
        {
            var actual = execute();
            actual.IntValue.Should().Be(expected.IntValue);
            actual.StringValue.Should().Be(expected.StringValue);
        });

    [Fact]
    public Task Read_type_with_two_parameters_reversed() =>
        Read(new TypeWithTwoParametersReversed(HelloSlonik, TheAnswer), (execute, expected) =>
        {
            var actual = execute();
            actual.IntValue.Should().Be(expected.IntValue);
            actual.StringValue.Should().Be(expected.StringValue);
        });

    [Fact]
    public Task Read_type_with_nine_parameters() =>
        Read(new TypeWithNineParameters(1, 2, 3, 4, 5, 6, 7, 8, 9), (execute, expected) =>
        {
            var actual = execute();
            actual.Value1.Should().Be(expected.Value1);
            actual.Value2.Should().Be(expected.Value2);
            actual.Value3.Should().Be(expected.Value3);
            actual.Value4.Should().Be(expected.Value4);
            actual.Value5.Should().Be(expected.Value5);
            actual.Value6.Should().Be(expected.Value6);
            actual.Value7.Should().Be(expected.Value7);
            actual.Value8.Should().Be(expected.Value8);
            actual.Value9.Should().Be(expected.Value9);
        });
}
