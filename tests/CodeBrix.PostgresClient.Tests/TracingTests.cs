using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using CodeBrix.PostgresClient.PgSqlTypes;
using CodeBrix.PostgresClient.Tests.Support;
using SilverAssertions;
using Xunit;
using static CodeBrix.PostgresClient.Tests.TestUtil;

namespace CodeBrix.PostgresClient.Tests; //was previously: Npgsql.Tests;

public abstract class TracingTests(MultiplexingMode multiplexingMode, bool async) : MultiplexingTestBase(multiplexingMode)
{
    #region Physical open

    [Fact]
    public async Task physical_open()
    {
        //Arrange
        using var activityListener = StartListener(out var activities);

        //Act
        await using var dataSource = CreateDataSource();
        await using var connection = async
            ? await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken)
            : dataSource.OpenConnection();

        //Assert
        activities.Should().HaveCount(1);
        ValidateActivity(activities[0], connection, IsMultiplexing);

        if (!IsMultiplexing)
            return;

        activities.Clear();

        // For multiplexing, we clear the pool to force next query to open another physical connection
        dataSource.Clear();

        await connection.ExecuteScalarAsync("SELECT 1", cancellationToken: TestContext.Current.CancellationToken);

        activities.Should().HaveCount(2);
        ValidateActivity(activities[0], connection, IsMultiplexing);

        // For multiplexing, query's activity can be considered as a parent for physical open's activity
        activities[0].Parent.Should().BeSameAs(activities[1]);

