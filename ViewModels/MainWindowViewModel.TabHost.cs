using System.Threading;
using ParallelScope.Data;
using ParallelScope.Utilities;

namespace ParallelScope.ViewModels;

/// <summary>
/// タブ（<see cref="BrowserTabViewModel"/>）へアプリ全体の状態を貸し出す窓口の実装。
/// 明示的インターフェース実装にしているのは、これらがタブ専用の入口であって
/// 画面やコードビハインドから使う公開APIではないことをはっきりさせるため。
/// </summary>
public partial class MainWindowViewModel : IBrowserTabHost
{
    SynchronizationContext IBrowserTabHost.UiContext => _uiContext;

    FileCacheRepository IBrowserTabHost.FileCacheRepository => _fileCacheRepository;

    BackgroundWorkGate IBrowserTabHost.BackgroundGate => _backgroundWorkGate;

    FileNameIndex? IBrowserTabHost.NameIndex => GetUsableNameIndex();

    bool IBrowserTabHost.UseRegexSearch => IsRegexSearchActive;

    // 索引はキャッシュDBの写しなので、書き換わった親フォルダは検索時にDBから引き直させる
    void IBrowserTabHost.OnCachedFolderChanged(string folderPath) => _fileNameIndex.MarkParentChanged(folderPath);

    bool IBrowserTabHost.ShowHiddenItems => _showHiddenItems;

    bool IBrowserTabHost.ShowSystemItems => _showSystemItems;

    bool IBrowserTabHost.IsExcludedPath(string path) => IsExcludedPath(path);

    bool IBrowserTabHost.IsExcludedNormalizedPath(string normalizedPath) => IsExcludedNormalizedPath(normalizedPath);

    IReadOnlyList<string> IBrowserTabHost.GetTraversalPaths(string path) => GetTraversalPaths(path);

    List<CachedFileSystemEntry> IBrowserTabHost.ReadEntriesFromFileSystem(string folderPath) => ReadEntriesFromFileSystem(folderPath);

    void IBrowserTabHost.RecordFolderUsage(string path) => RecordFolderUsage(path);

    // All Filesモードは settings.json に持つ設定のため、タブ側で切り替わったらそのまま保存する
    void IBrowserTabHost.OnFlatFileViewEnabledChanged() => SaveSettings();

    // タブごとの現在パスも保存対象なので、移動のたびに書き出す
    void IBrowserTabHost.OnTabNavigated() => OnPaneStateChanged();
}
