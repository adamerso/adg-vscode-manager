<p align="center">
  <img src="https://code.visualstudio.com/assets/images/code-stable.png" width="100" alt="VSCode Logo"/>
</p>

<h1 align="center">ADG VSCode Portable Manager</h1>

<p align="center">
  <strong>🚀 Zero-effort update manager for portable VSCode Insiders (and Release)</strong>
</p>

<p align="center">
  <a href="#features">Features</a> •
  <a href="#installation">Installation</a> •
  <a href="#usage">Usage</a> •
  <a href="#how-it-works">How It Works</a> •
  <a href="#building">Building</a> •
  <a href="#license">License</a>
</p>

<p align="center">
  <img src="https://img.shields.io/badge/status-release-brightgreen?style=flat-square" alt="Status: Release"/>
  <img src="https://img.shields.io/badge/version-2.3.7-blue?style=flat-square" alt="Version: 2.3.7"/>
  <img src="https://img.shields.io/badge/platform-Windows-0078d4?style=flat-square" alt="Platform: Windows"/>
  <img src="https://img.shields.io/badge/.NET-8.0-512bd4?style=flat-square" alt=".NET 8.0"/>
  <img src="https://img.shields.io/badge/license-MPL--2.0-green?style=flat-square" alt="License: MPL-2.0"/>
</p>

---

## 🎯 What is this?

**ADG VSCode Portable Manager** is a lightweight Windows tray application designed primarily for **VSCode Insiders** users who want automatic, hassle-free updates without losing precious development time.

VSCode Insiders releases updates **daily** (sometimes multiple times per day!). Manually downloading, extracting, and carefully preserving your `data` folder gets old fast. This manager handles everything automatically:

- ✅ **Downloads updates in the background** while you work
- ✅ **Unpacks archives silently** - no waiting, no interruptions  
- ✅ **Preserves your `data` folder** with settings, extensions, and keybindings
- ✅ **Instant updates** - when ready, just moves folders (takes seconds!)
- ✅ **One-click revert** if an update breaks something

> 💡 Works great with **VSCode Release** too, just with less frequent updates.

---

## ⚡ The Magic: Background Preparation

Traditional update process:
```
You click "Update" → Wait for download (2-5 min) → Wait for extraction (1-3 min) → Done
```

With ADG Manager:
```
Background: Download + Extract (you don't notice)
You click "Update" → Folder swap (2-5 seconds!) → Done
```

The manager downloads and extracts updates **in the background** while you code. When you're ready to update, it's just moving already-prepared folders around. **No waiting.**

---

## ✨ Features

### Core Functionality
- 🔄 **Automatic update detection** - Checks Microsoft's update API every 10 minutes
- 📦 **Background download & extraction** - Updates prepare while you work
- ⚡ **Instant installation** - Pre-extracted updates install in seconds
- 🔙 **One-click revert** - Instantly restore previous version if update causes issues
- 💾 **Smart data preservation** - Your `data` folder (settings, extensions) always stays safe

### User Experience  
- 🖥️ **System tray integration** - Stays out of your way, always accessible
- 🔔 **Blinking notification** - Gentle visual alert when updates are ready
- 📋 **Changelog viewer** - See what changed before updating (commit list from GitHub)
- 🎨 **Modern dark UI** - Matches VSCode's aesthetic

### Convenience
- 🚀 **Auto-start option** - Launch on Windows startup
- 🔧 **Auto-apply updates** - Apply pending updates when no VSCode is running
- 📊 **Statistics tracking** - See how many updates you've applied
- 🗂️ **Version history** - Keep backups of previous installations

---

## 📁 Directory Structure

```
your-folder/
├── vscode-portable-insiders/          # VSCode installation (managed)
│   ├── Code - Insiders.exe
│   ├── data/                          # YOUR settings & extensions (preserved!)
│   │   ├── user-data/
│   │   └── extensions/
│   └── ...
├── vscode-portable-insiders-manager-data/    # Manager's data
│   ├── config.json
│   ├── logs/
│   ├── zips/                          # Downloaded archives
│   ├── unpacked/                      # Pre-extracted updates
│   └── last-install/                  # Backup of previous version
└── AdgVscodeManager.exe               # The manager (place HERE!)
```

