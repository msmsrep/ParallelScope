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

- Ver 1.1.0.0　機能追加
  - 定期的なスキャン実行（既定3時間）
  - フォルダ右クリックからスキャン実行
  - 除外フォルダの指定

- Ver 1.0.0.0 リリース

## 動作環境

- Windows
- .NET SDK 10.0 以上（`net10.0-windows`）

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

## プライバシーポリシー

最終更新日：2026年6月14日

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

### Cookie・トラッキング技術

本アプリはデスクトップアプリであり、Web サイトで一般的な Cookie ベースのトラッキングは行いません。

### データの削除方法

アプリが保存したデータは、以下を削除することで利用者自身が消去できます。

- `%LOCALAPPDATA%\Packages\msmsrep.ParallelScope_77t1an0ygyrva\LocalState\settings.json`
- `%LOCALAPPDATA%\Packages\msmsrep.ParallelScope_77t1an0ygyrva\LocalState\ParallelScope.sqlite`

アプリのアンインストール時に保存されたデータも削除されます。

### お問い合わせ

プライバシー・その他ご質問は、[GitHub Issues](https://github.com/msmsrep/ParallelScope/issues) までお寄せください
