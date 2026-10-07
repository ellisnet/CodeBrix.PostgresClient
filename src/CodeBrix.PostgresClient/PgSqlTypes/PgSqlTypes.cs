using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

// ReSharper disable once CheckNamespace
namespace CodeBrix.PostgresClient.PgSqlTypes; //was previously: NpgsqlTypes;

/// <summary>
/// Represents a PostgreSQL point type.
/// </summary>
/// <remarks>
/// See https://www.postgresql.org/docs/current/static/datatype-geometric.html
/// </remarks>
public struct PgSqlPoint(double x, double y) : IEquatable<PgSqlPoint>
{
    /// <summary>The horizontal (x) coordinate of the point.</summary>
    public double X { get; set; } = x;
    /// <summary>The vertical (y) coordinate of the point.</summary>
    public double Y { get; set; } = y;

    // ReSharper disable CompareOfFloatsByEqualityOperator
    /// <inheritdoc />
    public bool Equals(PgSqlPoint other) => X == other.X && Y == other.Y;
    // ReSharper restore CompareOfFloatsByEqualityOperator

    /// <inheritdoc />
    public override bool Equals(object obj)
        => obj is PgSqlPoint point && Equals(point);

    /// <summary>Determines whether two points have identical coordinates.</summary>
    public static bool operator ==(PgSqlPoint x, PgSqlPoint y) => x.Equals(y);

    /// <summary>Determines whether two points differ in either coordinate.</summary>
    public static bool operator !=(PgSqlPoint x, PgSqlPoint y) => !(x == y);

    /// <inheritdoc />
    public override int GetHashCode()
        => HashCode.Combine(X, Y);

    /// <summary>Returns the point in PostgreSQL text form, <c>(x,y)</c>, formatted with the invariant culture.</summary>
    public override string ToString()
        => string.Format(CultureInfo.InvariantCulture, "({0},{1})", X, Y);

    /// <summary>Deconstructs the point into its coordinates.</summary>
    /// <param name="x">Receives <see cref="X"/>.</param>
    /// <param name="y">Receives <see cref="Y"/>.</param>
    public void Deconstruct(out double x, out double y) => (x, y) = (X, Y);
}

/// <summary>
/// Represents a PostgreSQL line type.
/// </summary>
/// <remarks>
/// See https://www.postgresql.org/docs/current/static/datatype-geometric.html
/// </remarks>
public struct PgSqlLine(double a, double b, double c) : IEquatable<PgSqlLine>
{
    /// <summary>The <c>A</c> coefficient of the line equation <c>Ax + By + C = 0</c>.</summary>
    public double A { get; set; } = a;
    /// <summary>The <c>B</c> coefficient of the line equation <c>Ax + By + C = 0</c>.</summary>
    public double B { get; set; } = b;
    /// <summary>The constant <c>C</c> of the line equation <c>Ax + By + C = 0</c>.</summary>
    public double C { get; set; } = c;

    /// <summary>Returns the line in PostgreSQL text form, <c>{A,B,C}</c>, formatted with the invariant culture.</summary>
    public override string ToString()
        => string.Format(CultureInfo.InvariantCulture, "{{{0},{1},{2}}}", A, B, C);

    /// <inheritdoc />
    public override int GetHashCode()
        => HashCode.Combine(A, B, C);

    /// <inheritdoc />
    public bool Equals(PgSqlLine other)
        => A == other.A && B == other.B && C == other.C;

    /// <inheritdoc />
    public override bool Equals(object obj)
        => obj is PgSqlLine line && Equals(line);

    /// <summary>Determines whether two lines have identical coefficients.</summary>
    public static bool operator ==(PgSqlLine x, PgSqlLine y) => x.Equals(y);
    /// <summary>Determines whether two lines differ in any coefficient.</summary>
    public static bool operator !=(PgSqlLine x, PgSqlLine y) => !(x == y);

