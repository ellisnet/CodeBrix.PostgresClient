using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.PostgresClient.Tests.Support;
using SilverAssertions;
using Xunit;

namespace CodeBrix.PostgresClient.Tests; //was previously: Npgsql.Tests;

// These tests observe the library's "PgSql" meter with the in-box System.Diagnostics.Metrics.MeterListener. Listeners are
// process-wide, so every assertion filters measurements by the test's own (unique) data source name, and the class runs
// in the non-parallel collection so that observable-instrument callbacks are not triggered concurrently by other tests.
[Collection(NonParallelCollection.Name)]
public class MetricTests : TestBase
{
    [Fact]
    public async Task operation_duration()
    {
        //Arrange
        using var recorder = new MetricRecorder("db.client.operation.duration");

        await using var dataSource = CreateDataSource();
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1";

        //Act
        await using (var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken))
            while (await reader.ReadAsync(TestContext.Current.CancellationToken));

        //Assert
        var measurements = recorder.GetMeasurements("db.client.operation.duration");
        measurements.Should().NotBeEmpty("metric 'db.client.operation.duration' should have been recorded");

        var points = GetFilteredMeasurements(measurements, dataSource.Name);
        points.Should().HaveCount(1, "the histogram count should be 1");
        points.Sum(p => Convert.ToDouble(p.Value)).Should().BeGreaterThan(0);

        var point = points.Single();
        point.Unit.Should().Be("s");

        var tags = point.Tags;

        // TODO: Vary this for PG-like databases (e.g. CockroachDB)?
        tags["db.system.name"].Should().Be("postgresql");

