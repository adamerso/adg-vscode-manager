using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AdgVscodeManager.Services;

/// <summary>
/// Service for managing VSCode processes
/// </summary>
public class ProcessService
{
    private readonly string _installDir;
    private readonly string _exeName;
    private readonly LoggingService _logger;
    
    public ProcessService(string installDir, VscodeChannel channel, LoggingService logger)
    {
        _installDir = installDir;
        _exeName = channel == VscodeChannel.Insiders 
            ? Constants.VscodeExeInsider 
            : Constants.VscodeExeRelease;
        _logger = logger;
    }
    
    /// <summary>
    /// Get path to VSCode executable
    /// </summary>
    public string GetVscodeExePath()
    {
        return Path.Combine(_installDir, _exeName);
    }
    
    /// <summary>
    /// Check if VSCode executable exists
    /// </summary>
    public bool VscodeExists()
    {
        return File.Exists(GetVscodeExePath());
    }
    
    /// <summary>
    /// Get all VSCode processes belonging to our installation
    /// This includes all child processes (helpers, node, rg, etc.) not just the main Code process
    /// </summary>
    public List<Process> GetManagedVscodeProcesses()
    {
        var result = new List<Process>();
        var normalizedInstallDir = NormalizePath(_installDir);
        
        try
        {
            // Get ALL processes and check if their executable is inside our install directory
            // This catches Code, Code Helper, rg (ripgrep), node, and any extension-spawned processes
            foreach (var process in Process.GetProcesses())
            {
                try
                {
                    var exePath = GetProcessPath(process);
                    if (!string.IsNullOrEmpty(exePath))
                    {
                        var normalizedExePath = NormalizePath(exePath);
                        if (normalizedExePath.StartsWith(normalizedInstallDir, StringComparison.OrdinalIgnoreCase))
                        {
                            result.Add(process);
                        }
                    }
                }
                catch
                {
                    // Skip processes we can't access
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to enumerate VSCode processes", ex);
        }
        
        // Log what we found for debugging
        if (result.Count > 0)
        {
            _logger.Info($"Found {result.Count} managed processes: {string.Join(", ", result.Select(p => $"{p.ProcessName}({p.Id})"))}");
        }
        
        return result;
    }
    
    /// <summary>
    /// Check if any VSCode processes from our installation are running
    /// </summary>
    public bool HasRunningVscodeProcesses()
    {
        return GetManagedVscodeProcesses().Count > 0;
    }
    
    /// <summary>
    /// Start a new VSCode window
    /// </summary>
    public bool OpenNewWindow()
    {
        try
        {
            var exePath = GetVscodeExePath();
            if (!File.Exists(exePath))
            {
                _logger.Error($"VSCode executable not found: {exePath}");
                return false;
            }
            
            _logger.Info($"Starting VSCode: {exePath}");
            
            var startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = true,
                WorkingDirectory = _installDir
            };
            
            Process.Start(startInfo);
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to start VSCode", ex);
            return false;
        }
    }
    
    /// <summary>
    /// Kill all managed VSCode processes
    /// </summary>
    public async Task<bool> KillAllVscodeProcessesAsync()
    {
        var processes = GetManagedVscodeProcesses();
        if (processes.Count == 0)
        {
            return true;
        }
        
        _logger.Info($"Killing {processes.Count} VSCode process(es)");
        
        // Try graceful close first
        foreach (var process in processes)
        {
            try
            {
                process.CloseMainWindow();
            }
            catch { }
        }
        
        // Wait for graceful close
        var deadline = DateTime.UtcNow.AddMilliseconds(Constants.ProcessKillTimeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(100);
            processes = GetManagedVscodeProcesses();
            if (processes.Count == 0)
            {
                _logger.Info("All VSCode processes closed gracefully");
                return true;
            }
        }
        
        // Force kill remaining
        foreach (var process in processes)
        {
            try
            {
                if (!process.HasExited)
                {
                    _logger.Warn($"Force killing process: {process.Id}");
                    process.Kill();
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to kill process {process.Id}", ex);
            }
        }
        
        // Final check
        await Task.Delay(500);
        return GetManagedVscodeProcesses().Count == 0;
    }
    
    /// <summary>
    /// Get the full path of a process executable
    /// </summary>
    private static string? GetProcessPath(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch
        {
            // Try alternative method for access denied
            return GetProcessPathFromWmi(process.Id);
        }
    }
    
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool QueryFullProcessImageNameW(
        IntPtr hProcess, 
        int dwFlags, 
        [Out] char[] lpExeName, 
        ref int lpdwSize);
    
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(int dwDesiredAccess, bool bInheritHandle, int dwProcessId);
    
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);
    
    private const int PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    
    private static string? GetProcessPathFromWmi(int processId)
    {
        IntPtr handle = IntPtr.Zero;
        try
        {
            handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
            if (handle == IntPtr.Zero)
                return null;
            
            var buffer = new char[1024];
            var size = buffer.Length;
            
            if (QueryFullProcessImageNameW(handle, 0, buffer, ref size))
            {
                return new string(buffer, 0, size);
            }
        }
        catch
        {
        }
        finally
        {
            if (handle != IntPtr.Zero)
                CloseHandle(handle);
        }
        
        return null;
    }
    
    private static string NormalizePath(string path)
    {
        return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
