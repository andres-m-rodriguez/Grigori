using System.Net.Http.Json;

namespace Grigori.Cli;

public class GrigoriClient
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private const int DefaultBatchSize = 100;

    public GrigoriClient(string baseUrl)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(5) // Timeout per batch
        };
    }

    public async Task<IndexFilesResponse> IndexFilesInBatchesAsync(
        string projectName,
        List<FileContent> files,
        int batchSize = DefaultBatchSize,
        Action<BatchProgress>? onProgress = null)
    {
        if (files.Count == 0)
        {
            return new IndexFilesResponse(true, 0, 0, 0, null);
        }

        var batches = files
            .Select((file, index) => new { file, index })
            .GroupBy(x => x.index / batchSize)
            .Select(g => g.Select(x => x.file).ToList())
            .ToList();

        var totalFiles = 0;
        var totalChunks = 0;
        long totalDuration = 0;

        for (int i = 0; i < batches.Count; i++)
        {
            var batch = batches[i];

            onProgress?.Invoke(new BatchProgress(
                CurrentBatch: i + 1,
                TotalBatches: batches.Count,
                FilesInBatch: batch.Count,
                TotalFiles: files.Count,
                Status: BatchStatus.Uploading
            ));

            var result = await IndexFilesAsync(projectName, batch);

            if (!result.Success)
            {
                onProgress?.Invoke(new BatchProgress(
                    CurrentBatch: i + 1,
                    TotalBatches: batches.Count,
                    FilesInBatch: batch.Count,
                    TotalFiles: files.Count,
                    Status: BatchStatus.Failed,
                    Error: result.Error
                ));

                return new IndexFilesResponse(
                    false,
                    totalFiles,
                    totalChunks,
                    totalDuration,
                    $"Batch {i + 1}/{batches.Count} failed: {result.Error}"
                );
            }

            totalFiles += result.FilesIndexed;
            totalChunks += result.ChunksCreated;
            totalDuration += result.DurationMs;

            onProgress?.Invoke(new BatchProgress(
                CurrentBatch: i + 1,
                TotalBatches: batches.Count,
                FilesInBatch: batch.Count,
                TotalFiles: files.Count,
                Status: BatchStatus.Completed,
                FilesIndexed: result.FilesIndexed,
                ChunksCreated: result.ChunksCreated
            ));
        }

        return new IndexFilesResponse(true, totalFiles, totalChunks, totalDuration, null);
    }

    public async Task<IndexFilesResponse> IndexFilesAsync(
        string projectName,
        List<FileContent> files)
    {
        try
        {
            var request = new IndexFilesRequest(projectName, files);

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

public enum BatchStatus
{
    Uploading,
    Completed,
    Failed
}

public record BatchProgress(
    int CurrentBatch,
    int TotalBatches,
    int FilesInBatch,
    int TotalFiles,
    BatchStatus Status,
    int FilesIndexed = 0,
    int ChunksCreated = 0,
    string? Error = null
);
