using System.IO;
using System.Windows;
using System.Windows.Threading;
using AdgVscodeManager.Models;
using AdgVscodeManager.Services;
using AdgVscodeManager.Views;

namespace AdgVscodeManager;

/// <summary>
/// Main application manager - orchestrates all services and procedures
/// </summary>
public class AppManager : IDisposable
{
    // Paths
    private readonly string _workDir;
    private string _installDir;
    private string _archiveRoot;
    private string _archiveZips;
    private string _unpackedDir;
    private string _logsDir;
    private string _lastInstallDir;
    private string _configPath;
    
    // Channel
    private VscodeChannel _channel;
    
    // Services
    private LoggingService _logger = null!;
    private ConfigService _configService = null!;
    private ProcessService _processService = null!;
    private UpdateService _updateService = null!;
    private FileSystemService _fileSystemService = null!;
    private AutostartService _autostartService = null!;
    private IpcService _ipcService = null!;
    
    // UI
    private TrayIconManager _trayManager = null!;
    private System.Windows.Media.ImageSource? _windowIcon;
    private System.Drawing.Icon? _colorIcon;  // Keep color icon for tray blinking
    
    // State
    private readonly RuntimeState _state;
    private readonly bool _isAutoStartMode;
    private DispatcherTimer? _backgroundTimer;
    private ProgressDialog? _currentProgressDialog;
    private UpdateDialog? _currentUpdateDialog;
    
    // Public property to access ConfigService from App
    public ConfigService ConfigService => _configService;
    
    public AppManager(bool isAutoStartMode)
    {
        _isAutoStartMode = isAutoStartMode;
        _state = new RuntimeState { IsAutoStartMode = isAutoStartMode };
        
        // Determine work directory (where exe is located)
        _workDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        
        // Initialize paths with temporary defaults (will be updated after channel detection)
        // Using Release data dir as placeholder, will be corrected in UpdatePathsForChannel()
        _archiveRoot = Path.Combine(_workDir, Constants.DataDirRelease);
        _archiveZips = Path.Combine(_archiveRoot, Constants.ZipsDir);
        _unpackedDir = Path.Combine(_archiveRoot, Constants.UnpackedDir);
        _logsDir = Path.Combine(_archiveRoot, Constants.LogsDir);
        _lastInstallDir = Path.Combine(_archiveRoot, Constants.LastInstallDir);
        _configPath = Path.Combine(_archiveRoot, Constants.ConfigFileName);
        
        // Placeholder - will be set after detection
        _installDir = "";
    }
    
