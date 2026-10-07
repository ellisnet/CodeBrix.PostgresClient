using System.Threading.Tasks;
using CodeBrix.PostgresClient.PgSqlTypes;
using Xunit;

namespace CodeBrix.PostgresClient.Tests.Types; //was previously: Npgsql.Tests.Types;

/// <summary>
/// Tests on PostgreSQL geometric types
/// </summary>
/// <remarks>
/// https://www.postgresql.org/docs/current/static/datatype-geometric.html
/// </remarks>
public abstract class GeometricTypeTests(MultiplexingMode multiplexingMode) : MultiplexingTestBase(multiplexingMode)
{
    [Fact]
    public Task point()
        => AssertType(new PgSqlPoint(1.2, 3.4), "(1.2,3.4)", "point", PgSqlDbType.Point);

    [Fact]
    public Task line()
        => AssertType(new PgSqlLine(1, 2, 3), "{1,2,3}", "line", PgSqlDbType.Line);

    [Fact]
    public Task line_segment()
        => AssertType(new PgSqlLSeg(1, 2, 3, 4), "[(1,2),(3,4)]", "lseg", PgSqlDbType.LSeg);

    [Fact]
    public async Task box()
    {
        await AssertType(
            new PgSqlBox(top: 3, right: 4, bottom: 1, left: 2),
            "(4,3),(2,1)",
            "box",
            PgSqlDbType.Box,
            skipArrayCheck: true); // Uses semicolon instead of comma as separator

        await AssertType(
            new PgSqlBox(top: -10, right: 0, bottom: -20, left: -10),
            "(0,-10),(-10,-20)",
            "box",
            PgSqlDbType.Box,
            skipArrayCheck: true); // Uses semicolon instead of comma as separator

        await AssertType(
            new PgSqlBox(top: 1, right: 2, bottom: 3, left: 4),
            "(4,3),(2,1)",
            "box",
            PgSqlDbType.Box,
            skipArrayCheck: true); // Uses semicolon instead of comma as separator

        var swapped = new PgSqlBox(top: -20, right: -10, bottom: -10, left: 0);

        await AssertType(
            swapped,
            "(0,-10),(-10,-20)",
            "box",
            PgSqlDbType.Box,
            skipArrayCheck: true); // Uses semicolon instead of comma as separator

        await AssertType(
            swapped with { UpperRight = new PgSqlPoint(-20,-10) },
            "(-10,-10),(-20,-20)",
            "box",
            PgSqlDbType.Box,
            skipArrayCheck: true); // Uses semicolon instead of comma as separator

        await AssertType(
            swapped with { LowerLeft = new PgSqlPoint(10, 10) },
            "(10,10),(0,-10)",
            "box",
            PgSqlDbType.Box,
            skipArrayCheck: true); // Uses semicolon instead of comma as separator
    }

    [Fact]
    public async Task box_array()
    {
        //Arrange
        var data = new[]
        {
            new PgSqlBox(top: 3, right: 4, bottom: 1, left: 2),
            new PgSqlBox(top: 5, right: 6, bottom: 3, left: 4),
            new PgSqlBox(top: -10, right: 0, bottom: -20, left: -10)
        };

        //Assert
        await AssertType(
            data,
            "{(4,3),(2,1);(6,5),(4,3);(0,-10),(-10,-20)}",
            "box[]",
            PgSqlDbType.Box | PgSqlDbType.Array
            );

        var swappedData = new[]
        {
            new PgSqlBox(top: 1, right: 2, bottom: 3, left: 4),
            new PgSqlBox(top: 3, right: 4, bottom: 5, left: 6),
            new PgSqlBox(top: -20, right: -10, bottom: -10, left: 0)
        };

        await AssertType(
            swappedData,
            "{(4,3),(2,1);(6,5),(4,3);(0,-10),(-10,-20)}",
            "box[]",
            PgSqlDbType.Box | PgSqlDbType.Array
            );
    }

    [Fact]
    public Task path_closed()
        => AssertType(
            new PgSqlPath([new PgSqlPoint(1, 2), new PgSqlPoint(3, 4)], false),
            "((1,2),(3,4))",
            "path",
            PgSqlDbType.Path);

    [Fact]
    public Task path_open()
        => AssertType(
            new PgSqlPath([new PgSqlPoint(1, 2), new PgSqlPoint(3, 4)], true),
            "[(1,2),(3,4)]",
            "path",
            PgSqlDbType.Path);

    [Fact]
    public Task polygon()
        => AssertType(
            new PgSqlPolygon(new PgSqlPoint(1, 2), new PgSqlPoint(3, 4)),
            "((1,2),(3,4))",
            "polygon",
            PgSqlDbType.Polygon);

    [Fact]
    public Task circle()
        => AssertType(
            new PgSqlCircle(1, 2, 0.5),
            "<(1,2),0.5>",
            "circle",
            PgSqlDbType.Circle);
}

public sealed class GeometricTypeTests_NonMultiplexing() : GeometricTypeTests(MultiplexingMode.NonMultiplexing);
public sealed class GeometricTypeTests_Multiplexing() : GeometricTypeTests(MultiplexingMode.Multiplexing);
