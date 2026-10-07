using System;
using System.Data;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;
using SilverAssertions;
using Xunit;
using static CodeBrix.PostgresClient.Tests.TestUtil;

namespace CodeBrix.PostgresClient.Tests; //was previously: Npgsql.Tests;

public class ExceptionTests : TestBase
{
    // Generates a basic server-side exception, checks that it's properly raised and populated
    [Fact]
    public void basic()
    {
        //Arrange
        // Make sure messages are in English
        using var dataSource = CreateDataSource(csb => csb.Options = "-c lc_messages=en_US.UTF-8");
        using var conn = dataSource.OpenConnection();
        conn.ExecuteNonQuery(
"""
CREATE OR REPLACE FUNCTION pg_temp.emit_exception() RETURNS VOID AS
   'BEGIN RAISE EXCEPTION ''testexception'' USING ERRCODE = ''12345'', DETAIL = ''testdetail''; END;'
LANGUAGE 'plpgsql';
""");

        //Act
        PostgresException ex = null;
        try
        {
            conn.ExecuteNonQuery("SELECT pg_temp.emit_exception()");
            Assert.Fail("No exception was thrown");
        }
        catch (PostgresException e)
        {
            ex = e;
        }

        //Assert
        ex.MessageText.Should().Be("testexception");
        ex.Severity.Should().Be("ERROR");
        ex.InvariantSeverity.Should().Be("ERROR");
        ex.SqlState.Should().Be("12345");
        ex.Position.Should().Be(0);
        ex.Message.Should().StartWith("12345: testexception");

        var data = ex.Data;
        data[nameof(PostgresException.Severity)].Should().Be("ERROR");
        data[nameof(PostgresException.SqlState)].Should().Be("12345");
        data.Contains(nameof(PostgresException.Position)).Should().BeFalse();

        var exString = ex.ToString();
        exString.Should().StartWith("CodeBrix.PostgresClient.PostgresException (0x80004005): 12345: testexception");
        exString.Should().Contain(nameof(PostgresException.Severity) + ": ERROR");
        exString.Should().Contain(nameof(PostgresException.SqlState) + ": 12345");

        conn.ExecuteScalar("SELECT 1").Should().Be(1, "the connection must not be left in a bad state after an exception");
    }

    // Ensures Detail is redacted by default in PostgresException and PostgresNotice
    [Fact]
    public async Task Error_details_are_redacted()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        var raiseExceptionFunc = await GetTempFunctionName(conn);
        var raiseNoticeFunc = await GetTempFunctionName(conn);

        await conn.ExecuteNonQueryAsync($@"
CREATE OR REPLACE FUNCTION {raiseExceptionFunc}() RETURNS VOID AS $$
BEGIN
    RAISE EXCEPTION 'testexception' USING DETAIL = 'secret';
END;
$$ LANGUAGE 'plpgsql';

CREATE OR REPLACE FUNCTION {raiseNoticeFunc}() RETURNS VOID AS $$
BEGIN
    RAISE NOTICE 'testexception' USING DETAIL = 'secret';
END;
$$ LANGUAGE 'plpgsql';", cancellationToken: TestContext.Current.CancellationToken);

        //Act
        var ex = await Assert.ThrowsAsync<PostgresException>(() => conn.ExecuteNonQueryAsync($"SELECT * FROM {raiseExceptionFunc}()", cancellationToken: TestContext.Current.CancellationToken));

        //Assert
        ex.Detail.Should().NotContain("secret");
        ex.Message.Should().NotContain("secret");
        ((string)ex.Data[nameof(PostgresException.Detail)]).Should().NotContain("secret");
        ex.ToString().Should().NotContain("secret");

        PostgresNotice notice = null;
        conn.Notice += (___, a) => notice = a.Notice;
        await conn.ExecuteNonQueryAsync($"SELECT * FROM {raiseNoticeFunc}()", cancellationToken: TestContext.Current.CancellationToken);
        notice.Detail.Should().NotContain("secret");
    }

