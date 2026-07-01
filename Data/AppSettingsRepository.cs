using System.IO;
using System.Text.Json;
using ParallelScope.Common;

namespace ParallelScope.Data;

public class AppSettingsRepository
{
    private readonly string _settingsPath;

    public AppSettingsRepository()
    {
        var appDataDir = AppDataLocation.GetAppDataDirectory();
        _settingsPath = Path.Combine(appDataDir, "settings.json");
    }

    public AppSettings Load()
    {
        if (!File.Exists(_settingsPath))
        {
            return new AppSettings();
        }

        try
        {
            var json = File.ReadAllText(_settingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        File.WriteAllText(_settingsPath, json);
    }
}
