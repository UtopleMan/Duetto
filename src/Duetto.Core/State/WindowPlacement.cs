namespace Duetto.Core.State;

public readonly record struct ScreenBounds(int X, int Y, int Width, int Height);

public sealed record WindowPlacement(int X, int Y, double Width, double Height, bool Maximized)
{
    public bool IsVisibleOn(IReadOnlyList<ScreenBounds> screens) =>
        screens.Any(s => X >= s.X && Y >= s.Y && X < s.X + s.Width && Y < s.Y + s.Height);
}
