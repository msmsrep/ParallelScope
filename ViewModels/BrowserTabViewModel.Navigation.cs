using System.IO;
using System.Threading;
using ParallelScope.Utilities;

namespace ParallelScope.ViewModels;

/// <summary>戻る/進む/上へ・アドレス入力・パス遷移など、フォルダ間ナビゲーションに関する処理。</summary>
public partial class BrowserTabViewModel
{
    /// <summary>指定フォルダへ移動する（履歴に追加される）。</summary>
    public bool LoadFiles(string folderPath)
    {
        return NavigateTo(folderPath, true);
    }

    /// <summary>戻る履歴の1つ前のフォルダへ移動する。</summary>
    public bool GoBack()
    {
        if (!CanGoBack)
        {
            return false;
        }

        var targetPath = _backHistory.Pop();

        if (!string.IsNullOrEmpty(CurrentPath))
        {
            _forwardHistory.Push(CurrentPath);
        }

        var success = LoadFilesInternal(targetPath);
        if (success)
        {
            _host.RecordFolderUsage(targetPath);
        }

        NotifyNavigationStateChanged();
        return success;
    }

    /// <summary>進む履歴の1つ先のフォルダへ移動する。</summary>
    public bool GoForward()
    {
        if (!CanGoForward)
        {
            return false;
        }

        var targetPath = _forwardHistory.Pop();

        if (!string.IsNullOrEmpty(CurrentPath))
        {
            _backHistory.Push(CurrentPath);
        }

        var success = LoadFilesInternal(targetPath);
        if (success)
        {
            _host.RecordFolderUsage(targetPath);
        }

        NotifyNavigationStateChanged();
        return success;
    }

    /// <summary>親フォルダへ移動する。</summary>
    public bool GoUp()
    {
        var parentPath = GetParentPath(CurrentPath);
        if (parentPath is null)
        {
            return false;
        }

        return NavigateTo(parentPath, true);
    }

    /// <summary>アドレス欄に入力されたパスへ移動する。</summary>
    public bool TryNavigateByAddressInput()
    {
        return NavigateTo(AddressInput, true);
    }

