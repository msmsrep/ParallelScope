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
                // ルートフォルダは追加時に即座に読み込みを開始する（遅延展開ではなく）。
                // ただし同期版だと切断中のNASルートでUIスレッドがSMBタイムアウトまでブロックするため、
                // 非同期版で開始だけして先へ進む（読み込み完了までツリーにはダミーの子が表示される）
                _ = newRootFolder.EnsureLoadedAsync();
                RootFolders.Add(newRootFolder);
            }
        }

        // 並び替え: 既存項目の削除・追加だけでは順序変更が反映されないため、設定の順序に合わせて移動する。
        // 再生成せずMoveで並び替えることで、読み込み済みのサブフォルダツリーを保持する
        var orderedIndex = 0;
        foreach (var rootPath in normalizedRootPaths)
        {
            var currentIndex = -1;
            for (var i = 0; i < RootFolders.Count; i++)
            {
                if (string.Equals(RootFolders[i].Path, rootPath, StringComparison.OrdinalIgnoreCase))
                {
                    currentIndex = i;
                    break;
                }
            }

            if (currentIndex < 0)
            {
                // 除外パス等でRootFoldersに存在しないルートは順序合わせの対象外
                continue;
            }

            if (currentIndex != orderedIndex)
            {
                RootFolders.Move(currentIndex, orderedIndex);
            }

            orderedIndex++;
        }

        _rootPathsSnapshot = RootFolders.Select(x => x.Path).ToList();

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

        if (AllRootsVirtualFolder.Matches(CurrentPath))
        {
            // 仮想「Roots」を表示中はナビゲーションせず、変更後のルート構成で一覧を取り直す
            RefreshCurrentFolder();
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

            // ここで存在確認はしない。切断中のNASルートを弾くと RootFolders から消え、
            // 次の設定保存（SaveSettings(RootFolders...)）で settings.json からも恒久的に失われてしまう。
            // 実在しないルートはスキャン対象の選別（FullScanConfiguredRootsAsync）側で除外される
            if (string.IsNullOrEmpty(normalized))
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

            // 除外パスは「パスに対する述語」であり実在は要件でない。存在確認で弾くと、
            // 切断中のNAS配下の除外設定が次の設定保存で恒久的に失われてしまうため確認しない
            if (string.IsNullOrEmpty(normalized))
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

        return IsExcludedNormalizedPath(normalizedPath);
    }

    /// <summary>正規化済みパス用の除外判定。DBキャッシュ由来のパスは保存時に正規化済みのため、大量の結果行に対して再正規化のコストをかけずに使える。</summary>
    private bool IsExcludedNormalizedPath(string normalizedPath)
    {
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
