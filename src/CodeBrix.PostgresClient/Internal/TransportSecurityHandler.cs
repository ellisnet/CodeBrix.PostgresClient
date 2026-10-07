using System;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.PostgresClient.Properties;
using CodeBrix.PostgresClient.Util;

namespace CodeBrix.PostgresClient.Internal; //was previously: Npgsql.Internal;

class TransportSecurityHandler
{
    public virtual bool SupportEncryption => false;

    public virtual Func<X509Certificate2Collection> RootCertificatesCallback
    {
        get => throw new NotSupportedException(string.Format(PgSqlStrings.TransportSecurityDisabled, nameof(PgSqlSlimDataSourceBuilder.EnableTransportSecurity)));
        set => throw new NotSupportedException(string.Format(PgSqlStrings.TransportSecurityDisabled, nameof(PgSqlSlimDataSourceBuilder.EnableTransportSecurity)));
    }

    public virtual Task NegotiateEncryption(bool async, PgSqlConnector connector, SslMode sslMode, PgSqlTimeout timeout, CancellationToken cancellationToken)
        => throw new NotSupportedException(string.Format(PgSqlStrings.TransportSecurityDisabled, nameof(PgSqlSlimDataSourceBuilder.EnableTransportSecurity)));

    public virtual void AuthenticateSASLSha256Plus(PgSqlConnector connector, ref string mechanism, ref string cbindFlag, ref string cbind,
        ref bool successfulBind)
        => throw new NotSupportedException(string.Format(PgSqlStrings.TransportSecurityDisabled, nameof(PgSqlSlimDataSourceBuilder.EnableTransportSecurity)));
}

sealed class RealTransportSecurityHandler : TransportSecurityHandler
{
    public override bool SupportEncryption => true;

    public override Func<X509Certificate2Collection> RootCertificatesCallback { get; set; }

    public override Task NegotiateEncryption(bool async, PgSqlConnector connector, SslMode sslMode, PgSqlTimeout timeout, CancellationToken cancellationToken)
        => connector.NegotiateEncryption(sslMode, timeout, async, cancellationToken);

    public override void AuthenticateSASLSha256Plus(PgSqlConnector connector, ref string mechanism, ref string cbindFlag, ref string cbind,
            ref bool successfulBind)
        => connector.AuthenticateSASLSha256Plus(ref mechanism, ref cbindFlag, ref cbind, ref successfulBind);
}
