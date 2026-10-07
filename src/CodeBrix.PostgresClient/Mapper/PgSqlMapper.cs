using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Dynamic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Numerics;
using System.Reflection;
using System.Text.RegularExpressions;

namespace CodeBrix.PostgresClient;

/// <summary>
/// Lightweight object-mapping extension methods for <see cref="PgSqlConnection"/>: Query /
/// QueryFirst / QuerySingle (and -OrDefault), Execute, ExecuteScalar, ExecuteReader and
/// QueryMultiple, in sync and async forms, with anonymous-object, POCO or dictionary parameters
/// and IN-list expansion. Every method opens a closed connection for the duration of the call and
/// closes it again afterwards; an already-open connection is left open.
/// </summary>
/// <remarks>
/// Parameters are referenced in SQL as <c>@name</c>. A parameter object property that the SQL
/// does not reference is skipped. A parameter whose value is a sequence (other than
/// <see cref="string"/> and <see cref="byte"/>[]) used as <c>IN @name</c> is expanded to
/// <c>IN (@name1, @name2, ...)</c>; an empty sequence expands to <c>(NULL)</c>, which matches no
/// rows. To bind a sequence as a single PostgreSQL array instead (for example
/// <c>= ANY(@ids)</c>), pass it as a <see cref="PgSqlParameter"/> in a dictionary, or create the
/// command yourself.
/// </remarks>
[RequiresUnreferencedCode("PgSqlMapper reads parameter objects and populates result types through reflection; the members of those types may be trimmed away. Use PgSqlCommand and PgSqlDataReader directly in trimmed applications.")]
[RequiresDynamicCode("PgSqlMapper creates result objects and dynamic rows through reflection, which may need code that is not available ahead of time. Use PgSqlCommand and PgSqlDataReader directly in NativeAOT applications.")]
public static partial class PgSqlMapper
{
    #region Query (sync)

    /// <summary>
    /// Executes a query and materializes each result row as <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The result row type: a simple type (the first column is used) or a POCO
    /// (columns are mapped to writable properties by name, case-insensitively and ignoring
    /// underscores, so <c>first_name</c> fills <c>FirstName</c>).</typeparam>
    /// <param name="connection">The connection to query on.</param>
    /// <param name="sql">The SQL to execute.</param>
    /// <param name="param">Optional parameters: an anonymous object, POCO, or <c>IDictionary&lt;string, object&gt;</c>.</param>
    /// <param name="transaction">Optional transaction to execute within.</param>
    /// <returns>The materialized rows (buffered).</returns>
    public static IEnumerable<T> Query<T>(this PgSqlConnection connection, string sql, object param = null, PgSqlTransaction transaction = null)
    {
        bool wasClosed = PrepareConnection(connection);
        try
        {
            using (PgSqlCommand command = CreateMapperCommand(connection, sql, param, transaction))
            using (PgSqlDataReader reader = command.ExecuteReader())
            {
                return BufferRows<T>(reader);
            }
        }
        finally
        {
            if (wasClosed) { connection.Close(); }
        }
    }

    /// <summary>
    /// Executes a query and materializes each result row as a dynamic object whose members are the
    /// result columns.
    /// </summary>
    /// <param name="connection">The connection to query on.</param>
    /// <param name="sql">The SQL to execute.</param>
    /// <param name="param">Optional parameters: an anonymous object, POCO, or <c>IDictionary&lt;string, object&gt;</c>.</param>
    /// <param name="transaction">Optional transaction to execute within.</param>
    /// <returns>The materialized rows (buffered); SQL NULL becomes <see langword="null"/>.</returns>
    public static IEnumerable<dynamic> Query(this PgSqlConnection connection, string sql, object param = null, PgSqlTransaction transaction = null)
    {
        bool wasClosed = PrepareConnection(connection);
        try
        {
            using (PgSqlCommand command = CreateMapperCommand(connection, sql, param, transaction))
            using (PgSqlDataReader reader = command.ExecuteReader())
            {
                var rows = new List<dynamic>();
                while (reader.Read()) { rows.Add(MaterializeDynamicRow(reader)); }
                return rows;
            }
        }
        finally
        {
            if (wasClosed) { connection.Close(); }
        }
    }

