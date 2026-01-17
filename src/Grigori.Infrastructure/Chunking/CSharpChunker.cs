using Grigori.Contracts.Interfaces;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;

namespace Grigori.Infrastructure.Chunking;

public class CSharpChunker : ILanguageChunker
{
    private readonly ILogger<CSharpChunker> _logger;
    private const int MaxChunkTokens = 500;
    private const int ApproxCharsPerToken = 4;
    private const int MaxChunkChars = MaxChunkTokens * ApproxCharsPerToken;

    public CSharpChunker(ILogger<CSharpChunker> logger)
    {
        _logger = logger;
    }

    public bool CanHandle(string filePath)
    {
        return Path.GetExtension(filePath).Equals(".cs", StringComparison.OrdinalIgnoreCase);
    }

    public List<CodeChunkInput> Chunk(string filePath, string content)
    {
        try
        {
            var tree = CSharpSyntaxTree.ParseText(content);
            var root = tree.GetRoot();
            var chunks = new List<CodeChunkInput>();

            var namespaceDeclaration = root.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault();
            var namespaceName = namespaceDeclaration?.Name.ToString() ?? string.Empty;

            var typeDeclarations = root.DescendantNodes().OfType<TypeDeclarationSyntax>();
            foreach (var typeDecl in typeDeclarations)
            {
                ProcessTypeDeclaration(filePath, typeDecl, namespaceName, chunks);
            }

            var enumDeclarations = root.DescendantNodes().OfType<EnumDeclarationSyntax>();
            foreach (var enumDecl in enumDeclarations)
            {
                ProcessEnumDeclaration(filePath, enumDecl, namespaceName, chunks);
            }

            if (chunks.Count == 0)
            {
                _logger.LogDebug("No AST chunks extracted from {FilePath}", filePath);
            }

            _logger.LogDebug("Extracted {ChunkCount} AST-aware chunks from {FilePath}", chunks.Count, filePath);
            return chunks;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse C# file {FilePath} with Roslyn", filePath);
            return [];
        }
    }

    private void ProcessTypeDeclaration(string filePath, TypeDeclarationSyntax typeDecl, string namespaceName, List<CodeChunkInput> chunks)
    {
        var className = typeDecl.Identifier.Text;
        var typeKind = typeDecl switch
        {
            ClassDeclarationSyntax => "Class",
            StructDeclarationSyntax => "Struct",
            InterfaceDeclarationSyntax => "Interface",
            RecordDeclarationSyntax => "Record",
            _ => "Type"
        };

        var methods = typeDecl.Members.OfType<MethodDeclarationSyntax>().ToList();
        var properties = typeDecl.Members.OfType<PropertyDeclarationSyntax>().ToList();
        var constructors = typeDecl.Members.OfType<ConstructorDeclarationSyntax>().ToList();
        var fields = typeDecl.Members.OfType<FieldDeclarationSyntax>().ToList();

        var typeText = typeDecl.GetText().ToString();
        if (typeText.Length <= MaxChunkChars)
        {
            var lineSpan = typeDecl.GetLocation().GetLineSpan();
            var contextPrefix = BuildContextPrefix(filePath, namespaceName, className, typeKind,
                lineSpan.StartLinePosition.Line + 1, lineSpan.EndLinePosition.Line + 1);

            chunks.Add(new CodeChunkInput
            {
                FilePath = filePath,
                StartLine = lineSpan.StartLinePosition.Line + 1,
                EndLine = lineSpan.EndLinePosition.Line + 1,
                Content = contextPrefix + typeText,
                ContentHash = string.Empty,
                Features = FeatureExtractor.ExtractFeatures(typeText)
            });
            return;
        }

        var typeSignature = GetTypeSignature(typeDecl);
        if (typeSignature.Length > 0)
        {
            var lineSpan = typeDecl.GetLocation().GetLineSpan();
            var contextPrefix = BuildContextPrefix(filePath, namespaceName, className, typeKind + " Signature",
                lineSpan.StartLinePosition.Line + 1, lineSpan.StartLinePosition.Line + 1);

            chunks.Add(new CodeChunkInput
            {
                FilePath = filePath,
                StartLine = lineSpan.StartLinePosition.Line + 1,
                EndLine = lineSpan.StartLinePosition.Line + 1,
                Content = contextPrefix + typeSignature,
                ContentHash = string.Empty,
                Features = FeatureExtractor.ExtractFeatures(typeSignature)
            });
        }

        foreach (var constructor in constructors)
        {
            ProcessMember(filePath, constructor, namespaceName, className, "Constructor", chunks);
        }

        foreach (var method in methods)
        {
            ProcessMember(filePath, method, namespaceName, className, "Method", chunks);
        }

        ProcessMemberGroup(filePath, fields.Cast<MemberDeclarationSyntax>().Concat(properties).ToList(),
            namespaceName, className, "Members", chunks);
    }

