#Requires -Version 7
<#
.SYNOPSIS
    ストア掲載用のスクリーンショットを、実際に動かしたアプリから撮る。

.DESCRIPTION
    掲載情報と同じ2言語ぶん、4枚ずつ撮る。2段になっている。

      artifacts/screenshots-raw/{ja,en}/  このスクリプトが撮ったウィンドウそのまま
      store/screenshots/{ja,en}/          掲載用（1920x1080。見出しと説明を添えたもの）

    **アプリの画面は実物のみ。描き起こしたモックアップは使わない。**
    仕立てで足すのは、周りの地色・見出し・説明文・アイコンだけ。

    写すフォルダは sample-tree.ps1 が作る架空の一式（既定で C:\ScopeDemo）だけを使う。
    第三者のファイル名が写り込むと審査で落ちる。

    表示モード（テーマ・言語・表示列・ツリーのノード・All Files）は、撮影のたびに
    settings.json を書いてから起動して切り替える。撮影用の引数をアプリへ足すより、
    利用者と同じ経路を通したほうが「実際の画面である」ことを保てる。

    settings.json では決められない操作（フォルダを開く・検索語を打つ・ノードを開く）は
    UI オートメーションでアプリを触って行う。これも利用者と同じ経路になる。

    **開発ビルド（bin/Debug）を撮る。** 表示言語はアプリ内の対訳表で切り替わるので、
    Parallel-Draft と違い MSIX の登録は要らない。Plus 機能は DEBUG ビルド限定の
    環境変数 PARALLELSCOPE_DEBUG_PLUS=1 で有効にする（購読済みの画面を撮るため）。

    撮影中は %LOCALAPPDATA%\ParallelScope\ を丸ごと退避する（設定とキャッシュDBの両方。
    撮影用のルートフォルダでフルスキャンを走らせるので、開発中のキャッシュを汚さない）。
    終わると元に戻す。

.EXAMPLE
    pwsh -File store/assets/capture-screenshots.ps1

.EXAMPLE
    pwsh -File store/assets/capture-screenshots.ps1 -Language en
#>

[CmdletBinding()]
param(
    # 撮る言語。掲載情報と同じ2言語が既定
    [ValidateSet('ja', 'en')]
    [string[]]$Language = @('ja', 'en'),
    # 写す架空のフォルダ一式の場所（sample-tree.ps1 の既定と揃えること）
    [string]$SamplePath = 'C:\ScopeDemo',
    # 撮るウィンドウの大きさ。掲載画像では縮めて置くので、画面に収まる範囲でできるだけ大きく撮る
    [int]$WindowWidth = 1840,
    [int]$WindowHeight = 940,
    # 起動後、初回フルスキャンが終わるのを待つ時間
    [int]$ScanSeconds = 8,
    # ビルド済みのexeがあっても作り直す
    [switch]$Rebuild,
    # 撮るだけで、掲載用の仕立てを行わない
    [switch]$SkipCompose
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @'
using System;
using System.Runtime.InteropServices;

public static class Win {
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hWnd, int attribute, out Rect value, int size);

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect { public int Left, Top, Right, Bottom; }

    // 影を含まない実際の枠。ウィンドウ矩形をそのまま使うと余白が入る
    public const int ExtendedFrameBounds = 9;
}
'@

$automation = [System.Windows.Automation.AutomationElement]
$treeScope = [System.Windows.Automation.TreeScope]

<#
  次を撮る前に、前のアプリが完全に消えるまで待つ。
  ウィンドウを閉じてもプロセスはすぐには消えず、次の起動の直後に名前で拾うと
  前のプロセスの方が返る。表示モードも言語も切り替わらないまま、同じウィンドウを
  撮り続けることになる。
#>
function Stop-App {
    $deadline = (Get-Date).AddSeconds(20)

    while ((Get-Date) -lt $deadline) {
        $running = @(Get-Process -Name 'ParallelScope' -ErrorAction SilentlyContinue)
        if ($running.Count -eq 0) { return }

        foreach ($item in $running) {
            if ($item.HasExited) { continue }
            if ($item.MainWindowHandle -ne 0) { [void]$item.CloseMainWindow() }
            else { $item.Kill() }
        }
        Start-Sleep -Milliseconds 400
    }

    Get-Process -Name 'ParallelScope' -ErrorAction SilentlyContinue | ForEach-Object { $_.Kill() }
    Start-Sleep -Milliseconds 500
}