    /// <summary>
    /// Initialize the application - detect channel, acquire lock, etc.
    /// </summary>
    public bool Initialize()
    {
        // FIRST: Validate that manager is not running from inside VSCode directory
        if (!ValidateManagerLocation())
        {
            return false;
        }
        
        // Detect channel FIRST (before creating any directories)
        // Use temporary console logging until we have proper paths
        if (!DetectChannel())
        {
            return false;
        }
        
        // Update all paths based on detected channel
        UpdatePathsForChannel(_channel);
        
        // NOW create directories with correct channel-specific paths
        EnsureDirectories();
        
        // Initialize logger
        _logger = new LoggingService(_logsDir);
        _logger.Info("=== ADG VSCode Manager Starting ===");
        _logger.LogPath("Work directory", _workDir);
        _logger.Info($"Autostart mode: {_isAutoStartMode}");
        _logger.LogPath("Install directory", _installDir);
        _logger.LogPath("Data directory", _archiveRoot);
        
        // Initialize IPC service and try to acquire lock
        _ipcService = new IpcService(_workDir, _channel, _logger);
        
        if (!_ipcService.TryAcquireLock())
        {
            // Another instance is running - send command and exit
            _logger.Info("Sending OPEN_NEW_WINDOW to existing instance");
            
            // Need to wait for async operation
            var sendTask = _ipcService.SendOpenNewWindowAsync();
            sendTask.Wait(5000);
            
            return false; // Signal to exit
        }
        
        // We are the first instance
        _ipcService.StartServer();
        _ipcService.OpenNewWindowRequested += (s, e) =>
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                _logger.Info("IPC: OPEN_NEW_WINDOW received");
                OpenNewWindow();
            });
        };
        
        // Initialize remaining services
        InitializeServices();
        
        // Check if config version is incompatible
        if (_configService.ConfigVersionIncompatible)
        {
            var result = System.Windows.MessageBox.Show(
                $"The configuration file was created by a newer version of VSCode Manager ({_configService.IncompatibleVersion}).\n\n" +
                $"This version ({Constants.AppVersion}) supports config versions {Constants.MinConfigVersion} to {Constants.MaxConfigVersion}.\n\n" +
                "Would you like to reset the configuration and continue?\n\n" +
                "Click 'Yes' to reset (a backup will be created)\n" +
                "Click 'No' to exit the application",
                "Configuration Incompatible",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning);
            
            if (result == System.Windows.MessageBoxResult.Yes)
            {
                _configService.DeleteAndReload();
                _logger.Info("User chose to reset config due to version incompatibility");
            }
            else
            {
                _logger.Info("User chose to exit due to config version incompatibility");
                return false;
            }
        }
        
        return true;
    }
    
    private void EnsureDirectories()
    {
        Directory.CreateDirectory(_archiveRoot);
        Directory.CreateDirectory(_archiveZips);
        Directory.CreateDirectory(_unpackedDir);
        Directory.CreateDirectory(_logsDir);
        Directory.CreateDirectory(_lastInstallDir);
    }
    
    /// <summary>
    /// Validate that manager is not running from inside VSCode directory
    /// </summary>
    private bool ValidateManagerLocation()
    {
        var workDirName = Path.GetFileName(_workDir);
        var parentDir = Path.GetDirectoryName(_workDir);
        var parentDirName = !string.IsNullOrEmpty(parentDir) ? Path.GetFileName(parentDir) : "";
        
        // Check if we're inside vscode-portable or vscode-portable-insiders directory
        var isInsideVscode = workDirName.Equals(Constants.VscodePortableRelease, StringComparison.OrdinalIgnoreCase)
            || workDirName.Equals(Constants.VscodePortableInsider, StringComparison.OrdinalIgnoreCase)
            || parentDirName.Equals(Constants.VscodePortableRelease, StringComparison.OrdinalIgnoreCase)
            || parentDirName.Equals(Constants.VscodePortableInsider, StringComparison.OrdinalIgnoreCase);
        
        // Also check if Code.exe exists in same directory (running from vscode folder)
        var codeExeInSameDir = File.Exists(Path.Combine(_workDir, Constants.VscodeExeRelease))
            || File.Exists(Path.Combine(_workDir, Constants.VscodeExeInsider));
        
        if (isInsideVscode || codeExeInSameDir)
        {
            MessageBox.Show(
                "⚠️ INCORRECT LOCATION ⚠️\n\n" +
                "ADG VSCode Manager must be placed ONE LEVEL ABOVE the VSCode portable directory, not inside it.\n\n" +
                "CORRECT structure:\n" +
                "   📁 your-folder/\n" +
                "      ├── 📁 vscode-portable/          (or vscode-portable-insiders/)\n" +
                "      │      └── Code.exe\n" +
                "      ├── 📁 vscode-portable-manager-data/\n" +
                "      └── 🔷 AdgVscodeManager.exe     ← HERE\n\n" +
                "INCORRECT (current):\n" +
                "   📁 vscode-portable/\n" +
                "      ├── Code.exe\n" +
                "      └── 🔷 AdgVscodeManager.exe     ← WRONG!\n\n" +
                "Please move AdgVscodeManager.exe to the parent directory.",
                "Invalid Location",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return false;
        }
        
        return true;
    }
    
    /// <summary>
    /// Update all paths based on detected channel
    /// </summary>
    private void UpdatePathsForChannel(VscodeChannel channel)
    {
        _archiveRoot = Path.Combine(_workDir, Constants.GetDataDirName(channel));
        _archiveZips = Path.Combine(_archiveRoot, Constants.ZipsDir);
        _unpackedDir = Path.Combine(_archiveRoot, Constants.UnpackedDir);
        _logsDir = Path.Combine(_archiveRoot, Constants.LogsDir);
        _lastInstallDir = Path.Combine(_archiveRoot, Constants.LastInstallDir);
        _configPath = Path.Combine(_archiveRoot, Constants.ConfigFileName);
        _installDir = GetInstallDir(channel);
    }
    
    private string GetInstallDir(VscodeChannel channel)
    {
        return channel == VscodeChannel.Insiders
            ? Path.Combine(_workDir, Constants.VscodePortableInsider)
            : Path.Combine(_workDir, Constants.VscodePortableRelease);
    }
    
    private bool DetectChannel()
    {
        var releaseDir = Path.Combine(_workDir, Constants.VscodePortableRelease);
        var insiderDir = Path.Combine(_workDir, Constants.VscodePortableInsider);
        
        var releaseExists = Directory.Exists(releaseDir) && HasVscodeExecutable(releaseDir, VscodeChannel.Release);
        var insiderExists = Directory.Exists(insiderDir) && HasVscodeExecutable(insiderDir, VscodeChannel.Insiders);
        
        // Logger may not be initialized yet, use null-conditional
        _logger?.Debug($"Release dir exists with exe: {releaseExists} ({releaseDir})");
        _logger?.Debug($"Insider dir exists with exe: {insiderExists} ({insiderDir})");
        
        if (releaseExists && insiderExists)
        {
            _logger?.Error("Both release and insider directories exist - cannot determine channel");
            MessageBox.Show(
                "Error: Both vscode-portable and vscode-portable-insider directories exist.\n\n" +
                "Please remove one of them and restart the manager.",
                "Channel Detection Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return false;
        }
        
        if (insiderExists)
        {
            _channel = VscodeChannel.Insiders;
            _logger?.Info("Detected channel: Insiders");
        }
        else if (releaseExists)
        {
            _channel = VscodeChannel.Release;
            _logger?.Info("Detected channel: Release");
        }
        else
        {
            // No installation found - show setup dialog
            _logger?.Info("No portable VSCode installation found - showing setup dialog");
            
            var setupDialog = new SetupDialog();
            var result = setupDialog.ShowDialog();
            
            if (result != true || !setupDialog.ShouldDownload)
            {
                _logger?.Info("User cancelled setup");
                return false;
            }
            
            _channel = setupDialog.SelectedChannel;
            _logger?.Info($"User selected channel: {_channel}");
            
            // Perform initial download
            if (!PerformInitialSetup())
            {
                return false;
            }
        }
        
        return true;
    }
    
    /// <summary>
    /// Check if directory contains VSCode executable AND bin script (for version detection)
    /// </summary>
    private bool HasVscodeExecutable(string dir, VscodeChannel channel)
    {
        var exeName = channel == VscodeChannel.Insiders 
            ? Constants.VscodeExeInsider 
            : Constants.VscodeExeRelease;
        var binScriptName = channel == VscodeChannel.Insiders
            ? Constants.VscodeBinScriptInsider
            : Constants.VscodeBinScriptRelease;
        
        var exeExists = File.Exists(Path.Combine(dir, exeName));
        var binExists = File.Exists(Path.Combine(dir, "bin", binScriptName));
        
        return exeExists && binExists;
    }
    
    /// <summary>
    /// Perform initial VSCode download and setup
    /// </summary>
    private bool PerformInitialSetup()
    {
        var installDir = GetInstallDir(_channel);
        
        _logger.Info($"Performing initial setup for {_channel} at {installDir}");
        
        // Show progress dialog
        var progressDialog = new ProgressDialog();
        var channelText = _channel == VscodeChannel.Insiders ? "Insiders" : "Release";
        progressDialog.Title = $"{Constants.AppName} v{Constants.AppVersion} - Downloading {channelText}";
        progressDialog.SetTitle($"Downloading VSCode {channelText}...");
        progressDialog.SetStatus("Connecting to update server...");
        
        var success = false;
        Exception? setupError = null;
        
        // Start async operation when dialog is shown
        progressDialog.Loaded += async (s, e) =>
        {
            try
            {
                // Initialize temporary services for download
                var tempConfigService = new ConfigService(_configPath, _logger);
                tempConfigService.SetChannel(_channel);
                var tempUpdateService = new UpdateService(tempConfigService, _logger, _channel);
                var tempFileSystemService = new FileSystemService(_logger);
                
                // 1) Check latest version
                progressDialog.SetStatus("Checking latest version...");
                
                var latest = await tempUpdateService.CheckLatestVersionAsync(progressDialog.CancellationToken);
                if (latest == null)
                {
                    throw new Exception("Failed to check latest version. Please check your internet connection.");
                }
                
                if (progressDialog.CancellationToken.IsCancellationRequested)
                    return;
                
                _logger.LogVersion("Latest version", latest.ProductVersion);
                
                // Get short commit hash from GitHub
                var shortCommit = await tempUpdateService.GetShortCommitHashAsync(latest.Version, progressDialog.CancellationToken);
                
                // 2) Download zip
                progressDialog.SetStatus($"Downloading VSCode {latest.ProductVersion}...");
                
                var timestamp = FileSystemService.GetTimestamp();
                // Use commit hash (short) for unique identification
                var commitShort = latest.Version.Length > 10 ? latest.Version[..10] : latest.Version;
                var zipFileName = $"date_{timestamp}_{latest.ProductVersion}_{commitShort}{Constants.LatestZipSuffix}";
                var zipPath = Path.Combine(_archiveZips, zipFileName);
                
                var progress = new Progress<int>(p => progressDialog.SetProgress(p));
                
                var downloadSuccess = await tempUpdateService.DownloadZipAsync(
                    latest.Url, zipPath, progress, progressDialog.CancellationToken);
                
                if (progressDialog.CancellationToken.IsCancellationRequested)
                    return;
                
                if (!downloadSuccess)
                {
                    throw new Exception("Download failed. Please try again.");
                }
                
                // 3) Extract with progress
                progressDialog.SetStatus("Extracting VSCode...");
                
                // Create install directory
                Directory.CreateDirectory(installDir);
                
                // Run extraction on thread pool with progress reporting
                var extractProgress = new Progress<(int current, int total)>(p =>
                {
                    var percent = p.total > 0 ? (p.current * 100 / p.total) : 0;
                    progressDialog.SetProgress(percent);
                    progressDialog.SetStatus($"Extracting... {p.current:N0}/{p.total:N0} files ({percent}%)");
                });
                
                var extractSuccess = await Task.Run(() => tempFileSystemService.ExtractZipWithProgress(zipPath, installDir, extractProgress));
                
                if (!extractSuccess)
                {
                    throw new Exception("Failed to extract VSCode.");
                }
                
                // 4) Create data directory for portable mode
                progressDialog.SetStatus("Configuring portable mode...");
                tempFileSystemService.EnsureDataDirectory(installDir);
                
                // 5) Update config
                tempConfigService.Update(c =>
                {
                    c.InstalledVersion = latest.ProductVersion;
                    c.InstalledCommit = latest.Version;
                    c.InstalledCommitShort = shortCommit;
                    c.LatestKnownVersion = latest.ProductVersion;
                    c.LatestKnownCommit = latest.Version;
                    c.LatestKnownCommitShort = shortCommit;
                    c.LastDownloadZipPath = zipPath;
                    c.LastUpdateCheckUtc = DateTime.UtcNow.ToString("o");
                });
                
                success = true;
                progressDialog.Complete("Setup complete!");
                
                // Close after delay
                await Task.Delay(1500);
                progressDialog.Close();
            }
            catch (OperationCanceledException)
            {
                _logger.Info("Setup cancelled by user");
                progressDialog.Close();
            }
            catch (Exception ex)
            {
                _logger.Error("Initial setup failed", ex);
                setupError = ex;
                
                MessageBox.Show(
                    $"Setup failed: {ex.Message}",
                    "Setup Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                
                // Cleanup failed installation
                try
                {
                    if (Directory.Exists(installDir))
                    {
                        Directory.Delete(installDir, true);
                    }
                }
                catch { }
                
                progressDialog.Close();
            }
        };
        
        // Show dialog modally - blocks until closed
        progressDialog.ShowDialog();
        
        return success;
    }
    
    private void InitializeServices()
    {
        _configService = new ConfigService(_configPath, _logger);
        _configService.SetChannel(_channel);
        
        _processService = new ProcessService(_installDir, _channel, _logger);
        _updateService = new UpdateService(_configService, _logger, _channel);
        _fileSystemService = new FileSystemService(_logger);
        _autostartService = new AutostartService(_logger);
        
        // Ensure data directory exists
        _fileSystemService.EnsureDataDirectory(_installDir);
        
        // Always try to read installed commit from bin script (source of truth)
        var commit = _fileSystemService.ReadVscodeCommit(_installDir, _channel);
        if (!string.IsNullOrEmpty(commit))
        {
            _configService.Update(c => c.InstalledCommit = commit);
            _logger.Info($"Detected installed commit: {commit}");
            
            // Add to history if new
            _configService.AddCommitToHistory(commit);
            
            // Try to read version from package.json and register in map
            var versionFromPackage = _fileSystemService.ReadVscodeVersion(_installDir);
            if (!string.IsNullOrEmpty(versionFromPackage))
            {
                _configService.RegisterCommitVersion(commit, versionFromPackage);
                _logger.LogVersion("Registered commit version from package.json", versionFromPackage);
            }
            
            // Get version from map (may be what we just registered, or "unidentified")
            var version = _configService.GetVersionForCommit(commit);
            _configService.Update(c => c.InstalledVersion = version);
            _logger.LogVersion("Installed version (from map)", version);
        }
        else
        {
            // No commit found - clear version info
            _configService.Update(c => 
            {
                c.InstalledCommit = "";
                c.InstalledVersion = "";
            });
            _logger.Warn("Could not detect installed commit from bin script");
        }
    }
    
    /// <summary>
    /// Start the application (tray, background tasks, startup flow)
    /// </summary>
    public void Start()
    {
        // Get icons from static files
        _colorIcon = IconExtractor.GetColorIcon(_channel);
        var blinkIcon = IconExtractor.GetBlinkIcon();
        
        // Use BW icon for our dialogs (Code_bw_60.ico)
        _windowIcon = IconExtractor.GetWindowIconImageSource();
        
        // Initialize tray icon with color icon (and dark gray for blinking)
        _trayManager = new TrayIconManager(
            _state,
            _channel,
            _colorIcon,
            blinkIcon,
            _autostartService,
            _processService,
            _logger,
            _configService);
        
        // Wire up tray events
        _trayManager.RunNewWindowRequested += (s, e) => OpenNewWindow();
        _trayManager.KillAllWindowsRequested += async (s, e) => await KillAllWindowsAsync();
        _trayManager.CheckUpdateRequested += (s, e) => ShowUpdateDialog();
        _trayManager.BringProgressToFrontRequested += (s, e) => BringProgressDialogToFront();
        _trayManager.RestoreRequested += async (s, e) => await RestorePreviousVersionAsync();
        _trayManager.ExitRequested += (s, e) => ExitApplication();
        _trayManager.AboutRequested += (s, e) => ShowAboutDialog();
        
        // Execute startup flow
        ExecuteStartupFlow();
        
        // Start background update checker
        StartBackgroundChecker();
        
        _logger.Info("Application started successfully");
    }
    
    /// <summary>
    /// Bring progress dialog to front, or create one if download is in progress but dialog doesn't exist (silent mode)
    /// </summary>
    private void BringProgressDialogToFront()
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            if (_currentProgressDialog != null)
            {
                _currentProgressDialog.Activate();
                _currentProgressDialog.Topmost = true;
                _currentProgressDialog.Topmost = false;
            }
            else if (_state.IsDownloadInProgress)
            {
                // Silent download in progress - create dialog and show current state
                var dialog = new ProgressDialog();
                ConfigureWindow(dialog, "Downloading");
                
                switch (_state.CurrentState)
                {
                    case AppState.CheckingUpdate:
                        dialog.SetTitle("Checking for updates...");
                        dialog.SetIndeterminate();
                        break;
                    case AppState.Downloading:
                        dialog.SetTitle("Downloading update...");
                        dialog.SetStatus($"Progress: {_state.DownloadProgress}%");
                        dialog.SetProgress(_state.DownloadProgress);
                        break;
                    default:
                        dialog.SetTitle("Working...");
                        dialog.SetIndeterminate();
                        break;
                }
                
                _currentProgressDialog = dialog;
                
                // Subscribe to progress updates
                void OnProgressChanged(object? sender, int progress)
                {
                    Application.Current?.Dispatcher.InvokeAsync(() =>
                    {
                        dialog.SetProgress(progress);
                        dialog.SetStatus($"Downloading... {progress}%");
                    });
                }
                
                void OnStateChanged(object? sender, AppState state)
                {
                    Application.Current?.Dispatcher.InvokeAsync(() =>
                    {
                        if (state == AppState.Idle || state == AppState.UpdateAvailable)
                        {
                            // Download finished - close dialog
                            _state.DownloadProgressChanged -= OnProgressChanged;
                            _state.StateChanged -= OnStateChanged;
                            dialog.Close();
                            _currentProgressDialog = null;
                        }
                        else if (state == AppState.CheckingUpdate)
                        {
                            dialog.SetTitle("Checking for updates...");
                            dialog.SetIndeterminate();
                        }
                    });
                }
                
                _state.DownloadProgressChanged += OnProgressChanged;
                _state.StateChanged += OnStateChanged;
                
                dialog.Show();
                dialog.Activate();
            }
        });
    }
    
    /// <summary>
    /// Configure a window with app icon and versioned title
    /// </summary>
    private void ConfigureWindow(Window window, string titleSuffix = "")
    {
        var channelText = _channel == VscodeChannel.Insiders ? " Insiders" : "";
        var title = $"{Constants.AppName} v{Constants.AppVersion}{channelText}";
        if (!string.IsNullOrEmpty(titleSuffix))
        {
            title += $" - {titleSuffix}";
        }
        window.Title = title;
        
        if (_windowIcon != null)
        {
            window.Icon = _windowIcon;
        }
    }
    
    /// <summary>
    /// Format version display with commit hash for clarity
    /// If shortCommit is provided, use it directly; otherwise fallback to first 7 chars
    /// </summary>
    private static string FormatVersionDisplay(string version, string commit, string? shortCommit = null)
    {
        // Use provided shortCommit or fallback to first 7 chars (git default)
        var displayCommit = !string.IsNullOrEmpty(shortCommit) 
            ? shortCommit 
            : (!string.IsNullOrEmpty(commit) && commit.Length >= 7 ? commit[..7] : commit);
        
        if (string.IsNullOrEmpty(version) || version == "unidentified")
        {
            // No version - show commit only
            return !string.IsNullOrEmpty(displayCommit) ? displayCommit : "unknown";
        }
        
        if (string.IsNullOrEmpty(displayCommit))
        {
            // No commit - show version only
            return version;
        }
        
        // Both available - show "version (commit)"
        return $"{version}\n({displayCommit})";
    }
    
    /// <summary>
    /// Execute the startup flow based on current state and config settings
    /// </summary>
    private void ExecuteStartupFlow()
    {
        var hasRunningProcesses = _processService.HasRunningVscodeProcesses();
        var config = _configService.Config;
        _logger.Info($"Startup: Has running VSCode processes: {hasRunningProcesses}");
        _logger.Info($"Startup config: AutoApply={config.AutoApplyUpdateOnStart}, OpenWhenNone={config.OpenWindowWhenNoneRunning}, OpenWhenRunning={config.OpenWindowWhenAlreadyRunning}");
        
        if (hasRunningProcesses)
        {
            // Scenario A/B: VSCode is already running
            // Optionally open new window based on config
            if (!_isAutoStartMode && config.OpenWindowWhenAlreadyRunning)
            {
                OpenNewWindow();
            }
            
            // Start silent update check in background
            _ = DownloadUpdateAsync(silent: true);
        }
        else
        {
            // Scenario C: No VSCode running
            if (_isAutoStartMode)
            {
                // Autostart mode - just start silent background check, no window
                _ = DownloadUpdateAsync(silent: true);
            }
            else
            {
                // Normal start - behavior depends on config
                _state.StartupRequiresWindow = config.OpenWindowWhenNoneRunning;
                
                if (config.AutoApplyUpdateOnStart)
                {
                    // Auto-apply mode: check + auto install if available
                    _ = ExecuteStartupAutoApply();
                }
                else
                {
                    // Normal mode: check for updates first, then maybe show dialog, then open
                    _ = ExecuteStartupUpdateCheck();
                }
            }
        }
    }
    
    /// <summary>
    /// Startup flow with auto-apply: check for pending unpacked update and install automatically if available
    /// Only installs if there's an unpacked update ready AND no VSCode processes running
    /// </summary>
    private async Task ExecuteStartupAutoApply()
    {
        var config = _configService.Config;
        
        // Check if there's a pending unpacked update ready
        var hasPendingUnpacked = !string.IsNullOrEmpty(config.LastUnpackedPath) 
            && Directory.Exists(config.LastUnpackedPath)
            && !string.IsNullOrEmpty(config.LastUnpackedCommit)
            && !string.IsNullOrEmpty(config.InstalledCommit)
            && !string.Equals(config.LastUnpackedCommit, config.InstalledCommit, StringComparison.OrdinalIgnoreCase)
            && !_configService.IsCommitSkipped(config.LastUnpackedCommit);
        
        if (!hasPendingUnpacked)
        {
            // No pending update - just do normal startup flow
            _logger.Info("AutoApply: No pending unpacked update, proceeding with normal startup");
            
            if (_state.StartupRequiresWindow)
            {
                OpenNewWindow();
            }
            
            // Start background update check
            _ = DownloadUpdateAsync(silent: true);
            _state.StartupRequiresWindow = false;
            return;
        }
        
        // Double-check no VSCode processes from our managed path
        if (_processService.HasRunningVscodeProcesses())
        {
            _logger.Warn("AutoApply: VSCode processes detected, skipping auto-install");
            
            if (_state.StartupRequiresWindow)
            {
                OpenNewWindow();
            }
            
            _state.StartupRequiresWindow = false;
            return;
        }
        
        _logger.Info($"AutoApply: Found pending unpacked update: {config.LastUnpackedCommit}");
        
        ProgressDialog? dialog = null;
        
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            dialog = new ProgressDialog();
            ConfigureWindow(dialog, "Installing Update");
            dialog.SetTitle("Installing pending update...");
            dialog.SetIndeterminate();
            _currentProgressDialog = dialog;
            dialog.Show();
        });
        
        try
        {
            // Install the pending unpacked update
            await InstallUpdateAsync();
            
            // After install, the InstallUpdateAsync should handle opening window if needed
            // based on ShowUpdateDialog flow
        }
        catch (Exception ex)
        {
            _logger.Error("AutoApply: Failed to install pending update", ex);
            
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                dialog?.Close();
                _currentProgressDialog = null;
            });
            
            if (_state.StartupRequiresWindow)
            {
                OpenNewWindow();
            }
        }
        finally
        {
            _state.StartupRequiresWindow = false;
        }
    }
    
    /// <summary>
    /// OLD: Startup flow with auto-apply that downloads first (kept for reference, not used)
    /// </summary>
    private async Task ExecuteStartupAutoApplyWithDownload()
    {
        ProgressDialog? dialog = null;
        
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            dialog = new ProgressDialog();
            ConfigureWindow(dialog, "Checking Updates");
            dialog.SetTitle("Checking for updates...");
            dialog.SetIndeterminate();
            _currentProgressDialog = dialog;
            dialog.Show();
        });
        
        try
        {
            await DownloadUpdateCoreAsync(dialog!.CancellationToken, dialog);
            
            var config = _configService.Config;
            var hasUpdate = !string.IsNullOrEmpty(config.LatestKnownCommit) 
                && !string.IsNullOrEmpty(config.InstalledCommit)
                && !string.Equals(config.LatestKnownCommit, config.InstalledCommit, StringComparison.OrdinalIgnoreCase)
                && !_configService.IsCommitSkipped(config.LatestKnownCommit);
            
            if (hasUpdate)
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    dialog?.SetTitle("Installing update...");
                });
                
                // Auto-install the update
                await InstallUpdateAsync();
            }
            else
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    dialog?.Close();
                    _currentProgressDialog = null;
                });
                
                if (_state.StartupRequiresWindow)
                {
                    OpenNewWindow();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Startup auto-apply failed", ex);
            
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                dialog?.Close();
                _currentProgressDialog = null;
            });
            
            if (_state.StartupRequiresWindow)
            {
                OpenNewWindow();
            }
        }
        finally
        {
            _state.StartupRequiresWindow = false;
        }
    }
    
    private async Task ExecuteStartupUpdateCheck()
    {
        // Show progress dialog
        ProgressDialog? dialog = null;
        
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            dialog = new ProgressDialog();
            ConfigureWindow(dialog, "Checking Updates");
            dialog.SetTitle("Checking for updates...");
            dialog.SetIndeterminate();
            _currentProgressDialog = dialog;
            dialog.Show();
        });
        
        try
        {
            var result = await DownloadUpdateCoreAsync(dialog!.CancellationToken, dialog);
            
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                dialog?.Close();
                _currentProgressDialog = null;
            });
            
            if (dialog!.WasCancelled)
            {
                // Cancelled - still open window if startup requires it
                if (_state.StartupRequiresWindow)
                {
                    OpenNewWindow();
                }
                return;
            }
            
            // Check if update available and not skipped
            var config = _configService.Config;
            var hasUpdate = false;
            
            // Compare by commit if available, otherwise by version
            if (!string.IsNullOrEmpty(config.LatestKnownCommit) && !string.IsNullOrEmpty(config.InstalledCommit))
            {
                hasUpdate = !string.Equals(config.LatestKnownCommit, config.InstalledCommit, StringComparison.OrdinalIgnoreCase);
            }
            else if (!string.IsNullOrEmpty(config.LatestKnownVersion) && !string.IsNullOrEmpty(config.InstalledVersion))
            {
                hasUpdate = UpdateService.IsNewerVersion(config.LatestKnownVersion, config.InstalledVersion);
            }
            
            // Also check for pending unpacked update (downloaded earlier but not installed)
            var hasPendingUnpacked = !string.IsNullOrEmpty(config.LastUnpackedPath) 
                && Directory.Exists(config.LastUnpackedPath)
                && !string.IsNullOrEmpty(config.LastUnpackedCommit)
                && !string.IsNullOrEmpty(config.InstalledCommit)
                && !string.Equals(config.LastUnpackedCommit, config.InstalledCommit, StringComparison.OrdinalIgnoreCase)
                && !_configService.IsCommitSkipped(config.LastUnpackedCommit);
            
            hasUpdate = hasUpdate || hasPendingUnpacked;
            
            if (hasUpdate && !_configService.IsCommitSkipped(config.LatestKnownCommit))
            {
                // Show update dialog
                ShowUpdateDialog();
            }
            else
            {
                // No update or skipped - just open window
                if (_state.StartupRequiresWindow)
                {
                    OpenNewWindow();
                }
            }
        }
        catch (OperationCanceledException)
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                dialog?.Close();
                _currentProgressDialog = null;
            });
            
            if (_state.StartupRequiresWindow)
            {
                OpenNewWindow();
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Startup update check failed", ex);
            
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                dialog?.Close();
                _currentProgressDialog = null;
            });
            
            // Open window anyway
            if (_state.StartupRequiresWindow)
            {
                OpenNewWindow();
            }
        }
        finally
        {
            _state.StartupRequiresWindow = false;
        }
    }
    
    private void StartBackgroundChecker()
    {
        _backgroundTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(Constants.BackgroundCheckIntervalMinutes)
        };
        
        _backgroundTimer.Tick += async (s, e) =>
        {
            _logger.Debug("Background update check triggered");
            await DownloadUpdateAsync(silent: true);
        };
        
        // Start timer - first tick will be after the interval (10 min)
        // This ensures we don't immediately check on startup, only after delay
        _backgroundTimer.Start();
        _logger.Info($"Background checker started (first check in {Constants.BackgroundCheckIntervalMinutes} min, then every {Constants.BackgroundCheckIntervalMinutes} min)");
    }
    
    /// <summary>
    /// PROCEDURE: OPEN_NEW_WINDOW
    /// </summary>
    public void OpenNewWindow()
    {
        _logger.Info("Opening new VSCode window");
        
        if (_processService.OpenNewWindow())
        {
            // Also trigger background update check if not already running
            if (!_state.IsDownloadInProgress)
            {
                _ = DownloadUpdateAsync(silent: true);
            }
        }
    }
    
    /// <summary>
    /// PROCEDURE: DOWNLOAD_UPDATE (wrapper)
    /// </summary>
    private async Task DownloadUpdateAsync(bool silent)
    {
        if (_state.IsDownloadInProgress)
        {
            _logger.Debug("Download already in progress, skipping");
            return;
        }
        
        _state.IsSilentMode = silent;
        
        try
        {
            _state.IsDownloadInProgress = true;
            await DownloadUpdateCoreAsync(CancellationToken.None, null);
            
            // Notify if update available and not already notified
            // Use commit for unique identification (version numbers can repeat)
            var config = _configService.Config;
            var notifyKey = !string.IsNullOrEmpty(config.LatestKnownCommit) 
                ? config.LatestKnownCommit 
                : config.LatestKnownVersion;
            
            if (_state.CurrentState == AppState.UpdateAvailable &&
                !_configService.WasVersionNotified(notifyKey))
            {
                _trayManager.ShowUpdateNotification(config.LatestKnownVersion, config.LatestKnownCommit);
                _configService.MarkVersionNotified(notifyKey);
            }
        }
        finally
        {
            _state.IsDownloadInProgress = false;
        }
    }
    
    /// <summary>
    /// PROCEDURE: DOWNLOAD_UPDATE (core implementation)
    /// </summary>
    private async Task<bool> DownloadUpdateCoreAsync(CancellationToken ct, ProgressDialog? dialog)
    {
        try
        {
            _state.SetState(AppState.CheckingUpdate);
            dialog?.SetStatus("Checking for updates...");
            
            // 1) Check latest version
            var latest = await _updateService.CheckLatestVersionAsync(ct);
            _state.LastUpdateCheck = DateTime.Now;
            
            if (latest == null)
            {
                _state.SetState(AppState.Idle);
                return false;
            }
            
            var installedCommit = _configService.Config.InstalledCommit;
            var installedVersion = _configService.Config.InstalledVersion;
            _logger.Info($"Installed commit: {installedCommit}");
            _logger.LogVersion("Installed version", installedVersion);
            _logger.Info($"Latest commit: {latest.Version}");
            _logger.LogVersion("Latest version", latest.ProductVersion);
            
            // Get short commit hash from GitHub (for UI display)
            var latestCommitShort = await _updateService.GetShortCommitHashAsync(latest.Version, ct);
            _logger.Debug($"Latest commit short: {latestCommitShort}");
            
            // 2) Compare commits (if we have installed commit) or versions as fallback
            var hasUpdate = false;
            if (!string.IsNullOrEmpty(installedCommit))
            {
                // Primary comparison: commit hash
                hasUpdate = !string.Equals(latest.Version, installedCommit, StringComparison.OrdinalIgnoreCase);
            }
            else if (!string.IsNullOrEmpty(installedVersion))
            {
                // Fallback: version comparison
                hasUpdate = UpdateService.IsNewerVersion(latest.ProductVersion, installedVersion);
            }
            else
            {
                // No version info - assume update available
                hasUpdate = true;
            }
            
            // Always register commit->version mapping for latest
            _configService.RegisterCommitVersion(latest.Version, latest.ProductVersion);
            
            if (!hasUpdate)
            {
                _logger.Info("No update available (commit matches)");
                _configService.Update(c =>
                {
                    c.LatestKnownVersion = latest.ProductVersion;
                    c.LatestKnownCommit = latest.Version;
                    c.LatestKnownCommitShort = latestCommitShort;
                    c.LastUpdateCheckUtc = DateTime.UtcNow.ToString("o");
                    c.UpdateCount = 0;
                    c.CommitCount = 0;
                });
                
                // Check if there's a pending unpacked update ready to install
                // (downloaded earlier but not yet installed)
                var config = _configService.Config;
                var hasPendingUpdate = !string.IsNullOrEmpty(config.LastUnpackedPath) 
                    && Directory.Exists(config.LastUnpackedPath)
                    && !string.IsNullOrEmpty(config.LastUnpackedCommit)
                    && !string.IsNullOrEmpty(config.InstalledCommit)
                    && !string.Equals(config.LastUnpackedCommit, config.InstalledCommit, StringComparison.OrdinalIgnoreCase)
                    && !_configService.IsCommitSkipped(config.LastUnpackedCommit);
                
                if (hasPendingUpdate)
                {
                    _logger.Info($"No NEW update, but pending unpacked update exists: {config.LastUnpackedCommit}");
                    // Restore saved UpdateCount or default to 1
                    _state.AvailableUpdateCount = config.UpdateCount > 0 ? config.UpdateCount : 1;
                    _state.SetState(AppState.UpdateAvailable);
                }
                else
                {
                    _state.SetState(AppState.Idle);
                }
                return true;
            }
            
            // Check if we already have this update downloaded
            var existingZipPath = _configService.Config.LastDownloadZipPath;
            var alreadyDownloaded = !string.IsNullOrEmpty(existingZipPath) 
                && File.Exists(existingZipPath)
                && string.Equals(_configService.Config.LatestKnownCommit, latest.Version, StringComparison.OrdinalIgnoreCase);
            
            if (alreadyDownloaded)
            {
                _logger.Info($"Update already downloaded: {existingZipPath}");
                _configService.Update(c =>
                {
                    c.LastUpdateCheckUtc = DateTime.UtcNow.ToString("o");
                });
                // Restore saved UpdateCount or default to 1
                var config = _configService.Config;
                _state.AvailableUpdateCount = config.UpdateCount > 0 ? config.UpdateCount : 1;
                _state.SetState(AppState.UpdateAvailable);
                return true;
            }
            
            // 3) Update available - prepare to download
            _state.SetState(AppState.Downloading);
            dialog?.SetTitle("Downloading update...");
            dialog?.SetStatus($"Downloading VSCode {latest.ProductVersion}...");
            
            // Rename existing _latest to _previous
            _fileSystemService.RenameLatestToPrevious(_archiveZips);
            
            // 4) Download zip
            var timestamp = FileSystemService.GetTimestamp();
            // Use commit hash (short) for unique identification
            var commitShort = latest.Version.Length > 10 ? latest.Version[..10] : latest.Version;
            var zipFileName = $"date_{timestamp}_{latest.ProductVersion}_{commitShort}{Constants.LatestZipSuffix}";
            var zipPath = Path.Combine(_archiveZips, zipFileName);
            
            var progress = new Progress<int>(p =>
            {
                _state.UpdateDownloadProgress(p);
                dialog?.SetProgress(p);
            });
            
            var downloadSuccess = await _updateService.DownloadZipAsync(latest.Url, zipPath, progress, ct);
            if (!downloadSuccess)
            {
                _state.SetError("Download failed");
                return false;
            }
            
            // 5) Get changelog - use commit-based for insiders, version-based for stable
            dialog?.SetStatus("Fetching changelog...");
            dialog?.SetIndeterminate();
            
            string changelog;
            int updateCount;
            int commitCount = 0;
            bool isExeAgeFallback = false;
            
            if (_channel == VscodeChannel.Insiders && !string.IsNullOrEmpty(installedCommit))
            {
                // For insiders: use GitHub Compare API with commits for changelog
                changelog = await _updateService.GetChangelogForCommitsAsync(installedCommit, latest.Version, ct);
                // Count RELEASES (tags) between commits - uses smart fetching (100 first, then 800)
                updateCount = await _updateService.CountReleasesBetweenCommitsAsync(installedCommit, latest.Version, ct);
                
                // Handle fallback: -1 means commit not found in 800 tags
                if (updateCount == -1)
                {
                    // Check if exe is old enough to consider it an update
                    var exeAgeDays = _fileSystemService.GetVscodeExeAgeDays(_installDir, _channel);
                    if (exeAgeDays >= Constants.ExeAgeThresholdDays)
                    {
                        _logger.Info($"Commit not found but exe is {exeAgeDays} days old (threshold: {Constants.ExeAgeThresholdDays}) - treating as update");
                        updateCount = 0; // Unknown count
                        isExeAgeFallback = true;
                        changelog = $"⚠️ Update detected by exe age fallback\n\n" +
                                   $"Your VSCode Insiders installation ({installedCommit[..Math.Min(7, installedCommit.Length)]}) " +
                                   $"is {exeAgeDays} days old.\n" +
                                   $"The installed commit was not found in the last 800 GitHub tags.\n\n" +
                                   $"Latest available: {latest.ProductVersion} ({latest.Version[..Math.Min(7, latest.Version.Length)]})\n\n" +
                                   $"Note: Detailed changelog is not available for very old versions.";
                    }
                    else
                    {
                        _logger.Info($"Commit not found but exe is only {exeAgeDays} days old - not treating as update");
                        updateCount = 1; // Assume small update
                    }
                }
                
                // Also count commits for display (only if not fallback)
                if (!isExeAgeFallback)
                {
                    commitCount = await _updateService.CountCommitsBetweenAsync(installedCommit, latest.Version, ct);
                }
            }
            else
            {
                // For stable: use GitHub Releases with version tags
                changelog = await _updateService.GetChangelogAsync(installedVersion, latest.ProductVersion, ct);
                updateCount = _updateService.CountVersionsBetween(installedVersion, latest.ProductVersion);
            }
            
            if (updateCount == 0 && !isExeAgeFallback) updateCount = 1;
            if (commitCount == 0 && _channel == VscodeChannel.Insiders && !isExeAgeFallback) commitCount = 1;
            
            var changesFileName = $"date_{timestamp}_{latest.ProductVersion}{Constants.ChangesLatestSuffix}";
            var changesPath = Path.Combine(_archiveZips, changesFileName);
            await File.WriteAllTextAsync(changesPath, changelog, ct);
            
            // 6) Smart unpack - pre-extract the zip for faster install
            dialog?.SetStatus("Unpacking update...");
            dialog?.SetIndeterminate();
            
            var unpackProgress = new Progress<(int current, int total)>(p =>
            {
                var percent = p.total > 0 ? (p.current * 100 / p.total) : 0;
                dialog?.SetProgress(percent);
                dialog?.SetStatus($"Unpacking... {p.current:N0}/{p.total:N0} files ({percent}%)");
            });
            
            var unpackedPath = await SmartUnpackAsync(zipPath, latest.Version, latest.ProductVersion, unpackProgress);
            
            // 7) Register commit->version mapping
            _configService.RegisterCommitVersion(latest.Version, latest.ProductVersion);
            _configService.AddCommitToHistory(latest.Version);
            
            // 8) Update config
            _configService.Update(c =>
            {
                c.LatestKnownVersion = latest.ProductVersion;
                c.LatestKnownCommit = latest.Version;
                c.LatestKnownCommitShort = latestCommitShort;
                c.LastDownloadZipPath = zipPath;
                c.LastUnpackedPath = unpackedPath;
                c.LastUnpackedCommit = latest.Version;
                c.LastChangesPath = changesPath;
                c.LastUpdateCheckUtc = DateTime.UtcNow.ToString("o");
                c.UpdateCount = updateCount;
                c.CommitCount = commitCount;
            });
            
            _state.AvailableUpdateCount = updateCount;
            _state.SetState(AppState.UpdateAvailable);
            
            _logger.Info($"Update downloaded: {latest.ProductVersion} ({updateCount} releases, {commitCount} commits)");
            return true;
        }
        catch (OperationCanceledException)
        {
            _state.SetState(AppState.Idle);
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error("Download update failed", ex);
            _state.SetError(ex.Message);
            _configService.AddError(ex.Message);
            return false;
        }
    }
    
    /// <summary>
    /// Smart unpack: pre-extract zip for faster install
    /// - Skip if already unpacked with same commit
    /// - Delete old unpacked if different commit exists
    /// - Keep only one unpacked version
    /// </summary>
    private async Task<string> SmartUnpackAsync(string zipPath, string commit, string version, IProgress<(int current, int total)>? progress = null)
    {
        var commitShort = commit.Length > 10 ? commit[..10] : commit;
        var unpackedFolderName = $"{version}_{commitShort}";
        var targetPath = Path.Combine(_unpackedDir, unpackedFolderName);
        
        // Check if already unpacked with same commit
        var config = _configService.Config;
        if (!string.IsNullOrEmpty(config.LastUnpackedPath) 
            && Directory.Exists(config.LastUnpackedPath)
            && string.Equals(config.LastUnpackedCommit, commit, StringComparison.OrdinalIgnoreCase))
        {
            _logger.Info($"Update already unpacked: {config.LastUnpackedPath}");
            return config.LastUnpackedPath;
        }
        
        // Clean up any existing unpacked folders (keep only current version)
        try
        {
            foreach (var dir in Directory.GetDirectories(_unpackedDir))
            {
                if (!dir.Equals(targetPath, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.Info($"Removing old unpacked: {Path.GetFileName(dir)}");
                    Directory.Delete(dir, recursive: true);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Warn($"Failed to clean old unpacked folders: {ex.Message}");
        }
        
        // Check if target already exists (race condition protection)
        if (Directory.Exists(targetPath))
        {
            _logger.Info($"Unpacked folder already exists: {targetPath}");
            return targetPath;
        }
        
        // Extract in background with progress reporting
        _logger.Info($"Unpacking {Path.GetFileName(zipPath)} to {unpackedFolderName}...");
        var success = await Task.Run(() => _fileSystemService.ExtractZipWithProgress(zipPath, targetPath, progress));
        
        if (!success)
        {
            _logger.Error("Failed to unpack update");
            return "";
        }
        
        _logger.Info($"Update unpacked successfully: {targetPath}");
        return targetPath;
    }
    
    /// <summary>
    /// PROCEDURE: ASK_IF_INSTALLING_UPDATE
    /// </summary>
    private void ShowUpdateDialog()
    {
        var config = _configService.Config;
        
        // Check if update available by commit or version
        var hasUpdate = false;
        if (!string.IsNullOrEmpty(config.LatestKnownCommit) && !string.IsNullOrEmpty(config.InstalledCommit))
        {
            hasUpdate = !string.Equals(config.LatestKnownCommit, config.InstalledCommit, StringComparison.OrdinalIgnoreCase);
        }
        else if (!string.IsNullOrEmpty(config.LatestKnownVersion))
        {
            hasUpdate = UpdateService.IsNewerVersion(config.LatestKnownVersion, config.InstalledVersion);
        }
        
        if (!hasUpdate)
        {
            _logger.Debug("No update to show");
            return;
        }
        
        if (_configService.IsCommitSkipped(config.LatestKnownCommit))
        {
            _logger.Debug("Commit is skipped");
            return;
        }
        
        // Read changelog
        var changelog = "";
        if (File.Exists(config.LastChangesPath))
        {
            changelog = File.ReadAllText(config.LastChangesPath);
        }
        
        // Format version display with commit info for clarity
        var currentDisplay = FormatVersionDisplay(config.InstalledVersion, config.InstalledCommit, config.InstalledCommitShort);
        var newDisplay = FormatVersionDisplay(config.LatestKnownVersion, config.LatestKnownCommit, config.LatestKnownCommitShort);
        
        Application.Current.Dispatcher.Invoke(() =>
        {
            // Single instance - bring existing dialog to front
            if (_currentUpdateDialog != null && _currentUpdateDialog.IsLoaded)
            {
                _currentUpdateDialog.Activate();
                _currentUpdateDialog.Focus();
                _currentUpdateDialog.Topmost = true;
                _currentUpdateDialog.Topmost = false;
                return;
            }
            
            var dialog = new UpdateDialog();
            ConfigureWindow(dialog, "Update Available");
            var isInsiders = config.ChannelEnum == VscodeChannel.Insiders;
            dialog.SetVersionInfo(
                currentDisplay,
                newDisplay,
                config.UpdateCount,
                config.CommitCount,
                changelog,
                isInsiders);
            
            _currentUpdateDialog = dialog;
            dialog.Closed += (s, e) => _currentUpdateDialog = null;
            
            // Ensure dialog gets focus when opened
            dialog.Activate();
            dialog.Focus();
            dialog.Topmost = true;
            dialog.Topmost = false;
            
            dialog.ShowDialog();
            
            switch (dialog.Result)
            {
                case UpdateDialog.UpdateDialogResult.UpdateNow:
                    _ = InstallUpdateAsync();
                    break;
                    
                case UpdateDialog.UpdateDialogResult.SkipVersion:
                    _configService.SkipCommit(config.LatestKnownCommit);
                    _state.SetState(AppState.Idle);
                    break;
                    
                case UpdateDialog.UpdateDialogResult.UpdateLater:
                    // State remains UpdateAvailable
                    break;
            }
        });
    }
    
    /// <summary>
    /// PROCEDURE: INSTALL_UPDATE
    /// </summary>
    private async Task InstallUpdateAsync()
    {
        if (_state.IsInstallInProgress)
        {
            _logger.Warn("Install already in progress");
            return;
        }
        
        try
        {
            _state.IsInstallInProgress = true;
            
            // Check for running processes
            if (_processService.HasRunningVscodeProcesses())
            {
                var result = await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    var dialog = ConfirmDialog.CreateVscodeRunningDialog();
                    dialog.ShowDialog();
                    return dialog.PrimaryClicked;
                });
                
                if (!result)
                {
                    return; // User cancelled
                }
                
                // Kill processes - this now includes all child processes (helpers, node, rg, etc.)
                var killed = await _processService.KillAllVscodeProcessesAsync();
                if (!killed)
                {
                    _logger.Error("Failed to kill VSCode processes");
                    return;
                }
                
                // Wait for Windows to release file handles
                // VSCode spawns many child processes and Windows needs time to clean up
                // Increased delay as some systems are slow to release handles
                _logger.Info("Waiting for file handles to be released...");
                await Task.Delay(3000);
                
                // Double-check and kill any remaining processes
                if (_processService.HasRunningVscodeProcesses())
                {
                    _logger.Warn("Some VSCode processes still running, killing again...");
                    await _processService.KillAllVscodeProcessesAsync();
                    await Task.Delay(3000);
                    
                    // Triple check - sometimes Windows is really slow
                    if (_processService.HasRunningVscodeProcesses())
                    {
                        _logger.Warn("Processes STILL running after second kill, final attempt...");
                        await _processService.KillAllVscodeProcessesAsync();
                        await Task.Delay(5000);
                    }
                }
            }
            
            var config = _configService.Config;
            
            // Format version display with commit info
            var fromDisplay = FormatVersionDisplay(config.InstalledVersion, config.InstalledCommit, config.InstalledCommitShort);
            var toDisplay = FormatVersionDisplay(config.LatestKnownVersion, config.LatestKnownCommit, config.LatestKnownCommitShort);
            
            // Show progress dialog
            InstallProgressDialog? progressDialog = null;
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                progressDialog = new InstallProgressDialog();
                ConfigureWindow(progressDialog, "Installing Update");
                var isInsiders = config.ChannelEnum == VscodeChannel.Insiders;
                progressDialog.SetVersionInfo(
                    fromDisplay,
                    toDisplay,
                    config.UpdateCount,
                    config.CommitCount,
                    File.Exists(config.LastChangesPath) ? File.ReadAllText(config.LastChangesPath) : "",
                    isInsiders);
                progressDialog.Show();
            });
            
            _state.SetState(AppState.Installing);
            
            try
            {
                // NOTE: Recheck during install is disabled (too slow)
                // If needed, enable via Constants.EnableRecheckDuringInstall
                
                // 1) Ensure we have the update (unpacked or zip)
                var hasUnpacked = !string.IsNullOrEmpty(config.LastUnpackedPath) 
                    && Directory.Exists(config.LastUnpackedPath)
                    && string.Equals(config.LastUnpackedCommit, config.LatestKnownCommit, StringComparison.OrdinalIgnoreCase);
                
                if (!hasUnpacked && !File.Exists(config.LastDownloadZipPath))
                {
                    progressDialog?.SetStatus("Downloading update...");
                    await DownloadUpdateCoreAsync(CancellationToken.None, null);
                    config = _configService.Config; // Refresh
                    hasUnpacked = !string.IsNullOrEmpty(config.LastUnpackedPath) && Directory.Exists(config.LastUnpackedPath);
                }
                
                // 2) Move current installation to archive (not blocking - just move)
                progressDialog?.SetStatus("Archiving current version...");
                progressDialog?.SetProgress(20);
                
                // First, rename any existing _LAST folders to remove that suffix
                _fileSystemService.RenameLastBackupFolders(_lastInstallDir);
                
                // Ensure last-install directory exists
                if (!Directory.Exists(_lastInstallDir))
                {
                    _logger.Info($"Creating last-install directory: {_lastInstallDir}");
                    Directory.CreateDirectory(_lastInstallDir);
                }
                
                var timestamp = FileSystemService.GetTimestamp();
                // Use commit (short) for unique identification
                var commitShort = !string.IsNullOrEmpty(config.InstalledCommit) && config.InstalledCommit.Length > 10
                    ? config.InstalledCommit[..10]
                    : config.InstalledCommit ?? "unknown";
                var archiveFolderName = $"{timestamp}_{config.InstalledVersion}_{commitShort}{Constants.LastBackupSuffix}";
                var archivePath = Path.Combine(_lastInstallDir, archiveFolderName);
                
                _logger.Info($"Archive path: {archivePath}");
                
                // Use async version with status callback to prevent UI freezing
                var archiveSuccess = await _fileSystemService.MoveDirectoryAsync(
                    _installDir, 
                    archivePath,
                    status => progressDialog?.SetStatus($"Archiving current version... {status}"));
                
                if (!archiveSuccess)
                {
                    // Gather diagnostic info about what might be blocking
                    var blockingProcesses = _processService.HasRunningVscodeProcesses() 
                        ? "VSCode processes are still running!" 
                        : "No VSCode processes detected.";
                    
                    throw new Exception(
                        $"Failed to move current installation to archive.\n" +
                        $"Source: {_installDir}\n" +
                        $"Target: {archivePath}\n\n" +
                        $"{blockingProcesses}\n\n" +
                        $"Possible causes:\n" +
                        $"• File Explorer is open in the VSCode folder\n" +
                        $"• Antivirus is scanning the folder\n" +
                        $"• Windows Search is indexing files\n" +
                        $"• Another program has files open\n\n" +
                        $"Close any programs using the folder and try again.");
                }
                
                // 3) Install new version (move from unpacked or extract from zip)
                progressDialog?.SetStatus("Installing new version...");
                progressDialog?.SetProgress(50);
                
                bool installSuccess;
                if (hasUnpacked)
                {
                    // Fast path: move pre-unpacked folder (use async version)
                    _logger.Info($"Using pre-unpacked: {config.LastUnpackedPath}");
                    installSuccess = await _fileSystemService.MoveDirectoryAsync(
                        config.LastUnpackedPath, 
                        _installDir,
                        status => progressDialog?.SetStatus($"Installing new version... {status}"));
                    
                    // Clear unpacked path in config since we moved it
                    if (installSuccess)
                    {
                        _configService.Update(c =>
                        {
                            c.LastUnpackedPath = "";
                            c.LastUnpackedCommit = "";
                        });
                    }
                }
                else
                {
                    // Fallback: extract from zip
                    _logger.Info($"Extracting from zip: {config.LastDownloadZipPath}");
                    installSuccess = _fileSystemService.ExtractZip(config.LastDownloadZipPath, _installDir);
                }
                
                if (!installSuccess)
                {
                    // Rollback - restore from archive
                    _logger.Warn("Install failed, rolling back...");
                    _fileSystemService.MoveDirectory(archivePath, _installDir);
                    throw new Exception("Failed to install update - installation rolled back.");
                }
                
                // 4) Move user data from archived version to new version
                progressDialog?.SetStatus("Restoring user data...");
                progressDialog?.SetProgress(70);
                
                var archivedDataDir = Path.Combine(archivePath, Constants.VscodeDataDir);
                var newDataDir = Path.Combine(_installDir, Constants.VscodeDataDir);
                
                if (Directory.Exists(archivedDataDir))
                {
                    await _fileSystemService.MoveDirectoryAsync(
                        archivedDataDir, 
                        newDataDir,
                        status => progressDialog?.SetStatus($"Restoring user data... {status}"));
                }
                else
                {
                    _fileSystemService.EnsureDataDirectory(_installDir);
                }
                
                // 5) Open new window
                progressDialog?.SetStatus("Launching VSCode...");
                progressDialog?.SetProgress(85);
                OpenNewWindow();
                
                // 6) Create archive zip in BACKGROUND (don't block update process)
                var archiveFolderNameCopy = archiveFolderName;
                var archivePathCopy = archivePath;
                _ = Task.Run(() =>
                {
                    try
                    {
                        _logger.Info($"Creating archive zip in background: {archiveFolderNameCopy}");
                        var archiveZipPath = Path.Combine(_archiveZips, $"{archiveFolderNameCopy}.zip");
                        _fileSystemService.CreateZip(archivePathCopy, archiveZipPath);
                        _fileSystemService.CleanupOldBackups(_lastInstallDir);
                        _fileSystemService.CleanupOldZips(_archiveZips);
                        _logger.Info("Background archive zip completed");
                    }
                    catch (Exception ex)
                    {
                        _logger.Warn($"Background archive zip failed (non-critical): {ex.Message}");
                    }
                });
                
                // 7) Update config - mark as installed, register in history
                var newCommit = _configService.Config.LatestKnownCommit;
                var newCommitShort = _configService.Config.LatestKnownCommitShort;
                var newVersion = _configService.Config.LatestKnownVersion;
                
                _configService.RegisterCommitVersion(newCommit, newVersion);
                _configService.AddCommitToHistory(newCommit);
                
                _configService.Update(c =>
                {
                    c.InstalledVersion = newVersion;
                    c.InstalledCommit = newCommit;
                    c.InstalledCommitShort = newCommitShort;
                    c.UpdateCount = 0;
                    c.CommitCount = 0;
                });
                
                // Record successful install
                _configService.RecordSuccessfulInstall();
                _configService.RecordUpdateAttempt("success");
                
                progressDialog?.SetProgress(100);
                progressDialog?.Complete("Upgrade complete! VSCode is starting...");
                
                _state.SetState(AppState.Idle);
                _logger.Info("Update installed successfully");
            }
            catch (Exception ex)
            {
                _logger.Error("Install failed", ex);
                progressDialog?.ShowError(ex.Message);
                _state.SetError(ex.Message);
                _configService.AddError(ex.Message);
                _configService.RecordFailedInstall(ex.Message);
                _configService.RecordUpdateAttempt("failed", ex.Message);
            }
        }
        finally
        {
            _state.IsInstallInProgress = false;
        }
    }
    
    /// <summary>
    /// PROCEDURE: RESTORE_PREV
    /// </summary>
    private async Task RestorePreviousVersionAsync()
    {
        _logger.Info("Restore previous version requested");
        
        // Find latest backup
        var backupDir = _fileSystemService.FindLatestBackup(_lastInstallDir);
        var backupZip = backupDir == null ? _fileSystemService.FindLatestBackupZip(_archiveZips) : null;
        
        if (backupDir == null && backupZip == null)
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                MessageBox.Show(
                    "Cannot restore: No backup found.\n\n" +
                    "A backup is created when you install an update.",
                    "Restore Not Available",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            });
            return;
        }
        
        // Extract version and commit from backup name
        var backupName = Path.GetFileName(backupDir ?? backupZip ?? "");
        var (backupVersion, backupCommit) = ExtractVersionAndCommitFromBackupName(backupName);
        var currentVersion = _configService.Config.InstalledVersion;
        var currentCommit = _configService.Config.InstalledCommit;
        
        // Verify backup is different from current installation
        bool isSameVersion = false;
        if (!string.IsNullOrEmpty(backupCommit) && !string.IsNullOrEmpty(currentCommit))
        {
            // Compare by commit (most reliable)
            isSameVersion = string.Equals(backupCommit, currentCommit, StringComparison.OrdinalIgnoreCase);
        }
        else if (!string.IsNullOrEmpty(backupVersion) && !string.IsNullOrEmpty(currentVersion))
        {
            // Fallback: compare by version
            isSameVersion = string.Equals(backupVersion, currentVersion, StringComparison.OrdinalIgnoreCase);
        }
        
        if (isSameVersion)
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                MessageBox.Show(
                    "Poprzednia wersja jest już przywrócona.\n\n" +
                    "Dalszy downgrade możesz wykonać ręcznie wybierając starszą wersję z archiwum.",
                    "Restore Not Needed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            });
            _logger.Info("Restore skipped - backup version equals current version");
            return;
        }
        
        // First confirmation
        var confirm1 = await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var dialog = ConfirmDialog.CreateRestoreConfirmDialog(currentVersion, backupVersion);
            dialog.ShowDialog();
            return dialog.PrimaryClicked;
        });
        
        if (!confirm1) return;
        
        // Second confirmation
        var confirm2 = await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var dialog = ConfirmDialog.CreateRestoreConfirmDialog2(backupVersion);
            dialog.ShowDialog();
            return dialog.PrimaryClicked;
        });
        
        if (!confirm2) return;
        
        // Check for running processes
        if (_processService.HasRunningVscodeProcesses())
        {
            var killConfirm = await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var dialog = ConfirmDialog.CreateVscodeRunningDialog();
                dialog.ShowDialog();
                return dialog.PrimaryClicked;
            });
            
            if (!killConfirm) return;
            
            await _processService.KillAllVscodeProcessesAsync();
        }
        
        _state.SetState(AppState.Restoring);
        
        try
        {
            // If we only have zip, extract it first
            string actualBackupDir;
            if (backupDir != null)
            {
                actualBackupDir = backupDir;
            }
            else
            {
                // Extract zip to temp location
                var tempExtract = Path.Combine(_lastInstallDir, $"temp_restore_{DateTime.Now.Ticks}");
                _fileSystemService.ExtractZip(backupZip!, tempExtract);
                actualBackupDir = tempExtract;
            }
            
            // Move current install to reverted folder
            var timestamp = FileSystemService.GetTimestamp();
            var revertedDir = Path.Combine(_archiveRoot, Constants.RevertedVersionDir);
            Directory.CreateDirectory(revertedDir);
            var revertedPath = Path.Combine(revertedDir, $"{timestamp}_{currentVersion}{Constants.RevertedSuffix}");
            
            _fileSystemService.MoveDirectory(_installDir, revertedPath);
            
            // Move backup to install dir
            _fileSystemService.MoveDirectory(actualBackupDir, _installDir);
            
            // Ensure data directory
            _fileSystemService.EnsureDataDirectory(_installDir);
            
            // Update config - re-read commit from restored installation
            var restoredCommit = _fileSystemService.ReadVscodeCommit(_installDir, _channel);
            _configService.Update(c =>
            {
                c.InstalledVersion = backupVersion;
                c.InstalledCommit = restoredCommit ?? "";
                c.InstalledCommitShort = ""; // Will be fetched on next update check
            });
            
            // Record revert
            _configService.RecordRevert();
            
            // Delete reverted folder (cleanup)
            _ = Task.Run(() =>
            {
                try
                {
                    Directory.Delete(revertedPath, true);
                }
                catch { }
            });
            
            // Open VSCode
            OpenNewWindow();
            
            _state.SetState(AppState.Idle);
            _logger.Info($"Restored to version {backupVersion}");
            
            _trayManager.ShowNotification("Restore Complete", $"VSCode restored to version {backupVersion}");
        }
        catch (Exception ex)
        {
            _logger.Error("Restore failed", ex);
            _state.SetError(ex.Message);
            _configService.AddError(ex.Message);
            
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                MessageBox.Show(
                    $"Restore failed: {ex.Message}",
                    "Restore Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            });
        }
    }
    
    private static (string version, string commit) ExtractVersionAndCommitFromBackupName(string name)
    {
        // Format: YYMMDD_HHMMss_VERSION_COMMITSHORT_LAST or YYMMDD_HHMMss_VERSION_LAST (legacy)
        // Example: 250120_143022_1.95.1_abc1234567_LAST
        var parts = name.Split('_');
        string version = "Unknown";
        string commit = "";
        
        if (parts.Length >= 3)
        {
            // parts[0] = date (YYMMDD)
            // parts[1] = time (HHMMss)
            // parts[2] = version
            // parts[3] = commit (optional, 10 chars) or LAST
            // parts[4] = LAST (if commit present)
            
            // Version is always at index 2
            version = parts[2];
            
            // Check if index 3 is commit (not LAST and not .zip extension)
            if (parts.Length >= 4)
            {
                var potentialCommit = parts[3].Replace(".zip", "");
                if (!potentialCommit.Equals("LAST", StringComparison.OrdinalIgnoreCase) &&
                    potentialCommit.Length >= 7) // Git short hash is at least 7 chars
                {
                    commit = potentialCommit;
                }
            }
        }
        
        return (version, commit);
    }
    
    /// <summary>
    /// Kill all managed VSCode processes
    /// </summary>
    private async Task KillAllWindowsAsync()
    {
        _logger.Info("Kill all windows requested");
        
        // Ask for confirmation first using ConfirmDialog (not MessageBox which disappears)
        var confirmed = await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var dialog = ConfirmDialog.Create(
                "Confirm Kill All",
                "Are you sure you want to kill all VSCode windows?\n\nUnsaved work may be lost!",
                "Kill All",
                "Cancel");
            ConfigureWindow(dialog, "Confirm");
            dialog.ShowDialog();
            return dialog.PrimaryClicked;
        });
        
        if (!confirmed)
        {
            _logger.Info("Kill all windows cancelled by user");
            return;
        }
        
        var success = await _processService.KillAllVscodeProcessesAsync();
        
        if (!success)
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                MessageBox.Show(
                    "Some VSCode processes could not be terminated.",
                    "Warning",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            });
        }
    }
    
    /// <summary>
    /// Show About and Stats dialog
    /// </summary>
    private void ShowAboutDialog()
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            var dialog = new AboutStatsDialog();
            ConfigureWindow(dialog, "About");
            dialog.ShowDialog();
        });
    }
    
    /// <summary>
    /// Exit the application
    /// </summary>
    private void ExitApplication()
    {
        _logger.Info("Exit requested");
        Application.Current.Shutdown();
    }
    
    public void Dispose()
    {
        _logger?.Info("Application shutting down");
        
        _backgroundTimer?.Stop();
        _trayManager?.Dispose();
        _ipcService?.Dispose();
    }
}
