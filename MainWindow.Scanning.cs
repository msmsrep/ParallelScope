using System.Windows;
using System.Windows.Threading;
using ParallelScope.Utilities;
using ParallelScope.ViewModels;
using ParallelScope.Views;

namespace ParallelScope;

/// <summary>フルスキャン・フォルダ単位スキャンの実行制御。ペインからのスキャン要求もここで受ける。</summary>
public partial class MainWindow : IBrowserPaneHost
{
    private readonly DispatcherTimer _scheduledFullScanTimer;
    private bool _hasStartedAutomaticFullScan;

    // フルスキャンの実行要求。完了メッセージを出すかどうかだけが違う
    private readonly record struct FullScanRequest(bool ShowCompletionMessage);

    private readonly SingleFlightCoalescer<FullScanRequest> _fullScanCoalescer;
    private CancellationTokenSource? _fullScanCts;

    // ペイン（ツリーのコンテキストメニュー）から要求された、フォルダ配下の個別スキャン
    async Task IBrowserPaneHost.RunFolderScanAsync(FolderItemViewModel folderItem)
    {
        if (folderItem.IsScanning)
        {
            return;
        }

        await RunFolderScanAsync(folderItem);
    }

    // 設定画面の「保存してフルスキャン」から呼ばれる、完了メッセージ付きのフルスキャン。
    // 実行中のフルスキャンはキャンセルし、それが終わり次第この要求をコアレサーが流し直す
    private void RequestFullScanFromSettings()
    {
        CancelFullScan();
        _fullScanCoalescer.Request(new FullScanRequest(ShowCompletionMessage: true));
    }

    // 選択フォルダ配下をスキャンし、現在表示中のフォルダに影響する場合は一覧を再読込する
    private async Task RunFolderScanAsync(FolderItemViewModel folderItem)
    {
        folderItem.IsScanning = true;

        try
        {
            var scannedFolderCount = await _viewModel.ScanFolderSubtreeAsync(folderItem.Path);

            if (PathNormalizer.IsAncestorOrSame(folderItem.Path, _viewModel.CurrentPath))
            {
                // LoadFiles(CurrentPath) は NavigateTo の同一パス早期returnで何もしないため、再読み込み専用APIを使う
                _viewModel.RefreshCurrentFolder();
                _pane.SyncTreeSelectionToCurrentPath();
            }

            MessageBox.Show(
                UiText.Format("Scan.Folder.Completed", scannedFolderCount),
                UiText.Get("Scan.Folder.Caption"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(UiText.Format("Scan.Folder.Failed", ex.Message), UiText.Get("Scan.Folder.ErrorCaption"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            folderItem.IsScanning = false;
        }
    }

    // 定期スキャンタイマー発火時に自動フルスキャンを実行する
    private void ScheduledFullScanTimer_Tick(object? sender, EventArgs e)
    {
        RequestAutomaticFullScan();
    }

    // 設定されたフルスキャン間隔でタイマーを再構成する
    private void ConfigureScheduledFullScanTimer()
    {
        _scheduledFullScanTimer.Stop();
        _scheduledFullScanTimer.Interval = TimeSpan.FromHours(_viewModel.GetFullScanIntervalHours());
        _scheduledFullScanTimer.Start();
    }

    // 起動時・定期タイマーからの自動フルスキャン。完了メッセージは出さず、
    // 実行中（または実行待ち）の場合は重ねて要求しない
    private void RequestAutomaticFullScan()
    {
        if (_fullScanCoalescer.IsBusy)
        {
            return;
        }

        _fullScanCoalescer.Request(new FullScanRequest(ShowCompletionMessage: false));
    }

    // コアレサーは待機の再開をUIスレッドに固定しないため、本体はディスパッチャー上で実行する
    // （メッセージ表示・ツリーの更新がUIスレッド前提のため）
    private Task RunFullScanAsync(FullScanRequest request)
    {
        return Dispatcher.InvokeAsync(() => RunFullScanCoreAsync(request)).Task.Unwrap();
    }

    // フルスキャン本体。完了/キャンセル/失敗に応じてメッセージを出し分ける
    private async Task RunFullScanCoreAsync(FullScanRequest request)
    {
        var showCompletionMessage = request.ShowCompletionMessage;
        _fullScanCts = new CancellationTokenSource();
        var token = _fullScanCts.Token;
        SetRootScanningState(true);

        try
        {
            var scannedFolderCount = await _viewModel.FullScanConfiguredRootsAsync(token);

            if (!string.IsNullOrWhiteSpace(_viewModel.CurrentPath))
            {
                // LoadFiles(CurrentPath) は NavigateTo の同一パス早期returnで何もしないため、再読み込み専用APIを使う
                _viewModel.RefreshCurrentFolder();
                _pane.SyncTreeSelectionToCurrentPath();
            }

            if (showCompletionMessage)
            {
                MessageBox.Show(
                    UiText.Format("Scan.Full.Completed", scannedFolderCount),
                    UiText.Get("Scan.Full.Caption"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
        catch (OperationCanceledException)
        {
            if (showCompletionMessage)
            {
                MessageBox.Show(UiText.Get("Scan.Full.Canceled"), UiText.Get("Scan.Full.CanceledCaption"), MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            if (showCompletionMessage)
            {
                MessageBox.Show(UiText.Format("Scan.Full.Failed", ex.Message), UiText.Get("Scan.Full.ErrorCaption"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        finally
        {
            SetRootScanningState(false);
            _fullScanCts?.Dispose();
            _fullScanCts = null;
        }
    }

    // 実行中のフルスキャンをキャンセルする（再実行の要求は呼び出し側がコアレサーへ投入する）
    private void CancelFullScan()
    {
        _fullScanCts?.Cancel();
    }

    // 全ルートフォルダのスキャン中表示フラグを一括で切り替える
    private void SetRootScanningState(bool isScanning)
    {
        foreach (var rootFolder in _viewModel.RootFolders)
        {
            rootFolder.IsScanning = isScanning;
        }
    }
}