    /// <summary>
    /// Executes a query and returns the first result row as <typeparamref name="T"/>, throwing when
    /// the result set is empty.
    /// </summary>
    /// <typeparam name="T">The result row type (see <see cref="Query{T}"/>).</typeparam>
    /// <param name="connection">The connection to query on.</param>
    /// <param name="sql">The SQL to execute.</param>
    /// <param name="param">Optional parameters.</param>
    /// <param name="transaction">Optional transaction to execute within.</param>
    /// <returns>The first row.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the result set is empty.</exception>
    public static T QueryFirst<T>(this PgSqlConnection connection, string sql, object param = null, PgSqlTransaction transaction = null)
        => connection.Query<T>(sql, param, transaction).First();

    /// <summary>
    /// Executes a query and returns the first result row as <typeparamref name="T"/>, or the type's
    /// default value when the result set is empty.
    /// </summary>
    /// <typeparam name="T">The result row type (see <see cref="Query{T}"/>).</typeparam>
    /// <param name="connection">The connection to query on.</param>
    /// <param name="sql">The SQL to execute.</param>
    /// <param name="param">Optional parameters.</param>
    /// <param name="transaction">Optional transaction to execute within.</param>
    /// <returns>The first row, or default.</returns>
    public static T QueryFirstOrDefault<T>(this PgSqlConnection connection, string sql, object param = null, PgSqlTransaction transaction = null)
        => connection.Query<T>(sql, param, transaction).FirstOrDefault();

    /// <summary>
    /// Executes a query and returns the single result row as <typeparamref name="T"/>, throwing
    /// when the result set is empty or holds more than one row.
    /// </summary>
    /// <typeparam name="T">The result row type (see <see cref="Query{T}"/>).</typeparam>
    /// <param name="connection">The connection to query on.</param>
    /// <param name="sql">The SQL to execute.</param>
    /// <param name="param">Optional parameters.</param>
    /// <param name="transaction">Optional transaction to execute within.</param>
    /// <returns>The single row.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the result set is empty or has more than one row.</exception>
    public static T QuerySingle<T>(this PgSqlConnection connection, string sql, object param = null, PgSqlTransaction transaction = null)
        => connection.Query<T>(sql, param, transaction).Single();

    /// <summary>
    /// Executes a query and returns the single result row as <typeparamref name="T"/> (or default
    /// when empty), throwing when the result set holds more than one row.
    /// </summary>
    /// <typeparam name="T">The result row type (see <see cref="Query{T}"/>).</typeparam>
    /// <param name="connection">The connection to query on.</param>
    /// <param name="sql">The SQL to execute.</param>
    /// <param name="param">Optional parameters.</param>
    /// <param name="transaction">Optional transaction to execute within.</param>
    /// <returns>The single row, or default.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the result set has more than one row.</exception>
    public static T QuerySingleOrDefault<T>(this PgSqlConnection connection, string sql, object param = null, PgSqlTransaction transaction = null)
        => connection.Query<T>(sql, param, transaction).SingleOrDefault();

    #endregion

    #region Execute / scalar / reader / multiple (sync)

    /// <summary>
    /// Executes a statement (INSERT, UPDATE, DELETE, DDL) and returns the number of rows affected.
    /// </summary>
    /// <param name="connection">The connection to execute on.</param>
    /// <param name="sql">The SQL to execute.</param>
    /// <param name="param">Optional parameters.</param>
    /// <param name="transaction">Optional transaction to execute within.</param>
    /// <returns>The number of rows affected (-1 for statements that report no row count, such as DDL).</returns>
    public static int Execute(this PgSqlConnection connection, string sql, object param = null, PgSqlTransaction transaction = null)
    {
        bool wasClosed = PrepareConnection(connection);
        try
        {
            using (PgSqlCommand command = CreateMapperCommand(connection, sql, param, transaction))
            {
                return command.ExecuteNonQuery();
            }
        }
        finally
        {
            if (wasClosed) { connection.Close(); }
        }
    }

    /// <summary>
    /// Executes a query and returns the first column of the first row converted to
    /// <typeparamref name="T"/>, or the type's default value when the result is empty or NULL.
    /// </summary>
    /// <typeparam name="T">The scalar type to convert the value to.</typeparam>
    /// <param name="connection">The connection to query on.</param>
    /// <param name="sql">The SQL to execute.</param>
    /// <param name="param">Optional parameters.</param>
    /// <param name="transaction">Optional transaction to execute within.</param>
    /// <returns>The converted scalar value.</returns>
    public static T ExecuteScalar<T>(this PgSqlConnection connection, string sql, object param = null, PgSqlTransaction transaction = null)
    {
        bool wasClosed = PrepareConnection(connection);
        try
        {
            using (PgSqlCommand command = CreateMapperCommand(connection, sql, param, transaction))
            {
                return ConvertScalar<T>(command.ExecuteScalar());
            }
        }
        finally
        {
            if (wasClosed) { connection.Close(); }
        }
    }

