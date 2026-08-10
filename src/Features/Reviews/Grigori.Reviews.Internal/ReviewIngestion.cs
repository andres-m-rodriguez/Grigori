using Grigori.Reviews.Application;
using Grigori.Reviews.Contracts.Dtos;
using Microsoft.Extensions.Logging;

namespace Grigori.Reviews.Internal;

internal sealed class ReviewIngestion(ILogger<ReviewIngestion> logger) : IReviewIngestion
{
    public Task Ingest(ReviewOpenedDto dto, CancellationToken cancellationToken)
    {
        // The baseline stops at the log line. The next commit appends an event and projects a
        // row; this method is the seam that grows, so no integration has to move when it does.
        logger.LogInformation(
            "Review opened {Origin} \"{Title}\" by {Author} — {SourceBranch} into {TargetBranch} at {HeadSha}, draft {IsDraft}, description {DescriptionLength} chars, opened {OpenedAt:O} — {Url}",
            dto.Origin.ToString(),
            dto.Title,
            dto.Author,
            dto.SourceBranch,
            dto.TargetBranch,
            dto.HeadSha,
            dto.IsDraft,
            dto.Description?.Length ?? 0,
            dto.OpenedAt,
            dto.Url);

        // Descriptions are multi-line and often long, so the body goes out separately rather
        // than wrapping the line above. Agents are the eventual consumer, not this log.
        if (dto.Description is string description)
            logger.LogDebug("Review {Origin} description:\n{Description}", dto.Origin.ToString(), description);

        return Task.CompletedTask;
    }
}
