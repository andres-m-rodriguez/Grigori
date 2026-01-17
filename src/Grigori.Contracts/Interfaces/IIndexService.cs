using Grigori.Contracts.Dtos.Index;
using Grigori.Contracts.Results;

namespace Grigori.Contracts.Interfaces;

public interface IIndexService
{
    Task<Result<IndexResultDto, GrigoriError>> IndexDirectoryAsync(
        IndexRequestDto request,
        CancellationToken cancellationToken = default);

    Task<Result<IndexResultDto, GrigoriError>> IndexFileAsync(
        string filePath,
        CancellationToken cancellationToken = default);

    Task<Result<bool, GrigoriError>> RemoveFileAsync(
        string filePath,
        CancellationToken cancellationToken = default);
}
