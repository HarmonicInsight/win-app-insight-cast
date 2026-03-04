<#
.SYNOPSIS
    Excel ファイルからツール紹介動画のプロジェクト JSON を一括生成するスクリプト。

.DESCRIPTION
    tools.xlsx のシート「ツール一覧」を読み込み、1ツールあたり1プロジェクト JSON を生成。
    AI ナレーション・画像生成を活用して、スクリーンショットがなくても動画を自動作成できます。

.PARAMETER ExcelPath
    入力 Excel ファイルのパス（必須）。

.PARAMETER OutputDir
    プロジェクト JSON の出力先ディレクトリ（デフォルト: ./output/projects）。

.PARAMETER ImageDir
    スクリーンショット画像の格納ディレクトリ（デフォルト: ./images）。
    各ツール名のサブフォルダに画像を配置する想定。

.PARAMETER Resolution
    出力動画の解像度（デフォルト: 1920x1080）。

.PARAMETER SpeakerId
    VOICEVOX の話者 ID（デフォルト: 13 = 青山龍星）。

.PARAMETER NarrationStyle
    AI ナレーションのスタイル（デフォルト: educational）。

.PARAMETER GenerateBatch
    バッチ JSON も同時に生成する場合に指定。

.EXAMPLE
    .\Generate-ToolProjects.ps1 -ExcelPath .\tools.xlsx
    .\Generate-ToolProjects.ps1 -ExcelPath .\tools.xlsx -OutputDir .\my-output -GenerateBatch
#>

param(
    [Parameter(Mandatory = $true)]
    [string]$ExcelPath,

    [string]$OutputDir = "./output/projects",

    [string]$ImageDir = "./images",

    [string]$Resolution = "1920x1080",

    [int]$SpeakerId = 13,

    [string]$NarrationStyle = "educational",

    [switch]$GenerateBatch
)

# ── ImportExcel モジュールチェック ──
if (-not (Get-Module -ListAvailable -Name ImportExcel)) {
    Write-Host "[INFO] ImportExcel モジュールをインストールしています..." -ForegroundColor Cyan
    Install-Module -Name ImportExcel -Force -Scope CurrentUser
}
Import-Module ImportExcel

# ── Excel 読み込み ──
if (-not (Test-Path $ExcelPath)) {
    Write-Error "Excel ファイルが見つかりません: $ExcelPath"
    exit 1
}

$tools = Import-Excel -Path $ExcelPath -WorksheetName "ツール一覧"

if (-not $tools -or $tools.Count -eq 0) {
    Write-Error "シート「ツール一覧」にデータがありません。"
    exit 1
}

# ── 出力ディレクトリ作成 ──
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

Write-Host "========================================" -ForegroundColor Green
Write-Host " InsightCast ツール紹介動画 自動生成" -ForegroundColor Green
Write-Host " 対象ツール数: $($tools.Count)" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green

$projectFiles = @()
$sceneCounter = 0

