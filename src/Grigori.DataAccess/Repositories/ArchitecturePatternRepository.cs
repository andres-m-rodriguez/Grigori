using System.Runtime.CompilerServices;
using Grigori.Contracts.Dtos;
using Grigori.Contracts.Interfaces;
using Grigori.Contracts.Parameters;
using Grigori.Database;
using Grigori.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace Grigori.DataAccess.Repositories;

public sealed class ArchitecturePatternRepository(GrigoriDbContext context)
    : IArchitecturePatternRepository
{
    public async IAsyncEnumerable<ArchitecturePatternForList> GetAsync(
        ArchitecturePatternParameters parameters,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var query = context.ArchitecturePatterns.AsQueryable();

        if (parameters.AfterId.HasValue)
            query = query.Where(p => p.Id > parameters.AfterId.Value);

        var results = query
            .OrderBy(static p => p.Id)
            .Take(parameters.PageSize)
            .Select(static p => new ArchitecturePatternForList(
                p.Id,
                p.Name,
                p.Description,
                p.IsActive,
                p.Layers.Count))
            .AsAsyncEnumerable();

        await foreach (var item in results.WithCancellation(cancellationToken))
        {
            yield return item;
        }
    }

    public async Task<ArchitecturePatternForDetail?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        return await context.ArchitecturePatterns
            .Where(p => p.Id == id)
            .Select(static p => new ArchitecturePatternForDetail(
                p.Id,
                p.Name,
                p.Description,
                p.Diagram,
                p.IsActive,
                p.CreatedAt,
                p.Layers.OrderBy(l => l.Order)
                    .Select(l => new ArchitectureLayerForList(
                        l.Id,
                        l.PatternId,
                        l.Name,
                        l.Description,
                        l.Responsibility,
                        l.Order))
                    .ToList()))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<ArchitecturePatternForDetail?> GetActiveAsync(
        CancellationToken cancellationToken = default)
    {
        return await context.ArchitecturePatterns
            .Where(static p => p.IsActive)
            .Select(static p => new ArchitecturePatternForDetail(
                p.Id,
                p.Name,
                p.Description,
                p.Diagram,
                p.IsActive,
                p.CreatedAt,
                p.Layers.OrderBy(l => l.Order)
                    .Select(l => new ArchitectureLayerForList(
                        l.Id,
                        l.PatternId,
                        l.Name,
                        l.Description,
                        l.Responsibility,
                        l.Order))
                    .ToList()))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<ArchitecturePatternForDetail> CreateAsync(
        ArchitecturePatternForCreate dto,
        CancellationToken cancellationToken = default)
    {
        var pattern = new ArchitecturePattern
        {
            Name = dto.Name,
            Description = dto.Description,
            Diagram = dto.Diagram,
            IsActive = false,
            CreatedAt = DateTime.UtcNow,
        };

        context.ArchitecturePatterns.Add(pattern);
        await context.SaveChangesAsync(cancellationToken);

        return new ArchitecturePatternForDetail(
            pattern.Id,
            pattern.Name,
            pattern.Description,
            pattern.Diagram,
            pattern.IsActive,
            pattern.CreatedAt,
            []);
    }

    public async Task<ArchitecturePatternForDetail?> UpdateAsync(
        int id,
        ArchitecturePatternForUpdate dto,
        CancellationToken cancellationToken = default)
    {
        var pattern = await context.ArchitecturePatterns.FindAsync([id], cancellationToken);
        if (pattern is null)
            return null;

        pattern.Name = dto.Name;
        pattern.Description = dto.Description;
        pattern.Diagram = dto.Diagram;
        pattern.IsActive = dto.IsActive;

        await context.SaveChangesAsync(cancellationToken);

        var layers = await context.ArchitectureLayers
            .Where(l => l.PatternId == id)
            .OrderBy(static l => l.Order)
            .Select(static l => new ArchitectureLayerForList(
                l.Id,
                l.PatternId,
                l.Name,
                l.Description,
                l.Responsibility,
                l.Order))
            .ToListAsync(cancellationToken);

        return new ArchitecturePatternForDetail(
            pattern.Id,
            pattern.Name,
            pattern.Description,
            pattern.Diagram,
            pattern.IsActive,
            pattern.CreatedAt,
            layers);
    }

    public async Task<bool> SetActiveAsync(int id, CancellationToken cancellationToken = default)
    {
        var pattern = await context.ArchitecturePatterns.FindAsync([id], cancellationToken);
        if (pattern is null)
            return false;

        await context.ArchitecturePatterns
            .Where(static p => p.IsActive)
            .ExecuteUpdateAsync(
                static s => s.SetProperty(static p => p.IsActive, false),
                cancellationToken);

        pattern.IsActive = true;
        await context.SaveChangesAsync(cancellationToken);

        return true;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var pattern = await context.ArchitecturePatterns.FindAsync([id], cancellationToken);
        if (pattern is null)
            return false;

        context.ArchitecturePatterns.Remove(pattern);
        await context.SaveChangesAsync(cancellationToken);

        return true;
    }
}
