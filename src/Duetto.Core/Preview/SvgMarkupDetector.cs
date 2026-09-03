namespace Duetto.Core.Preview;

internal static class SvgMarkupDetector
{
    private static ReadOnlySpan<byte> RootTag => "<svg"u8;
    private static ReadOnlySpan<byte> CommentStart => "<!--"u8;
    private static ReadOnlySpan<byte> CommentEnd => "-->"u8;
    private static ReadOnlySpan<byte> DeclarationStart => "<?"u8;
    private static ReadOnlySpan<byte> DeclarationEnd => "?>"u8;
    private static ReadOnlySpan<byte> DoctypeStart => "<!"u8;
    private static ReadOnlySpan<byte> DoctypeEnd => ">"u8;

    public static bool LooksLikeSvg(ReadOnlySpan<byte> head)
    {
        var rest = head;
        while (true)
        {
            rest = TrimLeadingWhitespace(rest);
            if (rest.StartsWith(RootTag))
                return IsTagNameComplete(rest[RootTag.Length..]);

            rest = SkipPrologueNode(rest);
            if (rest.IsEmpty)
                return false;
        }
    }

    private static ReadOnlySpan<byte> SkipPrologueNode(ReadOnlySpan<byte> head)
    {
        if (head.StartsWith(CommentStart))
            return SkipPast(head[CommentStart.Length..], CommentEnd);

        if (head.StartsWith(DeclarationStart))
            return SkipPast(head[DeclarationStart.Length..], DeclarationEnd);

        if (head.StartsWith(DoctypeStart))
            return SkipPast(head[DoctypeStart.Length..], DoctypeEnd);

        return [];
    }

    private static ReadOnlySpan<byte> SkipPast(ReadOnlySpan<byte> head, ReadOnlySpan<byte> terminator)
    {
        var end = head.IndexOf(terminator);
        return end < 0 ? [] : head[(end + terminator.Length)..];
    }

    private static bool IsTagNameComplete(ReadOnlySpan<byte> afterRootTag) =>
        afterRootTag.IsEmpty
        || afterRootTag[0] is (byte)'>' or (byte)'/'
        || IsWhitespace(afterRootTag[0]);

    private static ReadOnlySpan<byte> TrimLeadingWhitespace(ReadOnlySpan<byte> head)
    {
        var index = 0;
        while (index < head.Length && IsWhitespace(head[index]))
            index++;

        return head[index..];
    }

    private static bool IsWhitespace(byte value) =>
        value is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n';
}
