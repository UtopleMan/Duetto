using Duetto.Core.FileSystem;

namespace Duetto.Core.Preview;

public sealed class PreviewLoader(FileSystemRegistry registry)
{
    private const int ReadChunkBytes = 64 * 1024;

    private sealed record LoadedBody(byte[] Bytes, bool IsTruncated);

    public PreviewContent Load(string fullAddress, CancellationToken ct, PreviewLimits? limits = null)
    {
        ct.ThrowIfCancellationRequested();

        var budgets = limits ?? PreviewLimits.Default;
        var (provider, localPath) = registry.Resolve(fullAddress);
        var entry = provider.Stat(localPath)
                    ?? throw new FileNotFoundException("File not found.", fullAddress);

        if (entry.IsDirectory)
            throw new NotSupportedException($"Cannot preview a folder: {fullAddress}");

        using var stream = provider.OpenRead(localPath);
        var head = Read(stream, budgets.SniffBytes, ct);
        var kind = ContentSniffer.Detect(head, entry.SizeBytes, budgets);

        if (kind == PreviewKind.Empty)
            return EmptyContent(entry.SizeBytes);

        if (kind == PreviewKind.Image)
            return ImageContent(stream, head, entry.SizeBytes, budgets, ct);

        var body = ReadBody(stream, head, budgets.TextBudgetBytes, ct);
        return kind switch
        {
            PreviewKind.Text => TextContent(body, entry.SizeBytes),
            PreviewKind.Vector => VectorContent(body, entry.SizeBytes),
            _ => HexContent(body, entry.SizeBytes),
        };
    }

    private static LoadedBody ReadBody(Stream stream, byte[] head, long budgetBytes, CancellationToken ct)
    {
        var budget = (int)Math.Min(budgetBytes, int.MaxValue - 1);
        if (head.Length > budget)
            return new LoadedBody(head[..budget], true);

        var rest = Read(stream, budget - head.Length + 1, ct);
        var all = Concat(head, rest);
        return all.Length > budget
            ? new LoadedBody(all[..budget], true)
            : new LoadedBody(all, false);
    }

    private static PreviewContent ImageContent(
        Stream stream,
        byte[] head,
        long totalBytes,
        PreviewLimits budgets,
        CancellationToken ct)
    {
        var cap = (int)Math.Min(budgets.ImageMaxBytes, int.MaxValue);
        var bytes = Concat(head, Read(stream, cap - head.Length, ct));
        return new PreviewContent
        {
            Kind = PreviewKind.Image,
            Lines = [],
            ImageBytes = bytes,
            EncodingLabel = "",
            TotalBytes = totalBytes,
            LoadedBytes = bytes.LongLength,
            IsTruncated = false,
        };
    }

    private static PreviewContent TextContent(LoadedBody body, long totalBytes)
    {
        var (encoding, label, bomLength) = TextEncodingDetector.Detect(body.Bytes);
        var start = Math.Min(bomLength, body.Bytes.Length);
        var text = encoding.GetString(body.Bytes, start, body.Bytes.Length - start);
        return new PreviewContent
        {
            Kind = PreviewKind.Text,
            Lines = SplitLines(text),
            EncodingLabel = label,
            TotalBytes = totalBytes,
            LoadedBytes = body.Bytes.LongLength,
            IsTruncated = body.IsTruncated,
        };
    }

    private static PreviewContent VectorContent(LoadedBody body, long totalBytes)
    {
        var markup = TextContent(body, totalBytes);
        return body.IsTruncated
            ? markup
            : markup with { Kind = PreviewKind.Vector, ImageBytes = body.Bytes, EncodingLabel = "" };
    }

    private static PreviewContent HexContent(LoadedBody body, long totalBytes) => new()
    {
        Kind = PreviewKind.Hex,
        Lines = HexDump.Format(body.Bytes, 0),
        EncodingLabel = "",
        TotalBytes = totalBytes,
        LoadedBytes = body.Bytes.LongLength,
        IsTruncated = body.IsTruncated,
    };

    private static PreviewContent EmptyContent(long totalBytes) => new()
    {
        Kind = PreviewKind.Empty,
        Lines = [],
        EncodingLabel = "",
        TotalBytes = totalBytes,
        LoadedBytes = 0,
        IsTruncated = false,
    };

    private static IReadOnlyList<string> SplitLines(string text)
    {
        var lines = text.Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            if (lines[index].EndsWith('\r'))
                lines[index] = lines[index][..^1];
        }

        return lines.Length > 1 && lines[^1].Length == 0 ? lines[..^1] : lines;
    }

    private static byte[] Read(Stream stream, int count, CancellationToken ct)
    {
        if (count <= 0)
            return [];

        using var sink = new MemoryStream();
        var chunk = new byte[Math.Min(count, ReadChunkBytes)];
        var total = 0;
        while (total < count)
        {
            ct.ThrowIfCancellationRequested();
            var read = stream.Read(chunk, 0, Math.Min(chunk.Length, count - total));
            if (read == 0)
                break;
            sink.Write(chunk, 0, read);
            total += read;
        }

        return sink.ToArray();
    }

    private static byte[] Concat(byte[] first, byte[] second)
    {
        if (second.Length == 0)
            return first;

        var all = new byte[first.Length + second.Length];
        first.CopyTo(all, 0);
        second.CopyTo(all, first.Length);
        return all;
    }
}
