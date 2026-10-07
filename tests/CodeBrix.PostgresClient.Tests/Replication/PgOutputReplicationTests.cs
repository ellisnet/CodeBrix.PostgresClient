using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.PostgresClient.Replication;
using CodeBrix.PostgresClient.Replication.PgOutput;
using CodeBrix.PostgresClient.Replication.PgOutput.Messages;
using CodeBrix.PostgresClient.Tests.Support;
using CodeBrix.PostgresClient.Util;
using SilverAssertions;
using Xunit;
using ReplicaIdentitySetting = CodeBrix.PostgresClient.Replication.PgOutput.Messages.RelationMessage.ReplicaIdentitySetting;
using TruncateOptions = CodeBrix.PostgresClient.Replication.PgOutput.Messages.TruncateMessage.TruncateOptions;
using static CodeBrix.PostgresClient.Tests.TestUtil;

namespace CodeBrix.PostgresClient.Tests.Replication; //was previously: Npgsql.Tests.Replication;

// These tests aren't designed to be parallelizable: every concrete subclass below is in the
// non-parallel collection.
public abstract class PgOutputReplicationTests(
    PgOutputProtocolVersion protocolVersion,
    PgOutputReplicationTests.ReplicationDataMode dataMode,
    PgOutputReplicationTests.TransactionMode transactionMode)
    : SafeReplicationTestBase<LogicalReplicationConnection>
{
    readonly bool? _binary = dataMode == ReplicationDataMode.BinaryReplicationDataMode
        ? true
        : dataMode == ReplicationDataMode.TextReplicationDataMode
            ? false
            : null;
    readonly PgOutputStreamingMode? _streamingMode = transactionMode switch
    {
        TransactionMode.DefaultTransactionMode => null,
        TransactionMode.NonStreamingTransactionMode => PgOutputStreamingMode.Off,
        TransactionMode.StreamingTransactionMode => PgOutputStreamingMode.On,
        TransactionMode.ParallelStreamingTransactionMode => PgOutputStreamingMode.Parallel,
        _ => throw new ArgumentOutOfRangeException(nameof(transactionMode), transactionMode, null)
    };

    bool IsBinary => _binary ?? false;
    bool IsStreaming => _streamingMode.HasValue && _streamingMode.Value != PgOutputStreamingMode.Off;
    PgOutputProtocolVersion Version => protocolVersion;

    [Fact]
    public Task CreatePgOutputReplicationSlot()
    {
        // There's nothing special here for binary data or when streaming so only execute once
        if (IsBinary || IsStreaming)
            return Task.CompletedTask;

        return SafeReplicationTest(
            async (slotName, _) =>
            {

                await using var c = await OpenConnectionAsync();
                await using var rc = await OpenReplicationConnectionAsync();
                var options = await rc.CreatePgOutputReplicationSlot(slotName);

                using var cmd =
                    new PgSqlCommand($"SELECT * FROM pg_replication_slots WHERE slot_name = '{options.Name}'",
                        c);
                await using var reader = await cmd.ExecuteReaderAsync();

                reader.Read().Should().BeTrue();
                reader.GetFieldValue<string>(reader.GetOrdinal("slot_type")).Should().Be("logical");
                reader.GetFieldValue<string>(reader.GetOrdinal("plugin")).Should().Be("pgoutput");
                reader.Read().Should().BeFalse();
            });
    }

    // Tests whether INSERT commands get replicated as Logical Replication Protocol Messages
    [Fact]
    public Task insert()
        => SafePgOutputReplicationTest(
            async (slotName, tableName, publicationName) =>
            {
                await using var c = await OpenConnectionAsync();
                await c.ExecuteNonQueryAsync(@$"CREATE TABLE {tableName} (id INT PRIMARY KEY, name TEXT NULL);
                                                    CREATE PUBLICATION {publicationName} FOR TABLE {tableName};");
                await using var rc = await OpenReplicationConnectionAsync();
                var slot = await rc.CreatePgOutputReplicationSlot(slotName);

                await using var tran = await c.BeginTransactionAsync();
                await c.ExecuteNonQueryAsync(@$"INSERT INTO {tableName} VALUES (1, 'val1'), (2, NULL), (3, 'ignored');
                                                    INSERT INTO {tableName} SELECT i, 'val' || i::text FROM generate_series(4, 15000) s(i);");
                await tran.CommitAsync();

                using var streamingCts = new CancellationTokenSource();
                var messages = SkipEmptyTransactions(rc.StartReplication(slot, GetOptions(publicationName), streamingCts.Token))
                    .GetAsyncEnumerator();

                // Begin Transaction
                var transactionXid = await AssertTransactionStart(messages);

                // Relation
                var relationMsg = await NextMessage<RelationMessage>(messages);
                relationMsg.TransactionXid.Should().Be(IsStreaming ? transactionXid : null);
                relationMsg.ReplicaIdentity.Should().Be(ReplicaIdentitySetting.Default);
                relationMsg.Namespace.Should().Be("public");
                relationMsg.RelationName.Should().Be(tableName);
                relationMsg.Columns.Count.Should().Be(2);
                relationMsg.Columns[0].ColumnName.Should().Be("id");
                relationMsg.Columns[1].ColumnName.Should().Be("name");

                // Insert first value
                var insertMsg = await NextMessage<InsertMessage>(messages);
                insertMsg.TransactionXid.Should().Be(IsStreaming ? transactionXid : null);
                insertMsg.Relation.Should().BeSameAs(relationMsg);
                var columnEnumerator = insertMsg.NewRow.GetAsyncEnumerator();
                (await columnEnumerator.MoveNextAsync()).Should().BeTrue();
                var postgresType = columnEnumerator.Current.GetPostgresType();
                postgresType.FullName.Should().Be("pg_catalog.integer");
                columnEnumerator.Current.GetDataTypeName().Should().Be("integer");
                columnEnumerator.Current.GetFieldName().Should().Be("id");
                if (IsBinary)
                {
                    columnEnumerator.Current.GetFieldType().Should().Be(typeof(int));
                    (await columnEnumerator.Current.Get<int>()).Should().Be(1);
                }
                else
                {
                    columnEnumerator.Current.GetFieldType().Should().Be(typeof(string));
                    (await columnEnumerator.Current.Get<string>()).Should().Be("1");
                }

                (await columnEnumerator.MoveNextAsync()).Should().BeTrue();
                postgresType = columnEnumerator.Current.GetPostgresType();
                postgresType.FullName.Should().Be("pg_catalog.text");
                columnEnumerator.Current.GetDataTypeName().Should().Be("text");
                columnEnumerator.Current.GetFieldType().Should().Be(typeof(string));
                columnEnumerator.Current.GetFieldName().Should().Be("name");
                columnEnumerator.Current.IsDBNull.Should().BeFalse();
                (await columnEnumerator.Current.Get<string>()).Should().Be("val1");
                (await columnEnumerator.MoveNextAsync()).Should().BeFalse();

                // Insert second value
                insertMsg = await NextMessage<InsertMessage>(messages);
                insertMsg.TransactionXid.Should().Be(IsStreaming ? transactionXid : null);
                insertMsg.Relation.Should().BeSameAs(relationMsg);
                columnEnumerator = insertMsg.NewRow.GetAsyncEnumerator();
                (await columnEnumerator.MoveNextAsync()).Should().BeTrue();
                if (IsBinary)
                    (await columnEnumerator.Current.Get<int>()).Should().Be(2);
                else
                    (await columnEnumerator.Current.Get<string>()).Should().Be("2");
                (await columnEnumerator.MoveNextAsync()).Should().BeTrue();
                columnEnumerator.Current.IsDBNull.Should().BeTrue();
                (await columnEnumerator.MoveNextAsync()).Should().BeFalse();

                // Insert third value
                insertMsg = await NextMessage<InsertMessage>(messages);
                insertMsg.TransactionXid.Should().Be(IsStreaming ? transactionXid : null);
                insertMsg.Relation.Should().BeSameAs(relationMsg);
                await foreach (var tuple in insertMsg.NewRow) // Don't consume the value to trigger eventual bugs
                    tuple.Kind.Should().Be(IsBinary ? TupleDataKind.BinaryValue : TupleDataKind.TextValue);

                // Remaining inserts
                for (var insertCount = 0; insertCount < 14997; insertCount++)
                {
                    await NextMessage<InsertMessage>(messages);
                }

                // Commit Transaction
                await AssertTransactionCommit(messages);

                streamingCts.Cancel();
                await AssertReplicationCancellation(messages);
                await rc.DropReplicationSlot(slotName, cancellationToken: TestContext.Current.CancellationToken);
            });

    // Tests whether UPDATE commands get replicated as Logical Replication Protocol Messages for tables using the default replica identity
    [Fact]
    public Task update_for_default_replica_identity()
        => SafeReplicationTest(
            async (slotName, tableName, publicationName) =>
            {
                await using var c = await OpenConnectionAsync();
                await c.ExecuteNonQueryAsync(@$"CREATE TABLE {tableName} (id INT PRIMARY KEY, name TEXT NOT NULL);
                                                    INSERT INTO {tableName} SELECT i, 'val' || i::text FROM generate_series(1, 15000) s(i);
                                                    CREATE PUBLICATION {publicationName} FOR TABLE {tableName};");
                await using var rc = await OpenReplicationConnectionAsync();
                var slot = await rc.CreatePgOutputReplicationSlot(slotName);

                await using var tran = await c.BeginTransactionAsync();
                await c.ExecuteNonQueryAsync(@$"UPDATE {tableName} SET name='val1_updated' WHERE id = 1;
                                                    UPDATE {tableName} SET name = md5(name) WHERE id > 1");
                await tran.CommitAsync();

                using var streamingCts = new CancellationTokenSource();
                var messages = SkipEmptyTransactions(rc.StartReplication(slot, GetOptions(publicationName), streamingCts.Token))
                    .GetAsyncEnumerator();

                // Begin Transaction
                var transactionXid = await AssertTransactionStart(messages);

                // Relation
                var relationMsg = await NextMessage<RelationMessage>(messages);
                relationMsg.TransactionXid.Should().Be(IsStreaming ? transactionXid : null);
                relationMsg.ReplicaIdentity.Should().Be(ReplicaIdentitySetting.Default);
                relationMsg.Namespace.Should().Be("public");
                relationMsg.RelationName.Should().Be(tableName);
                relationMsg.Columns.Count.Should().Be(2);
                relationMsg.Columns[0].ColumnName.Should().Be("id");
                relationMsg.Columns[1].ColumnName.Should().Be("name");

                // Update
                var updateMsg = await NextMessage<DefaultUpdateMessage>(messages);
                updateMsg.TransactionXid.Should().Be(IsStreaming ? transactionXid : null);
                updateMsg.Relation.Should().BeSameAs(relationMsg);
                var columnEnumerator = updateMsg.NewRow.GetAsyncEnumerator();
                (await columnEnumerator.MoveNextAsync()).Should().BeTrue();
                if (IsBinary)
                    (await columnEnumerator.Current.Get<int>()).Should().Be(1);
                else
                    (await columnEnumerator.Current.Get<string>()).Should().Be("1");
                (await columnEnumerator.MoveNextAsync()).Should().BeTrue();
                columnEnumerator.Current.IsDBNull.Should().BeFalse();
                (await columnEnumerator.Current.Get<string>()).Should().Be("val1_updated");
                (await columnEnumerator.MoveNextAsync()).Should().BeFalse();

                // Remaining updates
                for (var updateCount = 0; updateCount < 14999; updateCount++)
                    await NextMessage<DefaultUpdateMessage>(messages);

                // Commit Transaction
                await AssertTransactionCommit(messages);

                streamingCts.Cancel();
                await AssertReplicationCancellation(messages);
                await rc.DropReplicationSlot(slotName, cancellationToken: TestContext.Current.CancellationToken);
            });

    // Tests whether UPDATE commands get replicated as Logical Replication Protocol Messages for tables using an index as replica identity
    [Fact]
    public  Task update_for_index_replica_identity()
        => SafeReplicationTest(
            async (slotName, tableName, publicationName) =>
            {
                await using var c = await OpenConnectionAsync();
                var indexName = $"i_{tableName.Substring(2)}";
                await c.ExecuteNonQueryAsync(@$"CREATE TABLE {tableName} (id INT PRIMARY KEY, name TEXT NOT NULL);
                                                    CREATE UNIQUE INDEX {indexName} ON {tableName} (name);
                                                    ALTER TABLE {tableName} REPLICA IDENTITY USING INDEX {indexName};
                                                    INSERT INTO {tableName} SELECT i, 'val' || i::text FROM generate_series(1, 15000) s(i);
                                                    CREATE PUBLICATION {publicationName} FOR TABLE {tableName};");
                await using var rc = await OpenReplicationConnectionAsync();
                var slot = await rc.CreatePgOutputReplicationSlot(slotName);

                await using var tran = await c.BeginTransactionAsync();
                await c.ExecuteNonQueryAsync(@$"UPDATE {tableName} SET name='val1_updated' WHERE id = 1;
                                                    UPDATE {tableName} SET name = md5(name) WHERE id > 1");
                await tran.CommitAsync();

                using var streamingCts = new CancellationTokenSource();
                var messages = SkipEmptyTransactions(rc.StartReplication(slot, GetOptions(publicationName), streamingCts.Token))
                    .GetAsyncEnumerator();

                // Begin Transaction
                var transactionXid = await AssertTransactionStart(messages);

                // Relation
                var relationMsg = await NextMessage<RelationMessage>(messages);
                relationMsg.TransactionXid.Should().Be(IsStreaming ? transactionXid : null);
                relationMsg.ReplicaIdentity.Should().Be(ReplicaIdentitySetting.IndexWithIndIsReplIdent);
                relationMsg.Namespace.Should().Be("public");
                relationMsg.RelationName.Should().Be(tableName);
                relationMsg.Columns.Count.Should().Be(2);
                relationMsg.Columns[0].ColumnName.Should().Be("id");
                relationMsg.Columns[1].ColumnName.Should().Be("name");

                // Update
                var updateMsg = await NextMessage<IndexUpdateMessage>(messages);
                updateMsg.TransactionXid.Should().Be(IsStreaming ? transactionXid : null);
                updateMsg.Relation.Should().BeSameAs(relationMsg);

                var oldRowColumnEnumerator = updateMsg.Key.GetAsyncEnumerator();
                (await oldRowColumnEnumerator.MoveNextAsync()).Should().BeTrue();
                oldRowColumnEnumerator.Current.IsDBNull.Should().BeTrue();
                (await oldRowColumnEnumerator.MoveNextAsync()).Should().BeTrue();
                (await oldRowColumnEnumerator.Current.Get<string>()).Should().Be("val1");
                (await oldRowColumnEnumerator.MoveNextAsync()).Should().BeFalse();

                var newRowColumnEnumerator = updateMsg.NewRow.GetAsyncEnumerator();
                (await newRowColumnEnumerator.MoveNextAsync()).Should().BeTrue();
                if (IsBinary)
                    (await newRowColumnEnumerator.Current.Get<int>()).Should().Be(1);
                else
                    (await newRowColumnEnumerator.Current.Get<string>()).Should().Be("1");
                (await newRowColumnEnumerator.MoveNextAsync()).Should().BeTrue();
                (await newRowColumnEnumerator.Current.Get<string>()).Should().Be("val1_updated");
                (await newRowColumnEnumerator.MoveNextAsync()).Should().BeFalse();

                // Remaining updates
                for (var updateCount = 0; updateCount < 14999; updateCount++)
                    await NextMessage<IndexUpdateMessage>(messages);

                // Commit Transaction
                await AssertTransactionCommit(messages);

                streamingCts.Cancel();
                await AssertReplicationCancellation(messages);
                await rc.DropReplicationSlot(slotName, cancellationToken: TestContext.Current.CancellationToken);
            });

    // Tests whether UPDATE commands get replicated as Logical Replication Protocol Messages for tables using full replica identity
    [Fact]
    public  Task update_for_full_replica_identity()
        => SafeReplicationTest(
            async (slotName, tableName, publicationName) =>
            {
                await using var c = await OpenConnectionAsync();
                await c.ExecuteNonQueryAsync(@$"CREATE TABLE {tableName} (id INT PRIMARY KEY, name TEXT NOT NULL);
                                                    ALTER TABLE {tableName} REPLICA IDENTITY FULL;
                                                    INSERT INTO {tableName} SELECT i, 'val' || i::text FROM generate_series(1, 15000) s(i);
                                                    CREATE PUBLICATION {publicationName} FOR TABLE {tableName};");
                await using var rc = await OpenReplicationConnectionAsync();
                var slot = await rc.CreatePgOutputReplicationSlot(slotName);

                await using var tran = await c.BeginTransactionAsync();
                await c.ExecuteNonQueryAsync(@$"UPDATE {tableName} SET name='val1_updated' WHERE id = 1;
                                                    UPDATE {tableName} SET name = md5(name) WHERE id > 1");
                await tran.CommitAsync();

                using var streamingCts = new CancellationTokenSource();
                var messages = SkipEmptyTransactions(rc.StartReplication(slot, GetOptions(publicationName), streamingCts.Token))
                    .GetAsyncEnumerator();

                // Begin Transaction
                var transactionXid = await AssertTransactionStart(messages);

                // Relation
                var relationMsg = await NextMessage<RelationMessage>(messages);
                relationMsg.TransactionXid.Should().Be(IsStreaming ? transactionXid : null);
                relationMsg.ReplicaIdentity.Should().Be(ReplicaIdentitySetting.AllColumns);
                relationMsg.Namespace.Should().Be("public");
                relationMsg.RelationName.Should().Be(tableName);
                relationMsg.Columns.Count.Should().Be(2);
                relationMsg.Columns[0].ColumnName.Should().Be("id");
                relationMsg.Columns[1].ColumnName.Should().Be("name");

                // Update
                var updateMsg = await NextMessage<FullUpdateMessage>(messages);
                updateMsg.TransactionXid.Should().Be(IsStreaming ? transactionXid : null);
                updateMsg.Relation.Should().BeSameAs(relationMsg);

                var oldRowColumnEnumerator = updateMsg.OldRow.GetAsyncEnumerator();
                (await oldRowColumnEnumerator.MoveNextAsync()).Should().BeTrue();
                if (IsBinary)
                    (await oldRowColumnEnumerator.Current.Get<int>()).Should().Be(1);
                else
                    (await oldRowColumnEnumerator.Current.Get<string>()).Should().Be("1");
                (await oldRowColumnEnumerator.MoveNextAsync()).Should().BeTrue();
                (await oldRowColumnEnumerator.Current.Get<string>()).Should().Be("val1");
                (await oldRowColumnEnumerator.MoveNextAsync()).Should().BeFalse();

                var newRowColumnEnumerator = updateMsg.NewRow.GetAsyncEnumerator();
                (await newRowColumnEnumerator.MoveNextAsync()).Should().BeTrue();
                (await newRowColumnEnumerator.MoveNextAsync()).Should().BeTrue();
                (await newRowColumnEnumerator.Current.Get<string>()).Should().Be("val1_updated");
                (await newRowColumnEnumerator.MoveNextAsync()).Should().BeFalse();

                // Remaining updates
                for (var updateCount = 0; updateCount < 14999; updateCount++)
                    await NextMessage<FullUpdateMessage>(messages);

                // Commit Transaction
                await AssertTransactionCommit(messages);

                streamingCts.Cancel();
                (await FluentActions.Awaiting(async () => await messages.MoveNextAsync())
                    .Should().ThrowAsync<OperationCanceledException>())
                    .WithInnerException<PostgresException>()
                    .Where(e => e.SqlState == PostgresErrorCodes.QueryCanceled);
                await rc.DropReplicationSlot(slotName, cancellationToken: TestContext.Current.CancellationToken);
            });

    // Tests whether DELETE commands get replicated as Logical Replication Protocol Messages for tables using the default replica identity
    [Fact]
    public Task delete_for_default_replica_identity()
        => SafeReplicationTest(
            async (slotName, tableName, publicationName) =>
            {
                await using var c = await OpenConnectionAsync();
                await c.ExecuteNonQueryAsync(@$"CREATE TABLE {tableName} (id INT PRIMARY KEY, name TEXT NOT NULL);
                                                    INSERT INTO {tableName} SELECT i, 'val' || i::text FROM generate_series(1, 15000) s(i);
                                                    CREATE PUBLICATION {publicationName} FOR TABLE {tableName};");
                await using var rc = await OpenReplicationConnectionAsync();
                var slot = await rc.CreatePgOutputReplicationSlot(slotName);

                await using var tran = await c.BeginTransactionAsync();
                await c.ExecuteNonQueryAsync(@$"DELETE FROM {tableName} WHERE id = 1;
                                                    DELETE FROM {tableName} WHERE id > 1");
                await tran.CommitAsync();

                using var streamingCts = new CancellationTokenSource();
                var messages = SkipEmptyTransactions(rc.StartReplication(slot, GetOptions(publicationName), streamingCts.Token))
                    .GetAsyncEnumerator();

                // Begin Transaction
                var transactionXid = await AssertTransactionStart(messages);

                // Relation
                var relationMsg = await NextMessage<RelationMessage>(messages);
                relationMsg.TransactionXid.Should().Be(IsStreaming ? transactionXid : null);
                relationMsg.ReplicaIdentity.Should().Be(ReplicaIdentitySetting.Default);
                relationMsg.Namespace.Should().Be("public");
                relationMsg.RelationName.Should().Be(tableName);
                relationMsg.Columns.Count.Should().Be(2);
                relationMsg.Columns[0].ColumnName.Should().Be("id");
                relationMsg.Columns[1].ColumnName.Should().Be("name");

                // Delete
                var deleteMsg = await NextMessage<KeyDeleteMessage>(messages);
                deleteMsg.TransactionXid.Should().Be(IsStreaming ? transactionXid : null);
                deleteMsg.Relation.Should().BeSameAs(relationMsg);
                var columnEnumerator = deleteMsg.Key.GetAsyncEnumerator();
                (await columnEnumerator.MoveNextAsync()).Should().BeTrue();
                if (IsBinary)
                    (await columnEnumerator.Current.Get<int>()).Should().Be(1);
                else
                    (await columnEnumerator.Current.Get<string>()).Should().Be("1");
                (await columnEnumerator.MoveNextAsync()).Should().BeTrue();
                columnEnumerator.Current.IsDBNull.Should().BeTrue();
                (await columnEnumerator.MoveNextAsync()).Should().BeFalse();

                // Remaining deletes
                for (var deleteCount = 0; deleteCount < 14999; deleteCount++)
                    await NextMessage<KeyDeleteMessage>(messages);

                // Commit Transaction
                await AssertTransactionCommit(messages);

                streamingCts.Cancel();
                await AssertReplicationCancellation(messages);
                await rc.DropReplicationSlot(slotName, cancellationToken: TestContext.Current.CancellationToken);
            });

    // Tests whether DELETE commands get replicated as Logical Replication Protocol Messages for tables using an index as replica identity
    [Fact]
    public Task delete_for_index_replica_identity()
        => SafeReplicationTest(
            async (slotName, tableName, publicationName) =>
            {
                await using var c = await OpenConnectionAsync();
                var indexName = $"i_{tableName.Substring(2)}";
                await c.ExecuteNonQueryAsync(@$"CREATE TABLE {tableName} (id INT PRIMARY KEY, name TEXT NOT NULL);
                                                    CREATE UNIQUE INDEX {indexName} ON {tableName} (name);
                                                    ALTER TABLE {tableName} REPLICA IDENTITY USING INDEX {indexName};
                                                    INSERT INTO {tableName} SELECT i, 'val' || i::text FROM generate_series(1, 15000) s(i);
                                                    CREATE PUBLICATION {publicationName} FOR TABLE {tableName};");
                await using var rc = await OpenReplicationConnectionAsync();
                var slot = await rc.CreatePgOutputReplicationSlot(slotName);

                await using var tran = await c.BeginTransactionAsync();
                await c.ExecuteNonQueryAsync(@$"DELETE FROM {tableName} WHERE id = 1;
                                                    DELETE FROM {tableName} WHERE id > 1");
                await tran.CommitAsync();

                using var streamingCts = new CancellationTokenSource();
                var messages = SkipEmptyTransactions(rc.StartReplication(slot, GetOptions(publicationName), streamingCts.Token))
                    .GetAsyncEnumerator();

                // Begin Transaction
                var transactionXid = await AssertTransactionStart(messages);

                // Relation
                var relationMsg = await NextMessage<RelationMessage>(messages);
                relationMsg.TransactionXid.Should().Be(IsStreaming ? transactionXid : null);
                relationMsg.ReplicaIdentity.Should().Be(ReplicaIdentitySetting.IndexWithIndIsReplIdent);
                relationMsg.Namespace.Should().Be("public");
                relationMsg.RelationName.Should().Be(tableName);
                relationMsg.Columns.Count.Should().Be(2);
                relationMsg.Columns[0].ColumnName.Should().Be("id");
                relationMsg.Columns[1].ColumnName.Should().Be("name");

                // Delete
                var deleteMsg = await NextMessage<KeyDeleteMessage>(messages);
                deleteMsg.TransactionXid.Should().Be(IsStreaming ? transactionXid : null);
                deleteMsg.Relation.Should().BeSameAs(relationMsg);
                var columnEnumerator = deleteMsg.Key.GetAsyncEnumerator();
                (await columnEnumerator.MoveNextAsync()).Should().BeTrue();
                columnEnumerator.Current.IsDBNull.Should().BeTrue();
                (await columnEnumerator.MoveNextAsync()).Should().BeTrue();
                (await columnEnumerator.Current.Get<string>()).Should().Be("val1");
                (await columnEnumerator.MoveNextAsync()).Should().BeFalse();

                // Remaining deletes
                for (var deleteCount = 0; deleteCount < 14999; deleteCount++)
                    await NextMessage<KeyDeleteMessage>(messages);

                // Commit Transaction
                await AssertTransactionCommit(messages);

                streamingCts.Cancel();
                await AssertReplicationCancellation(messages);
                await rc.DropReplicationSlot(slotName, cancellationToken: TestContext.Current.CancellationToken);
            });

    // Tests whether DELETE commands get replicated as Logical Replication Protocol Messages for tables using full replica identity
    [Fact]
    public Task delete_for_full_replica_identity()
        => SafeReplicationTest(
            async (slotName, tableName, publicationName) =>
            {
                await using var c = await OpenConnectionAsync();
                await c.ExecuteNonQueryAsync(@$"CREATE TABLE {tableName} (id INT PRIMARY KEY, name TEXT NOT NULL);
                                                    ALTER TABLE {tableName} REPLICA IDENTITY FULL;
                                                    INSERT INTO {tableName} SELECT i, 'val' || i::text FROM generate_series(1, 15000) s(i);
                                                    CREATE PUBLICATION {publicationName} FOR TABLE {tableName};");
                await using var rc = await OpenReplicationConnectionAsync();
                var slot = await rc.CreatePgOutputReplicationSlot(slotName);

                await using var tran = await c.BeginTransactionAsync();
                await c.ExecuteNonQueryAsync(@$"DELETE FROM {tableName} WHERE id = 1;
                                                    DELETE FROM {tableName} WHERE id > 1");
                await tran.CommitAsync();

                using var streamingCts = new CancellationTokenSource();
                var messages = SkipEmptyTransactions(rc.StartReplication(slot, GetOptions(publicationName), streamingCts.Token))
                    .GetAsyncEnumerator();

                // Begin Transaction
                var transactionXid = await AssertTransactionStart(messages);

                // Relation
                var relationMsg = await NextMessage<RelationMessage>(messages);
                relationMsg.TransactionXid.Should().Be(IsStreaming ? transactionXid : null);
                relationMsg.ReplicaIdentity.Should().Be(ReplicaIdentitySetting.AllColumns);
                relationMsg.Namespace.Should().Be("public");
                relationMsg.RelationName.Should().Be(tableName);
                relationMsg.Columns.Count.Should().Be(2);
                relationMsg.Columns[0].ColumnName.Should().Be("id");
                relationMsg.Columns[1].ColumnName.Should().Be("name");

                // Delete
                var deleteMsg = await NextMessage<FullDeleteMessage>(messages);
                deleteMsg.TransactionXid.Should().Be(IsStreaming ? transactionXid : null);
                deleteMsg.Relation.Should().BeSameAs(relationMsg);
                var columnEnumerator = deleteMsg.OldRow.GetAsyncEnumerator();
                (await columnEnumerator.MoveNextAsync()).Should().BeTrue();
                if (IsBinary)
                    (await columnEnumerator.Current.Get<int>()).Should().Be(1);
                else
                    (await columnEnumerator.Current.Get<string>()).Should().Be("1");
                (await columnEnumerator.MoveNextAsync()).Should().BeTrue();
                columnEnumerator.Current.IsDBNull.Should().BeFalse();
                (await columnEnumerator.Current.Get<string>()).Should().Be("val1");
                (await columnEnumerator.MoveNextAsync()).Should().BeFalse();

                // Remaining deletes
                for (var deleteCount = 0; deleteCount < 14999; deleteCount++)
                    await NextMessage<FullDeleteMessage>(messages);

                // Commit Transaction
                await AssertTransactionCommit(messages);

                streamingCts.Cancel();
                await AssertReplicationCancellation(messages);
                await rc.DropReplicationSlot(slotName, cancellationToken: TestContext.Current.CancellationToken);
            });

    // Tests whether TRUNCATE commands get replicated as Logical Replication Protocol Messages on PostgreSQL 11 and above
    [Theory]
    [InlineData(TruncateOptions.None)]
    [InlineData(TruncateOptions.Cascade)]
    [InlineData(TruncateOptions.RestartIdentity)]
    [InlineData(TruncateOptions.Cascade | TruncateOptions.RestartIdentity)]
    public Task truncate(TruncateOptions truncateOptionFlags)
        => SafeReplicationTest(
            async (slotName, tableName, publicationName) =>
            {
                await using var c = await OpenConnectionAsync();
                TestUtil.MinimumPgVersion(c, "11.0", "Replication of TRUNCATE commands was introduced in PostgreSQL 11");
                await c.ExecuteNonQueryAsync(@$"CREATE TABLE {tableName} (id INT PRIMARY KEY GENERATED ALWAYS AS IDENTITY, name TEXT NOT NULL);
                                                    INSERT INTO {tableName} (name) VALUES ('val1');
                                                    CREATE PUBLICATION {publicationName} FOR TABLE {tableName};");
                await using var rc = await OpenReplicationConnectionAsync();
                var slot = await rc.CreatePgOutputReplicationSlot(slotName);
                var sb = new StringBuilder("TRUNCATE TABLE ").Append(tableName);
                if (truncateOptionFlags.HasFlag(TruncateOptions.RestartIdentity))
                    sb.Append(" RESTART IDENTITY");
                if (truncateOptionFlags.HasFlag(TruncateOptions.Cascade))
                    sb.Append(" CASCADE");
                sb.Append($"; INSERT INTO {tableName} (name) SELECT 'val' || i::text FROM generate_series(1, 15000) s(i);");

                await using var tran = await c.BeginTransactionAsync();
                await c.ExecuteNonQueryAsync(sb.ToString());
                await tran.CommitAsync();

                using var streamingCts = new CancellationTokenSource();
                var messages = SkipEmptyTransactions(rc.StartReplication(slot, GetOptions(publicationName), streamingCts.Token))
                    .GetAsyncEnumerator();

                // Begin Transaction
                var transactionXid = await AssertTransactionStart(messages);

                // Relation
                var relationMessage = await NextMessage<RelationMessage>(messages);
                relationMessage.TransactionXid.Should().Be(IsStreaming ? transactionXid : null);
                relationMessage.ReplicaIdentity.Should().Be(ReplicaIdentitySetting.Default);
                relationMessage.Namespace.Should().Be("public");
                relationMessage.RelationName.Should().Be(tableName);
                relationMessage.Columns.Count.Should().Be(2);
                relationMessage.Columns[0].ColumnName.Should().Be("id");
                relationMessage.Columns[1].ColumnName.Should().Be("name");

                // Truncate
                var truncateMsg = await NextMessage<TruncateMessage>(messages);
                truncateMsg.TransactionXid.Should().Be(IsStreaming ? transactionXid : null);
                truncateMsg.Options.Should().Be(truncateOptionFlags);
                truncateMsg.Relations.Single().Should().BeSameAs(relationMessage);

                // Remaining inserts
                // Since the inserts run in the same transaction as the truncate, we'll
                // get a RelationMessage after every StreamStartMessage
                for (var insertCount = 0; insertCount < 15000; insertCount++)
                    await NextMessage<InsertMessage>(messages, expectRelationMessage: true);

                // Commit Transaction
                await AssertTransactionCommit(messages);

                streamingCts.Cancel();
                await AssertReplicationCancellation(messages);
                await rc.DropReplicationSlot(slotName, cancellationToken: TestContext.Current.CancellationToken);
            }, nameof(truncate) + truncateOptionFlags.ToString("D"));

    // Tests whether disposing while replicating will get us stuck forever.
    [Fact]
    public Task Dispose_while_replicating()
        => SafeReplicationTest(
            async (slotName, tableName, publicationName) =>
            {
                await using var c = await OpenConnectionAsync();
                await c.ExecuteNonQueryAsync(@$"
CREATE TABLE {tableName} (id INT PRIMARY KEY GENERATED ALWAYS AS IDENTITY, name TEXT NOT NULL);
CREATE PUBLICATION {publicationName} FOR TABLE {tableName};
");
                await using var rc = await OpenReplicationConnectionAsync();
                var slot = await rc.CreatePgOutputReplicationSlot(slotName);
                await c.ExecuteNonQueryAsync($"INSERT INTO {tableName} (name) VALUES ('value 1'), ('value 2');");

                using var streamingCts = new CancellationTokenSource();
                var messages = SkipEmptyTransactions(rc.StartReplication(slot, GetOptions(publicationName), streamingCts.Token))
                    .GetAsyncEnumerator();

                await NextMessage<BeginMessage>(messages);
            }, nameof(Dispose_while_replicating));

    // Tests whether logical decoding messages get replicated as Logical Replication Protocol Messages on PostgreSQL 14 and above
    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public Task logical_decoding_message(bool writeMessages, bool readMessages)
        => SafeReplicationTest(
            async (slotName, tableName, publicationName) =>
            {
                const string prefix = "My test Prefix";
                const string transactionalMessage = "A transactional message";
                const string nonTransactionalMessage = "A non-transactional message";
                await using var c = await OpenConnectionAsync();
                TestUtil.MinimumPgVersion(c, "14.0", "Replication of logical decoding messages was introduced in PostgreSQL 14");
                await c.ExecuteNonQueryAsync(@$"CREATE TABLE {tableName} (id INT PRIMARY KEY, name TEXT NOT NULL);
                                                    CREATE PUBLICATION {publicationName} FOR TABLE {tableName};");
                await using var rc = await OpenReplicationConnectionAsync();
                var slot = await rc.CreatePgOutputReplicationSlot(slotName);

                await using var tran = await c.BeginTransactionAsync();
                await c.ExecuteNonQueryAsync(@$"SELECT pg_logical_emit_message(true, '{prefix}', '{transactionalMessage}');
                                                    INSERT INTO {tableName} SELECT i, 'val' || i::text FROM generate_series(1, 15000) s(i);", tran);
                await tran.CommitAsync();

                await using var tran2 = await c.BeginTransactionAsync();
                await c.ExecuteNonQueryAsync(@$"SELECT pg_logical_emit_message(false, '{prefix}', '{nonTransactionalMessage}');
                                                    INSERT INTO {tableName} SELECT i, 'val' || i::text FROM generate_series(15001, 15010) s(i);
                                                    SELECT pg_logical_emit_message(true, '{prefix}', '{transactionalMessage}');
                                                    INSERT INTO {tableName} SELECT i, 'val' || i::text FROM generate_series(15011, 30000) s(i);
                                                    SELECT pg_logical_emit_message(false, '{prefix}', '{nonTransactionalMessage}');
                                                    ", tran2);
                await tran2.RollbackAsync();
                await c.ExecuteNonQueryAsync(@$"SELECT pg_switch_wal();");

                using var streamingCts = new CancellationTokenSource();
                var messages = SkipEmptyTransactions(rc.StartReplication(slot,
                        GetOptions(publicationName, writeMessages), streamingCts.Token))
                    .GetAsyncEnumerator();

                // Begin Transaction 1
                var transactionXid = await AssertTransactionStart(messages);

                // LogicalDecodingMessage
                if (writeMessages)
                {
                    var msg = await NextMessage<LogicalDecodingMessage>(messages);
                    msg.TransactionXid.Should().Be(IsStreaming ? transactionXid : null);
                    msg.Flags.Should().Be(1);
                    msg.Prefix.Should().Be(prefix);
                    msg.Data.Length.Should().Be(transactionalMessage.Length);
                    if (readMessages)
                    {
                        var buffer = new MemoryStream();
                        await msg.Data.CopyToAsync(buffer, TestContext.Current.CancellationToken);
                        rc.Encoding.GetString(buffer.ToArray()).Should().Be(transactionalMessage);
                    }
                }

                // Relation
                await NextMessage<RelationMessage>(messages);

                // Inserts
                for (var insertCount = 0; insertCount < 15000; insertCount++)
                    await NextMessage<InsertMessage>(messages);

                // Commit Transaction 1
                await AssertTransactionCommit(messages);

                // LogicalDecodingMessage 1 (non-transactional)
                if (writeMessages)
                {
                    var msg = await NextMessage<LogicalDecodingMessage>(messages);
                    msg.TransactionXid.Should().BeNull();
                    msg.Flags.Should().Be(0);
                    msg.Prefix.Should().Be(prefix);
                    msg.Data.Length.Should().Be(nonTransactionalMessage.Length);
                    if (readMessages)
                    {
                        var buffer = new MemoryStream();
                        await msg.Data.CopyToAsync(buffer, TestContext.Current.CancellationToken);
                        rc.Encoding.GetString(buffer.ToArray()).Should().Be(nonTransactionalMessage);
                    }
                }

                // PostgreSQL 18 skips logical decoding of already-aborted transactions
                if (c.PostgreSqlVersion.IsGreaterOrEqual(18))
                {
                    // LogicalDecodingMessage 2 (non-transactional)
                    if (writeMessages)
                    {
                        var msg = await NextMessage<LogicalDecodingMessage>(messages);
                        msg.TransactionXid.Should().BeNull();
                        msg.Flags.Should().Be(0);
                        msg.Prefix.Should().Be(prefix);
                        msg.Data.Length.Should().Be(nonTransactionalMessage.Length);
                        if (readMessages)
                        {
                            var buffer = new MemoryStream();
                            await msg.Data.CopyToAsync(buffer, TestContext.Current.CancellationToken);
                            rc.Encoding.GetString(buffer.ToArray()).Should().Be(nonTransactionalMessage);
                        }
                    }
                }
                else
                {
                    if (IsStreaming)
                    {
                        // Begin Transaction 2
                        transactionXid = await AssertTransactionStart(messages);

                        // Relation
                        await NextMessage<RelationMessage>(messages);

                        // Inserts
                        for (var insertCount = 0; insertCount < 10; insertCount++)
                            await NextMessage<InsertMessage>(messages);

                        // LogicalDecodingMessage 2 (transactional)
                        if (writeMessages)
                        {
                            var msg = await NextMessage<LogicalDecodingMessage>(messages);
                            msg.TransactionXid.Should().Be(IsStreaming ? transactionXid : null);
                            msg.Flags.Should().Be(1);
                            msg.Prefix.Should().Be(prefix);
                            msg.Data.Length.Should().Be(transactionalMessage.Length);
                            if (readMessages)
                            {
                                var buffer = new MemoryStream();
                                await msg.Data.CopyToAsync(buffer, TestContext.Current.CancellationToken);
                                rc.Encoding.GetString(buffer.ToArray()).Should().Be(transactionalMessage);
                            }
                        }

                        // Further inserts
                        // We don't try to predict how many insert messages we get here
                        // since the streaming transaction will most likely abort before
                        // we reach the expected number
                        while (await messages.MoveNextAsync() && messages.Current is InsertMessage
                               || messages.Current is StreamStopMessage
                               && await messages.MoveNextAsync()
                               && messages.Current is StreamStartMessage
                               && await messages.MoveNextAsync()
                               && messages.Current is InsertMessage)
                        {
                            // Ignore
                        }
                    }
                    else if (writeMessages)
                        await messages.MoveNextAsync();

                    // LogicalDecodingMessage 3 (non-transactional)
                    if (writeMessages)
                    {
                        var msg = (LogicalDecodingMessage)messages.Current;
                        msg.TransactionXid.Should().BeNull();
                        msg.Flags.Should().Be(0);
                        msg.Prefix.Should().Be(prefix);
                        msg.Data.Length.Should().Be(nonTransactionalMessage.Length);
                        if (readMessages)
                        {
                            var buffer = new MemoryStream();
                            await msg.Data.CopyToAsync(buffer, TestContext.Current.CancellationToken);
                            rc.Encoding.GetString(buffer.ToArray()).Should().Be(nonTransactionalMessage);
                        }

                        if (IsStreaming)
                            await messages.MoveNextAsync();
                    }

                    // Rollback Transaction 2
                    if (IsStreaming)
                    {
                        messages.Current.Should().BeOfType(
                            _streamingMode == PgOutputStreamingMode.On
                                ? typeof(StreamAbortMessage)
                                : typeof(ParallelStreamAbortMessage));
                    }
                }

                streamingCts.Cancel();
                await AssertReplicationCancellation(messages);
                await rc.DropReplicationSlot(slotName, cancellationToken: TestContext.Current.CancellationToken);
            }, $"{GetObjectName(nameof(logical_decoding_message))}_m_{BoolToChar(writeMessages)}");

    [Fact]
    public Task stream()
    {
        // We don't test transaction streaming here because there's nothing special in that case
        if (IsStreaming)
            return Task.CompletedTask;

        return SafePgOutputReplicationTest(
            async (slotName, tableName, publicationName) =>
            {
                await using var c = await OpenConnectionAsync();
                await c.ExecuteNonQueryAsync(@$"CREATE TABLE {tableName} (bytes bytea);
                                                    CREATE PUBLICATION {publicationName} FOR TABLE {tableName};");
                var rc = await OpenReplicationConnectionAsync();
                var slot = await rc.CreatePgOutputReplicationSlot(slotName);

                var bytes = new byte[16384];
                for (var i = 0; i < 10; i++)
                    bytes[i] = (byte)i;

                using (var command = new PgSqlCommand($"INSERT INTO {tableName} VALUES ($1)", c))
                {
                    command.Parameters.Add(new() { Value = bytes });
                    await command.ExecuteNonQueryAsync();
                }

                using var streamingCts = new CancellationTokenSource();
                var messages = SkipEmptyTransactions(rc.StartReplication(slot, GetOptions(publicationName), streamingCts.Token))
                    .GetAsyncEnumerator();

                await AssertTransactionStart(messages);
                await NextMessage<RelationMessage>(messages);
                var insertMsg = await NextMessage<InsertMessage>(messages);
                var columnEnumerator = insertMsg.NewRow.GetAsyncEnumerator();
                await columnEnumerator.MoveNextAsync();

                var stream = columnEnumerator.Current.GetStream();
                FluentActions.Invoking(() => columnEnumerator.Current.GetStream()).Should().ThrowExactly<InvalidOperationException>();
                await FluentActions.Awaiting(async () => await columnEnumerator.Current.Get()).Should().ThrowExactlyAsync<InvalidOperationException>();
                await FluentActions.Awaiting(async () => await columnEnumerator.Current.Get<byte[]>()).Should().ThrowExactlyAsync<InvalidOperationException>();

                if (IsBinary)
                {
                    var someBytes = new byte[10];
                    (await stream.ReadAsync(someBytes, 0, 10)).Should().Be(10);
                    someBytes.Should().BeEquivalentTo(bytes[..10]);
                }
                else
                {
                    // We assume bytea hex format here
                    var hexString = "\\x" + BitConverter.ToString(bytes[..10]).Replace("-", string.Empty);
                    var expected = Encoding.ASCII.GetBytes(hexString);
                    var someBytes = new byte[expected.Length];
                    (await stream.ReadAsync(someBytes, 0, someBytes.Length)).Should().Be(someBytes.Length);
                    someBytes.Should().BeEquivalentTo(expected);
                }

                await AssertTransactionCommit(messages);

                streamingCts.Cancel();
                await AssertReplicationCancellation(messages);
                await rc.DropReplicationSlot(slotName, cancellationToken: TestContext.Current.CancellationToken);
            });
    }

    [Fact]
    public Task text_reader()
    {
        // We don't test transaction streaming here because there's nothing special in that case
        if (IsStreaming)
            return Task.CompletedTask;

        return SafePgOutputReplicationTest(
            async (slotName, tableName, publicationName) =>
            {
                await using var c = await OpenConnectionAsync();
                await c.ExecuteNonQueryAsync(@$"CREATE TABLE {tableName} (id INT PRIMARY KEY, name TEXT NULL);
                                                    CREATE PUBLICATION {publicationName} FOR TABLE {tableName};");
                var rc = await OpenReplicationConnectionAsync();
                var slot = await rc.CreatePgOutputReplicationSlot(slotName);

                var expectedText = "val1";
                await c.ExecuteNonQueryAsync($"INSERT INTO {tableName} VALUES (1, '{expectedText}')");

                using var streamingCts = new CancellationTokenSource();
                var messages = SkipEmptyTransactions(rc.StartReplication(slot, GetOptions(publicationName), streamingCts.Token))
                    .GetAsyncEnumerator();

                await AssertTransactionStart(messages);
                await NextMessage<RelationMessage>(messages);
                var insertMsg = await NextMessage<InsertMessage>(messages);
                var columnEnumerator = insertMsg.NewRow.GetAsyncEnumerator();
                await columnEnumerator.MoveNextAsync(); // We are not interested in the id field
                await columnEnumerator.MoveNextAsync();
                using var reader = columnEnumerator.Current.GetTextReader();
                (await reader.ReadToEndAsync()).Should().Be(expectedText);

                await AssertTransactionCommit(messages);

                streamingCts.Cancel();
                await AssertReplicationCancellation(messages);
                await rc.DropReplicationSlot(slotName, cancellationToken: TestContext.Current.CancellationToken);
            });
    }

    [Fact]
    public Task value_metadata()
    {
        // We don't test transaction streaming here because there's nothing special in that case
        if (IsStreaming)
            return Task.CompletedTask;

        return SafePgOutputReplicationTest(
            async (slotName, tableName, publicationName) =>
            {
                await using var c = await OpenConnectionAsync();
                await c.ExecuteNonQueryAsync(@$"CREATE TABLE {tableName} (id INT PRIMARY KEY, name TEXT NULL);
                                                    CREATE PUBLICATION {publicationName} FOR TABLE {tableName};");
                var rc = await OpenReplicationConnectionAsync();
                var slot = await rc.CreatePgOutputReplicationSlot(slotName);

                await c.ExecuteNonQueryAsync($"INSERT INTO {tableName} VALUES (1, 'val1')");

                using var streamingCts = new CancellationTokenSource();
                var messages = SkipEmptyTransactions(rc.StartReplication(slot, GetOptions(publicationName), streamingCts.Token))
                    .GetAsyncEnumerator();

                await AssertTransactionStart(messages);
                await NextMessage<RelationMessage>(messages);
                var insertMsg = await NextMessage<InsertMessage>(messages);
                var columnEnumerator = insertMsg.NewRow.GetAsyncEnumerator();
                await columnEnumerator.MoveNextAsync();

                columnEnumerator.Current.GetFieldType().Should().BeSameAs(IsBinary ? typeof(int) : typeof(string));
                columnEnumerator.Current.GetPostgresType().Name.Should().Be("integer");
                columnEnumerator.Current.GetDataTypeName().Should().Be("integer");
                columnEnumerator.Current.IsUnchangedToastedValue.Should().BeFalse();

                await AssertTransactionCommit(messages);

                streamingCts.Cancel();
                await AssertReplicationCancellation(messages);
                await rc.DropReplicationSlot(slotName, cancellationToken: TestContext.Current.CancellationToken);
            });
    }

    [Fact]
    public Task @null()
    {
        // We don't test transaction streaming here because there's nothing special in that case
        if (IsStreaming)
            return Task.CompletedTask;

        return SafePgOutputReplicationTest(
            async (slotName, tableName, publicationName) =>
            {
                await using var c = await OpenConnectionAsync();
                await c.ExecuteNonQueryAsync(@$"CREATE TABLE {tableName} (int1 INT, int2 INT);
                                                    CREATE PUBLICATION {publicationName} FOR TABLE {tableName};");
                var rc = await OpenReplicationConnectionAsync();
                var slot = await rc.CreatePgOutputReplicationSlot(slotName);

                await c.ExecuteNonQueryAsync($"INSERT INTO {tableName} VALUES (1, 1), (NULL, NULL)");

                using var streamingCts = new CancellationTokenSource();
                var messages = SkipEmptyTransactions(rc.StartReplication(slot, GetOptions(publicationName), streamingCts.Token))
                    .GetAsyncEnumerator();

                await AssertTransactionStart(messages);
                await NextMessage<RelationMessage>(messages);

                // non-null
                var columnEnumerator = (await NextMessage<InsertMessage>(messages)).NewRow.GetAsyncEnumerator();
                await columnEnumerator.MoveNextAsync();
                columnEnumerator.Current.IsDBNull.Should().BeFalse();
                columnEnumerator.Current.IsUnchangedToastedValue.Should().BeFalse();
                if (IsBinary)
                    (await columnEnumerator.Current.Get<int>()).Should().Be(1);
                else
                    (await columnEnumerator.Current.Get<string>()).Should().Be("1");
                await columnEnumerator.MoveNextAsync();
                (await columnEnumerator.Current.Get()).Should().Be(IsBinary ? 1 : "1");

                // null
                columnEnumerator = (await NextMessage<InsertMessage>(messages)).NewRow.GetAsyncEnumerator();
                await columnEnumerator.MoveNextAsync();
                columnEnumerator.Current.IsDBNull.Should().BeTrue();
                columnEnumerator.Current.IsUnchangedToastedValue.Should().BeFalse();
                if (IsBinary)
                    await FluentActions.Awaiting(async () => await columnEnumerator.Current.Get<int>()).Should().ThrowExactlyAsync<InvalidCastException>();
                else
                    await FluentActions.Awaiting(async () => await columnEnumerator.Current.Get<string>()).Should().ThrowExactlyAsync<InvalidCastException>();
                await columnEnumerator.MoveNextAsync();
                (await columnEnumerator.Current.Get()).Should().BeSameAs(DBNull.Value);

                await AssertTransactionCommit(messages);

                streamingCts.Cancel();
                await AssertReplicationCancellation(messages);
                await rc.DropReplicationSlot(slotName, cancellationToken: TestContext.Current.CancellationToken);
            });
    }

    [CodeBrix.PostgresClient.PgSqlTypes.PgName("descriptor")]
    public class Descriptor
    {
        [CodeBrix.PostgresClient.PgSqlTypes.PgName("id")]
        public long Id { get; set; }

        [CodeBrix.PostgresClient.PgSqlTypes.PgName("name")]
        public string Name { get; set; } = string.Empty;
    }

    [Fact]
    public Task composite_type()
    {
        // We don't test transaction streaming here because there's nothing special in that case
        if (IsStreaming)
            return Task.CompletedTask;

        return SafePgOutputReplicationTest(
            async (slotName, tableName, publicationName) =>
            {
                await using var adminConnection = await OpenConnectionAsync();
                await adminConnection.ExecuteNonQueryAsync(@$"
DROP TYPE IF EXISTS descriptor CASCADE;
CREATE TYPE descriptor AS (id bigint, name text);
CREATE TABLE {tableName} (descriptor_field descriptor);
CREATE PUBLICATION {publicationName} FOR TABLE {tableName};");

                PgSqlConnection.GlobalTypeMapper.MapComposite<Descriptor>("descriptor");

                try
                {

                    // Use a one-time connection string to make sure we get a new data source without cached mappings.
                    // In regular tests we'd use a data source, but replication doesn't work with data sources (yet).
                    // In addition, clear the DatabaseInfo cache.
                    using var _ = CreateTempPool(ConnectionString, out var connString);
                    var rc = await OpenReplicationConnectionAsync(connString);
                    var slot = await rc.CreatePgOutputReplicationSlot(slotName);
                    var expected = new Descriptor { Id = 1248, Name = "My Descriptor" };
                    var stringValue = $"({expected.Id},\"{expected.Name}\")";

                    await using var c = await OpenConnectionAsync();
                    await c.ExecuteNonQueryAsync($"INSERT INTO {tableName} VALUES ('{stringValue}')");

                    using var streamingCts = new CancellationTokenSource();
                    var messages = SkipEmptyTransactions(rc.StartReplication(slot, GetOptions(publicationName), streamingCts.Token))
                        .GetAsyncEnumerator();

                    await AssertTransactionStart(messages);
                    await NextMessage<TypeMessage>(messages);
                    await NextMessage<RelationMessage>(messages);

                    // non-null
                    var columnEnumerator = (await NextMessage<InsertMessage>(messages)).NewRow.GetAsyncEnumerator();
                    await columnEnumerator.MoveNextAsync();
                    columnEnumerator.Current.IsDBNull.Should().BeFalse();
                    columnEnumerator.Current.IsUnchangedToastedValue.Should().BeFalse();
                    if (IsBinary)
                    {
                        var result = await columnEnumerator.Current.Get<Descriptor>();
                        result.Id.Should().Be(expected.Id);
                        result.Name.Should().Be(expected.Name);
                    }
                    else
                        (await columnEnumerator.Current.Get()).Should().Be(stringValue);

                    await columnEnumerator.MoveNextAsync();

                    await AssertTransactionCommit(messages);

                    streamingCts.Cancel();
                    await AssertReplicationCancellation(messages);
                    await rc.DropReplicationSlot(slotName, cancellationToken: TestContext.Current.CancellationToken);
                }
                finally
                {
                    await adminConnection.ExecuteNonQueryAsync("DROP TYPE IF EXISTS descriptor CASCADE;");

                    PgSqlConnection.GlobalTypeMapper.Reset();
                }
            });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task two_phase(bool commit)
    {
        // Streaming of prepared transaction is only supported for
        // logical streaming replication protocol >= 3
        if (protocolVersion < PgOutputProtocolVersion.V3)
            return Task.CompletedTask;

        return SafePgOutputReplicationTest(
            async (slotName, tableName, publicationName) =>
            {
                var gid = Guid.NewGuid().ToString();
                await using var c = await OpenConnectionAsync();
                await c.ExecuteNonQueryAsync(@$"CREATE TABLE {tableName} (a int primary key, b varchar);
                                                CREATE PUBLICATION {publicationName} FOR TABLE {tableName};");
                await using var rc = await OpenReplicationConnectionAsync();
                var slot = await rc.CreatePgOutputReplicationSlot(slotName, twoPhase: true);

                await using var tran = await c.BeginTransactionAsync();
                await c.ExecuteNonQueryAsync(@$"INSERT INTO {tableName} SELECT i, 'val' || i::text FROM generate_series(1, 15000) s(i);
	                                            PREPARE TRANSACTION '{gid}';");
                try
                {
                    using var streamingCts = new CancellationTokenSource();
                    var messages = SkipEmptyTransactions(rc.StartReplication(slot, GetOptions(publicationName), streamingCts.Token))
                        .GetAsyncEnumerator();

                    // Begin Transaction
                    var transactionXid = await AssertTransactionStart(messages);

                    // Relation
                    await NextMessage<RelationMessage>(messages);

                    // Remaining inserts
                    for (var insertCount = 0; insertCount < 15000; insertCount++)
                    {
                        await NextMessage<InsertMessage>(messages);
                    }

                    var prepareMessageBase = await AssertPrepare(messages);
                    prepareMessageBase.TransactionXid.Should().Be(transactionXid);
                    prepareMessageBase.TransactionGid.Should().Be(gid);

                    if (commit)
                    {
                        await c.ExecuteNonQueryAsync(@$"COMMIT PREPARED '{gid}';");

                        var commitPreparedMessage = await NextMessage<CommitPreparedMessage>(messages);
                        commitPreparedMessage.TransactionXid.Should().Be(transactionXid);
                        commitPreparedMessage.TransactionGid.Should().Be(gid);
                    }
                    else
                    {
                        await c.ExecuteNonQueryAsync(@$"ROLLBACK PREPARED '{gid}';");

                        var rollbackPreparedMessage = await NextMessage<RollbackPreparedMessage>(messages);
                        rollbackPreparedMessage.TransactionXid.Should().Be(transactionXid);
                        rollbackPreparedMessage.TransactionGid.Should().Be(gid);
                    }

                    streamingCts.Cancel();
                    await AssertReplicationCancellation(messages);
                    await rc.DropReplicationSlot(slotName, cancellationToken: TestContext.Current.CancellationToken);
                }
                finally
                {
                    try
                    {
                        await using var cx = await OpenConnectionAsync();
                        await cx.ExecuteNonQueryAsync(@$"ROLLBACK PREPARED '{gid}';");
                    }
                    catch
                    {
                        // Give up
                    }
                }
            }, $"{GetObjectName(nameof(two_phase))}_{(commit ? "commit" : "rollback")}");
    }

    // Tests whether columns of internally cached RelationMessage instances are accidentally overwritten.
    [Fact]
    [IssueLink("https://github.com/npgsql/npgsql/issues/4633")]
    public Task bug_4633()
    {
        // We don't need all the various test cases here since the bug gets triggered in any case
        if (IsStreaming || IsBinary || Version > PgOutputProtocolVersion.V1)
            return Task.CompletedTask;

        return SafePgOutputReplicationTest(
            async (slotName, tableNames, publicationName) =>
            {
                await using var c = await OpenConnectionAsync();
                await c.ExecuteNonQueryAsync(@$"
CREATE TABLE {tableNames[0]}
(
    id uuid NOT NULL,
    text text NOT NULL,
    created_at timestamp with time zone NOT NULL,
    CONSTRAINT pk_{tableNames[0]} PRIMARY KEY (id)
);
CREATE TABLE {tableNames[1]}
(
    id uuid NOT NULL,
    message_id uuid NOT NULL,
    created_at timestamp with time zone NOT NULL,
    CONSTRAINT pk_{tableNames[1]} PRIMARY KEY (id),
    CONSTRAINT fk_{tableNames[1]}_message_id FOREIGN KEY (message_id) REFERENCES {tableNames[0]} (id)
);
CREATE PUBLICATION {publicationName} FOR TABLE {tableNames[0]}, {tableNames[1]} WITH (PUBLISH = 'insert');");
                await using var rc = await OpenReplicationConnectionAsync();
                var slot = await rc.CreatePgOutputReplicationSlot(slotName);

                await using var tran = await c.BeginTransactionAsync();
                await c.ExecuteNonQueryAsync(@$"
INSERT INTO {tableNames[0]} VALUES ('B6CB5293-F65E-4F48-A74B-06D5355DAA74', 'random', now());
INSERT INTO {tableNames[1]} VALUES ('55870BEC-C42E-4AB0-83BA-225BB7777B37', 'B6CB5293-F65E-4F48-A74B-06D5355DAA74', now());
INSERT INTO {tableNames[0]} VALUES ('5F89F5FE-6F4F-465F-BB87-716B1413F88D', 'another random', now());");
                await tran.CommitAsync();

                using var streamingCts = new CancellationTokenSource();
                var messages = SkipEmptyTransactions(rc.StartReplication(slot, GetOptions(publicationName), streamingCts.Token))
                    .GetAsyncEnumerator();

                // Begin Transaction
                var transactionXid = await AssertTransactionStart(messages);

                // First Relation
                var relationMsg = await NextMessage<RelationMessage>(messages);
                var relation1Name = relationMsg.RelationName;
                var relation1Id = relationMsg.RelationId;
                relation1Name.Should().Be(tableNames[0]);
                relationMsg.Columns.Count.Should().Be(3);
                relationMsg.Columns[0].ColumnName.Should().Be("id");
                relationMsg.Columns[1].ColumnName.Should().Be("text");
                relationMsg.Columns[2].ColumnName.Should().Be("created_at");

                // Insert first value
                var insertMsg = await NextMessage<InsertMessage>(messages);
                insertMsg.Relation.RelationName.Should().Be(relation1Name);
                insertMsg.Relation.RelationId.Should().Be(relation1Id);
                insertMsg.Relation.Columns.Count.Should().Be(3);
                insertMsg.Relation.Columns[0].ColumnName.Should().Be("id");
                insertMsg.Relation.Columns[1].ColumnName.Should().Be("text");
                insertMsg.Relation.Columns[2].ColumnName.Should().Be("created_at");

                // Second Relation
                relationMsg = await NextMessage<RelationMessage>(messages);
                var relation2Name = relationMsg.RelationName;
                var relation2Id = relationMsg.RelationId;
                relation2Name.Should().Be(tableNames[1]);
                relationMsg.Columns.Count.Should().Be(3);
                relationMsg.Columns[0].ColumnName.Should().Be("id");
                relationMsg.Columns[1].ColumnName.Should().Be("message_id");
                relationMsg.Columns[2].ColumnName.Should().Be("created_at");

                // Insert second value
                insertMsg = await NextMessage<InsertMessage>(messages);
                insertMsg.Relation.RelationName.Should().Be(relation2Name);
                insertMsg.Relation.RelationId.Should().Be(relation2Id);
                insertMsg.Relation.Columns.Count.Should().Be(3);
                insertMsg.Relation.Columns[0].ColumnName.Should().Be("id");
                insertMsg.Relation.Columns[1].ColumnName.Should().Be("message_id");
                insertMsg.Relation.Columns[2].ColumnName.Should().Be("created_at");

                // Insert third value
                insertMsg = await NextMessage<InsertMessage>(messages);
                insertMsg.Relation.RelationName.Should().Be(relation1Name);
                insertMsg.Relation.RelationId.Should().Be(relation1Id);
                insertMsg.Relation.Columns.Count.Should().Be(3);
                insertMsg.Relation.Columns[0].ColumnName.Should().Be("id");
                insertMsg.Relation.Columns[1].ColumnName.Should().Be("text");
                insertMsg.Relation.Columns[2].ColumnName.Should().Be("created_at");

                // Commit Transaction
                await AssertTransactionCommit(messages);

                streamingCts.Cancel();
                await AssertReplicationCancellation(messages);
                await rc.DropReplicationSlot(slotName, cancellationToken: TestContext.Current.CancellationToken);
            }, 2);
    }

    // Tests whether FullUpdateMessage instances with unchanged toasted values behave as expected.
    // Explicit: massive inserts
    [Fact(Explicit = true)]
    public  Task update_for_full_replica_identity_with_unchanged_toasted_value()
        => SafeReplicationTest(
            async (slotName, tableName, publicationName) =>
            {
                await using var c = await OpenConnectionAsync();
                await c.ExecuteNonQueryAsync($$"""
                                               CREATE TABLE {{tableName}} (id INT PRIMARY KEY, name JSONB NOT NULL, something_else INT NULL);
                                               ALTER TABLE {{tableName}} REPLICA IDENTITY FULL;
                                               INSERT INTO {{tableName}} SELECT i, ('{"row_' || i::text || '": [{{string.Join(", ", Enumerable.Range(1, 1024))}}]}')::jsonb, NULL FROM generate_series(1, 15000) s(i);
                                               CREATE PUBLICATION {{publicationName}} FOR TABLE {{tableName}};
                                               """);
                await using var rc = await OpenReplicationConnectionAsync();
                var slot = await rc.CreatePgOutputReplicationSlot(slotName);

                await using var tran = await c.BeginTransactionAsync();
                await c.ExecuteNonQueryAsync($"""
                                              UPDATE {tableName} SET name='"val1_updated"' WHERE id = 1;
                                              UPDATE {tableName} SET something_else = id WHERE id > 1
                                              """);
                await tran.CommitAsync();

                using var streamingCts = new CancellationTokenSource();
                var messages = SkipEmptyTransactions(rc.StartReplication(slot, GetOptions(publicationName), streamingCts.Token))
                    .GetAsyncEnumerator();

                // Begin Transaction
                var transactionXid = await AssertTransactionStart(messages);

                // Relation
                var relationMsg = await NextMessage<RelationMessage>(messages);

                // Update of the first row (updating the jsonb column)
                var updateMsg = await NextMessage<FullUpdateMessage>(messages);
                updateMsg.TransactionXid.Should().Be(IsStreaming ? transactionXid : null);
                updateMsg.Relation.Should().BeSameAs(relationMsg);

                var newRowColumnEnumerator = updateMsg.NewRow.GetAsyncEnumerator();
                (await newRowColumnEnumerator.MoveNextAsync()).Should().BeTrue();
                (await newRowColumnEnumerator.MoveNextAsync()).Should().BeTrue();
                newRowColumnEnumerator.Current.IsUnchangedToastedValue.Should().BeFalse();
                (await newRowColumnEnumerator.Current.Get<object>()).Should().Be("\"val1_updated\"");
                (await newRowColumnEnumerator.MoveNextAsync()).Should().BeTrue();
                newRowColumnEnumerator.Current.IsDBNull.Should().BeTrue();
                (await newRowColumnEnumerator.MoveNextAsync()).Should().BeFalse();

                // Update of the following rows (not updating the jsonb column)
                updateMsg = await NextMessage<FullUpdateMessage>(messages);
                updateMsg.TransactionXid.Should().Be(IsStreaming ? transactionXid : null);
                updateMsg.Relation.Should().BeSameAs(relationMsg);

                newRowColumnEnumerator = updateMsg.NewRow.GetAsyncEnumerator();
                (await newRowColumnEnumerator.MoveNextAsync()).Should().BeTrue();
                (await newRowColumnEnumerator.MoveNextAsync()).Should().BeTrue();
                newRowColumnEnumerator.Current.IsUnchangedToastedValue.Should().BeTrue();
                await FluentActions.Awaiting(async () => await newRowColumnEnumerator.Current.Get<object>())
                    .Should().ThrowExactlyAsync<InvalidCastException>()
                    .Where(e => e.Message == "Column 'name' is an unchanged TOASTed value (actual value not sent).");
                (await newRowColumnEnumerator.MoveNextAsync()).Should().BeTrue();
                newRowColumnEnumerator.Current.IsDBNull.Should().BeFalse();
                (await newRowColumnEnumerator.MoveNextAsync()).Should().BeFalse();

                // Remaining updates
                for (var updateCount = 0; updateCount < 14998; updateCount++)
                    await NextMessage<FullUpdateMessage>(messages);

                // Commit Transaction
                await AssertTransactionCommit(messages);

                streamingCts.Cancel();
                (await FluentActions.Awaiting(async () => await messages.MoveNextAsync())
                    .Should().ThrowAsync<OperationCanceledException>())
                    .WithInnerException<PostgresException>()
                    .Where(e => e.SqlState == PostgresErrorCodes.QueryCanceled);
                await rc.DropReplicationSlot(slotName, cancellationToken: TestContext.Current.CancellationToken);
            });

    #region Non-Test stuff (helper methods, initialization, enums, ...)

    async Task<uint?> AssertTransactionStart(IAsyncEnumerator<PgOutputReplicationMessage> messages)
    {
        (await messages.MoveNextAsync()).Should().BeTrue();

        switch (messages.Current)
        {
        case StreamStartMessage streamStartMessage:
            IsStreaming.Should().BeTrue();
            return streamStartMessage.TransactionXid;
        case BeginMessage beginMessage:
            (!IsStreaming).Should().BeTrue();
            return beginMessage.TransactionXid;
        case BeginPrepareMessage beginPrepareMessage:
            (!IsStreaming).Should().BeTrue();
            return beginPrepareMessage.TransactionXid;
        default:
            Assert.Fail("Expected transaction start message but got: " + messages.Current);
            throw new Exception();
        }
    }

    async Task AssertTransactionCommit(IAsyncEnumerator<PgOutputReplicationMessage> messages)
    {
        (await messages.MoveNextAsync()).Should().BeTrue();

        switch (messages.Current)
        {
        case StreamStopMessage:
            IsStreaming.Should().BeTrue();
            (await messages.MoveNextAsync()).Should().BeTrue();
            messages.Current.Should().BeOfType<StreamCommitMessage>();
            return;
        case CommitMessage:
            return;
        default:
            Assert.Fail("Expected transaction end message but got: " + messages.Current);
            throw new Exception();
        }
    }

    async Task<PrepareMessageBase> AssertPrepare(IAsyncEnumerator<PgOutputReplicationMessage> enumerator)
    {
        (await enumerator.MoveNextAsync()).Should().BeTrue();
        if (IsStreaming && enumerator.Current is StreamStopMessage)
        {
            (await enumerator.MoveNextAsync()).Should().BeTrue();
            enumerator.Current.Should().BeOfType<StreamPrepareMessage>();
            return (PrepareMessageBase)enumerator.Current;
        }

        enumerator.Current.Should().BeOfType<PrepareMessage>();
        return (PrepareMessageBase)enumerator.Current;
    }

    async ValueTask<TExpected> NextMessage<TExpected>(IAsyncEnumerator<PgOutputReplicationMessage> enumerator, bool expectRelationMessage = false)
        where TExpected : PgOutputReplicationMessage
    {
        (await enumerator.MoveNextAsync()).Should().BeTrue();
        if (IsStreaming && enumerator.Current is StreamStopMessage)
        {
            (await enumerator.MoveNextAsync()).Should().BeTrue();
            enumerator.Current.Should().BeOfType<StreamStartMessage>();
            (await enumerator.MoveNextAsync()).Should().BeTrue();
            if (expectRelationMessage)
            {
                enumerator.Current.Should().BeOfType<RelationMessage>();
                (await enumerator.MoveNextAsync()).Should().BeTrue();
            }
        }

        enumerator.Current.Should().BeOfType<TExpected>();
        return (TExpected)enumerator.Current;
    }

    /// <summary>
    /// Unfortunately, empty transactions may get randomly created by PG because of auto-vacuuming; these cause test failures as we
    /// assert for specific expected message types. This filters them out.
    /// </summary>
    async IAsyncEnumerable<PgOutputReplicationMessage> SkipEmptyTransactions(IAsyncEnumerable<PgOutputReplicationMessage> messages)
    {
        var enumerator = messages.GetAsyncEnumerator();
        while (await enumerator.MoveNextAsync())
        {
            if (enumerator.Current is BeginMessage)
            {
                var current = enumerator.Current;
                if (!await enumerator.MoveNextAsync())
                {
                    yield return current;
                    yield break;
                }

                var next = enumerator.Current;
                if (next is CommitMessage)
                    continue;

                yield return current;
                yield return next;
                continue;
            }

            yield return enumerator.Current;
        }
    }

    PgOutputReplicationOptions GetOptions(string publicationName, bool? messages = null)
        => new(publicationName, protocolVersion, _binary, _streamingMode, messages);

    Task SafePgOutputReplicationTest(Func<string, string, string, Task> testAction, [CallerMemberName] string memberName = "")
        => SafeReplicationTest(testAction, GetObjectName(memberName));

    Task SafePgOutputReplicationTest(Func<string, string[], string, Task> testAction, int tableCount, [CallerMemberName] string memberName = "")
        => SafeReplicationTest(testAction, tableCount, GetObjectName(memberName));

    string GetObjectName(string memberName)
    {
        var sb = new StringBuilder(memberName)
            .Append("_v").Append(protocolVersion);
        if (_binary.HasValue)
            sb.Append("_b_").Append(BoolToChar(_binary.Value));
        if (_streamingMode.HasValue)
            sb.Append("_s_").Append(_streamingMode.Value);
        return sb.ToString();
    }

    static char BoolToChar(bool value)
        => value ? 't' : 'f';

    protected override string Postfix => "pgoutput_l";

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await using var c = await OpenConnectionAsync();
        TestUtil.MinimumPgVersion(c, "10.0", "The Logical Replication Protocol (via pgoutput plugin) was introduced in PostgreSQL 10");
        if (protocolVersion > PgOutputProtocolVersion.V3)
            TestUtil.MinimumPgVersion(c, "16.0", "Logical Streaming Replication Protocol version 4 was introduced in PostgreSQL 16");
        if (protocolVersion > PgOutputProtocolVersion.V2)
            TestUtil.MinimumPgVersion(c, "15.0", "Logical Streaming Replication Protocol version 3 was introduced in PostgreSQL 15");
        if (protocolVersion > PgOutputProtocolVersion.V1)
            TestUtil.MinimumPgVersion(c, "14.0", "Logical Streaming Replication Protocol version 2 was introduced in PostgreSQL 14");
        if (IsBinary)
            TestUtil.MinimumPgVersion(c, "14.0", "Sending replication values in binary representation was introduced in PostgreSQL 14");
        if (IsStreaming)
        {
            switch (_streamingMode)
            {
            case PgOutputStreamingMode.On:
                TestUtil.MinimumPgVersion(c, "14.0", "Streaming of in-progress transactions was introduced in PostgreSQL 14");
                break;
            case PgOutputStreamingMode.Parallel:
                TestUtil.MinimumPgVersion(c, "16.0", "Parallel streaming of in-progress transactions was introduced in PostgreSQL 16");
                break;
            }
            var logicalDecodingWorkMem = (string)(await c.ExecuteScalarAsync("SHOW logical_decoding_work_mem"));
            if (logicalDecodingWorkMem != "64kB")
            {
                TestUtil.IgnoreExceptOnBuildServer(
                    $"logical_decoding_work_mem is set to '{logicalDecodingWorkMem}', but must be set to '64kB' in order for the " +
                    "streaming replication tests to work correctly. Skipping replication tests");
            }
        }
    }

    public enum ReplicationDataMode
    {
        DefaultReplicationDataMode,
        TextReplicationDataMode,
        BinaryReplicationDataMode,
    }
    public enum TransactionMode
    {
        DefaultTransactionMode,
        NonStreamingTransactionMode,
        StreamingTransactionMode,
        ParallelStreamingTransactionMode
    }

    #endregion Non-Test stuff (helper methods, initialization, ennums, ...)
}

[Collection(NonParallelCollection.Name)]
public sealed class PgOutputReplicationTests_V1_DefaultReplicationDataMode_DefaultTransactionMode()
    : PgOutputReplicationTests(PgOutputProtocolVersion.V1, PgOutputReplicationTests.ReplicationDataMode.DefaultReplicationDataMode, PgOutputReplicationTests.TransactionMode.DefaultTransactionMode);
[Collection(NonParallelCollection.Name)]
public sealed class PgOutputReplicationTests_V1_BinaryReplicationDataMode_DefaultTransactionMode()
    : PgOutputReplicationTests(PgOutputProtocolVersion.V1, PgOutputReplicationTests.ReplicationDataMode.BinaryReplicationDataMode, PgOutputReplicationTests.TransactionMode.DefaultTransactionMode);
[Collection(NonParallelCollection.Name)]
public sealed class PgOutputReplicationTests_V2_DefaultReplicationDataMode_StreamingTransactionMode()
    : PgOutputReplicationTests(PgOutputProtocolVersion.V2, PgOutputReplicationTests.ReplicationDataMode.DefaultReplicationDataMode, PgOutputReplicationTests.TransactionMode.StreamingTransactionMode);
[Collection(NonParallelCollection.Name)]
public sealed class PgOutputReplicationTests_V3_DefaultReplicationDataMode_DefaultTransactionMode()
    : PgOutputReplicationTests(PgOutputProtocolVersion.V3, PgOutputReplicationTests.ReplicationDataMode.DefaultReplicationDataMode, PgOutputReplicationTests.TransactionMode.DefaultTransactionMode);
[Collection(NonParallelCollection.Name)]
public sealed class PgOutputReplicationTests_V3_DefaultReplicationDataMode_StreamingTransactionMode()
    : PgOutputReplicationTests(PgOutputProtocolVersion.V3, PgOutputReplicationTests.ReplicationDataMode.DefaultReplicationDataMode, PgOutputReplicationTests.TransactionMode.StreamingTransactionMode);
[Collection(NonParallelCollection.Name)]
public sealed class PgOutputReplicationTests_V4_DefaultReplicationDataMode_DefaultTransactionMode()
    : PgOutputReplicationTests(PgOutputProtocolVersion.V4, PgOutputReplicationTests.ReplicationDataMode.DefaultReplicationDataMode, PgOutputReplicationTests.TransactionMode.DefaultTransactionMode);
[Collection(NonParallelCollection.Name)]
public sealed class PgOutputReplicationTests_V4_DefaultReplicationDataMode_ParallelStreamingTransactionMode()
    : PgOutputReplicationTests(PgOutputProtocolVersion.V4, PgOutputReplicationTests.ReplicationDataMode.DefaultReplicationDataMode, PgOutputReplicationTests.TransactionMode.ParallelStreamingTransactionMode);
