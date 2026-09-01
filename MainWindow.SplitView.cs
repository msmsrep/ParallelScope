using System.Windows;
using System.Windows.Controls;
using ParallelScope.ViewModels;
using ParallelScope.Views;

namespace ParallelScope;

/// <summary>
/// 2画面（分割表示）のレイアウト制御。ペインの生成・配置・比率・最小サイズをここでまとめて扱う。
/// ペインはXAMLに直接置かず、分割の状態に合わせてコードから組み立て直す。
/// </summary>
public partial class MainWindow
{
    // 1ペインが窮屈にならない下限（ツリー200px＋スプリッター4px＋一覧の実用下限320px）
    private const double MinimumPaneWidth = 524;
    private const double MinimumPaneHeight = 260;

    // 分割の有無・向きごとのウィンドウ最小サイズ
    private const double MinimumWindowWidth = 560;
    private const double MinimumWindowHeight = 400;
    private const double MinimumSplitWindowWidth = 1080;
    private const double MinimumSplitWindowHeight = 700;

    /// <summary>分割用スプリッターの太さ。</summary>
    private const double SplitterThickness = 6;

    private readonly List<BrowserPaneView> _panes = new();

    /// <summary>操作対象のペイン（メニュー・スキャンの反映先）。</summary>
    private BrowserPaneView ActivePaneView =>
        _panes.FirstOrDefault(pane => ReferenceEquals(pane.ViewModel, _viewModel.ActivePane)) ?? _panes[0];

    /// <summary>ViewModelのペイン構成どおりに、ペインとスプリッターを配置し直す。</summary>
    private void RebuildPaneLayout()
    {
        PaneHost.Children.Clear();
        PaneHost.ColumnDefinitions.Clear();
        PaneHost.RowDefinitions.Clear();

        // ViewModel側のペインに対応するビューを用意する（既存のインスタンスは使い回す）
        foreach (var paneViewModel in _viewModel.Panes)
        {
            if (_panes.All(pane => !ReferenceEquals(pane.ViewModel, paneViewModel)))
            {
                var pane = new BrowserPaneView(_viewModel, paneViewModel, _storeLicenseService, this);
                pane.SetTabsEnabled(_storeLicenseService.IsPlusActive);
                pane.ApplyFileListColumnVisibility();
                pane.ApplyFileListColumnLayout();
                _panes.Add(pane);
            }
        }

        // 閉じられたペインのビューは破棄する
        foreach (var removed in _panes.Where(pane => !_viewModel.Panes.Contains(pane.ViewModel)).ToList())
        {
            removed.Detach();
            _panes.Remove(removed);
        }

        // ビューの並びをViewModelの並びに合わせる
        _panes.Sort((a, b) => _viewModel.Panes.IndexOf(a.ViewModel).CompareTo(_viewModel.Panes.IndexOf(b.ViewModel)));

        if (_panes.Count == 1)
        {
            PaneHost.Children.Add(_panes[0]);
            return;
        }

        var isVertical = _viewModel.SplitOrientation == PaneSplitOrientation.Vertical;
        var firstLength = new GridLength(_viewModel.SplitRatio, GridUnitType.Star);
        var secondLength = new GridLength(1 - _viewModel.SplitRatio, GridUnitType.Star);

        var splitter = new GridSplitter
        {
            Background = System.Windows.Media.Brushes.Transparent,
            HorizontalAlignment = isVertical ? HorizontalAlignment.Center : HorizontalAlignment.Stretch,
            VerticalAlignment = isVertical ? VerticalAlignment.Stretch : VerticalAlignment.Center,
            Cursor = isVertical ? System.Windows.Input.Cursors.SizeWE : System.Windows.Input.Cursors.SizeNS
        };
        splitter.DragCompleted += SplitViewSplitter_DragCompleted;

        if (isVertical)
        {
            splitter.Width = SplitterThickness;
            PaneHost.ColumnDefinitions.Add(new ColumnDefinition { Width = firstLength, MinWidth = MinimumPaneWidth });
            PaneHost.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            PaneHost.ColumnDefinitions.Add(new ColumnDefinition { Width = secondLength, MinWidth = MinimumPaneWidth });

            Grid.SetColumn(_panes[0], 0);
            Grid.SetColumn(splitter, 1);
            Grid.SetColumn(_panes[1], 2);
        }
        else
        {
            splitter.Height = SplitterThickness;
            PaneHost.RowDefinitions.Add(new RowDefinition { Height = firstLength, MinHeight = MinimumPaneHeight });
            PaneHost.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            PaneHost.RowDefinitions.Add(new RowDefinition { Height = secondLength, MinHeight = MinimumPaneHeight });

            Grid.SetRow(_panes[0], 0);
            Grid.SetRow(splitter, 1);
            Grid.SetRow(_panes[1], 2);
        }

        PaneHost.Children.Add(_panes[0]);
        PaneHost.Children.Add(splitter);
        PaneHost.Children.Add(_panes[1]);
    }

