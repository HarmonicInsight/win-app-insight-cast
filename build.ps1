<#
.SYNOPSIS
    InsightCast release build script.
    Publishes the app, downloads FFmpeg, and optionally builds the Inno Setup installer.

.DESCRIPTION
    1. dotnet publish (self-contained, win-x64)
    2. Downloads FFmpeg release binaries
    3. Downloads Snipaste portable (screen capture tool)
    4. Verifies build output
    5. Optionally runs Inno Setup to create the installer
    6. Creates a ZIP archive of the installer for store distribution

.EXAMPLE
    .\build.ps1
    .\build.ps1 -SkipInstaller
    .\build.ps1 -FfmpegZipPath C:\downloads\ffmpeg-release.zip
#>
param(
    [switch]$SkipInstaller,
    [string]$FfmpegZipPath = ""
)

$ErrorActionPreference = "Stop"

$projectDir   = "$PSScriptRoot\InsightCast"
$publishDir   = "$PSScriptRoot\publish"
$ffmpegDir    = "$publishDir\ffmpeg"
$installerDir = "$PSScriptRoot\Installer"
$toolsDir     = "$PSScriptRoot\_build_tools"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Insight Training Studio Build Script" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

# ── Step 1: dotnet publish ────────────────────────────────────────────
Write-Host ""
Write-Host "[1/6] Publishing Insight Training Studio..." -ForegroundColor Yellow

if (Test-Path $publishDir) {
    Remove-Item $publishDir -Recurse -Force
}

