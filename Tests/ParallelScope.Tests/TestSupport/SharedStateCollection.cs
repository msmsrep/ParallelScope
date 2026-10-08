namespace ParallelScope.Tests.TestSupport;

/// <summary>
/// プロセス全体で1つの静的な状態に触れるテストクラスをまとめるコレクション（同一コレクション内は並列実行されない）。
/// 対象は2つ —— フォルダツリーの列挙条件（<see cref="ParallelScope.ViewModels.FolderItemViewModel.AttributesToSkip"/>。
/// <see cref="ParallelScope.ViewModels.MainWindowViewModel"/>の生成・設定変更のたびに書き換わる）と、
/// 表示言語（<see cref="ParallelScope.Utilities.AppLanguage"/>。ツリーの仮想ノードの表示名もこれで決まる）。
/// 別々のコレクションに分けると、言語を切り替えるテストとViewModelを生成するテストが並列に走って互いの値を壊すため、1つにまとめている。
/// </summary>
[CollectionDefinition(SharedStateCollection.Name)]
public class SharedStateCollection
{
    public const string Name = "SharedState";
}
