using System.Diagnostics.CodeAnalysis;

namespace CodeBrix.PostgresClient.Internal; //was previously: Npgsql.Internal;

/// <summary>Creates the type info resolvers for a family of type mappings: the base types plus, optionally, their arrays, ranges and multiranges.</summary>
[Experimental(PgSqlDiagnostics.ConvertersExperimental)]
public abstract class PgTypeInfoResolverFactory
{
    /// <summary>Creates the resolver for the base (non-array) type mappings.</summary>
    /// <returns>The resolver.</returns>
    public abstract IPgTypeInfoResolver CreateResolver();
    /// <summary>Creates the resolver for the array type mappings of the base types.</summary>
    /// <returns>The resolver.</returns>
    public abstract IPgTypeInfoResolver CreateArrayResolver();

    /// <summary>Creates the resolver for range type mappings, if this family supports ranges.</summary>
    /// <returns>The resolver, or <see langword="null"/> when ranges are not supported (the default).</returns>
    public virtual IPgTypeInfoResolver CreateRangeResolver() => null;
    /// <summary>Creates the resolver for arrays of range types, if this family supports ranges.</summary>
    /// <returns>The resolver, or <see langword="null"/> when ranges are not supported (the default).</returns>
    public virtual IPgTypeInfoResolver CreateRangeArrayResolver() => null;

    /// <summary>Creates the resolver for multirange type mappings, if this family supports multiranges.</summary>
    /// <returns>The resolver, or <see langword="null"/> when multiranges are not supported (the default).</returns>
    public virtual IPgTypeInfoResolver CreateMultirangeResolver() => null;
    /// <summary>Creates the resolver for arrays of multirange types, if this family supports multiranges.</summary>
    /// <returns>The resolver, or <see langword="null"/> when multiranges are not supported (the default).</returns>
    public virtual IPgTypeInfoResolver CreateMultirangeArrayResolver() => null;
}
