using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ParallelScope.Data;

public class ParallelScopeDbContextFactory : IDesignTimeDbContextFactory<ParallelScopeDbContext>
{
    public ParallelScopeDbContext CreateDbContext(string[] args)
    {
        var appDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ParallelScope");
        Directory.CreateDirectory(appDataDir);

        var dbPath = Path.Combine(appDataDir, "ParallelScope.sqlite");
        var options = new DbContextOptionsBuilder<ParallelScopeDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        return new ParallelScopeDbContext(options);
    }
}
