# CLAUDE.md

このファイルは、このリポジトリで作業する Claude Code (claude.ai/code) に向けたガイドです。

## プロジェクト概要

ParallelScope は、複数のルートフォルダを登録して横断的にブラウズ・検索できる Windows デスクトップアプリ（WPF, .NET, `net10.0-windows`）です。ローカルの SQLite キャッシュがフォルダツリー・ファイル一覧を裏で支えており、高速な表示を実現しつつ、バックグラウンドでファイルシステムを再スキャンしてキャッシュを最新に保ちます。

## コマンド

```powershell
dotnet restore
dotnet build ParallelScope.csproj
dotnet run --project ParallelScope.csproj
dotnet publish -c Release
```

- 自動テストプロジェクトはこのリポジトリに存在しません。変更の検証は `dotnet build`（0警告・0エラーであることを確認）で行い、UIや操作に関わる変更については実際にビルドしたexeを起動して機能を触って確認してください（WPFアプリのためブラウザベースのプレビューは使えません）。ビルドしたexeは `bin/Debug/net10.0-windows/win-x64/ParallelScope.exe` にあります（本格的なUI自動化なしで手早く確認するなら、PowerShellの `Start-Process` / `Get-Process ... | Select MainWindowTitle` が便利です）。
- EF Core のマイグレーションは `dotnet-tools.json` で宣言されたローカルツール（`dotnet-ef` 10.0.9）を使います。
  ```powershell
  dotnet tool restore
  dotnet dotnet-ef migrations add <MigrationName>
  dotnet dotnet-ef database update
  ```

## アーキテクチャ

