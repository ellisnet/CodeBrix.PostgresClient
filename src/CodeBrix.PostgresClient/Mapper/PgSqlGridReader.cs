using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace CodeBrix.PostgresClient;

/// <summary>
/// Reads the result sets of a <see cref="PgSqlMapper.QueryMultiple"/> batch in order. Each call to
/// <see cref="Read{T}"/> materializes the current result set (with the same rules as
/// <see cref="PgSqlMapper.Query{T}"/>) and advances to the next one.
/// </summary>
[RequiresUnreferencedCode("PgSqlMapper reads parameter objects and populates result types through reflection; the members of those types may be trimmed away. Use PgSqlCommand and PgSqlDataReader directly in trimmed applications.")]
[RequiresDynamicCode("PgSqlMapper creates result objects and dynamic rows through reflection, which may need code that is not available ahead of time. Use PgSqlCommand and PgSqlDataReader directly in NativeAOT applications.")]
public sealed class PgSqlGridReader : IDisposable
{
    private readonly PgSqlCommand _command;
    private readonly PgSqlDataReader _reader;
    private readonly PgSqlConnection _connectionToClose;
    private bool _isConsumed;
    private bool _disposed;

    /// <summary>
    /// Creates a grid reader over the result sets of an executed multi-statement command. Instances
    /// are produced by <see cref="PgSqlMapper.QueryMultiple"/> rather than constructed directly.
    /// </summary>
    /// <param name="command">The executed command; disposed with this reader.</param>
    /// <param name="reader">The open data reader positioned on the first result set.</param>
    /// <param name="connectionToClose">The connection to close on disposal when this call opened it;
    /// null when the connection was already open and should be left open.</param>
    internal PgSqlGridReader(PgSqlCommand command, PgSqlDataReader reader, PgSqlConnection connectionToClose)
    {
        _command = command;
        _reader = reader;
        _connectionToClose = connectionToClose;
    }

    /// <summary>
    /// True when every result set of the batch has been read.
    /// </summary>
    public bool IsConsumed => _isConsumed;

    /// <summary>
    /// Materializes the current result set as <typeparamref name="T"/> rows and advances to the
    /// next result set.
    /// </summary>
    /// <typeparam name="T">The result row type (see <see cref="PgSqlMapper.Query{T}"/>).</typeparam>
    /// <returns>The materialized rows of the current result set (buffered).</returns>
    /// <exception cref="InvalidOperationException">Thrown when all result sets have already been consumed.</exception>
    public IEnumerable<T> Read<T>()
    {
        ThrowIfDisposed();
        if (_isConsumed) { throw new InvalidOperationException("All result sets of this grid reader have already been consumed."); }
        var rows = new List<T>();
        while (_reader.Read()) { rows.Add(PgSqlMapper.MaterializeRow<T>(_reader)); }
        if (!_reader.NextResult()) { _isConsumed = true; }
        return rows;
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(_disposed, this);

    /// <summary>
    /// Disposes the underlying reader and command, and closes the connection when it was opened by
    /// the QueryMultiple call that created this grid reader.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) { return; }
        _disposed = true;
        _reader.Dispose();
        _command.Dispose();
        _connectionToClose?.Close();
    }
}