function Capture($handle, $path) {
    $rect = New-Object Win+Rect
    [void][Win]::DwmGetWindowAttribute($handle, [Win]::ExtendedFrameBounds, [ref]$rect, 16)

    $w = $rect.Right - $rect.Left
    $h = $rect.Bottom - $rect.Top

    $window = New-Object System.Drawing.Bitmap $w, $h
    $graphics = [System.Drawing.Graphics]::FromImage($window)
    $graphics.CopyFromScreen($rect.Left, $rect.Top, 0, 0, $window.Size)
    $graphics.Dispose()

    $window.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $window.Dispose()
    return "$w x $h"
}

# アプリのウィンドウを UI オートメーションで掴む
function Get-Window([int]$processId) {
    $condition = New-Object System.Windows.Automation.PropertyCondition($automation::ProcessIdProperty, $processId)
    $deadline = (Get-Date).AddSeconds(20)
    while ((Get-Date) -lt $deadline) {
        $element = $automation::RootElement.FindFirst($treeScope::Children, $condition)
        if ($element) { return $element }
        Start-Sleep -Milliseconds 300
    }
    throw 'ウィンドウが開きませんでした。'
}

<#
  アドレス欄と検索欄を返す。どちらも名前を持たない TextBox なので、
  位置（アドレス欄はツールバー、検索欄はその下）で見分ける。
  FindAll の並び順は保証されないため、必ず上端の座標で並べ替えてから取る。
#>
function Get-TextBoxes($window) {
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        $automation::ControlTypeProperty, [System.Windows.Automation.ControlType]::Edit)

    # 初回起動はDBの作成と最初のフルスキャンが重なり、UIオートメーションの問い合わせが
    # 何度か空振りする。待ち時間を長めに取る（短いと1枚目だけ落ちる）
    $deadline = (Get-Date).AddSeconds(60)
    while ((Get-Date) -lt $deadline) {
        $found = @($window.FindAll($treeScope::Descendants, $condition))
        if ($found.Count -ge 2) {
            return $found | Sort-Object { $_.Current.BoundingRectangle.Top }
        }
        Start-Sleep -Milliseconds 300
    }
    # どのウィンドウを掴んでいたのか分からないと原因を追えないので、名前を添えて投げる
    throw ("アドレス欄と検索欄が見つかりませんでした。window='" + $window.Current.Name + "'")
}

function Set-TextBoxValue($element, [string]$value) {
    $pattern = $element.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
    $pattern.SetValue($value)
}

<#
  ツリーのノードを表示名で探して開く。見つからなければ $false を返す。

  ノード自身の Name では引けない。ツリーの項目は DisplayMemberPath ではなく
  テンプレートで組んであるため、UI オートメーションが返す名前は ViewModel の
  型名になる。表示名は項目の直下にある文字（記号＋名前の TextBlock）から読む。
#>
function Expand-TreeNode($window, [string]$name) {
    $itemCondition = New-Object System.Windows.Automation.PropertyCondition(
        $automation::ControlTypeProperty, [System.Windows.Automation.ControlType]::TreeItem)
    $textCondition = New-Object System.Windows.Automation.PropertyCondition(
        $automation::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)

    $deadline = (Get-Date).AddSeconds(10)
    while ((Get-Date) -lt $deadline) {
        foreach ($element in $window.FindAll($treeScope::Descendants, $itemCondition)) {
            $labels = @($element.FindAll($treeScope::Children, $textCondition)) |
                ForEach-Object { $_.Current.Name }
            if ($labels -notcontains $name) { continue }

            $pattern = $element.GetCurrentPattern(
                [System.Windows.Automation.ExpandCollapsePattern]::Pattern)
            $pattern.Expand()
            Start-Sleep -Milliseconds 700
            return $true
        }
        Start-Sleep -Milliseconds 300
    }
    return $false
}

<#
  撮る順番は掲載する順番。1枚目が製品ページの代表になる。

  **Type 列はどの1枚にも出さない。** 中身は Windows シェルが返す種類名で、
  アプリの表示言語ではなく OS の表示言語に従う。日本語環境のこの端末で撮ると、
  英語版の掲載画像に「ファイル フォルダー」が並んでしまう。