foreach ($tool in $tools) {
    $toolName     = $tool.'ツール名'
    $category     = $tool.'カテゴリ'
    $summary      = $tool.'概要'
    $feature1     = $tool.'特徴1'
    $feature2     = $tool.'特徴2'
    $feature3     = $tool.'特徴3'
    $targetUser   = $tool.'対象ユーザー'
    $pricing      = $tool.'料金'
    $url          = $tool.'URL'
    $imageFolder  = $tool.'画像フォルダ'

    if ([string]::IsNullOrWhiteSpace($toolName)) { continue }

    Write-Host "`n[処理中] $toolName ($category)" -ForegroundColor Yellow

    # ── 画像の検出 ──
    $toolImageDir = if ($imageFolder) {
        $imageFolder
    } else {
        Join-Path $ImageDir ($toolName -replace '[\\/:*?"<>|]', '_')
    }

    $screenshots = @()
    if (Test-Path $toolImageDir) {
        $screenshots = Get-ChildItem -Path $toolImageDir -Include *.png, *.jpg, *.jpeg, *.bmp -Recurse |
            Sort-Object Name |
            Select-Object -First 5
        Write-Host "  画像: $($screenshots.Count) 枚検出" -ForegroundColor Gray
    } else {
        Write-Host "  画像: なし（AI 画像生成を使用）" -ForegroundColor Gray
    }

    # ── シーン構成 ──
    $scenes = @()

    # シーン1: タイトル画面
    $titleScene = @{
        id             = "scene-$('{0:D3}' -f (++$sceneCounter))"
        mediaPath      = $null
        mediaType      = "none"
        narrationText  = "${toolName}の紹介動画です。${category}カテゴリの便利なツールをご紹介します。"
        subtitleText   = "${toolName} - ${category}"
        speakerId      = $SpeakerId
        keepOriginalAudio = $false
        transitionType = "fade"
        transitionDuration = 0.5
        durationMode   = "auto"
        fixedSeconds   = 5.0
        speechSpeed    = 1.0
        textOverlays   = @(
            @{
                text       = $toolName
                xPercent   = 50.0
                yPercent   = 35.0
                fontSize   = 80
                fontBold   = $true
                textColor  = @(255, 255, 255)
                strokeColor = @(0, 0, 0)
                strokeWidth = 4
                alignment  = "center"
                opacity    = 1.0
            },
            @{
                text       = $category
                xPercent   = 50.0
                yPercent   = 55.0
                fontSize   = 48
                fontBold   = $false
                textColor  = @(200, 200, 200)
                strokeColor = @(0, 0, 0)
                strokeWidth = 2
                alignment  = "center"
                opacity    = 0.9
            }
        )
    }

    # タイトル画像: AI生成 or スクリーンショット
    if ($screenshots.Count -gt 0) {
        $titleScene.mediaPath = $screenshots[0].FullName
        $titleScene.mediaType = "image"
    } else {
        $titleScene.aiGeneration = @{
            generateNarration = $false
            generateImage     = $true
            imageDescription  = "${toolName}のロゴやインターフェースを表す、プロフェッショナルで洗練されたイメージ。${category}ツールとして認識できるデザイン。"
            imageStyle        = "photorealistic"
        }
    }
    $scenes += $titleScene

    # シーン2: 概要説明
    $overviewScene = @{
        id             = "scene-$('{0:D3}' -f (++$sceneCounter))"
        mediaPath      = if ($screenshots.Count -gt 1) { $screenshots[1].FullName } else { $null }
        mediaType      = if ($screenshots.Count -gt 1) { "image" } else { "none" }
        narrationText  = $null
        subtitleText   = $null
        speakerId      = $SpeakerId
        keepOriginalAudio = $false
        transitionType = "dissolve"
        transitionDuration = 0.5
        durationMode   = "auto"
        fixedSeconds   = 5.0
        speechSpeed    = 1.0
        textOverlays   = @()
        aiGeneration   = @{
            generateNarration        = $true
            narrationTopic           = "${toolName}の概要説明。${summary}"
            narrationStyle           = $NarrationStyle
            targetDurationSeconds    = 20
            additionalInstructions   = "初心者にも分かりやすく、${toolName}とは何か、何ができるツールなのかを簡潔に説明してください。$(if ($targetUser) { "対象ユーザーは${targetUser}です。" })"
            generateImage            = if ($screenshots.Count -le 1) { $true } else { $false }
            imageDescription         = "${toolName}のメイン画面やダッシュボードのイメージ。清潔感のあるUI。"
            imageStyle               = "photorealistic"
        }
    }
    $scenes += $overviewScene

    # シーン3-5: 特徴紹介
    $features = @($feature1, $feature2, $feature3) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    $featureIdx = 2  # screenshots index

    foreach ($feature in $features) {
        $hasScreenshot = ($featureIdx -lt $screenshots.Count)
        $featureScene = @{
            id             = "scene-$('{0:D3}' -f (++$sceneCounter))"
            mediaPath      = if ($hasScreenshot) { $screenshots[$featureIdx].FullName } else { $null }
            mediaType      = if ($hasScreenshot) { "image" } else { "none" }
            narrationText  = $null
            subtitleText   = $null
            speakerId      = $SpeakerId
            keepOriginalAudio = $false
            transitionType = "slide"
            transitionDuration = 0.5
            durationMode   = "auto"
            fixedSeconds   = 5.0
            speechSpeed    = 1.0
            textOverlays   = @(
                @{
                    text       = $feature
                    xPercent   = 50.0
                    yPercent   = 10.0
                    fontSize   = 56
                    fontBold   = $true
                    textColor  = @(255, 255, 255)
                    strokeColor = @(0, 0, 0)
                    strokeWidth = 3
                    alignment  = "center"
                    opacity    = 1.0
                }
            )
            aiGeneration   = @{
                generateNarration        = $true
                narrationTopic           = "${toolName}の特徴: ${feature}"
                narrationStyle           = $NarrationStyle
                targetDurationSeconds    = 15
                additionalInstructions   = "この機能のメリットや使い方を具体的に説明してください。"
                generateImage            = (-not $hasScreenshot)
                imageDescription         = "${toolName}の「${feature}」機能を表すスクリーンショット風のイメージ。"
                imageStyle               = "photorealistic"
            }
        }
        $featureIdx++
        $scenes += $featureScene
    }

    # シーン6: まとめ（料金・URL 情報）
    $closingNarration = "${toolName}のご紹介は以上です。"
    if ($pricing) { $closingNarration += "料金は${pricing}です。" }
    if ($url) { $closingNarration += "詳しくは公式サイトをご確認ください。" }

    $closingScene = @{
        id             = "scene-$('{0:D3}' -f (++$sceneCounter))"
        mediaPath      = $null
        mediaType      = "none"
        narrationText  = $closingNarration
        subtitleText   = "まとめ"
        speakerId      = $SpeakerId
        keepOriginalAudio = $false
        transitionType = "fade"
        transitionDuration = 1.0
        durationMode   = "auto"
        fixedSeconds   = 5.0
        speechSpeed    = 1.0
        textOverlays   = @(
            @{
                text       = "まとめ"
                xPercent   = 50.0
                yPercent   = 20.0
                fontSize   = 64
                fontBold   = $true
                textColor  = @(255, 255, 255)
                strokeColor = @(0, 0, 0)
                strokeWidth = 3
                alignment  = "center"
                opacity    = 1.0
            }
        )
    }
    if ($pricing) {
        $closingScene.textOverlays += @{
            text       = "料金: $pricing"
            xPercent   = 50.0
            yPercent   = 45.0
            fontSize   = 44
            fontBold   = $false
            textColor  = @(255, 255, 200)
            strokeColor = @(0, 0, 0)
            strokeWidth = 2
            alignment  = "center"
            opacity    = 1.0
        }
    }
    if ($url) {
        $closingScene.textOverlays += @{
            text       = $url
            xPercent   = 50.0
            yPercent   = 65.0
            fontSize   = 36
            fontBold   = $false
            textColor  = @(100, 200, 255)
            strokeColor = @(0, 0, 0)
            strokeWidth = 2
            alignment  = "center"
            opacity    = 1.0
        }
    }
    $scenes += $closingScene

    # ── プロジェクト JSON 構築 ──
    $project = [ordered]@{
        version                   = "1.0"
        projectPath               = $null
        output                    = [ordered]@{
            resolution = $Resolution
            fps        = 30
            outputPath = $null
        }
        settings                  = [ordered]@{
            voicevoxBaseUrl = "http://127.0.0.1:50021"
            voicevoxRunExe  = $null
            ffmpegPath      = $null
            fontPath        = $null
        }
        scenes                    = $scenes
        bgm                       = [ordered]@{
            filePath        = $null
            volume          = 0.25
            fadeInEnabled   = $true
            fadeInDuration  = 2.0
            fadeInType      = "linear"
            fadeOutEnabled  = $true
            fadeOutDuration = 3.0
            fadeOutType     = "linear"
            loopEnabled     = $true
            duckingEnabled  = $true
            duckingVolume   = 0.08
            duckingAttack   = 0.2
            duckingRelease  = 0.5
        }
        watermark                 = [ordered]@{
            imagePath = $null
            position  = "bottomRight"
            opacity   = 0.7
            scale     = 0.12
        }
        defaultTransition         = "fade"
        defaultTransitionDuration = 0.5
        generateThumbnail         = $true
        generateChapters          = $true
    }

    # ── JSON ファイル出力 ──
    $safeName = $toolName -replace '[\\/:*?"<>|]', '_'
    $jsonPath = Join-Path $OutputDir "${safeName}.json"
    $project | ConvertTo-Json -Depth 10 | Out-File -FilePath $jsonPath -Encoding UTF8

    Write-Host "  出力: $jsonPath (シーン数: $($scenes.Count))" -ForegroundColor Green
    $projectFiles += @{
        projectFile = $jsonPath
        outputName  = "${safeName}.mp4"
    }
}

