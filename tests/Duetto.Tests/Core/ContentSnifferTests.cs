using System.Text;
using Duetto.Core.Preview;

namespace Duetto.Tests.Core;

public class ContentSnifferTests
{
    private static readonly PreviewLimits Limits = PreviewLimits.Default;

    private static readonly byte[] PngHead =
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D];

    [Fact]
    public void Empty_head_is_empty_kind() =>
        Assert.Equal(PreviewKind.Empty, ContentSniffer.Detect([], 0, Limits));

    [Fact]
    public void Plain_ascii_is_text() =>
        Assert.Equal(PreviewKind.Text, ContentSniffer.Detect("hello world\n"u8, 12, Limits));

    [Fact]
    public void Multibyte_utf8_is_text() =>
        Assert.Equal(PreviewKind.Text, ContentSniffer.Detect("café — naïve ☕\n"u8, 20, Limits));

    [Fact]
    public void Truncated_multibyte_sequence_is_still_text()
    {
        var full = Encoding.UTF8.GetBytes("aaaa☕");
        var cut = full[..^1];

        Assert.Equal(PreviewKind.Text, ContentSniffer.Detect(cut, cut.Length, Limits));
    }

    [Fact]
    public void Invalid_utf8_is_hex() =>
        Assert.Equal(PreviewKind.Hex, ContentSniffer.Detect([0x61, 0xC3, 0x28, 0x62], 4, Limits));

    [Fact]
    public void Embedded_nul_is_hex() =>
        Assert.Equal(PreviewKind.Hex, ContentSniffer.Detect([0x61, 0x62, 0x00, 0x63], 4, Limits));

    [Fact]
    public void Utf16_le_bom_is_text() =>
        Assert.Equal(PreviewKind.Text, ContentSniffer.Detect([0xFF, 0xFE, 0x61, 0x00], 4, Limits));

    [Fact]
    public void Utf16_be_bom_is_text() =>
        Assert.Equal(PreviewKind.Text, ContentSniffer.Detect([0xFE, 0xFF, 0x00, 0x61], 4, Limits));

    [Fact]
    public void Png_under_the_cap_is_image() =>
        Assert.Equal(PreviewKind.Image, ContentSniffer.Detect(PngHead, 4096, Limits));

    [Fact]
    public void Png_over_the_cap_is_hex() =>
        Assert.Equal(PreviewKind.Hex, ContentSniffer.Detect(PngHead, Limits.ImageMaxBytes + 1, Limits));

    [Fact]
    public void Jpeg_magic_is_image() =>
        Assert.Equal(PreviewKind.Image, ContentSniffer.Detect([0xFF, 0xD8, 0xFF, 0xE0], 900, Limits));

    [Fact]
    public void Gif_magic_is_image()
    {
        byte[] head = [0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 0x10, 0x00];

        Assert.Equal(PreviewKind.Image, ContentSniffer.Detect(head, 900, Limits));
    }

    [Fact]
    public void Webp_magic_is_image() =>
        Assert.Equal(PreviewKind.Image, ContentSniffer.Detect("RIFF____WEBPVP8 "u8, 900, Limits));

    [Fact]
    public void Bmp_magic_is_image_when_the_size_field_matches()
    {
        byte[] head = [0x42, 0x4D, 0x0E, 0x00, 0x00, 0x00, 0x00, 0x00];

        Assert.Equal(PreviewKind.Image, ContentSniffer.Detect(head, 14, Limits));
    }

    [Fact]
    public void Text_starting_with_bm_is_not_mistaken_for_bmp() =>
        Assert.Equal(PreviewKind.Text, ContentSniffer.Detect("BMW is a car\n"u8, 13, Limits));
}