    [Fact]
    public async Task IncludeErrorDetail()
    {
        //Arrange
        await using var dataSource = CreateDataSource(csb => csb.IncludeErrorDetail = true);
        await using var conn = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        var raiseExceptionFunc = await GetTempFunctionName(conn);
        var raiseNoticeFunc = await GetTempFunctionName(conn);

        await conn.ExecuteNonQueryAsync($@"
CREATE OR REPLACE FUNCTION {raiseExceptionFunc}() RETURNS VOID AS $$
BEGIN
    RAISE EXCEPTION 'testexception' USING DETAIL = 'secret';
END;
$$ LANGUAGE 'plpgsql';

CREATE OR REPLACE FUNCTION {raiseNoticeFunc}() RETURNS VOID AS $$
BEGIN
    RAISE NOTICE 'testexception' USING DETAIL = 'secret';
END;
$$ LANGUAGE 'plpgsql';", cancellationToken: TestContext.Current.CancellationToken);

        //Act
        var ex = await Assert.ThrowsAsync<PostgresException>(() => conn.ExecuteNonQueryAsync($"SELECT * FROM {raiseExceptionFunc}()", cancellationToken: TestContext.Current.CancellationToken));

        //Assert
        ex.Detail.Should().Contain("secret");
        ex.Message.Should().Contain("secret");
        ((string)ex.Data[nameof(PostgresException.Detail)]).Should().Contain("secret");
        ex.ToString().Should().Contain("secret");

        PostgresNotice notice = null;
        conn.Notice += (____, a) => notice = a.Notice;
        await conn.ExecuteNonQueryAsync($"SELECT * FROM {raiseNoticeFunc}()", cancellationToken: TestContext.Current.CancellationToken);
        notice.Detail.Should().Contain("secret");
    }

    [Fact]
    public async Task Error_position()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();

        //Act
        var ex = await Assert.ThrowsAsync<PostgresException>(() => conn.ExecuteNonQueryAsync("SELECT 1; SELECT * FROM \"NonExistingTable\"", cancellationToken: TestContext.Current.CancellationToken));

        //Assert
        ex.Message.Should().Contain("POSITION: 15");
    }

    [Fact]
    public void Exception_fields_are_populated()
    {
        //Arrange
        using var conn = OpenConnection();
        TestUtil.MinimumPgVersion(conn, "9.3.0", "5 error fields haven't been added yet");
        conn.ExecuteNonQuery("CREATE TEMP TABLE uniqueviolation (id INT NOT NULL, CONSTRAINT uniqueviolation_pkey PRIMARY KEY (id))");
        conn.ExecuteNonQuery("INSERT INTO uniqueviolation (id) VALUES(1)");

        //Act
        try
        {
            conn.ExecuteNonQuery("INSERT INTO uniqueviolation (id) VALUES(1)");
        }
        catch (PostgresException ex)
        {
            //Assert
            ex.ColumnName.Should().BeNull("ColumnName should not be populated for unique violations");
            ex.TableName.Should().Be("uniqueviolation");
            ex.SchemaName.Should().StartWith("pg_temp");
            ex.ConstraintName.Should().Be("uniqueviolation_pkey");
            ex.DataTypeName.Should().BeNull("DataTypeName should not be populated for unique violations");
        }
    }

    [Fact]
    public void Column_name_exception_field_is_populated()
    {
        //Arrange
        using var conn = OpenConnection();
        TestUtil.MinimumPgVersion(conn, "9.3.0", "5 error fields haven't been added yet");
        conn.ExecuteNonQuery("CREATE TEMP TABLE notnullviolation (id INT NOT NULL)");

        //Act
        try
        {
            conn.ExecuteNonQuery("INSERT INTO notnullviolation (id) VALUES(NULL)");
        }
        catch (PostgresException ex)
        {
            //Assert
            ex.SchemaName.Should().StartWith("pg_temp");
            ex.TableName.Should().Be("notnullviolation");
            ex.ColumnName.Should().Be("id");
        }
    }

    [Fact]
    public async Task DataTypeName_is_populated()
    {
        //Arrange
        // On reading the source code for PostgreSQL9.3beta1, the only time that the
        // datatypename field is populated is when using domain types. So here we'll
        // create a domain that simply does not allow NULLs then try and cast NULL
        // to it.
        await using var conn = await OpenConnectionAsync();
        MinimumPgVersion(conn, "9.3.0", "5 error fields haven't been added yet");

        var domainName = await GetTempTypeName(conn);

        await conn.ExecuteNonQueryAsync($"CREATE DOMAIN {domainName} AS INT NOT NULL", cancellationToken: TestContext.Current.CancellationToken);

        //Act
        var pgEx = await Assert.ThrowsAsync<PostgresException>(async () => await conn.ExecuteNonQueryAsync($"SELECT CAST(NULL AS {domainName})", cancellationToken: TestContext.Current.CancellationToken));

        //Assert
        pgEx.SqlState.Should().Be(PostgresErrorCodes.NotNullViolation);
        pgEx.SchemaName.Should().Be("public");
        pgEx.DataTypeName.Should().Be(domainName);
    }

    [Fact]
    public async Task PgSqlException_with_async()
    {
        //Arrange
        using var conn = OpenConnection();

        //Act
        await Assert.ThrowsAsync<PostgresException>(async () => await conn.ExecuteNonQueryAsync("MALFORMED", cancellationToken: TestContext.Current.CancellationToken));

        //Assert
        // Just in case, anything but a PostgresException would trigger the connection breaking, check that
        conn.FullState.Should().Be(ConnectionState.Open);
    }

    [Fact]
    public void PgSqlException_IsTransient()
    {
        new PgSqlException("", new IOException()).IsTransient.Should().BeTrue();
        new PgSqlException("", new SocketException()).IsTransient.Should().BeTrue();
        new PgSqlException("", new TimeoutException()).IsTransient.Should().BeTrue();
        new PgSqlException().IsTransient.Should().BeFalse();
        new PgSqlException("", new Exception("Inner Exception")).IsTransient.Should().BeFalse();
    }

}
