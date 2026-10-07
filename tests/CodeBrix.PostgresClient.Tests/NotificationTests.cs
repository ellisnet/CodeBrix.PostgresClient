using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using SilverAssertions;
using Xunit;
using static CodeBrix.PostgresClient.Tests.TestUtil;

namespace CodeBrix.PostgresClient.Tests; //was previously: Npgsql.Tests;

public class NotificationTests : TestBase
{
    // Simple LISTEN/NOTIFY scenario
    [Fact]
    public void Notification()
    {
        //Arrange
        var notify = GetUniqueIdentifier(nameof(NotificationTests));
        using var conn = OpenConnection();
        var receivedNotification = false;
        conn.ExecuteNonQuery($"LISTEN {notify}");
        conn.Notification += (o, e) => receivedNotification = true;

        //Act
        conn.ExecuteNonQuery($"NOTIFY {notify}");

        //Assert
        receivedNotification.Should().BeTrue();
    }

    // Generates a notification that arrives after reader data that is already being read
    [Fact]
    [IssueLink("https://github.com/npgsql/npgsql/issues/252")]
    public async Task Notification_after_data()
    {
        var notify = GetUniqueIdentifier(nameof(NotificationTests));

        var receivedNotification = false;
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"LISTEN {notify}";
        cmd.ExecuteNonQuery();
        conn.Notification += (o, e) => receivedNotification = true;

        cmd.CommandText = "SELECT generate_series(1,10000)";
        using (var reader = cmd.ExecuteReader())
        {
            //After "notify notifytest1", a notification message will be sent to client,
            //And so the notification message will stick with the last response message of "select generate_series(1,10000)" in CodeBrix.PostgresClient's tcp receiving buffer.
            using (var conn2 = new PgSqlConnection(ConnectionString))
            {
                conn2.Open();
                using (var command = conn2.CreateCommand())
                {
                    command.CommandText = $"NOTIFY {notify}";
                    command.ExecuteNonQuery();
                }
            }

            // Allow some time for the notification to get delivered
            await Task.Delay(2000, TestContext.Current.CancellationToken);

            reader.Read().Should().BeTrue();
            reader.GetValue(0).Should().Be(1);
        }

        conn.ExecuteScalar("SELECT 1").Should().Be(1);
        receivedNotification.Should().BeTrue();
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/1024")]
    public void Wait()
    {
        //Arrange
        var notify = GetUniqueIdentifier(nameof(NotificationTests));
        using var conn = OpenConnection();
        using var notifyingConn = OpenConnection();
        var receivedNotification = false;
        conn.ExecuteNonQuery($"LISTEN {notify}");
        notifyingConn.ExecuteNonQuery($"NOTIFY {notify}");
        conn.Notification += (o, e) => receivedNotification = true;

        //Act
        var result = conn.Wait(0);

        //Assert
        result.Should().BeTrue();
        receivedNotification.Should().BeTrue();
        conn.ExecuteScalar("SELECT 1").Should().Be(1);
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/1024")]
    public void Wait_with_timeout()
    {
        //Arrange
        using var conn = OpenConnection();

        //Act
        var result = conn.Wait(100);

        //Assert
        result.Should().BeFalse();
        conn.ExecuteScalar("SELECT 1").Should().Be(1);
    }

    [Fact]
    public void Wait_with_prepended_message()
    {
        //Arrange
        using var dataSource = CreateDataSource();
        using (dataSource.OpenConnection()) {}  // A DISCARD ALL is now prepended in the connection's write buffer
        using var conn = dataSource.OpenConnection();

        //Act
        var result = conn.Wait(100);

        //Assert
        result.Should().BeFalse();
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/1024")]
    public async Task WaitAsync()
    {
        //Arrange
        var notify = GetUniqueIdentifier(nameof(NotificationTests));
        await using var conn = await OpenConnectionAsync();
        await using var notifyingConn = await OpenConnectionAsync();
        var receivedNotification = false;
        await conn.ExecuteNonQueryAsync($"LISTEN {notify}", cancellationToken: TestContext.Current.CancellationToken);
        await notifyingConn.ExecuteNonQueryAsync($"NOTIFY {notify}", cancellationToken: TestContext.Current.CancellationToken);
        conn.Notification += (o, e) => receivedNotification = true;

        //Act
        await conn.WaitAsync(0, TestContext.Current.CancellationToken);

        //Assert
        receivedNotification.Should().BeTrue();
        (await conn.ExecuteScalarAsync("SELECT 1", cancellationToken: TestContext.Current.CancellationToken)).Should().Be(1);
    }

    [Fact]
    public async Task WaitAsync_with_timeout()
    {
        //Arrange
        using var conn = OpenConnection();

        //Act
        var result = await conn.WaitAsync(100, TestContext.Current.CancellationToken);

        //Assert
        result.Should().BeFalse();
        conn.ExecuteScalar("SELECT 1").Should().Be(1);
    }

    [Fact]
    public async Task Wait_with_keepalive()
    {
        var notify = GetUniqueIdentifier(nameof(NotificationTests));

        using var dataSource = CreateDataSource(csb =>
        {
            csb.KeepAlive = 1;
            csb.Pooling = false;
        });
        using var conn = dataSource.OpenConnection();
        using var notifyingConn = dataSource.OpenConnection();
        conn.ExecuteNonQuery($"LISTEN {notify}");
        var notificationTask = Task.Delay(2000, TestContext.Current.CancellationToken)
            .ContinueWith(t => notifyingConn.ExecuteNonQuery($"NOTIFY {notify}"), TestContext.Current.CancellationToken);
        conn.Wait();
        conn.ExecuteScalar("SELECT 1").Should().Be(1);
        // A safeguard against closing an active connection
        await notificationTask;
    }

    [Fact]
    public async Task WaitAsync_with_keepalive()
    {
        var notify = GetUniqueIdentifier(nameof(NotificationTests));

        await using var dataSource = CreateDataSource(csb =>
        {
            csb.KeepAlive = 1;
            csb.Pooling = false;
        });
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using var notifyingConn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await conn.ExecuteNonQueryAsync($"LISTEN {notify}", cancellationToken: TestContext.Current.CancellationToken);
        var notificationTask = Task.Delay(2000, TestContext.Current.CancellationToken)
            .ContinueWith(t => notifyingConn.ExecuteNonQuery($"NOTIFY {notify}"), TestContext.Current.CancellationToken);
        await conn.WaitAsync(TestContext.Current.CancellationToken);
        (await conn.ExecuteScalarAsync("SELECT 1", cancellationToken: TestContext.Current.CancellationToken)).Should().Be(1);
        // A safeguard against closing an active connection
        await notificationTask;
    }

    [Fact]
    public async Task WaitAsync_cancellation()
    {
        var notify = GetUniqueIdentifier(nameof(NotificationTests));

        using (var conn = OpenConnection())
        {
            await Assert.ThrowsAsync<OperationCanceledException>(async () => await conn.WaitAsync(new CancellationToken(true)));
            conn.ExecuteScalar("SELECT 1").Should().Be(1);
        }

        using (var conn = OpenConnection())
        {
            conn.ExecuteNonQuery($"LISTEN {notify}");
            var cts = new CancellationTokenSource(1000);
            await Assert.ThrowsAsync<OperationCanceledException>(async () => await conn.WaitAsync(cts.Token));
            conn.ExecuteScalar("SELECT 1").Should().Be(1);
        }
    }

    [Fact]
    public void Wait_breaks_connection()
    {
        //Arrange
        using var dataSource = CreateDataSource();
        using var conn = dataSource.OpenConnection();
        _ = Task.Delay(1000, TestContext.Current.CancellationToken).ContinueWith(t =>
        {
            using var conn2 = OpenConnection();
            conn2.ExecuteNonQuery($"SELECT pg_terminate_backend({conn.ProcessID})");
        }, TestContext.Current.CancellationToken);

        //Act
        var pgEx = Assert.Throws<PostgresException>(conn.Wait);

        //Assert
        pgEx.SqlState.Should().Be(PostgresErrorCodes.AdminShutdown);
        conn.FullState.Should().Be(ConnectionState.Broken);
    }

    [Fact]
    public async Task WaitAsync_breaks_connection()
    {
        //Arrange
        using var dataSource = CreateDataSource();
        using var conn = dataSource.OpenConnection();
        _ = Task.Delay(1000, TestContext.Current.CancellationToken).ContinueWith(t =>
        {
            using var conn2 = OpenConnection();
            conn2.ExecuteNonQuery($"SELECT pg_terminate_backend({conn.ProcessID})");
        }, TestContext.Current.CancellationToken);

        //Act
        var pgEx = await Assert.ThrowsAsync<PostgresException>(async () => await conn.WaitAsync(TestContext.Current.CancellationToken));

        //Assert
        pgEx.SqlState.Should().Be(PostgresErrorCodes.AdminShutdown);
        conn.FullState.Should().Be(ConnectionState.Broken);
    }

    [Fact, IssueLink("https://github.com/npgsql/npgsql/issues/4911")]
    public async Task big_notice_while_loading_types()
    {
        //Arrange
        await using var adminConn = await OpenConnectionAsync();
        // Max notification payload is 8000
        await using var dataSource = CreateDataSource(csb => csb.ReadBufferSize = 4096);
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        var notify = GetUniqueIdentifier(nameof(big_notice_while_loading_types));
        await conn.ExecuteNonQueryAsync($"LISTEN {notify}", cancellationToken: TestContext.Current.CancellationToken);
        var payload = new string('a', 5000);
        await adminConn.ExecuteNonQueryAsync($"NOTIFY {notify}, '{payload}'", cancellationToken: TestContext.Current.CancellationToken);

        //Act
        await conn.ReloadTypesAsync(TestContext.Current.CancellationToken);
    }
}
