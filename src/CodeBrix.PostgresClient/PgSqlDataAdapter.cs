using System;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.PostgresClient; //was previously: Npgsql;

/// <summary>
/// Represents the method that handles the <see cref="PgSqlDataAdapter.RowUpdated"/> events.
/// </summary>
/// <param name="sender">The source of the event.</param>
/// <param name="e">An <see cref="PgSqlRowUpdatedEventArgs"/> that contains the event data.</param>
public delegate void PgSqlRowUpdatedEventHandler(object sender, PgSqlRowUpdatedEventArgs e);

/// <summary>
/// Represents the method that handles the <see cref="PgSqlDataAdapter.RowUpdating"/> events.
/// </summary>
/// <param name="sender">The source of the event.</param>
/// <param name="e">An <see cref="PgSqlRowUpdatingEventArgs"/> that contains the event data.</param>
public delegate void PgSqlRowUpdatingEventHandler(object sender, PgSqlRowUpdatingEventArgs e);

/// <summary>
/// This class represents an adapter from many commands: select, update, insert and delete to fill a <see cref="System.Data.DataSet"/>.
/// </summary>
[System.ComponentModel.DesignerCategory("")]
public sealed class PgSqlDataAdapter : DbDataAdapter
{
    /// <summary>
    /// Row updated event.
    /// </summary>
    public event PgSqlRowUpdatedEventHandler RowUpdated;

    /// <summary>
    /// Row updating event.
    /// </summary>
    public event PgSqlRowUpdatingEventHandler RowUpdating;

    /// <summary>
    /// Default constructor.
    /// </summary>
    public PgSqlDataAdapter() {}

    /// <summary>
    /// Constructor.
    /// </summary>
    /// <param name="selectCommand"></param>
    public PgSqlDataAdapter(PgSqlCommand selectCommand)
        => SelectCommand = selectCommand;

    /// <summary>
    /// Constructor.
    /// </summary>
    /// <param name="selectCommandText"></param>
    /// <param name="selectConnection"></param>
    public PgSqlDataAdapter(string selectCommandText, PgSqlConnection selectConnection)
        : this(new PgSqlCommand(selectCommandText, selectConnection)) {}

    /// <summary>
    /// Constructor.
    /// </summary>
    /// <param name="selectCommandText"></param>
    /// <param name="selectConnectionString"></param>
    public PgSqlDataAdapter(string selectCommandText, string selectConnectionString)
        : this(selectCommandText, new PgSqlConnection(selectConnectionString)) {}

    /// <summary>
    /// Create row updated event.
    /// </summary>
    protected override RowUpdatedEventArgs CreateRowUpdatedEvent(DataRow dataRow, IDbCommand command,
        System.Data.StatementType statementType,
        DataTableMapping tableMapping)
        => new PgSqlRowUpdatedEventArgs(dataRow, command, statementType, tableMapping);

    /// <summary>
    /// Create row updating event.
    /// </summary>
    protected override RowUpdatingEventArgs CreateRowUpdatingEvent(DataRow dataRow, IDbCommand command,
        System.Data.StatementType statementType,
        DataTableMapping tableMapping)
        => new PgSqlRowUpdatingEventArgs(dataRow, command, statementType, tableMapping);

    /// <summary>
    /// Raise the RowUpdated event.
    /// </summary>
    /// <param name="value"></param>
    protected override void OnRowUpdated(RowUpdatedEventArgs value)
    {
        //base.OnRowUpdated(value);
        if (value is PgSqlRowUpdatedEventArgs args)
            RowUpdated?.Invoke(this, args);
        //if (RowUpdated != null && value is PgSqlRowUpdatedEventArgs args)
        //    RowUpdated(this, args);
    }

    /// <summary>
    /// Raise the RowUpdating event.
    /// </summary>
    /// <param name="value"></param>
    protected override void OnRowUpdating(RowUpdatingEventArgs value)
    {
        if (value is PgSqlRowUpdatingEventArgs args)
            RowUpdating?.Invoke(this, args);
    }

    /// <summary>
    /// Delete command.
    /// </summary>
    public new PgSqlCommand DeleteCommand
    {
        get => (PgSqlCommand)base.DeleteCommand;
        set => base.DeleteCommand = value;
    }

