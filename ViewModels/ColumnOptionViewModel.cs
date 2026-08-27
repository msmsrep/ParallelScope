using CommunityToolkit.Mvvm.ComponentModel;

namespace ParallelScope.ViewModels;

/// <summary>
/// 設定画面「Display Columns」の1行（ファイル一覧の列1つ）を表すViewModel。
/// 一覧の並びがそのまま列の並び順になり、チェック状態が表示/非表示になる。
/// </summary>
public class ColumnOptionViewModel : ObservableObject
{
    private bool _isVisible;

    /// <summary>列キー（<see cref="Utilities.FileListColumns"/>）。</summary>
    public string Key { get; }

    /// <summary>設定画面に表示する列名。</summary>
    public string DisplayName { get; }

    /// <summary>表示/非表示を切り替えられるか（Name列は常に表示のためfalse）。</summary>
    public bool CanToggleVisibility { get; }

    public bool IsVisible
    {
        get => _isVisible;
        set => SetProperty(ref _isVisible, value);
    }

    public ColumnOptionViewModel(string key, string displayName, bool isVisible, bool canToggleVisibility)
    {
        Key = key;
        DisplayName = displayName;
        _isVisible = isVisible;
        CanToggleVisibility = canToggleVisibility;
    }
}
