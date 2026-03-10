using Grigori.Contracts.Dtos;
using Grigori.Contracts.Parameters;

namespace Grigori.Contracts.Interfaces;

public interface ICodeTemplateRepository
{
    IAsyncEnumerable<CodeTemplateForList> GetAsync(CodeTemplateParameters parameters, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetCategoriesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetLanguagesAsync(CancellationToken cancellationToken = default);
    Task<CodeTemplateForDetail?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<CodeTemplateForDetail> CreateAsync(CodeTemplateForCreate dto, CancellationToken cancellationToken = default);
    Task<CodeTemplateForDetail?> UpdateAsync(int id, CodeTemplateForUpdate dto, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default);
}
