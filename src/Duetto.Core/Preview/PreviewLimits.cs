namespace Duetto.Core.Preview;

public sealed record PreviewLimits
{
    public required long TextBudgetBytes { get; init; }

    public required long ImageMaxBytes { get; init; }

    public required int SniffBytes { get; init; }

    public static PreviewLimits Default { get; } = new()
    {
        TextBudgetBytes = 4L * 1024 * 1024,
        ImageMaxBytes = 64L * 1024 * 1024,
        SniffBytes = 8 * 1024,
    };
}
