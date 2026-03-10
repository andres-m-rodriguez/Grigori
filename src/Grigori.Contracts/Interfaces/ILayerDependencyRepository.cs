using Grigori.Contracts.Dtos;
using Grigori.Contracts.Parameters;

namespace Grigori.Contracts.Interfaces;

public interface ILayerDependencyRepository
{
    IAsyncEnumerable<LayerDependencyForList> GetAsync(LayerDependencyParameters parameters, CancellationToken cancellationToken = default);
    Task<LayerDependencyForDetail?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<LayerDependencyForDetail> CreateAsync(LayerDependencyForCreate dto, CancellationToken cancellationToken = default);
    Task<LayerDependencyForDetail?> UpdateAsync(int id, LayerDependencyForUpdate dto, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default);
}
