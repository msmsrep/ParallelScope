using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ParallelScope.Services;
using ParallelScope.Utilities;
using ParallelScope.ViewModels;

namespace ParallelScope;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private readonly StoreLicenseService _storeLicenseService = new();
    public MainWindow()
    {
        InitializeComponent();

        _defaultFileListColumnWidths = GetFileListColumnsByKey()
            .ToDictionary(pair => pair.Key, pair => pair.Value.Width, StringComparer.OrdinalIgnoreCase);

        // AppxManifest.xmlのバージョンをタイトルに付与する（取得できない場合は元のタイトルのまま）
        Title = BuildWindowTitleWithVersion(Title);

        _viewModel = new MainWindowViewModel();
        // 保存済みの言語・テーマは最初の描画前に適用する
        // （App.xamlのThemeMode="System"のままだと一瞬OSの配色で表示されてしまう）
        _viewModel.ApplySavedLanguage();
        AppTheme.Apply(_viewModel.GetTheme());
        _scheduledFullScanTimer = new DispatcherTimer();
        _scheduledFullScanTimer.Tick += ScheduledFullScanTimer_Tick;
        // フルスキャンの多重実行防止とキャンセル後の再実行は、ViewModel側と同じくコアレサーに任せる
        _fullScanCoalescer = new SingleFlightCoalescer<FullScanRequest>(RunFullScanAsync);
        DataContext = _viewModel;
        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
        AppLanguage.Changed += AppLanguage_Changed;

        ApplyFileListColumnHeaders();
        ApplyFileListColumnVisibility();
        SyncTreeSelectionToCurrentPath();
    }

    // 言語切り替え時、バインディングでは追従しない箇所（ファイル一覧の列見出し）を貼り替える
    private void AppLanguage_Changed(object? sender, EventArgs e)
    {
        ApplyFileListColumnHeaders();
    }

    // ウィンドウ表示後に自動フルスキャンを1回だけ実行し、以降は定期スキャンタイマーに切り替える
    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (_hasStartedAutomaticFullScan)
        {
            return;
        }

        _hasStartedAutomaticFullScan = true;

        // Plusの購読状態を確認し、購読済みならユーザー設定の表示列を反映し直す
        // （コンストラクタ時点ではライセンス未取得のためデフォルト列で表示されている）。
        // settings.jsonに開発者キーが設定されていればStoreの購読状態に関わらずPlusを有効化する
        _storeLicenseService.ApplyDeveloperUnlockKey(_viewModel.GetDeveloperUnlockKey());
        await _storeLicenseService.RefreshLicenseAsync();
        ApplyFileListColumnVisibility();
        ApplyFileListColumnLayout();
        ApplyPlusTreeNodes();

        RequestAutomaticFullScan();
        ConfigureScheduledFullScanTimer();
    }

    // ウィンドウクローズ時に定期スキャンタイマーを停止する
    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        try
        {
            SaveFileListColumnLayout();
        }
        catch
        {
            // 設定ファイルが書けない状況でも終了処理は続行する
        }

        _scheduledFullScanTimer.Stop();
        _scheduledFullScanTimer.Tick -= ScheduledFullScanTimer_Tick;
        AppLanguage.Changed -= AppLanguage_Changed;
    }

    // "アプリ名" を "アプリ名 vX.Y.Z.W" に組み立てる。バージョンが取得できない場合は元のタイトルのまま返す
    private static string BuildWindowTitleWithVersion(string baseTitle)
    {
        var version = AppVersionProvider.GetVersion();
        return string.IsNullOrWhiteSpace(version) ? baseTitle : $"{baseTitle}  ver{version}";
    }

    // 設定画面を開き、保存された場合は設定を適用してタイマー・ツリー選択を再構成する
    private void OpenSettingsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ShowSettingsDialog(startOnSubscriptionPage: false);
    }

    private void ShowSettingsDialog(bool startOnSubscriptionPage)
    {
        // 設定画面には最新の並び順を渡したいので、ヘッダーのドラッグで変わっている可能性のある
        // 現在の列レイアウトを先に確定させる
        SaveFileListColumnLayout();

        var dialog = new SettingsWindow(
            _viewModel.GetConfiguredRootPaths(),
            _viewModel.GetExcludedPaths(),
            _viewModel.GetFullScanIntervalHours(),
            _viewModel.GetVisibleColumns(),
            _viewModel.GetColumnOrder(),
            _viewModel.GetVisibleTreeNodes(),
            _viewModel.GetTreeNodeOrder(),
            _viewModel.GetTheme(),
            _viewModel.ApplyTheme,
            _viewModel.GetLanguage(),
            _viewModel.ApplyLanguage,
            _storeLicenseService,
            _viewModel.GetShowHiddenItems(),
            _viewModel.GetShowSystemItems(),
            startOnSubscriptionPage)
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true)
        {
            // Cancelで閉じてもダイアログ内でPlusを購読した可能性があるため、Plus機能の表示は反映し直す
            ApplyFileListColumnVisibility();
            ApplyFileListColumnLayout();
            ApplyPlusTreeNodes();
            return;
        }

        // 設定画面で並び順を変える前に、ヘッダーのドラッグで変わっている可能性のある現在の並び順を確定させる
        _viewModel.ApplySettings(
            dialog.ResultRootPaths,
            dialog.ResultExcludedPaths,
            dialog.ResultFullScanIntervalHours,
            dialog.ResultVisibleColumns,
            dialog.ResultColumnOrder,
            dialog.ResultVisibleTreeNodes,
            dialog.ResultTreeNodeOrder,
            dialog.ResultShowHiddenItems,
            dialog.ResultShowSystemItems);
        // 保存済み幅を消してから並び順・列幅を反映し直す（消し忘れると直後のApplyで元の幅に戻ってしまう）
        if (dialog.ShouldResetColumnWidths)
        {
            ResetFileListColumnWidths();
        }

        ApplyFileListColumnVisibility();
        ApplyFileListColumnLayout();
        ApplyPlusTreeNodes();
        ConfigureScheduledFullScanTimer();
        SyncTreeSelectionToCurrentPath();

        if (dialog.ShouldRunFullScan)
        {
            RequestFullScanFromSettings();
        }
    }

    // 使い方ガイド（GitHub Pages）を既定のブラウザーで開く
    private void OpenUserGuideMenuItem_Click(object sender, RoutedEventArgs e)
    {
        // アプリの表示言語（CurrentUICultureはAppLanguage.Applyが設定済み）に合わせてページを選ぶ
        // （ページ側にも言語の切り替えリンクがあるため、外した場合も辿り着ける）
        var url = string.Equals(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, "ja", StringComparison.OrdinalIgnoreCase)
            ? "https://msmsrep.github.io/ParallelScope/index.ja.html"
            : "https://msmsrep.github.io/ParallelScope/";

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(UiText.Format("UserGuide.OpenFailed", ex.Message), UiText.Get("UserGuide.Caption"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
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

}