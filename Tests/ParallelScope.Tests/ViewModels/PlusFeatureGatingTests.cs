using ParallelScope.Data;
using ParallelScope.Tests.TestSupport;
using ParallelScope.Utilities;
using ParallelScope.ViewModels;

namespace ParallelScope.Tests.ViewModels;

/// <summary>
/// 無料版とPlus（購読済み）で機能が分かれていることの確認。
/// 購読状態そのものは <see cref="ParallelScope.Services.StoreLicenseService"/> が判定し、
/// その結果が <see cref="MainWindowViewModel.SetPlusFeaturesEnabled"/> に渡ってくる前提で、
/// 渡された後の切り替わり方をテストする。
/// </summary>
[Collection(SharedStateCollection.Name)]
public class PlusFeatureGatingTests : ShellTestBase
{
    private const string FavoritePath = @"C:\PlusTest\Favorite";
    private const string FrequentPath = @"C:\PlusTest\Frequent";
    private const string RecentPath = @"C:\PlusTest\Recent";

    /// <summary>お気に入り・アクセス実績を保存済みの状態でViewModelを起動する。</summary>
    private MainWindowViewModel StartWithSavedUsage()
    {
        SettingsRepository.Save(new AppSettings
        {
            RootPaths = { @"C:\PlusTest" },
            FavoritePaths = { FavoritePath },
            FolderUsages =
            {
                // 回数はFrequentが多く、最終アクセスはRecentが新しい（それぞれのノードの並び基準を分けて確かめる）
                new FolderUsageEntry { Path = FrequentPath, Count = 5, LastAccessedAt = new DateTime(2026, 1, 1) },
                new FolderUsageEntry { Path = RecentPath, Count = 1, LastAccessedAt = new DateTime(2026, 6, 1) }
            },
            VisibleColumns = new List<string> { FileListColumns.Attributes }
        });

        return CreateViewModel();
    }

    private static IReadOnlyList<string> TreeRootPaths(MainWindowViewModel viewModel)
    {
        return viewModel.TreeRoots.Select(x => x.Path).ToList();
    }

    [Fact]
    public void FreeVersion_ShowsOnlyTheFoldersNodeInTheTree()
    {
        var viewModel = StartWithSavedUsage();

        // 起動直後はライセンス未取得のため無料版と同じ状態
        Assert.False(viewModel.ArePlusFeaturesEnabled);
        Assert.Equal(new[] { VirtualFolders.AllRootsPath }, TreeRootPaths(viewModel));
    }

    [Fact]
    public void PlusVersion_AddsFavoritesRecentAndFrequentNodesBelowFolders()
    {
        var viewModel = StartWithSavedUsage();

        viewModel.SetPlusFeaturesEnabled(true);

        Assert.True(viewModel.ArePlusFeaturesEnabled);
        Assert.Equal(
            new[] { VirtualFolders.AllRootsPath, VirtualFolders.FavoritesPath, VirtualFolders.RecentPath, VirtualFolders.FrequentPath },
            TreeRootPaths(viewModel));
    }

    [Fact]
    public void DisablingPlus_RemovesFavoritesRecentAndFrequentNodesAgain()
    {
        // 購読が切れた場合（設定画面から戻った時など）に無料版の表示へ戻ること
        var viewModel = StartWithSavedUsage();

        viewModel.SetPlusFeaturesEnabled(true);
        viewModel.SetPlusFeaturesEnabled(false);

        Assert.False(viewModel.ArePlusFeaturesEnabled);
        Assert.Equal(new[] { VirtualFolders.AllRootsPath }, TreeRootPaths(viewModel));
    }

    [Fact]
    public void SetPlusFeaturesEnabled_IsIdempotent()
    {
        // 起動時と設定画面を閉じた時など複数回呼ばれるため、同じ値で呼んでもノードが増えない
        var viewModel = StartWithSavedUsage();

        viewModel.SetPlusFeaturesEnabled(true);
        viewModel.SetPlusFeaturesEnabled(true);

        Assert.Equal(
            new[] { VirtualFolders.AllRootsPath, VirtualFolders.FavoritesPath, VirtualFolders.RecentPath, VirtualFolders.FrequentPath },
            TreeRootPaths(viewModel));
    }

    [Fact]
    public void DisablingPlus_MovesAwayFromAVirtualNodeThatIsNoLongerVisible()
    {
        var viewModel = StartWithSavedUsage();
        viewModel.SetPlusFeaturesEnabled(true);
        viewModel.LoadFiles(VirtualFolders.FavoritesPath);
        Assert.Equal(VirtualFolders.FavoritesPath, viewModel.CurrentPath);

        viewModel.SetPlusFeaturesEnabled(false);

        // 非表示にしたノードを開いたままにしない
        Assert.Equal(VirtualFolders.AllRootsPath, viewModel.CurrentPath);
    }

