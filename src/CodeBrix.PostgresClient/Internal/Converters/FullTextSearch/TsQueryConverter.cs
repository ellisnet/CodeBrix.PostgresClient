using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.PostgresClient.PgSqlTypes;
using static CodeBrix.PostgresClient.PgSqlTypes.PgSqlTsQuery.NodeKind;

// ReSharper disable once CheckNamespace
namespace CodeBrix.PostgresClient.Internal.Converters; //was previously: Npgsql.Internal.Converters;

sealed class TsQueryConverter<T>(Encoding encoding) : PgStreamingConverter<T>
    where T : PgSqlTsQuery
{
    public override T Read(PgReader reader)
        => (T)Read(async: false, reader, CancellationToken.None).GetAwaiter().GetResult();

    public override async ValueTask<T> ReadAsync(PgReader reader, CancellationToken cancellationToken = default)
        => (T)await Read(async: true, reader, cancellationToken).ConfigureAwait(false);

    async ValueTask<PgSqlTsQuery> Read(bool async, PgReader reader, CancellationToken cancellationToken)
    {
        if (reader.ShouldBuffer(sizeof(int)))
            await reader.Buffer(async, sizeof(int), cancellationToken).ConfigureAwait(false);
        var numTokens = reader.ReadInt32();
        if (numTokens == 0)
            return new PgSqlTsQueryEmpty();

        PgSqlTsQuery value = null;
        var nodes = new Stack<(PgSqlTsQuery Node, int Location)>();

        for (var i = 0; i < numTokens; i++)
        {
            if (reader.ShouldBuffer(sizeof(byte)))
                await reader.Buffer(async, sizeof(byte), cancellationToken).ConfigureAwait(false);

            switch (reader.ReadByte())
            {
            case 1: // lexeme
                if (reader.ShouldBuffer(sizeof(byte) + sizeof(byte)))
                    await reader.Buffer(async, sizeof(byte) + sizeof(byte), cancellationToken).ConfigureAwait(false);
                var weight = (PgSqlTsQueryLexeme.Weight)reader.ReadByte();
                var prefix = reader.ReadByte() != 0;

                var str = async
                    ? await reader.ReadNullTerminatedStringAsync(encoding, cancellationToken).ConfigureAwait(false)
                    : reader.ReadNullTerminatedString(encoding);
                InsertInTree(new PgSqlTsQueryLexeme(str, weight, prefix), nodes, ref value);
                continue;

            case 2: // operation
                if (reader.ShouldBuffer(sizeof(byte)))
                    await reader.Buffer(async, sizeof(byte), cancellationToken).ConfigureAwait(false);
                var kind = (PgSqlTsQuery.NodeKind)reader.ReadByte();

                PgSqlTsQuery node;
                switch (kind)
                {
                case Not:
                    node = new PgSqlTsQueryNot(null);
                    InsertInTree(node, nodes, ref value);
                    nodes.Push((node, 0));
                    continue;

                case And:
                    node = new PgSqlTsQueryAnd(null, null);
                    break;
                case Or:
                    node = new PgSqlTsQueryOr(null, null);
                    break;
                case Phrase:
                    if (reader.ShouldBuffer(sizeof(short)))
                        await reader.Buffer(async, sizeof(short), cancellationToken).ConfigureAwait(false);
                    node = new PgSqlTsQueryFollowedBy(null, reader.ReadInt16(), null);
                    break;
                default:
                    throw new UnreachableException(
                        $"Internal PgSql bug: unexpected value {kind} of enum {nameof(PgSqlTsQuery.NodeKind)}. Please file a bug.");
                }

                InsertInTree(node, nodes, ref value);

                nodes.Push((node, 1));
                nodes.Push((node, 2));
                continue;

            case var tokenType:
                throw new UnreachableException(
                    $"Internal PgSql bug: unexpected token type {tokenType} when reading tsquery. Please file a bug.");
            }
        }

        if (nodes.Count != 0)
            throw new UnreachableException("Internal PgSql bug, please report.");

        return value;

        static void InsertInTree(PgSqlTsQuery node, Stack<(PgSqlTsQuery Node, int Location)> nodes, ref PgSqlTsQuery value)
        {
            if (nodes.Count == 0)
                value = node;
            else
            {
                var parent = nodes.Pop();
                switch (parent.Location)
                {
                case 0:
                    ((PgSqlTsQueryNot)parent.Node).Child = node;
                    break;
                case 1:
                    ((PgSqlTsQueryBinOp)parent.Node).Left = node;
                    break;
                case 2:
                    ((PgSqlTsQueryBinOp)parent.Node).Right = node;
                    break;
                default:
                    throw new UnreachableException("Internal PgSql bug, please report.");
                }
            }
        }
    }

    public override Size GetSize(SizeContext context, T value, ref object writeState)
        => value.Kind is Empty
            ? 4
            : 4 + GetNodeLength(value);

    int GetNodeLength(PgSqlTsQuery node)
        => node.Kind switch
        {
            Lexeme when encoding.GetByteCount(((PgSqlTsQueryLexeme)node).Text) is var strLen
                => strLen > 2046
                    ? throw new InvalidCastException("Lexeme text too long. Must be at most 2046 encoded bytes.")
                    : 4 + strLen,
            And or Or => 2 + GetNodeLength(((PgSqlTsQueryBinOp)node).Left) + GetNodeLength(((PgSqlTsQueryBinOp)node).Right),
            Not => 2 + GetNodeLength(((PgSqlTsQueryNot)node).Child),
            Empty => throw new InvalidOperationException("Empty tsquery nodes must be top-level"),

            // 2 additional bytes for uint16 phrase operator "distance" field.
            Phrase => 4 + GetNodeLength(((PgSqlTsQueryBinOp)node).Left) + GetNodeLength(((PgSqlTsQueryBinOp)node).Right),

            _ => throw new UnreachableException(
                $"Internal PgSql bug: unexpected value {node.Kind} of enum {nameof(PgSqlTsQuery.NodeKind)}. Please file a bug.")
        };

    public override void Write(PgWriter writer, T value)
        => Write(async: false, writer, value, CancellationToken.None).GetAwaiter().GetResult();

    public override ValueTask WriteAsync(PgWriter writer, T value, CancellationToken cancellationToken = default)
        => Write(async: true, writer, value, cancellationToken);

    async ValueTask Write(bool async, PgWriter writer, PgSqlTsQuery value, CancellationToken cancellationToken)
    {
        var numTokens = GetTokenCount(value);

        if (writer.ShouldFlush(sizeof(int)))
            await writer.Flush(async, cancellationToken).ConfigureAwait(false);
        writer.WriteInt32(numTokens);

        if (numTokens is 0)
            return;

        await WriteCore(value).ConfigureAwait(false);

        async Task WriteCore(PgSqlTsQuery node)
        {
            if (writer.ShouldFlush(sizeof(byte)))
                await writer.Flush(async, cancellationToken).ConfigureAwait(false);
            writer.WriteByte(node.Kind is Lexeme ? (byte)1 : (byte)2);

            if (node.Kind is Lexeme)
            {
                var lexemeNode = (PgSqlTsQueryLexeme)node;

                if (writer.ShouldFlush(sizeof(byte) + sizeof(byte)))
                    await writer.Flush(async, cancellationToken).ConfigureAwait(false);

                writer.WriteByte((byte)lexemeNode.Weights);
                writer.WriteByte(lexemeNode.IsPrefixSearch ? (byte)1 : (byte)0);

                if (async)
                    await writer.WriteCharsAsync(lexemeNode.Text.AsMemory(), encoding, cancellationToken).ConfigureAwait(false);
                else
                    writer.WriteChars(lexemeNode.Text.AsMemory().Span, encoding);

                if (writer.ShouldFlush(sizeof(byte)))
                    await writer.Flush(async, cancellationToken).ConfigureAwait(false);

                writer.WriteByte(0);
                return;
            }

            writer.WriteByte((byte)node.Kind);

            switch (node.Kind)
            {
            case Not:
                await WriteCore(((PgSqlTsQueryNot)node).Child).ConfigureAwait(false);
                return;
            case Phrase:
                writer.WriteInt16(((PgSqlTsQueryFollowedBy)node).Distance);
                break;
            }

            await WriteCore(((PgSqlTsQueryBinOp)node).Right).ConfigureAwait(false);
            await WriteCore(((PgSqlTsQueryBinOp)node).Left).ConfigureAwait(false);
        }
    }

    int GetTokenCount(PgSqlTsQuery node)
        => node.Kind switch
        {
            Lexeme => 1,
            And or Or or Phrase => 1 + GetTokenCount(((PgSqlTsQueryBinOp)node).Left) + GetTokenCount(((PgSqlTsQueryBinOp)node).Right),
            Not => 1 + GetTokenCount(((PgSqlTsQueryNot)node).Child),
            Empty => 0,

            _ => throw new UnreachableException(
                $"Internal PgSql bug: unexpected value {node.Kind} of enum {nameof(PgSqlTsQuery.NodeKind)}. Please file a bug.")
        };
}
