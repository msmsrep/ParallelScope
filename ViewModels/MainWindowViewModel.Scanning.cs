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
    // 列挙はほぼI/Oバウンド（特にNASではラウンドトリップのレイテンシが支配的）なので、
    // ディレクトリ単位で並列化しつつ同時実行数は絞る。下限2はコア数の少ないマシンでも
    // レイテンシの重なり合わせが効くようにするため。
    private static readonly ParallelOptions DirectoryEnumerationParallelOptions = new()
    {
        MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount / 2)
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
        // 掃除の基準には全ルートを渡し、切断中のドライブ等のキャッシュを誤削除しないようにする。
        // 切断中のNASルートへの Directory.Exists はUIスレッドを長時間ブロックしうるため、選別はバックグラウンドで行う
        var scannableRootPaths = await Task.Run(
            () => allConfiguredRootPaths
                .Where(path => Directory.Exists(path) && !IsExcludedPath(path))
                .ToList(),
            token);

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
        if (string.IsNullOrWhiteSpace(folderPath) || IsExcludedPath(folderPath))
        {
            return Task.FromResult(0);
        }

        // Directory.Exists は切断中のNASで長時間ブロックしうるため、呼び出し元（UIスレッド）ではなくバックグラウンドで確認する。
        // ルート外の掃除はフルスキャン時のみ行うため、ここでは null を渡す
        return Task.Run(() => Directory.Exists(folderPath) ? ScanFolderSubtrees(new[] { folderPath }, null) : 0);
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
                // スキャン中に増えたプール接続が抱えるページキャッシュ等のネイティブメモリを解放する
                _fileCacheRepository.ReleasePooledConnections();
            }
            catch
            {
                // 掃除失敗はスキャン結果へ影響させない
            }
        }

        return updatedFolderCount;
    }

    /// <summary>
    /// 1ルート配下を走査し、100フォルダごとにバッチでキャッシュへ保存する。
    /// ディレクトリの列挙はウェーブ（複数ディレクトリの束）単位で並列化する。NASでは1ディレクトリの列挙が
    /// ラウンドトリップ数回分のレイテンシになるため、直列だとフォルダ数分の往復時間が積み上がる。
    /// バッチ書き込み・訪問済み管理・失敗分類はウェーブ間の逐次部分で行い、共有状態の競合を避ける。
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

        // ウェーブは並列度より大きめに取り、列挙の速いディレクトリと遅いディレクトリの偏りを均す
        var waveCapacity = DirectoryEnumerationParallelOptions.MaxDegreeOfParallelism * 4;
        var wave = new List<string>(waveCapacity);
        var waveEntries = new List<CachedFileSystemEntry>?[waveCapacity];

        while (pendingDirectories.Count > 0)
        {
            if (token.IsCancellationRequested)
            {
                // ここで例外を投げず早期リターンする（キャンセル通知は呼び出し元で行う）
                return (updatedFolderCount, false);
            }

            // 未訪問かつ除外されていないディレクトリをウェーブ分まとめて取り出す
            wave.Clear();
            while (wave.Count < waveCapacity && pendingDirectories.Count > 0)
            {
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

                wave.Add(normalizedPath);
            }

            if (wave.Count == 0)
            {
                continue;
            }

            // ウェーブ内のディレクトリを並列に列挙する。列挙失敗は null として記録し、
            // 失敗原因の分類（切断か削除か）は Directory.Exists を伴うため逐次側で行う
            Parallel.For(0, wave.Count, DirectoryEnumerationParallelOptions, i =>
            {
                if (token.IsCancellationRequested)
                {
                    waveEntries[i] = null;
                    return;
                }

                try
                {
                    waveEntries[i] = ReadEntriesFromFileSystem(wave[i]);
                }
                catch
                {
                    waveEntries[i] = null;
                }
            });

            if (token.IsCancellationRequested)
            {
                return (updatedFolderCount, false);
            }

            var abortScan = false;
            for (var i = 0; i < wave.Count; i++)
            {
                var normalizedPath = wave[i];
                var entries = waveEntries[i];

                if (entries is null)
                {
                    // 列挙に失敗したフォルダはキャッシュを書き換えず（空と区別がつかないため）、失敗原因で扱いを分ける
                    if (Directory.Exists(normalizedPath))
                    {
                        // フォルダは見えるのに列挙できない（一時的なI/Oエラー等）。配下が未訪問のまま残り
                        // 掃除で誤削除されるのを防ぐため、このルートは未完走扱いにする（走査自体は継続）
                        completed = false;
                        continue;
                    }

                    if (!Directory.Exists(rootPath))
                    {
                        // ルートごと見えない＝ネットワーク切断とみなし、このルートを未完走として打ち切る
                        // （完走扱いにすると未訪問フォルダのキャッシュが残骸として削除されてしまう）
                        completed = false;
                        abortScan = true;
                        break;
                    }

                    // 列挙開始直前に削除されたフォルダ。未訪問の配下は残骸として通常の掃除に任せる
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

            if (abortScan)
            {
                break;
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

    /// <summary>
    /// 1フォルダ直下のファイル/フォルダを列挙し、種別→名前の順でソートして返す。
    /// 列挙で得た DirectoryInfo/FileInfo はメタデータ（サイズ・更新日時）が設定済みで返されるため、
    /// ここでの変換は追加のI/Oを伴わない。並列化はディレクトリ単位（ScanSingleSubtree側）で行う。
    /// 列挙自体の失敗（ネットワーク切断・フォルダ消失等）は例外として呼び出し元へ伝える。
    /// ここで握りつぶして空リストを返すと「本当に空のフォルダ」と区別できず、
    /// NASの瞬断などで既存キャッシュが空で上書きされてしまう。
    /// </summary>
    private List<CachedFileSystemEntry> ReadEntriesFromFileSystem(string folderPath)
    {
        var dirInfo = new DirectoryInfo(folderPath);
        var entries = new List<CachedFileSystemEntry>();

        foreach (var d in dirInfo.EnumerateDirectories("*", NonRecursiveEnumerationOptions))
        {
            if (IsExcludedPath(d.FullName))
            {
                continue;
            }

            var entry = TryCreateFolderEntry(folderPath, d);
            if (entry is not null)
            {
                entries.Add(entry);
            }
        }

        foreach (var f in dirInfo.EnumerateFiles("*", NonRecursiveEnumerationOptions))
        {
            var entry = TryCreateFileEntry(folderPath, f);
            if (entry is not null)
            {
                entries.Add(entry);
            }
        }

        entries.Sort((a, b) =>
        {
            // フォルダを先に配置
            var folderCompare = b.IsFolder.CompareTo(a.IsFolder);
            if (folderCompare != 0)
                return folderCompare;
            // 同じ種類ならば名前でソート
            return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });
        return entries;
    }

    private static CachedFileSystemEntry? TryCreateFolderEntry(string parentPath, DirectoryInfo directory)
    {
        try
        {
            // 作成日時・属性は列挙時に取得済みのデータ（WIN32_FIND_DATA）から読むだけで、追加のI/Oは発生しない
            return new CachedFileSystemEntry(
                parentPath,
                directory.FullName,
                directory.Name,
                true,
                null,
                directory.LastWriteTimeUtc,
                directory.CreationTimeUtc,
                (int)directory.Attributes);
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
            // 作成日時・属性は列挙時に取得済みのデータ（WIN32_FIND_DATA）から読むだけで、追加のI/Oは発生しない
            return new CachedFileSystemEntry(
                parentPath,
                file.FullName,
                file.Name,
                false,
                file.Length,
                file.LastWriteTimeUtc,
                file.CreationTimeUtc,
                (int)file.Attributes);
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