#>
$sample = $SamplePath
$shots = @(
    @{
        File     = '01-tree.png'
        Note     = '複数ルートのツリーとファイル一覧'
        Theme    = 'Light'
        Nodes    = @()
        Columns  = @('Size', 'Modified')
        Flat     = $false
        Navigate = "$sample\Projects\Apollo"
        Expand   = @('Apollo')
    }
    @{
        File     = '02-search.png'
        Note     = 'インクリメンタルサーチ'
        Theme    = 'Light'
        Nodes    = @()
        Columns  = @('Location', 'Size', 'Modified')
        Flat     = $false
        Navigate = "$sample\Reports"
        Search   = 'report'
    }
    @{
        File     = '03-all-files.png'
        Note     = 'All Files（フラット表示）'
        Theme    = 'Light'
        Nodes    = @()
        Columns  = @('Location', 'Size', 'Modified')
        Flat     = $true
        Navigate = "$sample\Projects"
    }
    @{
        File     = '04-favorites.png'
        Note     = 'お気に入り・最近・よく使う（ダークテーマ）'
        Theme    = 'Dark'
        Nodes    = @('Favorites', 'Recent', 'Frequent')
        # 並び順もカスタマイズできることが分かるよう、この1枚だけ Folders を最後に置く
        NodeOrder = @('Favorites', 'Recent', 'Frequent', 'AllRoots')
        Columns  = @('Size', 'Modified')
        Flat     = $false
        Navigate = "$sample\Design\Mockups"
        # 仮想ノードは既定で閉じているので、撮る前に開く。表示名は言語で変わる。
        # 開くとそのノード自身が選ばれてアドレス欄が "::Frequent::" になるので、
        # 開くのは移動より先。最後の移動で実在パスへ戻す
        ExpandVirtual = $true
    }
    @{
        File     = '05-tabs.png'
        Note     = '複数タブ'
        Theme    = 'Light'
        Nodes    = @()
        Columns  = @('Size', 'Modified')
        Widths   = @{ Name = 420; Size = 90; Modified = 160 }
        Flat     = $false
        # タブ構成は保存済みの設定として与える（Plus有効なら起動時に復元される）。
        # 移動も検索も要らないので Navigate は置かない
        Panes    = @(
            @{
                Tabs = @("$sample\Projects\Apollo\src", "$sample\Reports\2026",
                         "$sample\Design\Mockups", "$sample\Projects\Borealis")
                ActiveTabIndex = 0
                IsTreeVisible  = $true
            }
        )
    }
    @{
        File     = '06-split-view.png'
        Note     = '2画面（分割表示）'
        Theme    = 'Light'
        Nodes    = @()
        Columns  = @('Size', 'Modified')
        # 2画面では1ペインの幅が半分になるので、名前列を詰めて列が切れないようにする
        Widths   = @{ Name = 240; Size = 90; Modified = 160 }
        Flat     = $false
        Split    = $true
        # 右のペインはツリーを畳んで、ツリーの開閉も同じ1枚で見せる
        Panes    = @(
            @{
                Tabs = @("$sample\Projects\Apollo\src", "$sample\Projects\Apollo\docs")
                ActiveTabIndex = 0
                IsTreeVisible  = $true
            }
            @{
                Tabs = @("$sample\Reports", "$sample\Reports\Drafts")
                ActiveTabIndex = 0
                IsTreeVisible  = $false
            }
        )
    }
)

# 仮想ノードの表示名（Utilities/UiTextResources.cs と揃える）
$virtualNodeNames = @{
    ja = @('お気に入り', '最近開いたフォルダー', 'よく使うフォルダー')
    en = @('Favorites', 'Recent', 'Frequently Used')
}

$languageSetting = @{ ja = 'Japanese'; en = 'English' }

# ツリーに並べるルート。1つのドライブに置いた架空の一式だが、
# ルートは3つ登録して「離れた場所をまとめて見る」という絵にする
$rootPaths = @("$sample\Projects", "$sample\Design", "$sample\Reports")

<#
  お気に入り・よく使うフォルダの下地。実際に触って貯めるかわりに、保存済みの設定として
  与える（利用者が使い込んだ後と同じ状態）。

  ツリーには末尾のフォルダ名しか出ないので、**名前が重ならないフォルダを選ぶ**
  （"docs" が3つ並ぶと、何を指しているのか読み手に分からない）。
  「最近」と「よく使う」は回数と最終アクセスで並びが変わるよう、値をずらしてある。
#>
$favoritePaths = @("$sample\Projects\Apollo", "$sample\Reports\2026", "$sample\Design\Mockups")
$now = Get-Date
$folderUsages = @(
    @{ Path = "$sample\Projects\Borealis";  Count = 26; LastAccessedAt = $now.AddDays(-2) }
    @{ Path = "$sample\Design\Brand";       Count = 19; LastAccessedAt = $now.AddHours(-4) }
    @{ Path = "$sample\Reports\Templates";  Count = 6;  LastAccessedAt = $now.AddMinutes(-15) }
)

