using System;
using System.Net;
using CodeBrix.PostgresClient.PgSqlTypes;
using SilverAssertions;
using Xunit;

namespace CodeBrix.PostgresClient.Tests; //was previously: Npgsql.Tests;

/// <summary>
/// Tests CodeBrix.PostgresClient.PgSqlTypes.* independent of a database
/// </summary>
public class TypesTests
{
    [Fact]
    public void ts_vector()
    {
        PgSqlTsVector vec;

        vec = PgSqlTsVector.Parse("a");
        vec.ToString().Should().Be("'a'");

        vec = PgSqlTsVector.Parse("a ");
        vec.ToString().Should().Be("'a'");

        vec = PgSqlTsVector.Parse("a:1A");
        vec.ToString().Should().Be("'a':1A");

        vec = PgSqlTsVector.Parse(@"\abc\def:1a ");
        vec.ToString().Should().Be("'abcdef':1A");

        vec = PgSqlTsVector.Parse(@"abc:3A 'abc' abc:4B 'hello''yo' 'meh\'\\':5");
        vec.ToString().Should().Be(@"'abc':3A,4B 'hello''yo' 'meh''\\':5");

        vec = PgSqlTsVector.Parse(" a:12345C  a:24D a:25B b c d 1 2 a:25A,26B,27,28");
        vec.ToString().Should().Be("'1' '2' 'a':24,25A,26B,27,28,12345C 'b' 'c' 'd'");
    }

    [Fact]
    public void ts_query()
    {
        PgSqlTsQuery query;

        query = new PgSqlTsQueryLexeme("a", PgSqlTsQueryLexeme.Weight.A | PgSqlTsQueryLexeme.Weight.B);
        query = new PgSqlTsQueryOr(query, query);
        query = new PgSqlTsQueryOr(query, query);

        var str = query.ToString();

        query = PgSqlTsQuery.Parse("a & b | c");
        query.ToString().Should().Be("'a' & 'b' | 'c'");

        query = PgSqlTsQuery.Parse("'a''':*ab&d:d&!c");
        query.ToString().Should().Be("'a''':*AB & 'd':D & !'c'");

        query = PgSqlTsQuery.Parse("(a & !(c | d)) & (!!a&b) | c | d | e");
        query.ToString().Should().Be("( ( 'a' & !( 'c' | 'd' ) & !( !'a' ) & 'b' | 'c' ) | 'd' ) | 'e'");
        PgSqlTsQuery.Parse(query.ToString()).ToString().Should().Be(query.ToString());

        query = PgSqlTsQuery.Parse("(((a:*)))");
        query.ToString().Should().Be("'a':*");

        query = PgSqlTsQuery.Parse(@"'a\\b''cde'");
        ((PgSqlTsQueryLexeme)query).Text.Should().Be(@"a\b'cde");
        query.ToString().Should().Be(@"'a\\b''cde'");

        query = PgSqlTsQuery.Parse(@"a <-> b");
        query.ToString().Should().Be("'a' <-> 'b'");

        query = PgSqlTsQuery.Parse("((a & b) <5> c) <-> !d <0> e");
        query.ToString().Should().Be("( ( 'a' & 'b' <5> 'c' ) <-> !'d' ) <0> 'e'");

        Assert.Throws<FormatException>(() => PgSqlTsQuery.Parse("a b c & &"));
        Assert.Throws<FormatException>(() => PgSqlTsQuery.Parse("&"));
        Assert.Throws<FormatException>(() => PgSqlTsQuery.Parse("|"));
        Assert.Throws<FormatException>(() => PgSqlTsQuery.Parse("!"));
        Assert.Throws<FormatException>(() => PgSqlTsQuery.Parse("("));
        Assert.Throws<FormatException>(() => PgSqlTsQuery.Parse(")"));
        Assert.Throws<FormatException>(() => PgSqlTsQuery.Parse("()"));
        Assert.Throws<FormatException>(() => PgSqlTsQuery.Parse("<"));
        Assert.Throws<FormatException>(() => PgSqlTsQuery.Parse("<-"));
        Assert.Throws<FormatException>(() => PgSqlTsQuery.Parse("<->"));
        Assert.Throws<FormatException>(() => PgSqlTsQuery.Parse("a <->"));
        Assert.Throws<FormatException>(() => PgSqlTsQuery.Parse("<>"));
        Assert.Throws<FormatException>(() => PgSqlTsQuery.Parse("a <a> b"));
        Assert.Throws<FormatException>(() => PgSqlTsQuery.Parse("a <-1> b"));
    }

    [Fact]
    public void ts_vector_empty()
    {
        PgSqlTsVector.Empty.Should().BeEmpty();
        PgSqlTsVector.Empty.ToString().Should().BeEmpty();
    }

