using ParallelScope.Utilities;

namespace ParallelScope.Tests.Utilities;

public class FileListColumnsTests
{
    [Fact]
    public void AllColumns_IsNameFollowedByOptionalColumns()
    {
        Assert.Equal(
            new[] { FileListColumns.Name }.Concat(FileListColumns.OptionalColumns),
            FileListColumns.AllColumns);
    }

    [Fact]
    public void OptionalColumns_DoesNotContainName()
    {
        // Name列は表示/非表示を切り替えられない（並び順・列幅の対象にはなる）
        Assert.DoesNotContain(FileListColumns.Name, FileListColumns.OptionalColumns);
    }

    [Fact]
    public void DefaultVisibleColumns_AreAllOptionalColumns()
    {
        Assert.All(FileListColumns.DefaultVisibleColumns, column =>
            Assert.Contains(column, FileListColumns.OptionalColumns));
    }

    [Fact]
    public void AllColumns_HasNoDuplicateKeys()
    {
        // キーは settings.json に保存されるため、重複すると列の並び順・列幅の復元が壊れる
        Assert.Equal(FileListColumns.AllColumns.Count, FileListColumns.AllColumns.Distinct(StringComparer.Ordinal).Count());
    }
}
