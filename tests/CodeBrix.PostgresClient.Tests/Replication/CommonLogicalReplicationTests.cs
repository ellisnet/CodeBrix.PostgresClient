using System;
using System.Data;
using System.Threading.Tasks;
using CodeBrix.PostgresClient.PgSqlTypes;
using CodeBrix.PostgresClient.Replication;
using CodeBrix.PostgresClient.Replication.Internal;
using CodeBrix.PostgresClient.Tests.Support;
using SilverAssertions;
using Xunit;

namespace CodeBrix.PostgresClient.Tests.Replication; //was previously: Npgsql.Tests.Replication;

/// <summary>
/// Tests for common logical replication functionality.
/// </summary>
/// <remarks>
/// While these tests might seem superfluous since we perform similar tests
/// for the individual logical replication tests, they are in fact not, because
/// the methods they test are extension points for plugin developers.
/// </remarks>
[Collection(NonParallelCollection.Name)]
public class CommonLogicalReplicationTests : SafeReplicationTestBase<LogicalReplicationConnection>
{
    // We use the test_decoding logical decoding plugin for the common
    // logical replication tests because it has existed since the
    // beginning of logical decoding and by that has the best backwards
    // compatibility.
    const string OutputPlugin = "test_decoding";

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public Task CreateLogicalReplicationSlot(bool temporary, bool twoPhase)
        => SafeReplicationTest(
            async (slotName, _) =>
            {
                await using var c = await OpenConnectionAsync();
                if (twoPhase)
                    TestUtil.MinimumPgVersion(c, "15.0", "Replication slots with two phase commit support were introduced in PostgreSQL 15");
                if (temporary)
                    TestUtil.MinimumPgVersion(c, "10.0", "Temporary replication slots were introduced in PostgreSQL 10");

                await using var rc = await OpenReplicationConnectionAsync();
                var options = await rc.CreateLogicalReplicationSlot(slotName, OutputPlugin, temporary, twoPhase: twoPhase);

                using var cmd =
                    new PgSqlCommand($"SELECT * FROM pg_replication_slots WHERE slot_name = '{options.SlotName}'",
                        c);
                await using var reader = await cmd.ExecuteReaderAsync();

                reader.Read().Should().BeTrue();
                reader.GetFieldValue<string>(reader.GetOrdinal("slot_type")).Should().Be("logical");
                if (c.PostgreSqlVersion >= Version.Parse("15.0"))
                    reader.GetFieldValue<bool>(reader.GetOrdinal("two_phase")).Should().Be(twoPhase);
                if (c.PostgreSqlVersion >= Version.Parse("10.0"))
                    reader.GetFieldValue<bool>(reader.GetOrdinal("temporary")).Should().Be(temporary);
                reader.GetFieldValue<bool>(reader.GetOrdinal("active")).Should().Be(temporary);
                if (c.PostgreSqlVersion >= Version.Parse("9.6"))
                    reader.GetFieldValue<PgSqlLogSequenceNumber>(reader.GetOrdinal("confirmed_flush_lsn")).Should().Be(options.ConsistentPoint);
                reader.Read().Should().BeFalse();
            }, nameof(CreateLogicalReplicationSlot) + (temporary ? "_tmp" : "") + (twoPhase ? "_tp" : ""));

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public Task CreateLogicalReplicationSlot_NoExport(bool temporary, bool twoPhase)
        => SafeReplicationTest(
            async (slotName, _) =>
            {
                await using var c = await OpenConnectionAsync();
                if (temporary)
                    TestUtil.MinimumPgVersion(c, "10.0", "Temporary replication slots were introduced in PostgreSQL 10");
                if (twoPhase)
                    TestUtil.MinimumPgVersion(c, "15.0", "Replication slots with two phase commit support were introduced in PostgreSQL 15");

                TestUtil.MinimumPgVersion(c, "10.0", "The *_SNAPSHOT syntax was introduced in PostgreSQL 10");
                await using var rc = await OpenReplicationConnectionAsync();
                var options = await rc.CreateLogicalReplicationSlot(slotName, OutputPlugin, temporary, LogicalSlotSnapshotInitMode.NoExport, twoPhase);
                options.SnapshotName.Should().BeNull();
            }, nameof(CreateLogicalReplicationSlot_NoExport) + (temporary ? "_tmp" : "") + (twoPhase ? "_tp" : ""));

