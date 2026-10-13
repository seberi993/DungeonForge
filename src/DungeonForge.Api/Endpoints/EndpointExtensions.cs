using System.Reflection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DungeonForge.Api.Endpoints;

public static class EndpointExtensions
{
    public static IServiceCollection AddEndpoints(this IServiceCollection services, Assembly assembly)
    {
        var descriptors = assembly.DefinedTypes
            .Where(t => t is { IsAbstract: false, IsInterface: false } && t.IsAssignableTo(typeof(IEndpoint)))
            .Select(t => ServiceDescriptor.Transient(typeof(IEndpoint), t));

        services.TryAddEnumerable(descriptors);
        return services;
    }

    public static WebApplication MapEndpoints(this WebApplication app, string prefix = "/api")
    {
        var group = app.MapGroup(prefix);
        foreach (var endpoint in app.Services.GetServices<IEndpoint>())
        {
            endpoint.MapEndpoint(group);
        }

        return app;
    }
}