# ── バッチ JSON 生成 ──
if ($GenerateBatch -and $projectFiles.Count -gt 0) {
    $batchConfig = [ordered]@{
        version        = "1.0"
        batchName      = "ツール紹介動画バッチ $(Get-Date -Format 'yyyy-MM-dd')"
        globalSettings = [ordered]@{
            outputDirectory   = "./output/videos"
            openaiApiKey      = '${OPENAI_API_KEY}'
            continueOnError   = $true
            parallelExecution = $false
        }
        projects       = $projectFiles
    }

    $batchPath = Join-Path $OutputDir "batch-tools.json"
    $batchConfig | ConvertTo-Json -Depth 10 | Out-File -FilePath $batchPath -Encoding UTF8
    Write-Host "`n[バッチ] $batchPath を生成しました ($($projectFiles.Count) プロジェクト)" -ForegroundColor Cyan
}

Write-Host "`n========================================" -ForegroundColor Green
Write-Host " 完了: $($projectFiles.Count) プロジェクト生成" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "次のステップ:" -ForegroundColor White
Write-Host "  1. InsightCast で個別プロジェクトを開いて確認" -ForegroundColor Gray
Write-Host "  2. バッチ実行で一括エクスポート:" -ForegroundColor Gray
Write-Host "     InsightCast.exe --batch `"$OutputDir\batch-tools.json`"" -ForegroundColor Gray
