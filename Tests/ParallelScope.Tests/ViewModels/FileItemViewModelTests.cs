using System.Globalization;
using ParallelScope.ViewModels;

namespace ParallelScope.Tests.ViewModels;

public class FileItemViewModelTests
{
    public FileItemViewModelTests()
    {
        // サイズ表示の小数点記号がカルチャ依存（"1.5" / "1,5"）になるため、期待値を固定できるよう揃える
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
    }

    [Fact]
    public void FileConstructor_DerivesLocationFromFullPath()
    {
        var item = new FileItemViewModel(@"C:\Root\Sub\a.txt", "a.txt", 2048, new DateTime(2026, 1, 2, 3, 4, 5));

        Assert.Equal(@"C:\Root\Sub", item.Location);
        Assert.Equal(@"C:\Root\Sub\a.txt", item.FullPath);
        Assert.False(item.IsFolder);
        Assert.Equal(2048, item.SizeBytes);
        Assert.Equal("2 KB", item.SizeText);
        Assert.Equal("2026-01-02 03:04:05", item.ModifiedTime);
    }

    [Fact]
    public void FileConstructor_SharesGivenLocationInstance()
    {
        // 数十万件分のパス文字列を作らないよう、呼び出し側が渡した location インスタンスをそのまま共有する
        var location = @"C:\Root\Sub";
        var item = new FileItemViewModel(@"C:\Root\Sub\a.txt", "a.txt", 1, DateTime.MinValue, location);

        Assert.Same(location, item.Location);
    }

    [Fact]
    public void FolderConstructor_LeavesSizeEmptyWhenCachedTotalIsUnknown()
    {
        var item = new FileItemViewModel(@"C:\Root\Sub", "Sub", new DateTime(2026, 1, 2));

        Assert.True(item.IsFolder);
        Assert.Null(item.SizeBytes);
        Assert.Equal(string.Empty, item.SizeText);
    }

    [Fact]
    public void FolderConstructor_ShowsCachedTotalSize()
    {
        var item = new FileItemViewModel(@"C:\Root\Sub", "Sub", new DateTime(2026, 1, 2), 1024);

        Assert.Equal("1 KB", item.SizeText);
    }

    [Fact]
    public void ModifiedTime_IsEmptyForMinValue()
    {
        // 仮想「Folders」のルート行など、更新日時を表示しない行は MinValue で表す
        var item = new FileItemViewModel(@"C:\Root", "Root", DateTime.MinValue);

        Assert.Equal(string.Empty, item.ModifiedTime);
    }

    [Fact]
    public void CreatedTime_IsEmptyUntilCreatedAtIsSet()
    {
        var item = new FileItemViewModel(@"C:\Root\a.txt", "a.txt", 1, new DateTime(2026, 1, 1));

        Assert.Equal(string.Empty, item.CreatedTime);

        item.CreatedAt = new DateTime(2026, 2, 3, 4, 5, 6);

        Assert.Equal("2026-02-03 04:05:06", item.CreatedTime);
    }

    [Fact]
    public void SizeBytes_RaisesChangeNotificationForSizeText()
    {
        // 表示用文字列は保持せず都度生成するため、生値の変更時に表示側へ通知が要る
        var item = new FileItemViewModel(@"C:\Root\a.txt", "a.txt", 1, new DateTime(2026, 1, 1));
        var changed = new List<string?>();
        item.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        item.SizeBytes = 2048;

        Assert.Contains(nameof(FileItemViewModel.SizeBytes), changed);
        Assert.Contains(nameof(FileItemViewModel.SizeText), changed);
    }

    [Fact]
    public void ModifiedAt_RaisesChangeNotificationForModifiedTime()
    {
        var item = new FileItemViewModel(@"C:\Root\a.txt", "a.txt", 1, new DateTime(2026, 1, 1));
        var changed = new List<string?>();
        item.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        item.ModifiedAt = new DateTime(2026, 3, 4);

        Assert.Contains(nameof(FileItemViewModel.ModifiedTime), changed);
    }
}
