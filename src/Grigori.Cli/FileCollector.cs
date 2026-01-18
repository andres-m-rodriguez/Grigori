using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;

namespace Grigori.Cli;

public class FileCollector
{
    private readonly string _rootPath;
    private readonly HashSet<string> _gitignorePatterns = [];

    // Default patterns to always exclude
    private static readonly string[] DefaultExcludes =
    [
        // Version control
        ".git/**",
        ".svn/**",
        ".hg/**",

        // Dependencies
        "node_modules/**",
        "vendor/**",
        "packages/**",

        // Build outputs
        "bin/**",
        "obj/**",
        "dist/**",
        "build/**",
        "out/**",
        "target/**",

        // IDE/Editor
        ".vs/**",
        ".vscode/**",
        ".idea/**",
        "*.suo",
        "*.user",

        // Package managers
        "*.nupkg",
        "*.zip",
        "*.tar.gz",

        // Binary files
        "*.exe",
        "*.dll",
        "*.so",
        "*.dylib",
        "*.pdb",

        // Images
        "*.png",
        "*.jpg",
        "*.jpeg",
        "*.gif",
        "*.ico",
        "*.svg",
        "*.webp",

        // Other
        "*.min.js",
        "*.min.css",
        "*.map",
        ".DS_Store",
        "Thumbs.db"
    ];

    // File extensions to include (code files)
    private static readonly string[] IncludeExtensions =
    [
        // C#/.NET
        ".cs", ".csproj", ".sln", ".slnx", ".razor", ".cshtml",

        // Web
        ".ts", ".tsx", ".js", ".jsx", ".vue", ".svelte",
        ".html", ".css", ".scss", ".sass", ".less",

        // Data/Config
        ".json", ".yaml", ".yml", ".xml", ".toml",

        // Documentation
        ".md", ".txt", ".rst",

        // Other languages
        ".py", ".rb", ".go", ".rs", ".java", ".kt", ".scala",
        ".c", ".cpp", ".h", ".hpp", ".swift", ".m",
        ".php", ".lua", ".sh", ".bash", ".ps1", ".psm1",
        ".sql", ".graphql", ".proto",

        // Config
        ".env.example", ".gitignore", ".dockerignore",
        "Dockerfile", "Makefile", "CMakeLists.txt"
    ];

    public FileCollector(string rootPath)
    {
        _rootPath = Path.GetFullPath(rootPath);
        LoadGitignore();
    }

    private void LoadGitignore()
    {
        var gitignorePath = Path.Combine(_rootPath, ".gitignore");
        if (!File.Exists(gitignorePath)) return;

        foreach (var line in File.ReadLines(gitignorePath))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#'))
                continue;

            // Convert gitignore pattern to glob pattern
            var pattern = trimmed;

            // If pattern doesn't start with /, it can match anywhere
            if (!pattern.StartsWith('/'))
            {
                pattern = "**/" + pattern;
            }
            else
            {
                pattern = pattern.TrimStart('/');
            }

            // If pattern ends with /, it's a directory
            if (pattern.EndsWith('/'))
            {
                pattern += "**";
            }

            _gitignorePatterns.Add(pattern);
        }
    }

    public List<FileContent> CollectFiles()
    {
        var matcher = new Matcher();

        // Add all file patterns
        foreach (var ext in IncludeExtensions)
        {
            if (ext.StartsWith('.'))
                matcher.AddInclude($"**/*{ext}");
            else
                matcher.AddInclude($"**/{ext}");
        }

        // Add default excludes
        foreach (var pattern in DefaultExcludes)
        {
            matcher.AddExclude(pattern);
        }

        // Add gitignore excludes
        foreach (var pattern in _gitignorePatterns)
        {
            matcher.AddExclude(pattern);
        }

        var result = matcher.Execute(new DirectoryInfoWrapper(new DirectoryInfo(_rootPath)));
        var files = new List<FileContent>();

        foreach (var match in result.Files)
        {
            var fullPath = Path.Combine(_rootPath, match.Path);

            try
            {
                // Skip files larger than 1MB
                var fileInfo = new FileInfo(fullPath);
                if (fileInfo.Length > 1024 * 1024)
                    continue;

                var content = File.ReadAllText(fullPath);

                // Skip binary-looking files
                if (content.Contains('\0'))
                    continue;

                files.Add(new FileContent(
                    match.Path.Replace('\\', '/'),
                    content
                ));
            }
            catch
            {
                // Skip files we can't read
            }
        }

        return files;
    }
}
