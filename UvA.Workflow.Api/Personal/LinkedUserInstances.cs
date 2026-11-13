namespace UvA.Workflow.Api.Personal;

public class LinkedUserInstances(
    IWorkflowInstanceRepository repository,
    ModelService modelService,
    RightsService rightsService)
{
    public static IEnumerable<PropertyDefinition> GetUserProperties(WorkflowDefinition definition) =>
        definition.Properties
            .Where(property => property.DataType == DataType.User)
            .DistinctBy(property => property.Name, StringComparer.OrdinalIgnoreCase);

    internal static FilterDefinition<WorkflowInstance>? BuildDirectAssignmentFilter(
        IEnumerable<WorkflowDefinition> definitions,
        IReadOnlyCollection<ObjectId> userIds,
        Func<WorkflowDefinition, IEnumerable<PropertyDefinition>>? getProperties = null)
    {
        if (userIds.Count == 0)
            return null;

        var selectProperties = getProperties ?? GetUserProperties;
        var builder = Builders<WorkflowInstance>.Filter;
        return BuildInstanceFilter(definitions, definition => selectProperties(definition)
            .Select(property => builder.In($"Properties.{property.Name}._id", userIds)));
    }

    internal async Task<IReadOnlyList<LinkedInstance>> FindVisibleLinks(
        IReadOnlyCollection<ObjectId> userIds, CancellationToken ct, bool stopWhenAllUsersFound = false)
    {
        var ids = userIds.ToHashSet();
        var remaining = stopWhenAllUsersFound ? ids : null;
        var visible = new List<LinkedInstance>();
        foreach (var match in await FindLinks(ids, ct))
        {
            ct.ThrowIfCancellationRequested();
            if (remaining != null && !match.Users.Any(link => remaining.Contains(link.Id)))
                continue;
            if (!await rightsService.Can(match.Instance, RoleAction.View))
                continue;

            visible.Add(match);
            if (remaining != null)
            {
                remaining.ExceptWith(match.Users.Select(link => link.Id));
                if (remaining.Count == 0)
                    break;
            }
        }

        return visible;
    }

    private async Task<IEnumerable<LinkedInstance>> FindLinks(IReadOnlySet<ObjectId> userIds, CancellationToken ct)
    {
        var definitions = modelService.WorkflowDefinitions;
        var filter = BuildDirectAssignmentFilter(definitions.Values, userIds);
        if (filter == null)
            return [];

        var matches = new Dictionary<string, (WorkflowInstance Instance, HashSet<UserLink> Users)>();
        var pending = new List<((string Definition, string Id) Source, UserLink Link)>();
        var propagated = new HashSet<(string InstanceId, UserLink Link)>();

        void AddMatch(WorkflowInstance instance, UserLink link)
        {
            if (!matches.TryGetValue(instance.Id, out var match))
                matches[instance.Id] = match = (instance, []);
            match.Users.Add(link);
        }

        void QueueInheritedLink(WorkflowInstance instance, UserLink link)
        {
            if (propagated.Add((instance.Id, link)))
                pending.Add(((instance.WorkflowDefinition, instance.Id), link));
        }

        foreach (var instance in await repository.GetByFilter(filter, ct))
        {
            ct.ThrowIfCancellationRequested();
            foreach (var (link, isInheritable) in GetDirectLinks(instance, definitions[instance.WorkflowDefinition],
                         userIds))
            {
                AddMatch(instance, link);
                if (isInheritable)
                    QueueInheritedLink(instance, link);
            }
        }

        // Follow typed references in reverse. A role can pass through a workflow that doesn't declare it.
        while (pending.Count > 0)
        {
            var linksBySource = pending.ToLookup(entry => entry.Source, entry => entry.Link);
            var referenceFilter = BuildReferenceFilter(definitions.Values, linksBySource.Select(group => group.Key));
            if (referenceFilter == null)
                break;

            pending.Clear();
            foreach (var instance in await repository.GetByFilter(referenceFilter, ct))
            {
                ct.ThrowIfCancellationRequested();
                var definition = definitions[instance.WorkflowDefinition];
                foreach (var source in GetReferencedInstances(instance, definition))
                foreach (var link in linksBySource[source])
                {
                    if (definition.Roles.GetOrDefault(link.Role) != null)
                        AddMatch(instance, link);
                    QueueInheritedLink(instance, link);
                }
            }
        }

        return matches.Values.Select(match => new LinkedInstance(match.Instance, match.Users));
    }

    private static IEnumerable<(UserLink Link, bool IsInheritable)> GetDirectLinks(
        WorkflowInstance instance, WorkflowDefinition definition, IReadOnlySet<ObjectId> userIds) =>
        from property in GetUserProperties(definition)
        from id in GetUserIds(instance.GetProperty(property.Name))
        where userIds.Contains(id)
        select (new UserLink(id, property.Name), property is UserPropertyDefinition { InheritRole: true });

    private static FilterDefinition<WorkflowInstance>? BuildReferenceFilter(
        IEnumerable<WorkflowDefinition> definitions, IEnumerable<(string Definition, string Id)> sources)
    {
        var idsByDefinition = sources.ToLookup(source => source.Definition, source => source.Id);
        var builder = Builders<WorkflowInstance>.Filter;
        return BuildInstanceFilter(definitions, definition => GetReferences(definition)
            .Where(property => idsByDefinition.Contains(property.Definition))
            .Select(property => builder.In($"Properties.{property.Name}",
                idsByDefinition[property.Definition])));
    }

    private static FilterDefinition<WorkflowInstance>? BuildInstanceFilter(
        IEnumerable<WorkflowDefinition> definitions,
        Func<WorkflowDefinition, IEnumerable<FilterDefinition<WorkflowInstance>>> getPropertyFilters)
    {
        var builder = Builders<WorkflowInstance>.Filter;
        var filters = new List<FilterDefinition<WorkflowInstance>>();
        foreach (var definition in definitions)
        {
            var propertyFilters = getPropertyFilters(definition).ToArray();
            if (propertyFilters.Length > 0)
                filters.Add(builder.Eq(instance => instance.WorkflowDefinition, definition.Name) &
                            builder.Or(propertyFilters));
        }

        return filters.Count == 0 ? null : builder.Or(filters);
    }

    private static IEnumerable<(string Definition, string Id)> GetReferencedInstances(
        WorkflowInstance instance, WorkflowDefinition definition) =>
        from property in GetReferences(definition)
        from id in GetReferenceIds(instance.GetProperty(property.Name))
        select (property.Definition, id);

    private static IEnumerable<(string Name, string Definition)> GetReferences(WorkflowDefinition definition) =>
        from property in definition.Properties.OfType<ReferencePropertyDefinition>()
        where property.WorkflowDefinition != null
        select (property.Name, property.WorkflowDefinition!.Name);

    private static IEnumerable<string> GetReferenceIds(BsonValue? value) => value switch
    {
        BsonString id => [id.Value],
        BsonArray ids => ids.OfType<BsonString>().Select(id => id.Value),
        _ => []
    };

    private static IEnumerable<ObjectId> GetUserIds(BsonValue? value) =>
        (value switch
        {
            BsonDocument user => [user],
            BsonArray users => users.OfType<BsonDocument>(),
            _ => []
        })
        .Select(user => user.GetValue("_id", BsonNull.Value))
        .OfType<BsonObjectId>()
        .Select(id => id.Value);

    internal readonly record struct UserLink(ObjectId Id, string Role);

    internal sealed record LinkedInstance(WorkflowInstance Instance, IReadOnlyCollection<UserLink> Users);
}