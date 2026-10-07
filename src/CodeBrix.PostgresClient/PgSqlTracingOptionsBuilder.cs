using System;
using System.Diagnostics;

namespace CodeBrix.PostgresClient; //was previously: Npgsql;

/// <summary>
/// A builder to configure CodeBrix.PostgresClient's support for OpenTelemetry tracing.
/// </summary>
public sealed class PgSqlTracingOptionsBuilder
{
    Func<PgSqlCommand, bool> _commandFilter;
    Func<PgSqlBatch, bool> _batchFilter;
    Action<Activity, PgSqlCommand> _commandEnrichmentCallback;
    Action<Activity, PgSqlBatch> _batchEnrichmentCallback;
    Func<PgSqlCommand, string> _commandSpanNameProvider;
    Func<PgSqlBatch, string> _batchSpanNameProvider;
    bool _enableFirstResponseEvent = true;
    bool _enablePhysicalOpenTracing = true;

    Func<string, bool> _copyOperationFilter;
    Action<Activity, string> _copyOperationEnrichmentCallback;
    Func<string, string> _copyOperationSpanNameProvider;

    internal PgSqlTracingOptionsBuilder()
    {
    }

    /// <summary>
    /// Configures a filter function that determines whether to emit tracing information for an <see cref="PgSqlCommand"/>.
    /// By default, tracing information is emitted for all commands.
    /// </summary>
    public PgSqlTracingOptionsBuilder ConfigureCommandFilter(Func<PgSqlCommand, bool> commandFilter)
    {
        _commandFilter = commandFilter;
        return this;
    }

    /// <summary>
    /// Configures a filter function that determines whether to emit tracing information for an <see cref="PgSqlBatch"/>.
    /// By default, tracing information is emitted for all batches.
    /// </summary>
    public PgSqlTracingOptionsBuilder ConfigureBatchFilter(Func<PgSqlBatch, bool> batchFilter)
    {
        _batchFilter = batchFilter;
        return this;
    }

    /// <summary>
    /// Configures a callback that can enrich the <see cref="Activity"/> emitted for the given <see cref="PgSqlCommand"/>.
    /// </summary>
    public PgSqlTracingOptionsBuilder ConfigureCommandEnrichmentCallback(Action<Activity, PgSqlCommand> commandEnrichmentCallback)
    {
        _commandEnrichmentCallback = commandEnrichmentCallback;
        return this;
    }

    /// <summary>
    /// Configures a callback that can enrich the <see cref="Activity"/> emitted for the given <see cref="PgSqlBatch"/>.
    /// </summary>
    public PgSqlTracingOptionsBuilder ConfigureBatchEnrichmentCallback(Action<Activity, PgSqlBatch> batchEnrichmentCallback)
    {
        _batchEnrichmentCallback = batchEnrichmentCallback;
        return this;
    }

    /// <summary>
    /// Configures a callback that provides the tracing span's name for an <see cref="PgSqlCommand"/>. If <c>null</c>, the default standard
    /// span name is used, which is the database name.
    /// </summary>
    public PgSqlTracingOptionsBuilder ConfigureCommandSpanNameProvider(Func<PgSqlCommand, string> commandSpanNameProvider)
    {
        _commandSpanNameProvider = commandSpanNameProvider;
        return this;
    }

    /// <summary>
    /// Configures a callback that provides the tracing span's name for an <see cref="PgSqlBatch"/>. If <c>null</c>, the default standard
    /// span name is used, which is the database name.
    /// </summary>
    public PgSqlTracingOptionsBuilder ConfigureBatchSpanNameProvider(Func<PgSqlBatch, string> batchSpanNameProvider)
    {
        _batchSpanNameProvider = batchSpanNameProvider;
        return this;
    }

    /// <summary>
    /// Gets or sets a value indicating whether to enable the "time-to-first-read" event.
    /// Default is true to preserve existing behavior.
    /// </summary>
    public PgSqlTracingOptionsBuilder EnableFirstResponseEvent(bool enable = true)
    {
        _enableFirstResponseEvent = enable;
        return this;
    }

    /// <summary>
    /// Gets or sets a value indicating whether to trace physical connection open.
    /// Default is true to preserve existing behavior.
    /// </summary>
    public PgSqlTracingOptionsBuilder EnablePhysicalOpenTracing(bool enable = true)
    {
        _enablePhysicalOpenTracing = enable;
        return this;
    }

    /// <summary>
    /// Configures a filter function that determines whether to emit tracing information for a copy operation.
    /// By default, tracing information is emitted for all copy operations.
    /// </summary>
    public PgSqlTracingOptionsBuilder ConfigureCopyOperationFilter(Func<string, bool> copyOperationFilter)
    {
        _copyOperationFilter = copyOperationFilter;
        return this;
    }

    /// <summary>
    /// Configures a callback that can enrich the <see cref="Activity"/> emitted for a given copy operation.
    /// </summary>
    public PgSqlTracingOptionsBuilder ConfigureCopyOperationEnrichmentCallback(Action<Activity, string> copyOperationEnrichmentCallback)
    {
        _copyOperationEnrichmentCallback = copyOperationEnrichmentCallback;
        return this;
    }

    /// <summary>
    /// Configures a callback that provides the tracing span's name for a copy operation. If <c>null</c>, the default standard
    /// span name is used, which is the database name.
    /// </summary>
    public PgSqlTracingOptionsBuilder ConfigureCopyOperationSpanNameProvider(Func<string, string> copyOperationSpanNameProvider)
    {
        _copyOperationSpanNameProvider = copyOperationSpanNameProvider;
        return this;
    }

    internal PgSqlTracingOptions Build() => new()
    {
        CommandFilter = _commandFilter,
        BatchFilter = _batchFilter,
        CommandEnrichmentCallback = _commandEnrichmentCallback,
        BatchEnrichmentCallback = _batchEnrichmentCallback,
        CommandSpanNameProvider = _commandSpanNameProvider,
        BatchSpanNameProvider = _batchSpanNameProvider,
        EnableFirstResponseEvent = _enableFirstResponseEvent,
        EnablePhysicalOpenTracing = _enablePhysicalOpenTracing,
        CopyOperationFilter = _copyOperationFilter,
        CopyOperationEnrichmentCallback = _copyOperationEnrichmentCallback,
        CopyOperationSpanNameProvider = _copyOperationSpanNameProvider
    };
}

sealed class PgSqlTracingOptions
{
    internal Func<PgSqlCommand, bool> CommandFilter { get; init; }
    internal Func<PgSqlBatch, bool> BatchFilter { get; init; }
    internal Action<Activity, PgSqlCommand> CommandEnrichmentCallback { get; init; }
    internal Action<Activity, PgSqlBatch> BatchEnrichmentCallback { get; init; }
    internal Func<PgSqlCommand, string> CommandSpanNameProvider { get; init; }
    internal Func<PgSqlBatch, string> BatchSpanNameProvider { get; init; }
    internal bool EnableFirstResponseEvent { get; init; }
    internal bool EnablePhysicalOpenTracing { get; init; }
    internal Func<string, bool> CopyOperationFilter { get; init; }
    internal Action<Activity, string> CopyOperationEnrichmentCallback { get; init; }
    internal Func<string, string> CopyOperationSpanNameProvider { get; init; }
}
