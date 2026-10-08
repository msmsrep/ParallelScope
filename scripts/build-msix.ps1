<#
.SYNOPSIS
  Release発行 → MSIX化 → 署名 → MSIX/verX.Y.Z.W/ への配置を一括で行う。

.DESCRIPTION
  winapp CLI（https://github.com/microsoft/WinAppCli）を使う。
  - WPFの非パッケージ構成は winapp のプロジェクトモード（csprojを直接渡す）を受け付けないため、
    dotnet publish の出力フォルダを渡すフォルダモードで固める。
  - winapp package --cert はタイムスタンプを付けられないため、署名は winapp sign で別に行う
    （タイムスタンプが無いと証明書の期限切れと同時にパッケージも無効になる）。
  - PRIは生成しない（言語・スケール別のリソースを持たないため。従来のmakeappx手順と中身をそろえる）。

.PARAMETER NoSign
  署名しない。ストア提出用（ストア側で署名し直すため自己署名は不要）。

.PARAMETER OutputRoot
  verX.Y.Z.W フォルダを作る場所。既定はリポジトリ直下の MSIX（.gitignore対象）。

.PARAMETER Force
  同じバージョンのMSIXが既にあっても上書きする。

.EXAMPLE
  $env:PARALLELSCOPE_CERT_PASSWORD = '...'; .\scripts\build-msix.ps1
.EXAMPLE
  .\scripts\build-msix.ps1 -NoSign
#>
[CmdletBinding()]
param(
    [switch]$NoSign,
    [switch]$Force,
    [string]$OutputRoot = (Join-Path $PSScriptRoot '..\MSIX'),
    [string]$CertPath = (Join-Path $PSScriptRoot '..\.env\msmsrep.pfx'),
    [string]$TimestampUrl = 'http://timestamp.digicert.com'
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')

function Invoke-Step([string]$name, [scriptblock]$block) {
    Write-Host "==> $name" -ForegroundColor Cyan
    & $block
    if ($LASTEXITCODE -ne 0) { throw "$name に失敗しました（exit code $LASTEXITCODE）" }
}

if (-not (Get-Command winapp -ErrorAction SilentlyContinue)) {
    throw 'winapp CLI が見つかりません。winget install Microsoft.WinAppCli で導入してください。'
}

# バージョンの出どころはマニフェストだけ（AppVersionProviderも同じものを読む）
[xml]$manifest = Get-Content (Join-Path $root 'AppxManifest.xml')
$version = $manifest.Package.Identity.Version
$outDir = Join-Path $OutputRoot "ver$version"
$outFile = Join-Path $outDir 'ParallelScope.msix'

if ((Test-Path $outFile) -and -not $Force) {
    throw "$outFile は既にあります。AppxManifest.xml のバージョンを上げるか、-Force で上書きしてください。"
}

if (-not $NoSign) {
    if (-not (Test-Path $CertPath)) { throw "証明書が見つかりません: $CertPath" }
    if (-not $env:PARALLELSCOPE_CERT_PASSWORD) {
        throw '環境変数 PARALLELSCOPE_CERT_PASSWORD に証明書のパスワードを設定してください（ストア提出用なら -NoSign）。'
    }
}

$publishDir = Join-Path $root 'bin\Release\net10.0-windows10.0.19041.0\win-x64\publish'

# ReadyToRunの中間出力は入力が変わらない限り再利用される。一度でも別の設定（PublishReadyToRunComposite等）で
# 発行すると、その形式のDLLが残ったまま通常の発行に混ざり、起動直後にcoreclrごと落ちるパッケージができる
# （WinRT系DLLで実際に起きた）。毎回作り直して、発行物を設定どおりのものにそろえる
$r2rIntermediateDir = Join-Path $root 'obj\Release\net10.0-windows10.0.19041.0\win-x64\R2R'
if (Test-Path $r2rIntermediateDir) { Remove-Item $r2rIntermediateDir -Recurse -Force }

Invoke-Step 'dotnet publish' { dotnet publish (Join-Path $root 'ParallelScope.csproj') -c Release }

New-Item -ItemType Directory -Force $outDir | Out-Null
Invoke-Step 'winapp package' { winapp package $publishDir --no-sign --skip-pri --output $outFile }

if (-not $NoSign) {
    Invoke-Step 'winapp sign' {
        winapp sign $outFile $CertPath --password $env:PARALLELSCOPE_CERT_PASSWORD --timestamp $TimestampUrl
    }
}

$state = if ($NoSign) { '未署名' } else { '署名済み' }
Write-Host "完了: $outFile（v$version, $state）" -ForegroundColor Green
