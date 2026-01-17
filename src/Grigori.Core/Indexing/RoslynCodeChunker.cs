using Grigori.Core.Storage;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;

namespace Grigori.Core.Indexing;

public class RoslynCodeChunker
{
    private readonly ILogger<RoslynCodeChunker> _logger;
    private const int MaxChunkTokens = 500;
    private const int ApproxCharsPerToken = 4;
    private const int MaxChunkChars = MaxChunkTokens * ApproxCharsPerToken;

    public RoslynCodeChunker(ILogger<RoslynCodeChunker> logger)
    {
        _logger = logger;
    }

    public List<CodeChunkInput> ChunkCSharpFile(string filePath, string content)
    {
        try
        {
            var tree = CSharpSyntaxTree.ParseText(content);
            var root = tree.GetRoot();
            var chunks = new List<CodeChunkInput>();

            // Extract namespace context
            var namespaceDeclaration = root.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault();
            var namespaceName = namespaceDeclaration?.Name.ToString() ?? string.Empty;

            // Process type declarations (classes, structs, interfaces, records, enums)
            var typeDeclarations = root.DescendantNodes().OfType<TypeDeclarationSyntax>();
            foreach (var typeDecl in typeDeclarations)
            {
                ProcessTypeDeclaration(filePath, content, typeDecl, namespaceName, chunks);
            }

            // Process enum declarations separately
            var enumDeclarations = root.DescendantNodes().OfType<EnumDeclarationSyntax>();
            foreach (var enumDecl in enumDeclarations)
            {
                ProcessEnumDeclaration(filePath, content, enumDecl, namespaceName, chunks);
            }

            // If no chunks were extracted, fall back to basic chunking
            if (chunks.Count == 0)
            {
                _logger.LogDebug("No AST chunks extracted from {FilePath}, file may be empty or have no type declarations", filePath);
            }

            _logger.LogDebug("Extracted {ChunkCount} AST-aware chunks from {FilePath}", chunks.Count, filePath);
            return chunks;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse C# file {FilePath} with Roslyn, falling back to basic chunking", filePath);
            return [];
        }
    }

    private void ProcessTypeDeclaration(string filePath, string content, TypeDeclarationSyntax typeDecl, string namespaceName, List<CodeChunkInput> chunks)
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

        // Get members that should be chunked separately
        var methods = typeDecl.Members.OfType<MethodDeclarationSyntax>().ToList();
        var properties = typeDecl.Members.OfType<PropertyDeclarationSyntax>().ToList();
        var constructors = typeDecl.Members.OfType<ConstructorDeclarationSyntax>().ToList();
        var fields = typeDecl.Members.OfType<FieldDeclarationSyntax>().ToList();

        // Check if the entire type fits in one chunk
        var typeText = typeDecl.GetText().ToString();
        if (typeText.Length <= MaxChunkChars)
        {
            var lineSpan = typeDecl.GetLocation().GetLineSpan();
            var contextPrefix = BuildContextPrefix(filePath, namespaceName, className, typeKind, lineSpan.StartLinePosition.Line + 1, lineSpan.EndLinePosition.Line + 1);

            chunks.Add(new CodeChunkInput
            {
                FilePath = filePath,
                StartLine = lineSpan.StartLinePosition.Line + 1,
                EndLine = lineSpan.EndLinePosition.Line + 1,
                Content = contextPrefix + typeText
            });
            return;
        }

        // Type is too large - chunk individual members
        // First, add the type signature (without body)
        var typeSignature = GetTypeSignature(typeDecl);
        if (typeSignature.Length > 0)
        {
            var lineSpan = typeDecl.GetLocation().GetLineSpan();
            var contextPrefix = BuildContextPrefix(filePath, namespaceName, className, typeKind + " Signature", lineSpan.StartLinePosition.Line + 1, lineSpan.StartLinePosition.Line + 1);

            chunks.Add(new CodeChunkInput
            {
                FilePath = filePath,
                StartLine = lineSpan.StartLinePosition.Line + 1,
                EndLine = lineSpan.StartLinePosition.Line + 1,
                Content = contextPrefix + typeSignature
            });
        }

        // Process constructors
        foreach (var constructor in constructors)
        {
            ProcessMember(filePath, content, constructor, namespaceName, className, "Constructor", chunks);
        }

        // Process methods
        foreach (var method in methods)
        {
            ProcessMember(filePath, content, method, namespaceName, className, "Method", chunks);
        }

        // Group fields and properties together if they're small
        var fieldPropertyGroup = new List<MemberDeclarationSyntax>();
        var fieldPropertyChars = 0;

        foreach (var field in fields)
        {
            var fieldText = field.GetText().ToString();
            if (fieldPropertyChars + fieldText.Length > MaxChunkChars && fieldPropertyGroup.Count > 0)
            {
                FlushMemberGroup(filePath, fieldPropertyGroup, namespaceName, className, "Fields", chunks);
                fieldPropertyGroup.Clear();
                fieldPropertyChars = 0;
            }
            fieldPropertyGroup.Add(field);
            fieldPropertyChars += fieldText.Length;
        }

        foreach (var property in properties)
        {
            var propertyText = property.GetText().ToString();
            if (fieldPropertyChars + propertyText.Length > MaxChunkChars && fieldPropertyGroup.Count > 0)
            {
                FlushMemberGroup(filePath, fieldPropertyGroup, namespaceName, className, "Properties", chunks);
                fieldPropertyGroup.Clear();
                fieldPropertyChars = 0;
            }
            fieldPropertyGroup.Add(property);
            fieldPropertyChars += propertyText.Length;
        }

        if (fieldPropertyGroup.Count > 0)
        {
            FlushMemberGroup(filePath, fieldPropertyGroup, namespaceName, className, "Members", chunks);
        }
    }

    private void ProcessEnumDeclaration(string filePath, string content, EnumDeclarationSyntax enumDecl, string namespaceName, List<CodeChunkInput> chunks)
    {
        var enumName = enumDecl.Identifier.Text;
        var enumText = enumDecl.GetText().ToString();
        var lineSpan = enumDecl.GetLocation().GetLineSpan();
        var contextPrefix = BuildContextPrefix(filePath, namespaceName, enumName, "Enum", lineSpan.StartLinePosition.Line + 1, lineSpan.EndLinePosition.Line + 1);

        chunks.Add(new CodeChunkInput
        {
            FilePath = filePath,
            StartLine = lineSpan.StartLinePosition.Line + 1,
            EndLine = lineSpan.EndLinePosition.Line + 1,
            Content = contextPrefix + enumText
        });
    }

    private void ProcessMember(string filePath, string content, MemberDeclarationSyntax member, string namespaceName, string className, string memberKind, List<CodeChunkInput> chunks)
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

        var contextPrefix = BuildContextPrefix(filePath, namespaceName, className, $"{memberKind}: {memberName}", lineSpan.StartLinePosition.Line + 1, lineSpan.EndLinePosition.Line + 1);

        // If member is too large, we still include it but truncate if necessary
        if (memberText.Length > MaxChunkChars)
        {
            _logger.LogDebug("Large member {MemberName} in {ClassName} ({Length} chars), including anyway", memberName, className, memberText.Length);
        }

        chunks.Add(new CodeChunkInput
        {
            FilePath = filePath,
            StartLine = lineSpan.StartLinePosition.Line + 1,
            EndLine = lineSpan.EndLinePosition.Line + 1,
            Content = contextPrefix + memberText
        });
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
            Content = contextPrefix + combinedText
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
