using Grigori.Contracts.Dtos.Dashboard;
using Grigori.Dashboard.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using MudBlazor;

namespace Grigori.Dashboard.Components.Pages;

public partial class Search : ComponentBase, IAsyncDisposable
{
    [Inject] private DashboardService DashboardService { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;

    private string _query = string.Empty;
    private string _searchMode = "hybrid";
    private string? _selectedFileType;
    private string? _selectedProject;
    private List<SearchResultViewModel>? _results;
    private List<SearchResultViewModel>? _filteredResults;
    private TimeSpan _searchDuration;
    private bool _isSearching;
    private CancellationTokenSource? _debounceTokenSource;
    private List<IndexedProjectDto> _projects = [];
    private HashSet<string> _availableFileTypes = [];

    // Debounce delay in milliseconds
    private const int DebounceDelay = 300;

    protected override async Task OnInitializedAsync()
    {
        _projects = await DashboardService.GetIndexedProjectsAsync();
    }

    private async Task OnQueryChanged(string value)
    {
        _query = value;

        // Cancel previous debounce
        _debounceTokenSource?.Cancel();
        _debounceTokenSource = new CancellationTokenSource();

        try
        {
            // Wait for debounce delay
            await Task.Delay(DebounceDelay, _debounceTokenSource.Token);

            // Run search if query is not empty
            if (!string.IsNullOrWhiteSpace(_query))
            {
                await RunSearchAsync();
            }
            else
            {
                _results = null;
                _filteredResults = null;
                _availableFileTypes.Clear();
            }
        }
        catch (TaskCanceledException)
        {
            // Debounce cancelled, ignore
        }
    }

    private async Task HandleKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Enter" && !string.IsNullOrWhiteSpace(_query) && !_isSearching)
        {
            _debounceTokenSource?.Cancel();
            await RunSearchAsync();
        }
    }

    private async Task RunSearchAsync()
    {
        if (_isSearching) return;

        _isSearching = true;
        StateHasChanged();

        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            _results = await DashboardService.SearchAsync(_query, 50, _searchMode); // Get more results for filtering
            sw.Stop();
            _searchDuration = sw.Elapsed;

            // Extract available file types from results
            _availableFileTypes = _results
                .Select(r => r.FileExtension)
                .Where(ext => !string.IsNullOrEmpty(ext))
                .ToHashSet();

            ApplyFilters();
        }
        finally
        {
            _isSearching = false;
            StateHasChanged();

            // Trigger syntax highlighting after render
            await Task.Delay(50); // Small delay to ensure DOM is updated
            await JS.InvokeVoidAsync("eval", "if(typeof hljs !== 'undefined') hljs.highlightAll()");
        }
    }

    private async Task OnSearchModeChanged(string value)
    {
        _searchMode = value;
        if (!string.IsNullOrWhiteSpace(_query))
        {
            await RunSearchAsync();
        }
    }

    private void OnFileTypeChanged(string? value)
    {
        _selectedFileType = value;
        ApplyFilters();
    }

    private void OnProjectChanged(string? value)
    {
        _selectedProject = value;
        ApplyFilters();
    }

    private string GetSearchPlaceholder() => _searchMode switch
    {
        "semantic" => "Search code semantically... (e.g., 'function that handles user authentication')",
        "lexical" => "Search for exact keywords... (e.g., 'getUserById')",
        _ => "Search code... (combines semantic understanding with keyword matching)"
    };

    private string GetSearchModeTooltip() => _searchMode switch
    {
        "semantic" => "Semantic search uses AI embeddings to find conceptually similar code, even with different wording.",
        "lexical" => "Lexical search (BM25) finds exact keyword matches, great for function names and identifiers.",
        _ => "Hybrid search combines semantic and lexical results using Reciprocal Rank Fusion for best results."
    };

    private string GetSearchModeIcon() => _searchMode switch
    {
        "semantic" => Icons.Material.Filled.Psychology,
        "lexical" => Icons.Material.Filled.TextFields,
        _ => Icons.Material.Filled.AutoAwesome
    };

    private void ApplyFilters()
    {
        if (_results == null)
        {
            _filteredResults = null;
            return;
        }

        _filteredResults = _results
            .Where(r => string.IsNullOrEmpty(_selectedFileType) || r.FileExtension == _selectedFileType)
            .Where(r => string.IsNullOrEmpty(_selectedProject) || r.ProjectName == _selectedProject)
            .Take(20) // Limit displayed results
            .ToList();
    }

    private void ClearFilters()
    {
        _selectedFileType = null;
        _selectedProject = null;
        ApplyFilters();
    }

    private async Task CopyToClipboard(string content)
    {
        await JS.InvokeVoidAsync("navigator.clipboard.writeText", content);
    }

    private async Task SetQueryAndSearch(string query)
    {
        _query = query;
        await RunSearchAsync();
    }

    public async ValueTask DisposeAsync()
    {
        _debounceTokenSource?.Cancel();
        _debounceTokenSource?.Dispose();
        await ValueTask.CompletedTask;
    }
}
