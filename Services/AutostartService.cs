using Microsoft.Win32;

namespace AdgVscodeManager.Services;

/// <summary>
/// Service for managing Windows autostart registry
/// </summary>
public class AutostartService
{
    private readonly LoggingService _logger;
    private readonly string _exePath;
    
    public AutostartService(LoggingService logger)
    {
        _logger = logger;
        _exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName 
            ?? throw new InvalidOperationException("Cannot determine executable path");
    }
    
    /// <summary>
    /// Check if autostart is currently enabled
    /// </summary>
    public bool IsAutostartEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(Constants.AutostartRegistryPath, false);
            if (key == null)
                return false;
            
            var value = key.GetValue(Constants.AutostartValueName) as string;
            return !string.IsNullOrEmpty(value);
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to check autostart status", ex);
            return false;
        }
    }
    
    /// <summary>
    /// Enable autostart with Windows
    /// </summary>
    public bool EnableAutostart()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(Constants.AutostartRegistryPath, true);
            if (key == null)
            {
                _logger.Error("Cannot open registry key for writing");
                return false;
            }
            
            // Add --autostart argument for silent startup
            var command = $"\"{_exePath}\" --autostart";
            key.SetValue(Constants.AutostartValueName, command, RegistryValueKind.String);
            
            _logger.Info($"Autostart enabled: {command}");
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to enable autostart", ex);
            return false;
        }
    }
    
    /// <summary>
    /// Disable autostart with Windows
    /// </summary>
    public bool DisableAutostart()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(Constants.AutostartRegistryPath, true);
            if (key == null)
            {
                return true; // No key means already disabled
            }
            
            key.DeleteValue(Constants.AutostartValueName, false);
            
            _logger.Info("Autostart disabled");
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to disable autostart", ex);
            return false;
        }
    }
    
    /// <summary>
    /// Toggle autostart state
    /// </summary>
    public bool ToggleAutostart()
    {
        if (IsAutostartEnabled())
        {
            return DisableAutostart();
        }
        else
        {
            return EnableAutostart();
        }
    }
}
