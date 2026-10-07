using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace CodeBrix.PostgresClient.Internal; //was previously: Npgsql.Internal;

/// <summary>The wire format of a value in the PostgreSQL protocol.</summary>
[Experimental(PgSqlDiagnostics.ConvertersExperimental)]
public enum DataFormat : byte
{
    /// <summary>The binary format (protocol format code 1).</summary>
    Binary,
    /// <summary>The text format (protocol format code 0).</summary>
    Text
}

static class DataFormatUtils
{
    public static DataFormat Create(short formatCode)
        => formatCode switch
        {
            0 => DataFormat.Text,
            1 => DataFormat.Binary,
            _ => throw new ArgumentOutOfRangeException(nameof(formatCode), formatCode, "Unknown postgres format code, please file a bug,")
        };

    public static short ToFormatCode(this DataFormat dataFormat)
        => dataFormat switch
        {
            DataFormat.Text => 0,
            DataFormat.Binary => 1,
            _ => throw new UnreachableException()
        };
}
