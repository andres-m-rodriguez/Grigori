using Grigori.Contracts.Dtos;
using Grigori.Contracts.Parameters;

namespace Grigori.Contracts.Interfaces;

public interface INamingConventionRepository
{
    IAsyncEnumerable<NamingConventionForList> GetAsync(NamingConventionParameters parameters, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetContextsAsync(CancellationToken cancellationToken = default);
    Task<NamingConventionForDetail?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<NamingConventionForDetail> CreateAsync(NamingConventionForCreate dto, CancellationToken cancellationToken = default);
    Task<NamingConventionForDetail?> UpdateAsync(int id, NamingConventionForUpdate dto, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default);
}
