using ParallelScope.Data;
using ParallelScope.ViewModels;

namespace ParallelScope.Tests.TestSupport;

/// <summary>
/// <see cref="MainWindowViewModel"/>を生成するテストクラスの共通の土台。
/// キャッシュDBと settings.json を使い捨ての一時フォルダへ向け、後始末で接続プールを手放してから消す
/// （接続プールがDBファイルを掴んだままだと一時フォルダを消せない）。
/// 派生クラスにも <c>[Collection(SharedStateCollection.Name)]</c> を付けること（属性は継承に頼らず明示する）。
/// </summary>
public abstract class ShellTestBase : IDisposable
{
    private readonly List<TempDirectory> _tempDirectories = new();

    protected ShellTestBase()
    {
        var appData = NewTempDirectory();
        FileCacheRepository = new FileCacheRepository(appData.Path);
        SettingsRepository = new AppSettingsRepository(appData.Path);
    }

    protected FileCacheRepository FileCacheRepository { get; }

    protected AppSettingsRepository SettingsRepository { get; }

    /// <summary>ルートフォルダ等に使う実在の一時フォルダ。後始末で一緒に消す。</summary>
    protected TempDirectory NewTempDirectory()
    {
        var directory = new TempDirectory();
        _tempDirectories.Add(directory);
        return directory;
    }

    /// <summary>設定は生成時に読み込まれるため、初期状態は先に <see cref="AppSettingsRepository.Save"/> しておく。</summary>
    protected MainWindowViewModel CreateViewModel() => new(FileCacheRepository, SettingsRepository);

    public virtual void Dispose()
    {
        FileCacheRepository.ReleasePooledConnections();
        foreach (var directory in _tempDirectories)
        {
            directory.Dispose();
        }

        GC.SuppressFinalize(this);
    }
}
