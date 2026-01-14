using System.IO;

namespace AdgVscodeManager.Services;

/// <summary>
/// Service for logging application events and errors
/// </summary>
public class LoggingService
{
    private readonly string _logsDir;
    private readonly object _lock = new();
    private string _currentLogPath = "";
    private DateTime _currentLogDate;
    
    public LoggingService(string logsDir)
    {
        _logsDir = logsDir;
        EnsureLogDirectory();
        UpdateLogPath();
    }
    
    private void EnsureLogDirectory()
    {
        try
        {
            if (!Directory.Exists(_logsDir))
            {
                Directory.CreateDirectory(_logsDir);
            }
        }
        catch
        {
            // If we can't create logs dir, we'll fail silently on logging
        }
    }
    
    private void UpdateLogPath()
    {
        var today = DateTime.UtcNow.Date;
        if (_currentLogDate != today)
        {
            _currentLogDate = today;
            _currentLogPath = Path.Combine(_logsDir, 
                $"{Constants.LogFilePrefix}{today:yyMMdd}.log");
        }
    }
    
    public void Log(string level, string message, Exception? ex = null)
    {
        lock (_lock)
        {
            try
            {
                UpdateLogPath();
                var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff");
                var logLine = $"[{timestamp}] [{level}] {message}";
                
                if (ex != null)
                {
                    logLine += $"\n  Exception: {ex.GetType().Name}: {ex.Message}";
                    if (ex.StackTrace != null)
                    {
                        logLine += $"\n  StackTrace: {ex.StackTrace}";
                    }
                }
                
                File.AppendAllText(_currentLogPath, logLine + Environment.NewLine);
            }
            catch
            {
                // Fail silently if logging fails
            }
        }
    }
    
    public void Info(string message) => Log("INFO", message);
    public void Warn(string message) => Log("WARN", message);
    public void Error(string message, Exception? ex = null) => Log("ERROR", message, ex);
    public void Debug(string message) => Log("DEBUG", message);
    
    public void LogStateChange(AppState oldState, AppState newState)
    {
        Info($"State change: {oldState} -> {newState}");
    }
    
    public void LogDownload(string url, string destination)
    {
        Info($"Download started: {url} -> {destination}");
    }
    
    public void LogVersion(string label, string version)
    {
        Info($"{label}: {version}");
    }
    
    public void LogPath(string label, string path)
    {
        Info($"{label}: {path}");
    }
}
