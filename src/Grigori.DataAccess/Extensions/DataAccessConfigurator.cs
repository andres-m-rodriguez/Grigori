using Grigori.Contracts.Interfaces;
using Grigori.DataAccess.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace Grigori.DataAccess.Extensions;

public static class DataAccessConfigurator
{
    public static IServiceCollection AddGrigoriDataAccess(this IServiceCollection services)
    {
        services.AddScoped<ICodingPatternRepository, CodingPatternRepository>();
        services.AddScoped<IDesignPreferenceRepository, DesignPreferenceRepository>();
        services.AddScoped<IAvoidanceRuleRepository, AvoidanceRuleRepository>();

        // Architecture context repositories
        services.AddScoped<IArchitecturePatternRepository, ArchitecturePatternRepository>();
        services.AddScoped<IArchitectureLayerRepository, ArchitectureLayerRepository>();
        services.AddScoped<ILayerDependencyRepository, LayerDependencyRepository>();
        services.AddScoped<ICodeTemplateRepository, CodeTemplateRepository>();
        services.AddScoped<INamingConventionRepository, NamingConventionRepository>();

        return services;
    }
}
