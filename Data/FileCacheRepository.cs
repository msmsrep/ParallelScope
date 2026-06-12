using System.IO;
using Microsoft.EntityFrameworkCore;

namespace ParallelFiler.Data;

public sealed record CachedFileSystemEntry(
    string ParentPath,
    string FullPath,
    string Name,
    bool IsFolder,
    long? SizeBytes,
    DateTime LastWriteTimeUtc);

public class FileCacheRepository
{
    private readonly DbContextOptions<ParallelFilerDbContext> _dbOptions;

    public FileCacheRepository()
    {
        var appDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ParallelFiler");
        Directory.CreateDirectory(appDataDir);

        var dbPath = Path.Combine(appDataDir, "parallelfiler.sqlite");
        _dbOptions = new DbContextOptionsBuilder<ParallelFilerDbContext>()
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

    private ParallelFilerDbContext CreateDbContext()
    {
        return new ParallelFilerDbContext(_dbOptions);
    }
}
