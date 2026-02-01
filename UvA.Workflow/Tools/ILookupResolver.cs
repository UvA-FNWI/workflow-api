using Microsoft.Extensions.DependencyInjection;

namespace UvA.Workflow.Tools;

public interface ILookupResolver
{
    public Task<object?> Resolve(ComplexLookup input, Dictionary<Lookup, object?> context, CancellationToken ct);
}

public static class LookupResolverExtensions
{
    private static readonly Dictionary<string, Type> Types = new();

    public static ILookupResolver GetResolver(this IServiceProvider provider, string name)
        => (ILookupResolver)provider.GetRequiredService(Types[name]);

    public static IServiceCollection AddLookupResolvers<T>(this IServiceCollection services)
    {
        var resolvers = typeof(T).Assembly.GetTypes().Where(t => t.GetInterfaces().Contains(typeof(ILookupResolver)));
        foreach (var resolver in resolvers)
        {
            var name = resolver.Name.Replace("Resolver", "");
            name = $"{char.ToLower(name[0])}{name[1..]}";
            Types.Add(name, resolver);
            services.AddScoped(resolver);
        }

        return services;
    }
}