dotnet publish $projectDir `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -o $publishDir `
    -p:PublishSingleFile=false `
    -p:IncludeNativeLibrariesForSelfExtract=false

if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: dotnet publish failed." -ForegroundColor Red
    exit 1
}

Write-Host "  Published to: $publishDir" -ForegroundColor Green

# ── Step 2: Download FFmpeg ───────────────────────────────────────────
Write-Host ""
Write-Host "[2/6] Setting up FFmpeg..." -ForegroundColor Yellow

$ffmpegBinDir = "$ffmpegDir\bin"

if (-not (Test-Path "$ffmpegBinDir\ffmpeg.exe")) {
    if ($FfmpegZipPath -and (Test-Path $FfmpegZipPath)) {
        Write-Host "  Using provided FFmpeg archive: $FfmpegZipPath"
    } else {
        # Download ffmpeg-release-essentials from gyan.dev
        $ffmpegUrl = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip"
        New-Item -ItemType Directory -Path $toolsDir -Force | Out-Null
        $FfmpegZipPath = "$toolsDir\ffmpeg-release-essentials.zip"

        if (-not (Test-Path $FfmpegZipPath)) {
            Write-Host "  Downloading FFmpeg from: $ffmpegUrl"
            Write-Host "  This may take a few minutes..."
            Invoke-WebRequest -Uri $ffmpegUrl -OutFile $FfmpegZipPath -UseBasicParsing
        } else {
            Write-Host "  Using cached download: $FfmpegZipPath"
        }
    }

    # Extract only ffmpeg.exe and ffprobe.exe
    Write-Host "  Extracting FFmpeg binaries..."
    New-Item -ItemType Directory -Path $ffmpegBinDir -Force | Out-Null

    $tempExtract = "$toolsDir\ffmpeg_extract"
    if (Test-Path $tempExtract) { Remove-Item $tempExtract -Recurse -Force }
    Expand-Archive -Path $FfmpegZipPath -DestinationPath $tempExtract -Force

    # Find ffmpeg.exe inside the extracted directory
    $ffmpegExe  = Get-ChildItem -Path $tempExtract -Recurse -Filter "ffmpeg.exe"  | Select-Object -First 1
    $ffprobeExe = Get-ChildItem -Path $tempExtract -Recurse -Filter "ffprobe.exe" | Select-Object -First 1

    if ($ffmpegExe) {
        Copy-Item $ffmpegExe.FullName  "$ffmpegBinDir\ffmpeg.exe"  -Force
        Write-Host "  Copied ffmpeg.exe" -ForegroundColor Green
    } else {
        Write-Host "  WARNING: ffmpeg.exe not found in archive!" -ForegroundColor Red
    }
    if ($ffprobeExe) {
        Copy-Item $ffprobeExe.FullName "$ffmpegBinDir\ffprobe.exe" -Force
        Write-Host "  Copied ffprobe.exe" -ForegroundColor Green
    }

    # Cleanup
    Remove-Item $tempExtract -Recurse -Force -ErrorAction SilentlyContinue
} else {
    Write-Host "  FFmpeg already present in publish directory." -ForegroundColor Green
}

# ── Step 3: Download Snipaste ────────────────────────────────────────
Write-Host ""
Write-Host "[3/6] Setting up Snipaste..." -ForegroundColor Yellow

$snipasteDir = "$publishDir\snipaste"

if (-not (Test-Path "$snipasteDir\Snipaste.exe")) {
    $snipasteUrl = "https://dl.snipaste.com/win-x64"
    New-Item -ItemType Directory -Path $toolsDir -Force | Out-Null
    $snipasteZipPath = "$toolsDir\snipaste-win-x64.zip"

    if (-not (Test-Path $snipasteZipPath)) {
        Write-Host "  Downloading Snipaste from: $snipasteUrl"
        Invoke-WebRequest -Uri $snipasteUrl -OutFile $snipasteZipPath -UseBasicParsing
    } else {
        Write-Host "  Using cached download: $snipasteZipPath"
    }

    Write-Host "  Extracting Snipaste..."
    $tempSnipaste = "$toolsDir\snipaste_extract"
    if (Test-Path $tempSnipaste) { Remove-Item $tempSnipaste -Recurse -Force }
    Expand-Archive -Path $snipasteZipPath -DestinationPath $tempSnipaste -Force

    # Find Snipaste.exe inside the extracted directory and copy the whole folder
    $snipasteExe = Get-ChildItem -Path $tempSnipaste -Recurse -Filter "Snipaste.exe" | Select-Object -First 1
    if ($snipasteExe) {
        $snipasteSourceDir = $snipasteExe.DirectoryName
        New-Item -ItemType Directory -Path $snipasteDir -Force | Out-Null
        Copy-Item "$snipasteSourceDir\*" $snipasteDir -Recurse -Force
        Write-Host "  Copied Snipaste to: $snipasteDir" -ForegroundColor Green
    } else {
        Write-Host "  WARNING: Snipaste.exe not found in archive!" -ForegroundColor Red
    }

    # Cleanup
    Remove-Item $tempSnipaste -Recurse -Force -ErrorAction SilentlyContinue
} else {
    Write-Host "  Snipaste already present in publish directory." -ForegroundColor Green
}

# ── Step 4: Verify output ────────────────────────────────────────────
Write-Host ""
Write-Host "[4/6] Verifying build output..." -ForegroundColor Yellow

$requiredFiles = @(
    "$publishDir\InsightCast.exe",
    "$ffmpegBinDir\ffmpeg.exe",
    "$snipasteDir\Snipaste.exe"
)

$allOk = $true
foreach ($f in $requiredFiles) {
    if (Test-Path $f) {
        $size = (Get-Item $f).Length / 1MB
        Write-Host ("  OK: {0} ({1:N1} MB)" -f (Split-Path $f -Leaf), $size) -ForegroundColor Green
    } else {
        Write-Host "  MISSING: $f" -ForegroundColor Red
        $allOk = $false
    }
}

if (-not $allOk) {
    Write-Host "ERROR: Some required files are missing." -ForegroundColor Red
    exit 1
}

# ── Step 5: Build Installer (optional) ───────────────────────────────
Write-Host ""
if ($SkipInstaller) {
    Write-Host "[5/6] Skipping installer build (-SkipInstaller)." -ForegroundColor Yellow
} else {
    Write-Host "[5/6] Building installer..." -ForegroundColor Yellow

    $issFile = "$installerDir\InsightCast.iss"
    if (-not (Test-Path $issFile)) {
        Write-Host "  WARNING: Inno Setup script not found: $issFile" -ForegroundColor Red
        Write-Host "  Skipping installer build." -ForegroundColor Yellow
    } else {
        # Try to find Inno Setup compiler
        $iscc = $null
        $isccPaths = @(
            "${env:LOCALAPPDATA}\Programs\Inno Setup 6\ISCC.exe",
            "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
            "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
            "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
        )
        foreach ($p in $isccPaths) {
            if (Test-Path $p) { $iscc = $p; break }
        }

        if ($iscc) {
            Write-Host "  Using Inno Setup: $iscc"
            & $iscc $issFile
            if ($LASTEXITCODE -eq 0) {
                Write-Host "  Installer built successfully!" -ForegroundColor Green
            } else {
                Write-Host "  WARNING: Installer build failed." -ForegroundColor Red
            }
        } else {
            Write-Host "  Inno Setup not found. Install from: https://jrsoftware.org/isinfo.php" -ForegroundColor Yellow
            Write-Host "  You can build the installer later with:" -ForegroundColor Yellow
            Write-Host "    ISCC.exe $issFile" -ForegroundColor White
        }
    }
}

# ── Step 6: Create ZIP for store distribution ────────────────────────
Write-Host ""
Write-Host "[6/6] Creating ZIP for store distribution..." -ForegroundColor Yellow

$outputDir = "$PSScriptRoot\Output"
$version = "1.0.3"
$installerExe = "$outputDir\InsightTrainingStudio_Setup_$version.exe"
$zipPath = "$outputDir\InsightTrainingStudio_$version.zip"

if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}

if (Test-Path $installerExe) {
    # Create ZIP containing the installer exe
    Compress-Archive -Path $installerExe -DestinationPath $zipPath -CompressionLevel Optimal
    $zipSize = (Get-Item $zipPath).Length / 1MB
    Write-Host ("  Created: {0} ({1:N1} MB)" -f (Split-Path $zipPath -Leaf), $zipSize) -ForegroundColor Green
} else {
    Write-Host "  Installer not found at: $installerExe" -ForegroundColor Yellow
    Write-Host "  Creating ZIP from publish folder instead..." -ForegroundColor Yellow
    Compress-Archive -Path "$publishDir\*" -DestinationPath $zipPath -CompressionLevel Optimal
    $zipSize = (Get-Item $zipPath).Length / 1MB
    Write-Host ("  Created: {0} ({1:N1} MB)" -f (Split-Path $zipPath -Leaf), $zipSize) -ForegroundColor Green
}

# ── Done ──────────────────────────────────────────────────────────────
Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Build complete!" -ForegroundColor Green
Write-Host "  Output: $publishDir" -ForegroundColor Cyan
if (Test-Path $zipPath) {
    Write-Host "  ZIP:    $zipPath" -ForegroundColor Cyan
}
Write-Host "========================================" -ForegroundColor Cyan
