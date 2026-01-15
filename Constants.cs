namespace AdgVscodeManager;

/// <summary>
/// Global constants for the application
/// </summary>
public static class Constants
{
    // Application version
    public const string AppVersion = "2.3.8";
    public const string AppName = "ADG VSCode Manager";
    
    // Config version compatibility
    // Config files with version < MinConfigVersion will be migrated
    // Config files with version > MaxConfigVersion will trigger error (newer app created them)
    public const string CurrentConfigVersion = "2.3.8";
    public const string MinConfigVersion = "2.3.7";
    public const string MaxConfigVersion = "2.4.0";
    
    // Feature flags
    // Set to true to enable re-checking for updates during install (slow, disabled by default)
    public const bool EnableRecheckDuringInstall = false;
    
    // Directory names
    // Data directory pattern: vscode-portable-manager-data (Release) or vscode-portable-insiders-manager-data (Insiders)
    public const string DataDirRelease = "vscode-portable-manager-data";
    public const string DataDirInsiders = "vscode-portable-insiders-manager-data";
    public const string ZipsDir = "zips";
    public const string UnpackedDir = "unpacked";
    public const string LogsDir = "logs";
    public const string LastInstallDir = "last-install";
    public const string RevertedVersionDir = "reverted_version";
    public const string ConfigFileName = "config.json";
    public const string LogFilePrefix = "adg-vscode-manager_";
    
    // Helper to get data directory name for channel
    public static string GetDataDirName(VscodeChannel channel) =>
        channel == VscodeChannel.Insiders ? DataDirInsiders : DataDirRelease;
    
    // VSCode portable directory names
    public const string VscodePortableRelease = "vscode-portable";
    public const string VscodePortableInsider = "vscode-portable-insiders";
    public const string VscodeDataDir = "data";
    
    // Bin script names (for reading commit hash)
    public const string VscodeBinScriptRelease = "code";
    public const string VscodeBinScriptInsider = "code-insiders";
    
    // Executable names
    public const string VscodeExeRelease = "Code.exe";
    public const string VscodeExeInsider = "Code - Insiders.exe";
    
    // Named pipe/mutex prefixes
    public const string MutexPrefix = "AdgVscodeManager_";
    public const string PipePrefix = "AdgVscodeManagerPipe_";
    
    // IPC commands
    public const string IpcOpenNewWindow = "OPEN_NEW_WINDOW";
    public const string IpcBringToFront = "BRING_TO_FRONT";
    
    // Update API URLs
    public const string MsUpdateApiBase = "https://update.code.visualstudio.com/api/update";
    public const string MsUpdateArchiveType = "win32-{0}-archive"; // {0} = x64, ia32, arm64
    public const string GithubReleasesApi = "https://api.github.com/repos/microsoft/vscode/releases";
    
    // Registry
    public const string AutostartRegistryPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    public const string AutostartValueName = "AdgVscodeManager";
    
    // Timing
    public const int BackgroundCheckIntervalMinutes = 10;
    public const int ProcessKillTimeoutMs = 5000;
    public const int DownloadTimeoutMinutes = 30;
    
    // GitHub API limits
    public const int GitHubReleasesPerPage = 100;   // Max allowed by GitHub API
    public const int GitHubTagsInitialFetch = 100;  // First attempt - fetch 100 tags
    public const int GitHubTagsExtendedFetch = 800; // If commit not found, extend to 800
    public const int GitHubReleasesToFetch = 250;   // How many releases to fetch for changelog
    public const int CachedReleasesLimit = 10000;   // Max releases to keep in cache
    
    // Fallback update detection
    public const int ExeAgeThresholdDays = 7;       // If exe older than this and commit not found = update available
    
    // File naming patterns
    public const string LatestZipSuffix = "_latest.zip";
    public const string PreviousZipSuffix = "_previous.zip";
    public const string ChangesLatestSuffix = "_changes_latest.txt";
    public const string LastBackupSuffix = "_LAST";
    public const string RevertedSuffix = "_REVERTED";
}

/// <summary>
/// VSCode channel type
/// </summary>
public enum VscodeChannel
{
    Release,
    Insiders
}

/// <summary>
/// Application state machine states
/// </summary>
public enum AppState
{
    Idle,
    CheckingUpdate,
    UpdateAvailable,
    Downloading,
    Installing,
    Restoring,
    Error
}
