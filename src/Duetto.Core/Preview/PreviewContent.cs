namespace Duetto.Core.Preview;

public sealed record PreviewContent
{
    public required PreviewKind Kind { get; init; }

    public required IReadOnlyList<string> Lines { get; init; }

    public byte[]? ImageBytes { get; init; }

    public required string EncodingLabel { get; init; }

    public required long TotalBytes { get; init; }

    public required long LoadedBytes { get; init; }

    public required bool IsTruncated { get; init; }
}
