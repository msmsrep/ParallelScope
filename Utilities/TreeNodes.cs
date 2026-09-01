namespace ParallelScope.Utilities;

/// <summary>
/// フォルダツリー最上位のノードのカスタマイズ（表示/非表示・並び順）で使うキーの定義。
/// キーは <see cref="VirtualFolderKind"/> の名前そのままで、settings.json に保存されるため
/// リネームすると既存設定が無効になる点に注意。
/// </summary>
public static class TreeNodes
{
    /// <summary>全ルートを束ねる「Folders」。常に表示され、並び順の指定対象にだけ含まれる。</summary>
    public const string AllRoots = nameof(VirtualFolderKind.AllRoots);

    public const string Favorites = nameof(VirtualFolderKind.Favorites);
    public const string Recent = nameof(VirtualFolderKind.Recent);
    public const string Frequent = nameof(VirtualFolderKind.Frequent);

    /// <summary>表示/非表示を切り替えられるノードの一覧（既定の並び順）。</summary>
    public static readonly IReadOnlyList<string> OptionalNodes =
        new[] { Favorites, Recent, Frequent };

    /// <summary>未設定時に表示するノード（Plus購読中は最初から全て表示する）。</summary>
    public static readonly IReadOnlyList<string> DefaultVisibleNodes =
        new[] { Favorites, Recent, Frequent };

    /// <summary>並び順の指定対象となる全てのノード（既定の並び順）。</summary>
    public static readonly IReadOnlyList<string> AllNodes =
        new[] { AllRoots, Favorites, Recent, Frequent };

    /// <summary>
    /// 実際にツリーへ表示するオプションノードを、既定の並び順で返す。
    /// ツリー最上位のノードはPlus機能のため、未購読（購読期限切れ含む）の間は1つも表示しない。
    /// 設定自体は残すので、購読すれば以前のカスタマイズがそのまま復活する。
    /// </summary>
    public static IReadOnlyList<string> GetEffectiveVisibleNodes(
        IEnumerable<string> configuredNodes,
        bool arePlusFeaturesEnabled)
    {
        if (!arePlusFeaturesEnabled)
        {
            return Array.Empty<string>();
        }

        var configured = configuredNodes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return OptionalNodes.Where(configured.Contains).ToList();
    }

    /// <summary>ノードキーを仮想ノードの種類へ変換する（未知のキーは <see cref="VirtualFolderKind.None"/>）。</summary>
    public static VirtualFolderKind GetKind(string? key)
    {
        return Enum.TryParse<VirtualFolderKind>(key, true, out var kind) && kind != VirtualFolderKind.None
            ? kind
            : VirtualFolderKind.None;
    }

    /// <summary>仮想ノードの種類をノードキーへ変換する。</summary>
    public static string GetKey(VirtualFolderKind kind) => kind.ToString();

    /// <summary>ノードキーに対応する表示名の対訳表キーを返す。</summary>
    public static string GetDisplayNameKey(string key) => VirtualFolders.GetDisplayNameKey(GetKind(key));
}
