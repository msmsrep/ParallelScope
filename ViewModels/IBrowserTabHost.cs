using System.Threading;
using ParallelScope.Data;

namespace ParallelScope.ViewModels;

/// <summary>
/// タブ（<see cref="BrowserTabViewModel"/>）からアプリ全体の状態へアクセスするための窓口。
/// 実装はシェルである <see cref="MainWindowViewModel"/> 側にあり、キャッシュDB・除外設定・
/// お気に入り／アクセス実績など「アプリに1つしかない状態」はタブごとに複製せずここ経由で参照する。
/// </summary>
internal interface IBrowserTabHost
{
    /// <summary>UIスレッドへ戻るための同期コンテキスト。</summary>
    SynchronizationContext UiContext { get; }

    /// <summary>ファイル一覧のキャッシュDB（アプリ全体で1インスタンス）。</summary>
    FileCacheRepository FileCacheRepository { get; }

    /// <summary>検索に使えるファイル名索引（Plus機能。無効・未完成なら null で、キャッシュDBへの検索に切り替える）。</summary>
    FileNameIndex? NameIndex { get; }

    /// <summary>1フォルダ分のキャッシュが書き換わったことを通知する（ファイル名索引の引き直し対象になる）。</summary>
    void OnCachedFolderChanged(string folderPath);

    /// <summary>隠し属性のファイル/フォルダを一覧に出すか。</summary>
    bool ShowHiddenItems { get; }

    /// <summary>システム属性のファイル/フォルダを一覧に出すか。</summary>
    bool ShowSystemItems { get; }

    /// <summary>指定パスが除外設定に該当するか（自身または祖先が除外パスに含まれるか）。</summary>
    bool IsExcludedPath(string path);

    /// <summary>正規化済みパス用の除外判定（大量の結果行に対して再正規化のコストをかけない）。</summary>
    bool IsExcludedNormalizedPath(string normalizedPath);

    /// <summary>横断列挙（All Files・横断検索）の起点となるパス群を返す。</summary>
    IReadOnlyList<string> GetTraversalPaths(string path);

    /// <summary>1フォルダ直下をファイルシステムから列挙する（除外設定を反映済み）。</summary>
    List<CachedFileSystemEntry> ReadEntriesFromFileSystem(string folderPath);

    /// <summary>フォルダへ移動したことを記録する（「最近」「よく使う」の並び順の元データ）。</summary>
    void RecordFolderUsage(string path);

    /// <summary>タブのAll Filesモードが切り替わったことを通知する（設定ファイルへの保存用）。</summary>
    void OnFlatFileViewEnabledChanged();

    /// <summary>タブが別のフォルダへ移動したことを通知する（タブ構成の保存用）。</summary>
    void OnTabNavigated();
}
