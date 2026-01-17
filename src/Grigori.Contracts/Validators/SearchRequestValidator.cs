using FluentValidation;
using Grigori.Contracts.Dtos.Search;

namespace Grigori.Contracts.Validators;

public class SearchRequestValidator : AbstractValidator<SearchRequestDto>
{
    private static readonly string[] ValidOutputModes = ["full", "compact", "summary", "paths"];

    public SearchRequestValidator()
    {
        RuleFor(x => x.Query)
            .NotEmpty()
            .WithMessage("Query is required")
            .MaximumLength(1000)
            .WithMessage("Query must not exceed 1000 characters");

        RuleFor(x => x.Limit)
            .GreaterThan(0)
            .WithMessage("Limit must be greater than 0")
            .LessThanOrEqualTo(100)
            .WithMessage("Limit must not exceed 100");

        RuleFor(x => x.OutputMode)
            .Must(mode => ValidOutputModes.Contains(mode.ToLowerInvariant()))
            .WithMessage($"OutputMode must be one of: {string.Join(", ", ValidOutputModes)}");
    }
}