        static void ValidateActivity(Activity activity, PgSqlConnection conn, bool isMultiplexing)
        {
            activity.DisplayName.Should().Be("CONNECT " + conn.Settings.Database);
            activity.OperationName.Should().Be("CONNECT " + conn.Settings.Database);
            activity.Status.Should().Be(ActivityStatusCode.Unset);

            activity.Events.Count().Should().Be(0);

            var tags = activity.TagObjects.ToDictionary(t => t.Key, t => t.Value);
            tags.Should().HaveCount(conn.Settings.Port == 5432 ? 5 : 6);

            tags["db.system.name"].Should().Be("postgresql");
            tags["db.namespace"].Should().Be(conn.Settings.Database);

            tags.Should().NotContainKey("db.query.text");

            tags["db.pgsql.data_source"].Should().Be(conn.ConnectionString);

            if (isMultiplexing)
                tags.Should().ContainKey("db.pgsql.connection_id");
            else
                tags["db.pgsql.connection_id"].Should().Be(conn.ProcessID);
        }
    }

    [Fact]
    public async Task physical_open_error()
    {
        //Arrange
        using var activityListener = StartListener(out var activities);

        //Act
        await using var dataSource = CreateDataSource(x => x.Host = "not-existing-host");
        var exception = await Assert.ThrowsAsync<PgSqlException>(async () =>
        {
            await using var connection = async
                ? await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken)
                : dataSource.OpenConnection();
        });

        //Assert
        var activity = GetSingleActivity(activities, "CONNECT " + dataSource.Settings.Database, "CONNECT " + dataSource.Settings.Database, ActivityStatusCode.Error, exception.Message);

        activity.Events.Count().Should().Be(1);
        var exceptionEvent = activity.Events.First();
        exceptionEvent.Name.Should().Be("exception");

        var exceptionTags = exceptionEvent.Tags.ToDictionary(t => t.Key, t => t.Value);
        exceptionTags.Should().HaveCount(3);

        exceptionTags["exception.type"].Should().Be(exception.GetType().FullName);
        ((string)exceptionTags["exception.message"]).Should().Contain(exception.Message);
        ((string)exceptionTags["exception.stacktrace"]).Should().Contain(exception.Message);

        var activityTags = activity.TagObjects.ToDictionary(t => t.Key, t => t.Value);
        activityTags.Should().HaveCount(3);

        activityTags["db.system.name"].Should().Be("postgresql");
        activityTags["db.pgsql.data_source"].Should().Be(dataSource.ConnectionString);

        activityTags["error.type"].Should().Be("System.Net.Sockets.SocketException");
    }

    [Fact]
    public async Task physical_open_disable()
    {
        //Arrange
        using var activityListener = StartListener(out var activities);

        //Act
        var dataSourceBuilder = CreateDataSourceBuilder();
        dataSourceBuilder.ConfigureTracing(options => options.EnablePhysicalOpenTracing(enable: false));
        await using var dataSource = dataSourceBuilder.Build();

        await using var connection = async ? await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken) : dataSource.OpenConnection();

        //Assert
        activities.Should().BeEmpty();
    }

    #endregion Physical open

    #region Command execution

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task command_execute(bool batch)
    {
        //Arrange
        var dataSourceBuilder = CreateDataSourceBuilder();
        dataSourceBuilder.Name = "TestTracingDataSource";
        dataSourceBuilder.ConfigureTracing(o => o.EnablePhysicalOpenTracing(false));
        await using var dataSource = dataSourceBuilder.Build();
        await using var connection = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        using var activityListener = StartListener(out var activities);

        //Act
        await ExecuteScalar(connection, async, batch, "SELECT 42");

        //Assert
        var activity = GetSingleActivity(activities, "postgresql", "postgresql");

        activity.Events.Count().Should().Be(1);
        var firstResponseEvent = activity.Events.First();
        firstResponseEvent.Name.Should().Be("received-first-response");

        var tags = activity.TagObjects.ToDictionary(t => t.Key, t => t.Value);
        tags.Should().HaveCount(connection.Settings.Port == 5432 ? 6 : 7);

        tags["db.query.text"].Should().Be("SELECT 42");
        tags["db.system.name"].Should().Be("postgresql");
        tags["db.namespace"].Should().Be(connection.Settings.Database);

        tags["db.pgsql.data_source"].Should().Be("TestTracingDataSource");

        if (IsMultiplexing)
            tags.Should().ContainKey("db.pgsql.connection_id");
        else
            tags["db.pgsql.connection_id"].Should().Be(connection.ProcessID);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task command_execute_error(bool batch)
    {
        //Arrange
        await using var dataSource = CreateDataSource(ds => ds.ConfigureTracing(o => o.EnablePhysicalOpenTracing(false)));
        await using var connection = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        using var activityListener = StartListener(out var activities);

        //Act
        await Assert.ThrowsAsync<PostgresException>(async () => await ExecuteScalar(connection, async, batch, "SELECT * FROM non_existing_table"));

        //Assert
        var activity = GetSingleActivity(activities, "postgresql", "postgresql", ActivityStatusCode.Error, PostgresErrorCodes.UndefinedTable);

        activity.Events.Count().Should().Be(1);
        var exceptionEvent = activity.Events.First();
        exceptionEvent.Name.Should().Be("exception");

        var exceptionTags = exceptionEvent.Tags.ToDictionary(t => t.Key, t => t.Value);
        exceptionTags.Should().HaveCount(3);

        exceptionTags["exception.type"].Should().Be(typeof(PostgresException).FullName);
        ((string)exceptionTags["exception.message"]).Should().Contain("relation \"non_existing_table\" does not exist");
        ((string)exceptionTags["exception.stacktrace"]).Should().Contain("relation \"non_existing_table\" does not exist");

        var activityTags = activity.TagObjects.ToDictionary(t => t.Key, t => t.Value);
        activityTags.Should().HaveCount(connection.Settings.Port == 5432 ? 8 : 9);

        activityTags["db.query.text"].Should().Be("SELECT * FROM non_existing_table");
        activityTags["db.system.name"].Should().Be("postgresql");
        activityTags["db.namespace"].Should().Be(connection.Settings.Database);

        activityTags["db.response.status_code"].Should().Be(PostgresErrorCodes.UndefinedTable);
        activityTags["error.type"].Should().Be(PostgresErrorCodes.UndefinedTable);

        activityTags["db.pgsql.data_source"].Should().Be(connection.ConnectionString);

        if (IsMultiplexing)
            activityTags.Should().ContainKey("db.pgsql.connection_id");
        else
            activityTags["db.pgsql.connection_id"].Should().Be(connection.ProcessID);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task command_execute_explicit_prepare(bool batch)
    {
        //Arrange
        if (IsMultiplexing)
        {
            Assert.Skip("Explicit prepare is not supported with multiplexing");
        }

        await using var dataSource = CreateDataSource(o => o.ConfigureTracing(o => o.EnablePhysicalOpenTracing(false)));
        await using var connection = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        using var activityListener = StartListener(out var activities);

        //Act
        await ExecuteScalar(connection, async, batch, "SELECT 42", prepare: false);

        //Assert
        var activity = GetSingleActivity(activities, "postgresql", "postgresql");
        var tags = activity.TagObjects.ToDictionary(t => t.Key, t => t.Value);
        tags.Should().NotContainKey("db.pgsql.prepared");

        activities.Clear();
        await ExecuteScalar(connection, async, batch, "SELECT 42", prepare: true);
        activity = GetSingleActivity(activities, "postgresql", "postgresql");
        tags = activity.TagObjects.ToDictionary(t => t.Key, t => t.Value);
        tags["db.pgsql.prepared"].Should().Be(true);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task command_execute_auto_prepare(bool batch)
    {
        //Arrange
        var dataSourceBuilder = CreateDataSourceBuilder();
        dataSourceBuilder.ConnectionStringBuilder.MaxPoolSize = 1;
        dataSourceBuilder.ConnectionStringBuilder.MaxAutoPrepare = 10;
        dataSourceBuilder.ConnectionStringBuilder.AutoPrepareMinUsages = 2;
        dataSourceBuilder.ConfigureTracing(o => o.EnablePhysicalOpenTracing(false));
        await using var dataSource = dataSourceBuilder.Build();
        await using var connection = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        using var activityListener = StartListener(out var activities);

        //Act
        await ExecuteScalar(connection, async, batch, "SELECT 42");

        //Assert
        var activity = GetSingleActivity(activities, "postgresql", "postgresql");
        var tags = activity.TagObjects.ToDictionary(t => t.Key, t => t.Value);
        tags.Should().NotContainKey("db.pgsql.prepared");

        activities.Clear();
        await ExecuteScalar(connection, async, batch, "SELECT 42");
        activity = GetSingleActivity(activities, "postgresql", "postgresql");
        tags = activity.TagObjects.ToDictionary(t => t.Key, t => t.Value);
        tags["db.pgsql.prepared"].Should().Be(true);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task command_execute_ConfigureTracing(bool batch)
    {
        //Arrange
        var dataSourceBuilder = CreateDataSourceBuilder();
        dataSourceBuilder.ConfigureTracing(options =>
        {
            options
                .EnablePhysicalOpenTracing(false)
                .EnableFirstResponseEvent(enable: false)
                .ConfigureCommandFilter(cmd => cmd.CommandText.Contains('2'))
                .ConfigureBatchFilter(batch => batch.BatchCommands[0].CommandText.Contains('2'))
                .ConfigureCommandSpanNameProvider(_ => "unknown_query")
                .ConfigureBatchSpanNameProvider(_ => "unknown_query")
                .ConfigureCommandEnrichmentCallback((activity, _) => activity.AddTag("custom_tag", "custom_value"))
                .ConfigureBatchEnrichmentCallback((activity, _) => activity.AddTag("custom_tag", "custom_value"));
        });
        await using var dataSource = dataSourceBuilder.Build();
        await using var connection = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        using var activityListener = StartListener(out var activities);

        //Act
        await ExecuteScalar(connection, async, batch, "SELECT 1");

        //Assert
        activities.Should().BeEmpty();

        await ExecuteScalar(connection, async, batch, "SELECT 2");

        var activity = GetSingleActivity(activities, "unknown_query", "unknown_query");

        activity.Events.Count().Should().Be(0);

        var tags = activity.TagObjects.ToDictionary(t => t.Key, t => t.Value);
        tags["custom_tag"].Should().Be("custom_value");
    }

    #endregion Command execution

    #region Binary import

    [Fact]
    public async Task binary_import()
    {
        //Arrange
        await using var dataSource = CreateDataSource(ds => ds.ConfigureTracing(o => o.EnablePhysicalOpenTracing(false)));
        await using var connection = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        var table = await CreateTempTable(connection, "field_text TEXT, field_int2 SMALLINT");

        using var activityListener = StartListener(out var activities);

        //Act
        var copyFromCommand = $"COPY {table} (field_text, field_int2) FROM STDIN BINARY";

        if (async)
        {
            await using var writer = await connection.BeginBinaryImportAsync(copyFromCommand, TestContext.Current.CancellationToken);

            await writer.StartRowAsync(TestContext.Current.CancellationToken);
            await writer.WriteAsync("Hello", TestContext.Current.CancellationToken);
            await writer.WriteAsync((short)8, PgSqlDbType.Smallint, TestContext.Current.CancellationToken);

            await writer.CompleteAsync(TestContext.Current.CancellationToken);
        }
        else
        {
            using var writer = connection.BeginBinaryImport(copyFromCommand);

            writer.StartRow();
            writer.Write("Hello");
            writer.Write((short)8, PgSqlDbType.Smallint);

            writer.Complete();
        }

        //Assert
        var activity = GetSingleActivity(activities, "COPY FROM");

        var tags = activity.TagObjects.ToDictionary(t => t.Key, t => t.Value);
        tags.Should().HaveCount(connection.Settings.Port == 5432 ? 8 : 9);

        tags["db.query.text"].Should().Be(copyFromCommand);
        tags["db.operation.name"].Should().Be("COPY FROM");
        tags["db.system.name"].Should().Be("postgresql");
        tags["db.namespace"].Should().Be(connection.Settings.Database);

        tags["db.pgsql.data_source"].Should().Be(connection.ConnectionString);
        tags["db.pgsql.rows"].Should().Be(1UL);

        if (IsMultiplexing)
            tags.Should().ContainKey("db.pgsql.connection_id");
        else
            tags["db.pgsql.connection_id"].Should().Be(connection.ProcessID);
    }

    [Fact]
    public async Task binary_import_cancel()
    {
        //Arrange
        await using var dataSource = CreateDataSource(ds => ds.ConfigureTracing(o => o.EnablePhysicalOpenTracing(false)));
        await using var connection = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        var table = await CreateTempTable(connection, "field_text TEXT, field_int2 SMALLINT");

        using var activityListener = StartListener(out var activities);

        //Act
        var copyFromCommand = $"COPY {table} (field_text, field_int2) FROM STDIN BINARY";

        if (async)
        {
            await using var writer = await connection.BeginBinaryImportAsync(copyFromCommand, TestContext.Current.CancellationToken);
            await writer.StartRowAsync(TestContext.Current.CancellationToken);
            await writer.WriteAsync("Hello", TestContext.Current.CancellationToken);
            await writer.WriteAsync((short)8, PgSqlDbType.Smallint, TestContext.Current.CancellationToken);
            // No Complete() call - disposing cancels
        }
        else
        {
            using var writer = connection.BeginBinaryImport(copyFromCommand);
            writer.StartRow();
            writer.Write("Hello");
            writer.Write((short)8, PgSqlDbType.Smallint);
            // No Complete() call - disposing cancels
        }

        //Assert
        _ = GetSingleActivity(activities, "COPY FROM");
    }

    [Fact]
    public async Task binary_import_error()
    {
        //Arrange
        await using var dataSource = CreateDataSource(ds => ds.ConfigureTracing(o => o.EnablePhysicalOpenTracing(false)));
        await using var connection = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        using var activityListener = StartListener(out var activities);

        //Act
        var copyFromCommand = $"COPY non_existing_table (field_text, field_int2) FROM STDIN BINARY";

        await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            await using var writer = async
                ? await connection.BeginBinaryImportAsync(copyFromCommand, TestContext.Current.CancellationToken)
                : connection.BeginBinaryImport(copyFromCommand);
        });

        //Assert
        var activity = GetSingleActivity(activities, "COPY FROM", "COPY FROM", ActivityStatusCode.Error, PostgresErrorCodes.UndefinedTable);

        activity.Events.Count().Should().Be(1);
        var exceptionEvent = activity.Events.First();
        exceptionEvent.Name.Should().Be("exception");

        var exceptionTags = exceptionEvent.Tags.ToDictionary(t => t.Key, t => t.Value);
        exceptionTags.Should().HaveCount(3);

        exceptionTags["exception.type"].Should().Be(typeof(PostgresException).FullName);
        ((string)exceptionTags["exception.message"]).Should().Contain("relation \"non_existing_table\" does not exist");
        ((string)exceptionTags["exception.stacktrace"]).Should().Contain("relation \"non_existing_table\" does not exist");
    }

    #endregion Binary import

    #region Binary export

    [Fact]
    public async Task binary_export()
    {
        //Arrange
        await using var dataSource = CreateDataSource(ds => ds.ConfigureTracing(o => o.EnablePhysicalOpenTracing(false)));
        await using var connection = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        var table = await CreateTempTable(connection, "field_text TEXT, field_int2 SMALLINT");
        await connection.ExecuteNonQueryAsync($"INSERT INTO {table} (field_text, field_int2) VALUES ('Hello', 8)", cancellationToken: TestContext.Current.CancellationToken);

        using var activityListener = StartListener(out var activities);

        //Act
        var copyToCommand = $"COPY {table} (field_text, field_int2) TO STDOUT BINARY";

        if (async)
        {
            await using var reader = await connection.BeginBinaryExportAsync(copyToCommand, TestContext.Current.CancellationToken);
            while (await reader.StartRowAsync(TestContext.Current.CancellationToken) != -1)
            {
                _ = await reader.ReadAsync<string>(TestContext.Current.CancellationToken);
                _ = await reader.ReadAsync<short>(TestContext.Current.CancellationToken);
            }
        }
        else
        {
            using var reader = connection.BeginBinaryExport(copyToCommand);
            while (reader.StartRow() != -1)
            {
                _ = reader.Read<string>();
                _ = reader.Read<short>();
            }
        }

        //Assert
        var activity = GetSingleActivity(activities, "COPY TO");

        var tags = activity.TagObjects.ToDictionary(t => t.Key, t => t.Value);
        tags.Should().HaveCount(connection.Settings.Port == 5432 ? 8 : 9);

        tags["db.query.text"].Should().Be(copyToCommand);
        tags["db.operation.name"].Should().Be("COPY TO");
        tags["db.system.name"].Should().Be("postgresql");
        tags["db.namespace"].Should().Be(connection.Settings.Database);

        tags["db.pgsql.data_source"].Should().Be(connection.ConnectionString);
        tags["db.pgsql.rows"].Should().Be(1UL);

        if (IsMultiplexing)
            tags.Should().ContainKey("db.pgsql.connection_id");
        else
            tags["db.pgsql.connection_id"].Should().Be(connection.ProcessID);
    }

    [Fact]
    public async Task binary_export_cancel()
    {
        //Arrange
        await using var dataSource = CreateDataSource(ds => ds.ConfigureTracing(o => o.EnablePhysicalOpenTracing(false)));
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        using var activityListener = StartListener(out var activities);

        //Act
        // This must be large enough to cause Postgres to queue up CopyData messages.
        const string copyToCommand = "COPY (select md5(random()::text) as id from generate_series(1, 100000)) TO STDOUT BINARY";

        if (async)
        {
            await using var exporter = await conn.BeginBinaryExportAsync(copyToCommand, TestContext.Current.CancellationToken);
            await exporter.StartRowAsync(TestContext.Current.CancellationToken);
            await exporter.ReadAsync<string>(TestContext.Current.CancellationToken);
            await exporter.CancelAsync();
        }
        else
        {
            using var exporter = await conn.BeginBinaryExportAsync(copyToCommand, TestContext.Current.CancellationToken);
            exporter.StartRow();
            exporter.Read<string>();
            exporter.Cancel();
        }

        //Assert
        _ = GetSingleActivity(activities, "COPY TO");
    }

    [Fact]
    public async Task binary_export_error()
    {
        //Arrange
        await using var dataSource = CreateDataSource(ds => ds.ConfigureTracing(o => o.EnablePhysicalOpenTracing(false)));
        await using var connection = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        using var activityListener = StartListener(out var activities);

        //Act
        var copyToCommand = $"COPY non_existing_table (field_text, field_int2) TO STDOUT BINARY";
        await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            await using var reader = async
                ? await connection.BeginBinaryExportAsync(copyToCommand, TestContext.Current.CancellationToken)
                : connection.BeginBinaryExport(copyToCommand);
        });

        //Assert
        var activity = GetSingleActivity(activities, "COPY TO", "COPY TO", ActivityStatusCode.Error, PostgresErrorCodes.UndefinedTable);

        activity.Events.Count().Should().Be(1);
        var exceptionEvent = activity.Events.First();
        exceptionEvent.Name.Should().Be("exception");

        var exceptionTags = exceptionEvent.Tags.ToDictionary(t => t.Key, t => t.Value);
        exceptionTags.Should().HaveCount(3);

        exceptionTags["exception.type"].Should().Be(typeof(PostgresException).FullName);
        ((string)exceptionTags["exception.message"]).Should().Contain("relation \"non_existing_table\" does not exist");
        ((string)exceptionTags["exception.stacktrace"]).Should().Contain("relation \"non_existing_table\" does not exist");
    }

    #endregion Binary export

    #region Raw binary

    [Fact]
    public async Task raw_binary_export()
    {
        //Arrange
        await using var dataSource = CreateDataSource(ds => ds.ConfigureTracing(o => o.EnablePhysicalOpenTracing(false)));
        await using var connection = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        var table = await CreateTempTable(connection, "field_text TEXT, field_int2 SMALLINT");
        await connection.ExecuteNonQueryAsync($"INSERT INTO {table} (field_text, field_int2) VALUES ('Hello', 8)", cancellationToken: TestContext.Current.CancellationToken);

        using var activityListener = StartListener(out var activities);

        //Act
        // Raw binary export
        var copyToCommand = $"COPY {table} (field_text, field_int2) TO STDIN BINARY";
        var buffer = new byte[1024];
        if (async)
        {
            await using var stream = await connection.BeginRawBinaryCopyAsync(copyToCommand, TestContext.Current.CancellationToken);
            while (await stream.ReadAsync(buffer, 0, buffer.Length, TestContext.Current.CancellationToken) > 0) { }
        }
        else
        {
            using var stream = connection.BeginRawBinaryCopy(copyToCommand);
            while (stream.Read(buffer, 0, buffer.Length) > 0) { }
        }

        //Assert
        var activity = GetSingleActivity(activities, "COPY");

        var tags = activity.TagObjects.ToDictionary(t => t.Key, t => t.Value);

        tags.Should().HaveCount(connection.Settings.Port == 5432 ? 7 : 8);

        tags["db.query.text"].Should().Be(copyToCommand);
        tags["db.operation.name"].Should().Be("COPY TO");
        tags["db.system.name"].Should().Be("postgresql");
        tags["db.namespace"].Should().Be(connection.Settings.Database);

        tags["db.pgsql.data_source"].Should().Be(connection.ConnectionString);

        if (IsMultiplexing)
            tags.Should().ContainKey("db.pgsql.connection_id");
        else
            tags["db.pgsql.connection_id"].Should().Be(connection.ProcessID);

        tags.Should().NotContainKey("db.pgsql.rows");
    }

    [Fact]
    public async Task raw_binary_export_cancel()
    {
        //Arrange
        await using var dataSource = CreateDataSource(ds => ds.ConfigureTracing(o => o.EnablePhysicalOpenTracing(false)));
        await using var connection = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        var table = await CreateTempTable(connection, "field_text TEXT, field_int2 SMALLINT");
        await connection.ExecuteNonQueryAsync($"INSERT INTO {table} (field_text, field_int2) VALUES ('Hello', 8)", cancellationToken: TestContext.Current.CancellationToken);

        using var activityListener = StartListener(out var activities);

        //Act
        var copyToCommand = $"COPY {table} (field_text, field_int2) TO STDIN BINARY";
        var buffer = new byte[1024];
        if (async)
        {
            await using var stream = await connection.BeginRawBinaryCopyAsync(copyToCommand, TestContext.Current.CancellationToken);
            var _ = await stream.ReadAsync(buffer, 0, buffer.Length, TestContext.Current.CancellationToken);
            await stream.CancelAsync();
        }
        else
        {
            using var stream = connection.BeginRawBinaryCopy(copyToCommand);
            var _ = stream.Read(buffer, 0, buffer.Length);
            stream.Cancel();
        }

        //Assert
        _ = GetSingleActivity(activities, "COPY");
    }

    [Fact]
    public async Task raw_binary_import_cancel()
    {
        //Arrange
        await using var dataSource = CreateDataSource(ds => ds.ConfigureTracing(o => o.EnablePhysicalOpenTracing(false)));
        await using var connection = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        var table = await CreateTempTable(connection, "field_text TEXT, field_int2 SMALLINT");

        using var activityListener = StartListener(out var activities);

        //Act
        var copyToCommand = $"COPY {table} (field_text, field_int2) FROM STDIN BINARY";
        byte[] garbage = [1, 2, 3, 4];
        if (async)
        {
            await using var stream = await connection.BeginRawBinaryCopyAsync(copyToCommand, TestContext.Current.CancellationToken);
            await stream.WriteAsync(garbage, TestContext.Current.CancellationToken);
            await stream.FlushAsync(TestContext.Current.CancellationToken);
            await stream.CancelAsync();
        }
        else
        {
            using var stream = connection.BeginRawBinaryCopy(copyToCommand);
            stream.Write(garbage);
            stream.Flush();
            stream.Cancel();
        }

        //Assert
        _ = GetSingleActivity(activities, "COPY");
    }

    [Fact]
    public async Task raw_binary_import_error()
    {
        //Arrange
        await using var dataSource = CreateDataSource(ds => ds.ConfigureTracing(o => o.EnablePhysicalOpenTracing(false)));
        await using var connection = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        using var activityListener = StartListener(out var activities);

        //Act
        var copyFromCommand = $"COPY non_existing_table (field_text, field_int2) FROM STDIN BINARY";
        await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            await using var stream = async
                ? await connection.BeginRawBinaryCopyAsync(copyFromCommand, TestContext.Current.CancellationToken)
                : connection.BeginRawBinaryCopy(copyFromCommand);
        });

        //Assert
        var activity = GetSingleActivity(activities, "COPY", "COPY", ActivityStatusCode.Error, PostgresErrorCodes.UndefinedTable);

        activity.Events.Count().Should().Be(1);
        var exceptionEvent = activity.Events.First();
        exceptionEvent.Name.Should().Be("exception");

        var exceptionTags = exceptionEvent.Tags.ToDictionary(t => t.Key, t => t.Value);
        exceptionTags.Should().HaveCount(3);

        exceptionTags["exception.type"].Should().Be(typeof(PostgresException).FullName);
        ((string)exceptionTags["exception.message"]).Should().Contain("relation \"non_existing_table\" does not exist");
        ((string)exceptionTags["exception.stacktrace"]).Should().Contain("relation \"non_existing_table\" does not exist");
    }

    #endregion Raw binary

    #region Text COPY

    [Fact]
    public async Task text_import()
    {
        //Arrange
        await using var dataSource = CreateDataSource(ds => ds.ConfigureTracing(o => o.EnablePhysicalOpenTracing(false)));
        await using var connection = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        var table = await CreateTempTable(connection, "field_text TEXT, field_int2 SMALLINT");

        using var activityListener = StartListener(out var activities);

        //Act
        var copyFromCommand = $"COPY {table} (field_text, field_int2) FROM STDIN";

        if (async)
        {
            await using var writer = await connection.BeginTextImportAsync(copyFromCommand, TestContext.Current.CancellationToken);
            await writer.WriteAsync("Hello\t8\n");
        }
        else
        {
            using var writer = connection.BeginTextImport(copyFromCommand);
            writer.Write("Hello\t8\n");
        }

        //Assert
        var activity = GetSingleActivity(activities, "COPY FROM");

        var tags = activity.TagObjects.ToDictionary(t => t.Key, t => t.Value);

        tags.Should().HaveCount(connection.Settings.Port == 5432 ? 7 : 8);

        tags["db.query.text"].Should().Be(copyFromCommand);
        tags["db.operation.name"].Should().Be("COPY FROM");
        tags["db.system.name"].Should().Be("postgresql");
        tags["db.namespace"].Should().Be(connection.Settings.Database);

        tags["db.pgsql.data_source"].Should().Be(connection.ConnectionString);

        if (IsMultiplexing)
            tags.Should().ContainKey("db.pgsql.connection_id");
        else
            tags["db.pgsql.connection_id"].Should().Be(connection.ProcessID);

        tags.Should().NotContainKey("db.pgsql.rows");
    }

    [Fact]
    public async Task text_export()
    {
        //Arrange
        await using var dataSource = CreateDataSource(ds => ds.ConfigureTracing(o => o.EnablePhysicalOpenTracing(false)));
        await using var connection = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        var table = await CreateTempTable(connection, "field_text TEXT, field_int2 SMALLINT");

        var insertCmd = $"INSERT INTO {table} (field_text, field_int2) VALUES ('Hello', 8)";
        await connection.ExecuteNonQueryAsync(insertCmd, cancellationToken: TestContext.Current.CancellationToken);

        using var activityListener = StartListener(out var activities);

        //Act
        var copyFromCommand = $"COPY {table} (field_text, field_int2) TO STDIN";

        var chars = new char[30];
        if (async)
        {
            await using var reader = await connection.BeginTextExportAsync(copyFromCommand, TestContext.Current.CancellationToken);
            _ = await reader.ReadAsync(chars, TestContext.Current.CancellationToken);
        }
        else
        {
            using var reader = connection.BeginTextExport(copyFromCommand);
            _ = reader.Read(chars);
        }

        //Assert
        var activity = GetSingleActivity(activities, "COPY TO");

        var tags = activity.TagObjects.ToDictionary(t => t.Key, t => t.Value);

        tags.Should().HaveCount(connection.Settings.Port == 5432 ? 7 : 8);

        tags["db.query.text"].Should().Be(copyFromCommand);
        tags["db.operation.name"].Should().Be("COPY TO");
        tags["db.system.name"].Should().Be("postgresql");
        tags["db.namespace"].Should().Be(connection.Settings.Database);

        tags["db.pgsql.data_source"].Should().Be(connection.ConnectionString);

        if (IsMultiplexing)
            tags.Should().ContainKey("db.pgsql.connection_id");
        else
            tags["db.pgsql.connection_id"].Should().Be(connection.ProcessID);

        tags.Should().NotContainKey("db.pgsql.rows");
    }

    // Text COPY is implemented over PgSqlRawCopyStream internally, without any additional tracing-related logic.
    // So we do only basic direct coverage and depend on the general raw tests for the rest.

    #endregion Text COPY

    // All ConfigureTracing() aspects of COPY are implemented in a single code path for all COPY paths, so we test just one.

    [Fact]
    public async Task copy_ConfigureTracing()
    {
        //Arrange
        await using var dataSource = CreateDataSource(builder => builder.ConfigureTracing(options =>
            options
                .EnablePhysicalOpenTracing(false)
                .ConfigureCopyOperationFilter(command => command.Contains("filter_in"))
                .ConfigureCopyOperationSpanNameProvider(_ => "custom_binary_import")
                .ConfigureCopyOperationEnrichmentCallback((activity, _) => activity.AddTag("custom_tag", "custom_value"))));

        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        var table = await CreateTempTable(conn, "field_text TEXT, field_int_filter_in SMALLINT");
        var copyCommand = $"COPY {table} (field_text, field_int_filter_in) FROM STDIN BINARY";

        var filteredOutTable = await CreateTempTable(conn, "field_text TEXT, field_int_filter_out SMALLINT");
        var filteredOutCopyCommand = $"COPY {filteredOutTable} (field_text, field_int_filter_out) FROM STDIN BINARY";

        using var activityListener = StartListener(out var activities);

        //Act
        if (async)
        {
            await using (var writer = await conn.BeginBinaryImportAsync(copyCommand, TestContext.Current.CancellationToken))
            {
                await writer.CompleteAsync(TestContext.Current.CancellationToken);
            }

            await using (var writer = await conn.BeginBinaryImportAsync(filteredOutCopyCommand, TestContext.Current.CancellationToken))
            {
                await writer.CompleteAsync(TestContext.Current.CancellationToken);
            }
        }
        else
        {
            using (var writer = conn.BeginBinaryImport(copyCommand))
            {
                writer.Complete();
            }

            using (var writer = conn.BeginBinaryImport(filteredOutCopyCommand))
            {
                writer.Complete();
            }
        }

        // There should be just one activity since one of the two COPY commands is filtered out

        //Assert
        var activity = GetSingleActivity(activities, "custom_binary_import", "custom_binary_import");

        var tags = activity.TagObjects.ToDictionary(t => t.Key, t => t.Value);
        tags["custom_tag"].Should().Be("custom_value");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task password_does_not_leak_via_datasource_name(bool persistSecurityInfo)
    {
        //Arrange
        var dataSourceBuilder = CreateDataSourceBuilder();
        dataSourceBuilder.ConnectionStringBuilder.PersistSecurityInfo = persistSecurityInfo;
        // Do not set the data source name - this makes it default to the connection string, but without
        // the password (even when Persist Security Info is true)
        await using var dataSource = dataSourceBuilder.Build();
        await using var connection = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

        using var activityListener = StartListener(out var activities);

        //Act
        await ExecuteScalar(connection, async, isBatch: false, query: "SELECT 42");

        //Assert
        var activity = GetSingleActivity(activities, "postgresql", "postgresql");

        var tags = activity.TagObjects.ToDictionary(t => t.Key, t => t.Value);
        var connectionString = new PgSqlConnectionStringBuilder((string)tags["db.pgsql.data_source"]);
        connectionString.Password.Should().BeNull();
    }

    static ActivityListener StartListener(out List<Activity> activities)
    {
        var a = new List<Activity>();

        var activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "PgSql",
            Sample = (ref _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => a.Add(activity)
        };
        ActivitySource.AddActivityListener(activityListener);

        activities = a;
        return activityListener;
    }

    static Activity GetSingleActivity(
        List<Activity> activities,
        string expectedDisplayName,
        string expectedOperationName = null,
        ActivityStatusCode? expectedStatusCode = null,
        string expectedStatusDescription = null)
    {
        activities.Should().HaveCount(1);
        var activity = activities[0];
        activity.DisplayName.Should().Be(expectedDisplayName);
        activity.OperationName.Should().Be(expectedOperationName ?? expectedDisplayName);
        activity.Status.Should().Be(expectedStatusCode ?? ActivityStatusCode.Unset);
        activity.StatusDescription.Should().Be(expectedStatusDescription);

        return activity;
    }

    static async Task<object> ExecuteScalar(PgSqlConnection connection, bool async, bool isBatch, string query, bool prepare = false)
    {
        if (isBatch)
        {
            await using var batch = connection.CreateBatch();
            var batchCommand = batch.CreateBatchCommand();
            batchCommand.CommandText = query;
            batch.BatchCommands.Add(batchCommand);

            if (prepare)
            {
                if (async)
                    await batch.PrepareAsync();
                else
                    batch.Prepare();
            }

            if (async)
                return await batch.ExecuteScalarAsync();
            else
                return batch.ExecuteScalar();
        }
        else
        {
            await using var command = connection.CreateCommand();
            command.CommandText = query;

            if (prepare)
            {
                if (async)
                    await command.PrepareAsync();
                else
                    command.Prepare();
            }

            if (async)
                return await command.ExecuteScalarAsync();
            else
                return command.ExecuteScalar();
        }
    }
}

[Collection(NonParallelCollection.Name)]
public sealed class TracingTests_NonMultiplexing_Async() : TracingTests(MultiplexingMode.NonMultiplexing, true);
[Collection(NonParallelCollection.Name)]
public sealed class TracingTests_NonMultiplexing_Sync() : TracingTests(MultiplexingMode.NonMultiplexing, false);
// Sync I/O not supported with multiplexing
[Collection(NonParallelCollection.Name)]
public sealed class TracingTests_Multiplexing_Async() : TracingTests(MultiplexingMode.Multiplexing, true);
