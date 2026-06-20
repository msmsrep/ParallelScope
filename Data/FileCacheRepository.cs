using System.IO;
using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;

namespace ParallelScope.Data;

public sealed record CachedFileSystemEntry(
    string ParentPath,
    string FullPath,
    string Name,
    bool IsFolder,
    long? SizeBytes,
    DateTime LastWriteTimeUtc);

public class FileCacheRepository
{
    private static readonly ConcurrentDictionary<string, object> ParentPathLocks = new(StringComparer.OrdinalIgnoreCase);

    private readonly DbContextOptions<ParallelScopeDbContext> _dbOptions;

    public FileCacheRepository()
    {
        string appDataDir;

        bool isMsix = Environment.ProcessPath?.Contains(@"\WindowsApps\") ?? false;

        if (isMsix)
        {
            // MSIX の LocalState
            appDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Packages",
                "msmsrep.ParallelScope_77t1an0ygyrva",
                "LocalState");
        }
        else
        {
            // 通常のローカルフォルダ
            appDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ParallelScope");
        }

        Directory.CreateDirectory(appDataDir);

        var dbPath = Path.Combine(appDataDir, "ParallelScope.sqlite");

        _dbOptions = new DbContextOptionsBuilder<ParallelScopeDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        using var db = CreateDbContext();
        db.Database.Migrate();
    }

    public List<CachedFileSystemEntry> GetEntriesByParentPath(string parentPath)
    {
        using var db = CreateDbContext();

        return db.FileSystemEntries
            .AsNoTracking()
            .Where(x => x.ParentPath == parentPath)
            .OrderByDescending(x => x.IsFolder)
            .ThenBy(x => x.Name)
            .Select(x => new CachedFileSystemEntry(
                x.ParentPath,
                x.FullPath,
                x.Name,
                x.IsFolder,
                x.SizeBytes,
                x.LastWriteTimeUtc))
            .ToList();
    }

    public List<CachedFileSystemEntry> SearchEntriesUnderPath(string rootPath, string nameQuery)
    {
        using var db = CreateDbContext();

        var normalizedRootPath = NormalizePath(rootPath);
        var rootWithSeparator = normalizedRootPath.EndsWith(Path.DirectorySeparatorChar)
            ? normalizedRootPath
            : normalizedRootPath + Path.DirectorySeparatorChar;

        return db.FileSystemEntries
            .AsNoTracking()
            .Where(x => x.FullPath.StartsWith(rootWithSeparator) && x.Name.Contains(nameQuery))
            .OrderByDescending(x => x.IsFolder)
            .ThenBy(x => x.Name)
            .Select(x => new CachedFileSystemEntry(
                x.ParentPath,
                x.FullPath,
                x.Name,
                x.IsFolder,
                x.SizeBytes,
                x.LastWriteTimeUtc))
            .ToList();
    }

    public void ReplaceEntriesByParentPath(string parentPath, IReadOnlyCollection<CachedFileSystemEntry> entries)
    {
        var normalizedParentPath = NormalizePath(parentPath);
        var lockObject = ParentPathLocks.GetOrAdd(normalizedParentPath, _ => new object());

        lock (lockObject)
        {
            ReplaceEntriesByParentPathInternal(normalizedParentPath, entries);
        }
    }

    private void ReplaceEntriesByParentPathInternal(string normalizedParentPath, IReadOnlyCollection<CachedFileSystemEntry> entries)
    {
        using var db = CreateDbContext();

        // 追跡済みエンティティ削除の競合を避けるため、対象親パスを一括削除する
        db.Database.ExecuteSqlInterpolated($"DELETE FROM FileSystemEntries WHERE ParentPath = {normalizedParentPath}");

        if (entries.Count > 0)
        {
            var entities = entries.Select(x => new FileSystemEntryEntity
            {
                ParentPath = normalizedParentPath,
                FullPath = x.FullPath,
                Name = x.Name,
                IsFolder = x.IsFolder,
                SizeBytes = x.SizeBytes,
                LastWriteTimeUtc = x.LastWriteTimeUtc
            });

            db.FileSystemEntries.AddRange(entities);
        }

        db.SaveChanges();
    }

    public Dictionary<string, long> GetCachedFolderTotalSizes(string parentPath, IEnumerable<string> folderPaths)
    {
        using var db = CreateDbContext();

        if (string.IsNullOrWhiteSpace(parentPath))
        {
            return new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        }

        var normalizedParentPath = NormalizePath(parentPath);
        var parentWithSeparator = normalizedParentPath.EndsWith(Path.DirectorySeparatorChar)
            ? normalizedParentPath
            : normalizedParentPath + Path.DirectorySeparatorChar;

        var folderNameToPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in folderPaths)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            string normalizedPath;
            try
            {
                normalizedPath = NormalizePath(path);
            }
            catch
            {
                continue;
            }

            if (!normalizedPath.StartsWith(parentWithSeparator, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var relative = normalizedPath.Substring(parentWithSeparator.Length);
            if (string.IsNullOrWhiteSpace(relative))
            {
                continue;
            }

            var separatorIndex = relative.IndexOfAny(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar });
            var firstSegment = separatorIndex >= 0 ? relative[..separatorIndex] : relative;

            if (!string.IsNullOrWhiteSpace(firstSegment))
            {
                folderNameToPath[firstSegment] = normalizedPath;
            }
        }

        if (folderNameToPath.Count == 0)
        {
            return new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        }

        var cachedFiles = db.FileSystemEntries
            .AsNoTracking()
            .Where(x => !x.IsFolder && x.FullPath.StartsWith(parentWithSeparator))
            .Select(x => new
            {
                x.FullPath,
                SizeBytes = x.SizeBytes ?? 0L
            })
            .ToList();

        var result = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in cachedFiles)
        {
            if (file.FullPath.Length <= parentWithSeparator.Length)
            {
                continue;
            }

            var relative = file.FullPath.Substring(parentWithSeparator.Length);
            if (string.IsNullOrWhiteSpace(relative))
            {
                continue;
            }

            var separatorIndex = relative.IndexOfAny(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar });
            if (separatorIndex <= 0)
            {
                // 親フォルダ直下のファイルは子フォルダ合計に含めない
                continue;
            }

            var firstSegment = relative[..separatorIndex];
            if (!folderNameToPath.TryGetValue(firstSegment, out var folderFullPath))
            {
                continue;
            }

            result.TryGetValue(folderFullPath, out var currentSize);
            result[folderFullPath] = currentSize + file.SizeBytes;
        }

        return result;
    }

    private ParallelScopeDbContext CreateDbContext()
    {
        return new ParallelScopeDbContext(_dbOptions);
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
}