    [Fact]
    public void DisablingPlus_KeepsTheCurrentFolderWhenItIsARealPath()
    {
        // 移動先は実在フォルダである必要があるため、一時フォルダを使う
        var realFolder = PathNormalizer.Normalize(NewTempDirectory().Path);
        var viewModel = StartWithSavedUsage();
        viewModel.SetPlusFeaturesEnabled(true);
        Assert.True(viewModel.LoadFiles(realFolder));

        viewModel.SetPlusFeaturesEnabled(false);

        Assert.Equal(realFolder, viewModel.CurrentPath);
    }

    [Fact]
    public void FavoritesAndUsage_AreKeptWhilePlusIsDisabled()
    {
        // 購読が切れても保存済みのお気に入り・アクセス実績は消さない（購読すればそのまま復活する）
        var viewModel = StartWithSavedUsage();

        Assert.Equal(new[] { FavoritePath }, viewModel.GetFavoritePaths());
        Assert.Equal(new[] { FrequentPath, RecentPath }, viewModel.GetFrequentPaths());

        viewModel.SetPlusFeaturesEnabled(true);
        viewModel.SetPlusFeaturesEnabled(false);

        Assert.Equal(new[] { FavoritePath }, viewModel.GetFavoritePaths());
        Assert.Equal(new[] { FrequentPath, RecentPath }, viewModel.GetFrequentPaths());
    }

    [Fact]
    public void RecentPaths_AreOrderedByTheMostRecentAccess()
    {
        // 「よく使う」は回数順、「最近」は最終アクセス順で、同じ実績から別の並びになる
        var viewModel = StartWithSavedUsage();

        Assert.Equal(new[] { RecentPath, FrequentPath }, viewModel.GetRecentPaths());
        Assert.Equal(new[] { FrequentPath, RecentPath }, viewModel.GetFrequentPaths());
    }

    [Fact]
    public void RecentPaths_LeaveOutFavoritesSoTheTreeHasNoDuplicates()
    {
        var viewModel = StartWithSavedUsage();
        Assert.Contains(RecentPath, viewModel.GetRecentPaths());

        viewModel.ToggleFavorite(RecentPath);

        Assert.DoesNotContain(RecentPath, viewModel.GetRecentPaths());
    }

    [Fact]
    public void DisablingPlus_MovesAwayFromTheRecentNode()
    {
        var viewModel = StartWithSavedUsage();
        viewModel.SetPlusFeaturesEnabled(true);
        viewModel.LoadFiles(VirtualFolders.RecentPath);
        Assert.Equal(VirtualFolders.RecentPath, viewModel.CurrentPath);

        viewModel.SetPlusFeaturesEnabled(false);

        Assert.Equal(VirtualFolders.AllRootsPath, viewModel.CurrentPath);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ToggleFavorite_IsAvailableRegardlessOfTheGate(bool arePlusFeaturesEnabled)
    {
        // ToggleFavorite自体はゲートを持たない。呼び出し側（コンテキストメニュー）が
        // ArePlusFeaturesEnabled を見て出し分けているという前提を固定しておく
        var viewModel = StartWithSavedUsage();
        viewModel.SetPlusFeaturesEnabled(arePlusFeaturesEnabled);

        Assert.True(viewModel.ToggleFavorite(@"C:\PlusTest\Another"));
        Assert.True(viewModel.IsFavorite(@"C:\PlusTest\Another"));
    }

    [Fact]
    public void EffectiveVisibleColumns_FallsBackToTheDefaultsForTheFreeVersion()
    {
        var viewModel = StartWithSavedUsage();

        // 保存済みの列設定（Attributesのみ）は購読中だけ効く
        Assert.Equal(
            new[] { FileListColumns.Attributes },
            FileListColumns.GetEffectiveVisibleColumns(viewModel.GetVisibleColumns(), arePlusFeaturesEnabled: true));

        Assert.Equal(
            FileListColumns.DefaultVisibleColumns,
            FileListColumns.GetEffectiveVisibleColumns(viewModel.GetVisibleColumns(), arePlusFeaturesEnabled: false));
    }

    [Fact]
    public void EffectiveVisibleColumns_AreOrderedByTheOnScreenColumnOrder()
    {
        var configured = new[] { FileListColumns.Attributes, FileListColumns.Type, FileListColumns.Location };

        var effective = FileListColumns.GetEffectiveVisibleColumns(configured, arePlusFeaturesEnabled: true);

        // 設定に入っている順ではなく、画面上の列順に並べ直す
        Assert.Equal(new[] { FileListColumns.Location, FileListColumns.Type, FileListColumns.Attributes }, effective);
    }

    [Fact]
    public void EffectiveVisibleColumns_IgnoreUnknownKeysFromOlderSettings()
    {
        var configured = new[] { "RemovedColumn", FileListColumns.Size };

        var effective = FileListColumns.GetEffectiveVisibleColumns(configured, arePlusFeaturesEnabled: true);

        Assert.Equal(new[] { FileListColumns.Size }, effective);
    }
}