    /// <summary>Deconstructs the line into its equation coefficients.</summary>
    /// <param name="a">Receives <see cref="A"/>.</param>
    /// <param name="b">Receives <see cref="B"/>.</param>
    /// <param name="c">Receives <see cref="C"/>.</param>
    public void Deconstruct(out double a, out double b, out double c) => (a, b, c) = (A, B, C);
}

/// <summary>
/// Represents a PostgreSQL Line Segment type.
/// </summary>
public struct PgSqlLSeg : IEquatable<PgSqlLSeg>
{
    /// <summary>The first endpoint of the line segment.</summary>
    public PgSqlPoint Start { get; set; }
    /// <summary>The second endpoint of the line segment.</summary>
    public PgSqlPoint End { get; set; }

    /// <summary>Creates a line segment between two points.</summary>
    /// <param name="start">The first endpoint.</param>
    /// <param name="end">The second endpoint.</param>
    public PgSqlLSeg(PgSqlPoint start, PgSqlPoint end)
        : this()
    {
        Start = start;
        End = end;
    }

    /// <summary>Creates a line segment from the coordinates of its two endpoints.</summary>
    /// <param name="startx">The x coordinate of the first endpoint.</param>
    /// <param name="starty">The y coordinate of the first endpoint.</param>
    /// <param name="endx">The x coordinate of the second endpoint.</param>
    /// <param name="endy">The y coordinate of the second endpoint.</param>
    public PgSqlLSeg(double startx, double starty, double endx, double endy) : this()
    {
        Start = new PgSqlPoint(startx, starty);
        End = new PgSqlPoint(endx,   endy);
    }

    /// <summary>Returns the segment in PostgreSQL text form, <c>[(x1,y1),(x2,y2)]</c>.</summary>
    public override string ToString()
        => string.Format(CultureInfo.InvariantCulture, "[{0},{1}]", Start, End);

    /// <inheritdoc />
    public override int GetHashCode()
        => HashCode.Combine(Start.X, Start.Y, End.X, End.Y);

    /// <inheritdoc />
    public bool Equals(PgSqlLSeg other)
        => Start == other.Start && End == other.End;

    /// <inheritdoc />
    public override bool Equals(object obj)
        => obj is PgSqlLSeg seg && Equals(seg);

    /// <summary>Determines whether two segments have the same start and end points.</summary>
    public static bool operator ==(PgSqlLSeg x, PgSqlLSeg y) => x.Equals(y);
    /// <summary>Determines whether two segments differ in their start or end point.</summary>
    public static bool operator !=(PgSqlLSeg x, PgSqlLSeg y) => !(x == y);

    /// <summary>Deconstructs the segment into its endpoints.</summary>
    /// <param name="start">Receives <see cref="Start"/>.</param>
    /// <param name="end">Receives <see cref="End"/>.</param>
    public void Deconstruct(out PgSqlPoint start, out PgSqlPoint end) => (start, end) = (Start, End);
}

/// <summary>
/// Represents a PostgreSQL box type.
/// </summary>
/// <remarks>
/// See https://www.postgresql.org/docs/current/static/datatype-geometric.html
/// </remarks>
public struct PgSqlBox : IEquatable<PgSqlBox>
{
    PgSqlPoint _upperRight;
    /// <summary>The upper-right corner of the box. Setting it re-normalizes the box so that this corner keeps the larger coordinates, as PostgreSQL does.</summary>
    public PgSqlPoint UpperRight
    {
        get => _upperRight;
        set
        {
            _upperRight = value;
            NormalizeBox();
        }
    }

    PgSqlPoint _lowerLeft;
    /// <summary>The lower-left corner of the box. Setting it re-normalizes the box so that this corner keeps the smaller coordinates, as PostgreSQL does.</summary>
    public PgSqlPoint LowerLeft
    {
        get => _lowerLeft;
        set
        {
            _lowerLeft = value;
            NormalizeBox();
        }
    }

