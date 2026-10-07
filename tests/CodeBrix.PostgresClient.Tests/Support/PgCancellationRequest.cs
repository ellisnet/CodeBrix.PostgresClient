using System.IO;
using CodeBrix.PostgresClient.Internal;

namespace CodeBrix.PostgresClient.Tests.Support; //was previously: Npgsql.Tests.Support;

class PgCancellationRequest(PgSqlReadBuffer readBuffer, PgSqlWriteBuffer writeBuffer, Stream stream, int processId, int secret)
{
    public int ProcessId { get; } = processId;
    public int Secret { get; } = secret;

    bool completed;

    public void Complete()
    {
        if (completed)
            return;

        readBuffer.Dispose();
        writeBuffer.Dispose();
        stream.Dispose();

        completed = true;
    }
}
