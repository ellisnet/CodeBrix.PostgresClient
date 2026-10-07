using System;
using System.Threading;
using CodeBrix.PostgresClient.Internal;

namespace CodeBrix.PostgresClient.Util; //was previously: Npgsql.Util;

/// <summary>
/// Represents a timeout that will expire at some point.
/// </summary>
public readonly struct PgSqlTimeout
{
    readonly DateTime _expiration;

    internal static readonly PgSqlTimeout Infinite = new(TimeSpan.Zero);

    internal PgSqlTimeout(TimeSpan expiration)
        => _expiration = expiration > TimeSpan.Zero
            ? DateTime.UtcNow + expiration
            : expiration == TimeSpan.Zero
                ? DateTime.MaxValue
                : DateTime.MinValue;

    internal void Check()
    {
        if (HasExpired)
            ThrowHelper.ThrowPgSqlExceptionWithInnerTimeoutException("The operation has timed out");
    }

    internal void CheckAndApply(PgSqlConnector connector)
    {
        if (!IsSet)
            return;

        var timeLeft = CheckAndGetTimeLeft();
        // Set the remaining timeout on the read and write buffers
        connector.ReadBuffer.Timeout = connector.WriteBuffer.Timeout = timeLeft;
    }

    internal bool IsSet => _expiration != DateTime.MaxValue;

    internal bool HasExpired => DateTime.UtcNow >= _expiration;

    internal TimeSpan CheckAndGetTimeLeft()
    {
        if (!IsSet)
            return Timeout.InfiniteTimeSpan;
        var timeLeft = _expiration - DateTime.UtcNow;
        if (timeLeft <= TimeSpan.Zero)
            Check();
        return timeLeft;
    }
}
