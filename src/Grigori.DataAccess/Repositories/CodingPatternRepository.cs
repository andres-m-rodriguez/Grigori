using System.Runtime.CompilerServices;
using Grigori.Contracts.Dtos;
using Grigori.Contracts.Interfaces;
using Grigori.Contracts.Parameters;
using Grigori.Database;
using Grigori.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace Grigori.DataAccess.Repositories;

public sealed class CodingPatternRepository(GrigoriDbContext context) : ICodingPatternRepository
{
    public async IAsyncEnumerable<CodingPatternForList> GetAsync(
        CodingPatternParameters parameters,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var query = context.CodingPatterns.AsQueryable();

        if (!string.IsNullOrWhiteSpace(parameters.Category))
            query = query.Where(p => p.Category == parameters.Category);

        if (parameters.AfterId.HasValue)
            query = query.Where(p => p.Id > parameters.AfterId.Value);

        var results = query
            .OrderBy(static p => p.Id)
            .Take(parameters.PageSize)
            .Select(static p => new CodingPatternForList(p.Id, p.Name, p.Description, p.Category, p.Example, p.IsActive))
            .AsAsyncEnumerable();

        await foreach (var item in results.WithCancellation(cancellationToken))
        {
            yield return item;
        }
    }

    public async Task<IReadOnlyList<string>> GetCategoriesAsync(CancellationToken cancellationToken = default)
    {
        return await context.CodingPatterns
            .Select(static p => p.Category)
            .Distinct()
            .OrderBy(static c => c)
            .ToListAsync(cancellationToken);
    }

    public async Task<CodingPatternForDetail?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await context.CodingPatterns
            .Where(p => p.Id == id)
            .Select(static p => new CodingPatternForDetail(
                p.Id, p.Name, p.Description, p.Category, p.Example, p.IsActive, p.CreatedAt))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<CodingPatternForDetail> CreateAsync(CodingPatternForCreate dto, CancellationToken cancellationToken = default)
    {
        var pattern = new CodingPattern
        {
            Name = dto.Name,
            Description = dto.Description,
            Category = dto.Category,
            Example = dto.Example,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        context.CodingPatterns.Add(pattern);
        await context.SaveChangesAsync(cancellationToken);

        return new CodingPatternForDetail(
            pattern.Id, pattern.Name, pattern.Description, pattern.Category,
            pattern.Example, pattern.IsActive, pattern.CreatedAt);
    }

    public async Task<CodingPatternForDetail?> UpdateAsync(int id, CodingPatternForUpdate dto, CancellationToken cancellationToken = default)
    {
        var pattern = await context.CodingPatterns.FindAsync([id], cancellationToken);
        if (pattern is null) return null;

        pattern.Name = dto.Name;
        pattern.Description = dto.Description;
        pattern.Category = dto.Category;
        pattern.Example = dto.Example;
        pattern.IsActive = dto.IsActive;

        await context.SaveChangesAsync(cancellationToken);

        return new CodingPatternForDetail(
            pattern.Id, pattern.Name, pattern.Description, pattern.Category,
            pattern.Example, pattern.IsActive, pattern.CreatedAt);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var pattern = await context.CodingPatterns.FindAsync([id], cancellationToken);
        if (pattern is null) return false;

        context.CodingPatterns.Remove(pattern);
        await context.SaveChangesAsync(cancellationToken);

        return true;
    }
}
