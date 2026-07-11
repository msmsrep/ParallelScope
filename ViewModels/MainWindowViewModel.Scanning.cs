using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using ParallelScope.Data;
using ParallelScope.Utilities;

namespace ParallelScope.ViewModels;

/// <summary>フルスキャン・フォルダ単位スキャンなど、ファイルシステムを読み取ってキャッシュへ書き込む処理。</summary>
public partial class MainWindowViewModel
{
    private static readonly EnumerationOptions NonRecursiveEnumerationOptions = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint
    };

    // 全コアを使うとバックグラウンドのフルスキャン中にスレッドプールが飽和し、
    // UIの応答（ツリー選択時のキャッシュ読み込み等のTask.Run）が待たされる。
    // 列挙はほぼI/Oバウンドなので半分に絞っても速度への影響は小さい。
    private static readonly ParallelOptions EntryEnumerationParallelOptions = new()
    {
        MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2)
    };

    /// <summary>設定済みの全ルートフォルダをフルスキャンし、キャッシュを更新する。</summary>
    public async Task<int> FullScanConfiguredRootsAsync(CancellationToken token)
    {
        var allConfiguredRootPaths = RootFolders
            .Select(x => PathNormalizer.Normalize(x.Path))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // スキャン対象は実在する（かつ除外されていない）ルートのみ。
        // 掃除の基準には全ルートを渡し、切断中のドライブ等のキャッシュを誤削除しないようにする
        var scannableRootPaths = allConfiguredRootPaths
            .Where(path => Directory.Exists(path) && !IsExcludedPath(path))
            .ToList();

        return await ScanFolderSubtreesAsync(scannableRootPaths, allConfiguredRootPaths, token);
    }

    private async Task<int> ScanFolderSubtreesAsync(
        IReadOnlyCollection<string> rootPaths,
        IReadOnlyCollection<string>? allConfiguredRootPaths,
        CancellationToken token)
    {
        var updatedFolderCount = await Task.Run(() => ScanFolderSubtrees(rootPaths, allConfiguredRootPaths, token), token);

        // キャンセル検知はバックグラウンドスレッド内で例外を投げず、呼び出し元に戻ってから行う
        // （Task.Run内で例外を投げると、デバッガが「ユーザーコードで未処理」として誤検知することがあるため）
        token.ThrowIfCancellationRequested();
        return updatedFolderCount;
    }

    /// <summary>指定フォルダ配下をスキャンし、キャッシュを更新する（右クリックメニューからの単一フォルダスキャン用）。</summary>
    public Task<int> ScanFolderSubtreeAsync(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath) || IsExcludedPath(folderPath))
        {
            return Task.FromResult(0);
        }

        // ルート外の掃除はフルスキャン時のみ行うため、ここでは null を渡す
        return Task.Run(() => ScanFolderSubtrees(new[] { folderPath }, null));
    }

    /// <summary>
    /// 各ルートを順番に走査してキャッシュを更新する。1ルート完了するたびにツリーのスキャン中表示を消す。
    /// キャッシュと内容が同一のフォルダは書き換えられないため、戻り値は「実際に書き換えたフォルダ数」。
    /// </summary>
    private int ScanFolderSubtrees(
        IReadOnlyCollection<string> rootPaths,
        IReadOnlyCollection<string>? allConfiguredRootPaths,
        CancellationToken token = default)
    {
        // 訪問済みセットはルート間で共有し、重なり合うルート（例: C:\ とその配下のフォルダ）の二重スキャンを防ぐ
        var visitedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var updatedFolderCount = 0;

        // 掃除の基準にできるのは完走したルートのみ。NAS等でスキャン途中にネットワークが切断されると
        // 残りのフォルダが「未訪問」のまま終わるため、そのルートを掃除対象に含めると
        // 一時的に見えないだけのフォルダのキャッシュを「削除された」と誤認して消してしまう
        var completedRootPaths = new List<string>();

        foreach (var rootPath in rootPaths)
        {
            var (updatedCount, completed) = ScanSingleSubtree(rootPath, visitedDirectories, token);
            updatedFolderCount += updatedCount;

            if (token.IsCancellationRequested)
            {
                // キャンセル時はスピナーに触れず即返す（呼び出し元のfinallyが一括で消す）
                return updatedFolderCount;
            }

            if (completed)
            {
                completedRootPaths.Add(rootPath);
            }

            // 完了したルートから順にスキャン中表示を消す（全ルート完了まで待たせない）
            NotifyRootScanCompleted(rootPath);
        }

        // 完走時のみ残骸を掃除する（途中キャンセルだと「未訪問＝削除された」と区別できないため）。
        // 削除済みフォルダ配下の孤児行を消し、その後フルスキャンで肥大化したWALを切り詰める
        if (!token.IsCancellationRequested)
        {
            try
            {
                _fileCacheRepository.DeleteStaleEntries(completedRootPaths, allConfiguredRootPaths, visitedDirectories);
                _fileCacheRepository.TruncateWal();
            }
            catch
            {
                // 掃除失敗はスキャン結果へ影響させない
            }
        }

        return updatedFolderCount;
    }

    /// <summary>
    /// 1ルート配下を深さ優先で走査し、100フォルダごとにバッチでキャッシュへ保存する。
    /// Completed はルートを完走できたかどうか（ネットワーク切断等で打ち切った場合は false）。
    /// </summary>
    private (int UpdatedFolderCount, bool Completed) ScanSingleSubtree(string rootPath, HashSet<string> visitedDirectories, CancellationToken token)
    {
        var pendingDirectories = new Stack<string>();
        pendingDirectories.Push(rootPath);
        var batchEntries = new Dictionary<string, IReadOnlyCollection<CachedFileSystemEntry>>();
        var updatedFolderCount = 0;
        var completed = true;
        const int BatchSize = 100;

        while (pendingDirectories.Count > 0)
        {
            if (token.IsCancellationRequested)
            {
                // ここで例外を投げず早期リターンする（キャンセル通知は呼び出し元で行う）
                return (updatedFolderCount, false);
            }

            var currentPath = pendingDirectories.Pop();
            string normalizedPath;

            try
            {
                normalizedPath = PathNormalizer.Normalize(currentPath);
            }
            catch
            {
                continue;
            }

            if (!visitedDirectories.Add(normalizedPath) || IsExcludedPath(normalizedPath))
            {
                continue;
            }

            if (!Directory.Exists(normalizedPath))
            {
                // フォルダが見えないのは通常は削除だが、NAS等ではネットワーク切断でも同じ結果になる。
                // ルートごと見えなくなっていれば切断とみなし、このルートを未完走として打ち切る
                // （完走扱いにすると未訪問フォルダのキャッシュが残骸として削除されてしまう）
                if (!Directory.Exists(rootPath))
                {
                    completed = false;
                    break;
                }
                continue;
            }

            List<CachedFileSystemEntry> entries;
            try
            {
                entries = ReadEntriesFromFileSystem(normalizedPath);
            }
            catch
            {
                continue;
            }

            batchEntries[normalizedPath] = entries;

            // バッチが一定サイズに達したら、まとめてデータベース更新
            // （キャッシュと同一内容の親パスは書き換えられず、実際に書き換えた件数が返る）
            if (batchEntries.Count >= BatchSize)
            {
                try
                {
                    updatedFolderCount += _fileCacheRepository.BatchReplaceEntriesByParentPaths(batchEntries);
                }
                catch
                {
                    // バッチ保存失敗時はスキャンを継続する
                }
                batchEntries.Clear();
            }

            foreach (var folderEntry in entries.Where(x => x.IsFolder && !IsExcludedPath(x.FullPath)).OrderByDescending(x => x.FullPath, StringComparer.OrdinalIgnoreCase))
            {
                pendingDirectories.Push(folderEntry.FullPath);
            }
        }

        // 残りのバッチを処理（このルートの完了通知より先にキャッシュへ反映しておく）。
        // 切断による打ち切り時も、切断前に列挙済みの内容は有効なので書き込む
        if (batchEntries.Count > 0)
        {
            try
            {
                updatedFolderCount += _fileCacheRepository.BatchReplaceEntriesByParentPaths(batchEntries);
            }
            catch
            {
                // バッチ保存失敗時でもスキャン結果は返す
            }
        }

        return (updatedFolderCount, completed);
    }

    /// <summary>ルート1件のスキャン完了をUIへ反映し、該当ルートのスキャン中表示を消す（バックグラウンドスレッドから呼ばれる）。</summary>
    private void NotifyRootScanCompleted(string rootPath)
    {
        _uiContext.Post(_ =>
        {
            var rootFolder = RootFolders.FirstOrDefault(x => PathNormalizer.AreSame(x.Path, rootPath));
            if (rootFolder is not null)
            {
                rootFolder.IsScanning = false;
            }
        }, null);
    }

    /// <summary>1フォルダ直下のファイル/フォルダを並列列挙し、種別→名前の順でソートして返す。</summary>
    private List<CachedFileSystemEntry> ReadEntriesFromFileSystem(string folderPath)
    {
        var dirInfo = new DirectoryInfo(folderPath);
        var entries = new ConcurrentBag<CachedFileSystemEntry>();

        try
        {
            // フォルダ処理を並列化
            var folders = dirInfo
                .EnumerateDirectories("*", NonRecursiveEnumerationOptions)
                .Where(d => !IsExcludedPath(d.FullName))
                .ToList();

            Parallel.ForEach(folders, EntryEnumerationParallelOptions, d =>
            {
                var entry = TryCreateFolderEntry(folderPath, d);
                if (entry is not null)
                {
                    entries.Add(entry);
                }
            });

            // ファイル処理を並列化
            var files = dirInfo
                .EnumerateFiles("*", NonRecursiveEnumerationOptions)
                .ToList();

            Parallel.ForEach(files, EntryEnumerationParallelOptions, f =>
            {
                var entry = TryCreateFileEntry(folderPath, f);
                if (entry is not null)
                {
                    entries.Add(entry);
                }
            });
        }
        catch
        {
            // 例外時は空のリストを返す
        }

        // 結果をソートして返す
        var result = entries.ToList();
        result.Sort((a, b) =>
        {
            // フォルダを先に配置
            var folderCompare = b.IsFolder.CompareTo(a.IsFolder);
            if (folderCompare != 0)
                return folderCompare;
            // 同じ種類ならば名前でソート
            return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });
        return result;
    }

    private static CachedFileSystemEntry? TryCreateFolderEntry(string parentPath, DirectoryInfo directory)
    {
        try
        {
            return new CachedFileSystemEntry(
                parentPath,
                directory.FullName,
                directory.Name,
                true,
                null,
                directory.LastWriteTimeUtc);
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static CachedFileSystemEntry? TryCreateFileEntry(string parentPath, FileInfo file)
    {
        try
        {
            return new CachedFileSystemEntry(
                parentPath,
                file.FullName,
                file.Name,
                false,
                file.Length,
                file.LastWriteTimeUtc);
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }
}
