using System.Text.Json;
using System.Text.Json.Serialization;

namespace AdgVscodeManager.Models;

/// <summary>
/// Application configuration persisted to config.json
/// </summary>
public class AppConfig
{
    /// <summary>
    /// Config file version for compatibility checking
    /// </summary>
    [JsonPropertyName("configVersion")]
    public string ConfigVersion { get; set; } = Constants.CurrentConfigVersion;
    
    [JsonPropertyName("channel")]
    public string Channel { get; set; } = "release";
    
    [JsonPropertyName("installedVersion")]
    public string InstalledVersion { get; set; } = "";
    
    [JsonPropertyName("installedCommit")]
    public string InstalledCommit { get; set; } = "";
    
    [JsonPropertyName("installedCommitShort")]
    public string InstalledCommitShort { get; set; } = "";
    
    [JsonPropertyName("latestKnownVersion")]
    public string LatestKnownVersion { get; set; } = "";
    
    [JsonPropertyName("latestKnownCommit")]
    public string LatestKnownCommit { get; set; } = "";
    
    [JsonPropertyName("latestKnownCommitShort")]
    public string LatestKnownCommitShort { get; set; } = "";
    
    [JsonPropertyName("skippedCommits")]
    public List<string> SkippedCommits { get; set; } = new();
    
    /// <summary>
    /// Legacy - kept for backward compatibility during migration
    /// </summary>
    [JsonPropertyName("skippedVersions")]
    public List<string> SkippedVersions { get; set; } = new();
    
    [JsonPropertyName("notifiedVersions")]
    public List<string> NotifiedVersions { get; set; } = new();
    
    [JsonPropertyName("lastDownloadZipPath")]
    public string LastDownloadZipPath { get; set; } = "";
    
    [JsonPropertyName("lastUnpackedPath")]
    public string LastUnpackedPath { get; set; } = "";
    
    [JsonPropertyName("lastUnpackedCommit")]
    public string LastUnpackedCommit { get; set; } = "";
    
    [JsonPropertyName("lastChangesPath")]
    public string LastChangesPath { get; set; } = "";
    
    [JsonPropertyName("lastUpdateCheckUtc")]
    public string LastUpdateCheckUtc { get; set; } = "";
    
    [JsonPropertyName("updateCount")]
    public int UpdateCount { get; set; } = 0;

    [JsonPropertyName("commitCount")]
    public int CommitCount { get; set; } = 0;

    [JsonPropertyName("autoStartEnabled")]
    public bool AutoStartEnabled { get; set; } = false;
    
    // ============== REOPEN BEHAVIOR SETTINGS ==============
    // These control what happens when the manager is launched (reopened)
    
    /// <summary>
    /// When NO VSCode is running: auto-apply pending update on manager start
    /// </summary>
    [JsonPropertyName("autoApplyUpdateOnStart")]
    public bool AutoApplyUpdateOnStart { get; set; } = true;
    
    /// <summary>
    /// When NO VSCode is running: open new window on manager start
    /// </summary>
    [JsonPropertyName("openWindowWhenNoneRunning")]
    public bool OpenWindowWhenNoneRunning { get; set; } = true;
    
    /// <summary>
    /// When VSCode IS running: open new window on manager start
    /// </summary>
    [JsonPropertyName("openWindowWhenAlreadyRunning")]
    public bool OpenWindowWhenAlreadyRunning { get; set; } = true;
    // ============== END REOPEN BEHAVIOR SETTINGS ==============
    
    /// <summary>
    /// Mapping of commit hash to version string (for display purposes)
    /// Key: commit hash (first 8+ chars), Value: version string like "1.108.0"
    /// </summary>
    [JsonPropertyName("commitVersionMap")]
    public Dictionary<string, string> CommitVersionMap { get; set; } = new();
    
    /// <summary>
    /// Mapping of full commit hash to short commit hash (cached from GitHub API)
    /// Key: full 40-char commit hash, Value: minimal unique short hash (7-10 chars)
    /// </summary>
    [JsonPropertyName("commitShortMap")]
    public Dictionary<string, string> CommitShortMap { get; set; } = new();
    
