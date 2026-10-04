# UIテスト

[winapp CLI](https://github.com/microsoft/WinAppCli) の `winapp ui`（UI Automation）でビルド済みのexeを実際に操作して、画面の振る舞いを確かめるテストです。単体テスト（`Tests/`）で扱えないWPFのコードビハインド — タブ列・2画面・右クリックメニュー・ショートカット・設定画面 — が対象です。

## 実行方法

リポジトリのルートから（PowerShell 7）:

```powershell
pwsh scripts/ui-tests/Run-UiTests.ps1                     # ビルドしてから全シナリオ
pwsh scripts/ui-tests/Run-UiTests.ps1 -NoBuild -Filter Tabs  # ファイル名の部分一致で絞り込む
pwsh scripts/ui-tests/Run-UiTests.ps1 -KeepArtifacts       # 成功しても一時フォルダを残す
```

- 必要なもの: winapp CLI（`winget install Microsoft.WinAppCli`）、PowerShell 7。
- 全シナリオで2分弱かかります。**実行中は実際のマウス・キーボード入力を使うので、PCを触らず、画面をロックしないでください**（ドラッグ・右クリック・ショートカットはOS全体への入力として送ります）。
- 失敗したステップではウィンドウのスクリーンショットを撮り、置き場所を表示します。
- 手元専用です（CIのランナーはデスクトップ操作との相性が悪いため組み込んでいません）。

## しくみ

- シナリオごとに一時フォルダへ決まった構成のフォルダ（`UiTestRoot`。中身は `UiTest.psm1` の `New-UiFixture`）と settings.json を作り、環境変数 `PARALLELSCOPE_DATA_DIR` でアプリデータの置き場をそこへ差し替えて起動します（DEBUGビルドのみ有効。`AppDataPathProvider`）。**実際の settings.json・キャッシュDBには触れません。**
- Plus機能のシナリオは `PARALLELSCOPE_DEBUG_PLUS=1` で起動します。表示言語は英語に固定します。
- シナリオ（`scenarios/*.ps1`）は `$Scenario` に `Plus` / `Settings`（settings.json へ足す値）/ `Steps`（順序付きのステップ）を定義します。ステップは前のステップの状態を引き継ぐので、1つ失敗したら残りは飛ばします。
- Windows 11 Home では Windows Sandbox（`winapp ui --on sandbox`）が使えないため、手元のデスクトップでそのまま動かします。

## テストを書くときの決まりごと

- **要素はAutomationIdで指定します。** `winapp ui inspect` が出すハッシュ付きのID（`btn-22ad` など）は画面構成が変わると変わります。新しく押す・読む要素を足したら、XAMLに `AutomationProperties.AutomationId`（アイコンだけのボタンには `AutomationProperties.Name` も）を付けてください。`x:Name` / `Name` を持つ要素はその名前がそのままAutomationIdになります。
- **2画面では同じAutomationIdが2組並ぶ**ので、ペイン内の要素は `-Pane 0` / `-Pane 1` を付けて探します（ペインには位置で `Pane0` / `Pane1` を付けています。`MainWindow.SplitView.cs`）。
- **固定の `Start-Sleep` で待たず**、`Wait-UiFileRows` / `Wait-UiValue` / `Wait-UiTabCount` / `Wait-UiElement` で期待する状態になるまで待ちます（一覧の更新はバックグラウンドで進むため）。
- **ショートカットは `Send-UiKeys`（`--via send-input`）で送ります。** winapp既定の post-message ではWPFの `PreviewKeyDown` に修飾キーの状態が届かず、`Ctrl+T` などが効きません。
- タブ名は一覧の行名と重なりうるので、タブを選ぶときは `Select-UiTab`（位置で選ぶ）を使います。行・ツリー項目のように名前で探す場合は `-Type DataItem` / `-Type TreeItem` で種類を絞ります。
- 右クリックメニュー・タイトルバーのメニューは別ウィンドウ（ポップアップ）ですが、メインウィンドウが所有しているので同じHWND指定のまま探せます。設定画面（モーダル）は `Use-UiWindow 'Settings'` で操作対象を切り替えます。
- 期待値はフィクスチャの構成に依存します。フィクスチャを変えるときは全シナリオを見直してください。

## シナリオ

| ファイル | 内容 |
| --- | --- |
| `01-Smoke` | 起動直後の一覧・アドレス欄・ツリー・戻る/進む/上への状態 |
| `02-Navigation` | ダブルクリック・戻る/進む/上へ・アドレス欄＋Enter・ツリー選択でのフォルダ移動 |
| `03-SearchAndAllFiles` | インクリメンタルサーチ（絞り込み・大文字小文字・クリア）とAll Filesの切り替え |
| `04-Tabs` | `Ctrl+T` / `Ctrl+W` / `Ctrl+Shift+T` / `Ctrl+Tab` / `Ctrl+数字`、閉じるボタン、20タブの上限 |
| `05-SplitView` | 反対側のペインで開く・メニューからの分割/解除・`F6`・タブのドラッグでのペイン間移動・ペインを閉じる |
| `06-FreeEdition` | 無料版でタブ列・ツリー開閉・分割・CSV出力・タブのショートカットが使えないこと |
| `07-RestoreLayout` | タブ構成・2画面の保存と復元、未購読で起動しても保存済みの構成が消えないこと |
| `08-Settings` | 表示言語の即時切り替え（列見出しまで）と、隠しファイル表示の保存 |
