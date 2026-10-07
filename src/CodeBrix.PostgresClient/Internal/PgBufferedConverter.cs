using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.PostgresClient.Internal; //was previously: Npgsql.Internal;

/// <summary>Base class for converters whose values are always fully buffered before reading or writing, so they only implement synchronous <see cref="ReadCore"/> and <see cref="WriteCore"/>; async reads and writes complete synchronously.</summary>
/// <typeparam name="T">The CLR type converted.</typeparam>
/// <param name="customDbNullPredicate">Whether the converter overrides <see cref="PgConverter{T}.IsDbNullValue"/> to decide which values are database NULL.</param>
[Experimental(PgSqlDiagnostics.ConvertersExperimental)]
public abstract class PgBufferedConverter<T>(bool customDbNullPredicate = false) : PgConverter<T>(customDbNullPredicate)
{
    /// <summary>Reads the value from the reader; the whole value is already buffered.</summary>
    /// <param name="reader">The reader positioned at the value.</param>
    /// <returns>The value read.</returns>
    protected abstract T ReadCore(PgReader reader);
    /// <summary>Writes the value to the writer; the buffer already has room for the whole value.</summary>
    /// <param name="writer">The writer.</param>
    /// <param name="value">The value to write.</param>
    protected abstract void WriteCore(PgWriter writer, T value);

    /// <summary>Throws <see cref="NotSupportedException"/>; converters that report an exact buffer requirement never need it, others must override it.</summary>
    /// <param name="context">The data format and buffer requirement of the write.</param>
    /// <param name="value">The value to measure.</param>
    /// <param name="writeState">Receives converter-specific state that is passed on to the write.</param>
    /// <returns>The exact size in bytes.</returns>
    public override Size GetSize(SizeContext context, T value, ref object writeState)
        => throw new NotSupportedException();

    /// <inheritdoc />
    public sealed override T Read(PgReader reader)
    {
        // We check FieldAtStart to speed up simple value reads, as field level buffering was handled by reader.StartRead() already.
        if (!reader.FieldAtStart && reader.ShouldBufferCurrent())
            ThrowIORequired(reader.CurrentBufferRequirement);

        return ReadCore(reader);
    }

    /// <inheritdoc />
    public sealed override ValueTask<T> ReadAsync(PgReader reader, CancellationToken cancellationToken = default)
        => new(Read(reader));

    internal sealed override ValueTask<object> ReadAsObject(bool async, PgReader reader, CancellationToken cancellationToken)
        => new(Read(reader));

    /// <inheritdoc />
    public sealed override void Write(PgWriter writer, T value)
    {
        if (!writer.BufferingWrite && writer.ShouldFlush(writer.CurrentBufferRequirement))
            ThrowIORequired(writer.CurrentBufferRequirement);

        WriteCore(writer, value);
    }

    /// <inheritdoc />
    public sealed override ValueTask WriteAsync(PgWriter writer, [DisallowNull] T value, CancellationToken cancellationToken = default)
    {
        Write(writer, value);
        return new();
    }

    internal sealed override ValueTask WriteAsObject(bool async, PgWriter writer, object value, CancellationToken cancellationToken)
    {
        Write(writer, (T)value);
        return new();
    }
}