    /// <summary>Creates a box from two opposite corners; the corners are swapped as needed so that <see cref="UpperRight"/> holds the larger coordinates.</summary>
    /// <param name="upperRight">The upper-right corner.</param>
    /// <param name="lowerLeft">The lower-left corner.</param>
    public PgSqlBox(PgSqlPoint upperRight, PgSqlPoint lowerLeft) : this()
    {
        _upperRight = upperRight;
        _lowerLeft = lowerLeft;
        NormalizeBox();
    }

    /// <summary>Creates a box from the coordinates of its four edges.</summary>
    /// <param name="top">The y coordinate of the top edge.</param>
    /// <param name="right">The x coordinate of the right edge.</param>
    /// <param name="bottom">The y coordinate of the bottom edge.</param>
    /// <param name="left">The x coordinate of the left edge.</param>
    public PgSqlBox(double top, double right, double bottom, double left)
        : this(new PgSqlPoint(right, top), new PgSqlPoint(left, bottom)) { }

    /// <summary>The x coordinate of the box's left edge (the lower-left corner's x).</summary>
    public double Left => LowerLeft.X;
    /// <summary>The x coordinate of the box's right edge (the upper-right corner's x).</summary>
    public double Right => UpperRight.X;
    /// <summary>The y coordinate of the box's bottom edge (the lower-left corner's y).</summary>
    public double Bottom => LowerLeft.Y;
    /// <summary>The y coordinate of the box's top edge (the upper-right corner's y).</summary>
    public double Top => UpperRight.Y;
    /// <summary>The horizontal extent of the box (<see cref="Right"/> minus <see cref="Left"/>).</summary>
    public double Width => Right - Left;
    /// <summary>The vertical extent of the box (<see cref="Top"/> minus <see cref="Bottom"/>).</summary>
    public double Height => Top - Bottom;

    /// <summary>Whether the box has zero width or zero height.</summary>
    public bool IsEmpty => Width == 0 || Height == 0;

    /// <inheritdoc />
    public bool Equals(PgSqlBox other)
        => UpperRight == other.UpperRight && LowerLeft == other.LowerLeft;

    /// <inheritdoc />
    public override bool Equals(object obj)
        => obj is PgSqlBox box && Equals(box);

    /// <summary>Determines whether two boxes have the same corners.</summary>
    public static bool operator ==(PgSqlBox x, PgSqlBox y) => x.Equals(y);
    /// <summary>Determines whether two boxes differ in either corner.</summary>
    public static bool operator !=(PgSqlBox x, PgSqlBox y) => !(x == y);
    /// <summary>Returns the box in PostgreSQL text form, <c>(x1,y1),(x2,y2)</c> with the upper-right corner first.</summary>
    public override string ToString()
        => string.Format(CultureInfo.InvariantCulture, "{0},{1}", UpperRight, LowerLeft);

    /// <inheritdoc />
    public override int GetHashCode()
        => HashCode.Combine(Top, Right, Bottom, LowerLeft);

    // Swaps corners for isomorphic boxes, to mirror postgres behavior.
    // See: https://github.com/postgres/postgres/blob/af2324fabf0020e464b0268be9ef03e8f46ed84b/src/backend/utils/adt/geo_ops.c#L435-L447
    void NormalizeBox()
    {
        if (_upperRight.X < _lowerLeft.X)
            (_upperRight.X, _lowerLeft.X) = (_lowerLeft.X, _upperRight.X);

        if (_upperRight.Y < _lowerLeft.Y)
            (_upperRight.Y, _lowerLeft.Y) = (_lowerLeft.Y, _upperRight.Y);
    }

    /// <summary>Deconstructs the box into its corners.</summary>
    /// <param name="lowerLeft">Receives <see cref="LowerLeft"/>.</param>
    /// <param name="upperRight">Receives <see cref="UpperRight"/>.</param>
    public void Deconstruct(out PgSqlPoint lowerLeft, out PgSqlPoint upperRight)
    {
        lowerLeft = LowerLeft;
        upperRight = UpperRight;
    }

