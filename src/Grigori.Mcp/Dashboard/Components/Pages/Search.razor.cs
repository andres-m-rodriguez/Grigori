using BlazingSingularity.Commands;
using Grigori.Mcp.Dashboard.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Grigori.Mcp.Dashboard.Components.Pages;

public partial class Search : ComponentBase
{
    [Inject]
    private DashboardService DashboardService { get; set; } = default!;

    private string _query = string.Empty;
    private List<SearchResult>? _results;
    private TimeSpan _searchDuration;

    private async Task HandleKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Enter" && RunSearchCommand.CanExecute(null))
        {
            await RunSearchCommand.ExecuteAsync(null);
        }
    }

    [RelayCommand]
    private async Task RunSearch()
    {
        _results = null;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        _results = await DashboardService.SearchAsync(_query, 10);
        sw.Stop();
        _searchDuration = sw.Elapsed;
        StateHasChanged();
    }

    private bool CanRunSearch() => !string.IsNullOrWhiteSpace(_query);
}
