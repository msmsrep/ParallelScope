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
        _ = SearchInBackground(searchRootPath, normalizedQuery, searchVersion);
        return true;
    }

    public Task<int> FullScanConfiguredRootsAsync()
    {
        var configuredRootPaths = RootFolders
            .Select(x => x.Path)
            .Where(path => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path) && !IsExcludedPath(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Task.Run(() => ScanFolderSubtrees(configuredRootPaths));
    }

    public Task<int> FullScanConfiguredRootsWithProgressAsync()
    {
        return Task.Run(async () =>
        {
            var totalScannedFolderCount = 0;

            foreach (var rootFolder in RootFolders)
            {
                if (string.IsNullOrWhiteSpace(rootFolder.Path) || !Directory.Exists(rootFolder.Path) || IsExcludedPath(rootFolder.Path))
                {
                    continue;
                }

                // UI スレッドでスキャン開始を表示
                _uiContext.Post(_ =>
                {
                    rootFolder.IsScanning = true;
                }, null);

                try
                {
                    var scannedCount = await Task.Run(() => ScanFolderSubtrees(new[] { rootFolder.Path }));
                    totalScannedFolderCount += scannedCount;
                }
                finally
                {
                    // UI スレッドでスキャン終了を表示
                    _uiContext.Post(_ =>
                    {
                        rootFolder.IsScanning = false;
                    }, null);
                }
            }

            return totalScannedFolderCount;
        });
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
            LoadFromCache(folderPath, navigationVersion);
            _ = RefreshFromFileSystemInBackground(folderPath, navigationVersion);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void LoadFromCache(string folderPath, int navigationVersion)
    {
        try
        {
            var cachedEntries = _fileCacheRepository.GetEntriesByParentPath(folderPath);
            UpdateCurrentDirectoryItems(cachedEntries.Select(ToViewModel));
            _ = ApplyCachedFolderSizesInBackground(folderPath, cachedEntries, navigationVersion);
        }
        catch
        {
            UpdateCurrentDirectoryItems(Array.Empty<FileItemViewModel>());
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
            _ = ApplyCachedFolderSizesInBackground(folderPath, liveEntries, navigationVersion);
        }, null);
    }

    private async Task ApplyCachedFolderSizesInBackground(string folderPath, IReadOnlyCollection<CachedFileSystemEntry> entries, int navigationVersion)
    {
        var folderPaths = entries
            .Where(x => x.IsFolder)
            .Select(x => x.FullPath)
            .ToList();

        if (folderPaths.Count == 0)
        {
            return;
        }

        Dictionary<string, long> cachedFolderSizes;
        try
        {
            cachedFolderSizes = await Task.Run(() => _fileCacheRepository.GetCachedFolderTotalSizes(folderPath, folderPaths));
        }
        catch
        {
            return;
        }

        if (cachedFolderSizes.Count == 0)
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

            var fileItems = entries.Select(entry => ToViewModel(entry, cachedFolderSizes)).ToList();

            // 変更がある場合のみ更新
            if (HaveItemsChanged(_currentDirectoryItems, fileItems))
            {
                UpdateCurrentDirectoryItems(fileItems);
            }
        }, null);
    }

    private List<CachedFileSystemEntry> ReadEntriesFromFileSystem(string folderPath)
    {
        var dirInfo = new DirectoryInfo(folderPath);
        var entries = new ConcurrentBag<CachedFileSystemEntry>();

        try
        {
            var folders = dirInfo
                .EnumerateDirectories("*", NonRecursiveEnumerationOptions)
                .Where(d => !IsExcludedPath(d.FullName))
                .OrderBy(d => d.Name)
                .ToList();

            // フォルダ処理を並列化
            Parallel.ForEach(folders, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, d =>
            {
                var entry = TryCreateFolderEntry(folderPath, d);
                if (entry is not null)
                {
                    entries.Add(entry);
                }
            });

            var files = dirInfo
                .EnumerateFiles("*", NonRecursiveEnumerationOptions)
                .OrderBy(f => f.Name)
                .ToList();

            // ファイル処理を並列化
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
        return entries
            .OrderByDescending(x => x.IsFolder)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private List<FileItemViewModel> SearchEntriesFromFileSystem(string rootPath, string query)
    {
        var results = new List<FileItemViewModel>();
        var comparison = StringComparison.OrdinalIgnoreCase;

        var pendingDirectories = new Stack<string>();
        pendingDirectories.Push(rootPath);

        while (pendingDirectories.Count > 0)
        {
            var currentPath = pendingDirectories.Pop();

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
                pendingDirectories.Push(directoryPath);

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

        // キャッシュ検索と同じソート順序に統一
        return results
            .OrderByDescending(x => x.IsFolder)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private int ScanFolderSubtrees(IReadOnlyCollection<string> rootPaths)
    {
        var visitedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pendingDirectories = new Stack<string>(rootPaths.Reverse());
        var batchEntries = new Dictionary<string, IReadOnlyCollection<CachedFileSystemEntry>>();
        var updatedFolderCount = 0;
        const int BatchSize = 100;

        while (pendingDirectories.Count > 0)
        {
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
        FileItems.Clear();

        foreach (var item in items)
        {
            FileItems.Add(item);
        }
    }

    private void UpdateCurrentDirectoryItems(IEnumerable<FileItemViewModel> items)
    {
        var newItems = items.ToList();

        // 変更がない場合は何もしない
        if (!HaveItemsChanged(_currentDirectoryItems, newItems))
        {
            return;
        }

        _currentDirectoryItems = newItems;

        if (string.IsNullOrWhiteSpace(SearchQuery))
        {
            ReplaceVisibleFileItems(_currentDirectoryItems);
        }
    }

    private static bool HaveItemsChanged(IReadOnlyList<FileItemViewModel> oldItems, IReadOnlyList<FileItemViewModel> newItems)
    {
        // 数が異なる場合は変更あり
        if (oldItems.Count != newItems.Count)
        {
            return true;
        }

        // 各アイテムを比較
        for (int i = 0; i < oldItems.Count; i++)
        {
            var oldItem = oldItems[i];
            var newItem = newItems[i];

            // FullPath、名前、サイズテキスト、更新日時、フォルダフラグを比較
            if (!string.Equals(oldItem.FullPath, newItem.FullPath, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(oldItem.Name, newItem.Name, StringComparison.Ordinal)
                || !string.Equals(oldItem.SizeText, newItem.SizeText, StringComparison.Ordinal)
                || !string.Equals(oldItem.ModifiedTime, newItem.ModifiedTime, StringComparison.Ordinal)
                || oldItem.IsFolder != newItem.IsFolder)
            {
                return true;
            }
        }

        return false;
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

        RootFolders.Clear();
        foreach (var rootPath in normalizedRootPaths)
        {
            if (IsExcludedPath(rootPath))
            {
                continue;
            }

            RootFolders.Add(new FolderItemViewModel(rootPath, IsExcludedPath));
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
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (drive.IsReady)
            {
                yield return drive.RootDirectory.FullName;
            }
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
