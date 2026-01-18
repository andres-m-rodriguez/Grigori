using System.Net.Http.Json;
using System.Text.Json;

namespace Grigori.Cli;

public class GrigoriClient
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;

    public GrigoriClient(string baseUrl)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(10) // Long timeout for large projects
        };
    }

    public async Task<IndexFilesResponse> IndexFilesAsync(
        string projectName,
        List<FileContent> files,
        Action<int>? onProgress = null)
    {
        try
        {
            var request = new IndexFilesRequest(files);

            var response = await _httpClient.PostAsJsonAsync(
                $"{_baseUrl}/api/index/files",
                request,
                JsonContext.Default.IndexFilesRequest
            );

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                return new IndexFilesResponse(false, 0, 0, 0, $"HTTP {(int)response.StatusCode}: {error}");
            }

            var result = await response.Content.ReadFromJsonAsync(JsonContext.Default.IndexFilesResponse);

            onProgress?.Invoke(100);

            return result ?? new IndexFilesResponse(false, 0, 0, 0, "Empty response from server");
        }
        catch (HttpRequestException ex)
        {
            return new IndexFilesResponse(false, 0, 0, 0, $"Connection failed: {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            return new IndexFilesResponse(false, 0, 0, 0, "Request timed out");
        }
        catch (Exception ex)
        {
            return new IndexFilesResponse(false, 0, 0, 0, ex.Message);
        }
    }

    public async Task<bool> HealthCheckAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}/api/health");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
