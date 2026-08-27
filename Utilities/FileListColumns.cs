namespace ParallelScope.Utilities;

/// <summary>
/// ファイル一覧の表示列カスタマイズで使う列キーの定義。
/// キーは settings.json に保存されるため、リネームすると既存設定が無効になる点に注意。
/// </summary>
public static class FileListColumns
{
    /// <summary>Name列。表示/非表示は切り替えられないが、並び順の指定対象には含まれる。</summary>
    public const string Name = "Name";

    public const string Location = "Location";
    public const string Type = "Type";
    public const string Size = "Size";
    public const string Modified = "Modified";
    public const string Created = "Created";
    public const string Attributes = "Attributes";

    /// <summary>表示/非表示を切り替えられる列の一覧（画面上の列順）。</summary>
    public static readonly IReadOnlyList<string> OptionalColumns =
        new[] { Location, Type, Size, Modified, Created, Attributes };

    /// <summary>未設定時に表示する列（Name + Type/Size/Modified 相当）。</summary>
    public static readonly IReadOnlyList<string> DefaultVisibleColumns =
        new[] { Type, Size, Modified };

    /// <summary>並び順・列幅の指定対象となる全ての列（既定の並び順）。</summary>
    public static readonly IReadOnlyList<string> AllColumns =
        new[] { Name, Location, Type, Size, Modified, Created, Attributes };

    /// <summary>
    /// 実際にファイル一覧へ表示するオプション列を、画面上の列順で返す。
    /// 列カスタマイズはPlus機能のため、未購読（購読期限切れ含む）の間は保存済み設定を無視して既定列にする。
    /// 設定自体は残すので、購読すれば以前のカスタマイズがそのまま復活する。
    /// </summary>
    public static IReadOnlyList<string> GetEffectiveVisibleColumns(
        IEnumerable<string> configuredColumns,
        bool arePlusFeaturesEnabled)
    {
        var configured = (arePlusFeaturesEnabled ? configuredColumns : DefaultVisibleColumns)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return OptionalColumns.Where(configured.Contains).ToList();
    }
}
