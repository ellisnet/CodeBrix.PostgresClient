using System;
using System.Diagnostics.CodeAnalysis;

namespace CodeBrix.PostgresClient.Internal.Postgres; //was previously: Npgsql.Internal.Postgres;

/// <summary>A PostgreSQL object identifier (OID), as used in <c>pg_type.oid</c> to identify a type within one database.</summary>
/// <param name="value">The numeric OID.</param>
[Experimental(PgSqlDiagnostics.ConvertersExperimental)]
public readonly struct Oid(uint value) : IEquatable<Oid>
{
    /// <summary>Returns the numeric value of the OID.</summary>
    /// <param name="oid">The OID.</param>
    public static explicit operator uint(Oid oid) => oid.Value;
    /// <summary>Wraps a numeric value as an OID.</summary>
    /// <param name="oid">The numeric value.</param>
    public static implicit operator Oid(uint oid) => new(oid);
    /// <summary>The numeric value of the OID.</summary>
    public uint Value { get; init; } = value;
    /// <summary>The OID 0, used by the protocol to mean "no type specified" (the server infers the type).</summary>
    public static Oid Unspecified => new(0);

    /// <summary>Returns the numeric value as a string.</summary>
    public override string ToString() => Value.ToString();
    /// <inheritdoc />
    public bool Equals(Oid other) => Value == other.Value;
    /// <inheritdoc />
    public override bool Equals(object obj) => obj is Oid other && Equals(other);
    /// <inheritdoc />
    public override int GetHashCode() => (int)Value;
    /// <summary>Determines whether two OIDs have the same numeric value.</summary>
    public static bool operator ==(Oid left, Oid right) => left.Equals(right);
    /// <summary>Determines whether two OIDs have different numeric values.</summary>
    public static bool operator !=(Oid left, Oid right) => !left.Equals(right);
}
