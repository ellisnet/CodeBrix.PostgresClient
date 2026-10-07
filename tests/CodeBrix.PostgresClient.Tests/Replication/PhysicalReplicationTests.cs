using System;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.PostgresClient.PgSqlTypes;
using CodeBrix.PostgresClient.Replication;
using SilverAssertions;
using Xunit;

namespace CodeBrix.PostgresClient.Tests.Replication; //was previously: Npgsql.Tests.Replication;

// Every test in this class is explicit because of flakiness.
public class PhysicalReplicationTests : SafeReplicationTestBase<PhysicalReplicationConnection>
{
    [Theory(Explicit = true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public Task CreateReplicationSlot(bool temporary, bool reserveWal)
        => SafeReplicationTest(
            async (slotName, _) =>
            {
                await using var c = await OpenConnectionAsync();
                if (reserveWal)
                    TestUtil.MinimumPgVersion(c, "10.0", "The RESERVE_WAL syntax was introduced in PostgreSQL 10");
                if (temporary)
                    TestUtil.MinimumPgVersion(c, "10.0", "Temporary replication slots were introduced in PostgreSQL 10");

                await using var rc = await OpenReplicationConnectionAsync();
                var slot = await rc.CreateReplicationSlot(slotName, temporary, reserveWal);

                using var cmd =
                    new PgSqlCommand($"SELECT * FROM pg_replication_slots WHERE slot_name = '{slot.Name}'",
                        c);
                await using var reader = await cmd.ExecuteReaderAsync();

                reader.Read().Should().BeTrue();
                reader.GetFieldValue<string>(reader.GetOrdinal("slot_type")).Should().Be("physical");
                reader.Read().Should().BeFalse();
                await rc.DropReplicationSlot(slotName);
            }, nameof(CreateReplicationSlot) + (temporary ? "_t" : "") + (reserveWal ? "_r" : ""));

    [Theory(Explicit = true)]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public Task ReadReplicationSlot(bool createSlot, bool reserveWal)
        => SafeReplicationTest(
            async (slotName, _) =>
            {
                await using var c = await OpenConnectionAsync();
                TestUtil.MinimumPgVersion(c, "15.0", "The READ_REPLICATION_SLOT command was introduced in PostgreSQL 15");
                if (createSlot)
                    await c.ExecuteNonQueryAsync($"SELECT pg_create_physical_replication_slot('{slotName}', {reserveWal}, false)");
                using var cmd =
                    new PgSqlCommand($@"SELECT slot_name, substring(pg_walfile_name(restart_lsn), 1, 8)::bigint AS timeline_id, restart_lsn
                                            FROM pg_replication_slots
                                            WHERE slot_name = '{slotName}'", c);
                await using var reader = await cmd.ExecuteReaderAsync();
                reader.Read().Should().Be(createSlot);
                var expectedSlotName = createSlot ? reader.GetFieldValue<string>(reader.GetOrdinal("slot_name")) : null;
                var expectedTli = createSlot ? (uint?)reader.GetFieldValue<long?>(reader.GetOrdinal("timeline_id")) : null;
                var expectedRestartLsn = createSlot ? reader.GetFieldValue<PgSqlLogSequenceNumber?>(reader.GetOrdinal("restart_lsn")) : null;
                reader.Read().Should().BeFalse();
                await using var rc = await OpenReplicationConnectionAsync();

                var slot = await rc.ReadReplicationSlot(slotName);

                (slot?.Name).Should().Be(expectedSlotName);
                (slot?.RestartTimeline).Should().Be(expectedTli);
                (slot?.RestartLsn).Should().Be(expectedRestartLsn);

            }, $"{nameof(ReadReplicationSlot)}_{reserveWal}");

    [Fact(Explicit = true)]
    public Task replication_with_slot()
        => SafeReplicationTest(
            async (slotName, tableName) =>
            {
                // var messages = new ConcurrentQueue<(PgSqlLogSequenceNumber WalStart, PgSqlLogSequenceNumber WalEnd, byte[] data)>();
                await using var rc = await OpenReplicationConnectionAsync();
                var slot = await rc.CreateReplicationSlot(slotName);
                var info = await rc.IdentifySystem();

                using var streamingCts = new CancellationTokenSource();
                var messages = rc.StartReplication(slot, info.XLogPos, streamingCts.Token).GetAsyncEnumerator();

                await using var c = await OpenConnectionAsync();
                await c.ExecuteNonQueryAsync($"CREATE TABLE {tableName} (value text)");

                for (var i = 1; i <= 10; i++)
                    await c.ExecuteNonQueryAsync($"INSERT INTO {tableName} VALUES ('Value {i}')");

                // We can't assert a lot in physical replication.
                // Since we're replicating in the scope of the whole cluster,
                // other transactions possibly from system processes can
                // interfere here, inserting additional messages, but more
                // likely we'll get everything in one big chunk.
                (await messages.MoveNextAsync()).Should().BeTrue();
                var message = messages.Current;
                message.WalStart.Should().Be(info.XLogPos);
                message.WalEnd.Should().BeGreaterThan(message.WalStart);
                message.Data.Length.Should().BeGreaterThan(0);

                streamingCts.Cancel();
                var exception = (await FluentActions.Awaiting(async () => await messages.MoveNextAsync())
                    .Should().ThrowAsync<OperationCanceledException>()).Which;
                if (c.PostgreSqlVersion < Version.Parse("9.4"))
                {
                    exception.InnerException.Should().BeAssignableTo<PostgresException>()
                        .Which.SqlState.Should().Be(PostgresErrorCodes.QueryCanceled);
                }
            });

    [Fact(Explicit = true)]
    public async Task replication_without_slot()
    {
        await using var rc = await OpenReplicationConnectionAsync(cancellationToken: TestContext.Current.CancellationToken);
        var info = await rc.IdentifySystem(TestContext.Current.CancellationToken);

        using var streamingCts = new CancellationTokenSource();
        var messages = rc.StartReplication(info.XLogPos, streamingCts.Token).GetAsyncEnumerator(TestContext.Current.CancellationToken);

        var tableName = "t_physicalreplicationwithoutslot_p";
        await using var c = await OpenConnectionAsync();
        await c.ExecuteNonQueryAsync($"CREATE TABLE IF NOT EXISTS {tableName} (value text)", cancellationToken: TestContext.Current.CancellationToken);
        try
        {
            for (var i = 1; i <= 10; i++)
                await c.ExecuteNonQueryAsync($"INSERT INTO {tableName} VALUES ('Value {i}')", cancellationToken: TestContext.Current.CancellationToken);

            // We can't assert a lot in physical replication.
            // Since we're replicating in the scope of the whole cluster,
            // other transactions possibly from system processes can
            // interfere here, inserting additional messages, but more
            // likely we'll get everything in one big chunk.
            (await messages.MoveNextAsync()).Should().BeTrue();
            var message = messages.Current;
            message.WalStart.Should().Be(info.XLogPos);
            message.WalEnd.Should().BeGreaterThan(message.WalStart);
            message.Data.Length.Should().BeGreaterThan(0);

            streamingCts.Cancel();
            var exception = (await FluentActions.Awaiting(async () => await messages.MoveNextAsync())
                .Should().ThrowAsync<OperationCanceledException>()).Which;
            if (c.PostgreSqlVersion < Version.Parse("9.4"))
            {
                exception.InnerException.Should().BeAssignableTo<PostgresException>()
                    .Which.SqlState.Should().Be(PostgresErrorCodes.QueryCanceled);
            }
        }
        finally
        {
            await c.ExecuteNonQueryAsync($"DROP TABLE {tableName}", cancellationToken: TestContext.Current.CancellationToken);
        }
    }

    protected override string Postfix => "physical_p";
}