    private void ProcessEnumDeclaration(string filePath, EnumDeclarationSyntax enumDecl, string namespaceName, List<CodeChunkInput> chunks)
    {
        var enumName = enumDecl.Identifier.Text;
        var enumText = enumDecl.GetText().ToString();
        var lineSpan = enumDecl.GetLocation().GetLineSpan();
        var contextPrefix = BuildContextPrefix(filePath, namespaceName, enumName, "Enum",
            lineSpan.StartLinePosition.Line + 1, lineSpan.EndLinePosition.Line + 1);

        chunks.Add(new CodeChunkInput
        {
            FilePath = filePath,
            StartLine = lineSpan.StartLinePosition.Line + 1,
            EndLine = lineSpan.EndLinePosition.Line + 1,
            Content = contextPrefix + enumText,
            ContentHash = string.Empty,
            Features = FeatureExtractor.ExtractFeatures(enumText)
        });
    }

    private void ProcessMember(string filePath, MemberDeclarationSyntax member, string namespaceName, string className, string memberKind, List<CodeChunkInput> chunks)
    {
        var memberText = member.GetText().ToString();
        var lineSpan = member.GetLocation().GetLineSpan();

        var memberName = member switch
        {
            MethodDeclarationSyntax m => m.Identifier.Text,
            ConstructorDeclarationSyntax c => c.Identifier.Text,
            PropertyDeclarationSyntax p => p.Identifier.Text,
            _ => memberKind
        };

        var contextPrefix = BuildContextPrefix(filePath, namespaceName, className, $"{memberKind}: {memberName}",
            lineSpan.StartLinePosition.Line + 1, lineSpan.EndLinePosition.Line + 1);

        if (memberText.Length > MaxChunkChars)
        {
            _logger.LogDebug("Large member {MemberName} in {ClassName} ({Length} chars)", memberName, className, memberText.Length);
        }

        chunks.Add(new CodeChunkInput
        {
            FilePath = filePath,
            StartLine = lineSpan.StartLinePosition.Line + 1,
            EndLine = lineSpan.EndLinePosition.Line + 1,
            Content = contextPrefix + memberText,
            ContentHash = string.Empty,
            Features = FeatureExtractor.ExtractFeatures(memberText)
        });
    }

    private void ProcessMemberGroup(string filePath, List<MemberDeclarationSyntax> members, string namespaceName, string className, string groupKind, List<CodeChunkInput> chunks)
    {
        if (members.Count == 0) return;

        var currentGroup = new List<MemberDeclarationSyntax>();
        var currentChars = 0;

        foreach (var member in members)
        {
            var memberText = member.GetText().ToString();

            if (currentChars + memberText.Length > MaxChunkChars && currentGroup.Count > 0)
            {
                FlushMemberGroup(filePath, currentGroup, namespaceName, className, groupKind, chunks);
                currentGroup.Clear();
                currentChars = 0;
            }

            currentGroup.Add(member);
            currentChars += memberText.Length;
        }

        if (currentGroup.Count > 0)
        {
            FlushMemberGroup(filePath, currentGroup, namespaceName, className, groupKind, chunks);
        }
    }

    private void FlushMemberGroup(string filePath, List<MemberDeclarationSyntax> members, string namespaceName, string className, string groupKind, List<CodeChunkInput> chunks)
    {
        if (members.Count == 0) return;

        var firstMember = members.First();
        var lastMember = members.Last();
        var startLine = firstMember.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
        var endLine = lastMember.GetLocation().GetLineSpan().EndLinePosition.Line + 1;

        var combinedText = string.Join("\n", members.Select(m => m.GetText().ToString()));
        var contextPrefix = BuildContextPrefix(filePath, namespaceName, className, groupKind, startLine, endLine);

        chunks.Add(new CodeChunkInput
        {
            FilePath = filePath,
            StartLine = startLine,
            EndLine = endLine,
            Content = contextPrefix + combinedText,
            ContentHash = string.Empty,
            Features = FeatureExtractor.ExtractFeatures(combinedText)
        });
    }

    private static string GetTypeSignature(TypeDeclarationSyntax typeDecl)
    {
        var modifiers = typeDecl.Modifiers.ToString();
        var keyword = typeDecl.Keyword.Text;
        var name = typeDecl.Identifier.Text;
        var typeParams = typeDecl.TypeParameterList?.ToString() ?? string.Empty;
        var baseList = typeDecl.BaseList?.ToString() ?? string.Empty;
        var constraints = string.Join(" ", typeDecl.ConstraintClauses.Select(c => c.ToString()));

        var signature = $"{modifiers} {keyword} {name}{typeParams}";
        if (!string.IsNullOrEmpty(baseList))
            signature += $" {baseList}";
        if (!string.IsNullOrEmpty(constraints))
            signature += $" {constraints}";

        return signature.Trim();
    }

    private static string BuildContextPrefix(string filePath, string namespaceName, string typeName, string memberInfo, int startLine, int endLine)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"// File: {filePath}");
        if (!string.IsNullOrEmpty(namespaceName))
            sb.AppendLine($"// Namespace: {namespaceName}");
        if (!string.IsNullOrEmpty(typeName))
            sb.AppendLine($"// Type: {typeName}");
        if (!string.IsNullOrEmpty(memberInfo) && memberInfo != typeName)
            sb.AppendLine($"// {memberInfo}");
        sb.AppendLine($"// Lines: {startLine}-{endLine}");
        return sb.ToString();
    }
}
