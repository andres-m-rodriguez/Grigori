using Grigori.Contracts.Interfaces;
using Grigori.DataAccess.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace Grigori.DataAccess.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddGrigoriDataAccess(this IServiceCollection services)
    {
        services.AddSingleton<IChunkRepository, ChunkRepository>();
        services.AddSingleton<IMentalNoteRepository, MentalNoteRepository>();
        return services;
    }
}
