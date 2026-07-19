namespace ParallelScope.Utilities;

/// <summary>
/// 全ルートフォルダを子として束ねる、フォルダツリー最上位の仮想ノード「Folders」の定義。
/// Windowsのパスに使えない ":" を含む文字列を仮想パスとして使い、実在パスと衝突しないようにする。
/// </summary>
public static class AllRootsVirtualFolder
{
    /// <summary>仮想ノードを識別するパス。CurrentPath・履歴・アドレス欄にもこの値がそのまま入る。</summary>
    public const string Path = "::Folders::";

    public const string DisplayName = "Folders";

    /// <summary>指定パスが仮想ノードのパスかどうかを判定する。</summary>
    public static bool Matches(string? path)
    {
        return string.Equals(path, Path, StringComparison.OrdinalIgnoreCase);
    }
}
