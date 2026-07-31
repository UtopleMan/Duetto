namespace Duetto.Core.State;

/// <summary>The directories the two panes were showing when the app last closed.</summary>
public sealed record SessionState(string LeftPath, string RightPath);
