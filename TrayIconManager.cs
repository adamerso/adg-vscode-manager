using System.IO;
using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AdgVscodeManager.Models;
using AdgVscodeManager.Services;
using AdgVscodeManager.Views;
using H.NotifyIcon;

namespace AdgVscodeManager;

/// <summary>
/// Manages the system tray icon and context menu
/// </summary>
public class TrayIconManager : IDisposable
{
    private readonly TaskbarIcon _trayIcon;
    private readonly RuntimeState _state;
    private readonly VscodeChannel _channel;
    private readonly AutostartService _autostartService;
    private readonly ProcessService _processService;
    private readonly LoggingService _logger;
    private readonly ConfigService _configService;
    
    // Icons for blinking
    private readonly Icon _colorIcon;
    private readonly Icon _blinkIcon;  // Dark gray for blinking
    private DispatcherTimer? _blinkTimer;
    private bool _showingColorIcon = true;
    private bool _isDisposed;
    
    // Menu items that need updating
    private MenuItem? _updateStatusItem;
    private MenuItem? _autostartItem;
    private MenuItem? _lastAttemptItem;
    private MenuItem? _autoApplyItem;
    private MenuItem? _openWindowNoneRunningItem;
    private MenuItem? _openWindowAlreadyRunningItem;
    
    public event EventHandler? RunNewWindowRequested;
    public event EventHandler? KillAllWindowsRequested;
    public event EventHandler? CheckUpdateRequested;
    public event EventHandler? BringProgressToFrontRequested;
    public event EventHandler? RestoreRequested;
    public event EventHandler? ExitRequested;
    public event EventHandler? AboutRequested;
    
    public TrayIconManager(
        RuntimeState state,
        VscodeChannel channel,
        Icon colorIcon,
        Icon blinkIcon,
        AutostartService autostartService,
        ProcessService processService,
        LoggingService logger,
        ConfigService configService)
    {
        _state = state;
        _channel = channel;
        _colorIcon = colorIcon;
        _blinkIcon = blinkIcon;
        _autostartService = autostartService;
        _processService = processService;
        _logger = logger;
        _configService = configService;
        
        _trayIcon = new TaskbarIcon
        {
            Icon = colorIcon,
            ToolTipText = GetTooltipText(),
            ContextMenu = CreateContextMenu(),
            Visibility = System.Windows.Visibility.Visible
        };
        
        // Force icon to appear
        _trayIcon.ForceCreate();
        
        // Handle double-click - show quick menu
        _trayIcon.TrayMouseDoubleClick += OnTrayDoubleClick;
        
        // Handle left click - log for debugging (first click issue)
        _trayIcon.TrayLeftMouseUp += (s, e) => _logger.Debug("Tray left click up");
        
        // Handle notification click - open update dialog
        _trayIcon.TrayBalloonTipClicked += OnNotificationClicked;
        
        // Subscribe to state changes
        _state.StateChanged += OnStateChanged;
        _state.DownloadProgressChanged += OnDownloadProgressChanged;
        
        _logger.Info("Tray icon initialized");
    }
    
    // ============== BLINKING CONFIGURATION START ==============
    // To adjust blinking behavior, modify the values below:
    // - COLOR_DURATION_MS: How long the color icon is shown (longer = less aggressive)
    // - BLINK_DURATION_MS: How long the blink icon is shown (shorter = subtle flash)
    // - _blinkIcon: The icon shown during blink (see IconExtractor.GetBlinkIcon())
    private const int COLOR_DURATION_MS = 2137;  // Color icon visible for 700ms
    private const int BLINK_DURATION_MS = 70;  // Blink icon visible for 70ms
    // ============== BLINKING CONFIGURATION END ================
    
