using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;

namespace AdgVscodeManager.Services;

/// <summary>
/// Service for inter-process communication and single instance management
/// </summary>
public class IpcService : IDisposable
{
    private readonly string _mutexName;
    private readonly string _pipeName;
    private readonly LoggingService _logger;
    private Mutex? _mutex;
    private CancellationTokenSource? _serverCts;
    private Task? _serverTask;
    
    public event EventHandler? OpenNewWindowRequested;
    public event EventHandler? BringToFrontRequested;
    
    public bool IsFirstInstance { get; private set; }
    
    public IpcService(string workDir, VscodeChannel channel, LoggingService logger)
    {
        _logger = logger;
        
        // Create unique names based on workdir + channel
        var identifier = $"{workDir}_{channel}".ToLowerInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identifier)))[..16];
        
        _mutexName = $"{Constants.MutexPrefix}{hash}";
        _pipeName = $"{Constants.PipePrefix}{hash}";
        
        _logger.Debug($"IPC mutex name: {_mutexName}");
        _logger.Debug($"IPC pipe name: {_pipeName}");
    }
    
    /// <summary>
    /// Try to acquire single instance lock
    /// </summary>
    public bool TryAcquireLock()
    {
        try
        {
            _mutex = new Mutex(true, _mutexName, out var createdNew);
            IsFirstInstance = createdNew;
            
            if (createdNew)
            {
                _logger.Info("Single instance lock acquired (first instance)");
            }
            else
            {
                _logger.Info("Another instance is already running");
            }
            
            return createdNew;
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to acquire single instance lock", ex);
            return false;
        }
    }
    
    /// <summary>
    /// Start the IPC server (call only if first instance)
    /// </summary>
    public void StartServer()
    {
        if (!IsFirstInstance)
        {
            _logger.Warn("Cannot start server - not first instance");
            return;
        }
        
        _serverCts = new CancellationTokenSource();
        _serverTask = Task.Run(() => ServerLoop(_serverCts.Token));
        _logger.Info("IPC server started");
    }
    
    /// <summary>
    /// Send command to the first instance
    /// </summary>
    public async Task<bool> SendCommandAsync(string command, int timeoutMs = 5000)
    {
        try
        {
            _logger.Info($"Sending IPC command: {command}");
            
            using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out);
            
            var connectTask = client.ConnectAsync(timeoutMs);
            if (await Task.WhenAny(connectTask, Task.Delay(timeoutMs)) != connectTask)
            {
                _logger.Error("IPC connection timeout");
                return false;
            }
            
            await connectTask;
            
            using var writer = new StreamWriter(client);
            await writer.WriteLineAsync(command);
            await writer.FlushAsync();
            
            _logger.Info("IPC command sent successfully");
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to send IPC command", ex);
            return false;
        }
    }
    
    /// <summary>
    /// Send OPEN_NEW_WINDOW command to first instance
    /// </summary>
    public Task<bool> SendOpenNewWindowAsync()
    {
        return SendCommandAsync(Constants.IpcOpenNewWindow);
    }
    
    private async Task ServerLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(_pipeName, PipeDirection.In, 
                    NamedPipeServerStream.MaxAllowedServerInstances, 
                    PipeTransmissionMode.Byte, 
                    PipeOptions.Asynchronous);
                
                await server.WaitForConnectionAsync(ct);
                
                using var reader = new StreamReader(server);
                var command = await reader.ReadLineAsync(ct);
                
                if (!string.IsNullOrEmpty(command))
                {
                    _logger.Info($"IPC command received: {command}");
                    HandleCommand(command);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.Error("IPC server error", ex);
                await Task.Delay(1000, ct); // Prevent tight loop on repeated errors
            }
        }
    }
    
    private void HandleCommand(string command)
    {
        switch (command)
        {
            case Constants.IpcOpenNewWindow:
                OpenNewWindowRequested?.Invoke(this, EventArgs.Empty);
                break;
                
            case Constants.IpcBringToFront:
                BringToFrontRequested?.Invoke(this, EventArgs.Empty);
                break;
                
            default:
                _logger.Warn($"Unknown IPC command: {command}");
                break;
        }
    }
    
    public void Dispose()
    {
        _serverCts?.Cancel();
        
        try
        {
            _serverTask?.Wait(1000);
        }
        catch { }
        
        _serverCts?.Dispose();
        
        // Only release mutex if we are the first instance (we acquired it)
        if (IsFirstInstance && _mutex != null)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // Mutex was not owned by this thread - ignore
            }
        }
        _mutex?.Dispose();
    }
}
