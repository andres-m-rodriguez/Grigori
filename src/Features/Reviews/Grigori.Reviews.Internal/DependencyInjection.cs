using Grigori.Reviews.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Grigori.Reviews.Internal;

public static class DependencyInjection
{
    public static IServiceCollection AddReviewsInternal(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<IReviewIngestion, ReviewIngestion>();

        return services;
    }
}
