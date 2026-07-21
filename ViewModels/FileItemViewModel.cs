using CommunityToolkit.Mvvm.ComponentModel;
using ParallelScope.Utilities;
using System.IO;
using System.Windows.Media;

namespace ParallelScope.ViewModels;

/// <summary>
/// ファイル一覧グリッドに表示する1行分（ファイルまたはフォルダ）を表すViewModel。
/// All Filesモード・検索では数十万インスタンスが保持され続けるため、表示用文字列
/// （FullPath・サイズ・日時）は1件ごとに保持せず、生値からバインディング評価時に生成する
/// （DataGridの行仮想化により、生成が走るのは可視行の分のみ）。
/// Location / TypeText / AttributesText は共有インスタンス（プール済み文字列）への参照なので保持コストは参照分のみ。
/// </summary>
public class FileItemViewModel : ObservableObject
{
    private string _name = string.Empty;
    private string _location = string.Empty;
    private string _typeText = string.Empty;
    private string _attributesText = string.Empty;
    private long? _sizeBytes;
    private DateTime _modifiedAt;
    private DateTime? _createdAt;
    private bool _isFolder;
    private ImageSource? _iconSource;
    private bool _iconInitialized;

    /// <summary>フルパス。Location と Name から都度組み立てる（数十万件分のパス文字列を保持しないため）。</summary>
    public string FullPath => Path.Join(_location, _name);

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    /// <summary>場所（親フォルダのパス）。検索結果・All Files表示でどこのファイルかを示す。</summary>
    public string Location
    {
        get => _location;
        set => SetProperty(ref _location, value);
    }

    public string TypeText
    {
        get => _typeText;
        set => SetProperty(ref _typeText, value);
    }

    /// <summary>ファイル属性のエクスプローラー風表記（R=読み取り専用 H=隠し S=システム A=アーカイブ）。</summary>
    public string AttributesText
    {
        get => _attributesText;
        set => SetProperty(ref _attributesText, value);
    }

    /// <summary>サイズのバイト数。null はサイズ未取得（キャッシュ集計が無いフォルダ等）で、表示は空になる。</summary>
    public long? SizeBytes
    {
        get => _sizeBytes;
        set
        {
            if (SetProperty(ref _sizeBytes, value))
            {
                OnPropertyChanged(nameof(SizeText));
            }
        }
    }

    /// <summary>サイズの表示文字列（表示時に生成）。</summary>
    public string SizeText => _sizeBytes is { } bytes ? FileSizeFormatter.Format(bytes) : string.Empty;

    /// <summary>更新日時（ローカル時刻）。MinValue は「表示しない」（仮想「Folders」のルート行等）。</summary>
    public DateTime ModifiedAt
    {
        get => _modifiedAt;
        set
        {
            if (SetProperty(ref _modifiedAt, value))
            {
                OnPropertyChanged(nameof(ModifiedTime));
            }
        }
    }

    /// <summary>更新日時の表示文字列（表示時に生成）。</summary>
    public string ModifiedTime => _modifiedAt == DateTime.MinValue ? string.Empty : _modifiedAt.ToString("yyyy-MM-dd HH:mm:ss");

    /// <summary>作成日時（ローカル時刻）。キャッシュに値が無い間（次のスキャンまで）は null で、表示は空になる。</summary>
    public DateTime? CreatedAt
    {
        get => _createdAt;
        set
        {
            if (SetProperty(ref _createdAt, value))
            {
                OnPropertyChanged(nameof(CreatedTime));
            }
        }
    }

    /// <summary>作成日時の表示文字列（表示時に生成）。</summary>
    public string CreatedTime => _createdAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty;

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
                // アイコンは拡張子で決まるため、フルパスを組み立てず Name を渡す
                _iconSource = IsFolder
                    ? WindowsShellIconProvider.GetFolderSmallIcon()
                    : WindowsShellIconProvider.GetFileSmallIcon(Name);
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

    /// <summary>ファイル用コンストラクタ。location を渡すとその文字列インスタンスを共有し、パス文字列の再生成を避ける。</summary>
    public FileItemViewModel(string fullPath, string name, long sizeBytes, DateTime modifiedTime, string? location = null)
    {
        _name = name;
        _location = location ?? GetLocation(fullPath);
        _sizeBytes = sizeBytes;
        // 種類名は拡張子単位でキャッシュされるため、2回目以降は辞書引きのみで実質コストゼロ
        _typeText = WindowsShellIconProvider.GetFileTypeName(name);
        _modifiedAt = modifiedTime;
        _isFolder = false;
    }

    /// <summary>フォルダ用コンストラクタ。cachedTotalSizeBytes が無い場合はサイズ未取得として空表示にする。</summary>
    public FileItemViewModel(string fullPath, string name, DateTime modifiedTime, long? cachedTotalSizeBytes = null, string? location = null)
    {
        _name = name;
        _location = location ?? GetLocation(fullPath);
        _sizeBytes = cachedTotalSizeBytes;
        _typeText = WindowsShellIconProvider.GetFolderTypeName();
        _modifiedAt = modifiedTime;
        _isFolder = true;
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
