using System.IO;

namespace ParallelScope.Utilities;

/// <summary>
/// 隠し属性・システム属性のファイル/フォルダを表示するかどうかの判定。
/// 無料版でも使える設定で、既定は両方とも表示する（この設定を入れる前と同じ見え方を保つため。
/// エクスプローラーの既定とは逆だが、更新で見えていたファイルが消える方が困る）。
/// キャッシュには属性に関わらず全て記録しておき、表示の時点で絞り込む
/// （設定を切り替えるたびにスキャンし直さずに済むため）。
/// </summary>
public static class HiddenItemVisibility
{
    /// <summary>表示設定に関わらず常に飛ばす属性（シンボリックリンク等をたどって循環しないため）。</summary>
    public const FileAttributes AlwaysSkippedAttributes = FileAttributes.ReparsePoint;

    /// <summary>
    /// キャッシュに記録された属性値から、ファイル一覧に出すかどうかを判定する。
    /// 属性列の追加前に書かれたキャッシュ行（null）は、次のスキャンで埋まるまで表示する。
    /// </summary>
    public static bool IsVisible(int? attributes, bool showHiddenItems, bool showSystemItems)
    {
        if (attributes is null)
        {
            return true;
        }

        var value = (FileAttributes)attributes.Value;

        if (!showHiddenItems && value.HasFlag(FileAttributes.Hidden))
        {
            return false;
        }

        return showSystemItems || !value.HasFlag(FileAttributes.System);
    }

    /// <summary>フォルダツリーの子フォルダ列挙で飛ばす属性を組み立てる。</summary>
    public static FileAttributes GetAttributesToSkip(bool showHiddenItems, bool showSystemItems)
    {
        var attributesToSkip = AlwaysSkippedAttributes;

        if (!showHiddenItems)
        {
            attributesToSkip |= FileAttributes.Hidden;
        }

        if (!showSystemItems)
        {
            attributesToSkip |= FileAttributes.System;
        }

        return attributesToSkip;
    }
}
