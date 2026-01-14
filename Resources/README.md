# Resources Folder

This folder contains application resources like icons.

## Icons

The application extracts icons directly from the VSCode executable (`Code.exe` or `Code - Insiders.exe`).

If you want to provide custom icons, add:

- `app.ico` - Main application icon (optional, used for build)
- `release.ico` - Release channel tray icon (optional)
- `insider.ico` - Insiders channel tray icon (optional)

## Default Behavior

If no icon files are provided:
1. The app first tries to extract the icon from the VSCode executable
2. If that fails, it generates a simple colored icon:
   - Blue circle with "VS" for Release
   - Green circle with "VS" for Insiders