    [Fact]
    public void ts_query_equatibility()
    {
        //Debugger.Launch();
        AreEqual(
            new PgSqlTsQueryLexeme("lexeme"),
            new PgSqlTsQueryLexeme("lexeme"));

        AreEqual(
            new PgSqlTsQueryLexeme("lexeme", PgSqlTsQueryLexeme.Weight.A | PgSqlTsQueryLexeme.Weight.B),
            new PgSqlTsQueryLexeme("lexeme", PgSqlTsQueryLexeme.Weight.A | PgSqlTsQueryLexeme.Weight.B));

        AreEqual(
            new PgSqlTsQueryLexeme("lexeme", PgSqlTsQueryLexeme.Weight.A | PgSqlTsQueryLexeme.Weight.B, true),
            new PgSqlTsQueryLexeme("lexeme", PgSqlTsQueryLexeme.Weight.A | PgSqlTsQueryLexeme.Weight.B, true));

        AreEqual(
            new PgSqlTsQueryNot(new PgSqlTsQueryLexeme("not")),
            new PgSqlTsQueryNot(new PgSqlTsQueryLexeme("not")));

        AreEqual(
            new PgSqlTsQueryAnd(new PgSqlTsQueryLexeme("left"), new PgSqlTsQueryLexeme("right")),
            new PgSqlTsQueryAnd(new PgSqlTsQueryLexeme("left"), new PgSqlTsQueryLexeme("right")));

        AreEqual(
            new PgSqlTsQueryOr(new PgSqlTsQueryLexeme("left"), new PgSqlTsQueryLexeme("right")),
            new PgSqlTsQueryOr(new PgSqlTsQueryLexeme("left"), new PgSqlTsQueryLexeme("right")));

        AreEqual(
            new PgSqlTsQueryFollowedBy(new PgSqlTsQueryLexeme("left"), 0, new PgSqlTsQueryLexeme("right")),
            new PgSqlTsQueryFollowedBy(new PgSqlTsQueryLexeme("left"), 0, new PgSqlTsQueryLexeme("right")));

        AreEqual(
            new PgSqlTsQueryFollowedBy(new PgSqlTsQueryLexeme("left"), 1, new PgSqlTsQueryLexeme("right")),
            new PgSqlTsQueryFollowedBy(new PgSqlTsQueryLexeme("left"), 1, new PgSqlTsQueryLexeme("right")));

        AreEqual(
            new PgSqlTsQueryEmpty(),
            new PgSqlTsQueryEmpty());

        AreNotEqual(
            new PgSqlTsQueryLexeme("lexeme a"),
            new PgSqlTsQueryLexeme("lexeme b"));

        AreNotEqual(
            new PgSqlTsQueryLexeme("lexeme", PgSqlTsQueryLexeme.Weight.A | PgSqlTsQueryLexeme.Weight.D),
            new PgSqlTsQueryLexeme("lexeme", PgSqlTsQueryLexeme.Weight.A | PgSqlTsQueryLexeme.Weight.B));

        AreNotEqual(
            new PgSqlTsQueryLexeme("lexeme", PgSqlTsQueryLexeme.Weight.A | PgSqlTsQueryLexeme.Weight.B, true),
            new PgSqlTsQueryLexeme("lexeme", PgSqlTsQueryLexeme.Weight.A | PgSqlTsQueryLexeme.Weight.B, false));

        AreNotEqual(
            new PgSqlTsQueryNot(new PgSqlTsQueryLexeme("not")),
            new PgSqlTsQueryNot(new PgSqlTsQueryLexeme("ton")));

        AreNotEqual(
            new PgSqlTsQueryAnd(new PgSqlTsQueryLexeme("right"), new PgSqlTsQueryLexeme("left")),
            new PgSqlTsQueryAnd(new PgSqlTsQueryLexeme("left"), new PgSqlTsQueryLexeme("right")));

        AreNotEqual(
            new PgSqlTsQueryOr(new PgSqlTsQueryLexeme("right"), new PgSqlTsQueryLexeme("left")),
            new PgSqlTsQueryOr(new PgSqlTsQueryLexeme("left"), new PgSqlTsQueryLexeme("right")));

        AreNotEqual(
            new PgSqlTsQueryFollowedBy(new PgSqlTsQueryLexeme("right"), 0, new PgSqlTsQueryLexeme("left")),
            new PgSqlTsQueryFollowedBy(new PgSqlTsQueryLexeme("left"), 0, new PgSqlTsQueryLexeme("right")));

        AreNotEqual(
            new PgSqlTsQueryFollowedBy(new PgSqlTsQueryLexeme("left"), 0, new PgSqlTsQueryLexeme("right")),
            new PgSqlTsQueryFollowedBy(new PgSqlTsQueryLexeme("left"), 1, new PgSqlTsQueryLexeme("right")));

        void AreEqual(PgSqlTsQuery left, PgSqlTsQuery right)
        {
            (left == right).Should().BeTrue();
            (left != right).Should().BeFalse();
            right.Should().Be(left);
            right.GetHashCode().Should().Be(left.GetHashCode());
        }

        void AreNotEqual(PgSqlTsQuery left, PgSqlTsQuery right)
        {
            (left == right).Should().BeFalse();
            (left != right).Should().BeTrue();
            right.Should().NotBe(left);
            right.GetHashCode().Should().NotBe(left.GetHashCode());
        }
    }

