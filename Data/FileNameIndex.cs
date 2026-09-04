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
/// **格納順は画面の表示順（フォルダが先、次に名前の昇順）**にしてある —— 検索結果を上から順に
/// 流して出せるようにするため。SQLの ORDER BY は全件そろうまで1件も返さないので、
/// 並べ替えを索引側に持たないと「貯まった分から表示する」段階表示が効かない。
/// 索引はキャッシュDBのある時点の写しなので、その後に変わった親フォルダは
/// <see cref="MarkParentChanged"/> で控えておき、検索時にその分だけDBから引き直して混ぜる。
/// </remarks>
public sealed class FileNameIndex
{
    private readonly FileCacheRepository _repository;

    /// <summary>本体をまとめて取り出す単位。この件数ごとに結果を返せるので、段階表示の粒度にもなる。</summary>
    private const int FetchBatchSize = 2_000;

    /// <summary>組み上がった索引一式。差し替えは参照の入れ替え1回で済ませ、読み手が途中の状態を見ないようにする。</summary>
    private sealed record Snapshot(
        char[] PackedNames,
        int[] NameOffsets,
        int[] ParentIds,
        int[] RowIds,
        bool[] IsFolders,
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

    /// <summary>索引を作り直す（バックグラウンドから呼ぶこと。150万件で2秒強かかる）。</summary>
    public void Build()
    {
        var (packedNames, nameOffsets, parentIds, rowIds, isFolders, parentPaths) = ReadAllEntries();
        var displayOrder = BuildDisplayOrder(packedNames, nameOffsets, isFolders);
        var snapshot = Reorder(packedNames, nameOffsets, parentIds, rowIds, isFolders, parentPaths, displayOrder);

        lock (_changedParentPaths)
        {
            _changedParentPaths.Clear();
        }

        Volatile.Write(ref _snapshot, snapshot);
    }

    /// <summary>キャッシュDBの全行から、名前（ASCII大文字化して連結）と付随情報を読み出す。</summary>
    private (List<char> PackedNames, List<int> NameOffsets, List<int> ParentIds, List<int> RowIds, List<bool> IsFolders, List<string> ParentPaths)
        ReadAllEntries()
    {
        var packedNames = new List<char>();
        var nameOffsets = new List<int> { 0 };
        var parentIds = new List<int>();
        var rowIds = new List<int>();
        var isFolders = new List<bool>();
        var parentPaths = new List<string>();
        var parentIdByPath = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var row in _repository.EnumerateNameIndexRows())
        {
            rowIds.Add(row.Id);
            isFolders.Add(row.IsFolder);

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
                packedNames.Add(NameSearchMatcher.FoldAscii(character));
            }

            nameOffsets.Add(packedNames.Count);
        }

