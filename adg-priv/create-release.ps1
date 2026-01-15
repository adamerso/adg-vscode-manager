# GitHub Release Script for ADG VSCode Manager
# Requires: GITHUB_TOKEN environment variable

param(
    [Parameter(Mandatory=$true)]
    [string]$Version,
    
    [string]$Token = $env:GITHUB_TOKEN,
    
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"

$Owner = "adamerso"
$Repo = "adg-vscode-manager"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

# Check token
if (-not $Token) {
    Write-Host "ERROR: GITHUB_TOKEN environment variable not set" -ForegroundColor Red
    Write-Host "Set it with: `$env:GITHUB_TOKEN = 'your_token'" -ForegroundColor Yellow
    exit 1
}

# Headers for GitHub API
$Headers = @{
    "Authorization" = "Bearer $Token"
    "Accept" = "application/vnd.github+json"
    "X-GitHub-Api-Version" = "2022-11-28"
}

# Build files to upload
$Assets = @(
    @{ Path = Join-Path $ScriptDir "x64\AdgVscodeManager-win-x64.exe"; Name = "AdgVscodeManager-win-x64.exe" }
    @{ Path = Join-Path $ScriptDir "x86\AdgVscodeManager-win-x86.exe"; Name = "AdgVscodeManager-win-x86.exe" }
    @{ Path = Join-Path $ScriptDir "arm64\AdgVscodeManager-win-arm64.exe"; Name = "AdgVscodeManager-win-arm64.exe" }
)

# Verify assets exist
Write-Host "`nChecking assets..." -ForegroundColor Cyan
foreach ($asset in $Assets) {
    if (-not (Test-Path $asset.Path)) {
        Write-Host "ERROR: Asset not found: $($asset.Path)" -ForegroundColor Red
        exit 1
    }
    $size = (Get-Item $asset.Path).Length / 1MB
    Write-Host "  OK: $($asset.Name) ($([math]::Round($size, 2)) MB)"
}

# Release body
$Body = @"
## 🚀 ADG VSCode Portable Manager v$Version

Zero-effort update manager for portable VSCode Insiders (and Release).

### ✨ What's New in v$Version

**Improved update detection for VSCode Insiders:**

- **Smart GitHub tag fetching**: First checks 100 tags, extends to 800 if installed commit not found
- **Exe age fallback**: If commit not found in 800 tags but exe is >7 days old → update available
- **Better old installation support**: Properly detects updates even for month-old Insiders builds

### 🔧 Technical Changes

- Paginated GitHub API calls for tags (100 → 800 on demand)
- Added ``GetVscodeExeAgeDays()`` for fallback update detection  
- Releases pagination: up to 250 releases for detailed changelog
- Increased cache limit to 10,000 entries
- New constants: ``GitHubTagsInitialFetch``, ``GitHubTagsExtendedFetch``, ``ExeAgeThresholdDays``

### 💾 Installation

1. Download the exe for your architecture below
2. Place it in a folder (e.g., ``C:\Tools\VSCode\``)
3. Run it - it will offer to download VSCode for you

### 📁 Expected structure

``````
your-folder/
├── vscode-portable-insiders/     ← Your VSCode
├── vscode-portable-insiders-manager-data/  ← Manager's data (auto-created)
└── AdgVscodeManager.exe          ← Place here!
``````

### ⚙️ Requirements

- Windows 10/11
- No additional dependencies (self-contained .NET 8.0)

### 📦 Downloads

| Architecture | File |
|--------------|------|
| 64-bit Intel/AMD | ``AdgVscodeManager-win-x64.exe`` |
| 32-bit Intel/AMD | ``AdgVscodeManager-win-x86.exe`` |
| 64-bit ARM | ``AdgVscodeManager-win-arm64.exe`` |

Full documentation: [README](https://github.com/$Owner/$Repo#readme)
"@

Write-Host "`n=== Creating GitHub Release v$Version ===" -ForegroundColor Cyan

if ($DryRun) {
    Write-Host "[DRY RUN] Would create release with body:" -ForegroundColor Yellow
    Write-Host $Body
    exit 0
}

# Create release
$ReleaseData = @{
    tag_name = $Version
    name = "ADG VSCode Portable Manager v$Version"
    body = $Body
    draft = $false
    prerelease = $false
} | ConvertTo-Json

Write-Host "Creating release..." -ForegroundColor Green

try {
    $Release = Invoke-RestMethod -Uri "https://api.github.com/repos/$Owner/$Repo/releases" `
        -Method Post -Headers $Headers -Body $ReleaseData -ContentType "application/json"
    
    Write-Host "Release created: $($Release.html_url)" -ForegroundColor Green
    $ReleaseId = $Release.id
    $UploadUrl = $Release.upload_url -replace '\{.*\}', ''
}
catch {
    Write-Host "ERROR creating release: $_" -ForegroundColor Red
    exit 1
}

# Upload assets
Write-Host "`nUploading assets..." -ForegroundColor Cyan

foreach ($asset in $Assets) {
    Write-Host "  Uploading $($asset.Name)..." -NoNewline
    
    try {
        $FileBytes = [System.IO.File]::ReadAllBytes($asset.Path)
        $UploadHeaders = @{
            "Authorization" = "Bearer $Token"
            "Accept" = "application/vnd.github+json"
            "Content-Type" = "application/octet-stream"
        }
        
        $UploadUri = "${UploadUrl}?name=$($asset.Name)"
        
        $Result = Invoke-RestMethod -Uri $UploadUri -Method Post -Headers $UploadHeaders -Body $FileBytes
        
        Write-Host " Done ($($Result.size / 1MB) MB)" -ForegroundColor Green
    }
    catch {
        Write-Host " FAILED: $_" -ForegroundColor Red
    }
}

Write-Host "`n=== Release Complete ===" -ForegroundColor Green
Write-Host "URL: $($Release.html_url)"
