using Grigori.Contracts.Dtos;
using Grigori.Contracts.Parameters;

namespace Grigori.Contracts.Interfaces;

public interface IDesignPreferenceRepository
{
    IAsyncEnumerable<DesignPreferenceForList> GetAsync(DesignPreferenceParameters parameters, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetCategoriesAsync(CancellationToken cancellationToken = default);
    Task<DesignPreferenceForDetail?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<DesignPreferenceForDetail> CreateAsync(DesignPreferenceForCreate dto, CancellationToken cancellationToken = default);
    Task<DesignPreferenceForDetail?> UpdateAsync(int id, DesignPreferenceForUpdate dto, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default);
}
