using System.IO;
using ParallelScope.Data;
using ParallelScope.Utilities;

namespace ParallelScope.ViewModels;

/// <summary>ルートフォルダ・除外パス・フルスキャン間隔などのアプリ設定に関する処理。</summary>
public partial class MainWindowViewModel
{
    /// <summary>起動時に保存済み設定を読み込み、ルートフォルダ一覧を構築する。</summary>
    private void InitializeRootFolders()
    {
        var settings = _appSettingsRepository.Load();
        _fullScanIntervalHours = NormalizeFullScanIntervalHours(settings.FullScanIntervalHours);
        _excludedPaths = NormalizeExcludedPaths(settings.ExcludedPaths ?? Enumerable.Empty<string>()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        // プロパティセッター経由だとCurrentPath未設定の状態でリクエストが走ってしまうため、フィールドへ直接読み込む
        _isFlatFileViewEnabled = settings.IsFlatFileViewEnabled;
        ApplyRootPaths(settings.RootPaths ?? Enumerable.Empty<string>(), false);
    }

    /// <summary>現在設定されているルートフォルダのパス一覧を取得する。</summary>
    public IReadOnlyList<string> GetConfiguredRootPaths()
    {
        return RootFolders.Select(x => x.Path).ToList();
    }

    /// <summary>フルスキャンの実行間隔（時間）を取得する。</summary>
    public int GetFullScanIntervalHours()
    {
        return _fullScanIntervalHours;
    }

    /// <summary>除外パス一覧を取得する。</summary>
    public IReadOnlyList<string> GetExcludedPaths()
    {
        return _excludedPaths.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>設定画面からの入力を適用し、設定ファイルへ保存する。</summary>
    public void ApplySettings(IEnumerable<string> rootPaths, IEnumerable<string> excludedPaths, int fullScanIntervalHours)
    {
        _fullScanIntervalHours = NormalizeFullScanIntervalHours(fullScanIntervalHours);
        _excludedPaths = NormalizeExcludedPaths(excludedPaths ?? Enumerable.Empty<string>()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        ApplyRootPaths(rootPaths ?? Enumerable.Empty<string>(), true);
    }

    public void ApplyRootPaths(IEnumerable<string> rootPaths)
    {
        ApplyRootPaths(rootPaths, true);
    }

    /// <summary>ルートフォルダ一覧を差分更新する。saveSettingsがtrueなら設定ファイルにも保存する。</summary>
    private void ApplyRootPaths(IEnumerable<string> rootPaths, bool saveSettings)
    {
        var normalizedRootPaths = NormalizeRootPaths(rootPaths).ToList();
        if (normalizedRootPaths.Count == 0)
        {
            normalizedRootPaths = GetFallbackDriveRoots().ToList();
        }

        // 差分更新: 既存のルートフォルダをマップ化
        var newRootPaths = new HashSet<string>(normalizedRootPaths, StringComparer.OrdinalIgnoreCase);

        // 削除: 新しいリストに含まれないルートフォルダを削除
        var rootsToRemove = RootFolders
            .Where(x => !newRootPaths.Contains(x.Path) || IsExcludedPath(x.Path))
            .ToList();
        foreach (var root in rootsToRemove)
        {
            RootFolders.Remove(root);
        }

        // 追加: 新しいリストに含まれるがまだ存在しないルートフォルダを追加
        var existingRootPaths = new HashSet<string>(RootFolders.Select(x => x.Path), StringComparer.OrdinalIgnoreCase);
        foreach (var rootPath in normalizedRootPaths)
        {
            if (!IsExcludedPath(rootPath) && !existingRootPaths.Contains(rootPath))
            {
                var newRootFolder = new FolderItemViewModel(rootPath, IsExcludedPath);
                // ルートフォルダは初期化時に即座に読み込む（遅延展開ではなく）
                newRootFolder.EnsureLoaded();
                RootFolders.Add(newRootFolder);
            }
        }

        if (saveSettings)
        {
            SaveSettings(normalizedRootPaths);
        }

        var currentRoot = RootFolders.FirstOrDefault();
        if (currentRoot is null)
        {
            CurrentPath = string.Empty;
            AddressInput = string.Empty;
            _currentDirectoryItems.Clear();
            ReplaceVisibleFileItems(Array.Empty<FileItemViewModel>());
            return;
        }

        if (string.IsNullOrWhiteSpace(CurrentPath)
            || !RootFolders.Any(x => PathNormalizer.IsAncestorOrSame(x.Path, CurrentPath)))
        {
            NavigateTo(currentRoot.Path, false);
        }
    }

    /// <summary>現在の設定一式（ルートパス・除外パス・フルスキャン間隔・フラット表示モード）をsettings.jsonへ保存する。</summary>
    private void SaveSettings(IEnumerable<string> rootPaths)
    {
        _appSettingsRepository.Save(new AppSettings
        {
            RootPaths = rootPaths.ToList(),
            ExcludedPaths = _excludedPaths.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
            FullScanIntervalHours = _fullScanIntervalHours,
            IsFlatFileViewEnabled = _isFlatFileViewEnabled
        });
    }

    private static IEnumerable<string> NormalizeRootPaths(IEnumerable<string> rootPaths)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in rootPaths)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            string normalized;
            try
            {
                normalized = PathNormalizer.Normalize(path);
            }
            catch
            {
                continue;
            }

            if (string.IsNullOrEmpty(normalized) || !Directory.Exists(normalized))
            {
                continue;
            }

            if (seen.Add(normalized))
            {
                yield return normalized;
            }
        }
    }

    private static IEnumerable<string> GetFallbackDriveRoots()
    {
        var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        if (!string.IsNullOrEmpty(desktopPath) && Directory.Exists(desktopPath))
        {
            yield return desktopPath;
        }
    }

    private static IEnumerable<string> NormalizeExcludedPaths(IEnumerable<string> excludedPaths)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in excludedPaths)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            string normalized;
            try
            {
                normalized = PathNormalizer.Normalize(path);
            }
            catch
            {
                continue;
            }

            if (string.IsNullOrEmpty(normalized) || !Directory.Exists(normalized))
            {
                continue;
            }

            if (seen.Add(normalized))
            {
                yield return normalized;
            }
        }
    }

    private static int NormalizeFullScanIntervalHours(int hours)
    {
        return hours > 0 ? hours : AppSettings.DefaultFullScanIntervalHours;
    }

    /// <summary>指定パスが除外設定に該当するか（自身または祖先が除外パスに含まれるか）を判定する。</summary>
    private bool IsExcludedPath(string path)
    {
        var normalizedPath = PathNormalizer.Normalize(path);
        if (string.IsNullOrWhiteSpace(normalizedPath))
        {
            return false;
        }

        foreach (var excludedPath in _excludedPaths)
        {
            if (string.Equals(normalizedPath, excludedPath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var prefix = PathNormalizer.WithTrailingSeparator(excludedPath);
            if (normalizedPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
