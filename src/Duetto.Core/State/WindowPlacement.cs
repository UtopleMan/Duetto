namespace Duetto.Core.State;

// Bounds in physical pixels.
public readonly record struct ScreenBounds(int X, int Y, int Width, int Height);

// Position is in physical pixels, size in device-independent pixels.
public sealed record WindowPlacement(int X, int Y, double Width, double Height, bool Maximized)
{
    // A corner off every screen (e.g. after a monitor is unplugged) would open out of reach,
    // so the caller falls back to a default placement instead.
    public bool IsVisibleOn(IReadOnlyList<ScreenBounds> screens) =>
        screens.Any(s => X >= s.X && Y >= s.Y && X < s.X + s.Width && Y < s.Y + s.Height);
}
