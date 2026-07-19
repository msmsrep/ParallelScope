using CommunityToolkit.Mvvm.ComponentModel;
using ParallelScope.Utilities;
using System.IO;
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
    private string _location = string.Empty;
    private string _createdTime = string.Empty;
    private string _attributesText = string.Empty;
    private bool _isFolder;
    private ImageSource? _iconSource;
    private bool _iconInitialized;

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

    /// <summary>場所（親フォルダのパス）。検索結果・All Files表示でどこのファイルかを示す。</summary>
    public string Location
    {
        get => _location;
        set => SetProperty(ref _location, value);
    }

    /// <summary>作成日時の表示文字列。キャッシュに値が無い間（次のスキャンまで）は空。</summary>
    public string CreatedTime
    {
        get => _createdTime;
        set => SetProperty(ref _createdTime, value);
    }

    /// <summary>ファイル属性のエクスプローラー風表記（R=読み取り専用 H=隠し S=システム A=アーカイブ）。</summary>
    public string AttributesText
    {
        get => _attributesText;
        set => SetProperty(ref _attributesText, value);
    }

    public bool IsFolder
    {
        get => _isFolder;
        set => SetProperty(ref _isFolder, value);
    }

    public ImageSource? IconSource
    {
        get
        {
            if (!_iconInitialized)
            {
                // 一覧生成時に全件分のアイコンを先読みすると表示開始が遅くなるため、
                // 表示時（バインディング評価時）に必要な分だけ解決する。
                _iconSource = IsFolder
                    ? WindowsShellIconProvider.GetFolderSmallIcon()
                    : WindowsShellIconProvider.GetFileSmallIcon(FullPath);
                _iconInitialized = true;
            }

            return _iconSource;
        }
        set
        {
            _iconInitialized = true;
            SetProperty(ref _iconSource, value);
        }
    }

    /// <summary>
    /// フォルダサイズのバイト数（再描画判定用）。SizeText の値が同じでもバイト数で正確に比較。
    /// </summary>
    public long CachedSizeBytes { get; set; }

    /// <summary>ファイル用コンストラクタ。location を渡すとその文字列インスタンスを共有し、パス文字列の再生成を避ける。</summary>
    public FileItemViewModel(string fullPath, string name, long sizeBytes, DateTime modifiedTime, string? location = null)
    {
        FullPath = fullPath;
        Name = name;
        SizeText = FileSizeFormatter.Format(sizeBytes);
        // 種類名は拡張子単位でキャッシュされるため、2回目以降は辞書引きのみで実質コストゼロ
        TypeText = WindowsShellIconProvider.GetFileTypeName(fullPath);
        ModifiedTime = modifiedTime.ToString("yyyy-MM-dd HH:mm:ss");
        Location = location ?? GetLocation(fullPath);
        IsFolder = false;
        CachedSizeBytes = sizeBytes;
    }

    /// <summary>フォルダ用コンストラクタ。cachedTotalSizeBytes が無い場合はサイズ未取得として空表示にする。</summary>
    public FileItemViewModel(string fullPath, string name, DateTime modifiedTime, long? cachedTotalSizeBytes = null, string? location = null)
    {
        FullPath = fullPath;
        Name = name;
        SizeText = cachedTotalSizeBytes.HasValue ? FileSizeFormatter.Format(cachedTotalSizeBytes.Value) : string.Empty;
        TypeText = WindowsShellIconProvider.GetFolderTypeName();
        ModifiedTime = modifiedTime.ToString("yyyy-MM-dd HH:mm:ss");
        Location = location ?? GetLocation(fullPath);
        IsFolder = true;
        CachedSizeBytes = cachedTotalSizeBytes ?? 0;
    }

    /// <summary>親フォルダのパスを求める（純粋な文字列操作でファイルシステムへはアクセスしない）。</summary>
    private static string GetLocation(string fullPath)
    {
        try
        {
            return Path.GetDirectoryName(fullPath) ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}
