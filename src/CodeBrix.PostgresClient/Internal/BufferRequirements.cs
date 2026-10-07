using System;
using System.Diagnostics.CodeAnalysis;

namespace CodeBrix.PostgresClient.Internal; //was previously: Npgsql.Internal;

/// <summary>The number of bytes a converter needs to have buffered before it reads or writes a value, specified separately for reading and writing.</summary>
[Experimental(PgSqlDiagnostics.ConvertersExperimental)]
public readonly struct BufferRequirements : IEquatable<BufferRequirements>
{
    readonly Size _read;
    readonly Size _write;

    BufferRequirements(Size read, Size write)
    {
        _read = read;
        _write = write;
    }

    /// <summary>The buffer requirement for reading a value.</summary>
    public Size Read => _read;
    /// <summary>The buffer requirement for writing a value.</summary>
    public Size Write => _write;

    /// Streaming
    public static BufferRequirements None => new(Size.Unknown, Size.Unknown);
    /// Entire value should be buffered
    public static BufferRequirements Value => new(Size.CreateUpperBound(int.MaxValue), Size.CreateUpperBound(int.MaxValue));
    /// Fixed size value should be buffered
    public static BufferRequirements CreateFixedSize(int byteCount) => new(byteCount, byteCount);
    /// Custom requirements
    public static BufferRequirements Create(Size value) => new(value, value);
    /// <summary>Creates requirements with different sizes for reading and writing.</summary>
    /// <param name="read">The requirement for reading.</param>
    /// <param name="write">The requirement for writing.</param>
    /// <returns>The requirements.</returns>
    public static BufferRequirements Create(Size read, Size write) => new(read, write);

    /// <summary>Adds sizes to the read and write requirements, e.g. to account for a header written before the value.</summary>
    /// <param name="read">The size added to the read requirement.</param>
    /// <param name="write">The size added to the write requirement.</param>
    /// <returns>The combined requirements.</returns>
    public BufferRequirements Combine(Size read, Size write)
        => new(_read.Combine(read), _write.Combine(write));

    /// <summary>Adds another set of requirements to this one, read to read and write to write.</summary>
    /// <param name="other">The requirements to add.</param>
    /// <returns>The combined requirements.</returns>
    public BufferRequirements Combine(BufferRequirements other)
        => Combine(other._read, other._write);

    /// <summary>Adds a fixed number of bytes to both the read and write requirements.</summary>
    /// <param name="byteCount">The number of bytes to add.</param>
    /// <returns>The combined requirements.</returns>
    public BufferRequirements Combine(int byteCount)
        => Combine(CreateFixedSize(byteCount));

    /// <inheritdoc />
    public bool Equals(BufferRequirements other) => _read.Equals(other._read) && _write.Equals(other._write);
    /// <inheritdoc />
    public override bool Equals(object obj) => obj is BufferRequirements other && Equals(other);
    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(_read, _write);
    /// <summary>Determines whether two requirements have the same read and write sizes.</summary>
    public static bool operator ==(BufferRequirements left, BufferRequirements right) => left.Equals(right);
    /// <summary>Determines whether two requirements differ in their read or write size.</summary>
    public static bool operator !=(BufferRequirements left, BufferRequirements right) => !left.Equals(right);
}
