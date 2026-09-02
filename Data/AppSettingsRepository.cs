using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using ParallelScope.Utilities;

namespace ParallelScope.Data;

/// <summary>アプリ設定(AppSettings)をJSONファイルとして永続化するリポジトリ。</summary>
public class AppSettingsRepository
{
    private readonly string _settingsPath;

    /// <param name="settingsDirectory">
    /// settings.json を置くフォルダ。null（通常の起動時）ならアプリデータフォルダを使う。
    /// テストから一時フォルダを指定し、実際の設定ファイルを壊さずに動かすための引数。
    /// </param>
    public AppSettingsRepository(string? settingsDirectory = null)
    {
        var appDataDir = settingsDirectory ?? AppDataPathProvider.GetOrCreateAppDataDirectory();
        _settingsPath = Path.Combine(appDataDir, "settings.json");
    }

    // 項目ごとの読み直しで使う。書き出しは既定の名前（プロパティ名そのまま）なので、
    // 大文字小文字だけは緩めて拾う
    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>設定ファイルを読み込む。存在しない、または読み込みに失敗した場合はデフォルト設定を返す。</summary>
    public AppSettings Load()
    {
        if (!File.Exists(_settingsPath))
        {
            return new AppSettings();
        }

        string json;
        try
        {
            json = File.ReadAllText(_settingsPath);
        }
        catch
        {
            // アクセスできない場合はデフォルト設定で続行する（元の挙動を維持）
            return new AppSettings();
        }

        try
        {
            return JsonSerializer.Deserialize<AppSettings>(json, ReadOptions) ?? new AppSettings();
        }
        catch (JsonException)
        {
            // 1項目でも型が合わないと丸ごとデフォルトに落ちてしまい、ルートフォルダの設定まで消えたように見える。
            // JSONとして読める限りは項目ごとに拾い直し、壊れている項目だけを捨てる
            return LoadPerProperty(json);
        }
    }

    /// <summary>
    /// 設定を項目ごとに読み直す。読めなかった項目だけを既定値のままにする。
    /// JSON自体が壊れていて読めない場合は、原因を追えるようファイルを退避してから既定値を返す。
    /// </summary>
    private AppSettings LoadPerProperty(string json)
    {
        JsonObject? root;
        try
        {
            root = JsonNode.Parse(json) as JsonObject;
        }
        catch (JsonException)
        {
            root = null;
        }

        var settings = new AppSettings();
        if (root is null)
        {
            BackUpBrokenFile();
            return settings;
        }

        foreach (var property in typeof(AppSettings).GetProperties())
        {
            if (!property.CanWrite)
            {
                continue;
            }

            var node = root.FirstOrDefault(pair =>
                string.Equals(pair.Key, property.Name, StringComparison.OrdinalIgnoreCase)).Value;
            if (node is null)
            {
                continue;
            }

            try
            {
                var value = node.Deserialize(property.PropertyType, ReadOptions);
                if (value is not null)
                {
                    property.SetValue(settings, value);
                }
            }
            catch (JsonException)
            {
                // この項目だけ既定値のままにして、他の設定は残す
            }
        }

        return settings;
    }

    /// <summary>読めなかった settings.json を settings.broken.json へ退避する（上書きで1世代だけ残す）。</summary>
    private void BackUpBrokenFile()
    {
        try
        {
            var brokenPath = Path.Combine(
                Path.GetDirectoryName(_settingsPath)!, "settings.broken.json");
            File.Copy(_settingsPath, brokenPath, true);
        }
        catch
        {
            // 退避できなくても起動は続ける
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
