# Microsoft Store への提出物（画像）

Partner Center へ上げる画像と、その作り方をまとめる。
**掲載情報は日本語と英語の2言語ぶんあるので、文字が載る画像は言語ごとに作る。**

## 何がどこにあるか

| Partner Center の名前 | ファイル | 寸法 |
| --- | --- | --- |
| スクリーンショット | `store/screenshots/{ja,en}/01〜04-*.png` | 1920x1080（各4枚） |
| ストアロゴ（掲載ロゴ） | `store/logo-300x300.png` | 300x300 |
| ポスターアート | `store/promo/{ja,en}/poster-art-720x1080.png` | 720x1080 |
| ボックスアート | `store/promo/{ja,en}/box-art-1080x1080.png` | 1080x1080 |
| スーパーヒーローアート | `store/promo/{ja,en}/hero-art-1920x1080.png` | 1920x1080 |

掲載ロゴだけは文字が無いので2言語で共通。

## 配色

7枚とも、アプリのアイコン（`Assets/icon.svg`）の色から起こしている。
色を変えるときは、スクリーンショットと宣伝画像の**両方**の定数を同じ値に直すこと
（`compose-screenshots.py` と `generate-store-art.py` にそれぞれ置いてある）。

| 役割 | 値 | 由来 |
| --- | --- | --- |
| 地色 | `#F5F6FB` | アイコンの藤色 `#9FA8DA` を薄く延ばしたもの |
| 左下の円 | `#DFE3F4` | 地色より一段濃い藤色 |
| 見出し | `#18193F` | アイコンの線の色そのまま |
| 本文 | `#4A4E78` | 見出しを地の上で読める濃さまで起こしたもの |
| 特徴の印 | `#5A63B0` | アイコンの藤色を濃くしたもの（`generate-store-art.py` のみ） |
| 掲載ロゴのタイル | `#DCE0F2` | 藤色 `#9FA8DA` の面が沈まない明るさ |

絵柄（`store/assets/art/parallel-scope-panels.svg`）も同じ系統で、
紙が `#FCFCFF`、線が `#18193F`、検索に当たった行の塗りがアイコンと同じ `#9FA8DA`。
フォルダの黄色（`#F6B93B`）だけは、アプリのファイル一覧に出るフォルダアイコンに合わせてある。

## 必要なもの

- Python（Pillow）… 画像の合成
- Inkscape … SVG のラスタライズ（`C:\Program Files\Inkscape\bin\inkscape.com` か PATH）
- PowerShell 7（`pwsh`）… スクリーンショットの撮影
- 游ゴシック（`YuGothB.ttc` / `YuGothR.ttc`）と Segoe UI … 見出しと本文

## 1. スクリーンショット

```powershell
pwsh -File store/assets/capture-screenshots.ps1
```

2段になっている。

1. 実際に動かしたアプリのウィンドウを撮る → `artifacts/screenshots-raw/{ja,en}/`
2. `store/assets/compose-screenshots.py` が掲載用に仕立てる
   → `store/screenshots/{ja,en}/`（1920x1080）

片方の言語だけ撮り直すこともできる。

```powershell
pwsh -File store/assets/capture-screenshots.ps1 -Language en
```

**アプリの画面は実物のみ。描き起こしたモックアップは使わない。**
仕立てで足すのは、周りの地色・見出し・説明文・アイコンだけ。

見出しと説明文は `store/assets/compose-screenshots.py` の `SHOTS` にある（言語ごと）。
文言だけ直すときは、撮り直さずに次を実行すればよい。

```powershell
python store/assets/compose-screenshots.py
```

### 撮影の決まりごと

- **写すのは架空のフォルダ一式だけ**（`store/assets/sample-tree.ps1` が `C:\ScopeDemo` に作る）。
  第三者のファイル名が写り込むと審査で落ちる。無ければ撮影時に自動で作られる。
- **4枚は別の画面にすること**（ツリー／検索／All Files／お気に入り＋ダークテーマ）。
  同じ絵が並んでいたら撮り直す。
- **Type 列はどの1枚にも出さない。** 中身は Windows シェルが返す種類名で、アプリの表示言語ではなく
  **OS の表示言語**に従う。日本語環境で撮ると、英語版の掲載画像に「ファイル フォルダー」が並ぶ。
