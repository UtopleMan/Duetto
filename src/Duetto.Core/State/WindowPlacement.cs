namespace Duetto.Core.State;

/// <summary>The bounds of a connected display, in physical pixels.</summary>
public readonly record struct ScreenBounds(int X, int Y, int Width, int Height);

/// <summary>
/// A persisted window placement: top-left position and size in the window's own units
/// (position in physical pixels, size in device-independent pixels), plus whether the
/// window was maximized.
/// </summary>
public sealed record WindowPlacement(int X, int Y, double Width, double Height, bool Maximized)
{
    /// <summary>
    /// True when the saved top-left corner still lands inside one of the connected screens.
    /// A window whose corner is off every screen (e.g. after a monitor is unplugged) would
    /// open out of reach, so the caller falls back to a default placement instead.
    /// </summary>
    public bool IsVisibleOn(IReadOnlyList<ScreenBounds> screens) =>
        screens.Any(s => X >= s.X && Y >= s.Y && X < s.X + s.Width && Y < s.Y + s.Height);
}
