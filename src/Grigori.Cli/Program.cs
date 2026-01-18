using Grigori.Cli;

// ANSI color codes
const string Reset = "\x1b[0m";
const string Cyan = "\x1b[36m";
const string Green = "\x1b[32m";
const string Red = "\x1b[31m";
const string Yellow = "\x1b[33m";
const string Dim = "\x1b[90m";

// Parse arguments
var server = "http://localhost:5151";
var path = ".";
var showHelp = false;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "-s":
        case "--server":
            if (i + 1 < args.Length)
                server = args[++i];
            break;
        case "-h":
        case "--help":
            showHelp = true;
            break;
        case "index":
            // Next non-flag arg is the path
            if (i + 1 < args.Length && !args[i + 1].StartsWith('-'))
                path = args[++i];
            break;
        default:
            if (!args[i].StartsWith('-') && i == 0)
            {
                // First arg without dash could be a command or path
                if (args[i] != "index")
                    path = args[i];
            }
            break;
    }
}

if (showHelp || args.Length == 0)
{
    Console.WriteLine($"""
        {Cyan}Grigori CLI{Reset} - Index projects to a Grigori semantic search server

        {Yellow}Usage:{Reset}
          grigori index [path] [options]

        {Yellow}Arguments:{Reset}
          path                  Path to the project directory (default: current directory)

        {Yellow}Options:{Reset}
          -s, --server <url>    Grigori server URL (default: http://localhost:5151)
          -h, --help            Show help information

        {Yellow}Examples:{Reset}
          grigori index                     Index current directory
          grigori index ./my-project        Index specific directory
          grigori index . -s http://grigori:5151   Use custom server
        """);
    return 0;
}

// Resolve path
var absolutePath = Path.GetFullPath(path);
var projectName = Path.GetFileName(absolutePath);

if (!Directory.Exists(absolutePath))
{
    Console.WriteLine($"{Red}Error:{Reset} Directory not found: {absolutePath}");
    return 1;
}

Console.WriteLine($"{Cyan}Indexing:{Reset} {absolutePath}");
Console.WriteLine($"{Cyan}Project:{Reset} {projectName}");
Console.WriteLine($"{Cyan}Server:{Reset} {server}");
Console.WriteLine();

// Check server health
var client = new GrigoriClient(server);
Console.Write($"{Dim}Checking server...{Reset}");

if (!await client.HealthCheckAsync())
{
    Console.WriteLine($"\r{Red}Error:{Reset} Cannot connect to Grigori server at {server}");
    Console.WriteLine($"{Dim}Make sure the Grigori Docker container is running:{Reset}");
    Console.WriteLine($"  docker run -d -p 5151:5151 grigori");
    return 1;
}
Console.WriteLine($"\r{Green}Server online{Reset}              ");

// Collect files
Console.Write($"{Dim}Collecting files...{Reset}");
var collector = new FileCollector(absolutePath);
var files = collector.CollectFiles();
Console.WriteLine($"\r{Green}Found:{Reset} {files.Count} files          ");

if (files.Count == 0)
{
    Console.WriteLine($"{Yellow}No files to index.{Reset}");
    return 0;
}

// Send to server
Console.Write($"{Dim}Indexing...{Reset}");
var result = await client.IndexFilesAsync(projectName, files);

if (result.Success)
{
    Console.WriteLine($"\r{Green}Success!{Reset} Indexed {result.FilesIndexed} files with {result.ChunksCreated} chunks in {result.DurationMs}ms");
    return 0;
}
else
{
    Console.WriteLine($"\r{Red}Error:{Reset} {result.Error}");
    return 1;
}
