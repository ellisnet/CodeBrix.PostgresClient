using System;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.PostgresClient.PgSqlTypes;
using CodeBrix.PostgresClient.Properties;
using SilverAssertions;
using Xunit;

namespace CodeBrix.PostgresClient.Tests.Types; //was previously: Npgsql.Tests.Types;

public abstract class CubeTests(MultiplexingMode multiplexingMode, CubeTestsFixture fixture)
    : MultiplexingTestBase(multiplexingMode), IClassFixture<CubeTestsFixture>, IAsyncLifetime
{
    public static readonly TheoryData<PgSqlCube, string> CubeValues = new()
    {
        { new PgSqlCube(new[] { 1.0, 2.0, 3.0 }, new[] { 4.0, 5.0, 6.0 }), "(1, 2, 3),(4, 5, 6)" },
        { new PgSqlCube(new[] { 1.0, 2.0, 3.0 }), "(1, 2, 3)" },
        { new PgSqlCube(1.0), "(1)" },
        { new PgSqlCube(1.0, 2.0), "(1),(2)" }
    };

    [Theory, MemberData(nameof(CubeValues))]
    public Task cube(PgSqlCube cube, string sqlLiteral)
        => AssertType(cube, sqlLiteral, "cube", PgSqlDbType.Cube, isDefault: true, isPgSqlDbTypeInferredFromClrType: false);

    [Fact]
    public void cube_constructor_single_value()
    {
        //Arrange
        var cube = new PgSqlCube(1.0);

        //Assert
        cube.IsPoint.Should().BeTrue();
        cube.Dimensions.Should().Be(1);
        cube.LowerLeft.Should().BeEquivalentTo(new [] { 1.0 });
        cube.UpperRight.Should().BeEquivalentTo(new [] { 1.0 });
    }

    [Fact]
    public void cube_constructor_single_coord_point()
    {
        //Arrange
        var cube = new PgSqlCube(1.0, 1.0);

        //Assert
        cube.IsPoint.Should().BeTrue();
        cube.Dimensions.Should().Be(1);
        cube.LowerLeft.Should().BeEquivalentTo(new [] { 1.0 });
        cube.UpperRight.Should().BeEquivalentTo(new [] { 1.0 });
    }

    [Fact]
    public void cube_constructor_single_coord_not_point()
    {
        //Arrange
        var cube = new PgSqlCube(1.0, 2.0);

        //Assert
        cube.IsPoint.Should().BeFalse();
        cube.Dimensions.Should().Be(1);
        cube.LowerLeft.Should().BeEquivalentTo(new [] { 1.0 });
        cube.UpperRight.Should().BeEquivalentTo(new [] { 2.0 });
    }

    [Fact]
    public void cube_constructor_lower_left_upper_right_not_point()
    {
        //Arrange
        var cube = new PgSqlCube(new[] { 1.0, 2.0 }, new[] { 3.0, 4.0 });

        //Assert
        cube.IsPoint.Should().BeFalse();
        cube.Dimensions.Should().Be(2);
        cube.LowerLeft.Should().BeEquivalentTo(new [] { 1.0, 2.0 });
        cube.UpperRight.Should().BeEquivalentTo(new [] { 3.0, 4.0 });
    }

    [Fact]
    public void cube_constructor_lower_left_upper_right_point()
    {
        //Arrange
        var cube = new PgSqlCube(new[] { 1.0, 2.0 }, new[] { 1.0, 2.0 });

        //Assert
        cube.IsPoint.Should().BeTrue();
        cube.Dimensions.Should().Be(2);
        cube.LowerLeft.Should().BeEquivalentTo(new [] { 1.0, 2.0 });
        cube.UpperRight.Should().BeEquivalentTo(new [] { 1.0, 2.0 });
    }

    [Fact]
    public void cube_constructor_add_dimension_single_point()
    {
        //Arrange
        var existingCube = new PgSqlCube(new[] { 1.0, 2.0, 3.0 });
        var cube = new PgSqlCube(existingCube, 4.0);

        //Assert
        cube.IsPoint.Should().BeTrue();
        cube.Dimensions.Should().Be(4);
        cube.LowerLeft.Should().BeEquivalentTo(new [] { 1.0, 2.0, 3.0, 4.0 });
        cube.UpperRight.Should().BeEquivalentTo(new [] { 1.0, 2.0, 3.0, 4.0 });
    }

    [Fact]
    public void cube_constructor_add_dimension_single_not_point()
    {
        //Arrange
        var existingCube = new PgSqlCube(new [] { 1.0, 2.0 }, new [] { 3.0, 4.0 });
        var cube = new PgSqlCube(existingCube, 3.0);

        //Assert
        cube.IsPoint.Should().BeFalse();
        cube.Dimensions.Should().Be(3);
        cube.LowerLeft.Should().BeEquivalentTo(new [] { 1.0, 2.0, 3.0 });
        cube.UpperRight.Should().BeEquivalentTo(new [] { 3.0, 4.0, 3.0 });
    }

    [Fact]
    public void cube_constructor_add_dimension_lower_left_upper_right_point()
    {
        //Arrange
        var existingCube = new PgSqlCube(new[] { 1.0, 2.0, 3.0 });
        var cube = new PgSqlCube(existingCube, 4.0, 4.0);

        //Assert
        cube.IsPoint.Should().BeTrue();
        cube.Dimensions.Should().Be(4);
        cube.LowerLeft.Should().BeEquivalentTo(new [] { 1.0, 2.0, 3.0, 4.0 });
        cube.UpperRight.Should().BeEquivalentTo(new [] { 1.0, 2.0, 3.0, 4.0 });
    }

    [Fact]
    public void cube_constructor_add_dimension_lower_left_upper_right_not_point()
    {
        //Arrange
        var existingCube = new PgSqlCube(new [] { 1.0, 2.0 }, new [] { 3.0, 4.0 });
        var cube = new PgSqlCube(existingCube, 4.0, 5.0);

        //Assert
        cube.IsPoint.Should().BeFalse();
        cube.Dimensions.Should().Be(3);
        cube.LowerLeft.Should().BeEquivalentTo(new [] { 1.0, 2.0, 4.0 });
        cube.UpperRight.Should().BeEquivalentTo(new [] { 3.0, 4.0, 5.0 });
    }

    [Fact]
    public void cube_subset()
    {
        //Arrange
        var cube = new PgSqlCube(new [] { 1.0, 2.0, 3.0 }, new [] { 4.0, 5.0, 6.0 });

        //Assert
        cube.ToSubset(0, 2, 1, 1).Should().Be(new PgSqlCube(new [] { 1.0, 3.0, 2.0, 2.0 }, new [] { 4.0, 6.0, 5.0, 5.0 }));
    }

    [Fact]
    public void cube_to_string_not_point()
    {
        //Arrange
        var cube = new PgSqlCube(new[] { 1.0, 2.0, 3.0 }, new[] { 4.0, 5.0, 6.0 });

        //Assert
        cube.ToString().Should().Be("(1, 2, 3),(4, 5, 6)");
    }

    [Fact]
    public void cube_to_string_point()
    {
        //Arrange
        var cube = new PgSqlCube(new[] { 1.0, 2.0, 3.0 });

        //Assert
        cube.ToString().Should().Be("(1, 2, 3)");
    }

    [Fact]
    public async Task cube_array()
    {
        //Arrange
        var data = new[]
        {
            new PgSqlCube(new[] { 1.0, 2.0 }, new[] { 3.0, 4.0 }),
            new PgSqlCube(new[] { 5.0, 6.0 }),
            new PgSqlCube(1.0, 2.0)
        };

        //Assert
        await AssertType(
            data,
            @"{""(1, 2),(3, 4)"",""(5, 6)"",""(1),(2)""}",
            "cube[]",
            PgSqlDbType.Cube | PgSqlDbType.Array,
            isDefault: true,
            isPgSqlDbTypeInferredFromClrType: false);
    }

    [Fact]
    public void cube_dimension_mismatch_throws_argument_exception()
    {
        //Act
        var ex = Assert.Throws<ArgumentException>(() => new PgSqlCube(new[] { 1.0, 2.0 }, new[] { 3.0 }));

        //Assert
        ex.Message.Should().Contain("Different point dimensions");
    }

    [Fact]
    public Task cube_negative_values()
        => AssertType(
            new PgSqlCube(new[] { -1.0, -2.0, -3.0 }, new[] { -4.0, -5.0, -6.0 }),
            "(-1, -2, -3),(-4, -5, -6)",
            "cube",
            PgSqlDbType.Cube,
            isDefault: true,
            isPgSqlDbTypeInferredFromClrType: false);

    [Fact]
    public void cube_equality_hash_code()
    {
        //Arrange
        var cube1 = new PgSqlCube(new[] { 1.0, 2.0 }, new[] { 3.0, 4.0 });
        var cube2 = new PgSqlCube(new[] { 1.0, 2.0 }, new[] { 3.0, 4.0 });
        var cube3 = new PgSqlCube(new[] { 1.0, 2.0 }, new[] { 3.0, 5.0 });

        //Assert
        // Test equality
        cube1.Should().Be(cube2);
        (cube1 == cube2).Should().BeTrue();
        (cube1 != cube3).Should().BeTrue();
        cube1.Equals(cube2).Should().BeTrue();
        cube1.Equals(cube3).Should().BeFalse();

        // Test hash code consistency
        cube1.GetHashCode().Should().Be(cube2.GetHashCode());
        cube1.GetHashCode().Should().NotBe(cube3.GetHashCode());
    }

    [Fact]
    public Task cube_zero_values()
        => AssertType(
            new PgSqlCube(0.0, 0.0),
            "(0)",
            "cube",
            PgSqlDbType.Cube,
            isDefault: true,
            isPgSqlDbTypeInferredFromClrType: false);

    [Fact]
    public Task cube_max_dimensions()
    {
        //Arrange
        var lowerLeft = new double[100];
        var upperRight = new double[100];
        for (var i = 0; i < 100; i++)
        {
            lowerLeft[i] = i;
            upperRight[i] = i + 100;
        }

        var expectedLower = string.Join(", ", lowerLeft);
        var expectedUpper = string.Join(", ", upperRight);
        var expected = $"({expectedLower}),({expectedUpper})";

        //Assert
        return AssertType(
            new PgSqlCube(lowerLeft, upperRight),
            expected,
            "cube",
            PgSqlDbType.Cube,
            isDefault: true,
            isPgSqlDbTypeInferredFromClrType: false);
    }

    [Fact]
    public async Task cube_not_supported_by_default_on_PgSqlSlimSourceBuilder()
    {
        //Arrange
        var errorMessage = string.Format(
            PgSqlStrings.CubeNotEnabled, nameof(PgSqlSlimDataSourceBuilder.EnableCube), nameof(PgSqlSlimDataSourceBuilder));

        var dataSourceBuilder = new PgSqlSlimDataSourceBuilder(ConnectionString);
        await using var dataSource = dataSourceBuilder.Build();

        //Act
        var exception =
            await AssertTypeUnsupportedRead<PgSqlCube>("(1),(2)", "cube", dataSource);
        exception.InnerException.Message.Should().Be(errorMessage);
        exception = await AssertTypeUnsupportedWrite<PgSqlCube>(new PgSqlCube(1.0, 2.0), "cube", dataSource);
        exception.InnerException.Message.Should().Be(errorMessage);
    }

    [Fact]
    public async Task PgSqlSlimSourceBuilder_EnableCube()
    {
        //Arrange
        var dataSourceBuilder = new PgSqlSlimDataSourceBuilder(ConnectionString);
        dataSourceBuilder.EnableCube();
        await using var dataSource = dataSourceBuilder.Build();

        //Assert
        await AssertType(dataSource, new PgSqlCube(1.0, 2.0), "(1),(2)", "cube", PgSqlDbType.Cube, isDefaultForWriting: false, skipArrayCheck: true);
    }

    [Fact]
    public async Task PgSqlSlimSourceBuilder_EnableArrays()
    {
        //Arrange
        var dataSourceBuilder = new PgSqlSlimDataSourceBuilder(ConnectionString);
        dataSourceBuilder.EnableCube();
        dataSourceBuilder.EnableArrays();
        await using var dataSource = dataSourceBuilder.Build();

        //Assert
        await AssertType(dataSource, new PgSqlCube(1.0, 2.0), "(1),(2)", "cube", PgSqlDbType.Cube, isDefaultForWriting: false);
    }

    public ValueTask InitializeAsync()
        => new(fixture.EnsureSetUp(OpenConnectionAsync));

    public ValueTask DisposeAsync()
        => ValueTask.CompletedTask;
}

public sealed class CubeTests_NonMultiplexing(CubeTestsFixture fixture) : CubeTests(MultiplexingMode.NonMultiplexing, fixture);
public sealed class CubeTests_Multiplexing(CubeTestsFixture fixture) : CubeTests(MultiplexingMode.Multiplexing, fixture);

/// <summary>
/// The once-per-test-class setup of <see cref="CubeTests"/>: makes sure the cube extension exists, using the first test's
/// own data source so its type information gets reloaded.
/// </summary>
public sealed class CubeTestsFixture
{
    readonly SemaphoreSlim _setUpLock = new(1);
    bool _isSetUp;

    internal async Task EnsureSetUp(Func<ValueTask<PgSqlConnection>> openConnection)
    {
        await _setUpLock.WaitAsync(TestContext.Current.CancellationToken);
        try
        {
            if (_isSetUp)
                return;

            await using var conn = await openConnection();
            TestUtil.MinimumPgVersion(conn, "13.0");
            await TestUtil.EnsureExtensionAsync(conn, "cube");
            _isSetUp = true;
        }
        finally
        {
            _setUpLock.Release();
        }
    }
}
