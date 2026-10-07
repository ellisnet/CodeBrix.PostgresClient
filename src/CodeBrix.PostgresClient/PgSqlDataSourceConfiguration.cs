using System;
using System.Collections.Generic;
using System.Net.Security;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.PostgresClient.Internal;

namespace CodeBrix.PostgresClient; //was previously: Npgsql;

sealed record PgSqlDataSourceConfiguration(string Name,
    PgSqlLoggingConfiguration LoggingConfiguration,
    PgSqlTracingOptions TracingOptions,
    PgSqlTypeLoadingOptions TypeLoading,
    TransportSecurityHandler TransportSecurityHandler,
    IntegratedSecurityHandler IntegratedSecurityHandler,
    Action<SslClientAuthenticationOptions> SslClientAuthenticationOptionsCallback,
    Func<PgSqlConnectionStringBuilder, string> PasswordProvider,
    Func<PgSqlConnectionStringBuilder, CancellationToken, ValueTask<string>> PasswordProviderAsync,
    Func<PgSqlConnectionStringBuilder, CancellationToken, ValueTask<string>> PeriodicPasswordProvider,
    TimeSpan PeriodicPasswordSuccessRefreshInterval,
    TimeSpan PeriodicPasswordFailureRefreshInterval,
    PgTypeInfoResolverChain ResolverChain,
    IEnumerable<DbTypeResolverFactory> DbTypeResolverFactories,
    IPgSqlNameTranslator DefaultNameTranslator,
    Action<PgSqlConnection> ConnectionInitializer,
    Func<PgSqlConnection, Task> ConnectionInitializerAsync,
    Action<NegotiateAuthenticationClientOptions> NegotiateOptionsCallback);
