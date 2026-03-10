using Grigori.Contracts.Dtos;
using Grigori.Contracts.Parameters;

namespace Grigori.Contracts.Interfaces;

public interface IArchitecturePatternRepository
{
    IAsyncEnumerable<ArchitecturePatternForList> GetAsync(ArchitecturePatternParameters parameters, CancellationToken cancellationToken = default);
    Task<ArchitecturePatternForDetail?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<ArchitecturePatternForDetail?> GetActiveAsync(CancellationToken cancellationToken = default);
    Task<ArchitecturePatternForDetail> CreateAsync(ArchitecturePatternForCreate dto, CancellationToken cancellationToken = default);
    Task<ArchitecturePatternForDetail?> UpdateAsync(int id, ArchitecturePatternForUpdate dto, CancellationToken cancellationToken = default);
    Task<bool> SetActiveAsync(int id, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default);
}
