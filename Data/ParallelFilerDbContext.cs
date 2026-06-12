using Microsoft.EntityFrameworkCore;

namespace ParallelFiler.Data;

public class ParallelFilerDbContext : DbContext
{
    public DbSet<FileSystemEntryEntity> FileSystemEntries => Set<FileSystemEntryEntity>();

    public ParallelFilerDbContext(DbContextOptions<ParallelFilerDbContext> options)
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

            entity.HasIndex(x => x.ParentPath);
            entity.HasIndex(x => new { x.ParentPath, x.Name });
            entity.HasIndex(x => x.FullPath).IsUnique();
        });
    }
}
