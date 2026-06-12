using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ParallelFiler.Data;

public class ParallelFilerDbContextFactory : IDesignTimeDbContextFactory<ParallelFilerDbContext>
{
    public ParallelFilerDbContext CreateDbContext(string[] args)
    {
        var appDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ParallelFiler");
        Directory.CreateDirectory(appDataDir);

        var dbPath = Path.Combine(appDataDir, "parallelfiler.sqlite");
        var options = new DbContextOptionsBuilder<ParallelFilerDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        return new ParallelFilerDbContext(options);
    }
}
