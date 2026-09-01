using System.Runtime.CompilerServices;
using System.Windows;

// 単体テストからテスト用コンストラクタ（保存先を差し替えたリポジトリを渡す形）を使うために公開する
[assembly: InternalsVisibleTo("ParallelScope.Tests")]

[assembly:ThemeInfo(
    ResourceDictionaryLocation.None,            //where theme specific resource dictionaries are located
                                                //(used if a resource is not found in the page,
                                                // or application resource dictionaries)
    ResourceDictionaryLocation.SourceAssembly   //where the generic resource dictionary is located
                                                //(used if a resource is not found in the page,
                                                // app, or any theme specific resource dictionaries)
)]
