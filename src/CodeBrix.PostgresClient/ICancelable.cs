using System;
using System.Threading.Tasks;

namespace CodeBrix.PostgresClient; //was previously: Npgsql;

interface ICancelable : IDisposable, IAsyncDisposable
{
    void Cancel();

    Task CancelAsync();
}
