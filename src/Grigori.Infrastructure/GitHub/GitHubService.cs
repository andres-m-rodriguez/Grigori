using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Grigori.Contracts.Dtos.GitHub;
using Grigori.Contracts.Interfaces;
using Microsoft.Extensions.Logging;

namespace Grigori.Infrastructure.GitHub;

/// <summary>
/// GitHub service implementation using Personal Access Token authentication.
/// Token is stored encrypted in local app data.
/// </summary>
public class GitHubService : IGitHubService, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<GitHubService> _logger;
    private readonly string _tokenFilePath;
    private readonly JsonSerializerOptions _jsonOptions;

    private string? _token;
    private GitHubUserDto? _cachedUser;

    public bool IsAuthenticated => !string.IsNullOrEmpty(_token);
    public event Action? AuthStateChanged;

    public GitHubService(IHttpClientFactory httpClientFactory, ILogger<GitHubService> logger)
    {
        _httpClient = httpClientFactory.CreateClient("GitHub");
        _httpClient.BaseAddress = new Uri("https://api.github.com/");
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        _httpClient.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Grigori", "1.0"));

        _logger = logger;
        _tokenFilePath = GetTokenFilePath();
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = true
        };

        // Try to load existing token on startup
        LoadTokenFromStorage();
    }

    public async Task<GitHubAuthResult> ConfigureTokenAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return GitHubAuthResult.Failed("Token cannot be empty");

        // Validate the token by fetching user info
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        try
        {
            var response = await _httpClient.GetAsync("user");

            if (!response.IsSuccessStatusCode)
            {
                _httpClient.DefaultRequestHeaders.Authorization = null;
                var error = response.StatusCode switch
                {
                    System.Net.HttpStatusCode.Unauthorized => "Invalid token or token has expired",
                    System.Net.HttpStatusCode.Forbidden => "Token does not have required permissions",
                    _ => $"GitHub API error: {response.StatusCode}"
                };
                return GitHubAuthResult.Failed(error);
            }

            var ghUser = await response.Content.ReadFromJsonAsync<GitHubApiUser>(_jsonOptions);
            if (ghUser == null)
                return GitHubAuthResult.Failed("Failed to parse user response");

            var user = MapToUserDto(ghUser);

            // Store token securely
            _token = token;
            _cachedUser = user;
            SaveTokenToStorage(token);

            _logger.LogInformation("GitHub authentication successful for user {User}", user.Login);
            AuthStateChanged?.Invoke();

            return GitHubAuthResult.Succeeded(user);
        }
        catch (HttpRequestException ex)
        {
            _httpClient.DefaultRequestHeaders.Authorization = null;
            _logger.LogError(ex, "Failed to validate GitHub token");
            return GitHubAuthResult.Failed($"Network error: {ex.Message}");
        }
    }

    public void ClearToken()
    {
        _token = null;
        _cachedUser = null;
        _httpClient.DefaultRequestHeaders.Authorization = null;

        try
        {
            if (File.Exists(_tokenFilePath))
                File.Delete(_tokenFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete token file");
        }

        _logger.LogInformation("GitHub token cleared");
        AuthStateChanged?.Invoke();
    }

    public async Task<GitHubUserDto?> GetCurrentUserAsync()
    {
        if (!IsAuthenticated)
            return null;

        if (_cachedUser != null)
            return _cachedUser;

        try
        {
            var ghUser = await _httpClient.GetFromJsonAsync<GitHubApiUser>("user", _jsonOptions);
            if (ghUser != null)
            {
                _cachedUser = MapToUserDto(ghUser);
                return _cachedUser;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get current user");
        }

        return null;
    }

    public async Task<List<GitHubRepositoryDto>> GetRepositoriesAsync(int limit = 30)
    {
        if (!IsAuthenticated)
            return [];

        try
        {
            var repos = await _httpClient.GetFromJsonAsync<List<GitHubApiRepository>>(
                $"user/repos?sort=pushed&per_page={limit}", _jsonOptions);

            return repos?.Select(MapToRepoDto).ToList() ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get repositories");
            return [];
        }
    }

    public async Task<List<GitHubCommitDto>> GetRecentCommitsAsync(string owner, string repo, int limit = 30)
    {
        if (!IsAuthenticated)
            return [];

        try
        {
            var commits = await _httpClient.GetFromJsonAsync<List<GitHubApiCommit>>(
                $"repos/{owner}/{repo}/commits?per_page={limit}", _jsonOptions);

            return commits?.Select(MapToCommitDto).ToList() ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get commits for {Owner}/{Repo}", owner, repo);
            return [];
        }
    }

    public async Task<List<GitHubPullRequestDto>> GetPullRequestsAsync(string owner, string repo, int limit = 30)
    {
        if (!IsAuthenticated)
            return [];

        try
        {
            var prs = await _httpClient.GetFromJsonAsync<List<GitHubApiPullRequest>>(
                $"repos/{owner}/{repo}/pulls?state=all&sort=updated&per_page={limit}", _jsonOptions);

            return prs?.Select(MapToPrDto).ToList() ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get pull requests for {Owner}/{Repo}", owner, repo);
            return [];
        }
    }

    public async Task<List<GitHubBranchDto>> GetBranchesAsync(string owner, string repo)
    {
        if (!IsAuthenticated)
            return [];

        try
        {
            // Get repo info for default branch
            var repoInfo = await _httpClient.GetFromJsonAsync<GitHubApiRepository>(
                $"repos/{owner}/{repo}", _jsonOptions);

            var branches = await _httpClient.GetFromJsonAsync<List<GitHubApiBranch>>(
                $"repos/{owner}/{repo}/branches", _jsonOptions);

            return branches?.Select(b => new GitHubBranchDto(
                b.Name,
                b.Commit?.Sha ?? "",
                b.Protected,
                b.Name == repoInfo?.DefaultBranch
            )).ToList() ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get branches for {Owner}/{Repo}", owner, repo);
            return [];
        }
    }

    public async Task<bool> DownloadRepositoryZipAsync(string owner, string repo, string? branch, string outputPath, CancellationToken cancellationToken = default)
    {
        if (!IsAuthenticated)
            return false;

        try
        {
            var branchRef = branch ?? "HEAD";
            var response = await _httpClient.GetAsync($"repos/{owner}/{repo}/zipball/{branchRef}", cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Failed to download repository {Owner}/{Repo}: {Status}", owner, repo, response.StatusCode);
                return false;
            }

            await using var fs = File.Create(outputPath);
            await response.Content.CopyToAsync(fs, cancellationToken);

            _logger.LogInformation("Downloaded repository {Owner}/{Repo} to {Path}", owner, repo, outputPath);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download repository {Owner}/{Repo}", owner, repo);
            return false;
        }
    }

    private static string GetTokenFilePath()
    {
        // Use LocalApplicationData on Windows, or home directory on Linux/macOS
        var basePath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        // Fallback to home directory if LocalApplicationData is empty (common in Docker)
        if (string.IsNullOrEmpty(basePath))
        {
            basePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        // Final fallback to /tmp if still empty
        if (string.IsNullOrEmpty(basePath))
        {
            basePath = Path.GetTempPath();
        }

        var grigoriPath = Path.Combine(basePath, ".grigori");
        Directory.CreateDirectory(grigoriPath);
        return Path.Combine(grigoriPath, ".github_token");
    }

    private void LoadTokenFromStorage()
    {
        try
        {
            if (!File.Exists(_tokenFilePath))
                return;

            var encryptedData = File.ReadAllBytes(_tokenFilePath);
            var token = DecryptToken(encryptedData);

            if (!string.IsNullOrEmpty(token))
            {
                _token = token;
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
                _logger.LogInformation("Loaded GitHub token from storage");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load GitHub token from storage");
        }
    }

    private void SaveTokenToStorage(string token)
    {
        try
        {
            var encryptedData = EncryptToken(token);
            File.WriteAllBytes(_tokenFilePath, encryptedData);
            _logger.LogDebug("Saved GitHub token to storage");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save GitHub token to storage");
        }
    }

    private static byte[] EncryptToken(string token)
    {
        var tokenBytes = Encoding.UTF8.GetBytes(token);

        // Use DPAPI on Windows for secure storage
        if (OperatingSystem.IsWindows())
        {
            return ProtectedData.Protect(tokenBytes, null, DataProtectionScope.CurrentUser);
        }

        // For non-Windows, use a simple obfuscation (not truly secure, but better than plaintext)
        // In production, consider using platform-specific keychains
        var entropy = Encoding.UTF8.GetBytes(Environment.MachineName + Environment.UserName);
        using var aes = Aes.Create();
        aes.Key = SHA256.HashData(entropy);
        aes.GenerateIV();

        using var ms = new MemoryStream();
        ms.Write(aes.IV, 0, aes.IV.Length);
        using (var cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write))
        {
            cs.Write(tokenBytes, 0, tokenBytes.Length);
        }
        return ms.ToArray();
    }

    private static string? DecryptToken(byte[] encryptedData)
    {
        if (OperatingSystem.IsWindows())
        {
            var tokenBytes = ProtectedData.Unprotect(encryptedData, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(tokenBytes);
        }

        // For non-Windows
        var entropy = Encoding.UTF8.GetBytes(Environment.MachineName + Environment.UserName);
        using var aes = Aes.Create();
        aes.Key = SHA256.HashData(entropy);

        var iv = new byte[16];
        Array.Copy(encryptedData, 0, iv, 0, 16);
        aes.IV = iv;

        using var ms = new MemoryStream(encryptedData, 16, encryptedData.Length - 16);
        using var cs = new CryptoStream(ms, aes.CreateDecryptor(), CryptoStreamMode.Read);
        using var sr = new StreamReader(cs);
        return sr.ReadToEnd();
    }

    #region API Response Mapping

    private static GitHubUserDto MapToUserDto(GitHubApiUser u) => new(
        u.Login,
        u.Name,
        u.AvatarUrl,
        u.Email,
        u.PublicRepos,
        u.TotalPrivateRepos,
        u.CreatedAt
    );

    private static GitHubRepositoryDto MapToRepoDto(GitHubApiRepository r) => new(
        r.Id,
        r.Name,
        r.FullName,
        r.Description,
        r.DefaultBranch,
        r.Private,
        r.Language,
        r.StargazersCount,
        r.ForksCount,
        r.OpenIssuesCount,
        r.CreatedAt,
        r.UpdatedAt,
        r.PushedAt,
        r.HtmlUrl,
        r.CloneUrl
    );

    private static GitHubCommitDto MapToCommitDto(GitHubApiCommit c) => new(
        c.Sha,
        c.Sha[..7],
        c.Commit?.Message ?? "",
        c.Commit?.Author?.Name,
        c.Commit?.Author?.Email,
        c.Author?.AvatarUrl,
        c.Commit?.Author?.Date ?? DateTime.UtcNow,
        c.HtmlUrl,
        c.Stats?.Additions ?? 0,
        c.Stats?.Deletions ?? 0,
        c.Files?.Count ?? 0
    );

    private static GitHubPullRequestDto MapToPrDto(GitHubApiPullRequest pr) => new(
        pr.Number,
        pr.Title,
        pr.State,
        pr.Body,
        pr.User?.Login ?? "",
        pr.User?.AvatarUrl,
        pr.Head?.Ref ?? "",
        pr.Base?.Ref ?? "",
        pr.CreatedAt,
        pr.UpdatedAt,
        pr.MergedAt,
        pr.ClosedAt,
        pr.HtmlUrl,
        pr.Additions,
        pr.Deletions,
        pr.ChangedFiles,
        pr.Commits,
        pr.Draft
    );

    #endregion

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}

#region GitHub API Response Types

internal class GitHubApiUser
{
    public string Login { get; set; } = "";
    public string? Name { get; set; }
    public string? AvatarUrl { get; set; }
    public string? Email { get; set; }
    public int PublicRepos { get; set; }
    public int TotalPrivateRepos { get; set; }
    public DateTime CreatedAt { get; set; }
}

internal class GitHubApiRepository
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string FullName { get; set; } = "";
    public string? Description { get; set; }
    public string DefaultBranch { get; set; } = "main";
    public bool Private { get; set; }
    public string? Language { get; set; }
    public int StargazersCount { get; set; }
    public int ForksCount { get; set; }
    public int OpenIssuesCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? PushedAt { get; set; }
    public string HtmlUrl { get; set; } = "";
    public string CloneUrl { get; set; } = "";
}

internal class GitHubApiCommit
{
    public string Sha { get; set; } = "";
    public GitHubApiCommitDetail? Commit { get; set; }
    public GitHubApiUser? Author { get; set; }
    public string HtmlUrl { get; set; } = "";
    public GitHubApiCommitStats? Stats { get; set; }
    public List<object>? Files { get; set; }
}

internal class GitHubApiCommitDetail
{
    public string Message { get; set; } = "";
    public GitHubApiCommitAuthor? Author { get; set; }
}

internal class GitHubApiCommitAuthor
{
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public DateTime Date { get; set; }
}

internal class GitHubApiCommitStats
{
    public int Additions { get; set; }
    public int Deletions { get; set; }
}

internal class GitHubApiPullRequest
{
    public int Number { get; set; }
    public string Title { get; set; } = "";
    public string State { get; set; } = "";
    public string? Body { get; set; }
    public GitHubApiUser? User { get; set; }
    public GitHubApiBranchRef? Head { get; set; }
    public GitHubApiBranchRef? Base { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? MergedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public string HtmlUrl { get; set; } = "";
    public int Additions { get; set; }
    public int Deletions { get; set; }
    public int ChangedFiles { get; set; }
    public int Commits { get; set; }
    public bool Draft { get; set; }
}

internal class GitHubApiBranchRef
{
    public string Ref { get; set; } = "";
    public string Sha { get; set; } = "";
}

internal class GitHubApiBranch
{
    public string Name { get; set; } = "";
    public GitHubApiBranchCommit? Commit { get; set; }
    public bool Protected { get; set; }
}

internal class GitHubApiBranchCommit
{
    public string Sha { get; set; } = "";
}

#endregion
