# Usurper Reborn — Auto-Update Check Script (Windows)
# Checks GitHub for new releases, backs up current install, and updates.
#
# Usage:
#   .\updatecheck.ps1                    # Check and update if available
#   .\updatecheck.ps1 -CheckOnly         # Just check, don't update
#   .\updatecheck.ps1 -Force             # Force re-download even if current
#   .\updatecheck.ps1 -InstallDir "C:\games\usurper"  # Custom install path
#
# Task Scheduler (check at 3am daily):
#   Action: powershell.exe
#   Arguments: -ExecutionPolicy Bypass -File "C:\usurper\updatecheck.ps1"
#
# Exit codes:
#   0 = Updated successfully
#   1 = Already up to date
#   2 = Error

param(
    [string]$InstallDir = "",
    [string]$Platform = "",
    [switch]$CheckOnly,
    [switch]$Force,
    [int]$MaxBackups = 5
)

$ErrorActionPreference = "Stop"

# ─── Configuration ───────────────────────────
if (-not $InstallDir) {
    # Auto-detect: script directory or current directory
    $InstallDir = if ($PSScriptRoot) { $PSScriptRoot } else { Get-Location }
}

# Auto-detect platform
if (-not $Platform) {
    if ([Environment]::Is64BitOperatingSystem) {
        $Platform = "Windows-x64"
    } else {
        $Platform = "Windows-x86"
    }
}

$BackupDir = Join-Path $InstallDir "backups"
$GameBinary = Join-Path $InstallDir "UsurperReborn.exe"
$VersionFile = Join-Path $InstallDir "version.txt"
$GitHubRepo = "binary-knight/usurper-reborn"
$GitHubAPI = "https://api.github.com/repos/$GitHubRepo/releases/latest"
$FallbackAPI = "http://usurper-reborn.net/api/releases/latest"

function Log($msg) {
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    Write-Host "$timestamp [updatecheck] $msg"
}

# ─── Get current version ─────────────────────
function Get-CurrentVersion {
    # Try version.txt first (written by updater) — read only the first line
    if (Test-Path $VersionFile) {
        $v = (Get-Content $VersionFile -TotalCount 1).Trim()
        if ($v) { return $v }
    }
    # Fall back to reading FileVersion from the exe
    if (Test-Path $GameBinary) {
        $info = (Get-Item $GameBinary).VersionInfo
        if ($info.FileVersion) { return $info.FileVersion.Trim() }
        if ($info.ProductVersion) { return $info.ProductVersion.Trim() }
    }
    return "unknown"
}

# ─── TLS setup (needed for Windows 7) ────────
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

# ─── Check GitHub for latest release ─────────
Log "Checking for updates..."

$currentVersion = Get-CurrentVersion
Log "Current version: $currentVersion"
Log "Platform: $Platform"

$releaseJson = $null
try {
    $headers = @{ "User-Agent" = "UsurperReborn-Updater" }
    $response = Invoke-RestMethod -Uri $GitHubAPI -Headers $headers -TimeoutSec 15
    $releaseJson = $response
} catch {
    Log "GitHub API failed, trying fallback..."
    try {
        $response = Invoke-RestMethod -Uri $FallbackAPI -TimeoutSec 15
        $releaseJson = $response
    } catch {
        Log "ERROR: Cannot reach update server: $_"
        exit 2
    }
}

$latestVersion = $releaseJson.tag_name -replace '^v', ''
$releaseName = $releaseJson.name

if (-not $latestVersion) {
    Log "ERROR: Could not parse latest version"
    exit 2
}

Log "Latest version: $latestVersion ($releaseName)"

# ─── Compare versions ────────────────────────
if ($currentVersion -eq $latestVersion -and -not $Force) {
    Log "Already up to date (v$currentVersion)"
    exit 1
}

if ($CheckOnly) {
    Log "Update available: v$currentVersion -> v$latestVersion"
    exit 0
}

# ─── Find download URL ───────────────────────
$asset = $releaseJson.assets | Where-Object {
    $_.name -like "*$Platform*" -and
    $_.name -like "*.zip" -and
    $_.name -notlike "*WezTerm*" -and
    $_.name -notlike "*Desktop*"
} | Select-Object -First 1

if (-not $asset) {
    Log "ERROR: No $Platform asset found in release $latestVersion"
    exit 2
}

$downloadUrl = $asset.browser_download_url
Log "Download URL: $downloadUrl"

# ─── Create backup ────────────────────────────
Log "Creating backup..."
if (-not (Test-Path $BackupDir)) { New-Item -ItemType Directory -Path $BackupDir | Out-Null }

$backupFile = Join-Path $BackupDir "usurper-v$currentVersion-$(Get-Date -Format 'yyyyMMdd_HHmmss').zip"

# Backup exe + dll (the core files)
$filesToBackup = @()
if (Test-Path $GameBinary) { $filesToBackup += $GameBinary }
$dllPath = Join-Path $InstallDir "UsurperReborn.dll"
if (Test-Path $dllPath) { $filesToBackup += $dllPath }

if ($filesToBackup.Count -gt 0) {
    Compress-Archive -Path $filesToBackup -DestinationPath $backupFile -Force
    Log "Backup saved: $backupFile"
}

# Clean old backups
$oldBackups = Get-ChildItem "$BackupDir\usurper-v*.zip" -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending |
    Select-Object -Skip $MaxBackups
foreach ($old in $oldBackups) {
    Remove-Item $old.FullName -Force
}
Log "Cleaned old backups (keeping last $MaxBackups)"

# ─── Kill running game process ────────────────
$gameProcess = Get-Process -Name "UsurperReborn" -ErrorAction SilentlyContinue
if ($gameProcess) {
    Log "Stopping UsurperReborn process..."
    $gameProcess | Stop-Process -Force
    Start-Sleep -Seconds 2
}

# ─── Download update ──────────────────────────
Log "Downloading v$latestVersion..."
$tempDir = Join-Path $env:TEMP "usurper-update-$(Get-Random)"
New-Item -ItemType Directory -Path $tempDir | Out-Null
$tempZip = Join-Path $tempDir "update.zip"

try {
    $wc = New-Object System.Net.WebClient
    $wc.DownloadFile($downloadUrl, $tempZip)
} catch {
    Log "ERROR: Download failed: $_"
    Remove-Item $tempDir -Recurse -Force -ErrorAction SilentlyContinue
    exit 2
}

$zipSize = (Get-Item $tempZip).Length / 1MB
Log "Downloaded: $([math]::Round($zipSize, 1))MB"

# ─── Extract update ───────────────────────────
Log "Extracting to $InstallDir..."
try {
    Expand-Archive -Path $tempZip -DestinationPath $InstallDir -Force
} catch {
    Log "ERROR: Extraction failed: $_"
    Remove-Item $tempDir -Recurse -Force -ErrorAction SilentlyContinue
    exit 2
}

# Write version file
$latestVersion | Out-File -FilePath $VersionFile -Encoding ASCII -NoNewline

# ─── Cleanup ──────────────────────────────────
Remove-Item $tempDir -Recurse -Force -ErrorAction SilentlyContinue

Log "Update complete: v$currentVersion -> v$latestVersion"
exit 0
