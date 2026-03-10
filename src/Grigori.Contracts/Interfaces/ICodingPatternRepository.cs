using Grigori.Contracts.Dtos;
using Grigori.Contracts.Parameters;

namespace Grigori.Contracts.Interfaces;

public interface ICodingPatternRepository
{
    IAsyncEnumerable<CodingPatternForList> GetAsync(CodingPatternParameters parameters, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetCategoriesAsync(CancellationToken cancellationToken = default);
    Task<CodingPatternForDetail?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<CodingPatternForDetail> CreateAsync(CodingPatternForCreate dto, CancellationToken cancellationToken = default);
    Task<CodingPatternForDetail?> UpdateAsync(int id, CodingPatternForUpdate dto, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default);
}