if (-not (Test-Path $sample)) {
    Write-Host '架空のフォルダ一式を作ります…' -ForegroundColor Cyan
    & pwsh -File (Join-Path $PSScriptRoot 'sample-tree.ps1') -Path $sample
    if ($LASTEXITCODE -ne 0) { throw "sample-tree.ps1 に失敗しました（終了コード $LASTEXITCODE）。" }
}

# Plus 機能を出すために DEBUG ビルドを撮る（PARALLELSCOPE_DEBUG_PLUS が DEBUG 限定のため）
$exe = Join-Path $root 'bin\Debug\net10.0-windows10.0.19041.0\win-x64\ParallelScope.exe'
if ($Rebuild -or -not (Test-Path $exe)) {
    Write-Host 'アプリをビルドします…' -ForegroundColor Cyan
    & dotnet build (Join-Path $root 'ParallelScope.csproj') -c Debug
    if ($LASTEXITCODE -ne 0) { throw "ビルドに失敗しました（終了コード $LASTEXITCODE）。" }
}

Stop-App

# 撮影中はアプリデータを丸ごと退避する。撮影用のルートでフルスキャンを走らせるので、
# 開発中の設定とキャッシュDBをそのまま使うわけにはいかない
$state = Join-Path $env:LOCALAPPDATA 'ParallelScope'
$backup = Join-Path $env:LOCALAPPDATA 'ParallelScope.capture-backup'

if (Test-Path $backup) {
    throw "$backup が残っています。前回の撮影が中断した可能性があります。中身を確かめて、$state へ戻してから撮り直してください。"
}
if (Test-Path $state) {
    Move-Item $state $backup
}
New-Item -ItemType Directory -Path $state -Force | Out-Null
$settingsPath = Join-Path $state 'settings.json'

$env:PARALLELSCOPE_DEBUG_PLUS = '1'