    // Tests whether we throw a helpful exception about the unsupported *_SNAPSHOT syntax on old servers.
    [Theory]
    [InlineData(LogicalSlotSnapshotInitMode.Export)]
    [InlineData(LogicalSlotSnapshotInitMode.NoExport)]
    [InlineData(LogicalSlotSnapshotInitMode.Use)]
    public Task CreateLogicalReplicationSlot_with_SnapshotInitMode_on_old_postgres_throws(LogicalSlotSnapshotInitMode mode)
        => SafeReplicationTest(
            async (slotName, _) =>
            {
                await using var c = await OpenConnectionAsync();
                TestUtil.MaximumPgVersionExclusive(c, "10.0", "The *_SNAPSHOT syntax was introduced in PostgreSQL 10");
                (await FluentActions.Awaiting(async () =>
                {
                    await using var rc = await OpenReplicationConnectionAsync();
                    await rc.CreateLogicalReplicationSlot(slotName, OutputPlugin, slotSnapshotInitMode: mode);
                }).Should().ThrowAsync<NotSupportedException>()
                    .WithMessage("The EXPORT_SNAPSHOT, USE_SNAPSHOT and NOEXPORT_SNAPSHOT syntax was introduced in PostgreSQL*"))
                    .WithInnerExceptionExactly<PostgresException>()
                    .Where(e => e.SqlState == PostgresErrorCodes.SyntaxError);
            });

    // Tests whether we throw a helpful exception about unsupported temporary replication slots on old servers.
    [Fact]
    public Task CreateLogicalReplicationSlot_with_isTemporary_set_to_true_on_old_postgres_throws()
        => SafeReplicationTest(
            async (slotName, _) =>
            {
                await using var c = await OpenConnectionAsync();
                TestUtil.MaximumPgVersionExclusive(c, "10.0", "Temporary replication slots were introduced in PostgreSQL 10");
                (await FluentActions.Awaiting(async () =>
                {
                    await using var rc = await OpenReplicationConnectionAsync();
                    await rc.CreateLogicalReplicationSlot(slotName, OutputPlugin, isTemporary: true);
                }).Should().ThrowAsync<NotSupportedException>()
                    .WithMessage("Temporary replication slots were introduced in PostgreSQL*"))
                    .WithInnerExceptionExactly<PostgresException>()
                    .Where(e => e.SqlState == PostgresErrorCodes.SyntaxError);
            });

    // Tests whether we throw a helpful exception about the unsupported TWO_PHASE syntax on old servers.
    [Fact]
    public Task CreateLogicalReplicationSlot_with_twoPhase_set_to_true_on_old_postgres_throws()
        => SafeReplicationTest(
            async (slotName, _) =>
            {
                await using var c = await OpenConnectionAsync();
                TestUtil.MaximumPgVersionExclusive(c, "15.0",
                    "Logical replication support for prepared transactions was  introduced in PostgreSQL 15");
                (await FluentActions.Awaiting(async () =>
                {
                    await using var rc = await OpenReplicationConnectionAsync();
                    await rc.CreateLogicalReplicationSlot(slotName, OutputPlugin, twoPhase: true);
                }).Should().ThrowAsync<NotSupportedException>()
                    .WithMessage("Logical replication support for prepared transactions was introduced in PostgreSQL*"))
                    .WithInnerExceptionExactly<PostgresException>()
                    .Where(e => e.SqlState == PostgresErrorCodes.SyntaxError);
            });

    // We can use the exported snapshot to query the database in the very moment the replication slot was created.
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public Task CreateLogicalReplicationSlot_Export(bool temporary, bool twoPhase, bool implicitInitMode)
        => SafeReplicationTest(
            async (slotName, tableName) =>
            {
                await using var c = await OpenConnectionAsync();
                if (temporary)
                    TestUtil.MinimumPgVersion(c, "10.0", "Temporary replication slots were introduced in PostgreSQL 10");
                if (twoPhase)
                    TestUtil.MinimumPgVersion(c, "15.0", "Replication slots with two phase commit support were introduced in PostgreSQL 15");
                if (!implicitInitMode)
                    TestUtil.MinimumPgVersion(c, "10.0", "The *_SNAPSHOT syntax was introduced in PostgreSQL 10");
                await using (var transaction = c.BeginTransaction())
                {
                    await c.ExecuteNonQueryAsync($"CREATE TABLE {tableName} (value text)");
                    await c.ExecuteNonQueryAsync($"INSERT INTO {tableName} (value) VALUES('Before snapshot')");
                    transaction.Commit();
                }
                await using var rc = await OpenReplicationConnectionAsync();
                var options = await rc.CreateLogicalReplicationSlot(slotName, OutputPlugin, temporary, implicitInitMode ? null : LogicalSlotSnapshotInitMode.Export, twoPhase);
                await using (var transaction = c.BeginTransaction())
                {
                    await c.ExecuteNonQueryAsync($"INSERT INTO {tableName} (value) VALUES('After snapshot')");
                    transaction.Commit();
                }
                await using (var transaction = c.BeginTransaction(IsolationLevel.RepeatableRead))
                {
                    await c.ExecuteScalarAsync($"SET TRANSACTION SNAPSHOT '{options.SnapshotName}';", transaction);
                    using var cmd = new PgSqlCommand($"SELECT value FROM {tableName}", c, transaction);
                    await using var reader = await cmd.ExecuteReaderAsync();
                    reader.Read().Should().BeTrue();
                    reader.GetFieldValue<string>(0).Should().Be("Before snapshot");
                    reader.Read().Should().BeFalse();
                }
            }, nameof(CreateLogicalReplicationSlot_Export) + (temporary ? "_tmp" : "") + (twoPhase ? "_tp" : "") + (implicitInitMode ? "_i" : ""));

