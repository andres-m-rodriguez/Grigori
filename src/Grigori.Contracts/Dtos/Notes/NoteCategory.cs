namespace Grigori.Contracts.Dtos.Notes;

/// <summary>
/// Categories for mental notes to organize project knowledge.
/// </summary>
public enum NoteCategory
{
    /// <summary>
    /// Architectural patterns, layer structure, design decisions.
    /// </summary>
    Architecture = 0,

    /// <summary>
    /// Coding conventions, naming standards, formatting rules.
    /// </summary>
    Convention = 1,

    /// <summary>
    /// Important warnings, pitfalls, things to avoid.
    /// </summary>
    Warning = 2,

    /// <summary>
    /// Helpful tips, best practices, shortcuts.
    /// </summary>
    Tip = 3,

    /// <summary>
    /// General context, background information, domain knowledge.
    /// </summary>
    Context = 4
}
