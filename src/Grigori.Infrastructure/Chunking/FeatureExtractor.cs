using System.Text.RegularExpressions;

namespace Grigori.Infrastructure.Chunking;

public static class FeatureExtractor
{
    private static readonly Dictionary<string, string[]> FeaturePatterns = new(StringComparer.OrdinalIgnoreCase)
    {
        ["authentication"] = ["Login", "Authenticate", "Password", "Token", "JWT", "SignIn", "SignOut", "Credential", "Identity"],
        ["authorization"] = ["Authorize", "Role", "Permission", "Claims", "Policy", "Access", "Grant", "Deny"],
        ["database"] = ["Repository", "DbContext", "Query", "Insert", "Update", "Delete", "Table", "Column", "Entity", "Migration", "Sql"],
        ["api"] = ["Controller", "Endpoint", "MapGet", "MapPost", "MapPut", "MapDelete", "HttpGet", "HttpPost", "HttpPut", "HttpDelete", "Route", "ApiController"],
        ["validation"] = ["Validate", "IsValid", "ModelState", "Validator", "ValidationResult", "Required", "Range", "StringLength"],
        ["logging"] = ["Logger", "Log", "ILogger", "LogInformation", "LogWarning", "LogError", "LogDebug", "Trace", "Serilog"],
        ["caching"] = ["Cache", "IMemoryCache", "IDistributedCache", "Redis", "Cached", "Invalidate", "Expiration"],
        ["messaging"] = ["Message", "Queue", "Event", "Publish", "Subscribe", "Handler", "Bus", "Notification", "RabbitMQ", "Kafka"],
        ["testing"] = ["Test", "Fact", "Theory", "Mock", "Assert", "Arrange", "Act", "xUnit", "NUnit", "MSTest"],
        ["configuration"] = ["Config", "Configuration", "Settings", "Options", "IOptions", "Environment", "AppSettings"],
        ["dependency_injection"] = ["Services", "AddScoped", "AddSingleton", "AddTransient", "IServiceCollection", "Inject", "Dependency"],
        ["error_handling"] = ["Exception", "Error", "Throw", "Catch", "Try", "Finally", "Fallback", "Retry"]
    };

    public static List<string> ExtractFeatures(string content)
    {
        var features = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (feature, patterns) in FeaturePatterns)
        {
            foreach (var pattern in patterns)
            {
                if (ContainsWord(content, pattern))
                {
                    features.Add(feature);
                    break;
                }
            }
        }

        return features.ToList();
    }

    public static List<string> ExtractFeaturesFromQuery(string query)
    {
        var features = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var feature in FeaturePatterns.Keys)
        {
            if (ContainsWord(query, feature) || ContainsWord(query, feature.Replace("_", " ")))
            {
                features.Add(feature);
            }
        }

        foreach (var (feature, patterns) in FeaturePatterns)
        {
            foreach (var pattern in patterns)
            {
                if (ContainsWord(query, pattern))
                {
                    features.Add(feature);
                    break;
                }
            }
        }

        return features.ToList();
    }

    private static bool ContainsWord(string text, string word)
    {
        var pattern = $@"\b{Regex.Escape(word)}\b";
        return Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase);
    }

    public static string FeaturesToString(IEnumerable<string> features)
    {
        return string.Join(",", features.OrderBy(f => f));
    }

    public static List<string> StringToFeatures(string? featuresString)
    {
        if (string.IsNullOrEmpty(featuresString))
            return [];

        return featuresString.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }
}