    [Fact]
    public void ts_query_operator_precedence()
    {
        //Arrange
        var query = PgSqlTsQuery.Parse("!a <-> b & c | d & e");
        //Act
        var expectedGrouping = PgSqlTsQuery.Parse("((!(a) <-> b) & c) | (d & e)");
        //Assert
        query.ToString().Should().Be(expectedGrouping.ToString());
    }

    [Fact]
    public void PgSqlPath_empty()
        => new PgSqlPath { new(1, 2) }.Should().Equal(new PgSqlPath(new PgSqlPoint(1, 2)));

    [Fact]
    public void PgSqlPolygon_empty()
        => new PgSqlPolygon { new(1, 2) }.Should().Equal(new PgSqlPolygon(new PgSqlPoint(1, 2)));

    [Fact]
    public void PgSqlPath_default()
    {
        //Arrange
        PgSqlPath defaultPath = default;
        //Assert
        defaultPath.Equals([new(1, 2)]).Should().BeFalse();
    }

    [Fact]
    public void PgSqlPolygon_default()
    {
        //Arrange
        PgSqlPolygon defaultPolygon = default;
        //Assert
        defaultPolygon.Equals([new(1, 2)]).Should().BeFalse();
    }

    [Fact]
    public void bug_1011018()
    {
        var p = new PgSqlParameter();
        p.PgSqlDbType = PgSqlDbType.Time;
        p.Value = DateTime.Now;
        var o = p.Value;
    }

    [Fact]
    [IssueLink("https://github.com/npgsql/npgsql/issues/750")]
    public void PgSqlInet()
    {
        //Arrange
        var v = new PgSqlInet(IPAddress.Parse("2001:1db8:85a3:1142:1000:8a2e:1370:7334"), 32);
        //Assert
        v.ToString().Should().Be("2001:1db8:85a3:1142:1000:8a2e:1370:7334/32");
    }

    [Fact]
    public void PgSqlInet_parse_ipv4()
    {
        //Arrange
        var ipv4 = new PgSqlInet("192.168.1.1/8");
        //Assert
        ipv4.Address.Should().Be(IPAddress.Parse("192.168.1.1"));
        ipv4.Netmask.Should().Be(8);

        ipv4 = new PgSqlInet("192.168.1.1/32");
        ipv4.Address.Should().Be(IPAddress.Parse("192.168.1.1"));
        ipv4.Netmask.Should().Be(32);
    }

    [Fact]
    [IssueLink("https://github.com/npgsql/npgsql/issues/5638")]
    public void PgSqlInet_parse_ipv6()
    {
        //Arrange
        var ipv6 = new PgSqlInet("2001:0000:130F:0000:0000:09C0:876A:130B/32");
        //Assert
        ipv6.Address.Should().Be(IPAddress.Parse("2001:0000:130F:0000:0000:09C0:876A:130B"));
        ipv6.Netmask.Should().Be(32);

        ipv6 = new PgSqlInet("2001:0000:130F:0000:0000:09C0:876A:130B");
        ipv6.Address.Should().Be(IPAddress.Parse("2001:0000:130F:0000:0000:09C0:876A:130B"));
        ipv6.Netmask.Should().Be(128);
    }

    [Fact]
    public void PgSqlInet_ToString_ipv4()
    {
        new PgSqlInet("192.168.1.1/8").ToString().Should().Be("192.168.1.1/8");
        new PgSqlInet("192.168.1.1/32").ToString().Should().Be("192.168.1.1");
    }

    [Fact]
    public void PgSqlInet_ToString_ipv6()
    {
        new PgSqlInet("2001:0:130f::9c0:876a:130b/32").ToString().Should().Be("2001:0:130f::9c0:876a:130b/32");
        new PgSqlInet("2001:0:130f::9c0:876a:130b/128").ToString().Should().Be("2001:0:130f::9c0:876a:130b");
    }
}