    /// <summary>
    /// Start blinking the tray icon (color then blink)
    /// </summary>
    private void StartBlinking()
    {
        if (_blinkTimer != null)
            return;
        
        // Start with color icon
        _trayIcon.UpdateIcon(_colorIcon);
        _showingColorIcon = true;
        
        _blinkTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(COLOR_DURATION_MS)
        };
        _blinkTimer.Tick += OnBlinkTick;
        _blinkTimer.Start();
    }
    
    /// <summary>
    /// Stop blinking and restore color icon
    /// </summary>
    private void StopBlinking()
    {
        if (_blinkTimer == null)
            return;
        
        _blinkTimer.Stop();
        _blinkTimer.Tick -= OnBlinkTick;
        _blinkTimer = null;
        
        // Restore color icon
        _trayIcon.UpdateIcon(_colorIcon);
        _showingColorIcon = true;
    }
    
    private void OnBlinkTick(object? sender, EventArgs e)
    {
        if (_isDisposed || _blinkTimer == null)
            return;
        
        try
        {
            if (_showingColorIcon)
            {
                _trayIcon.UpdateIcon(_blinkIcon);
                _blinkTimer.Interval = TimeSpan.FromMilliseconds(BLINK_DURATION_MS);
            }
            else
            {
                _trayIcon.UpdateIcon(_colorIcon);
                _blinkTimer.Interval = TimeSpan.FromMilliseconds(COLOR_DURATION_MS);
            }
            _showingColorIcon = !_showingColorIcon;
        }
        catch (ObjectDisposedException)
        {
            StopBlinking();
        }
        catch (Exception ex)
        {
            _logger.Error("OnBlinkTick error", ex);
        }
    }
    
    private void OnTrayDoubleClick(object sender, RoutedEventArgs e)
    {
        _logger.Debug("Tray double-click - showing quick menu");
        ShowQuickMenu();
    }
    
    private void OnNotificationClicked(object sender, RoutedEventArgs e)
    {
        _logger.Debug("Notification clicked - opening update dialog");
        // Open update dialog when user clicks on notification balloon
        if (_state.CurrentState == AppState.UpdateAvailable)
        {
            CheckUpdateRequested?.Invoke(this, EventArgs.Empty);
        }
    }
    
    private void ShowQuickMenu()
    {
        var quickMenu = new ContextMenu();
        
        // Option 1: New Window (default, bold)
        var newWindowItem = new MenuItem 
        { 
            Header = "New VSCode Window",
            FontWeight = FontWeights.Bold
        };
        newWindowItem.Click += (s, e) => RunNewWindowRequested?.Invoke(this, EventArgs.Empty);
        quickMenu.Items.Add(newWindowItem);
        
        // Option 2: Updates status/action
        var updateItem = new MenuItem();
        switch (_state.CurrentState)
        {
            case AppState.UpdateAvailable:
                var count = _state.AvailableUpdateCount;
                updateItem.Header = count > 1 
                    ? $"Install {count} updates now"
                    : "Install update now";
                updateItem.Click += (s, e) => CheckUpdateRequested?.Invoke(this, EventArgs.Empty);
                break;
                
            case AppState.Downloading:
                updateItem.Header = $"Downloading... {_state.DownloadProgress}%";
                updateItem.IsEnabled = false;
                break;
                
            case AppState.CheckingUpdate:
                updateItem.Header = "Checking for updates...";
                updateItem.IsEnabled = false;
                break;
                
            default:
                updateItem.Header = "Check for updates";
                updateItem.Click += (s, e) => CheckUpdateRequested?.Invoke(this, EventArgs.Empty);
                break;
        }
        quickMenu.Items.Add(updateItem);
        
        // Show at cursor position
        quickMenu.IsOpen = true;
    }
    
    private string GetTooltipText()
    {
        var channelText = _channel == VscodeChannel.Insiders ? "Insiders" : "Release";
        return $"ADG VSCode Manager ({channelText})";
    }
    
    private ContextMenu CreateContextMenu()
    {
        var menu = new ContextMenu();
        
        // Version info (disabled, grayed out)
        var channelSuffix = _channel == VscodeChannel.Insiders ? " Insiders" : "";
        var versionItem = new MenuItem 
        { 
            Header = $"{Constants.AppName} v{Constants.AppVersion}{channelSuffix}",
            IsEnabled = false,
            FontStyle = FontStyles.Italic
        };
        menu.Items.Add(versionItem);
        
        menu.Items.Add(new Separator());
        
        // Run new window
        var runItem = new MenuItem { Header = "Run New VSCode Window" };
        runItem.Click += (s, e) => RunNewWindowRequested?.Invoke(this, EventArgs.Empty);
        runItem.FontWeight = FontWeights.Bold;
        menu.Items.Add(runItem);
        
        menu.Items.Add(new Separator());
        
        // Kill all windows
        var killItem = new MenuItem { Header = "Kill All VSCode Windows" };
        killItem.Click += (s, e) => KillAllWindowsRequested?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(killItem);
        
        menu.Items.Add(new Separator());
        
        // Last update attempt (disabled, grayed out)
        _lastAttemptItem = new MenuItem
        {
            Header = GetLastAttemptText(),
            IsEnabled = false,
            FontStyle = FontStyles.Italic
        };
        menu.Items.Add(_lastAttemptItem);
        
        // Update status
        _updateStatusItem = new MenuItem { Header = "Update Status: Idle" };
        _updateStatusItem.Click += OnUpdateStatusClick;
        menu.Items.Add(_updateStatusItem);
        
        // Restore previous
        var restoreItem = new MenuItem { Header = "Restore Previous Version" };
        restoreItem.Click += (s, e) => RestoreRequested?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(restoreItem);
        
        menu.Items.Add(new Separator());
        
        // Autostart toggle
        _autostartItem = new MenuItem 
        { 
            Header = GetAutostartMenuText(),
            IsCheckable = false
        };
        _autostartItem.Click += OnAutostartClick;
        menu.Items.Add(_autostartItem);
        
        menu.Items.Add(new Separator());
        
        // ============== REOPEN BEHAVIOR SUBMENU ==============
        var reopenBehaviorHeader = new MenuItem
        {
            Header = "Manager Reopen Behavior",
            IsEnabled = false,
            FontStyle = FontStyles.Italic
        };
        menu.Items.Add(reopenBehaviorHeader);
        
        var usefulToPinHeader = new MenuItem
        {
            Header = "  (useful to pin manager to Start menu)",
            IsEnabled = false,
            FontSize = 11
        };
        menu.Items.Add(usefulToPinHeader);
        
        // When NO VSCode is running
        var noVscodeHeader = new MenuItem
        {
            Header = "  When no VSCode is running:",
            IsEnabled = false,
            FontStyle = FontStyles.Italic
        };
        menu.Items.Add(noVscodeHeader);
        
        _autoApplyItem = new MenuItem
        {
            Header = GetAutoApplyMenuText(),
            IsCheckable = false
        };
        _autoApplyItem.Click += OnAutoApplyClick;
        menu.Items.Add(_autoApplyItem);
        
        _openWindowNoneRunningItem = new MenuItem
        {
            Header = GetOpenWindowNoneRunningMenuText(),
            IsCheckable = false
        };
        _openWindowNoneRunningItem.Click += OnOpenWindowNoneRunningClick;
        menu.Items.Add(_openWindowNoneRunningItem);
        
        // When VSCode IS running
        var vscodeRunningHeader = new MenuItem
        {
            Header = "  When VSCode is already running:",
            IsEnabled = false,
            FontStyle = FontStyles.Italic
        };
        menu.Items.Add(vscodeRunningHeader);
        
        _openWindowAlreadyRunningItem = new MenuItem
        {
            Header = GetOpenWindowAlreadyRunningMenuText(),
            IsCheckable = false
        };
        _openWindowAlreadyRunningItem.Click += OnOpenWindowAlreadyRunningClick;
        menu.Items.Add(_openWindowAlreadyRunningItem);
        // ============== END REOPEN BEHAVIOR SUBMENU ==============
        
        menu.Items.Add(new Separator());
        
        // About and Stats
        var aboutItem = new MenuItem { Header = "About and Stats..." };
        aboutItem.Click += OnAboutClick;
        menu.Items.Add(aboutItem);
        
        menu.Items.Add(new Separator());
        
        // Exit
        var exitItem = new MenuItem { Header = "Exit" };
        exitItem.Click += (s, e) => ExitRequested?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(exitItem);
        
        return menu;
    }
    
    private string GetAutostartMenuText()
    {
        var enabled = _autostartService.IsAutostartEnabled();
        return enabled 
            ? "✓ Autostart with Windows [Active]" 
            : "  Autostart with Windows [Inactive]";
    }
    
    private void OnAutostartClick(object sender, RoutedEventArgs e)
    {
        _autostartService.ToggleAutostart();
        UpdateAutostartMenuItem();
    }
    
    private void UpdateAutostartMenuItem()
    {
        if (_autostartItem != null)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                _autostartItem.Header = GetAutostartMenuText();
            });
        }
    }
    
    // ============== REOPEN BEHAVIOR MENU METHODS ==============
    private string GetAutoApplyMenuText()
    {
        var enabled = _configService.Config.AutoApplyUpdateOnStart;
        return enabled 
            ? "    ✓ Auto-apply pending update [Active]" 
            : "      Auto-apply pending update [Inactive]";
    }
    
    private string GetOpenWindowNoneRunningMenuText()
    {
        var enabled = _configService.Config.OpenWindowWhenNoneRunning;
        return enabled 
            ? "    ✓ Open new window [Active]" 
            : "      Open new window [Inactive]";
    }
    
    private string GetOpenWindowAlreadyRunningMenuText()
    {
        var enabled = _configService.Config.OpenWindowWhenAlreadyRunning;
        return enabled 
            ? "    ✓ Open new window [Active]" 
            : "      Open new window [Inactive]";
    }
    
    private void OnAutoApplyClick(object sender, RoutedEventArgs e)
    {
        _configService.Update(c => c.AutoApplyUpdateOnStart = !c.AutoApplyUpdateOnStart);
        UpdateReopenBehaviorMenuItems();
    }
    
    private void OnOpenWindowNoneRunningClick(object sender, RoutedEventArgs e)
    {
        _configService.Update(c => c.OpenWindowWhenNoneRunning = !c.OpenWindowWhenNoneRunning);
        UpdateReopenBehaviorMenuItems();
    }
    
    private void OnOpenWindowAlreadyRunningClick(object sender, RoutedEventArgs e)
    {
        _configService.Update(c => c.OpenWindowWhenAlreadyRunning = !c.OpenWindowWhenAlreadyRunning);
        UpdateReopenBehaviorMenuItems();
    }
    
    private void UpdateReopenBehaviorMenuItems()
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            if (_autoApplyItem != null)
                _autoApplyItem.Header = GetAutoApplyMenuText();
            if (_openWindowNoneRunningItem != null)
                _openWindowNoneRunningItem.Header = GetOpenWindowNoneRunningMenuText();
            if (_openWindowAlreadyRunningItem != null)
                _openWindowAlreadyRunningItem.Header = GetOpenWindowAlreadyRunningMenuText();
        });
    }
    // ============== END REOPEN BEHAVIOR MENU METHODS ==============
    
    private void OnUpdateStatusClick(object sender, RoutedEventArgs e)
    {
        switch (_state.CurrentState)
        {
            case AppState.Idle:
                // Click to check for updates
                CheckUpdateRequested?.Invoke(this, EventArgs.Empty);
                break;
                
            case AppState.CheckingUpdate:
            case AppState.Downloading:
                // Bring progress dialog to front
                BringProgressToFrontRequested?.Invoke(this, EventArgs.Empty);
                break;
                
            case AppState.UpdateAvailable:
                CheckUpdateRequested?.Invoke(this, EventArgs.Empty);
                break;
                
            case AppState.Error:
                // Click on error = retry update check
                CheckUpdateRequested?.Invoke(this, EventArgs.Empty);
                break;
        }
    }
    
    private string GetLastCheckText()
    {
        if (_state.LastUpdateCheck == null)
            return "";
        
        var elapsed = DateTime.Now - _state.LastUpdateCheck.Value;
        if (elapsed.TotalMinutes < 1)
            return " (just now)";
        if (elapsed.TotalMinutes < 60)
            return $" ({(int)elapsed.TotalMinutes} min ago)";
        if (elapsed.TotalHours < 24)
            return $" ({(int)elapsed.TotalHours} hr ago)";
        return $" ({(int)elapsed.TotalDays} days ago)";
    }
    
    private void OnStateChanged(object? sender, AppState newState)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            UpdateMenuForState(newState);
            
            // Handle blinking for update available state
            if (newState == AppState.UpdateAvailable)
            {
                StartBlinking();
            }
            else
            {
                StopBlinking();
            }
        });
    }
    
    private void OnDownloadProgressChanged(object? sender, int progress)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            if (_state.CurrentState == AppState.Downloading && _updateStatusItem != null)
            {
                _updateStatusItem.Header = $"Downloading update ({progress}%)... click for details";
            }
        });
    }
    
    private void UpdateMenuForState(AppState state)
    {
        if (_updateStatusItem == null) return;
        
        switch (state)
        {
            case AppState.Idle:
                var lastCheckText = GetLastCheckText();
                _updateStatusItem.Header = $"No updates available{lastCheckText}";
                _updateStatusItem.IsEnabled = true; // Allow click to check now
                break;
                
            case AppState.CheckingUpdate:
                _updateStatusItem.Header = "Update Status: Checking...";
                _updateStatusItem.IsEnabled = true;
                break;
                
            case AppState.Downloading:
                _updateStatusItem.Header = $"Downloading update ({_state.DownloadProgress}%)... click for details";
                _updateStatusItem.IsEnabled = true;
                break;
                
            case AppState.UpdateAvailable:
                var count = _state.AvailableUpdateCount;
                _updateStatusItem.Header = count > 1 
                    ? $"{count} updates available - click to install"
                    : "Update available - click to install";
                _updateStatusItem.IsEnabled = true;
                break;
                
            case AppState.Installing:
                _updateStatusItem.Header = "Update Status: Installing...";
                _updateStatusItem.IsEnabled = false;
                break;
                
            case AppState.Restoring:
                _updateStatusItem.Header = "Update Status: Restoring...";
                _updateStatusItem.IsEnabled = false;
                break;
                
            case AppState.Error:
                _updateStatusItem.Header = $"Update failed: {TruncateError(_state.LastError)} - click to retry";
                _updateStatusItem.IsEnabled = true;
                break;
        }
        
        // Also update tooltip
        _trayIcon.ToolTipText = GetTooltipText() + $"\n{_updateStatusItem.Header}";
    }
    
    private static string TruncateError(string error)
    {
        const int maxLen = 50;
        if (string.IsNullOrEmpty(error)) return "Unknown error";
        return error.Length > maxLen ? error[..maxLen] + "..." : error;
    }
    
    /// <summary>
    /// Show a balloon notification
    /// </summary>
    public void ShowNotification(string title, string message, H.NotifyIcon.Core.NotificationIcon icon = H.NotifyIcon.Core.NotificationIcon.Info)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            _trayIcon.ShowNotification(title, message, icon);
        });
    }
    
    /// <summary>
    /// Show update available notification
    /// </summary>
    public void ShowUpdateNotification(string version, string? commit = null)
    {
        var commitShort = !string.IsNullOrEmpty(commit) && commit.Length > 10 
            ? commit[..10] 
            : commit ?? "";
        
        var message = !string.IsNullOrEmpty(commitShort)
            ? $"Version {version} ({commitShort}) is ready to install."
            : $"Version {version} is ready to install.";
            
        ShowNotification(
            "VSCode Update Available",
            message,
            H.NotifyIcon.Core.NotificationIcon.Info
        );
    }
    
    private string GetLastAttemptText()
    {
        var config = _configService.Config;
        if (string.IsNullOrEmpty(config.LastUpdateAttemptUtc) || 
            !DateTime.TryParse(config.LastUpdateAttemptUtc, out var attemptTime))
        {
            return "Last update attempt: never";
        }
        
        var timeStr = attemptTime.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        var status = config.LastUpdateAttemptStatus ?? "unknown";
        
        if (status == "success" || status == "canceled")
        {
            return $"Last update attempt: {timeStr} - {status}";
        }
        else if (status == "failed")
        {
            var reason = !string.IsNullOrEmpty(config.LastUpdateAttemptReason) 
                ? config.LastUpdateAttemptReason 
                : "unknown error";
            return $"Last update attempt: {timeStr} - failed: {reason}";
        }
        
        return $"Last update attempt: {timeStr} - {status}";
    }
    
    private void OnAboutClick(object sender, RoutedEventArgs e)
    {
        AboutRequested?.Invoke(this, EventArgs.Empty);
    }
    
    public void Dispose()
    {
        _isDisposed = true;
        StopBlinking();
        _state.StateChanged -= OnStateChanged;
        _state.DownloadProgressChanged -= OnDownloadProgressChanged;
        try { _trayIcon.Dispose(); } catch { }
    }
}
