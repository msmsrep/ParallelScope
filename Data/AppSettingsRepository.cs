using System.IO;
using System.Text.Json;
using ParallelScope.Utilities;

namespace ParallelScope.Data;

/// <summary>アプリ設定(AppSettings)をJSONファイルとして永続化するリポジトリ。</summary>
public class AppSettingsRepository
{
    private readonly string _settingsPath;

    public AppSettingsRepository()
    {
        var appDataDir = AppDataPathProvider.GetOrCreateAppDataDirectory();
        _settingsPath = Path.Combine(appDataDir, "settings.json");
    }

    /// <summary>設定ファイルを読み込む。存在しない、または読み込みに失敗した場合はデフォルト設定を返す。</summary>
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
            // 設定ファイルが破損・アクセス不可などの場合はデフォルト設定にフォールバックする（元の挙動を維持）
            return new AppSettings();
        }
    }

    /// <summary>設定をJSONファイルに書き出す。</summary>
    public void Save(AppSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        File.WriteAllText(_settingsPath, json);
    }
}
