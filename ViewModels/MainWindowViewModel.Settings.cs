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
        // プロパティセッター経由だとCurrentPath未設定の状態でリクエストが走ってしまうため、副作用の無い初期化用APIで読み込む
        ActiveTab.InitializeFlatFileViewEnabled(settings.IsFlatFileViewEnabled);
        _visibleColumns = NormalizeVisibleColumns(settings.VisibleColumns);
        _columnOrder = NormalizeColumnOrder(settings.ColumnOrder);
        _columnWidths = NormalizeColumnWidths(settings.ColumnWidths);
        _csvExportSizeInBytes = settings.CsvExportSizeInBytes;
        _showHiddenItems = settings.ShowHiddenItems;
        _showSystemItems = settings.ShowSystemItems;
        ApplyHiddenItemVisibilityToTree();
        _developerUnlockKey = settings.DeveloperUnlockKey;
        _theme = AppTheme.Parse(settings.Theme);
        _language = AppLanguage.Parse(settings.Language);
        _visibleTreeNodes = NormalizeVisibleTreeNodes(settings.VisibleTreeNodes);
        _treeNodeOrder = NormalizeTreeNodeOrder(settings.TreeNodeOrder);
        // 除外パスの読み込み後に呼ぶ（「よく使う」の絞り込みで除外設定を参照するため）
        LoadFavoritesAndUsage(settings);
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

    /// <summary>ファイル一覧に表示する列キーの一覧を、画面上の列順で取得する（Name列は常時表示のため含まない）。</summary>
    public IReadOnlyList<string> GetVisibleColumns()
    {
        return FileListColumns.OptionalColumns.Where(_visibleColumns.Contains).ToList();
    }

    /// <summary>隠し属性のファイル/フォルダを表示する設定かどうかを取得する。</summary>
    public bool GetShowHiddenItems()
    {
        return _showHiddenItems;
    }

    /// <summary>システム属性のファイル/フォルダを表示する設定かどうかを取得する。</summary>
    public bool GetShowSystemItems()
    {
        return _showSystemItems;
    }

    /// <summary>現在の表示設定をフォルダツリーの列挙条件へ反映する（読み込み済みの子は呼び出し側が読み直す）。</summary>
    private void ApplyHiddenItemVisibilityToTree()
    {
        FolderItemViewModel.AttributesToSkip = HiddenItemVisibility.GetAttributesToSkip(_showHiddenItems, _showSystemItems);
    }

    /// <summary>ファイル一覧の列の並び順（列キー。Nameを含む）を取得する。</summary>
    public IReadOnlyList<string> GetColumnOrder()
    {
        return _columnOrder.ToList();
    }

    /// <summary>ユーザーが変更した列幅（列キー→ピクセル幅）を取得する。未変更の列は含まれない。</summary>
    public IReadOnlyDictionary<string, double> GetColumnWidths()
    {
        return new Dictionary<string, double>(_columnWidths, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// ファイル一覧の列レイアウト（並び順・列幅）を保存する。
    /// ヘッダーのドラッグ操作はイベントで拾わずウィンドウを閉じる時にまとめて保存するため、
    /// 変化が無ければ書き込まない。
    /// </summary>
    public void SaveColumnLayout(IEnumerable<string> columnOrder, IReadOnlyDictionary<string, double> columnWidths)
    {
        var normalizedOrder = NormalizeColumnOrder(columnOrder.ToList());
        var normalizedWidths = NormalizeColumnWidths(columnWidths.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase));

        if (normalizedOrder.SequenceEqual(_columnOrder, StringComparer.OrdinalIgnoreCase)
            && normalizedWidths.Count == _columnWidths.Count
            && normalizedWidths.All(pair => _columnWidths.TryGetValue(pair.Key, out var width) && width == pair.Value))
        {
            return;
        }

        _columnOrder = normalizedOrder;
        _columnWidths = normalizedWidths;
        SaveSettings(RootFolders.Select(x => x.Path));
    }

    /// <summary>
    /// 保存済みの列幅をすべて破棄し、既定幅（XAML定義の幅）に戻す。
    /// 列の並び順は保持する（設定画面で個別に編集できるため）。
    /// </summary>
    public void ResetColumnWidths()
    {
        if (_columnWidths.Count == 0)
        {
            return;
        }

        _columnWidths = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        SaveSettings(RootFolders.Select(x => x.Path));
    }

    /// <summary>CSV出力でSize列を生のバイト数で書き出す設定か（保存ダイアログの既定選択に使う）。</summary>
    public bool GetCsvExportSizeInBytes()
    {
        return _csvExportSizeInBytes;
    }

    /// <summary>CSV出力のSize列の書式を記憶する（次回の保存ダイアログの既定選択になる）。</summary>
    public void SetCsvExportSizeInBytes(bool sizeInBytes)
    {
        if (_csvExportSizeInBytes == sizeInBytes)
        {
            return;
        }

        _csvExportSizeInBytes = sizeInBytes;
        SaveSettings(RootFolders.Select(x => x.Path));
    }

    /// <summary>settings.jsonに書かれた開発者専用のPlus解放キーを取得する（未設定ならnull）。</summary>
    public string? GetDeveloperUnlockKey()
    {
        return _developerUnlockKey;
    }

    /// <summary>現在の配色テーマ設定を取得する。</summary>
    public AppThemeSetting GetTheme()
    {
        return _theme;
    }

    /// <summary>
    /// 配色テーマを切り替えて即座に適用し、設定ファイルへ保存する。
    /// 設定画面では見た目のプレビューを兼ねるため、Saveボタンを待たずにここで確定させる。
    /// </summary>
    public void ApplyTheme(AppThemeSetting theme)
    {
        if (_theme == theme)
        {
            return;
        }

        _theme = theme;
        AppTheme.Apply(_theme);
        SaveSettings(RootFolders.Select(x => x.Path));
    }

    /// <summary>現在の表示言語設定を取得する。</summary>
    public AppLanguageSetting GetLanguage()
    {
        return _language;
    }

    /// <summary>
    /// 表示言語を切り替えて即座に適用し、設定ファイルへ保存する。
    /// 配色テーマと同じく、設定画面ではその場で見た目が変わるためSaveボタンを待たずに確定させる。
    /// </summary>
    public void ApplyLanguage(AppLanguageSetting language)
    {
        if (_language == language)
        {
            return;
        }

        _language = language;
        AppLanguage.Apply(_language);
        // バインディング経由で更新されないツリー・タブ見出しの表示名（仮想ノード・読み込み中のダミー）を引き直す
        RefreshLocalizedTreeNames();
        RefreshLocalizedTabNames();
        SaveSettings(RootFolders.Select(x => x.Path));
    }

    /// <summary>起動時に、保存済みの表示言語をアプリ全体へ適用する。</summary>
    public void ApplySavedLanguage()
    {
        AppLanguage.Apply(_language);
        RefreshLocalizedTreeNames();
        RefreshLocalizedTabNames();
    }

    /// <summary>設定画面からの入力を適用し、設定ファイルへ保存する。</summary>
    public void ApplySettings(
        IEnumerable<string> rootPaths,
        IEnumerable<string> excludedPaths,
        int fullScanIntervalHours,
        IEnumerable<string>? visibleColumns,
        IEnumerable<string>? columnOrder,
        IEnumerable<string>? visibleTreeNodes,
        IEnumerable<string>? treeNodeOrder,
        bool showHiddenItems,
        bool showSystemItems)
    {
        var hiddenItemVisibilityChanged = _showHiddenItems != showHiddenItems || _showSystemItems != showSystemItems;
        _showHiddenItems = showHiddenItems;
        _showSystemItems = showSystemItems;
        _fullScanIntervalHours = NormalizeFullScanIntervalHours(fullScanIntervalHours);
        _excludedPaths = NormalizeExcludedPaths(excludedPaths ?? Enumerable.Empty<string>()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _visibleColumns = NormalizeVisibleColumns(visibleColumns?.ToList());
        _columnOrder = NormalizeColumnOrder(columnOrder?.ToList());
        _visibleTreeNodes = NormalizeVisibleTreeNodes(visibleTreeNodes?.ToList());
        _treeNodeOrder = NormalizeTreeNodeOrder(treeNodeOrder?.ToList());
        // ルートの反映（＝設定ファイルへの保存）より先にツリーを組み直す。
        // 非表示にしたノードを開いていた場合はここで「Folders」へ退避される
        RebuildTreeRoots();
        ApplyRootPaths(rootPaths ?? Enumerable.Empty<string>(), true);

        // 一覧・ツリーの絞り込み条件が変わった場合は、表示中の内容を取り直す
        // （ApplyRootPathsはルート構成が変わらない限り再読み込みしないため）
        if (hiddenItemVisibilityChanged)
        {
            RefreshHiddenItemVisibility();
        }
    }

    /// <summary>隠し/システム属性の表示条件を、ツリーの列挙条件と表示中の一覧へ反映し直す。</summary>
    private void RefreshHiddenItemVisibility()
    {
        ApplyHiddenItemVisibilityToTree();

        // 読み込み済みの子フォルダは前の条件で並んでいるため、ツリー上の実体ノードを読み直す
        // （お気に入り等の複製ノードも同じ実フォルダを列挙するため対象に含める）
        foreach (var folder in RootFolders.Concat(_favoriteFolders).Concat(_frequentFolders).Concat(_recentFolders))
        {
            folder.Reload();
        }

        foreach (var tab in AllTabs)
        {
            tab.RefreshCurrentFolder();
        }
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
            foreach (var tab in AllTabs)
            {
                tab.Clear();
            }

            return;
        }

        // ルート構成の変更は開いている全タブに効く（表示していないタブも次に開いた時点で正しい内容になる）
        foreach (var tab in AllTabs)
        {
            if (VirtualFolders.IsVirtual(tab.CurrentPath))
            {
                // 仮想ノードを表示中はナビゲーションせず、変更後の構成で一覧を取り直す
                tab.RefreshCurrentFolder();
                continue;
            }

            if (string.IsNullOrWhiteSpace(tab.CurrentPath)
                || !RootFolders.Any(x => PathNormalizer.IsAncestorOrSame(x.Path, tab.CurrentPath)))
            {
                tab.NavigateTo(currentRoot.Path, false);
            }
        }
    }

    /// <summary>現在の設定一式（ルートパス・除外パス・フルスキャン間隔・フラット表示モード・配色テーマ・お気に入り・アクセス実績）をsettings.jsonへ保存する。</summary>
    private void SaveSettings(IEnumerable<string> rootPaths)
    {
        _appSettingsRepository.Save(new AppSettings
        {
            RootPaths = rootPaths.ToList(),
            ExcludedPaths = _excludedPaths.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
            FullScanIntervalHours = _fullScanIntervalHours,
            IsFlatFileViewEnabled = ActiveTab.IsFlatFileViewEnabled,
            VisibleColumns = FileListColumns.OptionalColumns.Where(_visibleColumns.Contains).ToList(),
            ColumnOrder = _columnOrder.ToList(),
            ColumnWidths = new Dictionary<string, double>(_columnWidths, StringComparer.OrdinalIgnoreCase),
            CsvExportSizeInBytes = _csvExportSizeInBytes,
            ShowHiddenItems = _showHiddenItems,
            ShowSystemItems = _showSystemItems,
            Theme = _theme.ToString(),
            Language = _language.ToString(),
            VisibleTreeNodes = _treeNodeOrder.Where(_visibleTreeNodes.Contains).ToList(),
            TreeNodeOrder = _treeNodeOrder.ToList(),
            FavoritePaths = _favoritePaths.ToList(),
            FolderUsages = _folderUsages.Values.ToList(),
            DeveloperUnlockKey = _developerUnlockKey
        });
    }

    /// <summary>
    /// 表示するツリーノードのキーを既知のノードに絞り込んで正規化する。null（設定ファイルに項目が無い
    /// ＝旧バージョンからの移行時）は既定（全て表示）を返す。空リストは「Folders以外を全て非表示」として尊重する。
    /// </summary>
    private static HashSet<string> NormalizeVisibleTreeNodes(IReadOnlyCollection<string>? visibleTreeNodes)
    {
        if (visibleTreeNodes is null)
        {
            return TreeNodes.DefaultVisibleNodes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        return TreeNodes.OptionalNodes
            .Where(node => visibleTreeNodes.Contains(node, StringComparer.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// ツリー最上位のノードの並び順を正規化する。未知のキー・重複を除き、指定に無いノードは
    /// 既定の並び順で末尾に補う（設定ファイルが古いバージョンで書かれていてノードが増えている場合など）。
    /// </summary>
    private static List<string> NormalizeTreeNodeOrder(IReadOnlyCollection<string>? treeNodeOrder)
    {
        if (treeNodeOrder is null)
        {
            return TreeNodes.AllNodes.ToList();
        }

        var normalized = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var node in treeNodeOrder)
        {
            var known = TreeNodes.AllNodes.FirstOrDefault(x => string.Equals(x, node, StringComparison.OrdinalIgnoreCase));
            if (known is not null && seen.Add(known))
            {
                normalized.Add(known);
            }
        }

        normalized.AddRange(TreeNodes.AllNodes.Where(node => !seen.Contains(node)));
        return normalized;
    }

    /// <summary>
    /// 表示列キーを既知の列に絞り込んで正規化する。null（設定ファイルに項目が無い＝旧バージョンからの移行時）
    /// はデフォルト列を返す。空リストは「全ての任意列を非表示」として尊重する。
    /// </summary>
    private static HashSet<string> NormalizeVisibleColumns(IReadOnlyCollection<string>? visibleColumns)
    {
        if (visibleColumns is null)
        {
            return FileListColumns.DefaultVisibleColumns.ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        return FileListColumns.OptionalColumns
            .Where(column => visibleColumns.Contains(column, StringComparer.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 列の並び順を正規化する。未知のキー・重複を除き、指定に無い列は既定の並び順で末尾に補う
    /// （設定ファイルが古いバージョンで書かれていて列が増えている場合など）。
    /// </summary>
    private static List<string> NormalizeColumnOrder(IReadOnlyCollection<string>? columnOrder)
    {
        if (columnOrder is null)
        {
            return FileListColumns.AllColumns.ToList();
        }

        var result = new List<string>(FileListColumns.AllColumns.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var column in columnOrder)
        {
            var known = FileListColumns.AllColumns.FirstOrDefault(x => string.Equals(x, column, StringComparison.OrdinalIgnoreCase));
            if (known is not null && seen.Add(known))
            {
                result.Add(known);
            }
        }

        result.AddRange(FileListColumns.AllColumns.Where(column => !seen.Contains(column)));
        return result;
    }

    /// <summary>列幅を正規化する。未知のキーと、表示できない値（0以下・NaN・無限大）を除く。</summary>
    private static Dictionary<string, double> NormalizeColumnWidths(IReadOnlyDictionary<string, double>? columnWidths)
    {
        var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        if (columnWidths is null)
        {
            return result;
        }

        foreach (var column in FileListColumns.AllColumns)
        {
            if (columnWidths.TryGetValue(column, out var width) && double.IsFinite(width) && width > 0)
            {
                result[column] = width;
            }
        }

        return result;
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
