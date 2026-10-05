#Requires -Version 7
<#
.SYNOPSIS
winapp CLI（winapp ui）でビルド済みのexeを操作するUIテストを実行する。

.DESCRIPTION
scenarios/*.ps1 を順に実行する。シナリオごとに使い捨てのフィクスチャ（フォルダ構成と
settings.json・キャッシュDBの置き場）を作り、PARALLELSCOPE_DATA_DIR で差し替えて起動するため、
実際のアプリデータには触れない。

実行中は実際のマウス・キーボード入力を使うので、PCを触らず、画面をロックしないこと。

.PARAMETER Filter
シナリオ名（ファイル名）の部分一致で絞り込む。

.PARAMETER NoBuild
dotnet build を省略する。

.PARAMETER KeepArtifacts
成功してもフィクスチャ・スクリーンショットを消さずに残す。
#>
param(
    [string]$Filter = '',
    [switch]$NoBuild,
    [switch]$KeepArtifacts
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path

if (-not (Get-Command winapp -ErrorAction SilentlyContinue)) {
    throw 'winapp CLI が見つかりません（winget install Microsoft.WinAppCli）。'
}

Import-Module (Join-Path $PSScriptRoot 'UiTest.psm1') -Force

if (-not $NoBuild) {
    Write-Host 'ビルド中...'
    dotnet build (Join-Path $repoRoot 'ParallelScope.csproj') -c Debug -v quiet -nologo | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'ビルドに失敗しました。' }
}

$runRoot = Join-Path ([System.IO.Path]::GetTempPath()) "ParallelScope.UiTests\$(Get-Date -Format 'yyyyMMdd-HHmmss')"
New-Item -ItemType Directory $runRoot -Force | Out-Null

$scenarioFiles = Get-ChildItem (Join-Path $PSScriptRoot 'scenarios') -Filter '*.ps1' |
    Where-Object { $_.BaseName -like "*$Filter*" } |
    Sort-Object Name

$results = [System.Collections.Generic.List[object]]::new()

foreach ($file in $scenarioFiles) {
    # シナリオファイルは $Scenario（Plus / Settings / Steps）を定義する
    $Scenario = $null
    . $file.FullName
    if (-not $Scenario) { throw "$($file.Name) が `$Scenario を定義していません。" }

    Write-Host ''
    Write-Host "== $($file.BaseName)" -ForegroundColor Cyan

    $settings = if ($Scenario.ContainsKey('Settings')) { $Scenario.Settings } else { @{} }
    $fixture = New-UiFixture -WorkRoot $runRoot -Settings $settings
    $failed = $false

    try {
        Start-UiApp -Fixture $fixture -Plus:([bool]$Scenario.Plus)
    }
    catch {
        Write-Host "  起動に失敗: $($_.Exception.Message)" -ForegroundColor Red
        $results.Add([pscustomobject]@{ Scenario = $file.BaseName; Step = '(起動)'; Result = 'FAIL'; Message = $_.Exception.Message })
        Stop-UiApp
        continue
    }

    # ステップは前のステップの状態を引き継ぐため、1つ失敗したら残りは飛ばす
    foreach ($step in $Scenario.Steps.GetEnumerator()) {
        if ($failed) {
            $results.Add([pscustomobject]@{ Scenario = $file.BaseName; Step = $step.Key; Result = 'SKIP'; Message = '' })
            Write-Host "  - $($step.Key)" -ForegroundColor DarkGray
            continue
        }

        $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
        try {
            & $step.Value
            $results.Add([pscustomobject]@{ Scenario = $file.BaseName; Step = $step.Key; Result = 'PASS'; Message = '' })
            Write-Host "  ✓ $($step.Key) ($($stopwatch.ElapsedMilliseconds) ms)" -ForegroundColor Green
        }
        catch {
            $failed = $true
            $screenshot = Join-Path $runRoot "$($file.BaseName)-failure.png"
            try { Save-UiScreenshot -Path $screenshot } catch { }
            $message = $_.Exception.Message
            $results.Add([pscustomobject]@{ Scenario = $file.BaseName; Step = $step.Key; Result = 'FAIL'; Message = $message })
            Write-Host "  ✗ $($step.Key)" -ForegroundColor Red
            Write-Host ($message -split "`n" | ForEach-Object { "      $_" } | Out-String).TrimEnd() -ForegroundColor Red
            if (Test-Path $screenshot) { Write-Host "      スクリーンショット: $screenshot" -ForegroundColor Red }
        }
    }

    Stop-UiApp
}

$passed = @($results | Where-Object Result -eq 'PASS').Count
$failedCount = @($results | Where-Object Result -eq 'FAIL').Count
$skipped = @($results | Where-Object Result -eq 'SKIP').Count

Write-Host ''
$color = if ($failedCount -eq 0) { 'Green' } else { 'Red' }
Write-Host "合格: $passed  失敗: $failedCount  スキップ: $skipped" -ForegroundColor $color

if ($failedCount -eq 0 -and -not $KeepArtifacts) {
    Remove-Item $runRoot -Recurse -Force -ErrorAction SilentlyContinue
}
else {
    Write-Host "成果物: $runRoot"
}

exit ([int]($failedCount -gt 0))