    /// <summary>Deconstructs the box into its edge coordinates.</summary>
    /// <param name="left">Receives <see cref="Left"/>.</param>
    /// <param name="right">Receives <see cref="Right"/>.</param>
    /// <param name="bottom">Receives <see cref="Bottom"/>.</param>
    /// <param name="top">Receives <see cref="Top"/>.</param>
    public void Deconstruct(out double left, out double right, out double bottom, out double top)
    {
        left = Left;
        right = Right;
        bottom = Bottom;
        top = Top;
    }

    /// <summary>Deconstructs the box into its edge coordinates and dimensions.</summary>
    /// <param name="left">Receives <see cref="Left"/>.</param>
    /// <param name="right">Receives <see cref="Right"/>.</param>
    /// <param name="bottom">Receives <see cref="Bottom"/>.</param>
    /// <param name="top">Receives <see cref="Top"/>.</param>
    /// <param name="width">Receives <see cref="Width"/>.</param>
    /// <param name="height">Receives <see cref="Height"/>.</param>
    public void Deconstruct(out double left, out double right, out double bottom, out double top, out double width, out double height)
    {
        left = Left;
        right = Right;
        bottom = Bottom;
        top = Top;
        width = Width;
        height = Height;
    }
}

/// <summary>
/// Represents a PostgreSQL Path type.
/// </summary>
public struct PgSqlPath : IList<PgSqlPoint>, IEquatable<PgSqlPath>
{
    List<PgSqlPoint> _points;

    List<PgSqlPoint> Points => _points ??= [];

    /// <summary>Whether the path is open (its last point is not connected back to its first); a closed path is a cycle.</summary>
    public bool Open { get; set; }

    /// <summary>Creates an empty, closed path.</summary>
    public PgSqlPath()
        => _points = [];

    /// <summary>Creates a path containing the given points.</summary>
    /// <param name="points">The points of the path, in order.</param>
    /// <param name="open"><see langword="true"/> for an open path, <see langword="false"/> for a closed one.</param>
    public PgSqlPath(IEnumerable<PgSqlPoint> points, bool open)
    {
        _points = [..points];
        Open = open;
    }

    /// <summary>Creates a closed path containing the given points.</summary>
    /// <param name="points">The points of the path, in order.</param>
    public PgSqlPath(IEnumerable<PgSqlPoint> points) : this(points, false) {}
    /// <summary>Creates a closed path containing the given points.</summary>
    /// <param name="points">The points of the path, in order.</param>
    public PgSqlPath(params PgSqlPoint[] points) : this(points, false) {}

    /// <summary>Creates an empty path that is open or closed as specified.</summary>
    /// <param name="open"><see langword="true"/> for an open path, <see langword="false"/> for a closed one.</param>
    public PgSqlPath(bool open) : this()
    {
        _points = [];
        Open = open;
    }

    /// <summary>Creates an empty path with the given initial capacity, open or closed as specified.</summary>
    /// <param name="capacity">The number of points the path can hold before its storage must grow.</param>
    /// <param name="open"><see langword="true"/> for an open path, <see langword="false"/> for a closed one.</param>
    public PgSqlPath(int capacity, bool open) : this()
    {
        _points = new List<PgSqlPoint>(capacity);
        Open = open;
    }

    /// <summary>Creates an empty, closed path with the given initial capacity.</summary>
    /// <param name="capacity">The number of points the path can hold before its storage must grow.</param>
    public PgSqlPath(int capacity) : this(capacity, false) {}

    /// <summary>Gets or sets the point at the specified index of the path.</summary>
    /// <param name="index">The zero-based index of the point.</param>
    public PgSqlPoint this[int index]
    {
        get => Points[index];
        set => Points[index] = value;
    }

    /// <summary>The number of points the path's underlying storage can hold without growing.</summary>
    public int Capacity => Points.Capacity;
    /// <summary>The number of points in the path.</summary>
    public int Count => _points?.Count ?? 0;
    /// <summary>Always <see langword="false"/>; a path can be modified.</summary>
    public bool IsReadOnly => false;

