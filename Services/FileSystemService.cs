using System.IO;
using System.IO.Compression;
using System.Diagnostics;

namespace AdgVscodeManager.Services;

/// <summary>
/// Service for file system operations (backup, restore, unzip)
/// </summary>
public class FileSystemService
{
    private readonly LoggingService _logger;
    
    public FileSystemService(LoggingService logger)
    {
        _logger = logger;
    }
    
    /// <summary>
    /// Retry delays in milliseconds for file operations after killing processes
    /// Increased significantly to give Windows more time to release file handles
    /// Total retry time: 2+3+5+8+10+15 = 43 seconds
    /// </summary>
    private static readonly int[] RetryDelaysMs = [2000, 3000, 5000, 8000, 10000, 15000];
    
    /// <summary>
    /// Wait for directory to become accessible (no locked files)
    /// </summary>
    public async Task<bool> WaitForDirectoryUnlockedAsync(string path, int timeoutMs = 15000)
    {
        // Check for orphaned test directories from previous failed attempts
        var parentDir = Path.GetDirectoryName(path);
        var baseName = Path.GetFileName(path);
        if (!string.IsNullOrEmpty(parentDir) && Directory.Exists(parentDir))
        {
            var orphaned = Directory.GetDirectories(parentDir, baseName + "_unlock_test_*");
            if (orphaned.Length > 0 && !Directory.Exists(path))
            {
                // Found orphaned test directory and original doesn't exist - restore it
                try
                {
                    _logger.Warn($"Restoring orphaned directory: {orphaned[0]} -> {path}");
                    Directory.Move(orphaned[0], path);
                }
                catch (Exception ex)
                {
                    _logger.Error($"Failed to restore orphaned directory: {ex.Message}");
                }
            }
        }
        
        if (!Directory.Exists(path))
            return true;
            
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        var checkInterval = 500;
        
        while (DateTime.UtcNow < deadline)
        {
            if (IsDirectoryUnlocked(path))
            {
                _logger.Info($"Directory is unlocked: {path}");
                return true;
            }
            
            _logger.Info($"Directory still locked, waiting {checkInterval}ms...");
            await Task.Delay(checkInterval);
            checkInterval = Math.Min(checkInterval + 500, 2000); // Increase interval up to 2s
        }
        
        _logger.Warn($"Directory still locked after {timeoutMs}ms timeout");
        return false;
    }
    
