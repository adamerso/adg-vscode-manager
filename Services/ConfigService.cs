using System.IO;
using System.Text.Json;
using AdgVscodeManager.Models;

namespace AdgVscodeManager.Services;

/// <summary>
/// Service for managing application configuration
/// </summary>
public class ConfigService
{
    private readonly string _configPath;
    private readonly LoggingService _logger;
    private readonly object _lock = new();
    private AppConfig _config;
    
    /// <summary>
    /// Set to true if config version is incompatible (too new)
    /// </summary>
    public bool ConfigVersionIncompatible { get; private set; }
    
    /// <summary>
    /// The incompatible version string (if any)
    /// </summary>
    public string? IncompatibleVersion { get; private set; }
    
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    
    public AppConfig Config => _config;
    
    public ConfigService(string configPath, LoggingService logger)
    {
        _configPath = configPath;
        _logger = logger;
        _config = new AppConfig();
        Load();
    }
    
    /// <summary>
    /// Load configuration from disk
    /// </summary>
    public void Load()
    {
        lock (_lock)
        {
            try
            {
                ConfigVersionIncompatible = false;
                IncompatibleVersion = null;
                
                if (File.Exists(_configPath))
                {
                    var json = File.ReadAllText(_configPath);
                    var loaded = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);
                    
                    // Validate config - must have a valid channel property
                    if (loaded != null && IsValidConfig(loaded))
                    {
                        // Check config version compatibility
                        var versionCheck = CheckConfigVersion(loaded.ConfigVersion);
                        
                        if (versionCheck == ConfigVersionStatus.TooNew)
                        {
                            // Config was created by newer version - flag for user action
                            _logger.Error($"Config version {loaded.ConfigVersion} is newer than max supported {Constants.MaxConfigVersion}");
                            ConfigVersionIncompatible = true;
                            IncompatibleVersion = loaded.ConfigVersion;
                            _config = new AppConfig(); // Use empty config for now
                            return;
                        }
                        
                        _config = loaded;
                        
                        // Upgrade config version if needed
                        if (versionCheck == ConfigVersionStatus.Old || string.IsNullOrEmpty(_config.ConfigVersion))
                        {
                            _logger.Info($"Upgrading config version from {_config.ConfigVersion ?? "null"} to {Constants.CurrentConfigVersion}");
                            _config.ConfigVersion = Constants.CurrentConfigVersion;
                            Save();
                        }
                        
                        _logger.Info($"Config loaded from: {_configPath} (version: {_config.ConfigVersion})");
                    }
                    else
                    {
                        _logger.Warn("Config file has invalid format, creating new config");
                        _config = new AppConfig();
                        
                        // Backup old invalid config
                        var backupPath = _configPath + ".invalid";
                        try { File.Move(_configPath, backupPath, overwrite: true); }
                        catch { }
                    }
                }
                else
                {
                    _config = new AppConfig();
                    _logger.Info("No existing config found, using defaults");
                }
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to load config", ex);
                _config = new AppConfig();
            }
        }
    }
    
    private enum ConfigVersionStatus { Ok, Old, TooNew }
    
    /// <summary>
    /// Check if config version is compatible
    /// </summary>
    private static ConfigVersionStatus CheckConfigVersion(string? configVersion)
    {
        if (string.IsNullOrEmpty(configVersion))
            return ConfigVersionStatus.Old; // No version = old config, will be upgraded
        
        // Parse versions for comparison
        if (!Version.TryParse(configVersion, out var cfgVer))
            return ConfigVersionStatus.Old;
        
        if (!Version.TryParse(Constants.MaxConfigVersion, out var maxVer))
            return ConfigVersionStatus.Ok;
        
        if (cfgVer > maxVer)
            return ConfigVersionStatus.TooNew;
        
        if (!Version.TryParse(Constants.MinConfigVersion, out var minVer))
            return ConfigVersionStatus.Ok;
        
        if (cfgVer < minVer)
            return ConfigVersionStatus.Old;
        
        return ConfigVersionStatus.Ok;
    }
    
    /// <summary>
    /// Delete config file and reload (reset to defaults)
    /// </summary>
    public void DeleteAndReload()
    {
        lock (_lock)
        {
            try
            {
                if (File.Exists(_configPath))
                {
                    var backupPath = _configPath + $".backup_{DateTime.Now:yyyyMMdd_HHmmss}";
                    File.Move(_configPath, backupPath, overwrite: true);
                    _logger.Info($"Config backed up to: {backupPath}");
                }
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to backup config before delete", ex);
            }
            
            _config = new AppConfig();
            ConfigVersionIncompatible = false;
            IncompatibleVersion = null;
            Save();
            _logger.Info("Config reset to defaults");
        }
    }
    
    /// <summary>
    /// Validate that config has expected structure (not some other JSON format)
    /// </summary>
    private static bool IsValidConfig(AppConfig config)
    {
        // Channel must be "release" or "insiders" (default is "release")
        var validChannels = new[] { "release", "insiders" };
        return validChannels.Contains(config.Channel?.ToLowerInvariant() ?? "");
    }
    
    /// <summary>
    /// Save configuration to disk
    /// </summary>
    public void Save()
    {
        lock (_lock)
        {
            try
            {
                var dir = Path.GetDirectoryName(_configPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                
                var json = JsonSerializer.Serialize(_config, JsonOptions);
                File.WriteAllText(_configPath, json);
                _logger.Debug("Config saved");
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to save config", ex);
            }
        }
    }
    
    /// <summary>
    /// Update configuration with action and save
    /// </summary>
    public void Update(Action<AppConfig> updateAction)
    {
        lock (_lock)
        {
            updateAction(_config);
            Save();
        }
    }
    
    /// <summary>
    /// Set the channel and save
    /// </summary>
    public void SetChannel(VscodeChannel channel)
    {
        Update(c => c.Channel = channel == VscodeChannel.Insiders ? "insiders" : "release");
    }
    
    /// <summary>
    /// Add error to error log
    /// </summary>
    public void AddError(string message)
    {
        Update(c =>
        {
            c.Errors.Add(new ErrorEntry
            {
                TimeUtc = DateTime.UtcNow.ToString("o"),
                Message = message
            });
            
            // Keep only last 100 errors
            while (c.Errors.Count > 100)
            {
                c.Errors.RemoveAt(0);
            }
        });
    }
    
    /// <summary>
    /// Check if commit should be skipped
    /// </summary>
    public bool IsCommitSkipped(string commit)
    {
        if (string.IsNullOrEmpty(commit)) return false;
        var shortCommit = commit.Length > 12 ? commit[..12] : commit;
        return _config.SkippedCommits.Any(c => c.StartsWith(shortCommit) || shortCommit.StartsWith(c));
    }
    
    /// <summary>
    /// Skip a commit
    /// </summary>
    public void SkipCommit(string commit)
    {
        if (string.IsNullOrEmpty(commit)) return;
        var shortCommit = commit.Length > 12 ? commit[..12] : commit;
        
        if (!IsCommitSkipped(shortCommit))
        {
            Update(c => c.SkippedCommits.Add(shortCommit));
        }
    }
    
    /// <summary>
    /// Legacy: Check if version should be skipped
    /// </summary>
    [Obsolete("Use IsCommitSkipped instead")]
    public bool IsVersionSkipped(string version)
    {
        return _config.SkippedVersions.Contains(version);
    }
    
    /// <summary>
    /// Legacy: Skip a version
    /// </summary>
    [Obsolete("Use SkipCommit instead")]
    public void SkipVersion(string version)
    {
        if (!IsVersionSkipped(version))
        {
            Update(c => c.SkippedVersions.Add(version));
        }
    }
    
    /// <summary>
    /// Check if version was already notified
    /// </summary>
    public bool WasVersionNotified(string version)
    {
        return _config.NotifiedVersions.Contains(version);
    }
    
    /// <summary>
    /// Mark version as notified
    /// </summary>
    public void MarkVersionNotified(string version)
    {
        if (!WasVersionNotified(version))
        {
            Update(c => c.NotifiedVersions.Add(version));
        }
    }
    
    /// <summary>
    /// Update cached releases from GitHub
    /// </summary>
    public void UpdateCachedReleases(List<CachedRelease> releases)
    {
        Update(c =>
        {
            c.CachedReleases = releases;
            c.CachedReleasesUpdatedUtc = DateTime.UtcNow.ToString("o");
        });
    }
    
    /// <summary>
    /// Get cached releases (may be stale)
    /// </summary>
    public List<CachedRelease> GetCachedReleases()
    {
        return _config.CachedReleases;
    }
    
    /// <summary>
    /// Check if cached releases need refresh (older than 1 hour)
    /// </summary>
    public bool NeedsCacheRefresh()
    {
        if (string.IsNullOrEmpty(_config.CachedReleasesUpdatedUtc))
            return true;
            
        if (DateTime.TryParse(_config.CachedReleasesUpdatedUtc, out var lastUpdate))
        {
            return DateTime.UtcNow - lastUpdate > TimeSpan.FromHours(1);
        }
        
        return true;
    }
    
    /// <summary>
    /// Register a commit with its version in the map
    /// </summary>
    public void RegisterCommitVersion(string commit, string version)
    {
        if (string.IsNullOrEmpty(commit)) return;
        
        // Use short commit (first 12 chars) as key
        var shortCommit = commit.Length > 12 ? commit[..12] : commit;
        
        Update(c =>
        {
            if (!c.CommitVersionMap.ContainsKey(shortCommit))
            {
                c.CommitVersionMap[shortCommit] = version;
            }
        });
    }
    
    /// <summary>
    /// Get version string for a commit, or "unidentified" if not in map
    /// </summary>
    public string GetVersionForCommit(string commit)
    {
        if (string.IsNullOrEmpty(commit)) return "";
        
        var shortCommit = commit.Length > 12 ? commit[..12] : commit;
        
        if (_config.CommitVersionMap.TryGetValue(shortCommit, out var version))
        {
            return version;
        }
        
        // Also try full commit
        if (_config.CommitVersionMap.TryGetValue(commit, out version))
        {
            return version;
        }
        
        return "unidentified";
    }
    
    /// <summary>
    /// Get cached short commit hash for a full commit, or null if not cached
    /// </summary>
    public string? GetCachedShortCommit(string fullCommit)
    {
        if (string.IsNullOrEmpty(fullCommit)) return null;
        
        var normalized = fullCommit.ToLowerInvariant();
        if (_config.CommitShortMap.TryGetValue(normalized, out var shortCommit))
        {
            return shortCommit;
        }
        
        return null;
    }
    
    /// <summary>
    /// Cache a short commit hash for a full commit
    /// </summary>
    public void CacheShortCommit(string fullCommit, string shortCommit)
    {
        if (string.IsNullOrEmpty(fullCommit) || string.IsNullOrEmpty(shortCommit)) return;
        
        var normalized = fullCommit.ToLowerInvariant();
        
        Update(c =>
        {
            c.CommitShortMap[normalized] = shortCommit;
            
            // Limit cache size (keep last 50 entries)
            if (c.CommitShortMap.Count > 50)
            {
                var toRemove = c.CommitShortMap.Keys.Take(c.CommitShortMap.Count - 50).ToList();
                foreach (var key in toRemove)
                {
                    c.CommitShortMap.Remove(key);
                }
            }
        });
    }
    
    /// <summary>
    /// Add commit to history and return position (for counting updates)
    /// </summary>
    public void AddCommitToHistory(string commit)
    {
        if (string.IsNullOrEmpty(commit)) return;
        
        var shortCommit = commit.Length > 12 ? commit[..12] : commit;
        
        Update(c =>
        {
            // Don't add duplicates
            if (!c.CommitHistory.Contains(shortCommit))
            {
                c.CommitHistory.Add(shortCommit);
            }
        });
    }
    
    /// <summary>
    /// Count commits between two commits in history
    /// Returns 0 if either commit not found
    /// </summary>
    public int CountCommitsBetween(string fromCommit, string toCommit)
    {
        if (string.IsNullOrEmpty(fromCommit) || string.IsNullOrEmpty(toCommit))
            return 0;
            
        var shortFrom = fromCommit.Length > 12 ? fromCommit[..12] : fromCommit;
        var shortTo = toCommit.Length > 12 ? toCommit[..12] : toCommit;
        
        var fromIndex = _config.CommitHistory.IndexOf(shortFrom);
        var toIndex = _config.CommitHistory.IndexOf(shortTo);
        
        if (fromIndex < 0 || toIndex < 0)
            return 0;
            
        return Math.Abs(toIndex - fromIndex);
    }
    
    /// <summary>
    /// Record update attempt
    /// </summary>
    public void RecordUpdateAttempt(string status, string reason = "")
    {
        Update(c =>
        {
            c.LastUpdateAttemptUtc = DateTime.UtcNow.ToString("o");
            c.LastUpdateAttemptStatus = status;
            c.LastUpdateAttemptReason = reason;
        });
    }
    
    /// <summary>
    /// Record successful install
    /// </summary>
    public void RecordSuccessfulInstall()
    {
        Update(c =>
        {
            c.SuccessfulInstallsCount++;
            c.LastSuccessfulInstallUtc = DateTime.UtcNow.ToString("o");
        });
    }
    
    /// <summary>
    /// Record failed install
    /// </summary>
    public void RecordFailedInstall(string reason)
    {
        Update(c =>
        {
            c.FailedInstallsCount++;
            c.LastFailedInstallUtc = DateTime.UtcNow.ToString("o");
            c.LastFailedInstallReason = reason;
        });
    }
    
    /// <summary>
    /// Record revert
    /// </summary>
    public void RecordRevert()
    {
        Update(c =>
        {
            c.RevertsCount++;
            c.LastRevertUtc = DateTime.UtcNow.ToString("o");
        });
    }
}
