using System.Threading;
using ParallelScope.Utilities;

namespace ParallelScope.Data;

/// <summary>
/// 全ファイル/フォルダの名前をメモリに載せ、検索をキャッシュDBを引かずに済ませる索引（Plus機能）。
/// </summary>
/// <remarks>
/// キャッシュDBへの検索は「配下の全行のテーブル本体を読んで名前を判定する」形になるため、
/// ドライブ直下（150万行）では1回あたり数百msかかる。名前だけをメモリに持てば同じ判定が十数msで済む。
/// 名前は1件ずつ string を持たず1本の配列へ詰め、親フォルダのパスは重複排除して別に持つ
/// （150万件でおよそ160MB。1件ずつ string を持つとこの数倍になる）。
/// 索引はキャッシュDBのある時点の写しなので、その後に変わった親フォルダは
/// <see cref="MarkParentChanged"/> で控えておき、検索時にその分だけDBから引き直して混ぜる。
/// </remarks>
public sealed class FileNameIndex
{
    private readonly FileCacheRepository _repository;

    /// <summary>組み上がった索引一式。差し替えは参照の入れ替え1回で済ませ、読み手が途中の状態を見ないようにする。</summary>
    private sealed record Snapshot(
        char[] PackedNames,
        int[] NameOffsets,
        int[] ParentIds,
        int[] RowIds,
        string[] ParentPaths,
        int EntryCount);

    private Snapshot? _snapshot;

    /// <summary>索引を作ってから内容が変わった親フォルダ（検索時にここだけDBから引き直す）。</summary>
    private readonly HashSet<string> _changedParentPaths = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 変わった親フォルダがこの数を超えたら索引を捨てる。
    /// 引き直しの本数が増えるほど索引の速さが帳消しになるため、作り直しに任せる
    /// （作り直しは次のフルスキャン完了時に走る）。
    /// </summary>
    private const int MaxChangedParentPaths = 200;

    public FileNameIndex(FileCacheRepository repository)
    {
        _repository = repository;
    }

    /// <summary>索引が組み上がっていて検索に使えるか。</summary>
    public bool IsReady => Volatile.Read(ref _snapshot) is not null;

    /// <summary>索引を作り直す（バックグラウンドから呼ぶこと。150万件で1秒強かかる）。</summary>
    public void Build()
    {
        // 名前の総文字数は事前に分からないため、伸長に任せる（1件ずつ string を持つより結局安い）
        var packedNames = new List<char>();
        var nameOffsets = new List<int> { 0 };
        var parentIds = new List<int>();
        var rowIds = new List<int>();
        var parentPaths = new List<string>();
        var parentIdByPath = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var row in _repository.EnumerateNameIndexRows())
        {
            rowIds.Add(row.Id);

            if (!parentIdByPath.TryGetValue(row.ParentPath, out var parentId))
            {
                parentId = parentPaths.Count;
                parentPaths.Add(row.ParentPath);
                parentIdByPath[row.ParentPath] = parentId;
            }

            parentIds.Add(parentId);

            // 比較のたびに畳まずに済むよう、格納時にASCIIの大文字へ寄せておく（NameSearchMatcherと同じ規則）
            foreach (var character in row.Name)
            {
                packedNames.Add(FoldAscii(character));
            }

            nameOffsets.Add(packedNames.Count);
        }

        var snapshot = new Snapshot(
            packedNames.ToArray(),
            nameOffsets.ToArray(),
            parentIds.ToArray(),
            rowIds.ToArray(),
            parentPaths.ToArray(),
            rowIds.Count);

        lock (_changedParentPaths)
        {
            _changedParentPaths.Clear();
        }