    /// <inheritdoc />
    public int IndexOf(PgSqlPoint item) => Points.IndexOf(item);
    /// <inheritdoc />
    public void Insert(int index, PgSqlPoint item) => Points.Insert(index, item);
    /// <inheritdoc />
    public void RemoveAt(int index) => Points.RemoveAt(index);
    /// <inheritdoc />
    public void Add(PgSqlPoint item) => Points.Add(item);
    /// <inheritdoc />
    public void Clear() => Points.Clear();
    /// <inheritdoc />
    public bool Contains(PgSqlPoint item) => Points.Contains(item);
    /// <inheritdoc />
    public void CopyTo(PgSqlPoint[] array, int arrayIndex) => Points.CopyTo(array, arrayIndex);
    /// <inheritdoc />
    public bool Remove(PgSqlPoint item) => Points.Remove(item);
    /// <inheritdoc />
    public IEnumerator<PgSqlPoint> GetEnumerator() => Points.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <inheritdoc />
    public bool Equals(PgSqlPath other)
    {
        if (Open != other.Open || Count != other.Count)
            return false;
        if (ReferenceEquals(_points, other._points))//Short cut for shallow copies.
            return true;
        for (var i = 0; i != Count; ++i)
            if (this[i] != other[i])
                return false;
        return true;
    }

    /// <inheritdoc />
    public override bool Equals(object obj)
        => obj is PgSqlPath path && Equals(path);

    /// <summary>Determines whether two paths have the same openness and the same points in the same order.</summary>
    public static bool operator ==(PgSqlPath x, PgSqlPath y) => x.Equals(y);
    /// <summary>Determines whether two paths differ in openness or in any point.</summary>
    public static bool operator !=(PgSqlPath x, PgSqlPath y) => !(x == y);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hashCode = new HashCode();
        hashCode.Add(Open);

        foreach (var point in this)
        {
            hashCode.Add(point.X);
            hashCode.Add(point.Y);
        }

        return hashCode.ToHashCode();
    }

    /// <summary>Returns the path in PostgreSQL text form: points enclosed in <c>[...]</c> for an open path or <c>(...)</c> for a closed one.</summary>
    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.Append(Open ? '[' : '(');
        int i;
        for (i = 0; i < Count; i++)
        {
            var p = _points[i];
            sb.AppendFormat(CultureInfo.InvariantCulture, "({0},{1})", p.X, p.Y);
            if (i < _points.Count - 1)
                sb.Append(',');
        }
        sb.Append(Open ? ']' : ')');
        return sb.ToString();
    }
}

/// <summary>
/// Represents a PostgreSQL Polygon type.
/// </summary>
public struct PgSqlPolygon : IList<PgSqlPoint>, IEquatable<PgSqlPolygon>
{
    List<PgSqlPoint> _points;

    List<PgSqlPoint> Points => _points ??= [];

    /// <summary>Creates an empty polygon.</summary>
    public PgSqlPolygon()
        => _points = [];

    /// <summary>Creates a polygon with the given vertices.</summary>
    /// <param name="points">The vertices of the polygon, in order.</param>
    public PgSqlPolygon(IEnumerable<PgSqlPoint> points)
        => _points = [..points];

    /// <summary>Creates a polygon with the given vertices.</summary>
    /// <param name="points">The vertices of the polygon, in order.</param>
    public PgSqlPolygon(params PgSqlPoint[] points) : this((IEnumerable<PgSqlPoint>) points) {}

    /// <summary>Creates an empty polygon with the given initial capacity.</summary>
    /// <param name="capacity">The number of vertices the polygon can hold before its storage must grow.</param>
    public PgSqlPolygon(int capacity)
        => _points = new List<PgSqlPoint>(capacity);

    /// <summary>Gets or sets the vertex at the specified index of the polygon.</summary>
    /// <param name="index">The zero-based index of the vertex.</param>
    public PgSqlPoint this[int index]
    {
        get => Points[index];
        set => Points[index] = value;
    }

