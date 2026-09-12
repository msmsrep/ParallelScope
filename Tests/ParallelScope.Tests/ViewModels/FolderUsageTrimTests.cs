using System.IO;
using ParallelScope.Data;
using ParallelScope.Tests.TestSupport;
using ParallelScope.ViewModels;

namespace ParallelScope.Tests.ViewModels;

/// <summary>
/// アクセス実績（「最近」「よく使う」の元データ）の件数が上限で頭打ちになることの確認。
/// 上限が無いと訪問したフォルダの数だけ settings.json が際限なく育つ。
/// </summary>
[Collection(FolderTreeCollection.Name)]
public class FolderUsageTrimTests : IDisposable
{
    private const int Cap = MainWindowViewModel.MaxFolderUsages;

    private readonly TempDirectory _temp = new();
    private readonly FileCacheRepository _fileCacheRepository;
    private readonly AppSettingsRepository _settingsRepository;

    public FolderUsageTrimTests()
    {
        _fileCacheRepository = new FileCacheRepository(_temp.Path);
        _settingsRepository = new AppSettingsRepository(_temp.Path);
    }

    public void Dispose()
    {
        _fileCacheRepository.ReleasePooledConnections();
        _temp.Dispose();
    }

    /// <summary>アクセス実績を保存してからViewModelを起動する（読み込み時に絞られる）。</summary>
    private MainWindowViewModel Start(IEnumerable<FolderUsageEntry> usages)
    {
        _settingsRepository.Save(new AppSettings
        {
            RootPaths = { @"C:\UsageTest" },
            FolderUsages = usages.ToList()
        });

        var viewModel = new MainWindowViewModel(_fileCacheRepository, _settingsRepository);
        viewModel.SetPlusFeaturesEnabled(true);
        return viewModel;
    }

    /// <summary>iが小さいほど最終アクセスが新しいエントリ。</summary>
    private static FolderUsageEntry Usage(int i, int count = 1) => new()
    {
        Path = FolderPath(i),
        Count = count,
        LastAccessedAt = new DateTime(2026, 1, 1).AddMinutes(-i)
    };

    private static string FolderPath(int i) => $@"C:\UsageTest\Folder{i:D4}";

    private static bool Contains(IEnumerable<FolderUsageEntry> usages, int i) =>
        usages.Any(x => string.Equals(x.Path, FolderPath(i), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// 読み込み時に絞った結果は、次に設定が保存されたときにファイルへ反映される
    /// （起動しただけでは書き戻さない）。実アプリでは最初の移動やタブ操作で保存が走る。
    /// ここでは実績と無関係な設定を1つ変えて保存を起こす。
    /// </summary>
    private IReadOnlyList<FolderUsageEntry> SaveAndReload(MainWindowViewModel viewModel)
    {
        viewModel.SetRegexSearchEnabled(true);
        return _settingsRepository.Load().FolderUsages;
    }

    [Fact]
    public void LoadedUsages_AreCappedKeepingTheMostRecent()
    {
        // 上限の3倍を、最終アクセスが新しい順（i=0が最新）に用意する
        var viewModel = Start(Enumerable.Range(0, Cap * 3).Select(i => Usage(i)));

        var saved = SaveAndReload(viewModel);

        Assert.Equal(Cap, saved.Count);
        Assert.True(Contains(saved, 0));
        Assert.True(Contains(saved, Cap - 1));
        Assert.False(Contains(saved, Cap));
        Assert.False(Contains(saved, Cap * 3 - 1));
    }

    /// <summary>回数の多いフォルダは、最近触っていなくても残る（捨てると回数が数え直しになるため）。</summary>
    [Fact]
    public void FrequentlyUsedFolders_SurviveTheCapEvenWhenStale()
    {
        // 最終アクセスが最も古いエントリの回数だけ突出させる
        const int staleIndex = Cap * 3 - 1;
        var usages = Enumerable.Range(0, staleIndex)
            .Select(i => Usage(i))
            .Append(Usage(staleIndex, count: 999));

        var viewModel = Start(usages);

        var saved = SaveAndReload(viewModel);

        Assert.Equal(Cap, saved.Count);
        Assert.True(Contains(saved, staleIndex));
        // 「よく使う」の先頭に出続ける
        Assert.Equal(FolderPath(staleIndex), viewModel.GetFrequentPaths().First());
    }

    /// <summary>上限に達していなければ何も捨てない。</summary>
    [Fact]
    public void UsagesUnderTheCap_AreKeptAsIs()
    {
        var viewModel = Start(Enumerable.Range(0, Cap).Select(i => Usage(i)));

        Assert.Equal(Cap, SaveAndReload(viewModel).Count);
    }

    /// <summary>新しいフォルダへ移動し続けても、件数は上限を超えない。</summary>
    [Fact]
    public void NavigatingToNewFolders_DoesNotGrowBeyondTheCap()
    {
        var root = Directory.CreateDirectory(Path.Combine(_temp.Path, "Roots")).FullName;
        _settingsRepository.Save(new AppSettings { RootPaths = { root } });

        var viewModel = new MainWindowViewModel(_fileCacheRepository, _settingsRepository);
        viewModel.SetPlusFeaturesEnabled(true);

        for (var i = 0; i < Cap + 25; i++)
        {
            var folder = Directory.CreateDirectory(Path.Combine(root, $"F{i:D4}")).FullName;
            // ユーザー操作による移動として記録させる（LoadFiles は履歴に積む＝実績に数える経路）
            viewModel.ActiveTab.LoadFiles(folder);
        }

        Assert.Equal(Cap, _settingsRepository.Load().FolderUsages.Count);
    }
}
