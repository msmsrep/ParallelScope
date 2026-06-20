using System.Collections.ObjectModel;
using System.Collections.Generic;
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
    private string _searchSummary = string.Empty;
    private readonly Stack<string> _backHistory = new();
    private readonly Stack<string> _forwardHistory = new();
    private readonly FileCacheRepository _fileCacheRepository;
    private readonly AppSettingsRepository _appSettingsRepository;
    private readonly SynchronizationContext _uiContext;
    private int _navigationVersion;
    private int _searchVersion;
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

    public string SearchSummary
    {
        get => _searchSummary;
        private set => SetProperty(ref _searchSummary, value);
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
        ApplyRootPaths(settings.RootPaths, false);
    }

    public IReadOnlyList<string> GetConfiguredRootPaths()
    {
        return RootFolders.Select(x => x.Path).ToList();
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

        SearchSummary = $"Searching \"{normalizedQuery}\" in {searchRootPath}...";
        ReplaceVisibleFileItems(Array.Empty<FileItemViewModel>());
        _ = SearchInBackground(searchRootPath, normalizedQuery, searchVersion);
        return true;
    }

    public Task<int> FullScanConfiguredRootsAsync()
    {
        var configuredRootPaths = RootFolders
            .Select(x => x.Path)
            .Where(path => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Task.Run(() => FullScanConfiguredRoots(configuredRootPaths));
    }

    public void ClearSearch()
    {
        Interlocked.Increment(ref _searchVersion);
        SearchSummary = string.Empty;
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

            LoadFromCache(folderPath);

            var navigationVersion = Interlocked.Increment(ref _navigationVersion);
            _ = RefreshFromFileSystemInBackground(folderPath, navigationVersion);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void LoadFromCache(string folderPath)
    {
        try
        {
            var cachedEntries = _fileCacheRepository.GetEntriesByParentPath(folderPath);
            UpdateCurrentDirectoryItems(cachedEntries.Select(ToViewModel));
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
            SearchSummary = $"Cache search: {cacheResults.Count} hit(s) ({rootPath})";
        }, null);

        if (cacheResults.Count > 0)
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

            SearchSummary = $"No cache matches. Searching file system... ({rootPath})";
        }, null);

        List<FileItemViewModel> fullScanResults;
        try
        {
            fullScanResults = await Task.Run(() => SearchEntriesFromFileSystem(rootPath, query));
        }
        catch
        {
            fullScanResults = new List<FileItemViewModel>();
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

            ReplaceVisibleFileItems(fullScanResults);
            SearchSummary = $"File system search: {fullScanResults.Count} hit(s) ({rootPath})";
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
        }, null);
    }

    private static List<CachedFileSystemEntry> ReadEntriesFromFileSystem(string folderPath)
    {
        var dirInfo = new DirectoryInfo(folderPath);

        var folders = dirInfo
            .EnumerateDirectories("*", NonRecursiveEnumerationOptions)
            .OrderBy(d => d.Name)
            .Select(d => TryCreateFolderEntry(folderPath, d))
            .Where(e => e is not null)
            .Cast<CachedFileSystemEntry>();

        var files = dirInfo
            .EnumerateFiles("*", NonRecursiveEnumerationOptions)
            .OrderBy(f => f.Name)
            .Select(f => TryCreateFileEntry(folderPath, f))
            .Where(e => e is not null)
            .Cast<CachedFileSystemEntry>();

        return folders.Concat(files).ToList();
    }

    private static List<FileItemViewModel> SearchEntriesFromFileSystem(string rootPath, string query)
    {
        var results = new List<FileItemViewModel>();
        var comparison = StringComparison.OrdinalIgnoreCase;

        foreach (var directoryPath in Directory.EnumerateDirectories(rootPath, "*", new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        }).OrderBy(path => path))
        {
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

        foreach (var filePath in Directory.EnumerateFiles(rootPath, "*", new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        }).OrderBy(path => path))
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

        return results;
    }

    private int FullScanConfiguredRoots(IReadOnlyCollection<string> rootPaths)
    {
        var visitedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pendingDirectories = new Stack<string>(rootPaths.Reverse());
        var updatedFolderCount = 0;

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

            if (!visitedDirectories.Add(normalizedPath) || !Directory.Exists(normalizedPath))
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

            try
            {
                _fileCacheRepository.ReplaceEntriesByParentPath(normalizedPath, entries);
                updatedFolderCount++;
            }
            catch
            {
                // キャッシュ保存失敗時はスキャンを継続する
            }

            foreach (var folderEntry in entries.Where(x => x.IsFolder).OrderByDescending(x => x.FullPath, StringComparer.OrdinalIgnoreCase))
            {
                pendingDirectories.Push(folderEntry.FullPath);
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
        _currentDirectoryItems = items.ToList();

        if (string.IsNullOrWhiteSpace(SearchQuery))
        {
            ReplaceVisibleFileItems(_currentDirectoryItems);
            SearchSummary = string.Empty;
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
            RootFolders.Add(new FolderItemViewModel(rootPath));
        }

        if (saveSettings)
        {
            _appSettingsRepository.Save(new AppSettings
            {
                RootPaths = normalizedRootPaths
            });
        }

        var currentRoot = RootFolders.FirstOrDefault();
        if (currentRoot is null)
        {
            CurrentPath = string.Empty;
            AddressInput = string.Empty;
            _currentDirectoryItems.Clear();
            ReplaceVisibleFileItems(Array.Empty<FileItemViewModel>());
            SearchSummary = string.Empty;
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
}