    /// <summary>The number of vertices the polygon's underlying storage can hold without growing.</summary>
    public int Capacity => Points.Capacity;
    /// <summary>The number of vertices in the polygon.</summary>
    public int Count => _points?.Count ?? 0;
    /// <summary>Always <see langword="false"/>; a polygon can be modified.</summary>
    public bool IsReadOnly => false;

    /// <inheritdoc />
    public int IndexOf(PgSqlPoint item) => Points.IndexOf(item);
    /// <inheritdoc />
    public void Insert(int index, PgSqlPoint item) => Points.Insert(index, item);
    /// <inheritdoc />
    public void RemoveAt(int index) => Points.RemoveAt(index);
    /// <inheritdoc />
    public void Add(PgSqlPoint item) => Points.Add(item);
    /// <inheritdoc />
    public void Clear() => Points.Clear();
    /// <inheritdoc />
    public bool Contains(PgSqlPoint item) => Points.Contains(item);
    /// <inheritdoc />
    public void CopyTo(PgSqlPoint[] array, int arrayIndex) => Points.CopyTo(array, arrayIndex);
    /// <inheritdoc />
    public bool Remove(PgSqlPoint item) => Points.Remove(item);
    /// <inheritdoc />
    public IEnumerator<PgSqlPoint> GetEnumerator() => Points.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <inheritdoc />
    public bool Equals(PgSqlPolygon other)
    {
        if (Count != other.Count)
            return false;
        if (ReferenceEquals(_points, other._points))
            return true;
        for (var i = 0; i != Count; ++i)
            if (this[i] != other[i])
                return false;
        return true;
    }

    /// <inheritdoc />
    public override bool Equals(object obj)
        => obj is PgSqlPolygon polygon && Equals(polygon);

    /// <summary>Determines whether two polygons have the same vertices in the same order.</summary>
    public static bool operator ==(PgSqlPolygon x, PgSqlPolygon y) => x.Equals(y);
    /// <summary>Determines whether two polygons differ in any vertex.</summary>
    public static bool operator !=(PgSqlPolygon x, PgSqlPolygon y) => !(x == y);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hashCode = new HashCode();

        foreach (var point in this)
        {
            hashCode.Add(point.X);
            hashCode.Add(point.Y);
        }

        return hashCode.ToHashCode();
    }

    /// <summary>Returns the polygon in PostgreSQL text form, <c>((x1,y1),...)</c>.</summary>
    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.Append('(');
        int i;
        for (i = 0; i < Count; i++)
        {
            var p = _points[i];
            sb.AppendFormat(CultureInfo.InvariantCulture, "({0},{1})", p.X, p.Y);
            if (i < _points.Count - 1) {
                sb.Append(",");
            }
        }
        sb.Append(')');
        return sb.ToString();
    }
}

/// <summary>
/// Represents a PostgreSQL Circle type.
/// </summary>
public struct PgSqlCircle(double x, double y, double radius) : IEquatable<PgSqlCircle>
{
    /// <summary>The x coordinate of the circle's center.</summary>
    public double X { get; set; } = x;
    /// <summary>The y coordinate of the circle's center.</summary>
    public double Y { get; set; } = y;
    /// <summary>The radius of the circle.</summary>
    public double Radius { get; set; } = radius;

    /// <summary>Creates a circle from its center point and radius.</summary>
    /// <param name="center">The center of the circle.</param>
    /// <param name="radius">The radius of the circle.</param>
    public PgSqlCircle(PgSqlPoint center, double radius)
        : this(center.X, center.Y, radius)
    {
    }

    /// <summary>The center of the circle, composed from (and written back to) <see cref="X"/> and <see cref="Y"/>.</summary>
    public PgSqlPoint Center
    {
        get => new(X, Y);
        set => (X, Y) = (value.X, value.Y);
    }

    // ReSharper disable CompareOfFloatsByEqualityOperator
    /// <inheritdoc />
    public bool Equals(PgSqlCircle other)
        => X == other.X && Y == other.Y && Radius == other.Radius;
    // ReSharper restore CompareOfFloatsByEqualityOperator