    /// <summary>
    /// Executes a query and returns the open data reader. When the connection had to be opened by
    /// this call, the reader closes it again on disposal.
    /// </summary>
    /// <param name="connection">The connection to query on.</param>
    /// <param name="sql">The SQL to execute.</param>
    /// <param name="param">Optional parameters.</param>
    /// <param name="transaction">Optional transaction to execute within.</param>
    /// <returns>The open data reader.</returns>
    public static PgSqlDataReader ExecuteReader(this PgSqlConnection connection, string sql, object param = null, PgSqlTransaction transaction = null)
    {
        bool wasClosed = PrepareConnection(connection);
        PgSqlCommand command = CreateMapperCommand(connection, sql, param, transaction);
        return command.ExecuteReader(wasClosed ? CommandBehavior.CloseConnection : CommandBehavior.Default);
    }

    /// <summary>
    /// Executes a batch of statements and returns a <see cref="PgSqlGridReader"/> for reading each
    /// result set in turn.
    /// </summary>
    /// <param name="connection">The connection to query on.</param>
    /// <param name="sql">The SQL batch to execute (multiple statements separated by ';').</param>
    /// <param name="param">Optional parameters, shared by every statement in the batch.</param>
    /// <param name="transaction">Optional transaction to execute within.</param>
    /// <returns>The grid reader positioned on the first result set.</returns>
    public static PgSqlGridReader QueryMultiple(this PgSqlConnection connection, string sql, object param = null, PgSqlTransaction transaction = null)
    {
        bool wasClosed = PrepareConnection(connection);
        PgSqlCommand command = CreateMapperCommand(connection, sql, param, transaction);
        PgSqlDataReader reader = command.ExecuteReader();
        return new PgSqlGridReader(command, reader, wasClosed ? connection : null);
    }

    #endregion

    #region Shared internals

    private static bool PrepareConnection(PgSqlConnection connection)
    {
        if (connection == null) { throw new ArgumentNullException(nameof(connection)); }
        bool wasClosed = connection.State != ConnectionState.Open;
        if (wasClosed) { connection.Open(); }
        return wasClosed;
    }

    private static PgSqlCommand CreateMapperCommand(PgSqlConnection connection, string sql, object param, PgSqlTransaction transaction)
    {
        if (sql == null) { throw new ArgumentNullException(nameof(sql)); }
        if (String.IsNullOrWhiteSpace(sql)) { throw new ArgumentException("The SQL statement cannot be empty or whitespace.", nameof(sql)); }
        PgSqlCommand command = connection.CreateCommand();
        if (transaction != null) { command.Transaction = transaction; }
        command.CommandText = BindParameters(command, sql, param);
        return command;
    }

    /// <summary>
    /// Adds the parameters of <paramref name="param"/> that <paramref name="sql"/> references to
    /// <paramref name="command"/>, expanding sequence values into IN-lists, and returns the
    /// (possibly rewritten) SQL.
    /// </summary>
    /// <param name="command">The command to add parameters to.</param>
    /// <param name="sql">The SQL text.</param>
    /// <param name="param">The parameter object, dictionary or null.</param>
    /// <returns>The SQL text with any IN-list parameters expanded.</returns>
    internal static string BindParameters(PgSqlCommand command, string sql, object param)
    {
        if (param == null) { return sql; }
        foreach (KeyValuePair<string, object> entry in EnumerateParameters(param))
        {
            string name = entry.Key.TrimStart('@', ':');
            object value = entry.Value;
            if (!IsReferenced(sql, name))
            {
                continue; //parameter not referenced in the SQL - skip it
            }
            if (value is PgSqlParameter parameter)
            {
                parameter.ParameterName = name;
                command.Parameters.Add(parameter);
            }
            else if (value is IEnumerable list && !(value is string) && !(value is byte[]))
            {
                sql = ExpandListParameter(command, sql, name, list);
            }
            else
            {
                command.Parameters.AddWithValue(name, ToParameterValue(value));
            }
        }
        return sql;
    }

