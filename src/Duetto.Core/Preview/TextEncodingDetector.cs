using System.Text;

namespace Duetto.Core.Preview;

public static class TextEncodingDetector
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private static ReadOnlySpan<byte> Utf8Bom => [0xEF, 0xBB, 0xBF];
    private static ReadOnlySpan<byte> Utf16LeBom => [0xFF, 0xFE];
    private static ReadOnlySpan<byte> Utf16BeBom => [0xFE, 0xFF];

    public static (Encoding Encoding, string Label, int BomLength) Detect(ReadOnlySpan<byte> head)
    {
        if (head.StartsWith(Utf8Bom))
            return (Utf8NoBom, "UTF-8 (BOM)", Utf8Bom.Length);

        if (head.StartsWith(Utf16LeBom))
            return (Encoding.Unicode, "UTF-16 LE", Utf16LeBom.Length);

        if (head.StartsWith(Utf16BeBom))
            return (Encoding.BigEndianUnicode, "UTF-16 BE", Utf16BeBom.Length);

        return (Utf8NoBom, "UTF-8", 0);
    }
}
