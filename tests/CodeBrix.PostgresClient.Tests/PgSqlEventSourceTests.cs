using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Linq;
using CodeBrix.PostgresClient.Tests.Support;
using SilverAssertions;
using Xunit;

namespace CodeBrix.PostgresClient.Tests; //was previously: Npgsql.Tests;

[Collection(NonParallelCollection.Name)] // Events
public class PgSqlEventSourceTests : TestBase, IClassFixture<PgSqlEventSourceTests.EventSourceFixture>
{
    readonly EventSourceFixture _fixture;

    public PgSqlEventSourceTests(EventSourceFixture fixture)
    {
        _fixture = fixture;
        ClearEvents();
    }

    [Fact]
    public void Command_start_stop()
    {
        //Arrange
        using (var conn = OpenConnection())
        {
            // There is a new pool created, which sends a few queries to load pg types
            ClearEvents();

            //Act
            conn.ExecuteScalar("SELECT 1");
        }

        //Assert
        var commandStart = _fixture.Events.Single(e => e.EventId == PgSqlEventSource.CommandStartId);
        commandStart.EventName.Should().Be("CommandStart");

        var commandStop = _fixture.Events.Single(e => e.EventId == PgSqlEventSource.CommandStopId);
        commandStop.EventName.Should().Be("CommandStop");
    }

    void ClearEvents() => _fixture.Events.Clear();

    public sealed class EventSourceFixture : IDisposable
    {
        readonly TestEventListener _listener;

        public EventSourceFixture()
        {
            _listener = new TestEventListener(Events);
            _listener.EnableEvents(PgSqlSqlEventSource.Log, EventLevel.Informational);
        }

        internal List<EventWrittenEventArgs> Events { get; } = [];

        public void Dispose()
        {
            _listener.DisableEvents(PgSqlSqlEventSource.Log);
            _listener.Dispose();
        }
    }

    class TestEventListener(List<EventWrittenEventArgs> events) : EventListener
    {
        protected override void OnEventWritten(EventWrittenEventArgs eventData) => events.Add(eventData);
    }
}
