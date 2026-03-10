using Grigori.DataAccess.Extensions;
using Grigori.Database;
using Grigori.Database.Extensions;
using Grigori.Desktop.Mcp;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MudBlazor.Services;

namespace Grigori.Desktop;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            });

        builder.Services.AddMauiBlazorWebView();
        builder.Services.AddMudServices();

        // Configure database
        var dbPath = Path.Combine(FileSystem.AppDataDirectory, "grigori.db");
        builder.Services.AddGrigoriDatabase($"Data Source={dbPath}");
        builder.Services.AddGrigoriDataAccess();

        // MCP services
        builder.Services.AddScoped<GrigoriMcpTools>();
        builder.Services.AddSingleton<McpServerService>();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        var app = builder.Build();

        // Ensure database is created
        using (var scope = app.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<GrigoriDbContext>();
            context.Database.EnsureCreated();
        }

        // Start MCP server
        var mcpServer = app.Services.GetRequiredService<McpServerService>();
        mcpServer.Start();

        return app;
    }
}
