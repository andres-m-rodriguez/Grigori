using Grigori.Mcp.Dashboard.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace Grigori.Mcp.Dashboard.Components.Pages;

public partial class Search : ComponentBase, IAsyncDisposable
{
    [Inject] private DashboardService DashboardService { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;

    private string _query = string.Empty;
    private string? _selectedFileType;
    private string? _selectedProject;
    private List<SearchResult>? _results;
    private List<SearchResult>? _filteredResults;
    private TimeSpan _searchDuration;
    private bool _isSearching;
    private CancellationTokenSource? _debounceTokenSource;
    private List<IndexedProject> _projects = [];
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
            _results = await DashboardService.SearchAsync(_query, 50); // Get more results for filtering
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

    public async ValueTask DisposeAsync()
    {
        _debounceTokenSource?.Cancel();
        _debounceTokenSource?.Dispose();
        await ValueTask.CompletedTask;
    }
}