    /// <summary>
    /// Select command.
    /// </summary>
    public new PgSqlCommand SelectCommand
    {
        get => (PgSqlCommand)base.SelectCommand;
        set => base.SelectCommand = value;
    }

    /// <summary>
    /// Update command.
    /// </summary>
    public new PgSqlCommand UpdateCommand
    {
        get => (PgSqlCommand)base.UpdateCommand;
        set => base.UpdateCommand = value;
    }

    /// <summary>
    /// Insert command.
    /// </summary>
    public new PgSqlCommand InsertCommand
    {
        get => (PgSqlCommand)base.InsertCommand;
        set => base.InsertCommand = value;
    }

    // Temporary implementation, waiting for official support in System.Data via https://github.com/dotnet/runtime/issues/22109
    [RequiresUnreferencedCode("Members from serialized types or types used in expressions may be trimmed if not referenced directly.")]
    internal async Task<int> Fill(DataTable dataTable, bool async, CancellationToken cancellationToken = default)
    {
        var command = SelectCommand;
        var activeConnection = command?.Connection ?? throw new InvalidOperationException("Connection required");
        var originalState = ConnectionState.Closed;

        try
        {
            originalState = activeConnection.State;
            if (ConnectionState.Closed == originalState)
                await activeConnection.Open(async, cancellationToken).ConfigureAwait(false);

            var dataReader = await command.ExecuteReader(async, CommandBehavior.Default, cancellationToken).ConfigureAwait(false);
            try
            {
                return await Fill(dataTable, dataReader, async, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                if (async)
                    await dataReader.DisposeAsync().ConfigureAwait(false);
                else
                    dataReader.Dispose();
            }
        }
        finally
        {
            if (ConnectionState.Closed == originalState)
                activeConnection.Close();
        }
    }

    [RequiresUnreferencedCode("Members from serialized types or types used in expressions may be trimmed if not referenced directly.")]
    async Task<int> Fill(DataTable dataTable, PgSqlDataReader dataReader, bool async, CancellationToken cancellationToken = default)
    {
        dataTable.BeginLoadData();
        try
        {
            var rowsAdded = 0;
            var count = dataReader.FieldCount;
            var columnCollection = dataTable.Columns;
            for (var i = 0; i < count; ++i)
            {
                var fieldName = dataReader.GetName(i);
                if (!columnCollection.Contains(fieldName))
                {
                    var fieldType = dataReader.GetFieldType(i);
                    var dataColumn = new DataColumn(fieldName, fieldType);
                    columnCollection.Add(dataColumn);
                }
            }

            var values = new object[count];

            while (async ? await dataReader.ReadAsync(cancellationToken).ConfigureAwait(false) : dataReader.Read())
            {
                dataReader.GetValues(values);
                dataTable.LoadDataRow(values, true);
                rowsAdded++;
            }
            return rowsAdded;
        }
        finally
        {
            dataTable.EndLoadData();
        }
    }
}

/// <summary>Provides data for the <see cref="PgSqlDataAdapter.RowUpdating"/> event, raised before a command is executed against the database during an update.</summary>
/// <param name="dataRow">The row being updated.</param>
/// <param name="command">The command that will be executed.</param>
/// <param name="statementType">The kind of statement (insert, update or delete).</param>
/// <param name="tableMapping">The table mapping used for the update.</param>
public class PgSqlRowUpdatingEventArgs(
    DataRow dataRow,
    IDbCommand command,
    System.Data.StatementType statementType,
    DataTableMapping tableMapping)
    : RowUpdatingEventArgs(dataRow, command, statementType, tableMapping);

/// <summary>Provides data for the <see cref="PgSqlDataAdapter.RowUpdated"/> event, raised after a command has been executed against the database during an update.</summary>
/// <param name="dataRow">The row that was updated.</param>
/// <param name="command">The command that was executed.</param>
/// <param name="statementType">The kind of statement (insert, update or delete).</param>
/// <param name="tableMapping">The table mapping used for the update.</param>
public class PgSqlRowUpdatedEventArgs(
    DataRow dataRow,
    IDbCommand command,
    System.Data.StatementType statementType,
    DataTableMapping tableMapping)
    : RowUpdatedEventArgs(dataRow, command, statementType, tableMapping);
