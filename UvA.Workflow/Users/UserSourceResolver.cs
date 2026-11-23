using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Users;

public record UserSourceSearchGroup(string Label, UserSearchResult[] Users);

public class UserSourceResolver(IUserService userService, InstanceService instanceService, ModelService modelService)
{
    public async Task<UserSourceSearchGroup[]> Search(WorkflowInstance instance, UserPropertyDefinition property,
        string? query, CancellationToken ct, string? containingProperty = null)
    {
        var term = query?.Trim() ?? "";
        if (term.Length < 2)
            return [];

        var sources = property.UserSources ?? [new DataNoseUserSource()];
        var allowsExternalUsers = property.AllowsExternalUsers == true;
        var propertyUsers = await LoadPropertyUsers(instance, sources, containingProperty, ct);
        var seenUserNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var groups = new List<UserSourceSearchGroup>(sources.Length);

        foreach (var source in sources)
        {
            var (label, candidates) = source switch
            {
                DataNoseUserSource dataNose => ("DataNose", await userService.FindUsers(term, allowsExternalUsers,
                    dataNose.SearchOptions?.ToScope(), ct)),
                PropertyUserSource propertySource =>
                    (propertySource.Property, propertyUsers[propertySource.Property]
                        .Where(user => Matches(user, term)).Select(CreateResult)),
                _ => throw new InvalidOperationException($"Unknown user source '{source.Type}'.")
            };
            var users = candidates.Where(user => (allowsExternalUsers || !user.IsExternal) &&
                                                 seenUserNames.Add(user.UserName)).ToArray();
            groups.Add(new UserSourceSearchGroup(label, users));
        }

        return groups.ToArray();
    }

    public async Task<UserSearchResult?> FindEligible(WorkflowInstance instance, UserPropertyDefinition property,
        string username, CancellationToken ct, string? containingProperty = null)
    {
        if (property.UserSources is not { Length: > 0 } sources || string.IsNullOrWhiteSpace(username))
            return null;

        var allowsExternalUsers = property.AllowsExternalUsers == true;
        var propertyUsers = await LoadPropertyUsers(instance, sources, containingProperty, ct);
        var propertyUser = propertyUsers.Values.SelectMany(users => users)
            .FirstOrDefault(user => (allowsExternalUsers || !user.IsExternal) &&
                                    string.Equals(user.UserName, username, StringComparison.OrdinalIgnoreCase));
        if (propertyUser != null)
            return CreateResult(propertyUser);

        foreach (var source in sources.OfType<DataNoseUserSource>())
        {
            var user = await userService.FindSearchUser(username, allowsExternalUsers, source.SearchOptions?.ToScope(),
                ct);
            if (user != null && (allowsExternalUsers || !user.IsExternal) &&
                string.Equals(user.UserName, username, StringComparison.OrdinalIgnoreCase))
                return user;
        }

        return null;
    }

    private async Task<Dictionary<string, IEnumerable<InstanceUser>>> LoadPropertyUsers(WorkflowInstance instance,
        UserSourceDefinition[] sources, string? containingProperty, CancellationToken ct)
    {
        var lookupsByPath = sources.OfType<PropertyUserSource>()
            .Select(source => source.Property)
            .Distinct()
            .ToDictionary(path => path, path => GetPropertyLookup(instance, path, containingProperty));
        var lookups = lookupsByPath.Values.OfType<PropertyLookup>().Distinct().ToArray();
        var context = lookups.Length > 0 ? await instanceService.CreateContext(instance, lookups, ct) : null;
        return lookupsByPath.ToDictionary(entry => entry.Key,
            entry => GetUsers(entry.Value == null ? null : context?.Get(entry.Value)));
    }

    private PropertyLookup? GetPropertyLookup(WorkflowInstance instance, string path, string? containingProperty)
    {
        var lookup = new PropertyLookup(path);
        if (containingProperty != null &&
            modelService.GetProperty(instance, containingProperty.Split('.')) is
                ObjectPropertyDefinition { WorkflowDefinition: { } definition } &&
            definition.Properties.Contains(lookup.Parts[0]))
            lookup = new PropertyLookup($"{containingProperty}.{path}");

        return modelService.GetProperty(instance, lookup.Parts) is UserPropertyDefinition ? lookup : null;
    }

    private static UserSearchResult CreateResult(InstanceUser user) => new(user.UserName, user.DisplayName, user.Email,
        "property", user.IsExternal ? UserProviderKeys.External : UserProviderKeys.Internal, user.Organization);

    private static IEnumerable<InstanceUser> GetUsers(object? value) => value switch
    {
        InstanceUser user => [user],
        IEnumerable<object?> items => items.SelectMany(GetUsers),
        _ => []
    };

    private static bool Matches(InstanceUser user, string term) =>
        user.DisplayName?.Contains(term, StringComparison.OrdinalIgnoreCase) == true ||
        user.Email?.Contains(term, StringComparison.OrdinalIgnoreCase) == true ||
        user.UserName?.Contains(term, StringComparison.OrdinalIgnoreCase) == true;
}