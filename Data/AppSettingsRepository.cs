using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
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
        // 書き出しは遅延させているため、読む前に保留分を反映しておく（書いた直後でも最新が読める）
        Flush();

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

    /// <summary>
    /// 書き出しをまとめるための待ち時間。フォルダ移動1回で「タブ構成」「アクセス実績」の2回、
    /// タブの開閉ではさらに増えるため、そのたびにUIスレッドから同期I/Oを行わずに済ませる。
    /// </summary>
    private static readonly TimeSpan WriteDelay = TimeSpan.FromMilliseconds(300);

    // 保留中の書き出しは settings.json のパス単位（プロセス全体）で持つ。
    // 同じファイルを指すインスタンスが複数あっても「書いた直後に読める」ことを保証するため
    private static readonly Dictionary<string, (long Sequence, AppSettings Settings)> PendingWrites =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, long> LastWrittenSequences =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly object PendingWritesGate = new();
    private static readonly object WriteGate = new();
    private static long _writeSequence;

    /// <summary>
    /// 設定の書き出しを予約する。実際にファイルへ書くのは <see cref="WriteDelay"/> 後で、
    /// その間に来た保存は最後の1件にまとめられる。
    /// 読み出し（<see cref="Load"/>）と終了時（<see cref="Flush"/>）は待たずに反映される。
    /// </summary>
    public void Save(AppSettings settings)
    {
        var sequence = Interlocked.Increment(ref _writeSequence);
        lock (PendingWritesGate)
        {
            PendingWrites[_settingsPath] = (sequence, settings);
        }

        _ = Task.Run(async () =>
        {
            await Task.Delay(WriteDelay).ConfigureAwait(false);
            Flush();
        });
    }

    /// <summary>
    /// 保留中の書き出しがあれば今すぐファイルへ書く。読み出しの直前と、アプリの終了時に呼ぶ。
    /// 書けなかった場合は黙って諦める（設定ファイルが一時的に書けないだけで操作を失敗させない）。
    /// </summary>
    public void Flush()
    {
        (long Sequence, AppSettings Settings) pending;
        lock (PendingWritesGate)
        {
            if (!PendingWrites.Remove(_settingsPath, out pending))
            {
                return;
            }
        }

        lock (WriteGate)
        {
            // 取り出す順とファイルへ書く順は必ずしも一致しないため、より新しい内容を古い内容で上書きしない
            if (LastWrittenSequences.TryGetValue(_settingsPath, out var written) && written > pending.Sequence)
            {
                return;
            }

            LastWrittenSequences[_settingsPath] = pending.Sequence;

            try
            {
                WriteToFile(pending.Settings);
            }
            catch
            {
                // 書けない状況（ディスクフル・権限など）でも操作は続行させる。
                // 次の保存で改めて書き出されるため、ここでは諦めてよい
            }
        }
    }

    private void WriteToFile(AppSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        File.WriteAllText(_settingsPath, json);
    }
}
