using System;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.PostgresClient.Properties;
using Microsoft.Extensions.Logging;

namespace CodeBrix.PostgresClient.Internal; //was previously: Npgsql.Internal;

class IntegratedSecurityHandler
{
    public virtual bool IsSupported => false;

    public virtual ValueTask<string> GetUsername(bool async, bool includeRealm, ILogger connectionLogger, CancellationToken cancellationToken)
    {
        connectionLogger.LogDebug(string.Format(PgSqlStrings.IntegratedSecurityDisabled, nameof(PgSqlSlimDataSourceBuilder.EnableIntegratedSecurity)));
        return new();
    }

    public virtual ValueTask NegotiateAuthentication(bool async, bool isKerberos, PgSqlConnector connector, CancellationToken cancellationToken)
        => throw new NotSupportedException(string.Format(PgSqlStrings.IntegratedSecurityDisabled, nameof(PgSqlSlimDataSourceBuilder.EnableIntegratedSecurity)));

    public virtual ValueTask<GssEncryptionResult> GSSEncrypt(bool async, bool isRequired, PgSqlConnector connector, CancellationToken cancellationToken)
        => throw new NotSupportedException(string.Format(PgSqlStrings.IntegratedSecurityDisabled, nameof(PgSqlSlimDataSourceBuilder.EnableIntegratedSecurity)));
}

sealed class RealIntegratedSecurityHandler : IntegratedSecurityHandler
{
    public override bool IsSupported => true;

    public override ValueTask<string> GetUsername(bool async, bool includeRealm, ILogger connectionLogger, CancellationToken cancellationToken)
        => KerberosUsernameProvider.GetUsername(async, includeRealm, connectionLogger, cancellationToken);

    public override ValueTask NegotiateAuthentication(bool async, bool isKerberos, PgSqlConnector connector, CancellationToken cancellationToken)
        => connector.AuthenticateGSS(async, isKerberos, cancellationToken);

    public override ValueTask<GssEncryptionResult> GSSEncrypt(bool async, bool isRequired, PgSqlConnector connector, CancellationToken cancellationToken)
        => connector.GSSEncrypt(async, isRequired, cancellationToken);
}
