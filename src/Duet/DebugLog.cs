namespace Duet;

/// <summary>Diagnostic trace, active only when DUET_LOG points at a file.</summary>
internal static class DebugLog
{
    public static void Write(string message)
    {
        if (Environment.GetEnvironmentVariable("DUET_LOG") is { Length: > 0 } path)
        {
            try
            {
                File.AppendAllText(path, $"{DateTime.Now:HH:mm:ss.fff} {message}\n");
            }
            catch (IOException)
            {
            }
        }
    }
}
