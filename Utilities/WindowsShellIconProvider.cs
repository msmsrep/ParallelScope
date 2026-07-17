using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ParallelScope.Utilities;

/// <summary>Windowsシェル(SHGetFileInfo)からファイル/フォルダの小アイコンを取得し、拡張子単位でキャッシュするプロバイダ。</summary>
public static class WindowsShellIconProvider
{
    private const uint FileAttributeDirectory = 0x10;
    private const uint FileAttributeNormal = 0x80;

    private const uint ShgfiIcon = 0x000000100;
    private const uint ShgfiSmallIcon = 0x000000001;
    private const uint ShgfiUseFileAttributes = 0x000000010;

    private static readonly ConcurrentDictionary<string, ImageSource?> IconCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>フォルダ用の小アイコンを取得する（結果はキャッシュされる）。</summary>
    public static ImageSource? GetFolderSmallIcon()
    {
        return IconCache.GetOrAdd("__folder__", _ => GetSmallIconInternal("folder", true));
    }

    /// <summary>ファイルの拡張子に対応する小アイコンを取得する（拡張子単位でキャッシュされる）。</summary>
    public static ImageSource? GetFileSmallIcon(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            return null;
        }

        var extension = Path.GetExtension(fullPath);
        var cacheKey = string.IsNullOrWhiteSpace(extension)
            ? "__file_no_ext__"
            : extension;

        return IconCache.GetOrAdd(cacheKey, _ => GetSmallIconInternal(fullPath, false));
    }

    /// <summary>SHGetFileInfo を呼び出してHICONを取得し、ImageSourceに変換する（取得後はHICONを破棄する）。</summary>
    private static ImageSource? GetSmallIconInternal(string pathOrExtension, bool isFolder)
    {
        var attributes = isFolder ? FileAttributeDirectory : FileAttributeNormal;
        var flags = ShgfiIcon | ShgfiSmallIcon | ShgfiUseFileAttributes;

        var info = new Shfileinfo();
        var result = SHGetFileInfo(pathOrExtension, attributes, ref info, (uint)Marshal.SizeOf<Shfileinfo>(), flags);
        if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var image = Imaging.CreateBitmapSourceFromHIcon(
                info.hIcon,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());

            image.Freeze();
            return image;
        }
        finally
        {
            _ = DestroyIcon(info.hIcon);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(
        string pszPath,
        uint dwFileAttributes,
        ref Shfileinfo psfi,
        uint cbFileInfo,
        uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Shfileinfo
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }
}