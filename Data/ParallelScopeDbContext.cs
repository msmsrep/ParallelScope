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

            // 一覧表示・親パス単位の置き換え/削除・孤児掃除で使用
            entity.HasIndex(x => x.ParentPath);
            // クエリでは未使用だが、キャッシュ置き換えロジックの不具合で重複行が
            // 蓄積するのを防ぐ整合性制約として維持する
            entity.HasIndex(x => x.FullPath).IsUnique();
            // かつて存在した (ParentPath,Name)・Name・(IsFolder,Name) は、どのクエリの
            // 実行計画にも使われずパス文字列の重複保存でDBを肥大化させていたため削除した。
            // All Files・検索用の (IsFolder, FullPath) は FileCacheRepository の
            // PRAGMA 適用時に生SQLで作成している
        });
    }
}
