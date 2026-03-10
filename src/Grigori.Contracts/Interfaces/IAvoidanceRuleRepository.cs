using Grigori.Contracts.Dtos;
using Grigori.Contracts.Parameters;

namespace Grigori.Contracts.Interfaces;

public interface IAvoidanceRuleRepository
{
    IAsyncEnumerable<AvoidanceRuleForList> GetAsync(AvoidanceRuleParameters parameters, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetCategoriesAsync(CancellationToken cancellationToken = default);
    Task<AvoidanceRuleForDetail?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<AvoidanceRuleForDetail> CreateAsync(AvoidanceRuleForCreate dto, CancellationToken cancellationToken = default);
    Task<AvoidanceRuleForDetail?> UpdateAsync(int id, AvoidanceRuleForUpdate dto, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default);
}
