using System.IO;
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
        using var db = CreateDbContext();

        var oldEntries = db.FileSystemEntries
            .Where(x => x.ParentPath == parentPath)
            .ToList();

        if (oldEntries.Count > 0)
        {
            db.FileSystemEntries.RemoveRange(oldEntries);
        }

        if (entries.Count > 0)
        {
            var entities = entries.Select(x => new FileSystemEntryEntity
            {
                ParentPath = x.ParentPath,
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
