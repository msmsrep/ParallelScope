using System.IO;
using ParallelScope.Data;
using ParallelScope.Tests.TestSupport;
using ParallelScope.Utilities;

namespace ParallelScope.Tests.Data;

public class AppSettingsRepositoryTests
{
    [Fact]
    public void Load_ReturnsDefaultsWhenSettingsFileDoesNotExist()
    {
        using var temp = new TempDirectory();

        var settings = new AppSettingsRepository(temp.Path).Load();

        Assert.Empty(settings.RootPaths);
        Assert.Empty(settings.ExcludedPaths);
        Assert.Empty(settings.FavoritePaths);
        Assert.Empty(settings.FolderUsages);
        Assert.Equal(AppSettings.DefaultFullScanIntervalHours, settings.FullScanIntervalHours);
        Assert.False(settings.IsFlatFileViewEnabled);
        Assert.False(settings.CsvExportSizeInBytes);
        Assert.Null(settings.VisibleColumns);
        Assert.Null(settings.ColumnOrder);
        Assert.Null(settings.ColumnWidths);
        Assert.Null(settings.Theme);
        Assert.Null(settings.Language);
        Assert.Null(settings.VisibleTreeNodes);
        Assert.Null(settings.TreeNodeOrder);
    }

    [Fact]
    public void SaveAndLoad_RoundTripsEverySetting()
    {
        using var temp = new TempDirectory();
        var repository = new AppSettingsRepository(temp.Path);
        var saved = new AppSettings
        {
            RootPaths = { @"C:\Root", @"D:\Data" },
            ExcludedPaths = { @"C:\Root\Temp" },
            FullScanIntervalHours = 12,
            IsFlatFileViewEnabled = true,
            VisibleColumns = new List<string> { FileListColumns.Size, FileListColumns.Modified },
            ColumnOrder = new List<string> { FileListColumns.Name, FileListColumns.Size },
            VisibleTreeNodes = new List<string> { TreeNodes.Recent },
            TreeNodeOrder = new List<string> { TreeNodes.AllRoots, TreeNodes.Recent },
            ColumnWidths = new Dictionary<string, double> { [FileListColumns.Size] = 120.5 },
            CsvExportSizeInBytes = true,
            FavoritePaths = { @"C:\Root\Fav" },
            FolderUsages =
            {
                new FolderUsageEntry { Path = @"C:\Root\Fav", Count = 7, LastAccessedAt = new DateTime(2026, 5, 6, 7, 8, 9) }
            },
            Theme = nameof(AppThemeSetting.Dark),
            Language = nameof(AppLanguageSetting.Japanese),
            DeveloperUnlockKey = "unlock"
        };

        repository.Save(saved);
        var loaded = new AppSettingsRepository(temp.Path).Load();

        Assert.Equal(saved.RootPaths, loaded.RootPaths);
        Assert.Equal(saved.ExcludedPaths, loaded.ExcludedPaths);
        Assert.Equal(12, loaded.FullScanIntervalHours);
        Assert.True(loaded.IsFlatFileViewEnabled);
        Assert.Equal(saved.VisibleColumns, loaded.VisibleColumns);
        Assert.Equal(saved.ColumnOrder, loaded.ColumnOrder);
        Assert.Equal(saved.VisibleTreeNodes, loaded.VisibleTreeNodes);
        Assert.Equal(saved.TreeNodeOrder, loaded.TreeNodeOrder);
        Assert.Equal(120.5, loaded.ColumnWidths![FileListColumns.Size]);
        Assert.True(loaded.CsvExportSizeInBytes);
        Assert.Equal(saved.FavoritePaths, loaded.FavoritePaths);
        Assert.Equal(nameof(AppThemeSetting.Dark), loaded.Theme);
        Assert.Equal(nameof(AppLanguageSetting.Japanese), loaded.Language);
        Assert.Equal("unlock", loaded.DeveloperUnlockKey);

        var usage = Assert.Single(loaded.FolderUsages);
        Assert.Equal(@"C:\Root\Fav", usage.Path);
        Assert.Equal(7, usage.Count);
        Assert.Equal(new DateTime(2026, 5, 6, 7, 8, 9), usage.LastAccessedAt);
    }

