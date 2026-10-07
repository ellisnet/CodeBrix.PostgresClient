using System.Diagnostics.CodeAnalysis;

namespace CodeBrix.PostgresClient.Internal.Postgres; //was previously: Npgsql.Internal.Postgres;

/// Base field type shared between tables and composites.
[Experimental(PgSqlDiagnostics.ConvertersExperimental)]
public readonly struct Field(string name, PgTypeId pgTypeId, int typeModifier)
{
    /// <summary>The name of the column or composite attribute.</summary>
    public string Name { get; init; } = name;
    /// <summary>The PostgreSQL type of the field.</summary>
    public PgTypeId PgTypeId { get; init; } = pgTypeId;
    /// <summary>The type modifier of the field (e.g. the length of a <c>varchar(n)</c>), or -1 when there is none.</summary>
    public int TypeModifier { get; init; } = typeModifier;
}
