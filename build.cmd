@echo off
REM Build script for ADG VSCode Portable Manager
REM Creates single-file executables for all architectures

setlocal enabledelayedexpansion

echo ========================================
echo ADG VSCode Manager - Build Script
echo ========================================
echo.

REM Configuration
set PROJECT=AdgVscodeManager.csproj
set OUTPUT_DIR=publish

REM Clean previous builds
if exist "%OUTPUT_DIR%" (
    echo Cleaning previous builds...
    rmdir /s /q "%OUTPUT_DIR%"
)
mkdir "%OUTPUT_DIR%"

REM Build for each architecture
for %%A in (win-x64 win-x86 win-arm64) do (
    echo.
    echo Building for %%A...
    echo ----------------------------------------
    
    dotnet publish "%PROJECT%" ^
        -c Release ^
        -r %%A ^
        --self-contained true ^
        -p:PublishSingleFile=true ^
        -p:EnableCompressionInSingleFile=true ^
        -p:IncludeNativeLibrariesForSelfExtract=true ^
        -p:PublishTrimmed=false ^
        -o "%OUTPUT_DIR%\%%A"
    
    if !errorlevel! neq 0 (
        echo ERROR: Build failed for %%A
        exit /b 1
    )
    
    echo Build successful for %%A
)

echo.
echo ========================================
echo Build Complete!
echo ========================================
echo.
echo Output files:
for %%A in (win-x64 win-x86 win-arm64) do (
    if exist "%OUTPUT_DIR%\%%A\AdgVscodeManager.exe" (
        for %%F in ("%OUTPUT_DIR%\%%A\AdgVscodeManager.exe") do (
            echo   %%A: %%~zF bytes
        )
    )
)
echo.
echo Files are in: %OUTPUT_DIR%\
echo.

endlocal
