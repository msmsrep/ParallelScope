using System.IO;
using System.Threading;
using ParallelScope.Utilities;

namespace ParallelScope.ViewModels;

/// <summary>戻る/進む/上へ・アドレス入力・パス遷移など、フォルダ間ナビゲーションに関する処理。</summary>
public partial class MainWindowViewModel
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

        var normalizedTargetPath = PathNormalizer.Normalize(folderPath);
        if (string.IsNullOrEmpty(normalizedTargetPath))
        {
            return false;
        }

        if (IsExcludedPath(normalizedTargetPath))
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

        if (success && addToHistory && !string.IsNullOrEmpty(previousPath))
        {
            _backHistory.Push(previousPath);
            _forwardHistory.Clear();
        }

        NotifyNavigationStateChanged();
        return success;
    }

    /// <summary>現在地・アドレス表示・検索状態を更新し、キャッシュ読込とバックグラウンド更新を開始する。</summary>
    private bool LoadFilesInternal(string folderPath)
    {
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

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string? GetParentPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
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