    /// <inheritdoc />
    public override bool Equals(object obj)
        => obj is PgSqlCircle circle && Equals(circle);

    /// <summary>Returns the circle in PostgreSQL text form, <c>&lt;(x,y),r&gt;</c>.</summary>
    public override string ToString()
        => string.Format(CultureInfo.InvariantCulture, "<({0},{1}),{2}>", X, Y, Radius);

    /// <summary>Determines whether two circles have the same center and radius.</summary>
    public static bool operator ==(PgSqlCircle x, PgSqlCircle y) => x.Equals(y);
    /// <summary>Determines whether two circles differ in center or radius.</summary>
    public static bool operator !=(PgSqlCircle x, PgSqlCircle y) => !(x == y);

    /// <inheritdoc />
    public override int GetHashCode()
        => HashCode.Combine(X, Y, Radius);

    /// <summary>Deconstructs the circle into its center coordinates and radius.</summary>
    /// <param name="x">Receives <see cref="X"/>.</param>
    /// <param name="y">Receives <see cref="Y"/>.</param>
    /// <param name="radius">Receives <see cref="Radius"/>.</param>
    public void Deconstruct(out double x, out double y, out double radius)
    {
        x = X;
        y = Y;
        radius = Radius;
    }

    /// <summary>Deconstructs the circle into its center point and radius.</summary>
    /// <param name="center">Receives <see cref="Center"/>.</param>
    /// <param name="radius">Receives <see cref="Radius"/>.</param>
    public void Deconstruct(out PgSqlPoint center, out double radius)
    {
        center = Center;
        radius = Radius;
    }
}

/// <summary>
/// Represents a PostgreSQL inet type, which is a combination of an IPAddress and a subnet mask.
/// </summary>
/// <remarks>
/// https://www.postgresql.org/docs/current/static/datatype-net-types.html
/// </remarks>
public readonly record struct PgSqlInet
{
    /// <summary>The IPv4 or IPv6 host address.</summary>
    public IPAddress Address { get; }
    /// <summary>The number of bits in the network prefix (subnet mask length).</summary>
    public byte Netmask { get; }

    /// <summary>Creates an inet value from an address and a prefix length.</summary>
    /// <param name="address">An IPv4 or IPv6 address.</param>
    /// <param name="netmask">The network prefix length in bits.</param>
    /// <exception cref="ArgumentException"><paramref name="address"/> is not an IPv4 or IPv6 address.</exception>
    public PgSqlInet(IPAddress address, byte netmask)
    {
        CheckAddressFamily(address);
        Address = address;
        Netmask = netmask;
    }

    /// <summary>Creates an inet value for a single host, using a prefix length of 32 for IPv4 or 128 for IPv6.</summary>
    /// <param name="address">An IPv4 or IPv6 address.</param>
    public PgSqlInet(IPAddress address)
        : this(address, (byte)(address.AddressFamily == AddressFamily.InterNetwork ? 32 : 128))
    {
    }

    /// <summary>Parses an inet value from text of the form <c>address</c> or <c>address/prefix</c>.</summary>
    /// <param name="addr">The text to parse.</param>
    /// <exception cref="FormatException"><paramref name="addr"/> contains more than one '/'.</exception>
    public PgSqlInet(string addr)
    {
        switch (addr.Split('/'))
        {
        case { Length: 2 } segments:
            (Address, Netmask) = (IPAddress.Parse(segments[0]), byte.Parse(segments[1]));
            break;
        case { Length: 1 } segments:
            var ipAddr = IPAddress.Parse(segments[0]);
            CheckAddressFamily(ipAddr);
            (Address, Netmask) = (
                ipAddr,
                ipAddr.AddressFamily == AddressFamily.InterNetworkV6 ? (byte)128 : (byte)32);
            break;
        default:
            throw new FormatException("Invalid number of parts in CIDR specification");
        }
    }

    /// <summary>Returns the bare address for a single-host value, otherwise <c>address/prefix</c>.</summary>
    public override string ToString()
        => (Address?.AddressFamily == AddressFamily.InterNetwork && Netmask == 32) ||
           (Address?.AddressFamily == AddressFamily.InterNetworkV6 && Netmask == 128)
            ? Address.ToString()
            : $"{Address}/{Netmask}";

    /// <summary>Extracts the address, discarding the prefix length.</summary>
    /// <param name="inet">The inet value to convert.</param>
    public static explicit operator IPAddress(PgSqlInet inet)
        => inet.Address;

    /// <summary>Converts an address to a single-host inet value.</summary>
    /// <param name="ip">The address to convert.</param>
    public static implicit operator PgSqlInet(IPAddress ip)
        => new(ip);

    /// <summary>Converts an <see cref="IPNetwork"/> to an inet value with the same base address and prefix length.</summary>
    /// <param name="cidr">The network to convert.</param>
    /// <exception cref="ArgumentOutOfRangeException">The prefix length does not fit in a byte.</exception>
    public static implicit operator PgSqlInet(IPNetwork cidr)
        => new(
            cidr.BaseAddress,
            cidr.PrefixLength <= byte.MaxValue
                ? (byte)cidr.PrefixLength
                : throw new ArgumentOutOfRangeException(nameof(cidr), "IPNetwork.PrefixLength is too large to fit in a byte"));

    /// <summary>Deconstructs the value into its address and prefix length.</summary>
    /// <param name="address">Receives <see cref="Address"/>.</param>
    /// <param name="netmask">Receives <see cref="Netmask"/>.</param>
    public void Deconstruct(out IPAddress address, out byte netmask)
    {
        address = Address;
        netmask = Netmask;
    }

    static void CheckAddressFamily(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork && address.AddressFamily != AddressFamily.InterNetworkV6)
            throw new ArgumentException("Only IPAddress of InterNetwork or InterNetworkV6 address families are accepted", nameof(address));
    }
}

