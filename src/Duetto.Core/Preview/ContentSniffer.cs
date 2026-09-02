using System.Buffers;
using System.Buffers.Binary;
using System.Text.Unicode;

namespace Duetto.Core.Preview;

public static class ContentSniffer
{
    private static ReadOnlySpan<byte> PngMagic => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static ReadOnlySpan<byte> JpegMagic => [0xFF, 0xD8, 0xFF];
    private static ReadOnlySpan<byte> Gif87Magic => "GIF87a"u8;
    private static ReadOnlySpan<byte> Gif89Magic => "GIF89a"u8;
    private static ReadOnlySpan<byte> BmpMagic => "BM"u8;
    private static ReadOnlySpan<byte> RiffMagic => "RIFF"u8;
    private static ReadOnlySpan<byte> WebpMagic => "WEBP"u8;

    public static PreviewKind Detect(ReadOnlySpan<byte> head, long totalBytes, PreviewLimits limits)
    {
        if (head.IsEmpty)
            return PreviewKind.Empty;

        if (LooksLikeImage(head, totalBytes))
            return totalBytes <= limits.ImageMaxBytes ? PreviewKind.Image : PreviewKind.Hex;

        if (TextEncodingDetector.Detect(head).BomLength > 0)
            return PreviewKind.Text;

        if (head.IndexOf((byte)0) >= 0)
            return PreviewKind.Hex;

        return IsValidUtf8(head) ? PreviewKind.Text : PreviewKind.Hex;
    }

    private static bool LooksLikeImage(ReadOnlySpan<byte> head, long totalBytes) =>
        head.StartsWith(PngMagic)
        || head.StartsWith(JpegMagic)
        || head.StartsWith(Gif87Magic)
        || head.StartsWith(Gif89Magic)
        || IsBmp(head, totalBytes)
        || IsWebp(head);

    private static bool IsBmp(ReadOnlySpan<byte> head, long totalBytes) =>
        head.Length >= 6
        && head.StartsWith(BmpMagic)
        && BinaryPrimitives.ReadUInt32LittleEndian(head[2..6]) == totalBytes;

    private static bool IsWebp(ReadOnlySpan<byte> head) =>
        head.Length >= 12
        && head.StartsWith(RiffMagic)
        && head[8..12].SequenceEqual(WebpMagic);

    private static bool IsValidUtf8(ReadOnlySpan<byte> head)
    {
        var buffer = ArrayPool<char>.Shared.Rent(head.Length);
        try
        {
            var status = Utf8.ToUtf16(
                head,
                buffer,
                out _,
                out _,
                replaceInvalidSequences: false,
                isFinalBlock: false);
            return status is OperationStatus.Done or OperationStatus.NeedMoreData;
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buffer);
        }
    }
}
