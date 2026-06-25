using Microsoft.EntityFrameworkCore;

namespace ParallelScope.Data;

public class ParallelScopeDbContext : DbContext
{
    public DbSet<FileSystemEntryEntity> FileSystemEntries => Set<FileSystemEntryEntity>();

    public ParallelScopeDbContext(DbContextOptions<ParallelScopeDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<FileSystemEntryEntity>(entity =>
        {
            entity.ToTable("FileSystemEntries");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.ParentPath).IsRequired();
            entity.Property(x => x.FullPath).IsRequired();
            entity.Property(x => x.Name).IsRequired();

            // インデックス最適化
            entity.HasIndex(x => x.ParentPath);
            entity.HasIndex(x => new { x.ParentPath, x.Name });
            entity.HasIndex(x => x.FullPath).IsUnique();

            // 検索用インデックス（大文字小文字を区別しないスキャン用）
            entity.HasIndex(x => x.Name);
            entity.HasIndex(x => new { x.IsFolder, x.Name });
        });
    }
}
