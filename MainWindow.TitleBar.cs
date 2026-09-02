using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ParallelScope;

/// <summary>
/// 自前タイトルバー（<see cref="System.Windows.Shell.WindowChrome"/>）まわりの処理。
/// キャプションボタンの操作と、最大化時にクライアント領域がはみ出さないようにする補正を持つ。
/// </summary>
public partial class MainWindow
{
    // メニューボタンの真下にメニューを出す（ContextMenuなので右クリックでも同じものが開く）
    private void TitleBarMenuButton_Click(object sender, RoutedEventArgs e)
    {
        if (TitleBarMenuButton.ContextMenu is not { } menu)
        {
            return;
        }

        menu.PlacementTarget = TitleBarMenuButton;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        SystemCommands.MinimizeWindow(this);
    }

    private void MaximizeRestoreButton_Click(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            SystemCommands.RestoreWindow(this);
        }
        else
        {
            SystemCommands.MaximizeWindow(this);
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        SystemCommands.CloseWindow(this);
    }

    // ウィンドウハンドルができた時点でメッセージフックを差し込む
    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        if (PresentationSource.FromVisual(this) is HwndSource source)
        {
            source.AddHook(TitleBarWindowProc);
        }
    }

    private const int WM_GETMINMAXINFO = 0x0024;
    private const int MONITOR_DEFAULTTONEAREST = 0x00000002;

    // WindowChromeを使うとクライアント領域がウィンドウ全体に広がるが、Windowsは最大化時に
    // ウィンドウを枠のぶんだけ画面外へはみ出させるため、そのままだと四辺の内容が画面外で切れる。
    // 最大化サイズ・位置をモニターの作業領域そのものに指定して、はみ出しをなくす
    private IntPtr TitleBarWindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_GETMINMAXINFO)
        {
            return IntPtr.Zero;
        }

        var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        if (monitor == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        var monitorInfo = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref monitorInfo))
        {
            return IntPtr.Zero;
        }

        var info = Marshal.PtrToStructure<MINMAXINFO>(lParam);
        // 位置はモニター左上からの相対座標で指定する（マルチモニターでは負の絶対座標もあり得るため）
        info.ptMaxPosition.X = monitorInfo.rcWork.Left - monitorInfo.rcMonitor.Left;
        info.ptMaxPosition.Y = monitorInfo.rcWork.Top - monitorInfo.rcMonitor.Top;
        info.ptMaxSize.X = monitorInfo.rcWork.Right - monitorInfo.rcWork.Left;
        info.ptMaxSize.Y = monitorInfo.rcWork.Bottom - monitorInfo.rcWork.Top;
        // ptMaxTrackSize は触らない（サイズ変更の上限まで1モニターに縛ってしまうため）
        Marshal.StructureToPtr(info, lParam, false);

        handled = true;
        return IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public int dwFlags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);
}
