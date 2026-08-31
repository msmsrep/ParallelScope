#!/usr/bin/env python3
"""撮った画面を、ストア掲載用の1枚に仕立てる。

    python store/assets/compose-screenshots.py            # 日本語と英語
    python store/assets/compose-screenshots.py en         # 英語だけ

入力: artifacts/screenshots-raw/{ja,en}/*.png（capture-screenshots.ps1 が撮ったウィンドウ）
出力: store/screenshots/{ja,en}/*.png（1920x1080）

掲載情報は日本語と英語の2言語ぶんあるので、画像も言語ごとに分ける。Partner Center は
言語別の掲載情報にそれぞれ画像を持たせるので、英語の製品ページに日本語の画面が並ぶことは
避けられる。

薄いテーマ色の地に大きな見出しと説明、右にアプリの画面を浮かせ、左下にアイコンと円を置く。
地色と文字色は generate-store-art.py（宣伝画像）と揃えてある。

見出しと説明はここに持たせる。撮影（どの画面を出すか）と掲載文（何と書くか）は別の関心なので、
スクリプトも分けてある。文言だけ直すときは、撮り直さずにこれだけを流し直せばよい。
"""

from __future__ import annotations

import shutil
import subprocess
import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter, ImageFont

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
RAW = ROOT / "artifacts" / "screenshots-raw"
OUT = ROOT / "store" / "screenshots"
ICON = ROOT / "Assets" / "icon.svg"

WIDTH, HEIGHT = 1920, 1080

# 地色はアプリのアイコン（Assets/icon.svg）の色から起こしている。
# 藤色 #9FA8DA を薄く延ばしたものを地に、一段濃いものを円に、
# アイコンの線と同じ濃紺 #18193F を見出しに使う
BACKGROUND = (245, 246, 251)
CIRCLE = (223, 227, 244)
HEADLINE = (24, 25, 63)
BODY = (74, 78, 120)

# 左の文章の位置。右のアプリ画面と重ならない幅に収める
TEXT_LEFT = 120
TEXT_WIDTH = 580

WINDOW_LEFT = 740
WINDOW_WIDTH = 1120

# 言語ごとの組み方。日本語は字面が詰まっているぶん行間を広く取る。
# 英語は語で折り返し、同じ幅に収まるよう見出しを1段小さくする
LAYOUTS = {
    "ja": {
        "bold": r"C:\Windows\Fonts\YuGothB.ttc",
        "regular": r"C:\Windows\Fonts\YuGothR.ttc",
        "headline_size": 62,
        "headline_step": 88,
        "body_size": 25,
        "body_step": 48,
        "word_wrap": False,
    },
    "en": {
        "bold": r"C:\Windows\Fonts\segoeuib.ttf",
        "regular": r"C:\Windows\Fonts\segoeui.ttf",
        "headline_size": 58,
        "headline_step": 80,
        "body_size": 26,
        "body_step": 42,
        "word_wrap": True,
    },
}

