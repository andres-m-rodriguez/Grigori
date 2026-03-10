namespace Grigori.Common.Pagination;

/// <summary>
/// Base record for cursor-based pagination parameters.
/// Inherit from this to add domain-specific filters.
/// </summary>
public abstract record CursorParameters(int PageSize = 20, Guid? Cursor = null);
