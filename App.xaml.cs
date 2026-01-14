using System.Windows;
using AdgVscodeManager.Services;

namespace AdgVscodeManager;

/// <summary>
/// Application class - WPF entry point
/// </summary>
public partial class App : Application
{
    private AppManager? _appManager;
    
    public ConfigService ConfigService => _appManager?.ConfigService!;
    
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        
        // Handle unhandled exceptions
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        
        // Parse command line arguments
        var isAutoStart = e.Args.Contains("--autostart", StringComparer.OrdinalIgnoreCase);
        
        try
        {
            _appManager = new AppManager(isAutoStart);
            
            if (!_appManager.Initialize())
            {
                // Second instance or error - exit gracefully
                Shutdown(0);
                return;
            }
            
            // Start the application
            _appManager.Start();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to start ADG VSCode Manager:\n\n{ex.Message}", 
                "Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
    
    private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        LogException("Dispatcher", e.Exception);
        e.Handled = true; // Prevent app crash
    }
    
    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            LogException("AppDomain", ex);
        }
    }
    
    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LogException("Task", e.Exception);
        e.SetObserved(); // Prevent app crash
    }
    
    private void LogException(string source, Exception ex)
    {
        try
        {
            var logDir = System.IO.Path.Combine(AppContext.BaseDirectory, "version-history", "logs");
            System.IO.Directory.CreateDirectory(logDir);
            var logPath = System.IO.Path.Combine(logDir, $"crash_{DateTime.Now:yyyyMMdd_HHmmss}.log");
            System.IO.File.WriteAllText(logPath, $"[{source}] {ex}");
        }
        catch { /* ignore */ }
        
        MessageBox.Show($"Błąd ({source}):\n\n{ex.Message}\n\n{ex.StackTrace}", 
            "ADG VSCode Manager - Error", MessageBoxButton.OK, MessageBoxImage.Error);
    }
    
    protected override void OnExit(ExitEventArgs e)
    {
        _appManager?.Dispose();
        base.OnExit(e);
    }
}
