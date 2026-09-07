# テスト

ParallelScope の単体テスト（xUnit）です。実体は `ParallelScope.Tests/` にあります。

## 実行方法

リポジトリのルートから:

```powershell
dotnet test Tests/ParallelScope.Tests/ParallelScope.Tests.csproj
```

テストプロジェクトのフォルダ内であれば `dotnet test` だけで動きます。

よく使うオプション:

```powershell
# クラス名・メソッド名で絞り込む
dotnet test Tests/ParallelScope.Tests/ParallelScope.Tests.csproj --filter "FullyQualifiedName~PathNormalizerTests"

# 1件ずつ結果を表示する
dotnet test Tests/ParallelScope.Tests/ParallelScope.Tests.csproj --logger "console;verbosity=detailed"
```

Visual Studio の場合は、`ParallelScope.Tests.csproj` を開けばテストエクスプローラーに一覧が出ます（ソリューションファイルは無いので、本体と一緒に扱いたい場合は各csprojを追加してください）。

全テストは1〜2秒で完走します。テストが読み書きするのは使い捨ての一時フォルダだけで、実際のアプリデータ（`%LOCALAPPDATA%\ParallelScope`）には触れません（後述）。

## テストの範囲

対象は**UIに依存しない層**だけです。WPFのウィンドウ（`MainWindow` / `SettingsWindow`）のコードビハインドは対象外なので、そこに関わる変更は従来どおりビルドしたexeを起動して手で確認してください。`MainWindowViewModel` は、保存先を差し替えたリポジトリを渡すテスト用コンストラクタ（後述）を使って一部だけテストしています。

