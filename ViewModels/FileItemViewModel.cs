using System.Drawing;
using System.Windows.Media.Imaging;

namespace ParallelScope.ViewModels;

public class FileItemViewModel
{
    public string FullPath { get; set; }
    public string Name { get; set; }
    public string SizeText { get; set; }
    public string TypeText { get; set; }
    public string ModifiedTime { get; set; }
    public bool IsFolder { get; set; }
    public BitmapSource? Icon { get; set; }

    public FileItemViewModel(string fullPath, string name, long sizeBytes, DateTime modifiedTime)
    {
        FullPath = fullPath;
        Name = name;
        SizeText = FormatFileSize(sizeBytes);
        TypeText = "File";
        ModifiedTime = modifiedTime.ToString("yyyy-MM-dd HH:mm:ss");
        IsFolder = false;
        Icon = GetFileIcon(fullPath);
    }

    public FileItemViewModel(string fullPath, string name, DateTime modifiedTime, long? cachedTotalSizeBytes = null)
    {
        FullPath = fullPath;
        Name = name;
        SizeText = cachedTotalSizeBytes.HasValue ? FormatFileSize(cachedTotalSizeBytes.Value) : string.Empty;
        TypeText = "Folder";
        ModifiedTime = modifiedTime.ToString("yyyy-MM-dd HH:mm:ss");
        IsFolder = true;
        Icon = GetFolderIcon();
    }

    private static string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }

    private static BitmapSource? GetFileIcon(string filePath)
    {
        try
        {
            var icon = System.Drawing.Icon.ExtractAssociatedIcon(filePath);
            if (icon != null)
            {
                return ConvertIconToBitmapSource(icon);
            }
        }
        catch
        {
            // アイコン取得に失敗した場合はデフォルトアイコンを使用
        }
        return GetDefaultFileIcon();
    }

    private static BitmapSource? GetFolderIcon()
    {
        try
        {
            // Shell32.dllからフォルダアイコンを取得（インデックス3は標準フォルダアイコン）
            var icon = ExtractIconFromResource("shell32.dll", 3);
            if (icon != null)
            {
                return ConvertIconToBitmapSource(icon);
            }
        }
        catch
        {
            // フォルダアイコン取得に失敗した場合
        }
        return GetDefaultFileIcon();
    }

    private static System.Drawing.Icon? ExtractIconFromResource(string filePath, int index)
    {
        try
        {
            uint nIcons = 1;
            IntPtr[] largeIcons = new IntPtr[nIcons];
            IntPtr[] smallIcons = new IntPtr[nIcons];

            uint count = ExtractIconExW(filePath, index, largeIcons, smallIcons, nIcons);

            if (count > 0 && largeIcons[0] != IntPtr.Zero)
            {
                return System.Drawing.Icon.FromHandle(largeIcons[0]);
            }
        }
        catch
        {
        }
        return null;
    }

    [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    private static extern uint ExtractIconExW(string lpszFile, int nIconIndex, IntPtr[] phiconLarge, IntPtr[] phiconSmall, uint nIcons);

    private static BitmapSource? ConvertIconToBitmapSource(System.Drawing.Icon icon)
    {
        using (var bitmap = icon.ToBitmap())
        {
            IntPtr hBitmap = bitmap.GetHbitmap();
            try
            {
                return System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                    hBitmap,
                    IntPtr.Zero,
                    System.Windows.Int32Rect.Empty,
                    System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
            }
            finally
            {
                DeleteObject(hBitmap);
            }
        }
    }

    private static BitmapSource? GetDefaultFileIcon()
    {
        try
        {
            var icon = SystemIcons.Application;
            return ConvertIconToBitmapSource(icon);
        }
        catch
        {
            return null;
        }
    }

    [System.Runtime.InteropServices.DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteObject(IntPtr hObject);
}
