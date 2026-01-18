using Grigori.Contracts.Dtos.Notes;
using Grigori.Contracts.Interfaces;
using Grigori.Contracts.Results;
using Grigori.Database;
using Microsoft.Extensions.Logging;

namespace Grigori.DataAccess.Repositories;

public class MentalNoteRepository : IMentalNoteRepository
{
    private readonly GrigoriDbContext _dbContext;
    private readonly ILogger<MentalNoteRepository> _logger;

    public MentalNoteRepository(
        GrigoriDbContext dbContext,
        ILogger<MentalNoteRepository> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<Result<List<MentalNoteDto>, GrigoriError>> GetByProjectAsync(
        string projectName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var notes = await _dbContext.GetMentalNotesByProjectAsync(projectName, cancellationToken);
            return notes.Select(ToDto).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get mental notes for project {Project}", projectName);
            return GrigoriError.DatabaseError($"Failed to get notes: {ex.Message}", ex);
        }
    }

    public async Task<Result<List<MentalNoteDto>, GrigoriError>> GetByCategoryAsync(
        string projectName,
        NoteCategory category,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var notes = await _dbContext.GetMentalNotesByCategoryAsync(projectName, (int)category, cancellationToken);
            return notes.Select(ToDto).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get mental notes for project {Project} category {Category}", projectName, category);
            return GrigoriError.DatabaseError($"Failed to get notes: {ex.Message}", ex);
        }
    }

    public async Task<Result<List<MentalNoteDto>, GrigoriError>> GetByTagsAsync(
        string projectName,
        List<string> tags,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (tags.Count == 0)
            {
                return new List<MentalNoteDto>();
            }

            var notes = await _dbContext.GetMentalNotesByTagsAsync(projectName, tags, cancellationToken);
            return notes.Select(ToDto).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get mental notes for project {Project} with tags", projectName);
            return GrigoriError.DatabaseError($"Failed to get notes: {ex.Message}", ex);
        }
    }

    public async Task<Result<MentalNoteDto?, GrigoriError>> GetByIdAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var note = await _dbContext.GetMentalNoteByIdAsync(id, cancellationToken);
            return note is not null ? ToDto(note) : null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get mental note {Id}", id);
            return GrigoriError.DatabaseError($"Failed to get note: {ex.Message}", ex);
        }
    }

    public async Task<Result<MentalNoteDto, GrigoriError>> CreateAsync(
        CreateMentalNoteRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var tags = request.Tags.Count > 0 ? string.Join(",", request.Tags) : null;

            var id = await _dbContext.InsertMentalNoteAsync(
                request.ProjectName,
                (int)request.Category,
                request.Title,
                request.Content,
                tags,
                request.Priority,
                cancellationToken);

            var note = await _dbContext.GetMentalNoteByIdAsync(id, cancellationToken);
            return ToDto(note!);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create mental note for project {Project}", request.ProjectName);
            return GrigoriError.DatabaseError($"Failed to create note: {ex.Message}", ex);
        }
    }

    public async Task<Result<MentalNoteDto, GrigoriError>> UpdateAsync(
        long id,
        UpdateMentalNoteRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var existingNote = await _dbContext.GetMentalNoteByIdAsync(id, cancellationToken);
            if (existingNote is null)
            {
                return GrigoriError.NotFound("Mental note", id.ToString());
            }

            var tags = request.Tags is not null
                ? (request.Tags.Count > 0 ? string.Join(",", request.Tags) : "")
                : null;

            var updated = await _dbContext.UpdateMentalNoteAsync(
                id,
                request.Category.HasValue ? (int)request.Category.Value : null,
                request.Title,
                request.Content,
                tags,
                request.Priority,
                cancellationToken);

            if (!updated)
            {
                return GrigoriError.DatabaseError("Failed to update note");
            }

            var note = await _dbContext.GetMentalNoteByIdAsync(id, cancellationToken);
            return ToDto(note!);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update mental note {Id}", id);
            return GrigoriError.DatabaseError($"Failed to update note: {ex.Message}", ex);
        }
    }

    public async Task<Result<bool, GrigoriError>> DeleteAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var deleted = await _dbContext.DeleteMentalNoteAsync(id, cancellationToken);
            if (!deleted)
            {
                return GrigoriError.NotFound("Mental note", id.ToString());
            }
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete mental note {Id}", id);
            return GrigoriError.DatabaseError($"Failed to delete note: {ex.Message}", ex);
        }
    }

    public async Task<Result<List<MentalNoteDto>, GrigoriError>> SearchAsync(
        string projectName,
        string query,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return await GetByProjectAsync(projectName, cancellationToken);
            }

            var notes = await _dbContext.SearchMentalNotesAsync(projectName, query, cancellationToken);
            return notes.Select(ToDto).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to search mental notes for project {Project}", projectName);
            return GrigoriError.DatabaseError($"Failed to search notes: {ex.Message}", ex);
        }
    }

    private static MentalNoteDto ToDto(Database.Models.MentalNote note)
    {
        return new MentalNoteDto
        {
            Id = note.Id,
            ProjectName = note.ProjectName,
            Category = (NoteCategory)note.Category,
            Title = note.Title,
            Content = note.Content,
            Tags = note.Tags?.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList() ?? [],
            Priority = note.Priority,
            CreatedAt = note.CreatedAt,
            UpdatedAt = note.UpdatedAt
        };
    }
}
