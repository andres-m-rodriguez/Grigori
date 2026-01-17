using Grigori.Contracts.Dtos.Search;
using Grigori.Contracts.Results;

namespace Grigori.Contracts.Interfaces;

public interface ISearchService
{
    Task<Result<SearchResultDto, GrigoriError>> SearchAsync(
        SearchRequestDto request,
        CancellationToken cancellationToken = default);

    bool IsQueryCached(string query);
    void ClearCache();
}
