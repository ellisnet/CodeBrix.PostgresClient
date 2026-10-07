using System.Linq;
using System.Text;
using SilverAssertions;
using Xunit;

namespace CodeBrix.PostgresClient.Tests; //was previously: Npgsql.Tests;

public class LargeObjectTests : TestBase
{
    [Fact]
    public void test()
    {
        using var conn = OpenConnection();
        using var transaction = conn.BeginTransaction();
        var manager = new PgSqlLargeObjectManager(conn);
        var oid = manager.Create();

        using (var stream = manager.OpenReadWrite(oid))
        {
            var buf = Encoding.UTF8.GetBytes("Hello");
            stream.Write(buf, 0, buf.Length);
            stream.Seek(0, System.IO.SeekOrigin.Begin);
            var buf2 = new byte[buf.Length];
            stream.ReadExactly(buf2, 0, buf2.Length);
            buf.SequenceEqual(buf2).Should().BeTrue();

            stream.Position.Should().Be(5);

            stream.Length.Should().Be(5);

            stream.Seek(-1, System.IO.SeekOrigin.Current);
            stream.ReadByte().Should().Be((int)'o');

            manager.MaxTransferBlockSize = 3;

            stream.Write(buf, 0, buf.Length);
            stream.Seek(-5, System.IO.SeekOrigin.End);
            var buf3 = new byte[100];
            stream.Read(buf3, 0, 100).Should().Be(5);
            buf.SequenceEqual(buf3.Take(5)).Should().BeTrue();

            // Called through Stream so the synchronous override is exercised (the async overload takes a token)
            ((System.IO.Stream)stream).SetLength(43);
            stream.Length.Should().Be(43);
        }

        manager.Unlink(oid);

        transaction.Rollback();
    }
}
