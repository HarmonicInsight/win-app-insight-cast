<#
.SYNOPSIS
    ツール紹介動画用の Excel 雛形ファイルを生成します。

.DESCRIPTION
    「ツール一覧」シートに必要な列ヘッダーとサンプルデータを含む
    Excel ファイル（.xlsx）を作成します。
    サンプルデータ付きで生成されるので、すぐに編集を始められます。

.PARAMETER OutputPath
    出力 Excel ファイルのパス（デフォルト: ./tools-template.xlsx）。

.PARAMETER IncludeSamples
    サンプルデータを含める（デフォルト: $true）。

.EXAMPLE
    .\New-ToolTemplate.ps1
    .\New-ToolTemplate.ps1 -OutputPath .\my-tools.xlsx
    .\New-ToolTemplate.ps1 -IncludeSamples:$false
#>

param(
    [string]$OutputPath = "./tools-template.xlsx",
    [bool]$IncludeSamples = $true
)

# ── ImportExcel モジュールチェック ──
if (-not (Get-Module -ListAvailable -Name ImportExcel)) {
    Write-Host "[INFO] ImportExcel モジュールをインストールしています..." -ForegroundColor Cyan
    Install-Module -Name ImportExcel -Force -Scope CurrentUser
}
Import-Module ImportExcel

# ── テンプレートデータ作成 ──
$templateData = @()

if ($IncludeSamples) {
    $templateData = @(
        [PSCustomObject]@{
            'ツール名'     = 'Slack'
            'カテゴリ'     = 'コミュニケーション'
            '概要'         = 'チーム向けビジネスチャットツール。チャンネルベースのメッセージングで情報を整理。'
            '特徴1'        = 'チャンネルで話題を整理'
            '特徴2'        = 'ファイル共有・検索が簡単'
            '特徴3'        = '2000以上の外部サービス連携'
            '対象ユーザー' = 'チームでのコミュニケーションを効率化したい企業'
            '料金'         = '無料プランあり / Pro $8.75/月'
            'URL'          = 'https://slack.com'
            '画像フォルダ' = ''
            'プロセス名'   = 'slack'
        },
        [PSCustomObject]@{
            'ツール名'     = 'Notion'
            'カテゴリ'     = 'ナレッジ管理'
            '概要'         = 'ドキュメント・Wiki・プロジェクト管理を一つにまとめたオールインワンツール。'
            '特徴1'        = '柔軟なドキュメント作成'
            '特徴2'        = 'データベース機能でタスク管理'
            '特徴3'        = 'テンプレートで素早く開始'
            '対象ユーザー' = 'ナレッジ管理やプロジェクト管理を統合したいチーム'
            '料金'         = '無料プランあり / Plus $10/月'
            'URL'          = 'https://www.notion.so'
            '画像フォルダ' = ''
            'プロセス名'   = 'Notion'
        },
        [PSCustomObject]@{
            'ツール名'     = 'Visual Studio Code'
            'カテゴリ'     = '開発ツール'
            '概要'         = 'Microsoft製の軽量で高機能なコードエディタ。拡張機能で無限にカスタマイズ可能。'
            '特徴1'        = 'IntelliSense で賢い補完'
            '特徴2'        = '豊富な拡張機能マーケット'
            '特徴3'        = 'Git 統合でバージョン管理'
            '対象ユーザー' = 'ソフトウェア開発者・Web開発者'
            '料金'         = '無料'
            'URL'          = 'https://code.visualstudio.com'
            '画像フォルダ' = ''
            'プロセス名'   = 'Code'
        }
    )
} else {
    # ヘッダーのみ（空行1行）
    $templateData = @(
        [PSCustomObject]@{
            'ツール名'     = ''
            'カテゴリ'     = ''
            '概要'         = ''
            '特徴1'        = ''
            '特徴2'        = ''
            '特徴3'        = ''
            '対象ユーザー' = ''
            '料金'         = ''
            'URL'          = ''
            '画像フォルダ' = ''
            'プロセス名'   = ''
        }
    )
}

# ── Excel ファイル出力 ──
$excelParams = @{
    Path          = $OutputPath
    WorksheetName = 'ツール一覧'
    AutoSize      = $true
    AutoFilter    = $true
    FreezeTopRow  = $true
    BoldTopRow    = $true
    TableStyle    = 'Medium2'
}

$templateData | Export-Excel @excelParams

