using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using ParallelScope.Data;
using ParallelScope.Utilities;

namespace ParallelScope.ViewModels;

/// <summary>フルスキャン・フォルダ単位スキャンなど、ファイルシステムを読み取ってキャッシュへ書き込む処理。</summary>
public partial class MainWindowViewModel
{
    private static readonly EnumerationOptions NonRecursiveEnumerationOptions = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint
    };

    /// <summary>設定済みの全ルートフォルダをフルスキャンし、キャッシュを更新する。</summary>
    public async Task<int> FullScanConfiguredRootsAsync(CancellationToken token)
    {
        var configuredRootPaths = RootFolders
            .Select(x => PathNormalizer.Normalize(x.Path))
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
        var updatedFolderCount = await Task.Run(() => ScanFolderSubtrees(rootPaths, token), token);

        // キャンセル検知はバックグラウンドスレッド内で例外を投げず、呼び出し元に戻ってから行う
        // （Task.Run内で例外を投げると、デバッガが「ユーザーコードで未処理」として誤検知することがあるため）
        token.ThrowIfCancellationRequested();
        return updatedFolderCount;
    }

    /// <summary>指定フォルダ配下をスキャンし、キャッシュを更新する（右クリックメニューからの単一フォルダスキャン用）。</summary>
    public Task<int> ScanFolderSubtreeAsync(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath) || IsExcludedPath(folderPath))
        {
            return Task.FromResult(0);
        }

        return Task.Run(() => ScanFolderSubtrees(new[] { folderPath }));
    }

    /// <summary>深さ優先でフォルダツリーを走査し、100フォルダごとにバッチでキャッシュへ保存する。</summary>
    private int ScanFolderSubtrees(IReadOnlyCollection<string> rootPaths, CancellationToken token = default)
    {
        var visitedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pendingDirectories = new Stack<string>(rootPaths.Reverse());
        var batchEntries = new Dictionary<string, IReadOnlyCollection<CachedFileSystemEntry>>();
        var updatedFolderCount = 0;
        const int BatchSize = 100;

        while (pendingDirectories.Count > 0)
        {
            if (token.IsCancellationRequested)
            {
                // ここで例外を投げず早期リターンする（キャンセル通知は呼び出し元で行う）
                return updatedFolderCount;
            }

            var currentPath = pendingDirectories.Pop();
            string normalizedPath;

            try
            {
                normalizedPath = PathNormalizer.Normalize(currentPath);
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

    /// <summary>1フォルダ直下のファイル/フォルダを並列列挙し、種別→名前の順でソートして返す。</summary>
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
}
