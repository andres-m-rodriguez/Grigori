using Grigori.Contracts.Dtos.Notes;
using Grigori.Contracts.Results;

namespace Grigori.Contracts.Interfaces;

/// <summary>
/// Repository interface for mental notes persistence.
/// </summary>
public interface IMentalNoteRepository
{
    /// <summary>
    /// Gets all notes for a project, ordered by priority descending then created date descending.
    /// </summary>
    Task<Result<List<MentalNoteDto>, GrigoriError>> GetByProjectAsync(
        string projectName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets notes for a project filtered by category.
    /// </summary>
    Task<Result<List<MentalNoteDto>, GrigoriError>> GetByCategoryAsync(
        string projectName,
        NoteCategory category,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets notes for a project that have any of the specified tags.
    /// </summary>
    Task<Result<List<MentalNoteDto>, GrigoriError>> GetByTagsAsync(
        string projectName,
        List<string> tags,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a single note by ID.
    /// </summary>
    Task<Result<MentalNoteDto?, GrigoriError>> GetByIdAsync(
        long id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new mental note.
    /// </summary>
    Task<Result<MentalNoteDto, GrigoriError>> CreateAsync(
        CreateMentalNoteRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing mental note.
    /// </summary>
    Task<Result<MentalNoteDto, GrigoriError>> UpdateAsync(
        long id,
        UpdateMentalNoteRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a mental note by ID.
    /// </summary>
    Task<Result<bool, GrigoriError>> DeleteAsync(
        long id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches notes by text in title and content.
    /// </summary>
    Task<Result<List<MentalNoteDto>, GrigoriError>> SearchAsync(
        string projectName,
        string query,
        CancellationToken cancellationToken = default);
}