    /// <summary>
    /// Check if directory can be accessed (no locked files blocking rename/move)
    /// We check by trying to rename - if it works, we rename back immediately
    /// IMPORTANT: Must handle the case where first rename succeeds but second fails!
    /// </summary>
    private bool IsDirectoryUnlocked(string path)
    {
        // First check if directory even exists
        if (!Directory.Exists(path))
        {
            // Directory doesn't exist - check if there's an orphaned test rename
            var parentDir = Path.GetDirectoryName(path);
            var baseName = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parentDir) && Directory.Exists(parentDir))
            {
                var orphaned = Directory.GetDirectories(parentDir, baseName + "_unlock_test_*");
                if (orphaned.Length > 0)
                {
                    // Found orphaned test directory - try to rename it back
                    try
                    {
                        _logger.Warn($"Found orphaned unlock test directory, restoring: {orphaned[0]}");
                        Directory.Move(orphaned[0], path);
                        return true; // Successfully restored, directory is now unlocked
                    }
                    catch
                    {
                        return false; // Still locked
                    }
                }
            }
            return true; // Directory doesn't exist at all, consider it "unlocked"
        }
        
        var tempName = path + "_unlock_test_" + Guid.NewGuid().ToString("N")[..8];
        
        try
        {
            // Try to rename the directory - this will fail if files are locked
            Directory.Move(path, tempName);
        }
        catch
        {
            // First rename failed - directory is locked
            return false;
        }
        
        // First rename succeeded - now rename back
        try
        {
            Directory.Move(tempName, path);
            return true; // Both renames succeeded - directory is unlocked
        }
        catch
        {
            // Second rename failed! This is bad - we need to keep trying to restore
            _logger.Error($"CRITICAL: First rename succeeded but second failed! Directory stuck as: {tempName}");
            
            // Try a few more times to restore
            for (int i = 0; i < 5; i++)
            {
                Thread.Sleep(500);
                try
                {
                    Directory.Move(tempName, path);
                    _logger.Info("Successfully restored directory after retry");
                    return true;
                }
                catch { }
            }
            
            // Still failed - return false, the orphan check above will handle it next iteration
            return false;
        }
    }

    /// <summary>
    /// Extract zip file to destination directory with retry logic
    /// </summary>
    public bool ExtractZip(string zipPath, string destinationDir, bool cleanDestination = false)
    {
        Exception? lastException = null;
        
        for (int attempt = 0; attempt <= RetryDelaysMs.Length; attempt++)
        {
            try
            {
                if (attempt > 0)
                {
                    var delay = RetryDelaysMs[attempt - 1];
                    _logger.Info($"Retry {attempt}/{RetryDelaysMs.Length} after {delay}ms cooldown...");
                    Thread.Sleep(delay);
                }
                
                _logger.Info($"Extracting: {zipPath} -> {destinationDir}");
                
                if (cleanDestination && Directory.Exists(destinationDir))
                {
                    // Move to temp location first for safety
                    var tempBackup = destinationDir + "_temp_backup_" + DateTime.Now.Ticks;
                    Directory.Move(destinationDir, tempBackup);
                    
                    try
                    {
                        Directory.CreateDirectory(destinationDir);
                        ZipFile.ExtractToDirectory(zipPath, destinationDir);
                        
                        // Success - delete temp backup
                        Directory.Delete(tempBackup, true);
                    }
                    catch
                    {
                        // Restore from temp backup
                        if (Directory.Exists(destinationDir))
                            Directory.Delete(destinationDir, true);
                        Directory.Move(tempBackup, destinationDir);
                        throw;
                    }
                }
                else
                {
                    if (!Directory.Exists(destinationDir))
                    {
                        Directory.CreateDirectory(destinationDir);
                    }
                    
                    ZipFile.ExtractToDirectory(zipPath, destinationDir, overwriteFiles: true);
                }
                
                _logger.Info("Extraction completed");
                return true;
            }
            catch (IOException ex)
            {
                lastException = ex;
                _logger.Warn($"Extract attempt {attempt + 1} failed (IO): {ex.Message}");
            }
            catch (UnauthorizedAccessException ex)
            {
                lastException = ex;
                _logger.Warn($"Extract attempt {attempt + 1} failed (Access): {ex.Message}");
            }
            catch (Exception ex)
            {
                // Non-retryable error
                _logger.Error("Extraction failed", ex);
                return false;
            }
        }
        
        _logger.Error($"Extraction failed after {RetryDelaysMs.Length + 1} attempts", lastException);
        return false;
    }
    
    /// <summary>
    /// Create zip from directory
    /// </summary>
    public bool CreateZip(string sourceDir, string zipPath)
    {
        try
        {
            _logger.Info($"Creating zip: {sourceDir} -> {zipPath}");
            
            var dir = Path.GetDirectoryName(zipPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            
            if (File.Exists(zipPath))
            {
                File.Delete(zipPath);
            }
            
            ZipFile.CreateFromDirectory(sourceDir, zipPath, CompressionLevel.Optimal, false);
            
            _logger.Info("Zip created");
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to create zip", ex);
            return false;
        }
    }
    
    /// <summary>
    /// Move directory safely with retry logic
    /// </summary>
    public bool MoveDirectory(string source, string destination)
    {
        // First, wait for source to be unlocked (synchronously for compatibility)
        var unlockTask = WaitForDirectoryUnlockedAsync(source, 15000);
        unlockTask.Wait();
        
        if (!unlockTask.Result)
        {
            _logger.Warn($"Source directory may still be locked: {source}");
            // Continue anyway - retry logic will handle it
        }
        
        return MoveDirectoryWithRetry(source, destination, RetryDelaysMs);
    }
    
    /// <summary>
    /// Move directory safely with retry logic (async version with status callback)
    /// </summary>
    public async Task<bool> MoveDirectoryAsync(string source, string destination, Action<string>? statusCallback = null)
    {
        // Ensure parent directory of destination exists
        var destParent = Path.GetDirectoryName(destination);
        if (!string.IsNullOrEmpty(destParent) && !Directory.Exists(destParent))
        {
            _logger.Info($"Creating destination parent directory: {destParent}");
            Directory.CreateDirectory(destParent);
        }
        
        // First, wait for source to be unlocked (increased timeout to 30 seconds)
        statusCallback?.Invoke("Waiting for file handles...");
        var unlocked = await WaitForDirectoryUnlockedAsync(source, 30000);
        
        if (!unlocked)
        {
            _logger.Warn($"Source directory may still be locked after 30s: {source}");
            // Continue anyway - retry logic will handle it
        }
        
        return await MoveDirectoryWithRetryAsync(source, destination, RetryDelaysMs, statusCallback);
    }
    
    /// <summary>
    /// Move directory with configurable retry delays
    /// </summary>
    private bool MoveDirectoryWithRetry(string source, string destination, int[] retryDelays)
    {
        Exception? lastException = null;
        
        for (int attempt = 0; attempt <= retryDelays.Length; attempt++)
        {
            try
            {
                if (attempt > 0)
                {
                    var delay = retryDelays[attempt - 1];
                    _logger.Info($"Retry {attempt}/{retryDelays.Length} after {delay}ms cooldown...");
                    Thread.Sleep(delay);
                }
                
                _logger.Info($"Moving directory: {source} -> {destination}");
                
                var destDir = Path.GetDirectoryName(destination);
                if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                {
                    Directory.CreateDirectory(destDir);
                }
                
                if (Directory.Exists(destination))
                {
                    Directory.Delete(destination, true);
                }
                
                Directory.Move(source, destination);
                
                _logger.Info("Directory moved");
                return true;
            }
            catch (IOException ex)
            {
                lastException = ex;
                _logger.Warn($"Move attempt {attempt + 1} failed (IO): {ex.Message}");
            }
            catch (UnauthorizedAccessException ex)
            {
                lastException = ex;
                _logger.Warn($"Move attempt {attempt + 1} failed (Access): {ex.Message}");
            }
            catch (Exception ex)
            {
                // Non-retryable error
                _logger.Error("Failed to move directory", ex);
                return false;
            }
        }
        
        _logger.Error($"Failed to move directory after {retryDelays.Length + 1} attempts", lastException);
        return false;
    }
    
    /// <summary>
    /// Move directory with configurable retry delays (async version with status callback)
    /// </summary>
    private async Task<bool> MoveDirectoryWithRetryAsync(string source, string destination, int[] retryDelays, Action<string>? statusCallback = null)
    {
        Exception? lastException = null;
        
        for (int attempt = 0; attempt <= retryDelays.Length; attempt++)
        {
            try
            {
                if (attempt > 0)
                {
                    var delay = retryDelays[attempt - 1];
                    _logger.Info($"Retrying {attempt}/{retryDelays.Length} after {delay}ms cooldown...");
                    statusCallback?.Invoke($"Retrying... ({attempt}/{retryDelays.Length})");
                    await Task.Delay(delay);
                }
                
                _logger.Info($"Moving directory: {source} -> {destination}");
                
                var destDir = Path.GetDirectoryName(destination);
                if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                {
                    _logger.Info($"Creating destination directory: {destDir}");
                    Directory.CreateDirectory(destDir);
                }
                
                if (Directory.Exists(destination))
                {
                    Directory.Delete(destination, true);
                }
                
                Directory.Move(source, destination);
                
                _logger.Info("Directory moved");
                return true;
            }
            catch (IOException ex)
            {
                lastException = ex;
                _logger.Warn($"Move attempt {attempt + 1} failed (IO): {ex.Message}");
            }
            catch (UnauthorizedAccessException ex)
            {
                lastException = ex;
                _logger.Warn($"Move attempt {attempt + 1} failed (Access): {ex.Message}");
            }
            catch (Exception ex)
            {
                // Non-retryable error
                _logger.Error("Failed to move directory", ex);
                return false;
            }
        }
        
        _logger.Error($"Failed to move directory after {retryDelays.Length + 1} attempts", lastException);
        return false;
    }
    
    /// <summary>
    /// Copy directory recursively
    /// </summary>
    public bool CopyDirectory(string source, string destination)
    {
        try
        {
            _logger.Info($"Copying directory: {source} -> {destination}");
            
            if (!Directory.Exists(destination))
            {
                Directory.CreateDirectory(destination);
            }
            
            // Copy files
            foreach (var file in Directory.GetFiles(source))
            {
                var destFile = Path.Combine(destination, Path.GetFileName(file));
                File.Copy(file, destFile, true);
            }
            
            // Copy subdirectories
            foreach (var dir in Directory.GetDirectories(source))
            {
                var destDir = Path.Combine(destination, Path.GetFileName(dir));
                CopyDirectory(dir, destDir);
            }
            
            _logger.Info("Directory copied");
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to copy directory", ex);
            return false;
        }
    }
    
    /// <summary>
    /// Delete directory safely
    /// </summary>
    public bool DeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                _logger.Info($"Deleting directory: {path}");
                Directory.Delete(path, true);
            }
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to delete directory", ex);
            return false;
        }
    }
    
    /// <summary>
    /// Rename existing _latest.zip to _previous.zip
    /// </summary>
    public void RenameLatestToPrevious(string zipsDir)
    {
        try
        {
            var latestFiles = Directory.GetFiles(zipsDir, "*" + Constants.LatestZipSuffix);
            foreach (var file in latestFiles)
            {
                var newName = file.Replace(Constants.LatestZipSuffix, 
                    $"_{DateTime.Now:HHmmss}" + Constants.PreviousZipSuffix);
                _logger.Info($"Renaming: {file} -> {newName}");
                File.Move(file, newName);
            }
            
            // Also rename _changes_latest.txt
            var changesFiles = Directory.GetFiles(zipsDir, "*" + Constants.ChangesLatestSuffix);
            foreach (var file in changesFiles)
            {
                var newName = file.Replace(Constants.ChangesLatestSuffix, 
                    $"_{DateTime.Now:HHmmss}_changes_previous.txt");
                File.Move(file, newName);
            }
        }
        catch (Exception ex)
        {
            _logger.Warn($"Failed to rename latest to previous: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Generate timestamp string for filenames
    /// </summary>
    public static string GetTimestamp()
    {
        return DateTime.Now.ToString("yyMMdd_HHmmss");
    }
    
    /// <summary>
    /// Rename existing _LAST backup folders to remove the _LAST suffix
    /// This is done before creating a new backup so only one folder has _LAST
    /// </summary>
    public void RenameLastBackupFolders(string lastInstallDir)
    {
        try
        {
            if (!Directory.Exists(lastInstallDir))
                return;
            
            var lastBackups = Directory.GetDirectories(lastInstallDir, "*" + Constants.LastBackupSuffix);
            foreach (var dir in lastBackups)
            {
                var newName = dir.Replace(Constants.LastBackupSuffix, "");
                if (!Directory.Exists(newName))
                {
                    _logger.Info($"Renaming backup: {Path.GetFileName(dir)} -> {Path.GetFileName(newName)}");
                    Directory.Move(dir, newName);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to rename old backup folders", ex);
        }
    }
    
    /// <summary>
    /// Find the latest backup directory
    /// </summary>
    public string? FindLatestBackup(string lastInstallDir)
    {
        try
        {
            if (!Directory.Exists(lastInstallDir))
                return null;
            
            var backups = Directory.GetDirectories(lastInstallDir, "*" + Constants.LastBackupSuffix)
                .OrderByDescending(d => d)
                .ToList();
            
            return backups.FirstOrDefault();
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to find latest backup", ex);
            return null;
        }
    }
    
    /// <summary>
    /// Find the latest backup zip file
    /// </summary>
    public string? FindLatestBackupZip(string zipsDir)
    {
        try
        {
            if (!Directory.Exists(zipsDir))
                return null;
            
            var zips = Directory.GetFiles(zipsDir, "*" + Constants.LastBackupSuffix + ".zip")
                .OrderByDescending(f => f)
                .ToList();
            
            return zips.FirstOrDefault();
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to find latest backup zip", ex);
            return null;
        }
    }
    
    /// <summary>
    /// Read VSCode commit hash from bin/code or bin/code-insiders script
    /// </summary>
    public string? ReadVscodeCommit(string installDir, VscodeChannel channel)
    {
        try
        {
            var binScriptName = channel == VscodeChannel.Insiders 
                ? Constants.VscodeBinScriptInsider 
                : Constants.VscodeBinScriptRelease;
            
            var binScriptPath = Path.Combine(installDir, "bin", binScriptName);
            
            if (!File.Exists(binScriptPath))
            {
                _logger.Warn($"Bin script not found: {binScriptPath}");
                return null;
            }
            
            // Read the script file and find COMMIT="..." line
            var lines = File.ReadAllLines(binScriptPath);
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("COMMIT="))
                {
                    // Extract commit hash from COMMIT="hash" or COMMIT='hash' or COMMIT=hash
                    var value = trimmed.Substring(7).Trim();
                    // Remove quotes if present
                    if ((value.StartsWith("\"") && value.EndsWith("\"")) ||
                        (value.StartsWith("'") && value.EndsWith("'")))
                    {
                        value = value.Substring(1, value.Length - 2);
                    }
                    _logger.Info($"Read commit from bin script: {value}");
                    return value;
                }
            }
            
            _logger.Warn("COMMIT line not found in bin script");
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to read VSCode commit from bin script", ex);
        }
        
        return null;
    }
    
    /// <summary>
    /// Read VSCode version from product.json (fallback method)
    /// </summary>
    public string? ReadVscodeVersion(string installDir)
    {
        try
        {
            // Standard location: resources/app/product.json
            var productJsonPath = Path.Combine(installDir, "resources", "app", "product.json");
            
            if (!File.Exists(productJsonPath))
            {
                // Insiders pattern: commit-hash-folder/resources/app/product.json
                // Look for subdirectories that contain resources/app/product.json
                if (Directory.Exists(installDir))
                {
                    foreach (var subDir in Directory.GetDirectories(installDir))
                    {
                        var subDirName = Path.GetFileName(subDir);
                        // Skip known folders like bin, data, appx, etc.
                        if (subDirName is "bin" or "data" or "appx" or "locales" or "policies" or "resources" or "tools")
                            continue;
                        
                        // Check if this looks like a commit hash folder (alphanumeric, 8+ chars)
                        if (subDirName.Length >= 8 && subDirName.All(c => char.IsLetterOrDigit(c)))
                        {
                            var insidersPath = Path.Combine(subDir, "resources", "app", "product.json");
                            if (File.Exists(insidersPath))
                            {
                                _logger.Debug($"Found product.json in commit subfolder: {subDirName}");
                                productJsonPath = insidersPath;
                                break;
                            }
                        }
                    }
                }
            }
            
            if (!File.Exists(productJsonPath))
            {
                // Last fallback: product.json in root
                productJsonPath = Path.Combine(installDir, "product.json");
            }
            
            if (!File.Exists(productJsonPath))
            {
                _logger.Warn("product.json not found in any expected location");
                return null;
            }
            
            var json = File.ReadAllText(productJsonPath);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            
            if (doc.RootElement.TryGetProperty("version", out var version))
            {
                return version.GetString();
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to read VSCode version", ex);
        }
        
        return null;
    }
    
    /// <summary>
    /// Ensure data directory exists for portable mode
    /// </summary>
    public void EnsureDataDirectory(string installDir)
    {
        var dataDir = Path.Combine(installDir, Constants.VscodeDataDir);
        if (!Directory.Exists(dataDir))
        {
            _logger.Info($"Creating data directory: {dataDir}");
            Directory.CreateDirectory(dataDir);
        }
    }
    
    /// <summary>
    /// Check if portable data directory exists
    /// </summary>
    public bool HasDataDirectory(string installDir)
    {
        return Directory.Exists(Path.Combine(installDir, Constants.VscodeDataDir));
    }
    
    /// <summary>
    /// Clean up old backups to prevent disk space issues
    /// </summary>
    public void CleanupOldBackups(string lastInstallDir, int keepCount = 5)
    {
        try
        {
            if (!Directory.Exists(lastInstallDir))
                return;
            
            var backups = Directory.GetDirectories(lastInstallDir)
                .OrderByDescending(d => d)
                .Skip(keepCount)
                .ToList();
            
            foreach (var backup in backups)
            {
                _logger.Info($"Cleaning up old backup: {backup}");
                Directory.Delete(backup, true);
            }
        }
        catch (Exception ex)
        {
            _logger.Warn($"Failed to cleanup old backups: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Clean up old zips to prevent disk space issues
    /// </summary>
    public void CleanupOldZips(string zipsDir, int keepCount = 10)
    {
        try
        {
            if (!Directory.Exists(zipsDir))
                return;
            
            var zips = Directory.GetFiles(zipsDir, "*.zip")
                .Where(f => f.Contains(Constants.PreviousZipSuffix))
                .OrderByDescending(f => f)
                .Skip(keepCount)
                .ToList();
            
            foreach (var zip in zips)
            {
                _logger.Info($"Cleaning up old zip: {zip}");
                File.Delete(zip);
            }
        }
        catch (Exception ex)
        {
            _logger.Warn($"Failed to cleanup old zips: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Get the age of VSCode executable in days
    /// Returns the number of days since the exe was last modified
    /// </summary>
    public int GetVscodeExeAgeDays(string installDir, VscodeChannel channel)
    {
        try
        {
            var exeName = channel == VscodeChannel.Insiders 
                ? Constants.VscodeExeInsider 
                : Constants.VscodeExeRelease;
            
            var exePath = Path.Combine(installDir, exeName);
            
            if (!File.Exists(exePath))
            {
                _logger.Warn($"VSCode exe not found: {exePath}");
                return -1; // Unknown
            }
            
            var lastModified = File.GetLastWriteTimeUtc(exePath);
            var ageDays = (int)(DateTime.UtcNow - lastModified).TotalDays;
            
            _logger.Info($"VSCode exe age: {ageDays} days (last modified: {lastModified:yyyy-MM-dd HH:mm})");
            return ageDays;
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to get VSCode exe age: {ex.Message}");
            return -1;
        }
    }
    
    /// <summary>
    /// Check if VSCode installation is older than specified days based on exe modification date
    /// </summary>
    public bool IsVscodeExeOlderThan(string installDir, VscodeChannel channel, int days)
    {
        var ageDays = GetVscodeExeAgeDays(installDir, channel);
        return ageDays >= days;
    }
}
