using CommunityToolkit.Mvvm.ComponentModel;
using ParallelScope.Utilities;

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

    /// <summary>設定画面に表示する列名の対訳表キー（<see cref="UiTextResources"/>）。</summary>
    public string DisplayNameKey { get; }

    /// <summary>設定画面に表示する列名。言語で変わるため、都度引き当てる。</summary>
    public string DisplayName => UiText.Get(DisplayNameKey);

    /// <summary>表示/非表示を切り替えられるか（Name列は常に表示のためfalse）。</summary>
    public bool CanToggleVisibility { get; }

    public bool IsVisible
    {
        get => _isVisible;
        set => SetProperty(ref _isVisible, value);
    }

    public ColumnOptionViewModel(string key, string displayNameKey, bool isVisible, bool canToggleVisibility)
    {
        Key = key;
        DisplayNameKey = displayNameKey;
        _isVisible = isVisible;
        CanToggleVisibility = canToggleVisibility;
    }

    /// <summary>言語切り替え後に表示名を引き直す。</summary>
    public void RefreshDisplayName()
    {
        OnPropertyChanged(nameof(DisplayName));
    }
}
