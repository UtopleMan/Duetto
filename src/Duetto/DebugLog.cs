namespace Duetto;

/// <summary>Diagnostic trace, active only when DUETTO_LOG points at a file.</summary>
internal static class DebugLog
{
    public static void Write(string message)
    {
        if (Environment.GetEnvironmentVariable("DUETTO_LOG") is { Length: > 0 } path)
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
