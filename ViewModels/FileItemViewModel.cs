using CommunityToolkit.Mvvm.ComponentModel;
using ParallelScope.Utilities;
using System.Windows.Media;

namespace ParallelScope.ViewModels;

/// <summary>ファイル一覧グリッドに表示する1行分（ファイルまたはフォルダ）を表すViewModel。</summary>
public class FileItemViewModel : ObservableObject
{
    private string _fullPath = string.Empty;
    private string _name = string.Empty;
    private string _sizeText = string.Empty;
    private string _typeText = string.Empty;
    private string _modifiedTime = string.Empty;
    private bool _isFolder;
    private ImageSource? _iconSource;

    public string FullPath
    {
        get => _fullPath;
        set => SetProperty(ref _fullPath, value);
    }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public string SizeText
    {
        get => _sizeText;
        set => SetProperty(ref _sizeText, value);
    }

    public string TypeText
    {
        get => _typeText;
        set => SetProperty(ref _typeText, value);
    }

    public string ModifiedTime
    {
        get => _modifiedTime;
        set => SetProperty(ref _modifiedTime, value);
    }

    public bool IsFolder
    {
        get => _isFolder;
        set => SetProperty(ref _isFolder, value);
    }

    public ImageSource? IconSource
    {
        get => _iconSource;
        set => SetProperty(ref _iconSource, value);
    }

    /// <summary>
    /// フォルダサイズのバイト数（再描画判定用）。SizeText の値が同じでもバイト数で正確に比較。
    /// </summary>
    public long CachedSizeBytes { get; set; }

    /// <summary>ファイル用コンストラクタ。</summary>
    public FileItemViewModel(string fullPath, string name, long sizeBytes, DateTime modifiedTime)
    {
        FullPath = fullPath;
        Name = name;
        SizeText = FileSizeFormatter.Format(sizeBytes);
        TypeText = "File";
        ModifiedTime = modifiedTime.ToString("yyyy-MM-dd HH:mm:ss");
        IsFolder = false;
        IconSource = WindowsShellIconProvider.GetFileSmallIcon(fullPath);
        CachedSizeBytes = sizeBytes;
    }

    /// <summary>フォルダ用コンストラクタ。cachedTotalSizeBytes が無い場合はサイズ未取得として空表示にする。</summary>
    public FileItemViewModel(string fullPath, string name, DateTime modifiedTime, long? cachedTotalSizeBytes = null)
    {
        FullPath = fullPath;
        Name = name;
        SizeText = cachedTotalSizeBytes.HasValue ? FileSizeFormatter.Format(cachedTotalSizeBytes.Value) : string.Empty;
        TypeText = "Folder";
        ModifiedTime = modifiedTime.ToString("yyyy-MM-dd HH:mm:ss");
        IsFolder = true;
        IconSource = WindowsShellIconProvider.GetFolderSmallIcon();
        CachedSizeBytes = cachedTotalSizeBytes ?? 0;
    }
}
