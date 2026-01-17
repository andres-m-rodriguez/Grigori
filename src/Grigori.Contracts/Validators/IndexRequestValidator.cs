using FluentValidation;
using Grigori.Contracts.Dtos.Index;

namespace Grigori.Contracts.Validators;

public class IndexRequestValidator : AbstractValidator<IndexRequestDto>
{
    public IndexRequestValidator()
    {
        RuleFor(x => x.Path)
            .NotEmpty()
            .WithMessage("Path is required")
            .Must(path => Path.IsPathFullyQualified(path))
            .WithMessage("Path must be an absolute path");
    }
}
