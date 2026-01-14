using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text.Json;
using AdgVscodeManager.Models;

namespace AdgVscodeManager.Services;

/// <summary>
/// Service for checking and downloading VSCode updates
/// </summary>
public class UpdateService
{
    private readonly ConfigService _configService;
    private readonly LoggingService _logger;
    private readonly HttpClient _httpClient;
    private readonly VscodeChannel _channel;
    
    public UpdateService(ConfigService configService, LoggingService logger, VscodeChannel channel)
    {
        _configService = configService;
        _logger = logger;
        _channel = channel;
        
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("AdgVscodeManager", "1.0"));
        _httpClient.Timeout = TimeSpan.FromMinutes(Constants.DownloadTimeoutMinutes);
    }
    
    /// <summary>
    /// Get the architecture string for the current platform
    /// </summary>
    private static string GetArchitecture()
    {
        return RuntimeInformation.OSArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.X86 => "ia32",
            Architecture.Arm64 => "arm64",
            _ => "x64"
        };
    }
    
    /// <summary>
    /// Get the channel string for API
    /// </summary>
    private string GetChannelString()
    {
        return _channel == VscodeChannel.Insiders ? "insider" : "stable";
    }
    
    /// <summary>
    /// Check for latest version from Microsoft Update API
    /// </summary>
    public async Task<MsUpdateResponse?> CheckLatestVersionAsync(CancellationToken ct = default)
    {
        try
        {
            var arch = GetArchitecture();
            var channel = GetChannelString();
            var archiveType = string.Format(Constants.MsUpdateArchiveType, arch);
            
            // Use 'latest' or '0.0.0' to get the newest version
            var url = $"{Constants.MsUpdateApiBase}/{archiveType}/{channel}/latest";
            
            _logger.Info($"Checking for updates: {url}");
            
            var response = await _httpClient.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();
            
            var json = await response.Content.ReadAsStringAsync(ct);
            var result = JsonSerializer.Deserialize<MsUpdateResponse>(json);
            
            if (result != null)
            {
                _logger.LogVersion("Latest version available", result.ProductVersion);
            }
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to check for updates", ex);
            return null;
        }
    }
    
    /// <summary>
    /// Download zip file with progress reporting
    /// </summary>
    public async Task<bool> DownloadZipAsync(
        string url, 
        string destinationPath,
        IProgress<int>? progress = null,
        CancellationToken ct = default)
    {
        try
        {
            _logger.LogDownload(url, destinationPath);
            
            // Ensure directory exists
            var dir = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            
            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            
            var totalBytes = response.Content.Headers.ContentLength ?? -1;
            var downloadedBytes = 0L;
            
            using var contentStream = await response.Content.ReadAsStreamAsync(ct);
            using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
            
            var buffer = new byte[81920];
            int bytesRead;
            var lastProgress = 0;
            
            while ((bytesRead = await contentStream.ReadAsync(buffer, ct)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                downloadedBytes += bytesRead;
                
                if (totalBytes > 0)
                {
                    var currentProgress = (int)(downloadedBytes * 100 / totalBytes);
                    if (currentProgress != lastProgress)
                    {
                        lastProgress = currentProgress;
                        progress?.Report(currentProgress);
                    }
                }
            }
            
            _logger.Info($"Download completed: {downloadedBytes} bytes");
            return true;
        }
        catch (OperationCanceledException)
        {
            _logger.Info("Download cancelled");
            // Clean up partial file
            try { File.Delete(destinationPath); } catch { }
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error("Download failed", ex);
            // Clean up partial file
            try { File.Delete(destinationPath); } catch { }
            return false;
        }
    }
    
    /// <summary>
    /// Fetch releases from GitHub API
    /// </summary>
    public async Task<List<GithubRelease>?> FetchGithubReleasesAsync(CancellationToken ct = default)
    {
        try
        {
            // Check rate limit first
            var rateLimitUrl = "https://api.github.com/rate_limit";
            var rateLimitResponse = await _httpClient.GetAsync(rateLimitUrl, ct);
            
            if (rateLimitResponse.IsSuccessStatusCode)
            {
                var rateLimitJson = await rateLimitResponse.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(rateLimitJson);
                var remaining = doc.RootElement.GetProperty("resources")
                    .GetProperty("core")
                    .GetProperty("remaining")
                    .GetInt32();
                    
                if (remaining < 10)
                {
                    _logger.Warn($"GitHub API rate limit low: {remaining} remaining");
                }
            }
            
            _logger.Info("Fetching GitHub releases...");
            
            var url = $"{Constants.GithubReleasesApi}?per_page=100";
            var response = await _httpClient.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();
            
            var json = await response.Content.ReadAsStringAsync(ct);
            var releases = JsonSerializer.Deserialize<List<GithubRelease>>(json);
            
            _logger.Info($"Fetched {releases?.Count ?? 0} releases from GitHub");
            
            return releases;
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to fetch GitHub releases", ex);
            return null;
        }
    }
    
    /// <summary>
    /// Get the minimal unique short hash for a commit from GitHub
    /// First checks cache in config, only calls GitHub API if not cached
    /// </summary>
    public async Task<string> GetShortCommitHashAsync(string fullCommit, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(fullCommit) || fullCommit.Length < 7)
            return fullCommit ?? "";
        
        // Normalize to lowercase for consistent cache keys
        var normalizedCommit = fullCommit.ToLowerInvariant();
        
        // Check cache first
        var cached = _configService.GetCachedShortCommit(normalizedCommit);
        if (!string.IsNullOrEmpty(cached))
        {
            _logger.Debug($"Short hash from cache: {cached}");
            return cached;
        }
        
        // Not cached - query GitHub API
        // Try lengths from 7 to 10 (7 is git default, 10 is our max for filenames)
        for (int len = 7; len <= 10; len++)
        {
            var shortHash = normalizedCommit[..len];
            try
            {
                var url = $"https://api.github.com/repos/microsoft/vscode/commits/{shortHash}";
                var response = await _httpClient.GetAsync(url, ct);
                
                if (response.IsSuccessStatusCode)
                {
                    // Verify it resolves to the same full commit
                    var json = await response.Content.ReadAsStringAsync(ct);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("sha", out var sha))
                    {
                        var resolvedSha = sha.GetString();
                        if (resolvedSha != null && resolvedSha.Equals(normalizedCommit, StringComparison.OrdinalIgnoreCase))
                        {
                            _logger.Debug($"Short hash from API: {shortHash} (len={len})");
                            // Cache the result
                            _configService.CacheShortCommit(normalizedCommit, shortHash);
                            return shortHash;
                        }
                    }
                }
                else if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    // Ambiguous - need more characters
                    continue;
                }
            }
            catch
            {
                // On error, continue to next length
            }
        }
        
        // Fallback to 7 characters if nothing worked (git default)
        var fallback = normalizedCommit.Length >= 7 ? normalizedCommit[..7] : normalizedCommit;
        _logger.Debug($"Short hash fallback: {fallback}");
        return fallback;
    }
    
    /// <summary>
    /// Get short commit hash synchronously (uses cache or fallback)
    /// For UI display, prefer the async version when possible
    /// </summary>
    public static string GetShortCommitFallback(string? fullCommit, int minLength = 7, int maxLength = 10)
    {
        if (string.IsNullOrEmpty(fullCommit))
            return "";
        
        // Use 7 as default (git standard), max 10 for safety
        var len = Math.Min(Math.Max(minLength, 7), Math.Min(fullCommit.Length, maxLength));
        return fullCommit[..len];
    }
    
    /// <summary>
    /// Get changelog between two versions
    /// </summary>
    public async Task<string> GetChangelogAsync(string fromVersion, string toVersion, CancellationToken ct = default)
    {
        try
        {
            // Try to get fresh releases, fallback to cache
            var releases = await FetchGithubReleasesAsync(ct);
            List<CachedRelease> cachedReleases;
            
            if (releases != null && releases.Count > 0)
            {
                // Update cache
                cachedReleases = releases.Select(r => new CachedRelease
                {
                    TagName = r.TagName,
                    Name = r.Name,
                    PublishedAt = r.PublishedAt,
                    Body = r.Body,
                    IsPrerelease = r.Prerelease
                }).ToList();
                
                _configService.UpdateCachedReleases(cachedReleases);
            }
            else
            {
                // Use cache
                cachedReleases = _configService.GetCachedReleases();
                _logger.Warn("Using cached releases for changelog");
            }
            
            if (cachedReleases.Count == 0)
            {
                return "Error downloading history. No cached releases available.";
            }
            
            // Filter releases between versions (for insiders, include prereleases)
            var isInsiders = _channel == VscodeChannel.Insiders;
            var relevantReleases = cachedReleases
                .Where(r => isInsiders || !r.IsPrerelease)
                .Where(r => CompareVersions(r.TagName, fromVersion) > 0 && 
                           CompareVersions(r.TagName, toVersion) <= 0)
                .OrderByDescending(r => r.PublishedAt)
                .ToList();
            
            if (relevantReleases.Count == 0)
            {
                return $"No detailed changelog available between {fromVersion} and {toVersion}.";
            }
            
            var changelog = new System.Text.StringBuilder();
            changelog.AppendLine($"Changelog: {fromVersion} → {toVersion}");
            changelog.AppendLine(new string('=', 50));
            changelog.AppendLine();
            
            foreach (var release in relevantReleases)
            {
                changelog.AppendLine($"## {release.Name} ({release.TagName})");
                changelog.AppendLine($"Released: {release.PublishedAt}");
                changelog.AppendLine();
                changelog.AppendLine(release.Body);
                changelog.AppendLine();
                changelog.AppendLine(new string('-', 50));
                changelog.AppendLine();
            }
            
            return changelog.ToString();
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to get changelog", ex);
            return $"Error downloading history: {ex.Message}";
        }
    }
    
    /// <summary>
    /// Get changelog between two commits using GitHub Compare API
    /// This is the primary method for insiders builds
    /// </summary>
    public async Task<string> GetChangelogForCommitsAsync(string fromCommit, string toCommit, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(fromCommit) || string.IsNullOrEmpty(toCommit))
        {
            return "Cannot generate changelog: missing commit information.";
        }
        
        if (string.Equals(fromCommit, toCommit, StringComparison.OrdinalIgnoreCase))
        {
            return "No changes - same commit.";
        }
        
        try
        {
            _logger.Info($"Fetching commit comparison: {fromCommit[..Math.Min(8, fromCommit.Length)]}...{toCommit[..Math.Min(8, toCommit.Length)]}");
            
            // GitHub Compare API
            var url = $"https://api.github.com/repos/microsoft/vscode/compare/{fromCommit}...{toCommit}";
            var response = await _httpClient.GetAsync(url, ct);
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.Warn($"GitHub Compare API returned {response.StatusCode}");
                return $"Could not fetch commit comparison (HTTP {(int)response.StatusCode}).";
            }
            
            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;
            
            var changelog = new System.Text.StringBuilder();
            
            // Header - use 7 chars (git default short hash)
            var aheadBy = root.TryGetProperty("ahead_by", out var ahead) ? ahead.GetInt32() : 0;
            var fromShort = fromCommit.Length >= 7 ? fromCommit[..7] : fromCommit;
            var toShort = toCommit.Length >= 7 ? toCommit[..7] : toCommit;
            
            changelog.AppendLine($"Changelog: {fromShort} → {toShort}");
            changelog.AppendLine(new string('=', 60));
            changelog.AppendLine($"Total commits: {aheadBy}");
            changelog.AppendLine();
            
            // Get commits and show ALL of them (newest first for reading)
            if (root.TryGetProperty("commits", out var commits))
            {
                var commitList = commits.EnumerateArray().ToList();
                
                // Reverse to show newest first
                commitList.Reverse();
                
                changelog.AppendLine($"## All {commitList.Count} commits (newest first):");
                changelog.AppendLine();
                
                foreach (var commit in commitList)
                {
                    var sha = commit.GetProperty("sha").GetString() ?? "";
                    var shortSha = sha.Length >= 7 ? sha[..7] : sha;
                    
                    var message = "";
                    if (commit.TryGetProperty("commit", out var commitData) && 
                        commitData.TryGetProperty("message", out var msg))
                    {
                        message = msg.GetString() ?? "";
                        // Take only first line
                        var firstLine = message.Split('\n')[0];
                        if (firstLine.Length > 120)
                            firstLine = firstLine[..117] + "...";
                        message = firstLine;
                    }
                    
                    changelog.AppendLine($"  • [{shortSha}] {message}");
                }
            }
            
            changelog.AppendLine();
            changelog.AppendLine(new string('-', 60));
            
            // Files changed summary
            if (root.TryGetProperty("files", out var files))
            {
                var fileCount = files.GetArrayLength();
                changelog.AppendLine($"Files changed: {fileCount}");
            }
            
            return changelog.ToString();
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to get commit changelog", ex);
            return $"Error fetching commit history: {ex.Message}";
        }
    }
    
    /// <summary>
    /// Count commits between two commits
    /// </summary>
    public async Task<int> CountCommitsBetweenAsync(string fromCommit, string toCommit, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(fromCommit) || string.IsNullOrEmpty(toCommit))
            return 1; // Assume at least 1 update
            
        if (string.Equals(fromCommit, toCommit, StringComparison.OrdinalIgnoreCase))
            return 0;
        
        try
        {
            var url = $"https://api.github.com/repos/microsoft/vscode/compare/{fromCommit}...{toCommit}";
            var response = await _httpClient.GetAsync(url, ct);
            
            if (!response.IsSuccessStatusCode)
                return 1;
                
            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            
            if (doc.RootElement.TryGetProperty("ahead_by", out var ahead))
                return ahead.GetInt32();
        }
        catch
        {
            // Ignore errors
        }
        
        return 1;
    }
    
    /// <summary>
    /// Count releases (tags) between two commits by fetching tags from GitHub
    /// </summary>
    public async Task<int> CountReleasesBetweenCommitsAsync(string fromCommit, string toCommit, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(fromCommit) || string.IsNullOrEmpty(toCommit))
            return 1;
            
        if (string.Equals(fromCommit, toCommit, StringComparison.OrdinalIgnoreCase))
            return 0;
        
        try
        {
            // Fetch recent tags from GitHub
            var url = "https://api.github.com/repos/microsoft/vscode/tags?per_page=100";
            var response = await _httpClient.GetAsync(url, ct);
            
            if (!response.IsSuccessStatusCode)
                return 1;
                
            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            
            var tags = doc.RootElement.EnumerateArray().ToList();
            
            // Find positions of our commits in tag history
            int? fromIndex = null;
            int? toIndex = null;
            
            for (int i = 0; i < tags.Count; i++)
            {
                var tag = tags[i];
                if (!tag.TryGetProperty("commit", out var commitObj))
                    continue;
                if (!commitObj.TryGetProperty("sha", out var sha))
                    continue;
                    
                var commitSha = sha.GetString() ?? "";
                
                if (commitSha.StartsWith(fromCommit, StringComparison.OrdinalIgnoreCase) ||
                    fromCommit.StartsWith(commitSha, StringComparison.OrdinalIgnoreCase))
                {
                    fromIndex = i;
                }
                if (commitSha.StartsWith(toCommit, StringComparison.OrdinalIgnoreCase) ||
                    toCommit.StartsWith(commitSha, StringComparison.OrdinalIgnoreCase))
                {
                    toIndex = i;
                }
            }
            
            // If we found both, count tags between them
            if (fromIndex.HasValue && toIndex.HasValue)
            {
                var count = Math.Abs(fromIndex.Value - toIndex.Value);
                _logger.Info($"Found {count} releases between commits");
                return count > 0 ? count : 1;
            }
            
            // Fallback: count by comparing via API
            // Tags are ordered newest first, so if toCommit is newer, 
            // count how many tags are between index 0 and fromIndex
            if (fromIndex.HasValue)
            {
                _logger.Info($"Found fromCommit at tag index {fromIndex.Value}, counting releases to latest");
                return fromIndex.Value > 0 ? fromIndex.Value : 1;
            }
            
            // If we can't find in tags, use commit distance and estimate (1 release per ~50 commits)
            var commitCount = await CountCommitsBetweenAsync(fromCommit, toCommit, ct);
            var estimatedReleases = Math.Max(1, commitCount / 50);
            _logger.Info($"Estimated {estimatedReleases} releases from {commitCount} commits");
            return estimatedReleases;
        }
        catch (Exception ex)
        {
            _logger.Warn($"Failed to count releases between commits: {ex.Message}");
            return 1;
        }
    }
    
    /// <summary>
    /// Count versions between two version strings
    /// </summary>
    public int CountVersionsBetween(string fromVersion, string toVersion)
    {
        var cachedReleases = _configService.GetCachedReleases();
        var isInsiders = _channel == VscodeChannel.Insiders;
        
        // Strip "-insider" suffix for comparison (insiders versions are like "1.109.0-insider")
        var fromClean = fromVersion.Replace("-insider", "").Trim();
        var toClean = toVersion.Replace("-insider", "").Trim();
        
        return cachedReleases
            .Where(r => isInsiders || !r.IsPrerelease)
            .Count(r => CompareVersions(r.TagName, fromClean) > 0 && 
                       CompareVersions(r.TagName, toClean) <= 0);
    }
    
    /// <summary>
    /// Compare version strings (e.g., "1.85.0" vs "1.86.0")
    /// </summary>
    private static int CompareVersions(string v1, string v2)
    {
        // Strip 'v' prefix if present
        v1 = v1.TrimStart('v');
        v2 = v2.TrimStart('v');
        
        var parts1 = v1.Split('.').Select(p => int.TryParse(p, out var n) ? n : 0).ToArray();
        var parts2 = v2.Split('.').Select(p => int.TryParse(p, out var n) ? n : 0).ToArray();
        
        for (int i = 0; i < Math.Max(parts1.Length, parts2.Length); i++)
        {
            var p1 = i < parts1.Length ? parts1[i] : 0;
            var p2 = i < parts2.Length ? parts2[i] : 0;
            
            if (p1 != p2)
                return p1.CompareTo(p2);
        }
        
        return 0;
    }
    
    /// <summary>
    /// Check if v1 is newer than v2
    /// </summary>
    public static bool IsNewerVersion(string v1, string v2)
    {
        return CompareVersions(v1, v2) > 0;
    }
}
