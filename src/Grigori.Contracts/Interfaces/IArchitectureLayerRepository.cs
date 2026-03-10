using Grigori.Contracts.Dtos;
using Grigori.Contracts.Parameters;

namespace Grigori.Contracts.Interfaces;

public interface IArchitectureLayerRepository
{
    IAsyncEnumerable<ArchitectureLayerForList> GetAsync(ArchitectureLayerParameters parameters, CancellationToken cancellationToken = default);
    Task<ArchitectureLayerForDetail?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<ArchitectureLayerForDetail> CreateAsync(ArchitectureLayerForCreate dto, CancellationToken cancellationToken = default);
    Task<ArchitectureLayerForDetail?> UpdateAsync(int id, ArchitectureLayerForUpdate dto, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default);
}
