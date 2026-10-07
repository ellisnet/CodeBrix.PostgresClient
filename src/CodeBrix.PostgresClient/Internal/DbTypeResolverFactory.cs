using System.Diagnostics.CodeAnalysis;

namespace CodeBrix.PostgresClient.Internal; //was previously: Npgsql.Internal;

/// <summary>Creates the <see cref="IDbTypeResolver"/> used to map between <see cref="System.Data.DbType"/> values and PostgreSQL data type names for a given database.</summary>
[Experimental(PgSqlDiagnostics.DbTypeResolverExperimental)]
public abstract class DbTypeResolverFactory
{
    /// <summary>Creates a resolver for the given database.</summary>
    /// <param name="databaseInfo">The type catalog of the database the resolver is for.</param>
    /// <returns>The resolver.</returns>
    public abstract IDbTypeResolver CreateDbTypeResolver(PgSqlDatabaseInfo databaseInfo);
}