# ── 説明シートを追加 ──
$instructions = @(
    [PSCustomObject]@{
        '項目'   = 'ツール名'
        '説明'   = '紹介するツールの名前（必須）'
        '例'     = 'Slack, Notion, VS Code'
    },
    [PSCustomObject]@{
        '項目'   = 'カテゴリ'
        '説明'   = 'ツールのカテゴリ分類'
        '例'     = 'コミュニケーション, 開発ツール, デザイン'
    },
    [PSCustomObject]@{
        '項目'   = '概要'
        '説明'   = 'ツールの簡単な説明文（AI ナレーション生成のベースになります）'
        '例'     = 'チーム向けビジネスチャットツール'
    },
    [PSCustomObject]@{
        '項目'   = '特徴1〜3'
        '説明'   = 'ツールの主要な特徴（各特徴が1シーンになります）'
        '例'     = 'チャンネルで話題を整理'
    },
    [PSCustomObject]@{
        '項目'   = '対象ユーザー'
        '説明'   = '想定ユーザー（AI ナレーションの語り口に影響）'
        '例'     = 'チームでのコミュニケーションを効率化したい企業'
    },
    [PSCustomObject]@{
        '項目'   = '料金'
        '説明'   = '料金情報（まとめシーンに表示）'
        '例'     = '無料プランあり / Pro $8.75/月'
    },
    [PSCustomObject]@{
        '項目'   = 'URL'
        '説明'   = '公式サイト URL（スクリーンショット自動撮影・まとめシーンに使用）'
        '例'     = 'https://slack.com'
    },
    [PSCustomObject]@{
        '項目'   = '画像フォルダ'
        '説明'   = 'スクリーンショット格納フォルダパス（空欄なら AI 画像生成）'
        '例'     = './images/Slack'
    },
    [PSCustomObject]@{
        '項目'   = 'プロセス名'
        '説明'   = 'ウィンドウキャプチャ用のプロセス名（window モード時のみ使用）'
        '例'     = 'slack, Notion, Code'
    }
)

$instructionParams = @{
    Path          = $OutputPath
    WorksheetName = '入力ガイド'
    AutoSize      = $true
    FreezeTopRow  = $true
    BoldTopRow    = $true
    TableStyle    = 'Medium6'
}

$instructions | Export-Excel @instructionParams

# ── ワークフローシートを追加 ──
$workflow = @(
    [PSCustomObject]@{
        'ステップ' = '1'
        '操作'     = 'Excel 雛形を編集'
        'コマンド' = '「ツール一覧」シートにツール情報を入力'
        '備考'     = '1行 = 1ツール = 1動画'
    },
    [PSCustomObject]@{
        'ステップ' = '2'
        '操作'     = 'スクリーンショット撮影（任意）'
        'コマンド' = '.\Capture-ToolScreenshots.ps1 -ExcelPath .\tools.xlsx'
        '備考'     = 'URL 列があれば自動撮影。なければ AI 画像生成。'
    },
    [PSCustomObject]@{
        'ステップ' = '3'
        '操作'     = 'プロジェクト JSON 生成'
        'コマンド' = '.\Generate-ToolProjects.ps1 -ExcelPath .\tools.xlsx -GenerateBatch'
        '備考'     = 'AI ナレーション設定付きの JSON が自動生成される'
    },
    [PSCustomObject]@{
        'ステップ' = '4'
        '操作'     = '動画一括エクスポート'
        'コマンド' = 'InsightCast.exe --batch .\output\projects\batch-tools.json'
        '備考'     = 'VOICEVOX + FFmpeg で動画を自動生成'
    }
)

$workflowParams = @{
    Path          = $OutputPath
    WorksheetName = 'ワークフロー'
    AutoSize      = $true
    FreezeTopRow  = $true
    BoldTopRow    = $true
    TableStyle    = 'Medium9'
}

$workflow | Export-Excel @workflowParams

Write-Host "========================================" -ForegroundColor Green
Write-Host " Excel 雛形を生成しました" -ForegroundColor Green
Write-Host " ファイル: $OutputPath" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "シート構成:" -ForegroundColor White
Write-Host "  1. ツール一覧  - ツール情報を入力（$(if ($IncludeSamples) { 'サンプル3件入り' } else { '空' })）" -ForegroundColor Gray
Write-Host "  2. 入力ガイド  - 各列の説明" -ForegroundColor Gray
Write-Host "  3. ワークフロー - 動画作成の手順" -ForegroundColor Gray
