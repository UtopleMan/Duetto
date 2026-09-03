using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Duetto.Core.Preview;

namespace Duetto.ViewModels;

internal static class PdfPageBitmap
{
    private static readonly Vector StandardDpi = new(96, 96);

    public static WriteableBitmap From(PdfPage page)
    {
        var bitmap = new WriteableBitmap(
            new PixelSize(page.Width, page.Height),
            StandardDpi,
            PixelFormat.Bgra8888,
            AlphaFormat.Unpremul);

        using var buffer = bitmap.Lock();
        var sourceStride = page.Width * 4;
        for (var row = 0; row < page.Height; row++)
        {
            Marshal.Copy(
                page.Pixels,
                row * sourceStride,
                buffer.Address + (row * buffer.RowBytes),
                sourceStride);
        }

        return bitmap;
    }
}
