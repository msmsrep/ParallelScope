using System.IO;

namespace ParallelScope.Tests.TestSupport;

/// <summary>
/// テスト1件ごとに使い捨てる一時フォルダ。
/// 実際のアプリデータフォルダ（%LOCALAPPDATA%\ParallelScope）を汚さないため、
/// リポジトリ類にはこのフォルダを渡す。
/// </summary>
internal sealed class TempDirectory : IDisposable
{
    public string Path { get; }

    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ParallelScope.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Combine(string fileName) => System.IO.Path.Combine(Path, fileName);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // SQLiteの接続プールがまだファイルを掴んでいる場合など、後始末の失敗はテスト結果に影響させない
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
