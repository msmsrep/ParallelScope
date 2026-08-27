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
            ColumnWidths = new Dictionary<string, double> { [FileListColumns.Size] = 120.5 },
            CsvExportSizeInBytes = true,
            FavoritePaths = { @"C:\Root\Fav" },
            FolderUsages =
            {
                new FolderUsageEntry { Path = @"C:\Root\Fav", Count = 7, LastAccessedAt = new DateTime(2026, 5, 6, 7, 8, 9) }
            },
            Theme = nameof(AppThemeSetting.Dark),
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
        Assert.Equal(120.5, loaded.ColumnWidths![FileListColumns.Size]);
        Assert.True(loaded.CsvExportSizeInBytes);
        Assert.Equal(saved.FavoritePaths, loaded.FavoritePaths);
        Assert.Equal(nameof(AppThemeSetting.Dark), loaded.Theme);
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
    }
}
