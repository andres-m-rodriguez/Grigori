using Grigori.Reviews.Contracts.Dtos;

namespace Grigori.Reviews.Application;

/// <summary>
/// The inbound port. An integration that has observed something translates it into Grigori's
/// vocabulary and calls this; it never reaches past it into <c>Grigori.Reviews.Internal</c>.
/// </summary>
/// <remarks>
/// Implemented by Reviews, called by integrations. Ingestion is idempotent by contract: the
/// same Origin arriving twice must not produce two Reviews, because webhook deliveries are
/// retried and a reconciler will replay them.
/// </remarks>
public interface IReviewIngestion
{
    Task Ingest(ReviewOpenedDto dto, CancellationToken cancellationToken);
}