        return (packedNames, nameOffsets, parentIds, rowIds, isFolders, parentPaths);
    }

    /// <summary>表示順（フォルダが先、次に名前の昇順）へ並べ替える順番を求める。</summary>
    private static int[] BuildDisplayOrder(List<char> packedNames, List<int> nameOffsets, List<bool> isFolders)
    {
        var names = packedNames.ToArray();
        var offsets = nameOffsets;
        var order = new int[isFolders.Count];
        for (var i = 0; i < order.Length; i++)
        {
            order[i] = i;
        }

        Array.Sort(order, (left, right) =>
        {
            var folderCompare = isFolders[right].CompareTo(isFolders[left]);
            if (folderCompare != 0)
            {
                return folderCompare;
            }

            return names.AsSpan(offsets[left], offsets[left + 1] - offsets[left])
                .SequenceCompareTo(names.AsSpan(offsets[right], offsets[right + 1] - offsets[right]));
        });

        return order;
    }

    /// <summary>求めた順番どおりに詰め直す（走査が連続アクセスになり、そのまま表示順に流せる）。</summary>
    private static Snapshot Reorder(
        List<char> packedNames, List<int> nameOffsets, List<int> parentIds, List<int> rowIds,
        List<bool> isFolders, List<string> parentPaths, int[] displayOrder)
    {
        var count = displayOrder.Length;
        var sourceNames = packedNames.ToArray();
        var sortedNames = new char[sourceNames.Length];
        var sortedOffsets = new int[count + 1];
        var sortedParentIds = new int[count];
        var sortedRowIds = new int[count];
        var sortedIsFolders = new bool[count];

        var position = 0;
        for (var i = 0; i < count; i++)
        {
            var source = displayOrder[i];
            var start = nameOffsets[source];
            var length = nameOffsets[source + 1] - start;

            Array.Copy(sourceNames, start, sortedNames, position, length);
            position += length;

            sortedOffsets[i + 1] = position;
            sortedParentIds[i] = parentIds[source];
            sortedRowIds[i] = rowIds[source];
            sortedIsFolders[i] = isFolders[source];
        }

        return new Snapshot(sortedNames, sortedOffsets, sortedParentIds, sortedRowIds, sortedIsFolders, parentPaths.ToArray(), count);
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
    /// <remarks>
    /// 結果は表示順のまま少しずつ返す（全件そろうのを待たない）。呼び出し側の段階表示が
    /// 最初のひとまとまりをすぐ画面へ出せるようにするため。
    /// </remarks>
    public IEnumerable<CachedFileSystemEntry>? SearchUnderPath(string rootPath, NameSearchPattern pattern)
    {
        var snapshot = Volatile.Read(ref _snapshot);
        return snapshot is null ? null : EnumerateMatches(snapshot, rootPath, pattern);
    }

    private IEnumerable<CachedFileSystemEntry> EnumerateMatches(Snapshot snapshot, string rootPath, NameSearchPattern pattern)
    {
        var normalizedRoot = PathNormalizer.Normalize(rootPath);
        var rootPrefix = PathNormalizer.WithTrailingSeparator(normalizedRoot);

        string[] changedParentPaths;
        lock (_changedParentPaths)
        {
            changedParentPaths = _changedParentPaths.ToArray();
        }

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

        // 索引を作ったあとに変わった親フォルダはDBから引き直し、索引側と同じ並びに揃えて混ぜる
        var changedEntries = ReadChangedParents(changedParentPaths, normalizedRoot, rootPrefix, pattern);
        var changedIndex = 0;

        var batchRowIds = new List<int>(FetchBatchSize);
        var batchPositions = new Dictionary<int, int>(FetchBatchSize);

        for (var i = 0; i < snapshot.EntryCount; i++)
        {
            if (!searchable[snapshot.ParentIds[i]])
            {
                continue;
            }

            var nameStart = snapshot.NameOffsets[i];
            var nameLength = snapshot.NameOffsets[i + 1] - nameStart;
            if (!pattern.MatchesFolded(snapshot.PackedNames.AsSpan(nameStart, nameLength)))
            {
                continue;
            }

            batchPositions[snapshot.RowIds[i]] = i;
            batchRowIds.Add(snapshot.RowIds[i]);

            if (batchRowIds.Count < FetchBatchSize)
            {
                continue;
            }

            foreach (var entry in FetchOrdered(snapshot, batchRowIds, batchPositions, changedEntries, ref changedIndex))
            {
                yield return entry;
            }

            batchRowIds.Clear();
            batchPositions.Clear();
        }

        foreach (var entry in FetchOrdered(snapshot, batchRowIds, batchPositions, changedEntries, ref changedIndex))
        {
            yield return entry;
        }

        // 索引側を出し切ったあとに残った引き直し分（並びの最後に来るもの）
        for (; changedIndex < changedEntries.Count; changedIndex++)
        {
            yield return changedEntries[changedIndex].Entry;
        }
    }

    /// <summary>
    /// 1まとまり分の本体をDBから取り出し、索引の並び（＝表示順）へ戻したうえで、
    /// 引き直し分を並びの正しい位置へ差し込みながら返す。
    /// </summary>
    private List<CachedFileSystemEntry> FetchOrdered(
        Snapshot snapshot,
        List<int> batchRowIds,
        Dictionary<int, int> batchPositions,
        List<(CachedFileSystemEntry Entry, bool IsFolder, string FoldedName)> changedEntries,
        ref int changedIndex)
    {
        var fetched = _repository.GetEntriesByIds(batchRowIds);

        // DBは指定した順では返さないため、索引上の位置（＝表示順）で並べ直す
        var ordered = new List<(int Position, CachedFileSystemEntry Entry)>(fetched.Count);
        foreach (var (id, entry) in fetched)
        {
            if (batchPositions.TryGetValue(id, out var position))
            {
                ordered.Add((position, entry));
            }
        }

        ordered.Sort(static (left, right) => left.Position.CompareTo(right.Position));

        var result = new List<CachedFileSystemEntry>(ordered.Count);
        foreach (var (position, entry) in ordered)
        {
            // 引き直し分のうち、この行より前に並ぶものを先に出す
            while (changedIndex < changedEntries.Count
                && CompareChangedToIndexEntry(changedEntries[changedIndex], snapshot, position) <= 0)
            {
                result.Add(changedEntries[changedIndex].Entry);
                changedIndex++;
            }

            result.Add(entry);
        }

        return result;
    }

    /// <summary>引き直した1件が、索引上の指定位置の行より前に並ぶか（負なら前）。</summary>
    private static int CompareChangedToIndexEntry(
        (CachedFileSystemEntry Entry, bool IsFolder, string FoldedName) changed, Snapshot snapshot, int position)
    {
        // 表示順はフォルダが先。索引側がファイルで引き直し分がフォルダなら、引き直し分が前になる
        var folderCompare = snapshot.IsFolders[position].CompareTo(changed.IsFolder);
        if (folderCompare != 0)
        {
            return folderCompare;
        }

        var start = snapshot.NameOffsets[position];
        var length = snapshot.NameOffsets[position + 1] - start;
        return changed.FoldedName.AsSpan().SequenceCompareTo(snapshot.PackedNames.AsSpan(start, length));
    }

    /// <summary>索引を作ったあとに変わった親フォルダぶんを引き直し、索引と同じ並びに揃える。</summary>
    private List<(CachedFileSystemEntry Entry, bool IsFolder, string FoldedName)> ReadChangedParents(
        string[] changedParentPaths, string normalizedRoot, string rootPrefix, NameSearchPattern pattern)
    {
        var entries = new List<(CachedFileSystemEntry Entry, bool IsFolder, string FoldedName)>();

        foreach (var changedParentPath in changedParentPaths)
        {
            if (!IsUnderRoot(changedParentPath, normalizedRoot, rootPrefix))
            {
                continue;
            }

            foreach (var entry in _repository.GetSearchEntriesInParent(changedParentPath, pattern))
            {
                entries.Add((entry, entry.IsFolder, NameSearchMatcher.FoldAscii(entry.Name)));
            }
        }

        entries.Sort(static (left, right) =>
        {
            var folderCompare = right.IsFolder.CompareTo(left.IsFolder);
            return folderCompare != 0 ? folderCompare : string.CompareOrdinal(left.FoldedName, right.FoldedName);
        });

        return entries;
    }

    /// <summary>親フォルダのパスが検索の起点そのものか、その配下か。</summary>
    /// <remarks>起点直下のファイルは ParentPath が起点と等しくなるため、前方一致だけでは取りこぼす。</remarks>
    private static bool IsUnderRoot(string parentPath, string normalizedRoot, string rootPrefix)
    {
        return string.Equals(parentPath, normalizedRoot, StringComparison.OrdinalIgnoreCase)
            || parentPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase);
    }

}
