// =============================================================================
// AAR.Application - Validators/ProjectValidators.cs
// FluentValidation validators for project requests
// =============================================================================

using AAR.Application.DTOs;
using FluentValidation;

namespace AAR.Application.Validators;

/// <summary>
/// Validator for create project from zip requests
/// </summary>
public class CreateProjectFromZipRequestValidator : AbstractValidator<CreateProjectFromZipRequest>
{
    public CreateProjectFromZipRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .WithMessage("Project name is required")
            .MaximumLength(200)
            .WithMessage("Project name cannot exceed 200 characters")
            .Matches(@"^[\w\s\-\.]+$")
            .WithMessage("Project name contains invalid characters");

        RuleFor(x => x.Description)
            .MaximumLength(2000)
            .WithMessage("Description cannot exceed 2000 characters")
            .When(x => !string.IsNullOrEmpty(x.Description));
    }
}

/// <summary>
/// Validator for create project from Git requests
/// </summary>
public class CreateProjectFromGitRequestValidator : AbstractValidator<CreateProjectFromGitRequest>
{
    public CreateProjectFromGitRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .WithMessage("Project name is required")
            .MaximumLength(200)
            .WithMessage("Project name cannot exceed 200 characters")
            .Matches(@"^[\w\s\-\.]+$")
            .WithMessage("Project name contains invalid characters");

        RuleFor(x => x.GitRepoUrl)
            .NotEmpty()
            .WithMessage("Git repository URL is required")
            .Must(BeValidGitUrl)
            .WithMessage("Invalid Git repository URL. Only HTTPS URLs are supported.");

        RuleFor(x => x.Description)
            .MaximumLength(2000)
            .WithMessage("Description cannot exceed 2000 characters")
            .When(x => !string.IsNullOrEmpty(x.Description));
    }

    private static bool BeValidGitUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        // Must be HTTPS URL
        if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return false;

        // Must be a valid URI
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        // Must be github.com, gitlab.com, or similar
        var allowedHosts = new[] { "github.com", "gitlab.com", "bitbucket.org", "dev.azure.com" };
        return allowedHosts.Any(h => uri.Host.EndsWith(h, StringComparison.OrdinalIgnoreCase));
    }
}
