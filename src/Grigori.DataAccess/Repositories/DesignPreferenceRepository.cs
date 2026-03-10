using System.Runtime.CompilerServices;
using Grigori.Contracts.Dtos;
using Grigori.Contracts.Interfaces;
using Grigori.Contracts.Parameters;
using Grigori.Database;
using Grigori.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace Grigori.DataAccess.Repositories;

public sealed class DesignPreferenceRepository(GrigoriDbContext context) : IDesignPreferenceRepository
{
    public async IAsyncEnumerable<DesignPreferenceForList> GetAsync(
        DesignPreferenceParameters parameters,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var query = context.DesignPreferences.AsQueryable();

        if (!string.IsNullOrWhiteSpace(parameters.Category))
            query = query.Where(p => p.Category == parameters.Category);

        if (parameters.AfterId.HasValue)
            query = query.Where(p => p.Id > parameters.AfterId.Value);

        var results = query
            .OrderBy(static p => p.Id)
            .Take(parameters.PageSize)
            .Select(static p => new DesignPreferenceForList(p.Id, p.Category, p.Preference, p.Rationale, p.Priority))
            .AsAsyncEnumerable();

        await foreach (var item in results.WithCancellation(cancellationToken))
        {
            yield return item;
        }
    }

    public async Task<IReadOnlyList<string>> GetCategoriesAsync(CancellationToken cancellationToken = default)
    {
        return await context.DesignPreferences
            .Select(static p => p.Category)
            .Distinct()
            .OrderBy(static c => c)
            .ToListAsync(cancellationToken);
    }

    public async Task<DesignPreferenceForDetail?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await context.DesignPreferences
            .Where(p => p.Id == id)
            .Select(static p => new DesignPreferenceForDetail(
                p.Id, p.Category, p.Preference, p.Rationale, p.Priority, p.CreatedAt))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<DesignPreferenceForDetail> CreateAsync(DesignPreferenceForCreate dto, CancellationToken cancellationToken = default)
    {
        var preference = new DesignPreference
        {
            Category = dto.Category,
            Preference = dto.Preference,
            Rationale = dto.Rationale,
            Priority = dto.Priority,
            CreatedAt = DateTime.UtcNow
        };

        context.DesignPreferences.Add(preference);
        await context.SaveChangesAsync(cancellationToken);

        return new DesignPreferenceForDetail(
            preference.Id, preference.Category, preference.Preference,
            preference.Rationale, preference.Priority, preference.CreatedAt);
    }

    public async Task<DesignPreferenceForDetail?> UpdateAsync(int id, DesignPreferenceForUpdate dto, CancellationToken cancellationToken = default)
    {
        var preference = await context.DesignPreferences.FindAsync([id], cancellationToken);
        if (preference is null) return null;

        preference.Category = dto.Category;
        preference.Preference = dto.Preference;
        preference.Rationale = dto.Rationale;
        preference.Priority = dto.Priority;

        await context.SaveChangesAsync(cancellationToken);

        return new DesignPreferenceForDetail(
            preference.Id, preference.Category, preference.Preference,
            preference.Rationale, preference.Priority, preference.CreatedAt);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var preference = await context.DesignPreferences.FindAsync([id], cancellationToken);
        if (preference is null) return false;

        context.DesignPreferences.Remove(preference);
        await context.SaveChangesAsync(cancellationToken);

        return true;
    }
}
