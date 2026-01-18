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
var batchSize = 100;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "-s":
        case "--server":
            if (i + 1 < args.Length)
                server = args[++i];
            break;
        case "-b":
        case "--batch-size":
            if (i + 1 < args.Length && int.TryParse(args[++i], out var size))
                batchSize = size;
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
          -b, --batch-size <n>  Files per batch (default: 100)
          -h, --help            Show help information

        {Yellow}Examples:{Reset}
          grigori index                     Index current directory
          grigori index ./my-project        Index specific directory
          grigori index . -s http://grigori:5151   Use custom server
          grigori index . -b 50             Use smaller batch size
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

// Calculate number of batches
var totalBatches = (int)Math.Ceiling(files.Count / (double)batchSize);
Console.WriteLine($"{Dim}Uploading in {totalBatches} batch(es) of up to {batchSize} files each{Reset}");
Console.WriteLine();

// Send to server in batches
var lastBatch = 0;
var result = await client.IndexFilesInBatchesAsync(projectName, files, batchSize, progress =>
{
    if (progress.Status == BatchStatus.Uploading)
    {
        Console.Write($"\r{Dim}Batch {progress.CurrentBatch}/{progress.TotalBatches}:{Reset} Uploading {progress.FilesInBatch} files...          ");
    }
    else if (progress.Status == BatchStatus.Completed)
    {
        Console.WriteLine($"\r{Green}Batch {progress.CurrentBatch}/{progress.TotalBatches}:{Reset} {progress.FilesIndexed} files, {progress.ChunksCreated} chunks          ");
        lastBatch = progress.CurrentBatch;
    }
    else if (progress.Status == BatchStatus.Failed)
    {
        Console.WriteLine($"\r{Red}Batch {progress.CurrentBatch}/{progress.TotalBatches}:{Reset} Failed - {progress.Error}          ");
    }
});

Console.WriteLine();

if (result.Success)
{
    Console.WriteLine($"{Green}Success!{Reset} Indexed {result.FilesIndexed} files with {result.ChunksCreated} chunks in {result.DurationMs}ms");
    return 0;
}
else
{
    Console.WriteLine($"{Red}Error:{Reset} {result.Error}");
    return 1;
}