SHOTS = [
    {
        "raw": "01-tree.png",
        "out": "01-tree.png",
        "ja": {
            "headline": ["離れた場所も", "ひとつのツリーで"],
            "body": "ドライブも階層もばらばらのフォルダを、ルートとして登録するだけ。"
                    "左のツリーに並んで、戻る・進む・上への操作もそのまま使えます。",
        },
        "en": {
            "headline": ["Every folder you use,", "in a single tree"],
            "body": "Register the folders you work in, wherever they live, and they line up as "
                    "roots in one tree. Back, Forward, Up and the address bar all work the "
                    "way you expect.",
        },
    },
    {
        "raw": "02-search.png",
        "out": "02-search.png",
        "ja": {
            "headline": ["打つそばから", "候補が絞られる"],
            "body": "検索ボタンはありません。文字を入れるたびに、いま開いているフォルダの下が"
                    "キャッシュから絞り込まれます。空にすれば元の一覧に戻ります。",
        },
        "en": {
            "headline": ["Results narrow", "as you type"],
            "body": "There is no search button. Every keystroke filters what sits under the "
                    "folder you are in, answered from the local cache. Clear the box and the "
                    "list comes back.",
        },
    },
    {
        "raw": "03-all-files.png",
        "out": "03-all-files.png",
        "ja": {
            "headline": ["フォルダの奥まで", "まとめて一覧に"],
            "body": "「All Files」を押すと、いま開いているフォルダの配下にあるファイルが、"
                    "階層に関係なくすべて並びます。どこにあったのかは Location 列で分かります。",
        },
        "en": {
            "headline": ["Everything below,", "in one flat list"],
            "body": "Turn on All Files and every file under the current folder shows up at "
                    "once, however deep it sits. The Location column tells you where each came "
                    "from.",
        },
    },
    {
        "raw": "04-favorites.png",
        "out": "04-favorites.png",
        "ja": {
            "headline": ["よく使うフォルダに", "すぐ戻る"],
            "body": "お気に入り・最近使った・よく使うフォルダを、ツリーの最上位に置けます。"
                    "出すものと並び順は設定画面から選べます。配色はライトとダークから。",
        },
        "en": {
            "headline": ["Get back to the", "folders you live in"],
            "body": "Favorites, Recent and Frequently Used sit at the top of the tree. Pick "
                    "which of them to show, and in what order, from the settings window. "
                    "Light and dark are both included.",
        },
    },
]


def font(path: str, size: int) -> ImageFont.FreeTypeFont:
    return ImageFont.truetype(path, size)


def wrap(text: str, face: ImageFont.FreeTypeFont, width: int, *, word_wrap: bool) -> list[str]:
    """幅を超える手前で折り返す。

    英語は語の切れ目で、日本語は単語で切れないので1文字ずつ見る。
    """
    if word_wrap:
        lines: list[str] = []
        line = ""
        for word in text.split():
            candidate = f"{line} {word}" if line else word
            if face.getlength(candidate) > width and line:
                lines.append(line)
                line = word
            else:
                line = candidate
        if line:
            lines.append(line)
        return lines

    lines = []
    line = ""

    for character in text:
        candidate = line + character
        if face.getlength(candidate) > width and line:
            # 行頭に置けない文字は前の行に残す
            if character in "、。）」":
                lines.append(candidate)
                line = ""
                continue
            lines.append(line)
            line = character
        else:
            line = candidate

    if line:
        lines.append(line)
    return lines


def icon(height: int) -> Image.Image:
    """アイコン（Assets/icon.svg）を SVG から起こす。

    掲載画像の地色はアイコンの背景と同系統なので、角丸タイルは敷かず絵柄だけを置く。
    """
    work = HERE / ".work"
    work.mkdir(parents=True, exist_ok=True)
    path = work / f"icon-{height}.png"

    inkscape = shutil.which("inkscape") or r"C:\Program Files\Inkscape\bin\inkscape.com"
    if not Path(inkscape).exists():
        sys.exit("Inkscape が見つかりません。")

    subprocess.run(
        [
            inkscape,
            str(ICON),
            "--export-type=png",
            f"--export-filename={path}",
            f"--export-width={height}",
            f"--export-height={height}",
        ],
        check=True,
        capture_output=True,
    )
    with Image.open(path) as image:
        return image.convert("RGBA").copy()


def rounded(image: Image.Image, radius: int) -> Image.Image:
    """ウィンドウの角を丸める。実際の Windows の角丸に合わせる。"""
    mask = Image.new("L", image.size, 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, image.width - 1, image.height - 1], radius, fill=255)
    out = image.convert("RGBA")
    out.putalpha(mask)
    return out


