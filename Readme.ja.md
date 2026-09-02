# ParallelScope

[English](./Readme.md) | 日本語

ParallelScope は、指定した複数ルート配下のフォルダ/ファイルを横断して参照できる Windows 向けデスクトップアプリです。
WPF で UI を構築し、ローカル SQLite キャッシュを使って表示と検索を高速化しています。

📖 **[使い方ガイド](https://msmsrep.github.io/ParallelScope/index.ja.html)** — 画面写真つきの操作説明。

## 主な機能

- 複数ルートフォルダの登録（設定画面から追加/削除/並び替え）と除外フォルダの指定
- ツリー + 一覧によるファイルブラウズ
- 戻る/進む/上へ のナビゲーション
- アドレスバーへのパス直接入力
- 現在フォルダ配下のインクリメンタルサーチ（入力の都度、キャッシュに対して検索）
- 「All Files」モード（現在フォルダ配下の全ファイルをフラットに一覧表示）
- 一覧のダブルクリックでフォルダ移動/ファイルを既定アプリで起動
- 配色テーマ（System / Light / Dark）
- 表示言語（System / English / 日本語。既定はWindowsの表示言語に追従。無料版でも利用可）
- Plus機能: 複数タブ、2画面（分割表示）、ペインごとのフォルダツリーの開閉、ツリーの「★ Favorites」「🕘 Recent」「🕒 Frequently Used」（表示するノードと並び順を選択可）、
  ファイル一覧の列カスタマイズ（表示列・並び順・列幅）、表示中の一覧のCSV出力

## リリース

- 未リリース
  - 複数タブを追加（Plus機能。タブごとに現在フォルダ・履歴・検索語・表示モード・並び順を保持し、構成は次回起動時に復元）
  - 2画面（分割表示）を追加（Plus機能。左右／上下に分割し、ペインごとにツリーと一覧を持つ）
  - フォルダツリーの開閉を追加（Plus機能。ツリーを畳んで一覧を全幅で使える。開閉状態はペインごとに保存し、分割で増やしたペインは畳んだ状態から始まる）
  - 設定に「フォルダツリー」ページを追加し、ツリー最上位のノードの表示/非表示と並び順を変更できるように（Plus機能）
  - ツリーに「🕘 Recent」（最近開いたフォルダー）を追加（Plus機能）
  - 英語/日本語の表示言語切り替えを追加（既定はWindowsの表示言語に追従）
  - ツリーに「★ Favorites」「🕒 Frequently Used」を追加（Plus機能）
  - 表示中の一覧の「Export CSV...」を追加（Plus機能）
  - 「Display Columns」を刷新し、列の並び順と列幅も保存するよう変更（Plus機能）
- Ver 1.4.5.0 メニューに「User Guide」を追加
- Ver1.4.0.0 Monthly Subscription機能を追加
- Ver 1.3.0.0 機能変更
  - 「All Files」モードの追加
  - 検索をインクリメンタルサーチへ変更
- Ver 1.2.0.0 調整
  - 検索UIの表示修正
  - ファイルスキャンロジックの高速化
- Ver 1.1.0.0 機能追加
  - 定期的なスキャン実行（既定3時間）
  - フォルダ右クリックからスキャン実行
  - 除外フォルダの指定
- Ver 1.0.0.0 リリース

## 動作環境

- Windows
- .NET SDK 10.0 以上（`net10.0-windows10.0.19041.0`）

## セットアップ

```powershell
dotnet restore
```

## 実行

```powershell
dotnet run --project ParallelScope.csproj
```

## ビルド

```powershell
dotnet build ParallelScope.csproj
# リリース
dotnet publish -c Release
```

## テスト

```powershell
dotnet test Tests/ParallelScope.Tests/ParallelScope.Tests.csproj
```

単体テスト（xUnit）はUIに依存しない層を対象にしています。カバー範囲とテストの書き方は [Tests/README.md](./Tests/README.md) を参照してください。

## 使い方

1. 起動後、「Menu > Settings」をクリック
2. 監視したいルートフォルダを 1 件以上追加して「Save + Full Scan」
3. 左のツリーでフォルダを選択すると、右側に内容が表示
4. 検索ボックスに語句を入力（入力の都度、現在フォルダ配下を検索）
5. 一覧項目をダブルクリック
   - フォルダ: そのフォルダへ移動
   - ファイル: 既定アプリで開く
6. 「All Files」をONにすると、現在フォルダ配下の全ファイルを階層に関係なく一覧表示
7. 「Menu > Export CSV...」で、表示中の一覧をCSVへ書き出し（Plus機能）
8. タブ列の「＋」または Ctrl+T で新しいタブ、「Menu > 画面を分割する」で左右／上下の2画面表示（どちらもPlus機能）
9. 「Back」の左にある「☰」ボタンでフォルダツリーを畳み、一覧を全幅で表示（Plus機能）

詳しくは[使い方ガイド](https://msmsrep.github.io/ParallelScope/index.ja.html)を参照してください。

## データ保存先

`%LOCALAPPDATA%\Packages\msmsrep.ParallelScope_77t1an0ygyrva\LocalState`以下のフォルダへ保存します。
アプリのアンインストール時に保存されたデータも削除されます。

- `settings.json`: ルート/除外フォルダ・スキャン間隔・テーマ・表示言語・ツリー最上位ノードのレイアウト・ファイル一覧の列レイアウト・お気に入り・フォルダごとのアクセス回数
- `ParallelScope.sqlite`: ファイル一覧キャッシュ

## 開発メモ

### EF Core マイグレーション

このリポジトリはローカルツールとして `dotnet-ef`（10.0.9）を定義しています。

```powershell
dotnet tool restore
dotnet ef migrations add <MigrationName>
dotnet ef database update
```

### 主な構成

- `MainWindow.xaml` / `MainWindow.xaml.cs`: メイン画面（メニューとペインの置き場）
- `Views/BrowserPaneView.xaml`: 閲覧ペイン（タブ列＋フォルダツリー＋ファイル一覧）。2画面表示では2つ並ぶ
- `SettingsWindow.xaml` / `SettingsWindow.xaml.cs`: 設定ダイアログ（ルート/フォルダツリー/表示列/テーマ/言語/購読/支援）
- `ViewModels/`: 画面ロジック（シェル `MainWindowViewModel` / ペイン `BrowserPaneViewModel` / タブ `BrowserTabViewModel` の3層。いずれも責務ごとにpartialクラスへ分割）
- `Data/`: 設定/キャッシュ/DbContext
- `Utilities/`: 共通ユーティリティ（パス正規化・CSV出力・列定義・仮想フォルダなど）
- `Services/`: Microsoft Store のライセンス判定
- `Migrations/`: EF Core マイグレーション
- `Tests/ParallelScope.Tests/`: 単体テスト
- `docs/`: 公開している使い方ガイド

## ParallelScope Plus（月額サブスクリプション）

一部の機能は、Microsoft Store のアドオン「ParallelScope Plus」（月額サブスクリプション）として提供しています。

- **対象機能**:
  - 複数タブ（未購読の間はタブ列を表示しません。保存済みのタブ構成は消さずに残り、購読すると元の構成に戻ります）
  - 2画面（分割表示）（未購読の間は「Menu > 画面を分割する」を選べません）
  - フォルダツリーの開閉（未購読の間は「☰」ボタンを表示せず、ツリーは常に開いたままです）
  - ツリーの「★ Favorites」「🕘 Recent」「🕒 Frequently Used」と、その表示/非表示・並び順を選ぶ設定画面の「フォルダツリー」ページ（未購読の間はツリーに表示されません）
  - 設定画面の「Display Columns」（ファイル一覧の表示列・並び順・列幅のカスタマイズ）
  - 「Menu > Export CSV...」（表示中の一覧のCSV出力）
- 未購読でも、その他のすべての機能は引き続き無料で利用できます。対象機能は設定画面に薄字で表示されて操作のみ制限されるか、実行時に購読ページへの案内が表示されます
- 購読は、Microsoft Store 版アプリの「Settings > Subscription」ページにある「Subscribe to Plus」ボタンから行えます
- 決済・請求・解約はすべて Microsoft Store が処理します。

### OSS と課金の関係

本アプリのソースコードは課金機能の実装も含めてすべて公開しています。購読状態の判定は Microsoft Store のライセンス情報に基づくため、**課金（および Plus 機能のロック）が機能するのは Microsoft Store からインストールした版のみ**です。

## 開発を支援する

本アプリ（ParallelScope）は個人で開発・運営しています。継続的なアップデートや機能改善のため、任意の開発支援を受け付けています。  
ご協力いただける場合は、以下のリンクから支援していただけると大変励みになります。  
（本サポートは対価のない任意の寄付であり、特典の提供はございません。）  

- Ko‑fi: <https://ko-fi.com/msmsrep>  
- GitHub Sponsors: <https://github.com/sponsors/msmsrep>

## プライバシーポリシー

最終更新日：2026年7月26日

### 収集・保存するデータ

本アプリは、ユーザー登録情報、氏名、メールアドレスなどの個人情報を収集しません。
一方で、アプリ機能のために以下の情報をローカル端末内に保存します。

- ルートフォルダ設定（`settings.json`）
- ファイル一覧キャッシュ（`ParallelScope.sqlite`）

保存先は `%LOCALAPPDATA%\Packages\msmsrep.ParallelScope_77t1an0ygyrva\LocalState` です。

### データ処理の範囲

本アプリのファイル参照・検索処理は、ユーザーの端末内で実行されます。
開発者サーバー等にアップロードして処理する仕組みはありません。

### 外部送信・第三者提供

本アプリは、ユーザーデータを外部サービスへ自動送信しません。
また、第三者への販売・共有・提供は行いません。

### アプリ内課金（ParallelScope Plus）

Plus サブスクリプションの購入・請求・ライセンス管理は Microsoft Store が行います。
本アプリは購読状態の確認のために OS を通じて Microsoft Store と通信しますが、支払い情報（クレジットカード番号等）を本アプリが取得・保存することはありません。
購入履歴やサブスクリプションの管理は、ご自身の Microsoft アカウントから行えます。

### Cookie・トラッキング技術

本アプリはデスクトップアプリであり、Web サイトで一般的な Cookie ベースのトラッキングは行いません。

### データの削除方法

アプリが保存したデータは、以下を削除することで利用者自身が消去できます。

- `%LOCALAPPDATA%\Packages\msmsrep.ParallelScope_77t1an0ygyrva\LocalState\settings.json`
- `%LOCALAPPDATA%\Packages\msmsrep.ParallelScope_77t1an0ygyrva\LocalState\ParallelScope.sqlite`

アプリのアンインストール時に保存されたデータも削除されます。

### お問い合わせ

プライバシー・その他ご質問は、[GitHub Issues](https://github.com/msmsrep/ParallelScope/issues) までお寄せください
