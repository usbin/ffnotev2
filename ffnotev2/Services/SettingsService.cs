using System.IO;
using System.Text.Json;
using ffnotev2.Models;

namespace ffnotev2.Services;

public class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _path;

    public AppSettings Settings { get; private set; }

    public event EventHandler? SettingsChanged;

    public SettingsService(string dataDirectory)
    {
        ArgumentNullException.ThrowIfNull(dataDirectory);
        _path = Path.Combine(dataDirectory, "settings.json");
        Settings = Load();
    }

    private AppSettings Load()
    {
        if (!File.Exists(_path)) return new AppSettings();

        try
        {
            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch
        {
            // 손상된 파일이면 기본값으로
            return new AppSettings();
        }
    }

    public void Save()
    {
        var json = JsonSerializer.Serialize(Settings, JsonOptions);
        File.WriteAllText(_path, json);
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }
}
