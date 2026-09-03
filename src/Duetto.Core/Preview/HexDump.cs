using System.Text;

namespace Duetto.Core.Preview;

public static class HexDump
{
    private const int BytesPerRow = 16;
    private const int BytesPerGroup = 8;

    public static IReadOnlyList<string> Format(ReadOnlySpan<byte> bytes, long startOffset)
    {
        var rows = new List<string>((bytes.Length + BytesPerRow - 1) / BytesPerRow);
        for (var start = 0; start < bytes.Length; start += BytesPerRow)
        {
            var end = Math.Min(start + BytesPerRow, bytes.Length);
            rows.Add(FormatRow(bytes[start..end], startOffset + start));
        }

        return rows;
    }

    private static string FormatRow(ReadOnlySpan<byte> row, long offset)
    {
        var builder = new StringBuilder(80);
        builder.Append(offset.ToString("X8")).Append("  ");
        AppendGroup(builder, row[..Math.Min(BytesPerGroup, row.Length)]);
        builder.Append("  ");
        AppendGroup(builder, row.Length > BytesPerGroup ? row[BytesPerGroup..] : []);
        builder.Append("  |");
        foreach (var value in row)
            builder.Append(IsPrintable(value) ? (char)value : '.');
        builder.Append('|');
        return builder.ToString();
    }

    private static void AppendGroup(StringBuilder builder, ReadOnlySpan<byte> group)
    {
        for (var index = 0; index < BytesPerGroup; index++)
        {
            if (index > 0)
                builder.Append(' ');
            if (index < group.Length)
                builder.Append(group[index].ToString("X2"));
            else
                builder.Append("  ");
        }
    }

    private static bool IsPrintable(byte value) => value is >= 0x20 and <= 0x7E;
}
