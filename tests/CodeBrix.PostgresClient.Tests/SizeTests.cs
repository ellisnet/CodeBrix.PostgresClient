using System;
using CodeBrix.PostgresClient.Internal;
using SilverAssertions;
using Xunit;

namespace CodeBrix.PostgresClient.Tests; //was previously: Npgsql.Tests;

public class SizeTests
{
    [Fact]
    public void Unknown_kind() => Size.Unknown.Kind.Should().Be(SizeKind.Unknown);

    [Fact]
    public void Unknown_throws_on_value() => Assert.Throws<InvalidOperationException>(() => _ = Size.Unknown.Value);

    [Fact]
    public void exact()
    {
        //Assert
        Size.Create(1).Value.Should().Be(1);
        Size.Create(1).Kind.Should().Be(SizeKind.Exact);
    }

    [Fact]
    public void Zero_is_exact_kind() => Size.Zero.Kind.Should().Be(SizeKind.Exact);

    [Fact]
    public void upper_bound()
    {
        //Assert
        Size.CreateUpperBound(1).Value.Should().Be(1);
        Size.CreateUpperBound(1).Kind.Should().Be(SizeKind.UpperBound);
    }

    [Fact]
    public void Combine_throws_on_overflow() => Assert.Throws<OverflowException>(() => Size.Create(1).Combine(int.MaxValue));

    [Fact]
    public void Combine_exact_works() => Size.Create(1).Combine(1).Should().Be(Size.Create(2));

    [Fact]
    public void Combine_upper_bound_works() => Size.CreateUpperBound(1).Combine(1).Should().Be(Size.CreateUpperBound(2));

    [Fact]
    public void Combine_unknown_with_any_gives_unknown()
    {
        //Assert
        Size.Unknown.Combine(Size.Unknown).Should().Be(Size.Unknown);

        Size.Create(1).Combine(Size.Unknown).Should().Be(Size.Unknown);
        Size.Unknown.Combine(Size.Create(1)).Should().Be(Size.Unknown);

        Size.Unknown.Combine(Size.CreateUpperBound(1)).Should().Be(Size.Unknown);
        Size.CreateUpperBound(1).Combine(Size.Unknown).Should().Be(Size.Unknown);
    }

    [Fact]
    public void Combine_upper_bound_with_exact_gives_upper_bound()
    {
        //Assert
        Size.Create(1).Combine(Size.CreateUpperBound(1)).Should().Be(Size.CreateUpperBound(2));
        Size.CreateUpperBound(1).Combine(Size.Create(1)).Should().Be(Size.CreateUpperBound(2));
    }
}
