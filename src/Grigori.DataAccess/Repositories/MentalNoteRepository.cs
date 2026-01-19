using Grigori.Contracts.Dtos.Notes;
using Grigori.Contracts.Interfaces;
using Grigori.Contracts.Results;
using Grigori.Database;
using Grigori.Database.Entities;
using Microsoft.EntityFrameworkCore;
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
            var notes = await _dbContext.MentalNotes
                .Where(n => n.ProjectName == projectName)
                .OrderByDescending(n => n.Priority)
                .ThenByDescending(n => n.CreatedAt)
                .Select(n => ToDto(n))
                .ToListAsync(cancellationToken);

            return notes;
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
            var categoryInt = (int)category;
            var notes = await _dbContext.MentalNotes
                .Where(n => n.ProjectName == projectName && n.Category == categoryInt)
                .OrderByDescending(n => n.Priority)
                .ThenByDescending(n => n.CreatedAt)
                .Select(n => ToDto(n))
                .ToListAsync(cancellationToken);

            return notes;
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

            var notes = await _dbContext.MentalNotes
                .Where(n => n.ProjectName == projectName && n.Tags != null && tags.Any(t => n.Tags.Contains(t)))
                .OrderByDescending(n => n.Priority)
                .ThenByDescending(n => n.CreatedAt)
                .Select(n => ToDto(n))
                .ToListAsync(cancellationToken);

            return notes;
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
            var note = await _dbContext.MentalNotes
                .Where(n => n.Id == id)
                .Select(n => ToDto(n))
                .FirstOrDefaultAsync(cancellationToken);

            return note;
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
            var now = DateTime.UtcNow;
            var entity = new MentalNote
            {
                ProjectName = request.ProjectName,
                Category = (int)request.Category,
                Title = request.Title,
                Content = request.Content,
                Tags = request.Tags.Count > 0 ? string.Join(",", request.Tags) : null,
                Priority = request.Priority,
                CreatedAt = now,
                UpdatedAt = now
            };

            _dbContext.MentalNotes.Add(entity);
            await _dbContext.SaveChangesAsync(cancellationToken);

            return ToDto(entity);
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
            var entity = await _dbContext.MentalNotes.FindAsync([id], cancellationToken);
            if (entity is null)
            {
                return GrigoriError.NotFound("Mental note", id.ToString());
            }

            if (request.Category.HasValue)
                entity.Category = (int)request.Category.Value;
            if (request.Title is not null)
                entity.Title = request.Title;
            if (request.Content is not null)
                entity.Content = request.Content;
            if (request.Tags is not null)
                entity.Tags = request.Tags.Count > 0 ? string.Join(",", request.Tags) : null;
            if (request.Priority.HasValue)
                entity.Priority = request.Priority.Value;

            entity.UpdatedAt = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync(cancellationToken);

            return ToDto(entity);
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
            var deleted = await _dbContext.MentalNotes
                .Where(n => n.Id == id)
                .ExecuteDeleteAsync(cancellationToken);

            if (deleted == 0)
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

            var notes = await _dbContext.MentalNotes
                .Where(n => n.ProjectName == projectName &&
                           (n.Title.Contains(query) || n.Content.Contains(query)))
                .OrderByDescending(n => n.Priority)
                .ThenByDescending(n => n.CreatedAt)
                .Select(n => ToDto(n))
                .ToListAsync(cancellationToken);

            return notes;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to search mental notes for project {Project}", projectName);
            return GrigoriError.DatabaseError($"Failed to search notes: {ex.Message}", ex);
        }
    }

    private static MentalNoteDto ToDto(MentalNote note) => new()
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
