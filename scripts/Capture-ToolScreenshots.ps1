<#
.SYNOPSIS
    Excel のツール一覧から URL を読み取り、各ツールのスクリーンショットを自動撮影。

.DESCRIPTION
    ブラウザ（Microsoft Edge）でツールの URL を開き、自動でスクリーンショットを撮影します。
    Selenium を使わず、Edge の --headless モードで簡単にキャプチャします。

    撮影モード:
      - url    : Excel の URL 列を使ってウェブページを撮影（デフォルト）
      - window : 指定プロセス名のウィンドウを撮影
      - manual : 手動で各ツールを起動 → Enter キーでキャプチャ

.PARAMETER ExcelPath
    入力 Excel ファイルのパス（必須）。

.PARAMETER OutputDir
    スクリーンショット出力ディレクトリ（デフォルト: ./images）。

.PARAMETER Mode
    撮影モード: url / window / manual（デフォルト: url）。

.PARAMETER Width
    スクリーンショット幅（デフォルト: 1920）。

.PARAMETER Height
    スクリーンショット高さ（デフォルト: 1080）。

.PARAMETER DelayMs
    ページ読み込み後の待機時間（ミリ秒、デフォルト: 3000）。

.EXAMPLE
    .\Capture-ToolScreenshots.ps1 -ExcelPath .\tools.xlsx
    .\Capture-ToolScreenshots.ps1 -ExcelPath .\tools.xlsx -Mode manual
    .\Capture-ToolScreenshots.ps1 -ExcelPath .\tools.xlsx -Width 1280 -Height 720
#>

param(
    [Parameter(Mandatory = $true)]
    [string]$ExcelPath,

    [string]$OutputDir = "./images",

    [ValidateSet("url", "window", "manual")]
    [string]$Mode = "url",

    [int]$Width = 1920,

    [int]$Height = 1080,

    [int]$DelayMs = 3000
)

# ── ImportExcel モジュールチェック ──
if (-not (Get-Module -ListAvailable -Name ImportExcel)) {
    Write-Host "[INFO] ImportExcel モジュールをインストールしています..." -ForegroundColor Cyan
    Install-Module -Name ImportExcel -Force -Scope CurrentUser
}
Import-Module ImportExcel

# ── Windows API（window モード用）──
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Drawing;
using System.Text;
public class ScreenCapture {
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    public static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    public static void CaptureWindow(IntPtr hWnd, string savePath) {
        SetForegroundWindow(hWnd);
        System.Threading.Thread.Sleep(500);
        RECT rect;
        GetWindowRect(hWnd, out rect);
        int w = rect.Right - rect.Left;
        int h = rect.Bottom - rect.Top;
        if (w <= 0 || h <= 0) return;
        var bmp = new Bitmap(w, h);
        var g = Graphics.FromImage(bmp);
        g.CopyFromScreen(rect.Left, rect.Top, 0, 0, new Size(w, h));
        bmp.Save(savePath, System.Drawing.Imaging.ImageFormat.Png);
        g.Dispose();
        bmp.Dispose();
    }

    public static void CaptureFullScreen(string savePath) {
        var bounds = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
        var bmp = new Bitmap(bounds.Width, bounds.Height);
        var g = Graphics.FromImage(bmp);
        g.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size);
        bmp.Save(savePath, System.Drawing.Imaging.ImageFormat.Png);
        g.Dispose();
        bmp.Dispose();
    }
}
"@ -ReferencedAssemblies System.Drawing, System.Windows.Forms

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

Write-Host "========================================" -ForegroundColor Green
Write-Host " ツールスクリーンショット自動撮影" -ForegroundColor Green
Write-Host " モード: $Mode" -ForegroundColor Green
Write-Host " 対象ツール数: $($tools.Count)" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green

# ── Edge パス検出 ──
function Find-EdgePath {
    $candidates = @(
        "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe",
        "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe",
        "$env:LocalAppData\Microsoft\Edge\Application\msedge.exe"
    )
    foreach ($p in $candidates) {
        if (Test-Path $p) { return $p }
    }
    # PATH から探す
    $found = Get-Command msedge.exe -ErrorAction SilentlyContinue
    if ($found) { return $found.Source }
    return $null
}

$capturedCount = 0

