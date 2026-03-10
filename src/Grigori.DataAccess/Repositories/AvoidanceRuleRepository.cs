using System.Runtime.CompilerServices;
using Grigori.Contracts.Dtos;
using Grigori.Contracts.Interfaces;
using Grigori.Contracts.Parameters;
using Grigori.Database;
using Grigori.Database.Models;
using Microsoft.EntityFrameworkCore;
using DtoSeverity = Grigori.Contracts.Dtos.AvoidanceSeverity;

namespace Grigori.DataAccess.Repositories;

public sealed class AvoidanceRuleRepository(GrigoriDbContext context) : IAvoidanceRuleRepository
{
    public async IAsyncEnumerable<AvoidanceRuleForList> GetAsync(
        AvoidanceRuleParameters parameters,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var query = context.AvoidanceRules.AsQueryable();

        if (!string.IsNullOrWhiteSpace(parameters.Category))
            query = query.Where(r => r.Category == parameters.Category);

        if (parameters.Severity.HasValue)
        {
            var dbSeverity = (Database.Models.AvoidanceSeverity)parameters.Severity.Value;
            query = query.Where(r => r.Severity == dbSeverity);
        }

        if (parameters.AfterId.HasValue)
            query = query.Where(r => r.Id > parameters.AfterId.Value);

        var results = query
            .OrderBy(static r => r.Id)
            .Take(parameters.PageSize)
            .Select(static r => new AvoidanceRuleForList(
                r.Id,
                r.Name,
                r.Description,
                r.Category,
                (DtoSeverity)r.Severity,
                r.PreferredAlternativeId,
                r.PreferredAlternative != null
                    ? new PreferredAlternativeForList(r.PreferredAlternative.Id, r.PreferredAlternative.Preference, r.PreferredAlternative.Category)
                    : null))
            .AsAsyncEnumerable();

        await foreach (var item in results.WithCancellation(cancellationToken))
        {
            yield return item;
        }
    }

    public async Task<IReadOnlyList<string>> GetCategoriesAsync(CancellationToken cancellationToken = default)
    {
        return await context.AvoidanceRules
            .Select(static r => r.Category)
            .Distinct()
            .OrderBy(static c => c)
            .ToListAsync(cancellationToken);
    }

    public async Task<AvoidanceRuleForDetail?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await context.AvoidanceRules
            .Where(r => r.Id == id)
            .Select(static r => new AvoidanceRuleForDetail(
                r.Id,
                r.Name,
                r.Description,
                r.Category,
                (DtoSeverity)r.Severity,
                r.PreferredAlternativeId,
                r.PreferredAlternative != null
                    ? new PreferredAlternativeForList(r.PreferredAlternative.Id, r.PreferredAlternative.Preference, r.PreferredAlternative.Category)
                    : null,
                r.CreatedAt))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<AvoidanceRuleForDetail> CreateAsync(AvoidanceRuleForCreate dto, CancellationToken cancellationToken = default)
    {
        var rule = new AvoidanceRule
        {
            Name = dto.Name,
            Description = dto.Description,
            Category = dto.Category,
            Severity = (Database.Models.AvoidanceSeverity)dto.Severity,
            PreferredAlternativeId = dto.PreferredAlternativeId,
            CreatedAt = DateTime.UtcNow
        };

        context.AvoidanceRules.Add(rule);
        await context.SaveChangesAsync(cancellationToken);

        PreferredAlternativeForList? preferredAlternative = null;
        if (rule.PreferredAlternativeId.HasValue)
        {
            preferredAlternative = await context.DesignPreferences
                .Where(p => p.Id == rule.PreferredAlternativeId.Value)
                .Select(static p => new PreferredAlternativeForList(p.Id, p.Preference, p.Category))
                .FirstOrDefaultAsync(cancellationToken);
        }

        return new AvoidanceRuleForDetail(
            rule.Id, rule.Name, rule.Description, rule.Category,
            (DtoSeverity)rule.Severity, rule.PreferredAlternativeId,
            preferredAlternative, rule.CreatedAt);
    }

    public async Task<AvoidanceRuleForDetail?> UpdateAsync(int id, AvoidanceRuleForUpdate dto, CancellationToken cancellationToken = default)
    {
        var rule = await context.AvoidanceRules.FindAsync([id], cancellationToken);
        if (rule is null) return null;

        rule.Name = dto.Name;
        rule.Description = dto.Description;
        rule.Category = dto.Category;
        rule.Severity = (Database.Models.AvoidanceSeverity)dto.Severity;
        rule.PreferredAlternativeId = dto.PreferredAlternativeId;

        await context.SaveChangesAsync(cancellationToken);

        PreferredAlternativeForList? preferredAlternative = null;
        if (rule.PreferredAlternativeId.HasValue)
        {
            preferredAlternative = await context.DesignPreferences
                .Where(p => p.Id == rule.PreferredAlternativeId.Value)
                .Select(static p => new PreferredAlternativeForList(p.Id, p.Preference, p.Category))
                .FirstOrDefaultAsync(cancellationToken);
        }

        return new AvoidanceRuleForDetail(
            rule.Id, rule.Name, rule.Description, rule.Category,
            (DtoSeverity)rule.Severity, rule.PreferredAlternativeId,
            preferredAlternative, rule.CreatedAt);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var rule = await context.AvoidanceRules.FindAsync([id], cancellationToken);
        if (rule is null) return false;

        context.AvoidanceRules.Remove(rule);
        await context.SaveChangesAsync(cancellationToken);

        return true;
    }
}