    // Since we currently don't provide an API to start a transaction on a logical replication connection,
    // USE_SNAPSHOT currently doesn't work and always leads to an exception. On the other hand, starting
    // a transaction would only be useful if we'd also provide an API to issue commands.
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public Task CreateLogicalReplicationSlot_Use(bool temporary, bool twoPhase)
        => SafeReplicationTest(
            async (slotName, _) =>
            {
                await using var c = await OpenConnectionAsync();
                if (temporary)
                    TestUtil.MinimumPgVersion(c, "10.0", "Temporary replication slots were introduced in PostgreSQL 10");
                if (twoPhase)
                    TestUtil.MinimumPgVersion(c, "15.0", "Replication slots with two phase commit support were introduced in PostgreSQL 15");

                TestUtil.MinimumPgVersion(c, "10.0", "The *_SNAPSHOT syntax was introduced in PostgreSQL 10");
                var expectedMessagePart = c.PostgreSqlVersion.Major < 15
                    ? "USE_SNAPSHOT"
                    : "(SNAPSHOT 'use')";
                await FluentActions.Awaiting(async () =>
                {
                    await using var rc = await OpenReplicationConnectionAsync();
                    await rc.CreateLogicalReplicationSlot(slotName, OutputPlugin, temporary, LogicalSlotSnapshotInitMode.Use, twoPhase);
                }).Should().ThrowAsync<PostgresException>()
                    .Where(e => e.SqlState == "XX000" && e.Message.Contains(expectedMessagePart));
            }, nameof(CreateLogicalReplicationSlot_Use) + (temporary ? "_tmp" : "") + (twoPhase ? "_tp" : ""));

    [Fact]
    public Task CreateLogicalReplicationSlot_with_null_slot_throws()
        => FluentActions.Awaiting(async () =>
        {
            await using var rc = await OpenReplicationConnectionAsync();
            await rc.CreateLogicalReplicationSlot(null, OutputPlugin);
        }).Should().ThrowExactlyAsync<ArgumentNullException>()
            .WithParameterName("slotName");

    [Fact]
    public Task CreateLogicalReplicationSlot_with_null_output_plugin_throws()
        => SafeReplicationTest(
            async (slotName, _) =>
            {
                await FluentActions.Awaiting(async () =>
                {
                    await using var rc = await OpenReplicationConnectionAsync();
                    await rc.CreateLogicalReplicationSlot(slotName, null);
                }).Should().ThrowExactlyAsync<ArgumentNullException>()
                    .WithParameterName("outputPlugin");
            });

    [Fact]
    public Task CreateLogicalReplicationSlot_with_cancelled_token()
        => SafeReplicationTest(
            async (slotName, _) =>
            {
                await FluentActions.Awaiting(async () =>
                {
                    await using var rc = await OpenReplicationConnectionAsync();
                    var token = GetCancelledCancellationToken();
                    await rc.CreateLogicalReplicationSlot(slotName, OutputPlugin, cancellationToken: token);
                }).Should().ThrowAsync<OperationCanceledException>();
            });

    [Fact]
    public Task CreateLogicalReplicationSlot_with_invalid_SnapshotInitMode_throws()
        => SafeReplicationTest(
            async (slotName, _) =>
            {
                await FluentActions.Awaiting(async () =>
                {
                    await using var rc = await OpenReplicationConnectionAsync();
                    await rc.CreateLogicalReplicationSlot(slotName, OutputPlugin, slotSnapshotInitMode: (LogicalSlotSnapshotInitMode)42);
                }).Should().ThrowAsync<ArgumentOutOfRangeException>()
                    .Where(e => e.ParamName == "slotSnapshotInitMode"
                                && Equals(e.ActualValue, (LogicalSlotSnapshotInitMode)42));
            });

    [Fact]
    public Task CreateLogicalReplicationSlot_with_disposed_connection_throws()
        => SafeReplicationTest(
            async (slotName, _) =>
            {
                await FluentActions.Awaiting(async () =>
                {
                    var rc = await OpenReplicationConnectionAsync();
                    await rc.DisposeAsync();
                    await rc.CreateLogicalReplicationSlot(slotName, OutputPlugin);
                }).Should().ThrowAsync<ObjectDisposedException>()
                    .Where(e => e.ObjectName == nameof(LogicalReplicationConnection));
            });

    protected override string Postfix => "commonl_l";
}
