using System.Windows;
using System.Diagnostics;
using System.Windows.Input;
using System.Globalization;
using System.Windows.Threading;
using ParallelScope.Services;
using ParallelScope.Utilities;
using ParallelScope.ViewModels;
using ParallelScope.Views;

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

        // ペインは列レイアウトの初期化にViewModelを必要とするため、ViewModelの生成後に組み立てる
        RebuildPaneLayout();

        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
        // 自前タイトルバーの最大化時のはみ出し補正は、ウィンドウハンドルができてから仕掛ける
        SourceInitialized += MainWindow_SourceInitialized;
        // タブのショートカットは、アドレス欄・検索欄に入力中でも効かせたいのでウィンドウ側で拾う
        PreviewKeyDown += MainWindow_PreviewKeyDown;
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
        ApplyPlusFeatures();

        RequestAutomaticFullScan();
        ConfigureScheduledFullScanTimer();
    }

    // ウィンドウクローズ時に定期スキャンタイマーを停止する
    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        try
        {
            // 列レイアウトは全ペイン共通の設定なので、最後に触ったペイン（操作対象）の値を保存する
            ActivePaneView.SaveFileListColumnLayout();
        }
        catch
        {
            // 設定ファイルが書けない状況でも終了処理は続行する
        }

        _scheduledFullScanTimer.Stop();
        _scheduledFullScanTimer.Tick -= ScheduledFullScanTimer_Tick;
        PreviewKeyDown -= MainWindow_PreviewKeyDown;

        foreach (var pane in _panes)
        {
            pane.Detach();
        }
    }

    // Plus機能（ツリーのお気に入り・最近・よく使うノード、一覧の表示列・列幅、CSV書き出し）を
    // 購読状態に合わせて出し分ける
    private void ApplyPlusFeatures()
    {
        var isActive = _storeLicenseService.IsPlusActive;

        _viewModel.SetPlusFeaturesEnabled(isActive);
        // 保存済みのタブ構成・分割状態は、購読が確認できた時点で1回だけ復元する
        _viewModel.RestorePanes(isActive);
        ApplySplitViewState();

        ExportCsvMenuItem.IsEnabled = isActive;
        SplitViewMenuItem.IsEnabled = isActive;
        SplitVerticalMenuItem.IsEnabled = isActive;
        SplitHorizontalMenuItem.IsEnabled = isActive;

        // 購読が切れた状態で分割したままにはしない（1画面＝未購読時の見た目に戻す）
        if (!isActive && _viewModel.IsSplitViewEnabled)
        {
            _viewModel.DisableSplitView();
            ApplySplitViewState();
        }

        foreach (var pane in _panes)
        {
            pane.SetTabsEnabled(isActive);
            pane.SetTreeCollapseEnabled(isActive);
            pane.ApplyFileListColumnVisibility();
            pane.ApplyFileListColumnLayout();
        }
    }

    // タブ・ペイン操作のキーボードショートカット（Plus機能のため、未購読の間はペイン側が受け付けない）
    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // F6は修飾キー無しでペインを切り替える（分割していなければ何もしない）
        if (e.Key == Key.F6 && Keyboard.Modifiers == ModifierKeys.None)
        {
            ActivateOtherPane();
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) || !Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            return;
        }

        var isShiftPressed = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        var pane = ActivePaneView;

        switch (e.Key)
        {
            case Key.T when isShiftPressed:
                pane.ReopenClosedTab();
                break;
            case Key.T:
                pane.OpenNewTab();
                break;
            case Key.W:
                pane.CloseActiveTab();
                break;
            case Key.Tab:
                pane.ActivateAdjacentTab(!isShiftPressed);
                break;
            case >= Key.D1 and <= Key.D8:
                pane.ActivateTabAt(e.Key - Key.D1);
                break;
            case Key.D9:
                // ブラウザーと同じく、Ctrl+9 は位置ではなく末尾のタブ
                pane.ActivateLastTab();
                break;
            default:
                return;
        }

        e.Handled = true;
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
        ActivePaneView.SaveFileListColumnLayout();

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
            _viewModel.GetNameIndexEnabled(),
            _viewModel.SetNameIndexEnabled,
            _viewModel.GetRegexSearchEnabled(),
            _viewModel.SetRegexSearchEnabled,
            startOnSubscriptionPage)
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true)
        {
            // Cancelで閉じてもダイアログ内でPlusを購読した可能性があるため、Plus機能の表示は反映し直す
            ApplyPlusFeatures();
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
            foreach (var pane in _panes)
            {
                pane.ResetFileListColumnWidths();
            }
        }

        ApplyPlusFeatures();
        ConfigureScheduledFullScanTimer();

        foreach (var pane in _panes)
        {
            pane.SyncTreeSelectionToCurrentPath();
        }

        if (dialog.ShouldRunFullScan)
        {
            RequestFullScanFromSettings();
        }
    }

    // 表示中のファイル一覧をCSVへ書き出す（対象は操作対象のペインの一覧）
    private async void ExportCsvMenuItem_Click(object sender, RoutedEventArgs e)
    {
        await ActivePaneView.ExportCsvAsync();
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
}
