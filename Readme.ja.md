# ParallelScope

[English](./Readme.md) | 日本語

ParallelScope は、指定した複数ルート配下のフォルダ/ファイルを横断して参照できる Windows 向けデスクトップアプリです。
WPF で UI を構築し、ローカル SQLite キャッシュを使って表示と検索を高速化しています。

## 主な機能

- 複数ルートフォルダの登録（設定画面から追加/削除）
- ツリー + 一覧によるファイルブラウズ
- 戻る/進む/上へ のナビゲーション
- アドレスバーへのパス直接入力
- 現在フォルダ配下の検索
  - まずキャッシュ検索
  - ヒットなし時は実ファイルシステムを走査
- 一覧のダブルクリックでフォルダ移動/ファイルを既定アプリで起動

## リリース

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

## 使い方

1. 起動後、メニューの「設定 > 設定を開く」をクリック
2. 監視したいルートフォルダを 1 件以上追加して保存
3. 左のツリーでフォルダを選択すると、右側に内容が表示
4. 検索ボックスに語句を入力して Enter または「検索」を押下
5. 一覧項目をダブルクリック
   - フォルダ: そのフォルダへ移動
   - ファイル: 既定アプリで開く

## データ保存先

`%LOCALAPPDATA%\Packages\msmsrep.ParallelScope_77t1an0ygyrva\LocalState`以下のフォルダへ保存します。
アプリのアンインストール時に保存されたデータも削除されます。

- `settings.json`: ルートフォルダ設定
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

- `MainWindow.xaml` / `MainWindow.xaml.cs`: メイン画面
- `SettingsWindow.xaml` / `SettingsWindow.xaml.cs`: ルート設定ダイアログ
- `ViewModels/`: 画面ロジック
- `Data/`: 設定/キャッシュ/DbContext
- `Migrations/`: EF Core マイグレーション

## ParallelScope Plus（月額サブスクリプション）

一部の機能は、Microsoft Store のアドオン「ParallelScope Plus」（月額サブスクリプション）として提供しています。

- **対象機能**: 設定画面の「Display Columns」（ファイル一覧の表示列カスタマイズ）
- 未購読でも、その他のすべての機能は引き続き無料で利用できます。対象機能は設定画面に薄字で表示され、操作のみ制限されます
- 購読は、Microsoft Store 版アプリの「Settings > Display Columns」ページにある「Subscribe to Plus」ボタンから行えます
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
