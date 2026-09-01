using System.IO;
using ParallelScope.Utilities;

namespace ParallelScope.Tests.Utilities;

/// <summary>隠し属性・システム属性のファイル/フォルダを表示するかどうかの判定の確認。</summary>
public class HiddenItemVisibilityTests
{
    private static int Attributes(FileAttributes attributes) => (int)attributes;

    [Fact]
    public void IsVisible_HidesHiddenItemsWhenTurnedOff()
    {
        Assert.False(HiddenItemVisibility.IsVisible(Attributes(FileAttributes.Hidden), showHiddenItems: false, showSystemItems: false));
    }

    [Fact]
    public void IsVisible_HidesSystemItemsWhenTurnedOff()
    {
        Assert.False(HiddenItemVisibility.IsVisible(Attributes(FileAttributes.System), showHiddenItems: false, showSystemItems: false));
    }

    [Fact]
    public void IsVisible_ShowsNormalItems()
    {
        Assert.True(HiddenItemVisibility.IsVisible(Attributes(FileAttributes.Archive), showHiddenItems: false, showSystemItems: false));
    }

    [Fact]
    public void IsVisible_ShowsHiddenItemsWhenEnabled()
    {
        Assert.True(HiddenItemVisibility.IsVisible(Attributes(FileAttributes.Hidden), showHiddenItems: true, showSystemItems: false));
    }

    [Fact]
    public void IsVisible_KeepsHidingSystemItemsWhenOnlyHiddenIsEnabled()
    {
        // 隠し属性とシステム属性の両方を持つ項目（pagefile.sys 等）は、システム側の設定でも許可されるまで出さない
        var attributes = Attributes(FileAttributes.Hidden | FileAttributes.System);

        Assert.False(HiddenItemVisibility.IsVisible(attributes, showHiddenItems: true, showSystemItems: false));
        Assert.True(HiddenItemVisibility.IsVisible(attributes, showHiddenItems: true, showSystemItems: true));
    }

    [Fact]
    public void IsVisible_ShowsEntriesWithoutStoredAttributes()
    {
        // 属性列の追加前に書かれたキャッシュ行は、次のスキャンで埋まるまで表示する
        Assert.True(HiddenItemVisibility.IsVisible(null, showHiddenItems: false, showSystemItems: false));
    }

    [Fact]
    public void GetAttributesToSkip_SkipsHiddenAndSystemWhenTurnedOff()
    {
        var attributesToSkip = HiddenItemVisibility.GetAttributesToSkip(showHiddenItems: false, showSystemItems: false);

        Assert.True(attributesToSkip.HasFlag(FileAttributes.Hidden));
        Assert.True(attributesToSkip.HasFlag(FileAttributes.System));
        Assert.True(attributesToSkip.HasFlag(FileAttributes.ReparsePoint));
    }

    [Fact]
    public void GetAttributesToSkip_AlwaysSkipsReparsePoints()
    {
        // シンボリックリンクをたどると循環しうるため、表示設定に関わらず飛ばす
        var attributesToSkip = HiddenItemVisibility.GetAttributesToSkip(showHiddenItems: true, showSystemItems: true);

        Assert.Equal(FileAttributes.ReparsePoint, attributesToSkip);
    }
}
