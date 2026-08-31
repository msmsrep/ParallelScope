#!/usr/bin/env python3
"""Partner Center の「ストアロゴ」に載せる宣伝画像を作る。

必要なもの: Inkscape（SVG のラスタライズ）、Python の Pillow（合成）。

    python store/assets/generate-store-art.py            # 日本語と英語
    python store/assets/generate-store-art.py en         # 英語だけ

出力先（store/promo/{ja,en}/）:
    poster-art-720x1080.png    ポスターアート          720x1080
    box-art-1080x1080.png      ボックスアート          1080x1080
    hero-art-1920x1080.png     スーパーヒーローアート  1920x1080

掲載ロゴ（store/logo-300x300.png）も一緒に作る。

掲載情報は日本語と英語の2言語ぶんある。宣伝画像にも文字が載る以上、
スクリーンショット（store/screenshots/{ja,en}/）と同じく言語ごとに分ける。
掲載ロゴだけは文字が無いので2言語で共通。

絵柄は store/assets/art/parallel-scope-panels.svg、地色と文字色は
compose-screenshots.py と揃えてある。3枚は縦・正方・横で寸法が違うだけで、
載せるものは同じ:

    アイコン ＋ 絵柄 ＋ 製品名 ＋ 一行の説明 ＋ 特徴3つ

スーパーヒーローアートはストア側で製品名などが上に重なることがあるので、
文字と絵柄を左右に分け、下端には何も置いていない。
"""

from __future__ import annotations

import shutil
import subprocess
import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
PROMO = ROOT / "store" / "promo"
ART_SVG = HERE / "art" / "parallel-scope-panels.svg"
ICON_SVG = ROOT / "Assets" / "icon.svg"
LOGO = ROOT / "store" / "logo-300x300.png"
WORK = HERE / ".work"

# 掲載ロゴの角丸タイルの色。アイコンの藤色 #9FA8DA より明るく取り、
# 絵柄（藤色の面と濃紺の線）が地に沈まないようにする。
# 宣伝画像の薄い円より濃いのは、ロゴが小さく出ても輪郭が立つようにするため
TILE = (220, 224, 242)

# 端に寄せない余白。ストア側で角が丸められることがある
MARGIN = 60

# 絵柄の縦横比（SVG の viewBox と同じ）
ART_RATIO = 630 / 950

# 地色はアプリのアイコン（Assets/icon.svg）の色から起こしている。
# 藤色 #9FA8DA を薄く延ばしたものを地に、一段濃いものを円に、
# アイコンの線と同じ濃紺 #18193F を見出しに使う
BACKGROUND = (245, 246, 251)
CIRCLE = (223, 227, 244)
HEADLINE = (24, 25, 63)
BODY = (74, 78, 120)

# 特徴の頭に置く印。色分けの意味は持たせず、箇条書きの点として同じ色で並べる。
# アイコンの藤色を、地の上で読める濃さまで落としたもの
BULLET = (90, 99, 176)

NAME = "ParallelScope"

LANGUAGES = {
    "ja": {
        "bold": r"C:\Windows\Fonts\YuGothB.ttc",
        "regular": r"C:\Windows\Fonts\YuGothR.ttc",
        "tagline": ("離れた場所のフォルダも、", "ひとつのツリーで見渡せます。"),
        "box_line": "複数のフォルダを、ひとつの視界に",
        "features": ("複数ルート", "横断検索", "全ファイル"),
    },
    "en": {
        "bold": r"C:\Windows\Fonts\segoeuib.ttf",
        "regular": r"C:\Windows\Fonts\segoeui.ttf",
        "tagline": ("Folders from anywhere,", "gathered into one tree."),
        "box_line": "Many folders, one view",
        "features": ("Many roots", "Search", "All files"),
    },
}


def inkscape() -> str:
    found = shutil.which("inkscape")
    if found:
        return found
    fallback = Path(r"C:\Program Files\Inkscape\bin\inkscape.com")
    if fallback.exists():
        return str(fallback)
    sys.exit("Inkscape が見つかりません。PATH を通すか、既定の場所へ入れてください。")


