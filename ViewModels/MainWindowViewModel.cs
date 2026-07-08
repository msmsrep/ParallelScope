using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using ParallelScope.Data;

namespace ParallelScope.ViewModels;

public class MainWindowViewModel : ObservableObject
{
    private static readonly EnumerationOptions NonRecursiveEnumerationOptions = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint
    };

    private ObservableCollection<FolderItemViewModel> _rootFolders;
    private ObservableCollection<FileItemViewModel> _fileItems;
    private string _currentPath = string.Empty;
    private string _addressInput = string.Empty;
    private string _searchQuery = string.Empty;
    private readonly Stack<string> _backHistory = new();
    private readonly Stack<string> _forwardHistory = new();
    private readonly FileCacheRepository _fileCacheRepository;
    private readonly AppSettingsRepository _appSettingsRepository;
    private readonly SynchronizationContext _uiContext;
    private int _navigationVersion;
    private int _searchVersion;
    private int _fullScanIntervalHours = AppSettings.DefaultFullScanIntervalHours;
    private HashSet<string> _excludedPaths = new(StringComparer.OrdinalIgnoreCase);
    private List<FileItemViewModel> _currentDirectoryItems = new();

    // バックグラウンド更新の連続実行抑制
    private bool _isRefreshRunning;
    private string? _pendingRefreshPath;
    private int _pendingRefreshVersion;

    // フォルダサイズキャッシュ適用の連続実行抑制
    private bool _isFolderSizeApplyRunning;
    private string? _pendingFolderSizePath;
    private IReadOnlyCollection<CachedFileSystemEntry>? _pendingFolderSizeEntries;
    private int _pendingFolderSizeVersion;

    // 検索の連続実行抑制
    private bool _isSearchRunning;
    private string? _pendingSearchRootPath;
    private string? _pendingSearchQuery;
    private int _pendingSearchVersion;

    public ObservableCollection<FolderItemViewModel> RootFolders
    {
        get => _rootFolders;
        set => SetProperty(ref _rootFolders, value);
    }

    public ObservableCollection<FileItemViewModel> FileItems
    {
        get => _fileItems;
        set => SetProperty(ref _fileItems, value);
    }

    public string CurrentPath
    {
        get => _currentPath;
        set
        {
            if (SetProperty(ref _currentPath, value))
            {
                OnPropertyChanged(nameof(CanGoUp));
            }
        }
    }

    public string AddressInput
    {
        get => _addressInput;
        set => SetProperty(ref _addressInput, value);
    }

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (!SetProperty(ref _searchQuery, value))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                ClearSearch();
            }
        }
    }

    public bool CanGoBack => _backHistory.Count > 0;

    public bool CanGoForward => _forwardHistory.Count > 0;

    public bool CanGoUp => GetParentPath(CurrentPath) is not null;

    public MainWindowViewModel()
    {
        _rootFolders = new ObservableCollection<FolderItemViewModel>();
        _fileItems = new ObservableCollection<FileItemViewModel>();
        _fileCacheRepository = new FileCacheRepository();
        _appSettingsRepository = new AppSettingsRepository();
        _uiContext = SynchronizationContext.Current ?? new SynchronizationContext();

        InitializeRootFolders();
    }

    private void InitializeRootFolders()
    {
        var settings = _appSettingsRepository.Load();
        _fullScanIntervalHours = NormalizeFullScanIntervalHours(settings.FullScanIntervalHours);
        _excludedPaths = NormalizeExcludedPaths(settings.ExcludedPaths ?? Enumerable.Empty<string>()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        ApplyRootPaths(settings.RootPaths ?? Enumerable.Empty<string>(), false);
    }

    public IReadOnlyList<string> GetConfiguredRootPaths()
    {
        return RootFolders.Select(x => x.Path).ToList();
    }

    public int GetFullScanIntervalHours()
    {
        return _fullScanIntervalHours;
    }

    public IReadOnlyList<string> GetExcludedPaths()
    {
        return _excludedPaths.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
    }

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

    public bool LoadFiles(string folderPath)
    {
        return NavigateTo(folderPath, true);
    }

    public bool GoBack()
    {
        if (!CanGoBack)
        {
            return false;
        }

        var targetPath = _backHistory.Pop();

        if (!string.IsNullOrEmpty(CurrentPath))
        {
            _forwardHistory.Push(CurrentPath);
        }

        var success = LoadFilesInternal(targetPath);
        NotifyNavigationStateChanged();
        return success;
    }

    public bool GoForward()
    {
        if (!CanGoForward)
        {
            return false;
        }

        var targetPath = _forwardHistory.Pop();

        if (!string.IsNullOrEmpty(CurrentPath))
        {
            _backHistory.Push(CurrentPath);
        }

        var success = LoadFilesInternal(targetPath);
        NotifyNavigationStateChanged();
        return success;
    }

    public bool GoUp()
    {
        var parentPath = GetParentPath(CurrentPath);
        if (parentPath is null)
        {
            return false;
        }

        return NavigateTo(parentPath, true);
    }

    public bool TryNavigateByAddressInput()
    {
        return NavigateTo(AddressInput, true);
    }

    public bool SearchCurrentPath()
    {
        if (string.IsNullOrWhiteSpace(CurrentPath) || !Directory.Exists(CurrentPath))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(SearchQuery))
        {
            ClearSearch();
            return true;
        }

        var normalizedQuery = SearchQuery.Trim();
        var searchRootPath = CurrentPath;
        var searchVersion = Interlocked.Increment(ref _searchVersion);

        ReplaceVisibleFileItems(Array.Empty<FileItemViewModel>());
        // 検索リクエストを統合するメソッドを呼ぶ
        RequestSearch(searchRootPath, normalizedQuery, searchVersion);
        return true;
    }

    public async Task<int> FullScanConfiguredRootsAsync(CancellationToken token)
    {
        var configuredRootPaths = RootFolders
            .Select(x => NormalizePath(x.Path))
            .Where(path =>
                !string.IsNullOrWhiteSpace(path) &&
                Directory.Exists(path) &&
                !IsExcludedPath(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return await ScanFolderSubtreesAsync(configuredRootPaths, token);
    }

    private async Task<int> ScanFolderSubtreesAsync(
        IReadOnlyCollection<string> rootPaths,
        CancellationToken token)
    {
        return await Task.Run(() => ScanFolderSubtrees(rootPaths, token), token);
    }

    public Task<int> ScanFolderSubtreeAsync(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath) || IsExcludedPath(folderPath))
        {
            return Task.FromResult(0);
        }

        return Task.Run(() => ScanFolderSubtrees(new[] { folderPath }));
    }

    public void ClearSearch()
    {
        Interlocked.Increment(ref _searchVersion);
        ReplaceVisibleFileItems(_currentDirectoryItems);
    }

    public bool NavigateTo(string folderPath, bool addToHistory)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            return false;
        }

        var normalizedTargetPath = NormalizePath(folderPath);
        if (string.IsNullOrEmpty(normalizedTargetPath) || !Directory.Exists(normalizedTargetPath))
        {
            return false;
        }

        if (IsExcludedPath(normalizedTargetPath))
        {
            return false;
        }

        if (string.Equals(NormalizePath(CurrentPath), normalizedTargetPath, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (addToHistory && !string.IsNullOrEmpty(CurrentPath))
        {
            _backHistory.Push(CurrentPath);
            _forwardHistory.Clear();
        }

        var success = LoadFilesInternal(normalizedTargetPath);
        NotifyNavigationStateChanged();
        return success;
    }

    private bool LoadFilesInternal(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
        {
            return false;
        }

        try
        {
            CurrentPath = folderPath;
            AddressInput = folderPath;
            SearchQuery = string.Empty;

            var navigationVersion = Interlocked.Increment(ref _navigationVersion);
            _ = LoadFromCacheAsync(folderPath, navigationVersion);
            // 連続リクエストを統合するメソッドを呼ぶ
            RequestRefreshFromFileSystem(folderPath, navigationVersion);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task LoadFromCacheAsync(string folderPath, int navigationVersion)
    {
        List<CachedFileSystemEntry> cachedEntries;

        try
        {
            cachedEntries = await Task.Run(() => _fileCacheRepository.GetEntriesByParentPath(folderPath));
        }
        catch
        {
            return;
        }

        if (navigationVersion != Volatile.Read(ref _navigationVersion) || !IsSamePath(CurrentPath, folderPath))
        {
            return;
        }

        _uiContext.Post(_ =>
        {
            if (navigationVersion != Volatile.Read(ref _navigationVersion) || !IsSamePath(CurrentPath, folderPath))
            {
                return;
            }

            UpdateCurrentDirectoryItems(cachedEntries.Select(ToViewModel));
            // キャッシュサイズ適用をリクエスト（統合）
            RequestApplyCachedFolderSizes(folderPath, cachedEntries, navigationVersion);
        }, null);
    }

    // バックグラウンド更新のリクエストを統合（連続実行を抑制）
    private void RequestRefreshFromFileSystem(string folderPath, int navigationVersion)
    {
        lock (this)
        {
            _pendingRefreshPath = folderPath;
            _pendingRefreshVersion = navigationVersion;

            // 既に実行中の場合は、ペンディング状態で待つ
            if (_isRefreshRunning)
            {
                return;
            }

            _isRefreshRunning = true;
        }

        // 実行中フラグをセットしたので、バックグラウンドタスクを開始
        _ = ExecuteRefreshCycle();
    }

    // 検索のリクエストを統合（連続実行を抑制）
    private void RequestSearch(string rootPath, string query, int searchVersion)
    {
        lock (this)
        {
            _pendingSearchRootPath = rootPath;
            _pendingSearchQuery = query;
            _pendingSearchVersion = searchVersion;

            // 既に実行中の場合は、ペンディング状態で待つ
            if (_isSearchRunning)
            {
                return;
            }

            _isSearchRunning = true;
        }

        // 実行中フラグをセットしたので、バックグラウンドタスクを開始
        _ = ExecuteSearchCycle();
    }

    // 検索を順序立てて実行
    private async Task ExecuteSearchCycle()
    {
        try
        {
            while (true)
            {
                string? rootPath;
                string? query;
                int version;

                lock (this)
                {
                    if (_pendingSearchRootPath is null)
                    {
                        _isSearchRunning = false;
                        return;
                    }

                    rootPath = _pendingSearchRootPath;
                    query = _pendingSearchQuery;
                    version = _pendingSearchVersion;
                    _pendingSearchRootPath = null;
                    _pendingSearchQuery = null;
                }

                // 実際の検索を実行
                if (query is not null)
                {
                    await SearchInBackground(rootPath, query, version);
                }
            }
        }
        finally
        {
            lock (this)
            {
                _isSearchRunning = false;
            }
        }
    }

    private async Task SearchInBackground(string rootPath, string query, int searchVersion)
    {
        List<FileItemViewModel> cacheResults;

        try
        {
            cacheResults = await Task.Run(() =>
                _fileCacheRepository.SearchEntriesUnderPath(rootPath, query)
                    .Select(ToViewModel)
                    .ToList());
        }
        catch
        {
            cacheResults = new List<FileItemViewModel>();
        }

        if (searchVersion != Volatile.Read(ref _searchVersion)
            || !IsSamePath(CurrentPath, rootPath)
            || !string.Equals(SearchQuery.Trim(), query, StringComparison.Ordinal))
        {
            return;
        }

        _uiContext.Post(_ =>
        {
            if (searchVersion != Volatile.Read(ref _searchVersion)
                || !IsSamePath(CurrentPath, rootPath)
                || !string.Equals(SearchQuery.Trim(), query, StringComparison.Ordinal))
            {
                return;
            }

            ReplaceVisibleFileItems(cacheResults);
        }, null);
    }

    // バックグラウンド更新を順序立てて実行
    private async Task ExecuteRefreshCycle()
    {
        try
        {
            while (true)
            {
                string? refreshPath;
                int refreshVersion;

                lock (this)
                {
                    if (_pendingRefreshPath is null)
                    {
                        _isRefreshRunning = false;
                        return;
                    }

                    refreshPath = _pendingRefreshPath;
                    refreshVersion = _pendingRefreshVersion;
                    _pendingRefreshPath = null;
                }

                // 実際のバックグラウンド更新を実行
                await RefreshFromFileSystemInBackground(refreshPath, refreshVersion);
            }
        }
        finally
        {
            lock (this)
            {
                _isRefreshRunning = false;
            }
        }
    }

    private async Task RefreshFromFileSystemInBackground(string folderPath, int navigationVersion)
    {
        List<CachedFileSystemEntry> liveEntries;

        try
        {
            liveEntries = await Task.Run(() => ReadEntriesFromFileSystem(folderPath));
        }
        catch
        {
            return;
        }

        try
        {
            await Task.Run(() => _fileCacheRepository.ReplaceEntriesByParentPath(folderPath, liveEntries));
        }
        catch
        {
            // キャッシュ保存失敗時でも画面更新は継続する
        }

        if (navigationVersion != Volatile.Read(ref _navigationVersion) || !IsSamePath(CurrentPath, folderPath))
        {
            return;
        }

        _uiContext.Post(_ =>
        {
            if (navigationVersion != Volatile.Read(ref _navigationVersion) || !IsSamePath(CurrentPath, folderPath))
            {
                return;
            }

            UpdateCurrentDirectoryItems(liveEntries.Select(ToViewModel));
            // キャッシュサイズ適用をリクエスト（統合）
            RequestApplyCachedFolderSizes(folderPath, liveEntries, navigationVersion);
        }, null);
    }

    // フォルダサイズキャッシュ適用のリクエストを統合（連続実行を抑制）
    private void RequestApplyCachedFolderSizes(string folderPath, IReadOnlyCollection<CachedFileSystemEntry> entries, int navigationVersion)
    {
        lock (this)
        {
            _pendingFolderSizePath = folderPath;
            _pendingFolderSizeEntries = entries;
            _pendingFolderSizeVersion = navigationVersion;

            // 既に実行中の場合は、ペンディング状態で待つ
            if (_isFolderSizeApplyRunning)
            {
                return;
            }

            _isFolderSizeApplyRunning = true;
        }

        // 実行中フラグをセットしたので、バックグラウンドタスクを開始
        _ = ExecuteFolderSizeApplyCycle();
    }

    // フォルダサイズ適用を順序立てて実行
    private async Task ExecuteFolderSizeApplyCycle()
    {
        try
        {
            while (true)
            {
                string? folderPath;
                IReadOnlyCollection<CachedFileSystemEntry>? entries;
                int version;

                lock (this)
                {
                    if (_pendingFolderSizePath is null)
                    {
                        _isFolderSizeApplyRunning = false;
                        return;
                    }

                    folderPath = _pendingFolderSizePath;
                    entries = _pendingFolderSizeEntries;
                    version = _pendingFolderSizeVersion;
                    _pendingFolderSizePath = null;
                    _pendingFolderSizeEntries = null;
                }

                // 実際のフォルダサイズ適用を実行
                if (entries is not null)
                {
                    await ApplyCachedFolderSizesInBackground(folderPath, entries, version);
                }
            }
        }
        finally
        {
            lock (this)
            {
                _isFolderSizeApplyRunning = false;
            }
        }
    }

    private async Task ApplyCachedFolderSizesInBackground(string folderPath, IReadOnlyCollection<CachedFileSystemEntry> entries, int navigationVersion)
    {
        var folderEntries = entries
            .Where(x => x.IsFolder)
            .ToList();

        if (folderEntries.Count == 0)
        {
            return;
        }

        // キャッシュから取得するフォルダパスのみ抽出
        var folderPaths = folderEntries.Select(x => x.FullPath).ToList();

        Dictionary<string, long> cachedFolderSizes;
        try
        {
            cachedFolderSizes = await Task.Run(() => _fileCacheRepository.GetCachedFolderTotalSizes(folderPath, folderPaths));
        }
        catch
        {
            return;
        }

        // キャッシュが取得できなかったか、全フォルダがサイズ情報を持たない場合はスキップ
        if (cachedFolderSizes.Count == 0)
        {
            return;
        }

        // ナビゲーションの確認: 別のフォルダに移動していないか
        if (navigationVersion != Volatile.Read(ref _navigationVersion) || !IsSamePath(CurrentPath, folderPath))
        {
            return;
        }

        _uiContext.Post(_ =>
        {
            if (navigationVersion != Volatile.Read(ref _navigationVersion) || !IsSamePath(CurrentPath, folderPath))
            {
                return;
            }

            // バイト数で比較して、実際に変わったフォルダのみ更新（不要な再描画を完全に防止）
            foreach (var folderEntry in folderEntries)
            {
                if (cachedFolderSizes.TryGetValue(folderEntry.FullPath, out var cachedSize))
                {
                    var currentItem = FileItems.FirstOrDefault(x => x.FullPath == folderEntry.FullPath);
                    if (currentItem != null && currentItem.IsFolder && currentItem.CachedSizeBytes != cachedSize)
                    {
                        // バイト数が実際に変わった場合のみ更新
                        currentItem.CachedSizeBytes = cachedSize;
                        currentItem.SizeText = cachedSize > 0 ? FormatFileSize(cachedSize) : string.Empty;
                    }
                }
            }
        }, null);
    }

    private static string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }

    private List<CachedFileSystemEntry> ReadEntriesFromFileSystem(string folderPath)
    {
        var dirInfo = new DirectoryInfo(folderPath);
        var entries = new ConcurrentBag<CachedFileSystemEntry>();

        try
        {
            // フォルダ処理を並列化
            var folders = dirInfo
                .EnumerateDirectories("*", NonRecursiveEnumerationOptions)
                .Where(d => !IsExcludedPath(d.FullName))
                .ToList();

            Parallel.ForEach(folders, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, d =>
            {
                var entry = TryCreateFolderEntry(folderPath, d);
                if (entry is not null)
                {
                    entries.Add(entry);
                }
            });

            // ファイル処理を並列化
            var files = dirInfo
                .EnumerateFiles("*", NonRecursiveEnumerationOptions)
                .ToList();

            Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, f =>
            {
                var entry = TryCreateFileEntry(folderPath, f);
                if (entry is not null)
                {
                    entries.Add(entry);
                }
            });
        }
        catch
        {
            // 例外時は空のリストを返す
        }

        // 結果をソートして返す
        var result = entries.ToList();
        result.Sort((a, b) =>
        {
            // フォルダを先に配置
            var folderCompare = b.IsFolder.CompareTo(a.IsFolder);
            if (folderCompare != 0)
                return folderCompare;
            // 同じ種類ならば名前でソート
            return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });
        return result;
    }

    private List<FileItemViewModel> SearchEntriesFromFileSystem(string rootPath, string query)
    {
        var results = new List<FileItemViewModel>();
        var comparison = StringComparison.OrdinalIgnoreCase;

        var pendingDirectories = new Stack<(string path, int depth)>();
        pendingDirectories.Push((rootPath, 0));

        // 検索深度を制限（デフォルト10階層）
        const int MaxSearchDepth = 10;

        while (pendingDirectories.Count > 0)
        {
            var (currentPath, currentDepth) = pendingDirectories.Pop();

            // 深度制限に達した場合はスキップ
            if (currentDepth >= MaxSearchDepth)
            {
                continue;
            }

            IEnumerable<string> childDirectories;
            try
            {
                childDirectories = Directory.EnumerateDirectories(currentPath, "*", NonRecursiveEnumerationOptions)
                    .Where(path => !IsExcludedPath(path))
                    .OrderBy(path => path)
                    .ToList();
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            catch (IOException)
            {
                continue;
            }

            foreach (var directoryPath in childDirectories)
            {
                pendingDirectories.Push((directoryPath, currentDepth + 1));

                try
                {
                    var directoryInfo = new DirectoryInfo(directoryPath);
                    if (!directoryInfo.Name.Contains(query, comparison))
                    {
                        continue;
                    }

                    results.Add(new FileItemViewModel(directoryInfo.FullName, directoryInfo.Name, directoryInfo.LastWriteTime));
                }
                catch (UnauthorizedAccessException)
                {
                }
                catch (IOException)
                {
                }
            }

            IEnumerable<string> filePaths;
            try
            {
                filePaths = Directory.EnumerateFiles(currentPath, "*", NonRecursiveEnumerationOptions)
                    .OrderBy(path => path)
                    .ToList();
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            catch (IOException)
            {
                continue;
            }

            foreach (var filePath in filePaths)
            {
                try
                {
                    var fileInfo = new FileInfo(filePath);
                    if (!fileInfo.Name.Contains(query, comparison))
                    {
                        continue;
                    }

                    results.Add(new FileItemViewModel(fileInfo.FullName, fileInfo.Name, fileInfo.Length, fileInfo.LastWriteTime));
                }
                catch (UnauthorizedAccessException)
                {
                }
                catch (IOException)
                {
                }
            }
        }

        return results;
    }

    private int ScanFolderSubtrees(IReadOnlyCollection<string> rootPaths, CancellationToken token = default)
    {
        var visitedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pendingDirectories = new Stack<string>(rootPaths.Reverse());
        var batchEntries = new Dictionary<string, IReadOnlyCollection<CachedFileSystemEntry>>();
        var updatedFolderCount = 0;
        const int BatchSize = 100;

        while (pendingDirectories.Count > 0)
        {
            token.ThrowIfCancellationRequested();

            var currentPath = pendingDirectories.Pop();
            string normalizedPath;

            try
            {
                normalizedPath = NormalizePath(currentPath);
            }
            catch
            {
                continue;
            }

            if (!visitedDirectories.Add(normalizedPath) || !Directory.Exists(normalizedPath) || IsExcludedPath(normalizedPath))
            {
                continue;
            }

            List<CachedFileSystemEntry> entries;
            try
            {
                entries = ReadEntriesFromFileSystem(normalizedPath);
            }
            catch
            {
                continue;
            }

            batchEntries[normalizedPath] = entries;
            updatedFolderCount++;

            // バッチが一定サイズに達したら、まとめてデータベース更新
            if (batchEntries.Count >= BatchSize)
            {
                try
                {
                    _fileCacheRepository.BatchReplaceEntriesByParentPaths(batchEntries);
                }
                catch
                {
                    // バッチ保存失敗時はスキャンを継続する
                }
                batchEntries.Clear();
            }

            foreach (var folderEntry in entries.Where(x => x.IsFolder && !IsExcludedPath(x.FullPath)).OrderByDescending(x => x.FullPath, StringComparer.OrdinalIgnoreCase))
            {
                pendingDirectories.Push(folderEntry.FullPath);
            }
        }

        // 残りのバッチを処理
        if (batchEntries.Count > 0)
        {
            try
            {
                _fileCacheRepository.BatchReplaceEntriesByParentPaths(batchEntries);
            }
            catch
            {
                // バッチ保存失敗時でもスキャン結果は返す
            }
        }

        return updatedFolderCount;
    }

    private static CachedFileSystemEntry? TryCreateFolderEntry(string parentPath, DirectoryInfo directory)
    {
        try
        {
            return new CachedFileSystemEntry(
                parentPath,
                directory.FullName,
                directory.Name,
                true,
                null,
                directory.LastWriteTimeUtc);
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static CachedFileSystemEntry? TryCreateFileEntry(string parentPath, FileInfo file)
    {
        try
        {
            return new CachedFileSystemEntry(
                parentPath,
                file.FullName,
                file.Name,
                false,
                file.Length,
                file.LastWriteTimeUtc);
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private void ReplaceVisibleFileItems(IEnumerable<FileItemViewModel> items)
    {
        var newItems = items.ToList();
        var currentItems = FileItems.ToList();

        // 既存アイテムをパスでマップ
        var currentItemMap = currentItems.ToDictionary(x => x.FullPath, StringComparer.OrdinalIgnoreCase);

        // 削除するアイテムを特定（パスで比較）
        var newItemPaths = new HashSet<string>(newItems.Select(x => x.FullPath), StringComparer.OrdinalIgnoreCase);
        var itemsToRemove = currentItems
            .Where(x => !newItemPaths.Contains(x.FullPath))
            .ToList();

        // 追加するアイテムと更新するアイテムを特定
        var itemsToAdd = new List<FileItemViewModel>();
        var itemsToUpdate = new List<(FileItemViewModel existing, FileItemViewModel newItem)>();

        foreach (var newItem in newItems)
        {
            if (currentItemMap.TryGetValue(newItem.FullPath, out var existingItem))
            {
                itemsToUpdate.Add((existingItem, newItem));
            }
            else
            {
                itemsToAdd.Add(newItem);
            }
        }

        // 削除
        foreach (var item in itemsToRemove)
        {
            FileItems.Remove(item);
        }

        // 追加
        foreach (var item in itemsToAdd)
        {
            FileItems.Add(item);
        }

        // 更新（既存アイテムのプロパティを新規アイテムの情報で更新）
        foreach (var (existing, newItem) in itemsToUpdate)
        {
            existing.TypeText = newItem.TypeText;
            existing.ModifiedTime = newItem.ModifiedTime;

            // サイズ情報：新規アイテムが空で既存アイテムが有る場合は既存値を保持
            if (!string.IsNullOrEmpty(newItem.SizeText))
            {
                existing.SizeText = newItem.SizeText;
                existing.CachedSizeBytes = newItem.CachedSizeBytes;
            }
            // 新規アイテムが空で既存アイテムが有る場合は既存値を保持（キャッシュから取得したサイズ）
            else if (string.IsNullOrEmpty(newItem.SizeText) && !string.IsNullOrEmpty(existing.SizeText) && existing.CachedSizeBytes > 0)
            {
                // 既存のサイズ情報を保持
            }
        }
    }

    private void UpdateCurrentDirectoryItems(IEnumerable<FileItemViewModel> items)
    {
        var newItems = items.ToList();

        // 既存のアイテムからサイズ情報を引き継ぐ（ライブデータの再取得時にキャッシュサイズが失われるのを防止）
        var currentItemMap = _currentDirectoryItems.ToDictionary(x => x.FullPath, StringComparer.OrdinalIgnoreCase);
        foreach (var newItem in newItems)
        {
            if (currentItemMap.TryGetValue(newItem.FullPath, out var existingItem))
            {
                newItem.CachedSizeBytes = existingItem.CachedSizeBytes;
                // SizeText も引き継ぐ（キャッシュサイズから計算された値）
                if (!string.IsNullOrEmpty(existingItem.SizeText))
                {
                    newItem.SizeText = existingItem.SizeText;
                }
            }
        }

        _currentDirectoryItems = newItems;

        if (string.IsNullOrWhiteSpace(SearchQuery))
        {
            ReplaceVisibleFileItems(_currentDirectoryItems);
        }
    }

    private static FileItemViewModel ToViewModel(CachedFileSystemEntry entry)
    {
        var modifiedLocalTime = DateTime.SpecifyKind(entry.LastWriteTimeUtc, DateTimeKind.Utc).ToLocalTime();

        if (entry.IsFolder)
        {
            return new FileItemViewModel(entry.FullPath, entry.Name, modifiedLocalTime);
        }

        return new FileItemViewModel(entry.FullPath, entry.Name, entry.SizeBytes ?? 0L, modifiedLocalTime);
    }

    private static FileItemViewModel ToViewModel(CachedFileSystemEntry entry, IReadOnlyDictionary<string, long> cachedFolderSizes)
    {
        var modifiedLocalTime = DateTime.SpecifyKind(entry.LastWriteTimeUtc, DateTimeKind.Utc).ToLocalTime();

        if (entry.IsFolder)
        {
            cachedFolderSizes.TryGetValue(entry.FullPath, out var cachedTotalSizeBytes);
            var hasCachedSize = cachedFolderSizes.ContainsKey(entry.FullPath);
            return new FileItemViewModel(
                entry.FullPath,
                entry.Name,
                modifiedLocalTime,
                hasCachedSize ? cachedTotalSizeBytes : null);
        }

        return new FileItemViewModel(entry.FullPath, entry.Name, entry.SizeBytes ?? 0L, modifiedLocalTime);
    }

    private static string? GetParentPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return null;
        }

        return Directory.GetParent(path)?.FullName;
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var fullPath = Path.GetFullPath(path);
        var rootPath = Path.GetPathRoot(fullPath);

        if (!string.IsNullOrEmpty(rootPath) && string.Equals(fullPath, rootPath, StringComparison.OrdinalIgnoreCase))
        {
            return fullPath;
        }

        return fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static bool IsSamePath(string leftPath, string rightPath)
    {
        return string.Equals(NormalizePath(leftPath), NormalizePath(rightPath), StringComparison.OrdinalIgnoreCase);
    }

    private void NotifyNavigationStateChanged()
    {
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
        OnPropertyChanged(nameof(CanGoUp));
    }

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
            _appSettingsRepository.Save(new AppSettings
            {
                RootPaths = normalizedRootPaths,
                ExcludedPaths = _excludedPaths.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
                FullScanIntervalHours = _fullScanIntervalHours
            });
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
            || !RootFolders.Any(x => IsAncestorOrSamePath(x.Path, CurrentPath)))
        {
            NavigateTo(currentRoot.Path, false);
        }
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
                normalized = NormalizePath(path);
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

    private static bool IsAncestorOrSamePath(string ancestorPath, string targetPath)
    {
        var normalizedAncestor = NormalizePath(ancestorPath);
        var normalizedTarget = NormalizePath(targetPath);

        if (string.Equals(normalizedAncestor, normalizedTarget, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var prefix = normalizedAncestor.EndsWith(Path.DirectorySeparatorChar)
            ? normalizedAncestor
            : normalizedAncestor + Path.DirectorySeparatorChar;

        return normalizedTarget.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static int NormalizeFullScanIntervalHours(int hours)
    {
        return hours > 0 ? hours : AppSettings.DefaultFullScanIntervalHours;
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
                normalized = NormalizePath(path);
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

    private bool IsExcludedPath(string path)
    {
        var normalizedPath = NormalizePath(path);
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

            var prefix = excludedPath.EndsWith(Path.DirectorySeparatorChar)
                ? excludedPath
                : excludedPath + Path.DirectorySeparatorChar;

            if (normalizedPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