- 表示モード（テーマ・言語・表示列・ツリーのノード・All Files）は、撮影のたびに
  `settings.json` を書いてから起動して切り替える。撮影用の引数をアプリへ足すより、
  利用者と同じ経路を通したほうが「実際の画面である」ことを保てる。
- `settings.json` では決められない操作（フォルダを開く・検索語を打つ・ノードを開く）は
  UI オートメーションでアプリを触って行う。
- **撮るのは開発ビルド（`bin/Debug/...`）。** 表示言語はアプリ内の対訳表で切り替わるので、
  MSIX の登録は要らない。Plus 機能は DEBUG ビルド限定の環境変数 `PARALLELSCOPE_DEBUG_PLUS=1`
  で有効にする（購読済みの画面を撮るため）。
- 撮影中は `%LOCALAPPDATA%\ParallelScope\` を丸ごと `ParallelScope.capture-backup` へ退避し、
  終わったら戻す。撮影用のルートでフルスキャンを走らせるので、開発中のキャッシュを汚さない。
  **`ParallelScope.capture-backup` が残っていたら撮影が中断している。** 中身を確かめて戻すこと。

### つまずきどころ

- ツリーのノードは、UI オートメーションの名前が ViewModel の型名になる
  （`DisplayMemberPath` ではなくテンプレートで組んであるため）。表示名は項目の直下の
  文字（記号＋名前の TextBlock）から読む。
- 仮想ノード（お気に入り／最近／よく使う）を開くと、そのノード自身が選ばれて
  アドレス欄が `::Frequent::` になる。**開くのは移動より先**、そして移動は2回行う
  （1回目ではツリーの選択が開いたノードに残る）。
- ツリーには末尾のフォルダ名しか出ないので、お気に入り等に入れるフォルダは
  **名前が重ならないもの**を選ぶ（"docs" が3つ並ぶと何を指すのか分からない）。

## 2. 宣伝画像と掲載ロゴ

```powershell
python store/assets/generate-store-art.py            # 日本語と英語
python store/assets/generate-store-art.py en         # 英語だけ
```

`store/promo/{ja,en}/` の3枚と、`store/logo-300x300.png` ができる。

文言（製品名・一行の説明・特徴3つ）は `generate-store-art.py` の `LANGUAGES` にある。
**訳語が長くて枠からはみ出すと、その場で止まる。** 3枚は同じ組みで並ぶので、
1枚だけ字が飛び出すと目立つうえ、黙って作られると Partner Center へ上げるまで気付かない。

絵柄は `store/assets/art/parallel-scope-panels.svg`（左にツリー、右に横断した一覧、という
アプリの構図をそのまま2枚のパネルにしたもの）。文字がほとんど無いので2言語で共通。
地色と文字色はスクリーンショットと揃えてある。

スーパーヒーローアートはストア側で製品名などが上に重なることがあるので、
文字を左半分にまとめ、下端は空けてある。

## 提出前の確認

- [ ] スクリーンショットが日本語と英語で4枚ずつあり、掲載情報の言語ごとに貼ってある
      （`store/screenshots/ja/`・`store/screenshots/en/`）
- [ ] **4枚が別の画面になっている**（同じ絵が並んでいたら撮り直す）
- [ ] スクリーンショットに第三者のファイル名・フォルダ名が写っていない
- [ ] 英語版のスクリーンショットに日本語が写っていない（Type 列・シェルが返す文字列に注意）
- [ ] 掲載ロゴが `store/` に、宣伝画像3枚が `store/promo/{ja,en}/` にあり、
      寸法が Partner Center の要求どおり。**宣伝画像も掲載情報の言語ごとに貼る**
- [ ] アプリ内購入（ParallelScope Plus）の有無が、掲載情報・製品の申告・IARC の回答で一致している
- [ ] プライバシーポリシーの URL が開ける

## まだここに無いもの

掲載文（製品名・説明・機能一覧・キーワード）は、いまのところ `Readme.md` /
`Readme.ja.md` の内容を Partner Center へ手で入れている。
