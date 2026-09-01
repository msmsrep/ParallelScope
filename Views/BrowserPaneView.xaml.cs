using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ParallelScope.Services;
using ParallelScope.Utilities;
using ParallelScope.ViewModels;

namespace ParallelScope.Views;

/// <summary>
/// フォルダツリーとファイル一覧を1組にした閲覧ペイン。
/// 2画面表示（分割）では同じコントロールが2つ並ぶため、ツリーの選択同期・
/// ファイル一覧の列レイアウトなど「表示に紐づく状態」はすべてこのインスタンス側に持つ。
/// 各責務（列/ツリー/一覧）は partial クラスとしてファイル分割されている。
/// </summary>
public partial class BrowserPaneView : UserControl
{
    private readonly MainWindowViewModel _viewModel;
    private readonly StoreLicenseService _storeLicenseService;
    private readonly IBrowserPaneHost _host;

    internal BrowserPaneView(MainWindowViewModel viewModel, StoreLicenseService storeLicenseService, IBrowserPaneHost host)
    {
        InitializeComponent();

        _viewModel = viewModel;
        _storeLicenseService = storeLicenseService;
        _host = host;

        _defaultFileListColumnWidths = GetFileListColumnsByKey()
            .ToDictionary(pair => pair.Key, pair => pair.Value.Width, StringComparer.OrdinalIgnoreCase);

        DataContext = _viewModel;
        AppLanguage.Changed += AppLanguage_Changed;

        ApplyFileListColumnHeaders();
        ApplyFileListColumnVisibility();
        SyncTreeSelectionToCurrentPath();
    }

    /// <summary>ウィンドウを閉じる際に、購読しているアプリ全体のイベントから外れる。</summary>
    internal void Detach()
    {
        AppLanguage.Changed -= AppLanguage_Changed;
    }

    // 言語切り替え時、バインディングでは追従しない箇所（ファイル一覧の列見出し）を貼り替える
    private void AppLanguage_Changed(object? sender, EventArgs e)
    {
        ApplyFileListColumnHeaders();
    }

    // 戻る履歴のフォルダへ移動し、ツリー選択を同期する
    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.GoBack())
        {
            SyncTreeSelectionToCurrentPath();
        }
    }

    // 進む履歴のフォルダへ移動し、ツリー選択を同期する
    private void ForwardButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.GoForward())
        {
            SyncTreeSelectionToCurrentPath();
        }
    }

    // 親フォルダへ移動し、ツリー選択を同期する
    private void UpButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.GoUp())
        {
            SyncTreeSelectionToCurrentPath();
        }
    }

    // Enterキーでアドレス欄のパスへ移動する
    private void AddressTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        NavigateByAddressInput();
        e.Handled = true;
    }

    // アドレス欄のパスへ移動する。失敗した場合はエラーメッセージを表示する
    private void NavigateByAddressInput()
    {
        if (_viewModel.TryNavigateByAddressInput())
        {
            SyncTreeSelectionToCurrentPath();
            return;
        }

        MessageBox.Show(UiText.Get("Navigation.Failed"), UiText.Get("Navigation.Caption"), MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    // 指定した要素の祖先から、型Tに一致する最初の要素を探す（コンテキストメニュー表示位置の特定などに使用）
    private static T? GetAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }
}
