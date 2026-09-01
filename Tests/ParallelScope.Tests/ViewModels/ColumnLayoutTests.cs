using ParallelScope.Data;
using ParallelScope.Tests.TestSupport;
using ParallelScope.Utilities;
using ParallelScope.ViewModels;

namespace ParallelScope.Tests.ViewModels;

/// <summary>ファイル一覧の列レイアウト（並び順・列幅）の保存とリセットの確認。</summary>
public class ColumnLayoutTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly FileCacheRepository _fileCacheRepository;
    private readonly AppSettingsRepository _settingsRepository;

    public ColumnLayoutTests()
    {
        _fileCacheRepository = new FileCacheRepository(_temp.Path);
        _settingsRepository = new AppSettingsRepository(_temp.Path);
        _settingsRepository.Save(new AppSettings { RootPaths = { @"C:\ColumnTest" } });
    }

    public void Dispose()
    {
        _fileCacheRepository.ReleasePooledConnections();
        _temp.Dispose();
    }

    private MainWindowViewModel CreateViewModel()
    {
        return new MainWindowViewModel(_fileCacheRepository, _settingsRepository);
    }

    [Fact]
    public void SaveColumnLayout_KeepsWidthsUntilTheyAreReset()
    {
        var viewModel = CreateViewModel();

        viewModel.SaveColumnLayout(
            viewModel.GetColumnOrder(),
            new Dictionary<string, double> { [FileListColumns.Size] = 123 });

        Assert.Equal(123, viewModel.GetColumnWidths()[FileListColumns.Size]);
        Assert.Equal(123, _settingsRepository.Load().ColumnWidths?[FileListColumns.Size]);
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
        var saved = _settingsRepository.Load();
        Assert.Empty(saved.ColumnWidths ?? new Dictionary<string, double>());
        Assert.Equal(reorderedColumns, saved.ColumnOrder);
    }
}