    // スプリッターを離した時点の比率を控える（次に組み立て直すときの初期値になる）
    private void SplitViewSplitter_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        _viewModel.SetSplitRatio(GetCurrentSplitRatio());
    }

    private double GetCurrentSplitRatio()
    {
        if (_viewModel.SplitOrientation == PaneSplitOrientation.Vertical)
        {
            if (PaneHost.ColumnDefinitions.Count < 3)
            {
                return _viewModel.SplitRatio;
            }

            var total = PaneHost.ColumnDefinitions[0].ActualWidth + PaneHost.ColumnDefinitions[2].ActualWidth;
            return total > 0 ? PaneHost.ColumnDefinitions[0].ActualWidth / total : _viewModel.SplitRatio;
        }

        if (PaneHost.RowDefinitions.Count < 3)
        {
            return _viewModel.SplitRatio;
        }

        var totalHeight = PaneHost.RowDefinitions[0].ActualHeight + PaneHost.RowDefinitions[2].ActualHeight;
        return totalHeight > 0 ? PaneHost.RowDefinitions[0].ActualHeight / totalHeight : _viewModel.SplitRatio;
    }

    // メニューの「画面を分割する」で分割のON/OFFを切り替える
    private void SplitViewMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (SplitViewMenuItem.IsChecked)
        {
            _viewModel.EnableSplitView();
        }
        else
        {
            // 比率は畳む前の値を控えておく（次に分割したときに同じ配分で開く）
            _viewModel.SetSplitRatio(GetCurrentSplitRatio());
            _viewModel.DisableSplitView();
        }

        ApplySplitViewState();
    }

    // メニューの「左右に分割」「上下に分割」で向きを切り替える
    private void SplitOrientationMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var orientation = ReferenceEquals(sender, SplitHorizontalMenuItem)
            ? PaneSplitOrientation.Horizontal
            : PaneSplitOrientation.Vertical;

        // 選ばれている方だけがチェック状態になるようにする（クリックで外れた場合も戻す）
        _viewModel.SetSplitOrientation(orientation);
        ApplySplitViewState();
    }

    /// <summary>分割の状態（メニューのチェック・レイアウト・ウィンドウ最小サイズ）をまとめて反映する。</summary>
    private void ApplySplitViewState()
    {
        var isSplit = _viewModel.IsSplitViewEnabled;
        var isVertical = _viewModel.SplitOrientation == PaneSplitOrientation.Vertical;

        SplitViewMenuItem.IsChecked = isSplit;
        SplitVerticalMenuItem.IsChecked = isVertical;
        SplitHorizontalMenuItem.IsChecked = !isVertical;

        RebuildPaneLayout();
        ApplyWindowMinimumSize();
    }

    /// <summary>分割の有無・向きに合わせてウィンドウの最小サイズを設定し、必要なら広げる。</summary>
    private void ApplyWindowMinimumSize()
    {
        var isSplit = _viewModel.IsSplitViewEnabled;
        var isVertical = _viewModel.SplitOrientation == PaneSplitOrientation.Vertical;

        var minWidth = isSplit && isVertical ? MinimumSplitWindowWidth : MinimumWindowWidth;
        var minHeight = isSplit && !isVertical ? MinimumSplitWindowHeight : MinimumWindowHeight;

        // 画面が狭い環境ではウィンドウが画面外へはみ出すため、作業領域を超えないところで丸める
        var workArea = SystemParameters.WorkArea;
        minWidth = Math.Min(minWidth, workArea.Width);
        minHeight = Math.Min(minHeight, workArea.Height);

        MinWidth = minWidth;
        MinHeight = minHeight;

        if (WindowState != WindowState.Normal)
        {
            return;
        }

        // 分割したときに片側が潰れないよう、足りなければ最小サイズまで広げる
        if (Width < minWidth)
        {
            Width = minWidth;
        }

        if (Height < minHeight)
        {
            Height = minHeight;
        }
    }

    /// <summary>操作対象のペインを、もう一方へ切り替える（F6）。</summary>
    private void ActivateOtherPane()
    {
        if (_viewModel.GetOtherPane(_viewModel.ActivePane) is not { } otherPane)
        {
            return;
        }

        ActivatePane(otherPane);
    }

    void IBrowserPaneHost.OnPaneActivated(BrowserPaneView pane)
    {
        _viewModel.SetActivePane(pane.ViewModel);
    }

    void IBrowserPaneHost.ActivatePane(BrowserPaneViewModel paneViewModel) => ActivatePane(paneViewModel);

    private void ActivatePane(BrowserPaneViewModel paneViewModel)
    {
        _viewModel.SetActivePane(paneViewModel);

        var paneView = _panes.FirstOrDefault(pane => ReferenceEquals(pane.ViewModel, paneViewModel));
        paneView?.Focus();
    }
}