foreach ($tool in $tools) {
    $toolName = $tool.'ツール名'
    $url = $tool.'URL'

    if ([string]::IsNullOrWhiteSpace($toolName)) { continue }

    $safeName = $toolName -replace '[\\/:*?"<>|]', '_'
    $toolDir = Join-Path $OutputDir $safeName
    New-Item -ItemType Directory -Force -Path $toolDir | Out-Null

    Write-Host "`n[撮影] $toolName" -ForegroundColor Yellow

    switch ($Mode) {
        "url" {
            if ([string]::IsNullOrWhiteSpace($url)) {
                Write-Host "  スキップ: URL が未設定" -ForegroundColor Gray
                continue
            }

            $edgePath = Find-EdgePath
            if (-not $edgePath) {
                Write-Warning "Microsoft Edge が見つかりません。Chrome または別のブラウザで代替してください。"
                continue
            }

            # メインページ撮影
            $screenshotPath = Join-Path $toolDir "01_main.png"
            Write-Host "  URL: $url" -ForegroundColor Gray

            $tempDir = Join-Path $env:TEMP "insightcast-capture-$safeName"
            New-Item -ItemType Directory -Force -Path $tempDir | Out-Null

            try {
                $args = @(
                    "--headless=new",
                    "--disable-gpu",
                    "--no-sandbox",
                    "--window-size=${Width},${Height}",
                    "--screenshot=$screenshotPath",
                    "--hide-scrollbars",
                    "--user-data-dir=$tempDir",
                    $url
                )
                $process = Start-Process -FilePath $edgePath -ArgumentList $args -PassThru -Wait -NoNewWindow
                if (Test-Path $screenshotPath) {
                    Write-Host "  保存: $screenshotPath" -ForegroundColor Green
                    $capturedCount++
                } else {
                    Write-Host "  失敗: スクリーンショットが生成されませんでした" -ForegroundColor Red
                }
            }
            catch {
                Write-Host "  エラー: $($_.Exception.Message)" -ForegroundColor Red
            }
            finally {
                Start-Sleep -Milliseconds 500
                Remove-Item -Path $tempDir -Recurse -Force -ErrorAction SilentlyContinue
            }
        }

        "window" {
            $processName = $tool.'プロセス名'
            if ([string]::IsNullOrWhiteSpace($processName)) {
                Write-Host "  スキップ: プロセス名が未設定（Excel の「プロセス名」列を追加してください）" -ForegroundColor Gray
                continue
            }

            $proc = Get-Process -Name $processName -ErrorAction SilentlyContinue | Select-Object -First 1
            if (-not $proc) {
                Write-Host "  スキップ: プロセス '$processName' が実行されていません" -ForegroundColor Gray
                continue
            }

            $screenshotPath = Join-Path $toolDir "01_main.png"
            try {
                [ScreenCapture]::CaptureWindow($proc.MainWindowHandle, $screenshotPath)
                if (Test-Path $screenshotPath) {
                    Write-Host "  保存: $screenshotPath" -ForegroundColor Green
                    $capturedCount++
                }
            }
            catch {
                Write-Host "  エラー: $($_.Exception.Message)" -ForegroundColor Red
            }
        }

        "manual" {
            Write-Host "  $toolName を画面に表示してから Enter キーを押してください..." -ForegroundColor Cyan

            for ($i = 1; $i -le 3; $i++) {
                $label = switch ($i) {
                    1 { "メイン画面" }
                    2 { "機能画面1" }
                    3 { "機能画面2" }
                }
                Write-Host "    [$i/3] $label をキャプチャ → Enter (s でスキップ): " -NoNewline
                $input = Read-Host

                if ($input -eq 's') {
                    Write-Host "    スキップ" -ForegroundColor Gray
                    continue
                }

                $screenshotPath = Join-Path $toolDir "$('{0:D2}' -f $i)_${label}.png"
                try {
                    [ScreenCapture]::CaptureFullScreen($screenshotPath)
                    Write-Host "    保存: $screenshotPath" -ForegroundColor Green
                    $capturedCount++
                }
                catch {
                    Write-Host "    エラー: $($_.Exception.Message)" -ForegroundColor Red
                }
            }
        }
    }
}

Write-Host "`n========================================" -ForegroundColor Green
Write-Host " 完了: $capturedCount 枚撮影" -ForegroundColor Green
Write-Host " 保存先: $OutputDir" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "次のステップ:" -ForegroundColor White
Write-Host "  Generate-ToolProjects.ps1 を実行して動画プロジェクトを生成" -ForegroundColor Gray
Write-Host "  .\Generate-ToolProjects.ps1 -ExcelPath $ExcelPath -ImageDir $OutputDir -GenerateBatch" -ForegroundColor Gray