/// <summary>
/// Represents a PostgreSQL tid value
/// </summary>
/// <remarks>
/// https://www.postgresql.org/docs/current/static/datatype-oid.html
/// </remarks>
public readonly struct PgSqlTid(uint blockNumber, ushort offsetNumber) : IEquatable<PgSqlTid>
{
    /// <summary>
    /// Block number
    /// </summary>
    public uint BlockNumber { get; } = blockNumber;

    /// <summary>
    /// Tuple index within block
    /// </summary>
    public ushort OffsetNumber { get; } = offsetNumber;

    /// <inheritdoc />
    public bool Equals(PgSqlTid other)
        => BlockNumber == other.BlockNumber && OffsetNumber == other.OffsetNumber;

    /// <inheritdoc />
    public override bool Equals(object o)
        => o is PgSqlTid tid && Equals(tid);

    /// <inheritdoc />
    public override int GetHashCode() => (int)BlockNumber ^ OffsetNumber;
    /// <summary>Determines whether two tuple identifiers refer to the same block and offset.</summary>
    public static bool operator ==(PgSqlTid left, PgSqlTid right) => left.Equals(right);
    /// <summary>Determines whether two tuple identifiers differ in block or offset.</summary>
    public static bool operator !=(PgSqlTid left, PgSqlTid right) => !(left == right);
    /// <summary>Returns the tuple identifier in PostgreSQL text form, <c>(block,offset)</c>.</summary>
    public override string ToString() => $"({BlockNumber},{OffsetNumber})";

    /// <summary>Deconstructs the tuple identifier into its block number and offset.</summary>
    /// <param name="blockNumber">Receives <see cref="BlockNumber"/>.</param>
    /// <param name="offsetNumber">Receives <see cref="OffsetNumber"/>.</param>
    public void Deconstruct(out uint blockNumber, out ushort offsetNumber)
    {
        blockNumber = BlockNumber;
        offsetNumber = OffsetNumber;
    }
}
