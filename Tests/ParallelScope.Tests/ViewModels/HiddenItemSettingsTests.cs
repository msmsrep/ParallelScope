using ParallelScope.Data;
using ParallelScope.Tests.TestSupport;
using ParallelScope.ViewModels;

namespace ParallelScope.Tests.ViewModels;

/// <summary>隠し属性・システム属性の表示設定が反映され、保存・復元されることの確認。</summary>
[Collection(SharedStateCollection.Name)]
public class HiddenItemSettingsTests : ShellTestBase
{
    public HiddenItemSettingsTests()
    {
        SettingsRepository.Save(new AppSettings { RootPaths = { @"C:\HiddenItemTest" } });
    }

    private static void ApplyHiddenItemSettings(MainWindowViewModel viewModel, bool showHiddenItems, bool showSystemItems)
    {
        viewModel.ApplySettings(
            viewModel.GetConfiguredRootPaths(),
            viewModel.GetExcludedPaths(),
            viewModel.GetFullScanIntervalHours(),
            viewModel.GetVisibleColumns(),
            viewModel.GetColumnOrder(),
            viewModel.GetVisibleTreeNodes(),
            viewModel.GetTreeNodeOrder(),
            showHiddenItems,
            showSystemItems);
    }

    [Fact]
    public void DefaultsToShowingHiddenAndSystemItems()
    {
        // この設定を入れる前と同じ見え方（全部見える）を既定にする。更新で見えていたファイルが消えないため
        var viewModel = CreateViewModel();

        Assert.True(viewModel.GetShowHiddenItems());
        Assert.True(viewModel.GetShowSystemItems());
    }

    [Fact]
    public void ApplySettings_SavesAndRestoresTheSetting()
    {
        var viewModel = CreateViewModel();

        ApplyHiddenItemSettings(viewModel, showHiddenItems: true, showSystemItems: false);

        var restarted = CreateViewModel();
        Assert.True(restarted.GetShowHiddenItems());
        Assert.False(restarted.GetShowSystemItems());
    }

    [Fact]
    public void SettingAppliesWithoutPlus()
    {
        // 無料版でも使える設定なので、購読状態が変わっても表示条件は動かない
        var viewModel = CreateViewModel();
        ApplyHiddenItemSettings(viewModel, showHiddenItems: false, showSystemItems: false);

        viewModel.SetPlusFeaturesEnabled(true);
        viewModel.SetPlusFeaturesEnabled(false);

        Assert.False(viewModel.GetShowHiddenItems());
        Assert.False(viewModel.GetShowSystemItems());
        Assert.False(SettingsRepository.Load().ShowHiddenItems);
        Assert.True(FolderItemViewModel.AttributesToSkip.HasFlag(System.IO.FileAttributes.Hidden));
    }

    [Fact]
    public void TreeEnumerationFollowsTheSetting()
    {
        var viewModel = CreateViewModel();

        ApplyHiddenItemSettings(viewModel, showHiddenItems: true, showSystemItems: true);
        Assert.Equal(System.IO.FileAttributes.ReparsePoint, FolderItemViewModel.AttributesToSkip);

        ApplyHiddenItemSettings(viewModel, showHiddenItems: false, showSystemItems: false);
        Assert.True(FolderItemViewModel.AttributesToSkip.HasFlag(System.IO.FileAttributes.Hidden));
        Assert.True(FolderItemViewModel.AttributesToSkip.HasFlag(System.IO.FileAttributes.System));
    }
}
