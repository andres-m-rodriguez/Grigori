namespace Grigori.Database.Entities;

public enum ProjectStatus
{
    /// <summary>
    /// Project is registered but not yet indexed.
    /// </summary>
    NotIndexed = 0,

    /// <summary>
    /// Project is currently being indexed.
    /// </summary>
    Indexing = 1,

    /// <summary>
    /// Project has been successfully indexed.
    /// </summary>
    Indexed = 2,

    /// <summary>
    /// Indexing failed with an error.
    /// </summary>
    Failed = 3
}
