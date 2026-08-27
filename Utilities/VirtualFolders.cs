namespace ParallelScope.Utilities;

/// <summary>フォルダツリー最上位に置く仮想ノードの種類。</summary>
public enum VirtualFolderKind
{
    /// <summary>仮想ノードではない（実在パス）。</summary>
    None,

    /// <summary>全ルートフォルダを束ねる「Folders」。</summary>
    AllRoots,

    /// <summary>ユーザーが登録したお気に入りフォルダを束ねる「Favorites」。</summary>
    Favorites,

    /// <summary>アクセス回数の多いフォルダを自動で束ねる「Frequently Used」。</summary>
    Frequent
}

/// <summary>
/// フォルダツリー最上位の仮想ノード（Favorites / Frequently Used / Folders）の定義。
/// Windowsのパスに使えない ":" を含む文字列を仮想パスとして使い、実在パスと衝突しないようにする。
/// 仮想パスは CurrentPath・履歴・アドレス欄にもそのまま入る。
/// </summary>
public static class VirtualFolders
{
    public const string AllRootsPath = "::Folders::";
    public const string FavoritesPath = "::Favorites::";
    public const string FrequentPath = "::Frequent::";

    // 表示名は言語設定で変わるため、対訳表（UiTextResources）のキーだけを持つ。
    // アイコンは実フォルダと共通のため、種類の区別は表示名の絵文字で付ける
    public const string AllRootsDisplayNameKey = "Tree.Folders";
    public const string FavoritesDisplayNameKey = "Tree.Favorites";
    public const string FrequentDisplayNameKey = "Tree.Frequent";

    /// <summary>指定パスがどの仮想ノードのものかを判定する（実在パスなら None）。</summary>
    public static VirtualFolderKind GetKind(string? path)
    {
        if (string.Equals(path, AllRootsPath, StringComparison.OrdinalIgnoreCase))
        {
            return VirtualFolderKind.AllRoots;
        }

        if (string.Equals(path, FavoritesPath, StringComparison.OrdinalIgnoreCase))
        {
            return VirtualFolderKind.Favorites;
        }

        if (string.Equals(path, FrequentPath, StringComparison.OrdinalIgnoreCase))
        {
            return VirtualFolderKind.Frequent;
        }

        return VirtualFolderKind.None;
    }

    public static bool IsAllRoots(string? path) => GetKind(path) == VirtualFolderKind.AllRoots;

    /// <summary>仮想ノードのパスかどうか（種類を問わない）。</summary>
    public static bool IsVirtual(string? path) => GetKind(path) != VirtualFolderKind.None;

    /// <summary>仮想パスの正規形（大文字小文字を定数側に揃えた文字列）を返す。実在パスならnull。</summary>
    public static string? GetCanonicalPath(string? path) => GetKind(path) switch
    {
        VirtualFolderKind.AllRoots => AllRootsPath,
        VirtualFolderKind.Favorites => FavoritesPath,
        VirtualFolderKind.Frequent => FrequentPath,
        _ => null
    };

    /// <summary>現在の表示言語での仮想ノードの表示名を返す。</summary>
    public static string GetDisplayName(VirtualFolderKind kind) => UiText.Get(GetDisplayNameKey(kind));

    /// <summary>仮想ノードの表示名の対訳表キーを返す。</summary>
    public static string GetDisplayNameKey(VirtualFolderKind kind) => kind switch
    {
        VirtualFolderKind.Favorites => FavoritesDisplayNameKey,
        VirtualFolderKind.Frequent => FrequentDisplayNameKey,
        _ => AllRootsDisplayNameKey
    };
}
