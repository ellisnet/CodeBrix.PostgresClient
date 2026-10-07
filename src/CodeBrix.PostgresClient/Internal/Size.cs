using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace CodeBrix.PostgresClient.Internal; //was previously: Npgsql.Internal;

/// <summary>Describes how precisely a <see cref="Size"/> is known.</summary>
[Experimental(PgSqlDiagnostics.ConvertersExperimental)]
public enum SizeKind
{
    /// <summary>The size is not known up front.</summary>
    Unknown = 0,
    /// <summary>The size is known exactly.</summary>
    Exact,
    /// <summary>The size is a maximum; the actual size may be smaller.</summary>
    UpperBound
}

/// <summary>A byte count used for value sizes and buffer requirements, which may be exact, an upper bound, or unknown.</summary>
[Experimental(PgSqlDiagnostics.ConvertersExperimental)]
[DebuggerDisplay("{DebuggerDisplay,nq}")]
public readonly struct Size : IEquatable<Size>
{
    readonly int _value;
    readonly SizeKind _kind;

    Size(SizeKind kind, int value)
    {
        _value = value;
        _kind = kind;
    }

    /// <summary>The byte count; throws <see cref="InvalidOperationException"/> when <see cref="Kind"/> is <see cref="SizeKind.Unknown"/>.</summary>
    public int Value
    {
        get
        {
            if (_kind is SizeKind.Unknown)
                ThrowHelper.ThrowInvalidOperationException("Cannot get value from default or Unknown kind");
            return _value;
        }
    }

    internal int GetValueOrDefault() => _value;

    /// <summary>Whether this size is exact, an upper bound, or unknown.</summary>
    public SizeKind Kind => _kind;

    /// <summary>Creates an exact size.</summary>
    /// <param name="byteCount">The exact number of bytes.</param>
    /// <returns>The size.</returns>
    public static Size Create(int byteCount) => new(SizeKind.Exact, byteCount);
    /// <summary>Creates an upper-bound size.</summary>
    /// <param name="byteCount">The maximum number of bytes.</param>
    /// <returns>The size.</returns>
    public static Size CreateUpperBound(int byteCount) => new(SizeKind.UpperBound, byteCount);
    /// <summary>A size whose byte count is not known.</summary>
    public static Size Unknown { get; } = new(SizeKind.Unknown, 0);
    /// <summary>An exact size of zero bytes.</summary>
    public static Size Zero { get; } = new(SizeKind.Exact, 0);

    /// <summary>Adds two sizes without throwing on overflow; the result is unknown if either size is unknown, and an upper bound if either is an upper bound.</summary>
    /// <param name="other">The size to add.</param>
    /// <param name="result">Receives the combined size.</param>
    /// <returns><see langword="false"/> if the byte counts overflowed.</returns>
    public bool TryCombine(Size other, out Size result)
    {
        if (_kind is SizeKind.Unknown || other._kind is SizeKind.Unknown)
        {
            result = Unknown;
            return true;
        }

        var sum = unchecked(_value + other._value);
        if ((_value >= 0 && sum < other._value) || (_value < 0 && sum > other._value))
        {
            result = default;
            return false;
        }

        if (_kind is SizeKind.UpperBound || other._kind is SizeKind.UpperBound)
        {
            result = CreateUpperBound(sum);
            return true;
        }

        result = Create(sum);
        return true;
    }

    /// <summary>Adds two sizes; the result is unknown if either size is unknown, and an upper bound if either is an upper bound.</summary>
    /// <param name="other">The size to add.</param>
    /// <returns>The combined size.</returns>
    /// <exception cref="OverflowException">The combined byte count overflows.</exception>
    public Size Combine(Size other)
    {
        if (_kind is SizeKind.Unknown || other._kind is SizeKind.Unknown)
            return Unknown;

        if (_kind is SizeKind.UpperBound || other._kind is SizeKind.UpperBound)
            return CreateUpperBound(checked(_value + other._value));

        return Create(checked(_value + other._value));
    }

    /// <summary>Converts a byte count to an exact size.</summary>
    /// <param name="value">The exact number of bytes.</param>
    public static implicit operator Size(int value) => Create(value);

    string DebuggerDisplay => ToString();

    /// <inheritdoc />
    public bool Equals(Size other) => _value == other._value && _kind == other.Kind;
    /// <inheritdoc />
    public override bool Equals(object obj) => obj is Size other && Equals(other);
    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(_value, (int)_kind);
    /// <summary>Determines whether two sizes have the same kind and byte count.</summary>
    public static bool operator ==(Size left, Size right) => left.Equals(right);
    /// <summary>Determines whether two sizes differ in kind or byte count.</summary>
    public static bool operator !=(Size left, Size right) => !left.Equals(right);

    /// <summary>Returns the byte count and kind, e.g. <c>4 (Exact)</c>, or <c>Unknown</c>.</summary>
    public override string ToString() => _kind switch
    {
        SizeKind.Exact or SizeKind.UpperBound => $"{_value} ({_kind.ToString()})",
        SizeKind.Unknown => nameof(SizeKind.Unknown),
        _ => throw new ArgumentOutOfRangeException()
    };
}
