namespace ParallelScope.Utilities;

/// <summary>
/// ファイル一覧の表示列カスタマイズで使う列キーの定義。
/// キーは settings.json に保存されるため、リネームすると既存設定が無効になる点に注意。
/// Name列は常に表示のためここには含めない。
/// </summary>
public static class FileListColumns
{
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
}
