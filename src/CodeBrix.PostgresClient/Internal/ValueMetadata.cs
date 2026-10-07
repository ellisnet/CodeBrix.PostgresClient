using System.Diagnostics.CodeAnalysis;

namespace CodeBrix.PostgresClient.Internal; //was previously: Npgsql.Internal;

/// <summary>Describes a value (or nested part of a value) being read or written: its format, size, buffer requirement and any converter write state.</summary>
[Experimental(PgSqlDiagnostics.ConvertersExperimental)]
public readonly struct ValueMetadata
{
    /// <summary>The data format the value is encoded in.</summary>
    public required DataFormat Format { get; init; }
    /// <summary>The number of bytes that must be buffered before the value is read or written.</summary>
    public required Size BufferRequirement { get; init; }
    /// <summary>The size of the value in bytes.</summary>
    public required Size Size { get; init; }
    /// <summary>Converter-specific state computed during size calculation and passed to the write, or <see langword="null"/>.</summary>
    public object WriteState { get; init; }
}
