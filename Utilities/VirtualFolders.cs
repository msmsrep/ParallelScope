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
    Frequent,

    /// <summary>最近開いたフォルダを新しい順に束ねる「Recent」。</summary>
    Recent
}

/// <summary>
/// フォルダツリー最上位の仮想ノード（Favorites / Recent / Frequently Used / Folders）の定義。
/// Windowsのパスに使えない ":" を含む文字列を仮想パスとして使い、実在パスと衝突しないようにする。
/// 仮想パスは CurrentPath・履歴・アドレス欄にもそのまま入る。
/// </summary>
public static class VirtualFolders
{
    public const string AllRootsPath = "::Folders::";
    public const string FavoritesPath = "::Favorites::";
    public const string FrequentPath = "::Frequent::";
    public const string RecentPath = "::Recent::";

    // 表示名は言語設定で変わるため、対訳表（UiTextResources）のキーだけを持つ
    public const string AllRootsDisplayNameKey = "Tree.Folders";
    public const string FavoritesDisplayNameKey = "Tree.Favorites";
    public const string FrequentDisplayNameKey = "Tree.Frequent";
    public const string RecentDisplayNameKey = "Tree.Recent";

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

        if (string.Equals(path, RecentPath, StringComparison.OrdinalIgnoreCase))
        {
            return VirtualFolderKind.Recent;
        }

        return VirtualFolderKind.None;
    }

    public static bool IsAllRoots(string? path) => GetKind(path) == VirtualFolderKind.AllRoots;

    /// <summary>仮想ノードのパスかどうか（種類を問わない）。</summary>
    public static bool IsVirtual(string? path) => GetKind(path) != VirtualFolderKind.None;

    /// <summary>仮想ノードの種類に対応する仮想パスを返す（<see cref="VirtualFolderKind.None"/>ならnull）。</summary>
    public static string? GetPath(VirtualFolderKind kind) => kind switch
    {
        VirtualFolderKind.AllRoots => AllRootsPath,
        VirtualFolderKind.Favorites => FavoritesPath,
        VirtualFolderKind.Frequent => FrequentPath,
        VirtualFolderKind.Recent => RecentPath,
        _ => null
    };

    /// <summary>仮想パスの正規形（大文字小文字を定数側に揃えた文字列）を返す。実在パスならnull。</summary>
    public static string? GetCanonicalPath(string? path) => GetPath(GetKind(path));

    /// <summary>現在の表示言語での仮想ノードの表示名を返す。</summary>
    public static string GetDisplayName(VirtualFolderKind kind) => UiText.Get(GetDisplayNameKey(kind));

    /// <summary>
    /// ツリーでフォルダアイコンの代わりに表示する記号を返す（記号を持たないノードはnull）。
    /// 実フォルダと同じアイコンに絵文字付きの表示名を重ねると二重表示に見えるため、種類の区別はこの記号だけで付ける。
    /// </summary>
    public static string? GetGlyph(VirtualFolderKind kind) => kind switch
    {
        VirtualFolderKind.Favorites => "★",
        VirtualFolderKind.Frequent => "🕒",
        VirtualFolderKind.Recent => "🕘",
        _ => null
    };

    /// <summary>仮想ノードの表示名の対訳表キーを返す。</summary>
    public static string GetDisplayNameKey(VirtualFolderKind kind) => kind switch
    {
        VirtualFolderKind.Favorites => FavoritesDisplayNameKey,
        VirtualFolderKind.Frequent => FrequentDisplayNameKey,
        VirtualFolderKind.Recent => RecentDisplayNameKey,
        _ => AllRootsDisplayNameKey
    };
}
