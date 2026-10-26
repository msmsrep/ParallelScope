# 依存パッケージのライセンスと対応

本体は AGPL-3.0（`LICENSE`）。このメモは、配布物（MSIX / 発行フォルダ）に同梱しているサードパーティ製コンポーネントのライセンスと、そのために何をしているかをまとめたもの。

## 同梱しているもの

自己完結（`SelfContained=true`）で発行しているため、NuGetパッケージに加えて .NET ランタイム・WPF 本体も配布物に入る。

| コンポーネント | バージョン | ライセンス | 条件 |
| --- | --- | --- | --- |
| .NET Runtime / Windows Desktop (WPF) Runtime | 10.0 | MIT | 著作権表示と許諾文の同梱 |
| ↳ .NET Runtime が内包する第三者コンポーネント（zlib 等） | — | 各種（MIT/BSD/zlib 等） | 同上（`THIRD-PARTY-NOTICES.TXT` の同梱） |
| CommunityToolkit.Mvvm | 8.4.2 | MIT | 同上 |
| Microsoft.EntityFrameworkCore（.Abstractions / .Relational / .Sqlite）, Microsoft.Data.Sqlite | 10.0.9 | MIT | 同上 |
| Microsoft.Extensions.*（EF Core の推移的依存） | 10.0.9 | MIT | 同上 |
| WinRT.Runtime（C#/WinRT） | — | MIT | 同上 |
| SQLitePCLRaw（core / batteries_v2 / provider.e_sqlite3） | 2.1.11 | Apache-2.0 | ライセンス本文の同梱。NOTICE ファイルは無し |
| SQLitePCLRaw.lib.e_sqlite3（ネイティブ版のパッケージ） | 3.50.3 | Apache-2.0（nuspec に記載なし。README とリポジトリで確認） | 同上 |
| SQLite（`e_sqlite3.dll` の中身） | 3.50.3 | パブリックドメイン | なし |
| Microsoft.Windows.SDK.NET.dll（Windows SDK の .NET 投影） | 10.0.19041 | Microsoft Windows SDK ライセンス | アプリと一緒の再配布は許可されている |

同梱しないもの（開発時のみ）: `Microsoft.EntityFrameworkCore.Design` / `.Tools` とその依存（Roslyn、Humanizer、Newtonsoft.Json、Mono.TextTemplating 等）、テストプロジェクトのパッケージ（xUnit 等）。前者は `PrivateAssets=all` で、Debug の出力フォルダには出るが発行物には入らない（発行フォルダで確認済み）。

## 対応していること

- **ライセンス表記を配布物に入れる。** MIT・Apache-2.0 はどちらも「配布物に著作権表示とライセンス本文を含めること」が条件なので、次のファイルを `ParallelScope.csproj` で出力フォルダへコピーしている（発行フォルダ経由で MSIX にも入る）。
  - `LICENSE` — 本体の AGPL-3.0 本文
  - `THIRD-PARTY-NOTICES.txt` — 同梱コンポーネントの一覧と、どのライセンス本文に対応するか
  - `licenses/MIT-dotnet-foundation.txt` — MIT 本文（.NET Foundation）
  - `licenses/Apache-2.0.txt` — Apache-2.0 本文
  - `licenses/dotnet-runtime-THIRD-PARTY-NOTICES.txt` — .NET ランタイムが内包する第三者コンポーネントの表記（`microsoft.netcore.app.runtime.win-x64` パッケージに入っているものをそのまま写した）
- **README（英日）に「ライセンス」節を置き、上記ファイルへリンク。**

## AGPL-3.0 との両立

- MIT・Apache-2.0・パブリックドメインはいずれも AGPL-3.0（GPLv3 系）と両立する（Apache-2.0 は GPLv2 とは非両立だが v3 とは両立）。
- Windows SDK の投影 DLL はプロプライエタリだが、OS の API への窓口であり GPLv3 の「システムライブラリ」の例外に当たるものとして扱う（Windows 向け GPL アプリの一般的な扱い）。

## 本体を AGPL にしたことで気をつけること

- **ストア配布と対応ソース。** バイナリを配る以上、対応するソースを入手できるようにしておく必要がある（AGPL 第6条）。GitHub の公開リポジトリがその提供先。ストアの説明文かアプリ内（設定画面など）にリポジトリへの導線を置いておくとより確実。リリースごとにタグを打っておくと「どのソースが対応するか」が明確になる。
- **作者自身はライセンスに縛られない。** 著作権者は自分なので、ストアの規約や課金の仕組みと AGPL が食い違っても、自分が配る分には問題にならない。
- **外部からのコントリビューション。** 他人のコードを取り込むと、その部分の著作権はその人に残る。将来ライセンスを変えたい・別条件でも配りたい可能性があるなら、取り込む前に CLA（またはそれに類する同意）を取っておく。
- **フォークによる再配布。** AGPL ではソース公開を条件に、第三者が Plus のロックを外したビルドを配ることも許される（禁止できない）。ストアのアプリ名・ロゴ（商標的なもの）の使用はライセンスとは別の話なので、必要なら README に「名前とロゴは再配布物に使わないでほしい」と書き添える。

## 更新時の手順

依存パッケージを足す・上げる・ .NET のバージョンを上げるときは次を見直す。

1. `dotnet list ParallelScope.csproj package --include-transitive` と、発行フォルダ（`bin/Release/.../publish`）の DLL を突き合わせ、新しく同梱されるものを洗い出す。
2. 各パッケージの nuspec（`~/.nuget/packages/<id>/<ver>/*.nuspec` の `<license>`）でライセンスを確認し、`THIRD-PARTY-NOTICES.txt` と上の表を更新する。MIT・Apache-2.0 以外のものが入る場合は、本文を `licenses/` に足す。GPLv3 と両立しないもの（GPLv2-only など）は入れない。
3. .NET のバージョンを上げたら、`licenses/dotnet-runtime-THIRD-PARTY-NOTICES.txt` を新しいランタイムパッケージのものに差し替える。
