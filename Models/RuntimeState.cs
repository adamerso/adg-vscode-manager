namespace AdgVscodeManager.Models;

/// <summary>
/// Runtime state of the application (not persisted)
/// </summary>
public class RuntimeState
{
    /// <summary>
    /// Current state machine state
    /// </summary>
    public AppState CurrentState { get; set; } = AppState.Idle;
    
    /// <summary>
    /// Previous state (for error recovery)
    /// </summary>
    public AppState PreviousState { get; set; } = AppState.Idle;
    
    /// <summary>
    /// Whether download is currently in progress
    /// </summary>
    public bool IsDownloadInProgress { get; set; } = false;
    
    /// <summary>
    /// Whether install is currently in progress (guard against recursion)
    /// </summary>
    public bool IsInstallInProgress { get; set; } = false;
    
    /// <summary>
    /// Current download progress (0-100)
    /// </summary>
    public int DownloadProgress { get; set; } = 0;
    
    /// <summary>
    /// Current operation status message
    /// </summary>
    public string StatusMessage { get; set; } = "";
    
    /// <summary>
    /// Last error message
    /// </summary>
    public string LastError { get; set; } = "";
    
    /// <summary>
    /// Cancellation token source for current operation
    /// </summary>
    public CancellationTokenSource? CurrentOperationCts { get; set; }
    
    /// <summary>
    /// Whether startup requires opening a window after download completes/cancels
    /// </summary>
    public bool StartupRequiresWindow { get; set; } = false;
    
    /// <summary>
    /// Whether running in autostart mode (--autostart argument)
    /// </summary>
    public bool IsAutoStartMode { get; set; } = false;
    
    /// <summary>
    /// Whether running in silent mode (background check)
    /// </summary>
    public bool IsSilentMode { get; set; } = false;
    
    /// <summary>
    /// Number of available updates
    /// </summary>
    public int AvailableUpdateCount { get; set; } = 0;
    
    /// <summary>
    /// Last update check time
    /// </summary>
    public DateTime? LastUpdateCheck { get; set; }
    
    /// <summary>
    /// Set state and track previous
    /// </summary>
    public void SetState(AppState newState)
    {
        PreviousState = CurrentState;
        CurrentState = newState;
        StateChanged?.Invoke(this, newState);
    }
    
    /// <summary>
    /// Set error state with message
    /// </summary>
    public void SetError(string errorMessage)
    {
        LastError = errorMessage;
        SetState(AppState.Error);
    }
    
    /// <summary>
    /// Clear error and return to previous state
    /// </summary>
    public void ClearError()
    {
        LastError = "";
        SetState(PreviousState);
    }
    
    /// <summary>
    /// Event fired when state changes
    /// </summary>
    public event EventHandler<AppState>? StateChanged;
    
    /// <summary>
    /// Event fired when download progress changes
    /// </summary>
    public event EventHandler<int>? DownloadProgressChanged;
    
    /// <summary>
    /// Update download progress and fire event
    /// </summary>
    public void UpdateDownloadProgress(int progress)
    {
        DownloadProgress = progress;
        DownloadProgressChanged?.Invoke(this, progress);
    }
}
