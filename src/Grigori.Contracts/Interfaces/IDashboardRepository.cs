using Grigori.Contracts.Dtos.Dashboard;
using Grigori.Contracts.Results;

namespace Grigori.Contracts.Interfaces;

public interface IDashboardRepository
{
    Task<Result<IndexStatsDto, GrigoriError>> GetIndexStatsAsync(CancellationToken cancellationToken = default);

    Task<Result<List<IndexedProjectDto>, GrigoriError>> GetIndexedProjectsAsync(CancellationToken cancellationToken = default);
}
