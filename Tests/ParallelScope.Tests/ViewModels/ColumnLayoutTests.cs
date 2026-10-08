using ParallelScope.Data;
using ParallelScope.Tests.TestSupport;
using ParallelScope.Utilities;
using ParallelScope.ViewModels;

namespace ParallelScope.Tests.ViewModels;

/// <summary>ファイル一覧の列レイアウト（並び順・列幅）の保存とリセットの確認。</summary>
[Collection(SharedStateCollection.Name)]
public class ColumnLayoutTests : ShellTestBase
{
    public ColumnLayoutTests()
    {
        SettingsRepository.Save(new AppSettings { RootPaths = { @"C:\ColumnTest" } });
    }

    [Fact]
    public void SaveColumnLayout_KeepsWidthsUntilTheyAreReset()
    {
        var viewModel = CreateViewModel();

        viewModel.SaveColumnLayout(
            viewModel.GetColumnOrder(),
            new Dictionary<string, double> { [FileListColumns.Size] = 123 });

        Assert.Equal(123, viewModel.GetColumnWidths()[FileListColumns.Size]);
        Assert.Equal(123, SettingsRepository.Load().ColumnWidths?[FileListColumns.Size]);
    }

    [Fact]
    public void ResetColumnWidths_ClearsSavedWidthsButKeepsTheColumnOrder()
    {
        var viewModel = CreateViewModel();
        var reorderedColumns = viewModel.GetColumnOrder().Reverse().ToList();
        viewModel.SaveColumnLayout(
            reorderedColumns,
            new Dictionary<string, double> { [FileListColumns.Size] = 123 });

        viewModel.ResetColumnWidths();

        Assert.Empty(viewModel.GetColumnWidths());
        Assert.Equal(reorderedColumns, viewModel.GetColumnOrder());

        // リセットは設定ファイルにも反映される（再起動しても既定幅のまま）
        var saved = SettingsRepository.Load();
        Assert.Empty(saved.ColumnWidths ?? new Dictionary<string, double>());
        Assert.Equal(reorderedColumns, saved.ColumnOrder);
    }
}
