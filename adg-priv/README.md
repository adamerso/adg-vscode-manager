# adg-priv - Private build directory
This directory contains build scripts and outputs.

## Build
```powershell
# Build all architectures
.\build-all.ps1

# Clean and rebuild
.\build-all.ps1 -Clean
```

## Output
- `x64/AdgVscodeManager-win-x64.exe` - 64-bit Intel/AMD
- `x86/AdgVscodeManager-win-x86.exe` - 32-bit Intel/AMD  
- `arm64/AdgVscodeManager-win-arm64.exe` - 64-bit ARM

**Note:** This directory is in `.gitignore` - build outputs are not committed.
