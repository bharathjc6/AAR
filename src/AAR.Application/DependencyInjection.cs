// =============================================================================
// AAR.Application - DependencyInjection.cs
// Extension methods for registering application services
// =============================================================================

using AAR.Application.Services;
using AAR.Application.Validators;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace AAR.Application;

/// <summary>
/// Extension methods for configuring application layer services
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Adds application layer services to the DI container
    /// </summary>
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        // Register services
        services.AddScoped<IProjectService, ProjectService>();
        services.AddScoped<IReportService, ReportService>();
        services.AddScoped<IReportAggregator, ReportAggregator>();

        // Register validators
        services.AddValidatorsFromAssemblyContaining<CreateProjectFromZipRequestValidator>();

        return services;
    }
}