try {
    foreach ($code in $Language) {
        $output = Join-Path $root "artifacts\screenshots-raw\$code"
        New-Item -ItemType Directory -Path $output -Force | Out-Null

        foreach ($shot in $shots) {
            <#
              初回起動はDBの作成と最初のフルスキャンが重なり、UIオートメーションの問い合わせが
              空振りしたまま返ることがある（アプリのUIスレッドが応答しきれないため）。
              その場合は撮り直す。3回とも駄目なら実際の不具合として投げる。
            #>
            for ($attempt = 1; $attempt -le 3; $attempt++) {
                try {
                    Write-Host "撮影: $code/$($shot.File)  [$($shot.Note)]" -ForegroundColor Cyan

                    Stop-App

                    <#
                      ペインの状態は、要素が1つでも必ず配列として書き出す。
                      ConvertTo-Json は単独要素の配列を素のオブジェクトへ畳んでしまい、
                      AppSettings の List<PaneStateSettings> へ読めずに settings.json 全体が
                      既定値（＝利用者のデスクトップ）へ落ちる。
                    #>
                    [object[]]$paneStates = @()
                    foreach ($pane in @($shot.Panes)) {
                        if (-not $pane) { continue }
                        [object[]]$tabStates = @()
                        foreach ($path in @($pane.Tabs)) {
                            $tabStates += @{ Path = $path; IsFlatFileViewEnabled = [bool]$shot.Flat }
                        }
                        $paneStates += @{
                            Tabs           = $tabStates
                            ActiveTabIndex = $pane.ActiveTabIndex
                            IsTreeVisible  = $pane.IsTreeVisible
                        }
                    }
                    if ($paneStates.Count -eq 0) { $paneStates = $null }

                    @{
                        RootPaths            = $rootPaths
                        ExcludedPaths        = @()
                        FullScanIntervalHours = 3
                        IsFlatFileViewEnabled = [bool]$shot.Flat
                        VisibleColumns       = $shot.Columns
                        ColumnOrder          = @('Name', 'Location', 'Type', 'Size', 'Modified', 'Created', 'Attributes')
                        VisibleTreeNodes     = $shot.Nodes
                        TreeNodeOrder        = if ($shot.NodeOrder) { $shot.NodeOrder } else { @('AllRoots', 'Favorites', 'Recent', 'Frequent') }
                        # Location は既定幅だと配下の深いパスが切れて Size 列へめり込むので広げる
                        ColumnWidths         = if ($shot.Widths) { $shot.Widths } else { @{ Location = 380; Size = 90; Modified = 160 } }
                        CsvExportSizeInBytes = $false
                        FavoritePaths        = $favoritePaths
                        FolderUsages         = $folderUsages
                        Theme                = $shot.Theme
                        Language             = $languageSetting[$code]
                        # タブ・分割はPlus機能。保存済みの状態として与えると、起動時に復元される
                        IsSplitViewEnabled   = [bool]$shot.Split
                        SplitOrientation     = 'Vertical'
                        SplitRatio           = 0.5
                        ActivePaneIndex      = 0
                        Panes                = $paneStates
                    } | ConvertTo-Json -Depth 6 | Set-Content $settingsPath -Encoding UTF8

                    $process = Start-Process $exe -PassThru
                    $window = Get-Window $process.Id

                    # 画面の中央へ置く。SWP_NOZORDER 以外は指定しない（0x0004 = NOZORDER）
                    $screen = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
                    $handle = [IntPtr]$window.Current.NativeWindowHandle
                    [void][Win]::SetWindowPos(
                        $handle, [IntPtr]::Zero,
                        [int](($screen.Width - $WindowWidth) / 2), [int](($screen.Height - $WindowHeight) / 2),
                        $WindowWidth, $WindowHeight, 0x0004)
                    [void][Win]::SetForegroundWindow($handle)

                    # 起動直後のフルスキャンを待つ。検索と All Files はキャッシュだけを見るので、
                    # ここが終わっていないと空の一覧が撮れる
                    Start-Sleep -Seconds $ScanSeconds

                    $textBoxes = Get-TextBoxes $window
                    $addressBox = $textBoxes[0]
                    $searchBox = $textBoxes[1]

                    # 仮想ノードを開くのは移動より先（開いたノード自身が選ばれるため）
                    if ($shot.ExpandVirtual) {
                        foreach ($name in $virtualNodeNames[$code]) {
                            if (-not (Expand-TreeNode $window $name)) {
                                throw "ツリーのノード「$name」が見つかりませんでした（$code/$($shot.File)）。"
                            }
                        }
                    }

                    <#
                      アドレス欄にパスを入れて Enter。利用者が打ち込むのと同じ経路。

                      仮想ノードを開いた後は2回移動する。1回目の移動ではツリーの選択が
                      開いたノード（例:「よく使うフォルダー」）に残ったままになり、
                      アドレス欄の行き先とツリーの選択が食い違って写るため。
                    #>
                    $times = if (-not $shot.Navigate) { 0 } elseif ($shot.ExpandVirtual) { 2 } else { 1 }
                    for ($i = 0; $i -lt $times; $i++) {
                        Set-TextBoxValue $addressBox $shot.Navigate
                        $addressBox.SetFocus()
                        Start-Sleep -Milliseconds 300
                        [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
                        Start-Sleep -Seconds 3
                    }

                    # 移動先のフォルダを開く（実体のノードは移動して初めてツリーに現れる）
                    foreach ($name in @($shot.Expand)) {
                        if ($name -and -not (Expand-TreeNode $window $name)) {
                            throw "ツリーのノード「$name」が見つかりませんでした（$code/$($shot.File)）。"
                        }
                    }

                    if ($shot.Search) {
                        # 入力の都度走るインクリメンタルサーチなので、Enter は要らない
                        Set-TextBoxValue $searchBox $shot.Search
                        Start-Sleep -Seconds 3
                    }

                    # ツリーに残るキーボードフォーカスの黒枠が写らないよう、
                    # 最後にアドレス欄へフォーカスを戻してから撮る
                    [void][Win]::SetForegroundWindow($handle)
                    $addressBox.SetFocus()
                    Start-Sleep -Milliseconds 800

                    $size = Capture $handle (Join-Path $output $shot.File)
                    Write-Host "  -> $code/$($shot.File)  ($size)" -ForegroundColor Green
                    break
                }
                catch {
                    if ($attempt -eq 3) { throw }
                    Write-Host "  撮り直します（$($_.Exception.Message)）" -ForegroundColor Yellow
                    Stop-App
                }
            }
        }
    }
}
finally {
    Stop-App
    Remove-Item Env:\PARALLELSCOPE_DEBUG_PLUS -ErrorAction SilentlyContinue

    Remove-Item $state -Recurse -Force -ErrorAction SilentlyContinue
    if (Test-Path $backup) { Move-Item $backup $state }
}

Write-Host ''
Write-Host ("撮影しました: " + (Join-Path $root 'artifacts\screenshots-raw')) -ForegroundColor Green

if (-not $SkipCompose) {
    Write-Host '掲載用に仕立てます…' -ForegroundColor Cyan
    & python (Join-Path $PSScriptRoot 'compose-screenshots.py') @Language
    if ($LASTEXITCODE -ne 0) { throw "仕立てに失敗しました（終了コード $LASTEXITCODE）。" }
}
