using System.IO;
using System.Text.Json;

namespace ParallelScope.Data;

public class AppSettingsRepository
{
    private readonly string _settingsPath;

    public AppSettingsRepository()
    {
        string appDataDir;

        bool isMsix = Environment.ProcessPath?.Contains(@"\WindowsApps\") ?? false;

        if (isMsix)
        {
            // MSIX の LocalState
            appDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Packages",
                "msmsrep.ParallelScope_77t1an0ygyrva",
                "LocalState");
        }
        else
        {
            // 通常のローカルフォルダ
            appDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ParallelScope");
        }

        Directory.CreateDirectory(appDataDir);

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
