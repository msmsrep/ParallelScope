#Requires -Version 7
<#
.SYNOPSIS
    ストア掲載用のスクリーンショットに写す、架空のフォルダ／ファイル一式を作る。

.DESCRIPTION
    第三者のファイル名が写り込むと審査で落ちるので、撮影には必ずこの架空の一式を使う。
    ファイル名は英数字だけなので、日本語と英語で同じ一式を使い回せる（Type列だけは
    Windowsシェルが返す文字列なのでOSの表示言語に従う）。

    中身は空ではなく、表の Size ぶんのゼロ埋めで作る。ファイル一覧の Size 列と、
    フォルダの合計サイズ表示が「それらしい値」で並ぶようにするため。
    更新日時も表のとおりに揃える（撮るたびに日付が変わると、掲載画像を差し替えたとき
    1枚だけ日付が違うことになる）。

    既にあれば何もしない。作り直したいときは -Force を付ける。

.EXAMPLE
    pwsh -File store/assets/sample-tree.ps1

.EXAMPLE
    pwsh -File store/assets/sample-tree.ps1 -Force
#>

[CmdletBinding()]
param(
    # 一式を置く場所。capture-screenshots.ps1 の既定と揃えること
    [string]$Path = 'C:\ScopeDemo',
    # 既にあっても消してから作り直す
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

# 「相対パス|バイト数|更新日時」。Reports が多いのは、検索の絵（"report" で絞る）を
# 撮るため。node_modules は除外フォルダの説明に使う想定で置いてある
$table = @'
Archive\2024\old-report-2024.pdf|2027520|2025-01-20 18:00:00
Archive\2024\project-atlas-final.zip|7577600|2025-01-20 18:00:00
Design\Brand\color-palette.png|634880|2025-07-09 14:02:00
Design\Exports\banner-1200x630.png|786432|2026-08-06 15:41:00
Design\Exports\og-image.png|421888|2026-07-25 13:26:00
Design\Brand\logo-mono.svg|28672|2025-07-08 10:44:00
Design\Brand\logo-primary.svg|36864|2025-07-08 10:44:00
Design\Brand\typography-guide.pdf|1146880|2025-08-01 09:35:00
Design\Icons\icon-16.png|3072|2025-07-10 08:20:00
Design\Icons\icon-256.png|75776|2025-07-10 08:20:00
Design\Icons\icon-32.png|6144|2025-07-10 08:20:00
Design\Mockups\dashboard-v3.png|2539520|2026-08-06 15:30:00
Design\Mockups\mobile-layout.png|1372160|2026-06-11 17:44:00
Design\Mockups\settings-page.png|1802240|2026-07-25 13:18:00
Projects\Apollo\assets\banner.png|962560|2025-09-14 13:22:00
Projects\Apollo\build\Apollo.dll|1258291|2026-08-13 09:22:00
Projects\Apollo\build\Apollo.pdb|393216|2026-08-13 09:22:00
Projects\Apollo\CHANGELOG.md|9216|2026-08-04 18:41:00
Projects\Apollo\docs\design-spec.pdf|1884160|2026-06-19 11:03:00
Projects\Apollo\docs\meeting-notes.md|14336|2026-08-12 17:55:00
Projects\Apollo\docs\release-report.docx|274432|2026-08-08 10:37:00
Projects\Apollo\docs\requirements.docx|421888|2026-07-28 14:12:00
Projects\Apollo\LICENSE.txt|2048|2025-04-02 10:06:00
Projects\Apollo\README.md|6144|2026-08-11 09:24:00
Projects\Apollo\src\ApiClient.cs|18432|2026-08-05 20:16:00
Projects\Apollo\src\appsettings.json|2048|2026-05-30 12:00:00
Projects\Apollo\src\DataService.cs|22528|2026-08-13 08:47:00
Projects\Apollo\src\Program.cs|7168|2026-08-13 08:47:00
Projects\Apollo\tests\ApiClientTests.cs|11264|2026-07-22 16:28:00
Projects\Apollo\tests\DataServiceTests.cs|15360|2026-08-13 09:10:00
Projects\Borealis\docs\architecture.md|28672|2026-07-31 15:47:00
Projects\Borealis\docs\weekly-report.xlsx|159744|2026-08-14 09:05:00
Projects\Borealis\node_modules\lodash\index.js|542720|2025-11-05 09:02:00
Projects\Borealis\node_modules\react\index.js|325632|2025-11-05 09:02:00
Projects\Borealis\node_modules\typescript\lib.js|2969600|2025-11-05 09:02:00
Projects\Borealis\package.json|3072|2026-08-02 19:35:00
Projects\Borealis\README.md|5120|2026-07-14 10:11:00
Projects\Borealis\src\api.ts|13312|2026-08-10 21:02:00
Projects\Borealis\src\index.ts|4096|2026-08-10 21:02:00
Projects\Borealis\src\store.ts|9216|2026-06-27 14:19:00
Projects\Cascade\docs\cost-report.xlsx|188416|2026-04-01 09:28:00
Projects\Cascade\docs\proposal.pptx|2416640|2026-03-05 16:52:00
Projects\Cascade\README.md|4096|2026-03-18 13:40:00
Projects\Cascade\src\main.py|6144|2026-03-20 11:16:00
Projects\Cascade\src\utils.py|10240|2026-03-20 11:16:00
Reports\2025\annual-report-2025.pdf|3317760|2026-02-02 16:05:00
Reports\2025\sales-report-2025Q1.xlsx|253952|2025-04-08 10:00:00
Reports\2025\sales-report-2025Q2.xlsx|268288|2025-07-07 10:12:00
Reports\2025\sales-report-2025Q3.xlsx|277504|2025-10-06 09:48:00
Reports\2025\sales-report-2025Q4.xlsx|294912|2026-01-13 11:26:00
Reports\2026\inventory-report.xlsx|145408|2026-08-14 16:21:00
Reports\2026\monthly-report-2026-06.xlsx|98304|2026-07-02 08:44:00
Reports\2026\monthly-report-2026-07.xlsx|100352|2026-08-03 08:39:00
Reports\2026\sales-report-2026Q1.xlsx|301056|2026-04-07 10:31:00
Reports\2026\sales-report-2026Q2.xlsx|308224|2026-07-06 09:57:00
Reports\Templates\invoice-template.docx|53248|2025-03-14 09:00:00
Reports\Templates\report-template.xlsx|65536|2025-03-14 09:00:00
'@

# ファイルを1つも持たないフォルダ。「まだ何も入っていない下書き置き場」として置いてある
$emptyFolders = @('Reports\Drafts')

if (Test-Path $Path) {
    if (-not $Force) {
        Write-Host "$Path は既にあります（作り直すには -Force）。" -ForegroundColor Yellow
        return
    }
    Remove-Item $Path -Recurse -Force
}

foreach ($line in $table -split "`r?`n" | Where-Object { $_ }) {
    $relative, $size, $written = $line -split '\|'
    $file = Join-Path $Path $relative

    New-Item -ItemType Directory -Path (Split-Path -Parent $file) -Force | Out-Null

    # 中身はゼロ埋め。Size 列とフォルダ合計サイズが「それらしい値」で出るようにする
    $stream = [System.IO.File]::Create($file)
    try { $stream.SetLength([long]$size) } finally { $stream.Dispose() }

    $timestamp = [datetime]::ParseExact($written, 'yyyy-MM-dd HH:mm:ss', $null)
    $item = Get-Item $file
    $item.LastWriteTime = $timestamp
    $item.CreationTime = $timestamp
}

foreach ($relative in $emptyFolders) {
    New-Item -ItemType Directory -Path (Join-Path $Path $relative) -Force | Out-Null
}

# フォルダの更新日時も、その配下でいちばん新しいファイルに合わせる。
# 揃えないと「作った日時」が全フォルダに並んでしまい、撮り直すたびに変わる。
# 子から親の順に処理したいので、深いものから
Get-ChildItem -Recurse -Directory $Path |
    Sort-Object { $_.FullName.Length } -Descending |
    ForEach-Object {
        $newest = Get-ChildItem -Force $_.FullName |
            Sort-Object LastWriteTime -Descending |
            Select-Object -First 1
        if ($newest) { $_.LastWriteTime = $newest.LastWriteTime }
    }

$count = (Get-ChildItem -Recurse -File $Path).Count
Write-Host "$Path に $count 件のファイルを作りました。" -ForegroundColor Green
