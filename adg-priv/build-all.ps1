# ADG VSCode Manager - Build Script
# Builds for x64, x86, and arm64 architectures

param(
    [switch]$Release,
    [switch]$Clean
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectDir = Split-Path -Parent $ScriptDir
$OutputBase = $ScriptDir

Write-Host "=== ADG VSCode Manager Build Script ===" -ForegroundColor Cyan
Write-Host "Project: $ProjectDir"
Write-Host "Output:  $OutputBase"

# Architectures to build
$Architectures = @("win-x64", "win-x86", "win-arm64")

# Clean if requested
if ($Clean) {
    Write-Host "`nCleaning output directories..." -ForegroundColor Yellow
    foreach ($arch in $Architectures) {
        $archShort = $arch -replace "win-", ""
        $outDir = Join-Path $OutputBase $archShort
        if (Test-Path $outDir) {
            Remove-Item -Recurse -Force $outDir
            Write-Host "  Removed: $outDir"
        }
    }
}

# Build configuration
$Configuration = if ($Release) { "Release" } else { "Release" }

Write-Host "`nBuilding for architectures: $($Architectures -join ', ')" -ForegroundColor Green

foreach ($arch in $Architectures) {
    $archShort = $arch -replace "win-", ""
    $outDir = Join-Path $OutputBase $archShort
    $exeName = "AdgVscodeManager-$arch.exe"
    
    Write-Host "`n--- Building $arch ---" -ForegroundColor Cyan
    
    # Create output directory
    if (-not (Test-Path $outDir)) {
        New-Item -ItemType Directory -Path $outDir -Force | Out-Null
    }
    
    # Build command
    $publishArgs = @(
        "publish"
        $ProjectDir
        "-c", $Configuration
        "-r", $arch
        "-o", $outDir
        "--self-contained", "true"
        "-p:PublishSingleFile=true"
        "-p:EnableCompressionInSingleFile=true"
        "-p:IncludeNativeLibrariesForSelfExtract=true"
    )
    
    Write-Host "dotnet $($publishArgs -join ' ')"
    
    & dotnet @publishArgs
    
    if ($LASTEXITCODE -ne 0) {
        Write-Host "ERROR: Build failed for $arch" -ForegroundColor Red
        exit 1
    }
    
    # Rename output exe
    $originalExe = Join-Path $outDir "AdgVscodeManager.exe"
    $renamedExe = Join-Path $outDir $exeName
    
    if (Test-Path $originalExe) {
        if (Test-Path $renamedExe) {
            Remove-Item $renamedExe -Force
        }
        Rename-Item $originalExe $exeName
        Write-Host "  Output: $renamedExe" -ForegroundColor Green
        
        # Show file size
        $size = (Get-Item $renamedExe).Length / 1MB
        Write-Host "  Size: $([math]::Round($size, 2)) MB"
    }
    
    # Clean up pdb if exists
    $pdbFile = Join-Path $outDir "AdgVscodeManager.pdb"
    if (Test-Path $pdbFile) {
        Remove-Item $pdbFile -Force
    }
}

Write-Host "`n=== Build Complete ===" -ForegroundColor Green

# List all built files
Write-Host "`nBuilt executables:"
foreach ($arch in $Architectures) {
    $archShort = $arch -replace "win-", ""
    $outDir = Join-Path $OutputBase $archShort
    $exeName = "AdgVscodeManager-$arch.exe"
    $exePath = Join-Path $outDir $exeName
    
    if (Test-Path $exePath) {
        $size = (Get-Item $exePath).Length / 1MB
        Write-Host "  $exePath ($([math]::Round($size, 2)) MB)" -ForegroundColor Cyan
    }
}
