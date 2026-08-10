namespace Grigori.Reviews.Contracts;

/// <summary>
/// Where a Review came from, rendered as <c>github:owner/repo#4821</c>. <see cref="Integration"/>
/// names the integration that owns it and is what Reviews routes on when it needs to act.
/// </summary>
/// <remarks>
/// A Review with no Origin was never pushed anywhere — agents reviewing each other's work
/// before a branch exists is the case this leaves room for, so it is a property of a Review
/// rather than an identity for one.
/// </remarks>
public readonly record struct Origin(string Integration, string Workspace, string Number)
{
    public override string ToString() => $"{Integration}:{Workspace}#{Number}";
}
