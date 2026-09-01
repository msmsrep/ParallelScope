namespace ParallelScope.Tests.TestSupport;

/// <summary>
/// <see cref="ParallelScope.ViewModels.MainWindowViewModel"/> を生成するテストクラスをまとめるコレクション
/// （同一コレクション内は並列実行されない）。
/// フォルダツリーの列挙条件（<see cref="ParallelScope.ViewModels.FolderItemViewModel.AttributesToSkip"/>）は
/// アプリ全体で1つの静的な値で、ViewModelの生成・設定変更のたびに書き換わるため、並列に走らせると互いの値を壊す。
/// </summary>
[CollectionDefinition(FolderTreeCollection.Name)]
public class FolderTreeCollection
{
    public const string Name = "FolderTree";
}