        Volatile.Write(ref _snapshot, snapshot);
    }

    /// <summary>索引を捨ててメモリを返す（機能を無効にしたとき・購読が切れたとき）。</summary>
    public void Clear()
    {
        Volatile.Write(ref _snapshot, null);

        lock (_changedParentPaths)
        {
            _changedParentPaths.Clear();
        }
    }

    /// <summary>
    /// 指定した親フォルダの内容がキャッシュDB側で変わったことを控える。
    /// 控えた親フォルダは検索時にDBから引き直すので、索引が古いままでも結果は最新になる。
    /// </summary>
    public void MarkParentChanged(string parentPath)
    {
        if (Volatile.Read(ref _snapshot) is null || string.IsNullOrWhiteSpace(parentPath))
        {
            return;
        }

        var normalized = PathNormalizer.Normalize(parentPath);

        lock (_changedParentPaths)
        {
            _changedParentPaths.Add(normalized);

            if (_changedParentPaths.Count <= MaxChangedParentPaths)
            {
                return;
            }
        }

        // 引き直す本数が多くなりすぎたら索引をやめる（次のフルスキャン完了時に作り直される）
        Clear();
    }

    /// <summary>
    /// 指定パス配下から、名前に検索語を含むエントリを索引で探す。
    /// 索引が無ければ null を返すので、呼び出し側はキャッシュDBへの検索に切り替えること。
    /// </summary>
    public List<CachedFileSystemEntry>? SearchUnderPath(string rootPath, string nameQuery)
    {
        var snapshot = Volatile.Read(ref _snapshot);
        if (snapshot is null)
        {
            return null;
        }

        string[] changedParentPaths;
        lock (_changedParentPaths)
        {
            changedParentPaths = _changedParentPaths.ToArray();
        }

        var normalizedRoot = PathNormalizer.Normalize(rootPath);
        var rootPrefix = PathNormalizer.WithTrailingSeparator(normalizedRoot);
        var changedLookup = changedParentPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);

        // 親フォルダ単位で「探す対象か」を先に決めておく（1件ごとにパスを判定すると
        // 150万回の文字列比較になるが、親フォルダ単位なら20万回強で済む）
        var searchable = new bool[snapshot.ParentPaths.Length];
        for (var parentId = 0; parentId < searchable.Length; parentId++)
        {
            var parentPath = snapshot.ParentPaths[parentId];
            searchable[parentId] = IsUnderRoot(parentPath, normalizedRoot, rootPrefix)
                && !changedLookup.Contains(parentPath);
        }

        var foldedQuery = FoldAscii(nameQuery);
        var matchedRowIds = new List<int>();
        var packedNames = snapshot.PackedNames.AsSpan();

        for (var i = 0; i < snapshot.EntryCount; i++)
        {
            if (!searchable[snapshot.ParentIds[i]])
            {
                continue;
            }

            if (packedNames[snapshot.NameOffsets[i]..snapshot.NameOffsets[i + 1]].IndexOf(foldedQuery) >= 0)
            {
                matchedRowIds.Add(snapshot.RowIds[i]);
            }
        }

        var results = _repository.GetEntriesByIds(matchedRowIds);

        // 索引を作ったあとに変わった親フォルダは、DBから引き直して混ぜる
        foreach (var changedParentPath in changedParentPaths)
        {
            if (IsUnderRoot(changedParentPath, normalizedRoot, rootPrefix))
            {
                results.AddRange(_repository.GetSearchEntriesInParent(changedParentPath, nameQuery));
            }
        }

        // キャッシュDBへの検索（EnumerateSearchEntriesUnderPath）と同じ並びに揃える
        results.Sort(static (left, right) =>
        {
            var folderCompare = right.IsFolder.CompareTo(left.IsFolder);
            return folderCompare != 0 ? folderCompare : string.CompareOrdinal(left.Name, right.Name);
        });

        return results;
    }

    /// <summary>親フォルダのパスが検索の起点そのものか、その配下か。</summary>
    /// <remarks>起点直下のファイルは ParentPath が起点と等しくなるため、前方一致だけでは取りこぼす。</remarks>
    private static bool IsUnderRoot(string parentPath, string normalizedRoot, string rootPrefix)
    {
        return string.Equals(parentPath, normalizedRoot, StringComparison.OrdinalIgnoreCase)
            || parentPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>ASCIIの小文字だけを大文字へ寄せる（SQLiteのLIKE・NameSearchMatcherと同じ畳み方）。</summary>
    private static char FoldAscii(char value)
    {
        return value is >= 'a' and <= 'z' ? (char)(value - ('a' - 'A')) : value;
    }

    private static string FoldAscii(string value)
    {
        return string.Create(value.Length, value, static (destination, source) =>
        {
            for (var i = 0; i < source.Length; i++)
            {
                destination[i] = FoldAscii(source[i]);
            }
        });
    }
}