| テストクラス | 対象 | 主に守っていること |
| --- | --- | --- |
| `Utilities/PathNormalizerTests` | `PathNormalizer` | 末尾区切りの除去、ドライブルート（`C:\`）は区切りを残す、仮想パスの正規形、`C:\Temp` が `C:\Temporary` の祖先と誤判定されないこと |
| `Utilities/FileSizeFormatterTests` | `FileSizeFormatter` | 単位の繰り上げ、小数第2位への丸め、TBで打ち止め |
| `Utilities/VirtualFoldersTests` | `VirtualFolders` | 仮想ノードの種類判定（大文字小文字を無視）、正規形、表示名（対訳表キーと、言語に追従した文字列）、実在パスと衝突しない文字を含むこと |
| `Utilities/AppThemeTests` | `AppTheme.Parse` | 未設定・不正値をOS追従（System）へ丸めること。`Apply` は `Application` が要るため対象外 |
| `Utilities/LocalizationTests` | `UiText` / `UiTextResources` / `AppLanguage` | 英日でキーとプレースホルダーが揃っていること、空文字の文言が無いこと、現在の言語での引き当てと未知キーのフォールバック、言語切り替え時のインデクサー変更通知、`Parse` の丸めと `CurrentUICulture` の切り替え |
| `Utilities/TreeNodesTests` | `TreeNodes` | ノードキー定義の整合性（`AllNodes` = `OptionalNodes` + Folders、重複なし）、キーと `VirtualFolderKind` の相互変換、未購読時は1つも表示しないこと。キーは settings.json に保存されるため崩すと既存設定が壊れる |
| `Utilities/FileListColumnsTests` | `FileListColumns` | 列キー定義の整合性（`AllColumns` = Name + `OptionalColumns`、重複なし）。キーは settings.json に保存されるため崩すと既存設定が壊れる |
| `Utilities/SingleFlightCoalescerTests` | `SingleFlightCoalescer<T>` | 実行中のリクエストが最新1件へ統合されること、ハンドラが直列に走ること、ハンドラが例外を投げた後も後続を処理できること |
| `Utilities/BackgroundWorkGateTests` | `BackgroundWorkGate` | 処理の戻り値を返すこと、同時に走る本数が上限を超えないこと、処理が例外を投げても枠が返ること |
| `Utilities/FileListCsvExporterTests` | `FileListCsvExporter` | 選択列どおりの見出し・行、生バイト出力時の `Size (bytes)` 見出し、RFC 4180のエスケープ（必要な場合だけ引用符で囲む）、UTF-8 BOM、キャンセル |
| `ViewModels/FileItemViewModelTests` | `FileItemViewModel` | 表示用文字列（`SizeText` / `ModifiedTime` / `CreatedTime` / `FullPath`）の生成規則と、生値変更時の `PropertyChanged` 通知 |
| `Data/FileCacheRepositoryTests` | `FileCacheRepository` | 一覧の並び順（フォルダ先・名前昇順）、全列のラウンドトリップ、差分書き込みの戻り値、配下ファイルの再帰列挙、検索のLIKEエスケープ、子フォルダ合計サイズ、`DeleteStaleEntries` の各分岐 |
| `Data/FileNameIndexTests` | `FileNameIndex` | 索引での検索がキャッシュDBへの検索と同じ内容・同じ並びになること（部分一致・正規表現・範囲の絞り込み）、組み立て後に変わったフォルダの引き直し、上限超過での破棄、見積もりより長い名前（サロゲートペア）でも取りこぼさないこと |
| `Data/AppSettingsRepositoryTests` | `AppSettingsRepository` | 全設定のラウンドトリップ、ファイル未作成・破損JSON・旧形式（プロパティ欠落）でのフォールバック、遅延書き出し（保存直後でも別インスタンスから読めること、`Flush` でファイルへ書き切ること、保存が無ければファイルを作らないこと） |
| `Services/StoreLicenseServiceTests` | `StoreLicenseService` | ライセンス未取得の間は未購読扱いであること、誤った開発者キーで解放されないこと、`RefreshLicenseAsync` が例外を出さないこと |
| `ViewModels/LanguageSettingTests` | 表示言語の設定 | 既定がOS追従であること、`ApplyLanguage` の即時適用・保存、仮想ノードの表示名の追従、起動時の `ApplySavedLanguage` での復元 |
| `ViewModels/BrowserTabTests` | 1ペイン内のタブ操作 | 追加・複製・クローズ・切り替え・並べ替え・開き直しと、非表示タブが一覧を手放す基準がタブ数で絞られること |
| `ViewModels/PaneRestoreTests` | タブ構成・分割状態の保存と復元 | 並び順・表示中のタブ・All Filesモード・2画面構成の復元、消えたフォルダのタブがルートへ寄ること、表示するタブ以外は初回表示まで読み込まないこと、未購読時は復元せず保存済みの構成も消さないこと |
| `ViewModels/TreeNodeSettingsTests` | ツリー最上位ノードの表示/非表示・並び順 | 既定値、非表示にしたノードがツリーから消えること、Foldersは全て隠しても残ること、並び替えの反映、表示中のノードを隠したときの退避、保存と復元、旧設定・未知キーの補完 |
| `Utilities/HiddenItemVisibilityTests` | `HiddenItemVisibility` | 属性で一覧から外れること、片方だけ許可しても両方の属性を持つ項目は出さないこと、属性未取得（旧キャッシュ行）は出すこと、常にReparsePointを飛ばすこと |
| `ViewModels/HiddenItemSettingsTests` | 隠し・システム属性の表示設定 | 既定が「両方とも表示」であること（更新前と同じ見え方）、保存と復元、購読状態で表示条件が動かないこと、ツリーの列挙条件（`FolderItemViewModel.AttributesToSkip`）への反映 |
| `ViewModels/PlusFeatureGatingTests` | 無料版とPlusの機能分け | お気に入り・最近・よく使うノードのツリーへの出し入れ、非表示ノードからの退避、同じアクセス実績から「最近」（最終アクセス順）と「よく使う」（回数順）が別々に並ぶこと、購読が切れても保存済みデータを消さないこと、列カスタマイズの既定列へのフォールバック |

## テストを書くときの決まりごと

### `MainWindowViewModel` を生成するテストは直列に走らせる

フォルダツリーの列挙条件（`FolderItemViewModel.AttributesToSkip`）はプロセス全体で1つの静的な値で、ViewModelの生成・設定変更のたびに書き換わります。生成するテストクラスには `[Collection(FolderTreeCollection.Name)]` を付けてください（同一コレクション内は並列実行されません）。

### 表示言語を切り替えるテストは直列に走らせる

`AppLanguage` はプロセス全体で1つの静的な状態なので、切り替えるテストクラスには `[Collection(LanguageCollection.Name)]` を付けて（同一コレクション内は並列実行されません）、切り替えは `TestSupport/LanguageScope` で囲んで元の言語へ戻してください。

### 実際のアプリデータを絶対に触らない

`FileCacheRepository` と `AppSettingsRepository` は、省略可能な引数で保存先フォルダを差し替えられます（省略時＝アプリ本体の実行時は `AppDataPathProvider` のフォルダ）。テストからは必ず `TestSupport/TempDirectory` を渡してください。渡し忘れると `%LOCALAPPDATA%\ParallelScope` の実キャッシュDBや `settings.json` を書き換えてしまいます。

```csharp
using var temp = new TempDirectory();
var repository = new FileCacheRepository(temp.Path);
```

`FileCacheRepository` を使ったら、後始末で `ReleasePooledConnections()` を呼んでから一時フォルダを消します（SQLiteの接続プールがDBファイルを掴んだままだと削除に失敗する）。`FileCacheRepositoryTests` の `Dispose` がその形です。

### パスは実在しなくてよい

キャッシュ層は保存されたパス文字列を扱うだけでファイルシステムへは行かないので、`C:\Root\Sub\a.txt` のような架空のパスでテストできます。実フォルダが要るのは、ファイルを書き出す `FileListCsvExporter` と、実在確認を伴うフォルダ移動（`MainWindowViewModel.LoadFiles`。存在しないパスは移動が失敗して `CurrentPath` が変わらない）だけです。

### `MainWindowViewModel` をテストするとき

`MainWindowViewModel` には、リポジトリを外から渡す `internal` のコンストラクタがあります（アプリ本体が使う引数なしのコンストラクタは、従来どおり中でリポジトリを直接newします）。テストアセンブリには `AssemblyInfo.cs` の `InternalsVisibleTo` で公開しています。

```csharp
var settingsRepository = new AppSettingsRepository(temp.Path);
settingsRepository.Save(new AppSettings { /* テストしたい初期状態 */ });
var viewModel = new MainWindowViewModel(new FileCacheRepository(temp.Path), settingsRepository);
```

設定は**コンストラクタで読み込まれる**ため、初期状態は生成前に `Save` しておきます。生成時にルートパスが空だと全ドライブを列挙するフォールバックが走るので、`RootPaths` は必ず入れてください。`Dispatcher` は不要ですが（`SynchronizationContext` が無ければ既定のものを使う）、UIスレッド前提のコード（`MainWindow.xaml.cs` 側）はテストできません。

### カルチャ依存の表示を含むテストはカルチャを固定する

サイズ表記の小数点記号は環境のカルチャで変わります（`1.5 KB` / `1,5 KB`）。期待値を文字列で書くテストクラスでは、コンストラクタで `CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;` を設定しています。

### 非同期の完了待ちは `TaskCompletionSource` で

`SingleFlightCoalescer` のように「投げっぱなし（fire-and-forget）」で走る処理は、`Task.Delay` で待つとマシンの負荷次第で不安定になります。ハンドラ側から `TaskCompletionSource` に完了を通知し、`await tcs.Task.WaitAsync(timeout)` で待ってください。

### テストプロジェクトは本体のビルド対象外

本体の `ParallelScope.csproj` は配下の `.cs` を再帰的に拾うため、`Tests\**` を `DefaultItemExcludes` で除外しています。テスト用のフォルダを `Tests/` の外に作ると本体のビルドが壊れるので、テストコードは必ず `Tests/` 配下に置いてください。

### 無料版とPlusの機能分けについて

Plus（Microsoft Storeのサブスクリプション）で解放される機能は、次の2段構えでテストしています。

1. **購読状態の判定** — `StoreLicenseService`。ただし判定の本体は Store API（`StoreContext`）なので、テストで確認できるのは「購読が確認できない間は解放しない」側だけです。実行環境のStoreアカウントが実際にPlusを購読していると `RefreshLicenseAsync` の結果は true になり得るため、**この戻り値そのものはアサートしません**。正しい開発者キー（`ApplyDeveloperUnlockKey`）による解放も、埋め込まれているのがハッシュだけなのでテストできません。
2. **判定結果を受け取った後の挙動** — `MainWindowViewModel.SetPlusFeaturesEnabled(bool)` に true/false を直接渡し、お気に入り・最近・よく使うノードの出し入れを確認します。Storeには一切触れないので安定して動きます。

Plus機能を増やしたときは、ゲート（`IsPlusActive` を見る分岐）を **`MainWindow.xaml.cs` の中に書かず、ViewModel か `Utilities/` の純粋な関数として切り出す**とテストできます。列カスタマイズの `FileListColumns.GetEffectiveVisibleColumns(configuredColumns, arePlusFeaturesEnabled)` がその形です。

一方、CSV出力の購読案内（`ExportCsvMenuItem_Click` の `MessageBox`）や列レイアウトの実際の適用（`ApplyFileListColumnLayout`）はWPFのウィンドウ・`DataGridColumn` に直接触れるため、テスト対象外です。ここは引き続き実機で確認してください（デバッグ実行時は環境変数 `PARALLELSCOPE_DEBUG_PLUS=1` でPlusを有効にできます）。

### コメントは日本語で「なぜ」を書く

本体のコードと同じ方針です。テストでは特に、「この入力で何を防いでいるのか」（例: 単純な前方一致だと `C:\Temporary` を誤判定する）を1行添えると、後から失敗したときに直し方が分かります。