- **MVVM構成・DIコンテナ不使用。** `MainWindow`/`SettingsWindow` は素のWPFコードビハインドで、`MainWindowViewModel` やリポジトリはコンストラクタ内で直接 `new` して生成しています（`MainWindowViewModel()` のコンストラクタ参照）。DIフレームワークを導入せず、このパターンを踏襲してください。
- **`MainWindowViewModel` は責務ごとにpartialクラスへ分割**されています — `MainWindowViewModel.cs`（フィールド/プロパティ/コンストラクタ/コアレサー）、`.Settings.cs`、`.Navigation.cs`、`.Search.cs`、`.Scanning.cs`、`.Cache.cs`、`.FileItems.cs`、`.FlatView.cs`。実体は1つのクラス・1つの公開APIなので、挙動を追う際はベースファイルだけでなく `ViewModels/MainWindowViewModel.*.cs` を横断してgrepしてください。
- **ファイル一覧表示は2段構え。** フォルダを開くと、まずSQLiteキャッシュ（`FileCacheRepository.GetEntriesByParentPath`、`LoadFromCacheAsync` 経由）から即座に表示し、その後バックグラウンドでファイルシステムを読み直して一覧とキャッシュを置き換え（`RefreshFromFileSystemInBackground`）、続けてキャッシュ済みの子フォルダ合計サイズを反映するパス（`ApplyCachedFolderSizesInBackground`）が走ります。この3つの処理はすべて `Utilities/SingleFlightCoalescer<TRequest>` を介して統合されており（`_refreshCoalescer` / `_searchCoalescer` / `_folderSizeCoalescer` のそれぞれが専用インスタンスを持つ）、実行中に連続でリクエストが来ても最新の1件だけに統合されます。新たに `_isRunning`/`_pending*` のようなフラグの組を自前実装せず、このクラスを再利用してください。
- **検索はインクリメンタルサーチで、ボタン/Enterキー起動ではありません。** 検索用の `TextBox` は `UpdateSourceTrigger=PropertyChanged` で `SearchQuery` にバインドされており、そのプロパティセッター（`MainWindowViewModel.cs`）が入力の都度 `RequestSearch` を呼び、`_searchCoalescer` を経由します。検索完了前に表示中の一覧をクリアしない（入力の都度ちらつかせないため）設計になっており、結果は `ReplaceVisibleFileItems` の追加/削除/更新の差分適用で反映されます。専用の「Clear」ボタンは無く、検索欄を空にすると `SearchQuery` セッターが非公開の `ClearSearch()` を呼んで元の表示に戻します。
- **ファイル一覧の表示モードは「検索中」「All Filesモード（フラット表示）」「通常（直下一覧）」の3状態**で、優先順位は検索 > フラット > 通常です。「All Files」トグル（`IsFlatFileViewEnabled`、`MainWindowViewModel.FlatView.cs`）をONにすると、検索と同様にキャッシュ経由のみで（`FileCacheRepository.GetFilesUnderPath`、ライブのファイルシステム走査なし）現在フォルダ配下の全ファイルを再帰的に取得し、専用の `_flatViewCoalescer` で連続リクエストを統合します。`SearchQuery` セッター・`ClearSearch()`・`IsFlatFileViewEnabled` セッター・`LoadFilesInternal` はいずれもこの優先順位を踏まえて表示を切り替えます。バックグラウンドの直下一覧更新（`UpdateCurrentDirectoryItems`）は検索中・フラット表示中は表示中の一覧を上書きせず、`_currentDirectoryItems` だけを裏で更新し続けるので、モードをOFFに戻すと即座に最新の直下一覧が表示されます。
- **フルスキャン・フォルダ単位スキャン**はファイルシステムをウェーブ（複数ディレクトリの束）単位で並列列挙しながら走査し（`MainWindowViewModel.Scanning.cs`。NAS等の高レイテンシなパスで列挙の往復時間が直列に積み上がるのを防ぐため）、100フォルダごとにSQLiteへバッチ書き込みします（`FileCacheRepository.BatchReplaceEntriesByParentPaths`）。書き込みは差分方式で、バッチ内の各親パスをキャッシュ済み内容と比較し、変化があった親パスだけをDELETE+INSERTします（戻り値は実際に書き換えた親パス数）。スキャンが完走した場合は、訪問しなかった親パスの残骸行の削除（`FileCacheRepository.DeleteStaleEntries`）とWALファイルの切り詰め（`TruncateWal`）が続けて実行されます。キャンセルは協調的に行われます。スキャンループでは `Task.Run` のデリゲート内で `ThrowIfCancellationRequested()` を呼ばず、`token.IsCancellationRequested` をチェックして早期returnします。実際の `OperationCanceledException` は `Task.Run` 完了後に `ScanFolderSubtreesAsync` 側で投げられ、`MainWindow.xaml.cs` の `RunFullScanAsync` で捕捉されます（`Task.Run` のデリゲート内で例外を投げると、実際には正しく捕捉されているにもかかわらずVisual Studioのデバッガが「ユーザーコードで未処理の例外」として誤検知して停止することがあるため、例外の送出位置をバックグラウンドデリゲートの外に置いています）。
- **フルスキャン実行中に再度フルスキャンを要求した場合**（例: 設定画面を開き直して「Save and Full Scan」を再度押す）、`_restartFullScanRequested` をセットし `_fullScanCts` をキャンセルします。キャンセルされた実行が終了すると、`RunFullScanAsync` の `finally` ブロックが自動的にスキャンを再実行します。
- **`Utilities/` の共通ユーティリティ** — インラインで再実装せず、これらを再利用してください。
  - `PathNormalizer`: パスの正規化・比較・祖先判定（以前はViewModel・コードビハインド・リポジトリの間で重複実装されていました）。
  - `AppDataPathProvider`: アプリデータフォルダの解決。`Environment.ProcessPath` に `\WindowsApps\` が含まれるかで `%LOCALAPPDATA%\ParallelScope` とMSIXの `WindowsApps\...\LocalState` パスを切り替えます。
  - `FileSizeFormatter`: バイト数を `"12.3 MB"` のような表示用文字列に変換します。
  - `AppVersionProvider`: `AppxManifest.xml`（csprojの設定によりexeと同じフォルダにコピーされる）の `Identity/@Version` を読み取り、ウィンドウタイトルにバージョンを表示するために使います。
- **データ層**（`Data/`）: `ParallelScopeDbContext`（EF Core + SQLite、WALジャーナルモード、PRAGMA設定は `FileCacheRepository` のコンストラクタで調整）がファイル一覧キャッシュ（`FileSystemEntryEntity`、テーブル `FileSystemEntries`）を支えます。`AppSettingsRepository` は `AppSettings`（ルートパス・除外パス・フルスキャン間隔時間・All Filesモードの有効状態など）を `settings.json` として永続化します。どちらも保存先フォルダは `AppDataPathProvider` 経由で解決します。永続化したい状態を増やす場合は `AppSettings` にプロパティを足した上で、`MainWindowViewModel.Settings.cs` の `SaveSettings(rootPaths)` ヘルパー（現在の全設定値から `AppSettings` を組み立てて保存する一箇所）に反映し、変更のたびに呼び出すようにしてください（`IsFlatFileViewEnabled` セッターがその実装例です）。
- **MSIXパッケージング。** リポジトリルートの `AppxManifest.xml`（`Identity/Name = msmsrep.ParallelScope`）は、MSIXパッケージングと、実行時のバージョン取得元（`AppVersionProvider`）の両方に使われます。アプリは非パッケージ実行（開発時/F5）とパッケージ実行の両方を想定しています。ビルド済みの `.msix` はバージョンごとに `MSIX/verX.Y.Z.W/` 配下にアーカイブされます（`.gitignore` 対象で、ビルド成果物には含まれません）。

## 規約

- gitのコミットメッセージは日本語・簡潔・命令形です（`git log` 参照）。
- コードコメントは日本語で簡潔に、「何をしているか」ではなく「なぜそうしているか」（非自明な制約・回避策・スレッド/キャンセル周りの落とし穴など）を書きます。
- `memo.md`（`.gitignore` 対象）には開発者の非公式な日本語TODO・ロードマップメモが書かれています。今後の予定を把握する参考にはなりますが、正式な仕様書として扱わないでください。