        tags["server.address"].Should().Be(dataSource.Settings.Host);
        tags["server.port"].Should().Be(dataSource.Settings.Port);
        tags["db.client.connection.pool.name"].Should().Be(dataSource.Name);
    }

    [Fact]
    public async Task connection_count()
    {
        //Arrange
        using var recorder = new MetricRecorder("db.client.connection.count");

        await using var dataSource = CreateDataSource();

        using (var _ = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken))
        {
            recorder.RecordObservableInstruments();

            var points = GetFilteredMeasurements(recorder.GetMeasurements("db.client.connection.count"), dataSource.Name);

            var usedPoint = GetPoint(points, "used");
            Convert.ToInt64(usedPoint.Value).Should().Be(1, "expected used connections to be 1");

            var idlePoint = GetPoint(points, "idle");
            Convert.ToInt64(idlePoint.Value).Should().Be(0, "expected idle connections to be 0");

            recorder.Clear();
        }

        recorder.RecordObservableInstruments();

        {
            var points = GetFilteredMeasurements(recorder.GetMeasurements("db.client.connection.count"), dataSource.Name);

            var usedPoint = GetPoint(points, "used");
            Convert.ToInt64(usedPoint.Value).Should().Be(0, "expected used connections to be 0");

            var idlePoint = GetPoint(points, "idle");
            Convert.ToInt64(idlePoint.Value).Should().Be(1, "expected idle connections to be 1");
        }

        static RecordedMeasurement GetPoint(IEnumerable<RecordedMeasurement> points, string state)
        {
            // An observable up-down counter reports the current value on every observation; the latest one is the
            // equivalent of the exported point's sum.
            var point = points.LastOrDefault(p => p.Tags.TryGetValue("db.client.connection.state", out var value) && (string)value == state);
            point.Should().NotBeNull($"a point with state '{state}' should exist");
            return point;
        }
    }

    [Fact]
    public async Task connection_max()
    {
        //Arrange
        using var recorder = new MetricRecorder("db.client.connection.max");

        var dataSourceBuilder = CreateDataSourceBuilder();
        dataSourceBuilder.ConnectionStringBuilder.MaxPoolSize = 134;
        await using var dataSource = dataSourceBuilder.Build();

        //Act
        recorder.RecordObservableInstruments();

        //Assert
        var point = GetFilteredMeasurements(recorder.GetMeasurements("db.client.connection.max"), dataSource.Name)
            .First(p => Convert.ToInt64(p.Value) == 134);
        point.Tags["db.client.connection.pool.name"].Should().Be(dataSource.Name);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task password_does_not_leak_via_datasource_name(bool persistSecurityInfo)
    {
        //Arrange
        using var recorder = new MetricRecorder("db.client.connection.max");

        var dataSourceBuilder = base.CreateDataSourceBuilder();
        dataSourceBuilder.ConnectionStringBuilder.ApplicationName = "MetricsDataSource" + Interlocked.Increment(ref _dataSourceCounter);
        dataSourceBuilder.ConnectionStringBuilder.PersistSecurityInfo = persistSecurityInfo;
        // Do not set the data source name - this makes it default to the connection string, but without
        // the password (even when Persist Security Info is true)
        await using var dataSource = dataSourceBuilder.Build();

        //Act
        recorder.RecordObservableInstruments();

        //Assert
        var point = GetFilteredMeasurements(recorder.GetMeasurements("db.client.connection.max"), dataSource.Name).First();
        var connectionString = new PgSqlConnectionStringBuilder((string)point.Tags["db.client.connection.pool.name"]);
        connectionString.Password.Should().BeNull();
    }

    protected override PgSqlDataSourceBuilder CreateDataSourceBuilder()
    {
        var dataSourceBuilder = base.CreateDataSourceBuilder();
        dataSourceBuilder.Name = "MetricsDataSource" + Interlocked.Increment(ref _dataSourceCounter);
        return dataSourceBuilder;
    }

    protected override PgSqlDataSource CreateDataSource()
        => CreateDataSourceBuilder().Build();

    // Static (not per instance as in a single NUnit fixture instance), since xUnit creates a new instance per test.
    static int _dataSourceCounter;

    static List<RecordedMeasurement> GetFilteredMeasurements(IEnumerable<RecordedMeasurement> measurements, string dataSourceName)
        => measurements
            .Where(m => m.Tags.TryGetValue("db.client.connection.pool.name", out var value) && (string)value == dataSourceName)
            .ToList();

    /// <summary>A single measurement captured from an instrument of the "PgSql" meter.</summary>
    sealed record RecordedMeasurement(string InstrumentName, string Unit, object Value, Dictionary<string, object> Tags);

    /// <summary>
    /// Captures measurements of the named instruments of the "PgSql" meter with an in-box <see cref="MeterListener"/>.
    /// </summary>
    sealed class MetricRecorder : IDisposable
    {
        readonly MeterListener _listener;
        readonly HashSet<string> _instrumentNames;
        readonly List<RecordedMeasurement> _measurements = [];

        public MetricRecorder(params string[] instrumentNames)
        {
            _instrumentNames = [..instrumentNames];
            _listener = new MeterListener
            {
                InstrumentPublished = (instrument, listener) =>
                {
                    // Only enable the instruments under test: some observable callbacks have side effects (e.g. the
                    // prepared-ratio gauge resets its counters on every observation).
                    if (instrument.Meter.Name == "PgSql" && _instrumentNames.Contains(instrument.Name))
                        listener.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<int>((instrument, value, tags, _) => Record(instrument, value, tags));
            _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => Record(instrument, value, tags));
            _listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => Record(instrument, value, tags));
            _listener.Start();
        }

        void Record(Instrument instrument, object value, ReadOnlySpan<KeyValuePair<string, object>> tags)
        {
            var tagDictionary = new Dictionary<string, object>();
            foreach (var tag in tags)
                tagDictionary[tag.Key] = tag.Value;

            lock (_measurements)
                _measurements.Add(new RecordedMeasurement(instrument.Name, instrument.Unit, value, tagDictionary));
        }

        public void RecordObservableInstruments()
            => _listener.RecordObservableInstruments();

        public List<RecordedMeasurement> GetMeasurements(string instrumentName)
        {
            lock (_measurements)
                return _measurements.Where(m => m.InstrumentName == instrumentName).ToList();
        }

        public void Clear()
        {
            lock (_measurements)
                _measurements.Clear();
        }

        public void Dispose()
            => _listener.Dispose();
    }
}
