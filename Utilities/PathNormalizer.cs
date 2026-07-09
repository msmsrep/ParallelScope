using System.IO;

namespace ParallelScope.Utilities;

/// <summary>
/// パス比較・正規化ロジックを一箇所にまとめたユーティリティ。
/// 複数クラス（ViewModel / コードビハインド / リポジトリ）で重複していた実装を統合したもの。
/// </summary>
public static class PathNormalizer
{
    /// <summary>絶対パス化し、末尾の区切り文字を除去した正規形を返す（ドライブルートは除去しない）。</summary>
    public static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var fullPath = Path.GetFullPath(path);
        var rootPath = Path.GetPathRoot(fullPath);

        if (!string.IsNullOrEmpty(rootPath) && string.Equals(fullPath, rootPath, StringComparison.OrdinalIgnoreCase))
        {
            return fullPath;
        }

        return fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    /// <summary>正規化した上で2つのパスが同一かどうかを判定する。</summary>
    public static bool AreSame(string leftPath, string rightPath)
    {
        return string.Equals(Normalize(leftPath), Normalize(rightPath), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>targetPath が ancestorPath 自身またはその配下かどうかを判定する。</summary>
    public static bool IsAncestorOrSame(string ancestorPath, string targetPath)
    {
        var normalizedAncestor = Normalize(ancestorPath);
        var normalizedTarget = Normalize(targetPath);

        if (string.Equals(normalizedAncestor, normalizedTarget, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var prefix = WithTrailingSeparator(normalizedAncestor);
        return normalizedTarget.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>末尾にディレクトリ区切り文字が無ければ付与する。</summary>
    public static string WithTrailingSeparator(string normalizedPath)
    {
        return normalizedPath.EndsWith(Path.DirectorySeparatorChar)
            ? normalizedPath
            : normalizedPath + Path.DirectorySeparatorChar;
    }
}
