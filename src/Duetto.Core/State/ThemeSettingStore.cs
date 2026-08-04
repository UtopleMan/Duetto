using System.Text.Json;
using System.Text.Json.Serialization;

namespace Duetto.Core.State;

// The user's theme choice. System follows the OS appearance; Light/Dark are explicit.
public enum AppTheme
{
    System,
    Light,
    Dark,
}

// Persists the theme choice to settings.json in the config dir. Applied on next launch
// (restart-to-apply). Mirrors SessionStore: injectable reader/writer, atomic default write,
// never throws on a missing/corrupt/unknown file — an unreadable setting falls back to System.
public sealed class ThemeSettingStore
{
    private readonly string _path;
    private readonly Func<string, string?> _reader;
    private readonly Action<string, string> _writer;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public ThemeSettingStore(string path)
        : this(path,
               p => File.Exists(p) ? File.ReadAllText(p) : null,
               (p, content) =>
               {
                   var tmp = p + ".tmp";
                   File.WriteAllText(tmp, content);
                   File.Move(tmp, p, overwrite: true);
               })
    { }

    public ThemeSettingStore(string path, Func<string, string?> reader, Action<string, string> writer)
    {
        _path = path;
        _reader = reader;
        _writer = writer;
    }

    public AppTheme Load()
    {
        try
        {
            var content = _reader(_path);
            if (string.IsNullOrWhiteSpace(content))
                return AppTheme.System;

            return JsonSerializer.Deserialize<Model>(content, JsonOptions)?.Theme ?? AppTheme.System;
        }
        catch (JsonException)
        {
            return AppTheme.System;
        }
        catch (IOException)
        {
            return AppTheme.System;
        }
    }

    public void Save(AppTheme theme)
    {
        var content = JsonSerializer.Serialize(new Model { Theme = theme }, JsonOptions);
        _writer(_path, content);
    }

    private sealed class Model
    {
        public AppTheme Theme { get; set; }
    }
}