    [Fact]
    public void Save_OverwritesPreviousContent()
    {
        using var temp = new TempDirectory();
        var repository = new AppSettingsRepository(temp.Path);

        repository.Save(new AppSettings { RootPaths = { @"C:\Old" } });
        repository.Save(new AppSettings { RootPaths = { @"C:\New" } });

        Assert.Equal(new[] { @"C:\New" }, repository.Load().RootPaths);
    }

    [Fact]
    public void Load_FallsBackToDefaultsWhenFileIsCorrupted()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(Path.Combine(temp.Path, "settings.json"), "{ this is not json");

        // 設定ファイルが壊れていても起動できるよう、読み込み失敗はデフォルト設定へフォールバックする
        var settings = new AppSettingsRepository(temp.Path).Load();

        Assert.Empty(settings.RootPaths);
        Assert.Equal(AppSettings.DefaultFullScanIntervalHours, settings.FullScanIntervalHours);
    }

    [Fact]
    public void Load_FallsBackToDefaultsWhenFileContainsJsonNull()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(Path.Combine(temp.Path, "settings.json"), "null");

        var settings = new AppSettingsRepository(temp.Path).Load();

        Assert.Empty(settings.RootPaths);
    }

    [Fact]
    public void Load_KeepsDefaultsForPropertiesMissingFromAnOlderSettingsFile()
    {
        using var temp = new TempDirectory();
        // 旧バージョンが書いた、プロパティが少ない settings.json
        File.WriteAllText(Path.Combine(temp.Path, "settings.json"), """{ "RootPaths": ["C:\\Root"] }""");

        var settings = new AppSettingsRepository(temp.Path).Load();

        Assert.Equal(new[] { @"C:\Root" }, settings.RootPaths);
        Assert.Equal(AppSettings.DefaultFullScanIntervalHours, settings.FullScanIntervalHours);
        Assert.Null(settings.VisibleColumns);
        Assert.Null(settings.VisibleTreeNodes);
        // 更新前から使っている利用者のファイル一覧の見え方を変えないため、表示側に倒す
        Assert.True(settings.ShowHiddenItems);
        Assert.True(settings.ShowSystemItems);
    }

    [Fact]
    public void Load_KeepsTheOtherSettingsWhenOnePropertyHasTheWrongType()
    {
        using var temp = new TempDirectory();
        // Panes だけ型が合わない（配列であるべきところがオブジェクト）settings.json
        File.WriteAllText(Path.Combine(temp.Path, "settings.json"), """
            {
              "RootPaths": ["C:\\Root"],
              "FullScanIntervalHours": 12,
              "Theme": "Dark",
              "Panes": { "Tabs": [], "ActiveTabIndex": 0 }
            }
            """);

        var settings = new AppSettingsRepository(temp.Path).Load();

        // 壊れているのは Panes だけなので、他の設定は残る
        Assert.Equal(new[] { @"C:\Root" }, settings.RootPaths);
        Assert.Equal(12, settings.FullScanIntervalHours);
        Assert.Equal("Dark", settings.Theme);
        Assert.Null(settings.Panes);
    }

    [Fact]
    public void Load_ReturnsDefaultsAndKeepsACopyWhenTheFileIsNotValidJson()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "settings.json");
        File.WriteAllText(path, "{ これは JSON ではない");

        var settings = new AppSettingsRepository(temp.Path).Load();

        Assert.Empty(settings.RootPaths);
        // 原因を追えるよう、読めなかったファイルは退避しておく
        Assert.True(File.Exists(Path.Combine(temp.Path, "settings.broken.json")));
    }
}
