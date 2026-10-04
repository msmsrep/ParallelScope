#Requires -Version 7
# UIテストの補助関数。winapp CLI（winapp ui）でビルド済みのexeを操作する。
# 使い方・決まりごとは scripts/ui-tests/README.md を参照。

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$script:ExePath = Join-Path $script:RepoRoot 'bin\Debug\net10.0-windows10.0.19041.0\win-x64\ParallelScope.exe'

# 起動中のアプリ（1シナリオにつき1つ）
$script:App = $null

class UiTestFailure : System.Exception {
    UiTestFailure([string]$message) : base($message) {}
}

#region フィクスチャ

<#
.SYNOPSIS
決まった構成のフォルダとアプリデータフォルダを一時フォルダに作る。
シナリオの期待値はこの構成に依存するため、変えるときは scenarios/*.ps1 も合わせて直す。
#>
function New-UiFixture {
    param(
        [string]$WorkRoot,
        [hashtable]$Settings = @{}
    )

    $work = Join-Path $WorkRoot ([Guid]::NewGuid().ToString('N').Substring(0, 8))
    $root = Join-Path $work 'UiTestRoot'
    $data = Join-Path $work 'data'

    foreach ($dir in 'Alpha', 'Alpha\Sub1', 'Beta', 'Gamma') {
        New-Item -ItemType Directory (Join-Path $root $dir) -Force | Out-Null
    }
    $files = [ordered]@{
        'readme.txt'          = 'readme'
        'Alpha\alpha1.txt'    = 'alpha one'
        'Alpha\alpha2.md'     = 'alpha two'
        'Alpha\Sub1\deep.log' = 'deep'
        'Beta\beta.csv'       = 'b,e,t,a'
        'hidden.txt'          = 'hidden'
    }
    foreach ($entry in $files.GetEnumerator()) {
        Set-Content -Path (Join-Path $root $entry.Key) -Value $entry.Value -NoNewline
    }
    (Get-Item (Join-Path $root 'hidden.txt')).Attributes = 'Hidden'

    New-Item -ItemType Directory $data -Force | Out-Null

    # 表示言語は英語に固定する（UIの文字列で要素を探す箇所が言語で変わらないように）
    $appSettings = @{ RootPaths = @($root); Language = 'English'; Theme = 'Light' }
    foreach ($key in $Settings.Keys) { $appSettings[$key] = $Settings[$key] }
    $appSettings | ConvertTo-Json -Depth 10 | Set-Content -Path (Join-Path $data 'settings.json') -Encoding utf8

    [pscustomobject]@{ Work = $work; Root = $root; DataDir = $data }
}

function Get-FixturePath {
    param([AllowEmptyString()][string]$Relative = '')
    if (-not $script:App) { throw 'アプリが起動していません。' }
    if ($Relative -eq '') { return $script:App.Fixture.Root }
    Join-Path $script:App.Fixture.Root $Relative
}

#endregion

#region アプリの起動・終了

function Start-UiApp {
    param(
        [Parameter(Mandatory)]$Fixture,
        [switch]$Plus
    )

    if (-not (Test-Path $script:ExePath)) { throw "exeがありません: $script:ExePath（先に dotnet build してください）" }

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new($script:ExePath)
    $startInfo.UseShellExecute = $false
    $startInfo.Environment['PARALLELSCOPE_DATA_DIR'] = $Fixture.DataDir
    # 親プロセスから受け継いだ値で購読状態が変わらないよう、毎回明示的に決める
    $startInfo.Environment['PARALLELSCOPE_DEBUG_PLUS'] = if ($Plus) { '1' } else { '' }
    $process = [System.Diagnostics.Process]::Start($startInfo)

    $script:App = [pscustomobject]@{ Process = $process; Hwnd = $null; Fixture = $Fixture; Plus = [bool]$Plus }

    # メインウィンドウ（所有者の無いトップレベル）が出るまで待つ。
    # ツールチップ等の所有ウィンドウも同じプロセスに並ぶため、以降はHWNDで名指しする
    $deadline = [DateTime]::Now.AddSeconds(30)
    while ([DateTime]::Now -lt $deadline) {
        $windows = Get-UiWindows
        $main = $windows | Where-Object { $_.ownerHwnd -eq 0 -and $_.PSObject.Properties['title'] -and $_.title -like 'ParallelScope*' } | Select-Object -First 1
        if ($main) {
            $script:App.Hwnd = $main.hwnd
            break
        }
        Start-Sleep -Milliseconds 200
    }
    if (-not $script:App.Hwnd) { throw 'メインウィンドウが表示されませんでした。' }

    Wait-UiElement 'Pane0' -TimeoutMs 15000 | Out-Null
    Wait-UiScanIdle
}

function Stop-UiApp {
    if (-not $script:App) { return }
    $process = $script:App.Process
    if (-not $process.HasExited) {
        $process.Kill()
        $process.WaitForExit(5000) | Out-Null
    }
    $script:App = $null
}

<#
.SYNOPSIS
ウィンドウを閉じるボタンで正常終了させる（終了時の保存処理を通したい場合に使う）。
#>
function Close-UiApp {
    Invoke-UiElement 'CloseWindowButton'
    if (-not $script:App.Process.WaitForExit(10000)) { throw 'アプリが終了しませんでした。' }
}

<#
.SYNOPSIS
同じデータフォルダのまま起動し直す（設定の保存と復元の確認用）。
#>
function Restart-UiApp {
    $fixture = $script:App.Fixture
    $plus = $script:App.Plus
    Close-UiApp
    $script:App = $null
    Start-UiApp -Fixture $fixture -Plus:$plus
}

function Get-UiWindows {
    $pid_ = $script:App.Process.Id
    $json = & winapp ui list-windows -a $pid_ --json 2>$null
    if ($LASTEXITCODE -ne 0 -or -not $json) { return @() }
    @(($json | Out-String | ConvertFrom-Json))
}

<#
.SYNOPSIS
操作対象のウィンドウを切り替える（設定画面などのモーダルダイアログを操作するため）。
#>
function Use-UiWindow {
    param([Parameter(Mandatory)][string]$TitleLike, [int]$TimeoutMs = 5000)
    $deadline = [DateTime]::Now.AddMilliseconds($TimeoutMs)
    while ([DateTime]::Now -lt $deadline) {
        $window = Get-UiWindows | Where-Object { $_.PSObject.Properties['title'] -and $_.title -like $TitleLike } | Select-Object -First 1
        if ($window) {
            $previous = $script:App.Hwnd
            $script:App.Hwnd = $window.hwnd
            return $previous
        }
        Start-Sleep -Milliseconds 200
    }
    throw [UiTestFailure]::new("ウィンドウ '$TitleLike' が見つかりませんでした。")
}

function Set-UiWindowHandle {
    param([Parameter(Mandatory)]$Hwnd)
    $script:App.Hwnd = $Hwnd
}

#endregion

#region winapp ui の呼び出し

function Invoke-WinappUi {
    param(
        [Parameter(Mandatory)][string[]]$Arguments,
        [switch]$AllowFailure
    )

    $output = & winapp ui @Arguments -w $script:App.Hwnd --json 2>&1
    $exitCode = $LASTEXITCODE
    $stdout = ($output | Where-Object { $_ -isnot [System.Management.Automation.ErrorRecord] }) -join "`n"
    $parsed = $null
    if ($stdout.Trim().StartsWith('{') -or $stdout.Trim().StartsWith('[')) {
        try { $parsed = $stdout | ConvertFrom-Json -Depth 100 } catch { $parsed = $null }
    }
    if ($exitCode -ne 0 -and -not $AllowFailure) {
        throw [UiTestFailure]::new("winapp ui $($Arguments -join ' ') が失敗しました: $(($output | Out-String).Trim())")
    }
    $parsed
}

<#
.SYNOPSIS
AutomationId（または名前）で要素を1つ特定し、以降の操作に使うセレクターを返す。
-Pane を付けるとそのペインの中だけを探す（2画面では同じIDが2組並ぶため）。
#>
function Resolve-UiElement {
    param(
        [Parameter(Mandatory)][string]$Selector,
        [int]$Pane = -1,
        [string]$Type,
        [int]$TimeoutMs = 5000
    )

    $arguments = @('search', $Selector)
    if ($Pane -ge 0) { $arguments += @('--root', "Pane$Pane") }
    if ($Type) { $arguments += @('--type', $Type) }

    $deadline = [DateTime]::Now.AddMilliseconds($TimeoutMs)
    do {
        $result = Invoke-WinappUi $arguments -AllowFailure
        if ($result -and $result.PSObject.Properties['matches']) {
            # 検索は部分一致のため、AutomationId・名前が完全に一致するものを優先する
            $exact = @($result.matches | Where-Object {
                ($_.PSObject.Properties['automationId'] -and $_.automationId -eq $Selector) -or
                ($_.PSObject.Properties['name'] -and $_.name -eq $Selector)
            })
            if ($exact.Count -ge 1) { return $exact[0].selector }
        }
        Start-Sleep -Milliseconds 150
    } while ([DateTime]::Now -lt $deadline)

    $scope = if ($Pane -ge 0) { " (Pane$Pane)" } else { '' }
    throw [UiTestFailure]::new("要素 '$Selector'$scope が見つかりませんでした。")
}

function Wait-UiElement {
    param(
        [Parameter(Mandatory)][string]$Selector,
        [int]$Pane = -1,
        [switch]$Gone,
        [int]$TimeoutMs = 5000
    )

    $arguments = @('wait-for', $Selector, '-t', $TimeoutMs)
    if ($Pane -ge 0) { $arguments += @('--root', "Pane$Pane") }
    if ($Gone) { $arguments += '--gone' }
    $result = Invoke-WinappUi $arguments -AllowFailure
    if (-not $result -or $result.timedOut) {
        $state = if ($Gone) { '消えません' } else { '現れません' }
        throw [UiTestFailure]::new("要素 '$Selector' が $TimeoutMs ms 以内に$state でした。")
    }
    $result
}

<#
.SYNOPSIS
スキャン中の表示（ツリーの⟳）が消えるまで待つ。検索・All Filesはキャッシュを引くため、
起動直後のスキャンが終わる前に確かめると結果が欠ける。
#>
function Wait-UiScanIdle {
    param([int]$TimeoutMs = 30000)
    Wait-UiElement '⟳' -Gone -TimeoutMs $TimeoutMs | Out-Null
}

function Invoke-UiElement {
    param(
        [Parameter(Mandatory)][string]$Selector,
        [int]$Pane = -1,
        [string]$Action
    )
    $slug = Resolve-UiElement $Selector -Pane $Pane
    $arguments = @('invoke', $slug)
    if ($Action) { $arguments += @('--action', $Action) }
    Invoke-WinappUi $arguments | Out-Null
}

function Invoke-UiClick {
    param(
        [Parameter(Mandatory)][string]$Selector,
        [int]$Pane = -1,
        [string]$Type,
        [switch]$Double,
        [switch]$Right
    )
    $slug = Resolve-UiElement $Selector -Pane $Pane -Type $Type
    $arguments = @('click', $slug)
    if ($Double) { $arguments += '--double' }
    if ($Right) { $arguments += '--right' }
    Invoke-WinappUi $arguments | Out-Null
}

function Set-UiValue {
    param(
        [Parameter(Mandatory)][string]$Selector,
        [Parameter(Mandatory)][AllowEmptyString()][string]$Value,
        [int]$Pane = -1
    )
    $slug = Resolve-UiElement $Selector -Pane $Pane
    Invoke-WinappUi @('set-value', $slug, $Value) | Out-Null
}

function Get-UiValue {
    param(
        [Parameter(Mandatory)][string]$Selector,
        [int]$Pane = -1
    )
    $slug = Resolve-UiElement $Selector -Pane $Pane
    $result = Invoke-WinappUi @('get-value', $slug)
    [string]$result.text
}

function Get-UiProperty {
    param(
        [Parameter(Mandatory)][string]$Selector,
        [Parameter(Mandatory)][string]$Property,
        [int]$Pane = -1
    )
    $slug = Resolve-UiElement $Selector -Pane $Pane
    $result = Invoke-WinappUi @('get-property', $slug, '--property', $Property)
    $result.properties.$Property
}

function Test-UiElementEnabled {
    param(
        [Parameter(Mandatory)][string]$Selector,
        [int]$Pane = -1
    )
    # 値は "True"/"False" の文字列で返る（$true と -eq で比べると空でない文字列は真になるため文字列で比べる）
    "$(Get-UiProperty $Selector -Property 'IsEnabled' -Pane $Pane)" -eq 'True'
}

function Test-UiElementExists {
    param(
        [Parameter(Mandatory)][string]$Selector,
        [int]$Pane = -1
    )
    try {
        Resolve-UiElement $Selector -Pane $Pane -TimeoutMs 0 | Out-Null
        $true
    }
    catch [UiTestFailure] {
        $false
    }
}

<#
.SYNOPSIS
キー入力を送る。WPFのショートカット（PreviewKeyDown）は修飾キーの状態を見るため、
既定の post-message では届かない。実際の入力として送る send-input を使う。
#>
function Send-UiKeys {
    param(
        [Parameter(Mandatory)][string]$Keys,
        [string]$Target,
        [int]$Pane = -1
    )
    $arguments = @('send-keys', $Keys, '--via', 'send-input')
    if ($Target) { $arguments += @('--target', (Resolve-UiElement $Target -Pane $Pane)) }
    Invoke-WinappUi $arguments | Out-Null
}

function Move-UiElementByDrag {
    param(
        [Parameter(Mandatory)][string]$From,
        [Parameter(Mandatory)][string]$To,
        [int]$FromPane = -1,
        [int]$ToPane = -1
    )
    $fromSlug = Resolve-UiElement $From -Pane $FromPane
    $toSlug = Resolve-UiElement $To -Pane $ToPane
    Invoke-WinappUi @('drag', $fromSlug, $toSlug, '--dwell-ms', '300') | Out-Null
}

function Save-UiScreenshot {
    param([Parameter(Mandatory)][string]$Path)
    Invoke-WinappUi @('screenshot', '--output', $Path) -AllowFailure | Out-Null
}

#endregion

#region 画面の状態の読み取り

function Get-UiTree {
    param([Parameter(Mandatory)][string]$Selector, [int]$Depth = 4)
    $result = Invoke-WinappUi @('inspect', $Selector, '--depth', $Depth)
    $result.windows[0].elements
}

function Find-UiNode {
    param($Nodes, [scriptblock]$Predicate)
    foreach ($node in @($Nodes)) {
        if ($null -eq $node) { continue }
        if (& $Predicate $node) { return $node }
        if ($node.PSObject.Properties['children']) {
            $found = Find-UiNode $node.children $Predicate
            if ($found) { return $found }
        }
    }
}

<#
.SYNOPSIS
ファイル一覧に表示中の行のファイル名を上から順に返す（行は仮想化されているため画面内の分だけ）。
#>
function Get-UiFileRows {
    param([int]$Pane = 0)
    $tree = Get-UiTree "Pane$Pane" -Depth 3
    $grid = Find-UiNode $tree { param($n) $n.PSObject.Properties['automationId'] -and $n.automationId -eq 'FileListDataGrid' }
    if (-not $grid -or -not $grid.PSObject.Properties['children']) { return @() }
    @($grid.children | Where-Object { $_.type -eq 'DataItem' } | ForEach-Object { $_.name })
}

<#
.SYNOPSIS
ファイル一覧の行が期待どおりになるまで待つ（一覧の更新はバックグラウンドで進むため）。
#>
function Wait-UiFileRows {
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]]$Expected,
        [int]$Pane = 0,
        [switch]$AnyOrder,
        [int]$TimeoutMs = 5000
    )
    $deadline = [DateTime]::Now.AddMilliseconds($TimeoutMs)
    do {
        $actual = @(Get-UiFileRows -Pane $Pane)
        $matched = if ($AnyOrder) {
            (@($actual | Sort-Object) -join '|') -eq (@($Expected | Sort-Object) -join '|')
        }
        else {
            ($actual -join '|') -eq ($Expected -join '|')
        }
        if ($matched) { return }
        Start-Sleep -Milliseconds 200
    } while ([DateTime]::Now -lt $deadline)

    throw [UiTestFailure]::new("ファイル一覧 (Pane$Pane) が期待と違います。`n  期待: $($Expected -join ', ')`n  実際: $($actual -join ', ')")
}

function Get-UiTabNames {
    param([int]$Pane = 0)
    $tree = Get-UiTree "Pane$Pane" -Depth 4
    $tabs = Find-UiNode $tree { param($n) $n.PSObject.Properties['automationId'] -and $n.automationId -eq 'TabItemsControl' }
    if (-not $tabs -or -not $tabs.PSObject.Properties['children']) { return @() }
    @($tabs.children | Where-Object { $_.type -eq 'DataItem' } | ForEach-Object { $_.name })
}

function Wait-UiTabCount {
    param(
        [Parameter(Mandatory)][int]$Expected,
        [int]$Pane = 0,
        [int]$TimeoutMs = 5000
    )
    $deadline = [DateTime]::Now.AddMilliseconds($TimeoutMs)
    do {
        $actual = @(Get-UiTabNames -Pane $Pane)
        if ($actual.Count -eq $Expected) { return }
        Start-Sleep -Milliseconds 200
    } while ([DateTime]::Now -lt $deadline)
    throw [UiTestFailure]::new("タブ数 (Pane$Pane) が期待と違います。期待: $Expected / 実際: $($actual.Count) ($($actual -join ', '))")
}

function Wait-UiValue {
    param(
        [Parameter(Mandatory)][string]$Selector,
        [Parameter(Mandatory)][AllowEmptyString()][string]$Expected,
        [int]$Pane = -1,
        [int]$TimeoutMs = 5000
    )
    $deadline = [DateTime]::Now.AddMilliseconds($TimeoutMs)
    do {
        $actual = Get-UiValue $Selector -Pane $Pane
        if ($actual -eq $Expected) { return }
        Start-Sleep -Milliseconds 200
    } while ([DateTime]::Now -lt $deadline)
    throw [UiTestFailure]::new("'$Selector' の値が期待と違います。`n  期待: $Expected`n  実際: $actual")
}

function Get-UiPaneCount {
    $count = 0
    while (Test-UiElementExists "Pane$count") { $count++ }
    $count
}

#endregion

#region アサーション

function Assert-UiEqual {
    param($Expected, $Actual, [string]$Message = '')
    if ("$Expected" -ne "$Actual") {
        throw [UiTestFailure]::new("$Message 期待: $Expected / 実際: $Actual".Trim())
    }
}

function Assert-UiTrue {
    param([bool]$Condition, [Parameter(Mandatory)][string]$Message)
    if (-not $Condition) { throw [UiTestFailure]::new($Message) }
}

#endregion

Export-ModuleMember -Function *-Ui*, Get-FixturePath, New-UiFixture, Invoke-WinappUi, Find-UiNode