def rasterize(svg: Path, width: int, height: int, name: str) -> Image.Image:
    """SVG を指定の寸法で描いて返す。"""
    WORK.mkdir(parents=True, exist_ok=True)
    path = WORK / f"{name}-{width}x{height}.png"
    subprocess.run(
        [
            inkscape(),
            str(svg),
            "--export-type=png",
            f"--export-filename={path}",
            f"--export-width={width}",
            f"--export-height={height}",
        ],
        check=True,
        capture_output=True,
    )
    with Image.open(path) as image:
        return image.convert("RGBA").copy()


def art(width: int) -> Image.Image:
    """絵柄を指定の幅で描いて返す。"""
    return rasterize(ART_SVG, width, round(width * ART_RATIO), "art")


def icon(size: int) -> Image.Image:
    """アプリのアイコンの絵柄を指定の大きさで描いて返す。

    掲載ロゴ（logo()）と違い角丸タイルは敷かない。地色がアイコンと同系統なので、
    タイルがあると輪郭が濁る。スクリーンショット側（compose-screenshots.py）も同じ扱い。
    """
    return rasterize(ICON_SVG, size, size, "icon")


def logo() -> Image.Image:
    """掲載ロゴ 300x300。角丸タイルにアイコンの絵柄を載せる。

    アプリのアイコン（Assets/icon.svg）は背景を持たない絵柄だけなので、
    ストアの一覧で地色に溶けないよう、ここでタイルを敷く。
    """
    size = 300
    # 4倍で描いてから縮める（そのまま描くと角丸の縁が階段になる）
    scale = 4
    tile = Image.new("RGBA", (size * scale, size * scale), (0, 0, 0, 0))
    ImageDraw.Draw(tile).rounded_rectangle(
        [0, 0, size * scale - 1, size * scale - 1], round(size * scale * 0.22), fill=TILE)
    image = tile.resize((size, size), Image.LANCZOS)

    glyph = icon(208)
    image.paste(glyph, ((size - glyph.width) // 2, (size - glyph.height) // 2), glyph)
    return image


def font(path: str, size: int) -> ImageFont.FreeTypeFont:
    return ImageFont.truetype(path, size)


def canvas(width: int, height: int, circle: tuple[int, int, int, int]) -> Image.Image:
    """地色を塗り、指定の位置に薄い円を置いた下地を返す。

    円は4倍で描いてから縮める（そのまま描くと縁が階段になる）。
    """
    scale = 4
    image = Image.new("RGB", (width * scale, height * scale), BACKGROUND)
    ImageDraw.Draw(image).ellipse([value * scale for value in circle], fill=CIRCLE)
    return image.resize((width, height), Image.LANCZOS)


def fits(length: float, room: int, text: str) -> None:
    """はみ出したらそこで止める。

    3枚は同じ組みで並ぶので、訳語が長くて1枚だけ字が飛び出すと目立つ。
    黙って作られると、Partner Center へ上げるまで気付かない。
    """
    if length > room:
        sys.exit(f"文字が {room} px に収まりません（{round(length)} px）: {text}")


def centered(draw: ImageDraw.ImageDraw, text: str, y: int, face: ImageFont.FreeTypeFont,
             fill: tuple[int, int, int], width: int) -> None:
    length = draw.textlength(text, font=face)
    fits(length, width - MARGIN * 2, text)
    draw.text(((width - length) / 2, y), text, font=face, fill=fill)


def features(draw: ImageDraw.ImageDraw, labels: tuple[str, ...], x: int, y: int, size: int,
             gap: int, face: ImageFont.FreeTypeFont, room: int, centre: int | None = None) -> None:
    """特徴を横に並べる。centre を渡すとその x を中心に置く。"""
    label_gap = round(size * 0.5)
    widths = [size + label_gap + draw.textlength(label, font=face) for label in labels]

    total = sum(widths) + gap * (len(labels) - 1)
    fits(total, room, " / ".join(labels))
    if centre is not None:
        x = round(centre - total / 2)

    for label, width in zip(labels, widths):
        draw.rounded_rectangle([x, y, x + size, y + size], round(size / 4), fill=BULLET)
        top = y + (size - face.size) / 2 - face.size * 0.18
        draw.text((x + size + label_gap, top), label, font=face, fill=BODY)
        x += round(width) + gap


def hero(copy: dict) -> Image.Image:
    """スーパーヒーローアート 1920x1080。左に文字、右に絵柄。"""
    width, height = 1920, 1080
    image = canvas(width, height, (-190, 780, 610, 1580))
    draw = ImageDraw.Draw(image)

    left = 150
    picture = art(980)
    art_left = 845
    # 文字が絵柄へ食い込まない幅
    room = art_left - left - 40

    # アイコンは左下の円の上へ置く（スクリーンショットと同じ置き方）
    badge = icon(140)
    image.paste(badge, (left, height - 260), badge)

    draw.text((left, 322), NAME, font=font(copy["bold"], 104), fill=HEADLINE)

    y = 496
    tagline = font(copy["regular"], 40)
    for line in copy["tagline"]:
        fits(draw.textlength(line, font=tagline), room, line)
        draw.text((left, y), line, font=tagline, fill=BODY)
        y += 66

    features(draw, copy["features"], left, 682, 32, 38, font(copy["regular"], 30), room)

    image.paste(picture, (art_left, (height - picture.height) // 2), picture)
    return image


def poster(copy: dict) -> Image.Image:
    """ポスターアート 720x1080。縦に絵柄・製品名・説明。"""
    width, height = 720, 1080
    image = canvas(width, height, (-200, 40, 320, 560))
    draw = ImageDraw.Draw(image)

    # アイコンは左上の円の上へ置く（スクリーンショットと同じ置き方）
    badge = icon(96)
    image.paste(badge, (MARGIN, 72), badge)

    picture = art(600)
    image.paste(picture, ((width - picture.width) // 2, 208), picture)

    centered(draw, NAME, 640, font(copy["bold"], 62), HEADLINE, width)

    y = 762
    tagline = font(copy["regular"], 29)
    for line in copy["tagline"]:
        centered(draw, line, y, tagline, BODY, width)
        y += 48

    features(draw, copy["features"], 0, 900, 26, 34, font(copy["regular"], 26),
             width - MARGIN * 2, centre=width // 2)
    return image


def box(copy: dict) -> Image.Image:
    """ボックスアート 1080x1080。小さく出ることを考えて要素を絞る。"""
    width, height = 1080, 1080
    image = canvas(width, height, (-360, -140, 400, 620))
    draw = ImageDraw.Draw(image)

    # アイコンは左上の円の上へ置く（スクリーンショットと同じ置き方）
    badge = icon(120)
    image.paste(badge, (MARGIN + 12, 72), badge)

    picture = art(780)
    image.paste(picture, ((width - picture.width) // 2, 196), picture)

    centered(draw, NAME, 764, font(copy["bold"], 76), HEADLINE, width)
    centered(draw, copy["box_line"], 880, font(copy["regular"], 34), BODY, width)

    features(draw, copy["features"], 0, 954, 30, 42, font(copy["regular"], 28),
             width - MARGIN * 2, centre=width // 2)
    return image


def main() -> None:
    languages = sys.argv[1:] or list(LANGUAGES)
    unknown = [language for language in languages if language not in LANGUAGES]
    if unknown:
        sys.exit("知らない言語です: " + ", ".join(unknown))

    for language in languages:
        copy = LANGUAGES[language]
        folder = PROMO / language
        folder.mkdir(parents=True, exist_ok=True)

        for name, build in (
            ("poster-art-720x1080.png", poster),
            ("box-art-1080x1080.png", box),
            ("hero-art-1920x1080.png", hero),
        ):
            image = build(copy)
            path = folder / name
            image.save(path)
            print(f"{path}  ({image.width}x{image.height})")

    # 文字が無いので言語ごとには分けない
    logo().save(LOGO)
    print(f"{LOGO}  (300x300)")

    shutil.rmtree(WORK, ignore_errors=True)


if __name__ == "__main__":
    main()
