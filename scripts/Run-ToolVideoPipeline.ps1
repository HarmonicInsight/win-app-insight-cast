<#
.SYNOPSIS
    ツール紹介動画の自動生成パイプラインを一括実行します。

.DESCRIPTION
    以下のステップを順番に実行:
      1. Excel 雛形の生成（存在しない場合）
      2. スクリーンショットの自動撮影（URL がある場合）
      3. プロジェクト JSON の一括生成 + バッチ JSON 生成
      4. InsightCast ヘッドレスモードで動画一括エクスポート

    各ステップは個別にスキップ可能です。

.PARAMETER ExcelPath
    ツール一覧 Excel ファイルのパス（デフォルト: ./tools.xlsx）。

.PARAMETER SkipCapture
    スクリーンショット撮影をスキップ。

.PARAMETER SkipExport
    動画エクスポートをスキップ（JSON 生成まで）。

.PARAMETER InsightCastPath
    InsightCast.exe のパス（デフォルト: 自動検出）。

.PARAMETER Resolution
    出力動画の解像度（デフォルト: 1920x1080）。

.EXAMPLE
    .\Run-ToolVideoPipeline.ps1
    .\Run-ToolVideoPipeline.ps1 -ExcelPath .\my-tools.xlsx -SkipCapture
    .\Run-ToolVideoPipeline.ps1 -SkipExport
#>

param(
    [string]$ExcelPath = "./tools.xlsx",
    [switch]$SkipCapture,
    [switch]$SkipExport,
    [string]$InsightCastPath = "",
    [string]$Resolution = "1920x1080"
)

$ErrorActionPreference = "Stop"
$scriptDir = $PSScriptRoot

Write-Host ""
Write-Host "================================================" -ForegroundColor Cyan
Write-Host "  InsightCast ツール紹介動画 自動生成パイプライン" -ForegroundColor Cyan
Write-Host "================================================" -ForegroundColor Cyan
Write-Host ""

# ── ステップ 0: Excel 雛形チェック ──
if (-not (Test-Path $ExcelPath)) {
    Write-Host "[ステップ 0] Excel 雛形を生成します..." -ForegroundColor Yellow
    & "$scriptDir\New-ToolTemplate.ps1" -OutputPath $ExcelPath
    Write-Host ""
    Write-Host "Excel 雛形が生成されました: $ExcelPath" -ForegroundColor Green
    Write-Host "ツール情報を入力してから、このスクリプトを再実行してください。" -ForegroundColor White
    Write-Host ""
    exit 0
}

Write-Host "[確認] Excel ファイル: $ExcelPath" -ForegroundColor Green

# ── ステップ 1: スクリーンショット撮影 ──
$imageDir = "./images"
if (-not $SkipCapture) {
    Write-Host ""
    Write-Host "[ステップ 1] スクリーンショットを撮影します..." -ForegroundColor Yellow
    & "$scriptDir\Capture-ToolScreenshots.ps1" -ExcelPath $ExcelPath -OutputDir $imageDir
} else {
    Write-Host ""
    Write-Host "[ステップ 1] スクリーンショット撮影をスキップ" -ForegroundColor Gray
}

# ── ステップ 2: プロジェクト JSON + バッチ JSON 生成 ──
$projectDir = "./output/projects"
Write-Host ""
Write-Host "[ステップ 2] プロジェクト JSON を生成します..." -ForegroundColor Yellow
& "$scriptDir\Generate-ToolProjects.ps1" `
    -ExcelPath $ExcelPath `
    -OutputDir $projectDir `
    -ImageDir $imageDir `
    -Resolution $Resolution `
    -GenerateBatch

# ── ステップ 3: 動画一括エクスポート ──
$batchJsonPath = Join-Path $projectDir "batch-tools.json"

if ($SkipExport) {
    Write-Host ""
    Write-Host "[ステップ 3] 動画エクスポートをスキップ" -ForegroundColor Gray
    Write-Host ""
    Write-Host "手動で実行する場合:" -ForegroundColor White
    Write-Host "  InsightCast.exe --batch `"$batchJsonPath`"" -ForegroundColor Gray
} else {
    # InsightCast.exe の検出
    if ([string]::IsNullOrWhiteSpace($InsightCastPath)) {
        $candidates = @(
            ".\publish\InsightCast.exe",
            ".\InsightCast\bin\Release\net8.0-windows\InsightCast.exe",
            ".\InsightCast\bin\Debug\net8.0-windows\InsightCast.exe",
            "$env:LocalAppData\InsightCast\InsightCast.exe"
        )
        foreach ($c in $candidates) {
            if (Test-Path $c) {
                $InsightCastPath = $c
                break
            }
        }
    }

    if ([string]::IsNullOrWhiteSpace($InsightCastPath) -or -not (Test-Path $InsightCastPath)) {
        Write-Host ""
        Write-Host "[ステップ 3] InsightCast.exe が見つかりません" -ForegroundColor Red
        Write-Host "  --InsightCastPath パラメータでパスを指定するか、先にビルドしてください:" -ForegroundColor Yellow
        Write-Host "  .\build.ps1" -ForegroundColor Gray
        Write-Host ""
        Write-Host "ビルド後に以下を実行:" -ForegroundColor White
        Write-Host "  InsightCast.exe --batch `"$batchJsonPath`"" -ForegroundColor Gray
    } else {
        Write-Host ""
        Write-Host "[ステップ 3] 動画を一括エクスポートします..." -ForegroundColor Yellow
        Write-Host "  InsightCast: $InsightCastPath" -ForegroundColor Gray
        Write-Host "  バッチ設定: $batchJsonPath" -ForegroundColor Gray
        Write-Host ""

        & $InsightCastPath --batch $batchJsonPath
        $exitCode = $LASTEXITCODE

        if ($exitCode -eq 0) {
            Write-Host ""
            Write-Host "================================================" -ForegroundColor Green
            Write-Host "  全ての動画が正常にエクスポートされました！" -ForegroundColor Green
            Write-Host "  出力先: ./output/videos/" -ForegroundColor Green
            Write-Host "================================================" -ForegroundColor Green
        } else {
            Write-Host ""
            Write-Host "一部の動画でエラーが発生しました（終了コード: $exitCode）" -ForegroundColor Red
        }
    }
}

Write-Host ""