    /// <summary>指定フォルダへ移動する。addToHistoryがtrueなら戻る履歴に現在地を積む。</summary>
    public bool NavigateTo(string folderPath, bool addToHistory)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            return false;
        }

        // まだ一度も表示していない復元中のタブは、控えてある移動先を差し替えるだけにする
        // （読み込みは初回表示のときにまとめて行う）
        if (_pendingRestorePath is not null)
        {
            return TryRedirectPendingRestore(folderPath);
        }

        var normalizedTargetPath = PathNormalizer.Normalize(folderPath);
        if (string.IsNullOrEmpty(normalizedTargetPath))
        {
            return false;
        }

        if (_host.IsExcludedPath(normalizedTargetPath))
        {
            return false;
        }

        if (string.Equals(PathNormalizer.Normalize(CurrentPath), normalizedTargetPath, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // 移動先の存在確認は LoadFilesInternal に一本化されている（切断中のNASでは確認自体が
        // ブロックしうるため、二重に確認すると待ち時間が倍になる）。履歴は移動成功時のみ積む
        var previousPath = CurrentPath;
        var success = LoadFilesInternal(normalizedTargetPath);

        if (success && addToHistory)
        {
            // 「よく使う」の集計対象はユーザー操作による移動のみ。
            // 起動時やルート設定変更時の自動移動は addToHistory=false で呼ばれるため数えない
            _host.RecordFolderUsage(normalizedTargetPath);

            if (!string.IsNullOrEmpty(previousPath))
            {
                _backHistory.Push(previousPath);
                _forwardHistory.Clear();
            }
        }

        NotifyNavigationStateChanged();
        return success;
    }

    /// <summary>現在地・アドレス表示・検索状態を更新し、キャッシュ読込とバックグラウンド更新を開始する。</summary>
    private bool LoadFilesInternal(string folderPath)
    {
        // 仮想ノード（Folders / Favorites / Frequently Used）: 実パスではないため存在確認・ライブFS更新は
        // 行わず、対応するフォルダ群をフォルダ行として一覧表示する（サイズはキャッシュから集計）
        var virtualKind = VirtualFolders.GetKind(folderPath);
        if (virtualKind != VirtualFolderKind.None)
        {
            var canonicalPath = VirtualFolders.GetCanonicalPath(folderPath)!;
            CurrentPath = canonicalPath;
            AddressInput = canonicalPath;
            SearchQuery = string.Empty;

            var virtualNavigationVersion = Interlocked.Increment(ref _navigationVersion);
            _ = LoadVirtualFolderListingAsync(canonicalPath, virtualNavigationVersion);

            if (IsFlatFileViewEnabled)
            {
                // フラット表示モード中は全ルート横断の全ファイルを再取得する
                RequestFlatFileView();
            }

            _host.OnTabNavigated();
            return true;
        }

        // 切断中のNASでもUIを固めないよう、存在確認はタイムアウト付きで行う。
        // タイムアウト時は存在する扱いで進み、キャッシュからの表示（LoadFromCacheAsync）に任せる。
        // 実際に読めない場合はバックグラウンド更新が何もせず終わるだけで、キャッシュ由来の一覧は閲覧できる
        if (string.IsNullOrWhiteSpace(folderPath) || !DirectoryAvailabilityChecker.ExistsOrTimedOut(folderPath))
        {
            return false;
        }

        try
        {
            CurrentPath = folderPath;
            AddressInput = folderPath;
            SearchQuery = string.Empty;

            var navigationVersion = Interlocked.Increment(ref _navigationVersion);
            _ = LoadFromCacheAsync(folderPath, navigationVersion);
            // 連続リクエストを統合するキューへ委譲
            _refreshCoalescer.Request((folderPath, navigationVersion));

            if (IsFlatFileViewEnabled)
            {
                // フラット表示モード中は、移動先フォルダの全ファイルを再取得する
                RequestFlatFileView();
            }

            _host.OnTabNavigated();
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 現在表示中のフォルダの一覧を再読み込みする（スキャン完了後にキャッシュの最新内容を反映するため）。
    /// NavigateTo は同一パスへの移動を早期returnで無視するため、再読み込みには使えない。
    /// ナビゲーションではないので履歴・検索状態には触れず、アクティブな表示モードに応じて再取得する。
    /// </summary>
    public void RefreshCurrentFolder()
    {
        // まだ表示していない復元中のタブは、初回表示のときに最新の内容で読み込まれる
        if (_pendingRestorePath is not null)
        {
            return;
        }

        // 読み直す＝キャッシュが変わった可能性があるので、控えてある検索結果は使わない
        ForgetCompletedSearch();

        var folderPath = CurrentPath;
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            return;
        }

        var navigationVersion = Interlocked.Increment(ref _navigationVersion);

        if (VirtualFolders.IsVirtual(folderPath))
        {
            // 仮想ノードにはキャッシュ行もライブFSも無いため、対象フォルダ一覧の再構築のみ行う
            _ = LoadVirtualFolderListingAsync(folderPath, navigationVersion);
        }
        else
        {
            _ = LoadFromCacheAsync(folderPath, navigationVersion);
            _refreshCoalescer.Request((folderPath, navigationVersion));
        }

        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            // 検索中は検索結果を最新キャッシュで取り直す（直下一覧は上の再取得が裏で更新する）
            RequestSearch(SearchQuery);
            return;
        }

        if (IsFlatFileViewEnabled)
        {
            RequestFlatFileView();
        }
    }

    /// <summary>
    /// 復元したタブの移動先を控えるだけにして、実際の読み込みは初めて表示するときまで遅らせる。
    /// 起動時に全タブぶんのキャッシュ読み・ファイルシステム列挙・キャッシュ書き込みが一斉に走ると、
    /// SQLiteの書き込み待ちとスレッドプールの飽和でアプリ全体が固まるため。
    /// 移動先の存在確認もここでは行わない（切断中のNASでは1タブあたり最大2秒UIスレッドが止まる）。
    /// </summary>
    internal void PrepareDeferredRestore(string path, string? fallbackPath)
    {
        var resolvedFallbackPath = ResolveRestorePath(fallbackPath);
        var resolvedPath = ResolveRestorePath(path) ?? resolvedFallbackPath;
        if (resolvedPath is null)
        {
            // 移動先が1つも決まらないタブは、CurrentPathが空のまま残す（呼び出し側が取り除く）
            return;
        }

        _pendingRestorePath = resolvedPath;
        _pendingRestoreFallbackPath = resolvedFallbackPath;

        // 復元前にこのタブへ移動が入っていた場合（2画面の復元では EnableSplitView が先に移動させる）、
        // 走り出している取得の結果が後から届いても捨てられるよう番号を進めておく
        Interlocked.Increment(ref _navigationVersion);

        // タブ見出しと settings.json への保存は移動先が分かれば足りるため、一覧を読まずに現在地だけ入れる
        CurrentPath = resolvedPath;
        AddressInput = resolvedPath;
    }

    /// <summary>
    /// 遅らせていた移動をここで実行する。移動できなければ代わりのルートへ寄せ、それも無理なら空にする。
    /// 遅延中でなければ false を返す。
    /// </summary>
    private bool TryConsumePendingRestore()
    {
        if (_pendingRestorePath is not { } path)
        {
            return false;
        }

        var fallbackPath = _pendingRestoreFallbackPath;
        _pendingRestorePath = null;
        _pendingRestoreFallbackPath = null;

        // これから読み込むので、遅延中に付いた「表示時に読み直す」印は消す
        _isSuspended = false;
        _isStale = false;

        // NavigateTo は同一パスへの移動を早期returnで無視するため、控えてあった現在地はここで外す
        CurrentPath = string.Empty;

        if (NavigateTo(path, false))
        {
            return true;
        }

        if (fallbackPath is not null && NavigateTo(fallbackPath, false))
        {
            return true;
        }

        // 移動先が無くなっている（ルートが外された等）。空のタブとして残す
        Clear();
        return true;
    }

    /// <summary>遅延中のタブの移動先を差し替える（読み込みは行わない）。</summary>
    private bool TryRedirectPendingRestore(string folderPath)
    {
        if (ResolveRestorePath(folderPath) is not { } resolvedPath)
        {
            return false;
        }

        _pendingRestorePath = resolvedPath;
        CurrentPath = resolvedPath;
        AddressInput = resolvedPath;
        return true;
    }

    /// <summary>遅延復元用にパスを正規化する（仮想ノードは正規表記へ。除外パスは移動先にしない）。</summary>
    private string? ResolveRestorePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        if (VirtualFolders.GetKind(path) != VirtualFolderKind.None)
        {
            return VirtualFolders.GetCanonicalPath(path);
        }

        var normalizedPath = PathNormalizer.Normalize(path);
        if (string.IsNullOrEmpty(normalizedPath) || _host.IsExcludedPath(normalizedPath))
        {
            return null;
        }

        return normalizedPath;
    }

    private static string? GetParentPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        // 仮想ノードはツリーの最上位なので親は無い（Directory.GetParent に仮想パスを渡さない）
        if (VirtualFolders.IsVirtual(path))
        {
            return null;
        }

        // Directory.GetParent はファイルシステムへアクセスしない純粋なパス操作。
        // ここで存在確認をすると、CanGoUp のバインディング評価（UIスレッド）が
        // 切断中のNASでブロックするため行わない（移動先の検証は移動時に行われる）
        return Directory.GetParent(path)?.FullName;
    }

    private void NotifyNavigationStateChanged()
    {
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
        OnPropertyChanged(nameof(CanGoUp));
    }
}