def shadow(size: tuple[int, int], radius: int, blur: int, alpha: int) -> Image.Image:
    layer = Image.new("RGBA", (size[0] + blur * 4, size[1] + blur * 4), (0, 0, 0, 0))
    ImageDraw.Draw(layer).rounded_rectangle(
        [blur * 2, blur * 2, blur * 2 + size[0], blur * 2 + size[1]],
        radius,
        fill=(24, 25, 63, alpha),
    )
    return layer.filter(ImageFilter.GaussianBlur(blur))


def compose(shot: dict, language: str, badge: Image.Image) -> Image.Image:
    layout = LAYOUTS[language]
    copy = shot[language]

    canvas = Image.new("RGB", (WIDTH, HEIGHT), BACKGROUND)

    # 左下の円。4倍で描いて縮めると縁が滑らかになる
    scale = 4
    circle = Image.new("RGB", (WIDTH * scale, HEIGHT * scale), BACKGROUND)
    ImageDraw.Draw(circle).ellipse(
        [(-150) * scale, 850 * scale, 550 * scale, 1550 * scale],
        fill=CIRCLE,
    )
    canvas = Image.blend(canvas, circle.resize((WIDTH, HEIGHT), Image.LANCZOS), 1.0)

    draw = ImageDraw.Draw(canvas)

    headline_font = font(layout["bold"], layout["headline_size"])
    body_font = font(layout["regular"], layout["body_size"])

    # 見出しが決めた幅からはみ出したら、そこで気付けるようにする。
    # 掲載画像は4枚とも同じ組みで並ぶので、1枚だけ字が飛び出すと目立つ
    for line in copy["headline"]:
        if headline_font.getlength(line) > TEXT_WIDTH:
            sys.exit(f"見出しが {TEXT_WIDTH} px に収まりません（{language} / {shot['out']}）: {line}")

    y = 300
    for line in copy["headline"]:
        draw.text((TEXT_LEFT, y), line, font=headline_font, fill=HEADLINE)
        y += layout["headline_step"]

    y += 42
    for line in wrap(copy["body"], body_font, TEXT_WIDTH, word_wrap=layout["word_wrap"]):
        draw.text((TEXT_LEFT, y), line, font=body_font, fill=BODY)
        y += layout["body_step"]

    canvas.paste(badge, (TEXT_LEFT, HEIGHT - 260), badge)

    # アプリの画面。角を丸めて影を落とし、右側に浮かせる
    with Image.open(RAW / language / shot["raw"]) as raw:
        window = raw.convert("RGBA")

    height = round(window.height * WINDOW_WIDTH / window.width)
    window = rounded(window.resize((WINDOW_WIDTH, height), Image.LANCZOS), 14)

    top = (HEIGHT - height) // 2
    cast = shadow((WINDOW_WIDTH, height), 14, blur=30, alpha=90)
    canvas.paste(cast, (WINDOW_LEFT - 60, top - 44), cast)
    canvas.paste(window, (WINDOW_LEFT, top), window)

    return canvas


def main() -> None:
    languages = sys.argv[1:] or list(LAYOUTS)
    unknown = [language for language in languages if language not in LAYOUTS]
    if unknown:
        sys.exit("知らない言語です: " + ", ".join(unknown))

    missing = [
        f"{language}/{shot['raw']}"
        for language in languages
        for shot in SHOTS
        if not (RAW / language / shot["raw"]).exists()
    ]
    if missing:
        sys.exit(
            "撮影した画面がありません: " + ", ".join(missing)
            + "\n先に pwsh -File store/assets/capture-screenshots.ps1 を実行してください。"
        )

    badge = icon(140)

    for language in languages:
        folder = OUT / language
        folder.mkdir(parents=True, exist_ok=True)
        for shot in SHOTS:
            path = folder / shot["out"]
            compose(shot, language, badge).save(path)
            print(f"{path}  ({WIDTH}x{HEIGHT})")

    shutil.rmtree(HERE / ".work", ignore_errors=True)


if __name__ == "__main__":
    main()
