using CodeBrix.PostgresClient.Internal.Postgres;

namespace CodeBrix.PostgresClient.PostgresTypes; //was previously: Npgsql.PostgresTypes;

/// <summary>
/// Represents a PostgreSQL data type that isn't known to CodeBrix.PostgresClient and cannot be handled.
/// </summary>
public sealed class UnknownBackendType : PostgresType
{
    internal static readonly PostgresType Instance = new UnknownBackendType();

    /// <summary>
    /// Constructs a the unknown backend type.
    /// </summary>
    UnknownBackendType() : base(DataTypeName.Unspecified,0) { }
}
