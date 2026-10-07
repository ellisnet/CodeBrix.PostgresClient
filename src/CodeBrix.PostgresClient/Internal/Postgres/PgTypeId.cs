using System;
using System.Diagnostics.CodeAnalysis;

namespace CodeBrix.PostgresClient.Internal.Postgres; //was previously: Npgsql.Internal.Postgres;

/// <summary>
/// A discriminated union of <see cref="Oid" /> and <see cref="DataTypeName" />.
/// </summary>
[Experimental(PgSqlDiagnostics.ConvertersExperimental)]
public readonly struct PgTypeId: IEquatable<PgTypeId>
{
    readonly DataTypeName _dataTypeName;
    readonly Oid _oid;

    /// <summary>Creates a type id that identifies the type by its fully qualified name.</summary>
    /// <param name="name">The data type name.</param>
    public PgTypeId(DataTypeName name) => _dataTypeName = name;
    /// <summary>Creates a type id that identifies the type by its OID.</summary>
    /// <param name="oid">The OID.</param>
    public PgTypeId(Oid oid) => _oid = oid;

    /// <summary>Whether this id holds a <see cref="Postgres.DataTypeName"/>.</summary>
    [MemberNotNullWhen(true, nameof(_dataTypeName))]
    public bool IsDataTypeName => _dataTypeName != default;
    /// <summary>Whether this id holds an <see cref="Postgres.Oid"/>.</summary>
    public bool IsOid => _dataTypeName == default;

    /// <summary>The data type name; throws <see cref="InvalidOperationException"/> if this id holds an OID.</summary>
    public DataTypeName DataTypeName
        => IsDataTypeName ? _dataTypeName : throw new InvalidOperationException("This value does not describe a DataTypeName.");

    /// <summary>The OID; throws <see cref="InvalidOperationException"/> if this id holds a data type name.</summary>
    public Oid Oid
        => IsOid ? _oid : throw new InvalidOperationException("This value does not describe an Oid.");

    /// <summary>Wraps a data type name as a type id.</summary>
    /// <param name="name">The data type name.</param>
    public static implicit operator PgTypeId(DataTypeName name) => new(name);
    /// <summary>Wraps an OID as a type id.</summary>
    /// <param name="id">The OID.</param>
    public static implicit operator PgTypeId(Oid id) => new(id);

    /// <summary>Returns <c>OID n</c> or <c>DataTypeName schema.name</c>, depending on which form the id holds.</summary>
    public override string ToString() => IsOid ? "OID " + _oid : "DataTypeName " + _dataTypeName.Value;

    /// <inheritdoc />
    public bool Equals(PgTypeId other)
    {
        if (IsOid && other.IsOid)
            return _oid == other._oid;
        if (IsDataTypeName && other.IsDataTypeName)
            return _dataTypeName.Equals(other._dataTypeName);
        return false;
    }

    /// <inheritdoc />
    public override bool Equals(object obj) => obj is PgTypeId other && Equals(other);
    /// <inheritdoc />
    public override int GetHashCode() => IsOid ? _oid.GetHashCode() : _dataTypeName.GetHashCode();
    /// <summary>Determines whether two ids are of the same form and hold the same value; an OID never equals a data type name.</summary>
    public static bool operator ==(PgTypeId left, PgTypeId right) => left.Equals(right);
    /// <summary>Determines whether two ids differ in form or value.</summary>
    public static bool operator !=(PgTypeId left, PgTypeId right) => !left.Equals(right);

    internal bool IsUnspecified => IsOid && _oid == Oid.Unspecified || _dataTypeName == DataTypeName.Unspecified;
}