    private static bool IsReferenced(string sql, string name)
        => Regex.IsMatch(sql, $"@{Regex.Escape(name)}\\b", RegexOptions.IgnoreCase);

    private static IEnumerable<KeyValuePair<string, object>> EnumerateParameters(object param)
    {
        if (param is IDictionary<string, object> dictionary)
        {
            foreach (KeyValuePair<string, object> entry in dictionary) { yield return entry; }
            yield break;
        }
        foreach (PropertyInfo property in param.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanRead || property.GetIndexParameters().Length > 0) { continue; }
            yield return new KeyValuePair<string, object>(property.Name, property.GetValue(param));
        }
    }

    private static string ExpandListParameter(PgSqlCommand command, string sql, string name, IEnumerable list)
    {
        var parameterNames = new List<string>();
        int index = 0;
        foreach (object element in list)
        {
            string elementName = $"{name}{++index}";
            parameterNames.Add("@" + elementName);
            command.Parameters.AddWithValue(elementName, ToParameterValue(element));
        }
        //An empty list expands to (NULL), which matches no rows - the useful IN () semantics.
        string expansion = parameterNames.Count == 0 ? "(NULL)" : $"({String.Join(", ", parameterNames)})";
        return Regex.Replace(sql, $"@{Regex.Escape(name)}\\b", expansion, RegexOptions.IgnoreCase);
    }

    /// <summary>
    /// Converts a parameter value to the value bound to the command: null becomes
    /// <see cref="DBNull.Value"/>, and an enum becomes its underlying integral value (so it binds
    /// as smallint / integer / bigint rather than requiring a PostgreSQL enum type).
    /// </summary>
    /// <param name="value">The parameter value.</param>
    /// <returns>The value to bind.</returns>
    internal static object ToParameterValue(object value)
    {
        if (value == null) { return DBNull.Value; }
        if (value is Enum) { return Convert.ChangeType(value, Enum.GetUnderlyingType(value.GetType()), CultureInfo.InvariantCulture); }
        return value;
    }

    #endregion

    #region Materialization

    private static readonly ConcurrentDictionary<Type, Dictionary<string, PropertyInfo>> PropertyMaps =
        new ConcurrentDictionary<Type, Dictionary<string, PropertyInfo>>();

    private static List<T> BufferRows<T>(PgSqlDataReader reader)
    {
        var rows = new List<T>();
        while (reader.Read()) { rows.Add(MaterializeRow<T>(reader)); }
        return rows;
    }

    /// <summary>
    /// Materializes the reader's current row as <typeparamref name="T"/>: a simple type takes the
    /// first column's value, and anything else is built as a POCO whose writable properties are
    /// matched to column names case-insensitively and ignoring underscores.
    /// </summary>
    /// <typeparam name="T">The type to materialize the row as.</typeparam>
    /// <param name="reader">The reader, positioned on the row to materialize.</param>
    /// <returns>The materialized row.</returns>
    internal static T MaterializeRow<T>(PgSqlDataReader reader)
    {
        Type type = typeof(T);
        if (IsSimpleType(type))
        {
            object converted = ConvertValue(reader.GetValue(0), type);
            return converted == null ? default : (T)converted;
        }
        return (T)MaterializePoco(reader, type);
    }

    private static object MaterializePoco(PgSqlDataReader reader, Type type)
    {
        Dictionary<string, PropertyInfo> map = PropertyMaps.GetOrAdd(type, BuildPropertyMap);
        object instance = Activator.CreateInstance(type);
        for (int i = 0; i < reader.FieldCount; i++)
        {
            if (!map.TryGetValue(NormalizeName(reader.GetName(i)), out PropertyInfo property)) { continue; }
            if (reader.IsDBNull(i)) { continue; }
            property.SetValue(instance, ConvertValue(reader.GetValue(i), property.PropertyType));
        }
        return instance;
    }

    /// <summary>
    /// Materializes the reader's current row as an <see cref="ExpandoObject"/> keyed by column name.
    /// </summary>
    /// <param name="reader">The reader, positioned on the row to materialize.</param>
    /// <returns>The row as a dynamic object.</returns>
    internal static dynamic MaterializeDynamicRow(PgSqlDataReader reader)
    {
        IDictionary<string, object> row = new ExpandoObject();
        for (int i = 0; i < reader.FieldCount; i++)
        {
            row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
        }
        return row;
    }

    private static Dictionary<string, PropertyInfo> BuildPropertyMap(Type type)
    {
        var map = new Dictionary<string, PropertyInfo>();
        foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanWrite || property.GetIndexParameters().Length > 0) { continue; }
            map[NormalizeName(property.Name)] = property;
        }
        return map;
    }

    private static string NormalizeName(string name)
        => name.Replace("_", "").ToLowerInvariant();

    /// <summary>
    /// Converts a scalar result to <typeparamref name="T"/>, mapping NULL and DBNull to default.
    /// </summary>
    /// <typeparam name="T">The target type.</typeparam>
    /// <param name="value">The value read from the database.</param>
    /// <returns>The converted value.</returns>
    internal static T ConvertScalar<T>(object value)
        => value == null || value == DBNull.Value ? default : (T)ConvertValue(value, typeof(T));

    /// <summary>
    /// True when rows of <paramref name="type"/> are materialized from the first column rather than
    /// built as a POCO: primitives, enums, strings, the common date/time and numeric structs,
    /// arrays (PostgreSQL arrays), network types, and the PostgreSQL value types in
    /// <c>CodeBrix.PostgresClient.PgSqlTypes</c>.
    /// </summary>
    /// <param name="type">The requested row type.</param>
    /// <returns>True for a simple (single-column) type.</returns>
    internal static bool IsSimpleType(Type type)
    {
        Type target = Nullable.GetUnderlyingType(type) ?? type;
        if (target.IsArray) { return true; }
        if (target.IsGenericType && target.Namespace == typeof(PgSqlTypes.PgSqlRange<>).Namespace) { return true; }
        return target.IsPrimitive || target.IsEnum || target == typeof(string) || target == typeof(decimal)
            || target == typeof(DateTime) || target == typeof(DateTimeOffset) || target == typeof(TimeSpan)
            || target == typeof(DateOnly) || target == typeof(TimeOnly) || target == typeof(Guid)
            || target == typeof(BigInteger) || target == typeof(object) || target == typeof(BitArray)
            || target == typeof(IPAddress) || target == typeof(IPNetwork) || target == typeof(PhysicalAddress)
            || target.Namespace == typeof(PgSqlTypes.PgSqlInterval).Namespace;
    }

    /// <summary>
    /// Converts a value read from the database to <paramref name="targetType"/>, handling the
    /// conversions PostgreSQL results commonly need (integral width changes, enums from integers or
    /// text, DateTime to DateOnly / DateTimeOffset, TimeSpan to TimeOnly, text to Guid).
    /// </summary>
    /// <param name="value">The value read from the database.</param>
    /// <param name="targetType">The type to convert to.</param>
    /// <returns>The converted value, or null for NULL / DBNull.</returns>
    internal static object ConvertValue(object value, Type targetType)
    {
        if (value == null || value == DBNull.Value) { return null; }
        Type target = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (target == typeof(object) || target.IsInstanceOfType(value)) { return value; }
        if (target.IsEnum)
        {
            return value is string text
                ? Enum.Parse(target, text, ignoreCase: true)
                : Enum.ToObject(target, Convert.ToInt64(value, CultureInfo.InvariantCulture));
        }
        if (target == typeof(Guid))
        {
            return value is byte[] blob ? new Guid(blob) : Guid.Parse(Convert.ToString(value, CultureInfo.InvariantCulture));
        }
        if (target == typeof(DateOnly) && value is DateTime dateForDateOnly) { return DateOnly.FromDateTime(dateForDateOnly); }
        if (target == typeof(TimeOnly) && value is TimeSpan timeForTimeOnly) { return TimeOnly.FromTimeSpan(timeForTimeOnly); }
        if (target == typeof(DateTimeOffset) && value is DateTime dateForOffset) { return new DateTimeOffset(dateForOffset); }
        if (target == typeof(DateTime) && value is DateTimeOffset offsetForDate) { return offsetForDate.UtcDateTime; }
        if (target == typeof(DateTime)) { return Convert.ToDateTime(value, CultureInfo.InvariantCulture); }
        if (target == typeof(char))
        {
            string text = Convert.ToString(value, CultureInfo.InvariantCulture);
            return text.Length > 0 ? text[0] : default(char);
        }
        return Convert.ChangeType(value, target, CultureInfo.InvariantCulture);
    }

    #endregion
}