    /// <summary>
    /// History of installed commits (oldest first) for counting updates
    /// </summary>
    [JsonPropertyName("commitHistory")]
    public List<string> CommitHistory { get; set; } = new();
    
    [JsonPropertyName("cachedReleases")]
    public List<CachedRelease> CachedReleases { get; set; } = new();
    
    [JsonPropertyName("cachedReleasesUpdatedUtc")]
    public string CachedReleasesUpdatedUtc { get; set; } = "";
    
    [JsonPropertyName("errors")]
    public List<ErrorEntry> Errors { get; set; } = new();
    
    // Statistics
    [JsonPropertyName("lastUpdateAttemptUtc")]
    public string LastUpdateAttemptUtc { get; set; } = "";
    
    [JsonPropertyName("lastUpdateAttemptStatus")]
    public string LastUpdateAttemptStatus { get; set; } = ""; // "success", "failed", "canceled"
    
    [JsonPropertyName("lastUpdateAttemptReason")]
    public string LastUpdateAttemptReason { get; set; } = "";
    
    [JsonPropertyName("successfulInstallsCount")]
    public int SuccessfulInstallsCount { get; set; } = 0;
    
    [JsonPropertyName("lastSuccessfulInstallUtc")]
    public string LastSuccessfulInstallUtc { get; set; } = "";
    
    [JsonPropertyName("failedInstallsCount")]
    public int FailedInstallsCount { get; set; } = 0;
    
    [JsonPropertyName("lastFailedInstallUtc")]
    public string LastFailedInstallUtc { get; set; } = "";
    
    [JsonPropertyName("lastFailedInstallReason")]
    public string LastFailedInstallReason { get; set; } = "";
    
    [JsonPropertyName("revertsCount")]
    public int RevertsCount { get; set; } = 0;
    
    [JsonPropertyName("lastRevertUtc")]
    public string LastRevertUtc { get; set; } = "";
    
    [JsonIgnore]
    public VscodeChannel ChannelEnum => Channel.Equals("insiders", StringComparison.OrdinalIgnoreCase) 
        ? VscodeChannel.Insiders 
        : VscodeChannel.Release;
}

/// <summary>
/// Cached release information from GitHub
/// </summary>
public class CachedRelease
{
    [JsonPropertyName("tagName")]
    public string TagName { get; set; } = "";
    
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
    
    [JsonPropertyName("publishedAt")]
    public string PublishedAt { get; set; } = "";
    
    [JsonPropertyName("body")]
    public string Body { get; set; } = "";
    
    [JsonPropertyName("isPrerelease")]
    public bool IsPrerelease { get; set; } = false;
}

/// <summary>
/// Error log entry
/// </summary>
public class ErrorEntry
{
    [JsonPropertyName("timeUtc")]
    public string TimeUtc { get; set; } = "";
    
    [JsonPropertyName("message")]
    public string Message { get; set; } = "";
}

/// <summary>
/// Microsoft Update API response
/// </summary>
public class MsUpdateResponse
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = "";
    
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
    
    [JsonPropertyName("version")]
    public string Version { get; set; } = "";
    
    [JsonPropertyName("productVersion")]
    public string ProductVersion { get; set; } = "";
    
    [JsonPropertyName("hash")]
    public string Hash { get; set; } = "";
    
    [JsonPropertyName("sha256hash")]
    public string Sha256Hash { get; set; } = "";
    
    [JsonPropertyName("supportsFastUpdate")]
    public bool SupportsFastUpdate { get; set; } = false;
}

/// <summary>
/// GitHub release API response (simplified)
/// </summary>
public class GithubRelease
{
    [JsonPropertyName("tag_name")]
    public string TagName { get; set; } = "";
    
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
    
    [JsonPropertyName("published_at")]
    public string PublishedAt { get; set; } = "";
    
    [JsonPropertyName("body")]
    public string Body { get; set; } = "";
    
    [JsonPropertyName("prerelease")]
    public bool Prerelease { get; set; } = false;
}
