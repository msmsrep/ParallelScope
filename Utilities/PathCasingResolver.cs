using System.IO;
using System.Runtime.InteropServices;

namespace ParallelScope.Utilities;

/// <summary>
/// 入力されたパスの大文字小文字を、実際のファイルシステム上の表記へ直す。
/// </summary>
/// <remarks>
/// キャッシュDBは親パスを表記のまま（BINARY照合で）持つため、アドレス欄に <c>c:\users\foo</c> と
/// 打って開くと、<c>C:\Users\foo</c> の中身が別の親パスとして二重に書き込まれてしまう。
/// FullPath のUNIQUE制約も大文字小文字を区別するので重複は弾かれず、ファイル名索引では検索結果に
/// 同じファイルが2件ずつ出る。移動先を確定する時点で表記をそろえ、そもそも別表記で書き込ませない。
/// 登録済みルートの配下はルートの表記を優先する —— スキャンはルートの表記をそのまま前置きして
/// 書き込むため、ルート自体が実際の表記と違っていてもキャッシュ側とそろう。
/// ファイルシステムへの問い合わせを伴うので、UIスレッドからは直接呼ばないこと。
/// </remarks>
public static class PathCasingResolver
{
    /// <param name="normalizedPath"><see cref="PathNormalizer.Normalize"/> 済みの実在するフォルダパス。</param>
    /// <param name="knownRoots">登録済みルート（正規化済み）。配下ならルートの表記を前置きに使う。</param>
    /// <param name="isKnownExactPath">
    /// この表記のままで正しいと分かっているか（キャッシュにフォルダとしてそのまま載っている等）。
    /// 一覧やツリーからの移動はほぼこれで済み、階層ぶんのファイルシステム問い合わせ（NASでは往復）を省ける。
    /// </param>
    /// <returns>表記をそろえたパス。問い合わせに失敗した部分は入力の表記のまま残す。</returns>
    public static string Resolve(string normalizedPath, IReadOnlyList<string> knownRoots, Func<string, bool>? isKnownExactPath = null)
    {
        try
        {
            if (string.IsNullOrEmpty(normalizedPath) || VirtualFolders.IsVirtual(normalizedPath))
            {
                return normalizedPath;
            }

            if (knownRoots.Any(root => string.Equals(root, normalizedPath, StringComparison.Ordinal))
                || isKnownExactPath?.Invoke(normalizedPath) == true)
            {
                return normalizedPath;
            }

            var (prefix, prefixLength) = FindPrefix(normalizedPath, knownRoots);
            if (prefix is null)
            {
                return normalizedPath;
            }

            var current = prefix;
            var segments = normalizedPath[prefixLength..].Split(
                new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var segment in segments)
            {
                current = Path.Combine(current, GetActualName(current, segment) ?? segment);
            }

            return current;
        }
        catch
        {
            // 表記をそろえられなくても移動自体は続けられる（従来どおり入力の表記で開く）
            return normalizedPath;
        }
    }

    /// <summary>
    /// 表記を確定させる前置き部分と、それが入力パスの何文字目までに当たるかを返す。
    /// 登録済みルートの配下なら最も深いルート、それ以外はボリュームのルート（ドライブ文字は大文字へ）。
    /// </summary>
    private static (string? Prefix, int Length) FindPrefix(string normalizedPath, IReadOnlyList<string> knownRoots)
    {
        var root = knownRoots
            .Where(knownRoot => !string.IsNullOrEmpty(knownRoot) && PathNormalizer.IsAncestorOrSame(knownRoot, normalizedPath))
            .MaxBy(knownRoot => knownRoot.Length);
        if (root is not null)
        {
            return (root, root.Length);
        }

        var volumeRoot = Path.GetPathRoot(normalizedPath);
        if (string.IsNullOrEmpty(volumeRoot))
        {
            return (null, 0);
        }

        // UNCのサーバー名・共有名は列挙で表記を確かめられないため、入力のまま使う
        var prefix = volumeRoot.Length >= 2 && volumeRoot[1] == Path.VolumeSeparatorChar
            ? char.ToUpperInvariant(volumeRoot[0]) + volumeRoot[1..]
            : volumeRoot;
        return (prefix, volumeRoot.Length);
    }

    /// <summary>フォルダ内の指定した名前のエントリを、実際の表記で返す（見つからなければ null）。</summary>
    /// <remarks>
    /// Directory.EnumerateDirectories(parent, name) だと名前の照合が.NET側で行われ、親フォルダを丸ごと
    /// 列挙する（数万件のフォルダでは1階層あたり数百ms）。FindFirstFileEx に名前を渡せば1件の問い合わせで済む。
    /// </remarks>
    private static string? GetActualName(string parentPath, string name)
    {
        if (name.IndexOfAny(new[] { '*', '?' }) >= 0)
        {
            return null;
        }

        var handle = FindFirstFileEx(
            Path.Combine(parentPath, name), FindExInfoBasic, out var findData, FindExSearchNameMatch, IntPtr.Zero, 0);
        if (handle == InvalidHandleValue)
        {
            return null;
        }

        FindClose(handle);
        return string.IsNullOrEmpty(findData.cFileName) ? null : findData.cFileName;
    }

    private const int FindExInfoBasic = 1;
    private const int FindExSearchNameMatch = 0;
    private static readonly IntPtr InvalidHandleValue = new(-1);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WIN32_FIND_DATAW
    {
        public uint dwFileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftCreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftLastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftLastWriteTime;
        public uint nFileSizeHigh;
        public uint nFileSizeLow;
        public uint dwReserved0;
        public uint dwReserved1;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string cFileName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
        public string cAlternateFileName;
    }

    [DllImport("kernel32.dll", EntryPoint = "FindFirstFileExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindFirstFileEx(
        string lpFileName, int fInfoLevelId, out WIN32_FIND_DATAW lpFindFileData, int fSearchOp, IntPtr lpSearchFilter, int dwAdditionalFlags);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FindClose(IntPtr hFindFile);
}