> 📌 **Important:** The manager must be placed **one level above** the `vscode-portable*` directory, not inside it!

---

## 🚀 Installation

### Quick Start

1. **Download** the latest `AdgVscodeManager.exe` from [Releases](../../releases)
2. **Place it** in a folder where you want your portable VSCode
3. **Run it** - the manager will offer to download VSCode for you

### Existing Installation

If you already have a portable VSCode:

1. Make sure your folder is named `vscode-portable` (Release) or `vscode-portable-insiders`
2. Place `AdgVscodeManager.exe` in the **parent folder** (see directory structure above)
3. Run the manager

---

## 📖 Usage

### System Tray Menu

Right-click the tray icon to access:

| Option | Description |
|--------|-------------|
| **Run New Window** | Launch a new VSCode window |
| **Kill All Windows** | Close all VSCode instances (with confirmation) |
| **Check for Updates** / **Update Available!** | Check or install updates |
| **Restore Previous** | Revert to the backed-up version |
| **Autostart** | Toggle Windows startup |
| **Manager Reopen Behavior** | Configure what happens when manager starts |
| **About / Stats** | Version info and update statistics |
| **Exit** | Close the manager |

### Update Flow

1. 🔔 Tray icon blinks when update is available
2. Click **"Update Available!"** to see changelog
3. Click **"Update Now"** 
4. If VSCode is running, you'll be asked to close it
5. ⚡ Update installs in seconds (folders swap)
6. New VSCode window opens automatically

### Revert Flow

Something broken after update?

1. Right-click tray → **Restore Previous**
2. Confirm the restore
3. Previous version is back instantly

---

## ⚙️ Configuration

The manager stores its configuration in `config.json`. Most settings are configurable via the tray menu:

### Manager Reopen Behavior

When the manager starts (e.g., after reboot):

| Setting | Default | Description |
|---------|---------|-------------|
| Auto-apply pending update | ✅ On | Automatically install pre-downloaded updates |
| Open window when none running | ✅ On | Launch VSCode if no instances are running |
| Open window when already running | ✅ On | Launch new window if VSCode already running |

---

## 🔧 How It Works

### Update Detection
- Polls Microsoft's official update API (`update.code.visualstudio.com`)
- Compares commit hashes to detect new versions
- Generates changelog by querying GitHub's releases API

### Smart Download
- Downloads to `zips/` folder
- Extracts to `unpacked/` folder
- All happens in background while you work

### Installation (The Fast Part)
1. Closes VSCode (with your permission)
2. Moves current installation to `last-install/` backup
3. Moves pre-extracted update to installation path
4. Preserves `data/` folder throughout
5. Launches fresh VSCode

### Revert
1. Moves current (broken?) installation aside  
2. Restores from `last-install/` backup
3. Done - your previous version is back

---

## 🛠️ Building from Source

### Prerequisites
- .NET 8.0 SDK
- Windows (WPF application)

### Build

```bash
# Debug build
dotnet build AdgVscodeManager.csproj

# Release build (self-contained, single file)
dotnet publish AdgVscodeManager.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

Output will be in `bin/Release/net8.0-windows/win-x64/publish/`

---

## 🤝 Contributing

Contributions are welcome! Please feel free to submit issues and pull requests.

---

## 📄 License

This project is licensed under the **Mozilla Public License 2.0** - see the [LICENSE](LICENSE) file for details.

---

## 🙏 Acknowledgments

- Microsoft for [Visual Studio Code](https://code.visualstudio.com/)
- The VSCode team for the excellent [portable mode](https://code.visualstudio.com/docs/editor/portable) support
- Everyone who uses and tests this tool!

---

<p align="center">
  Made with ❤️ for the VSCode Insiders community
</p>
