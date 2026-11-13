namespace UvA.Workflow.Api.Personal;

internal static class LinkedUserInstances
{
    public static IEnumerable<PropertyDefinition> GetUserProperties(WorkflowDefinition definition) =>
        definition.Properties
            .Where(property => property.DataType == DataType.User)
            .DistinctBy(property => property.Name, StringComparer.OrdinalIgnoreCase);

    public static FilterDefinition<WorkflowInstance>? BuildFilter(IEnumerable<WorkflowDefinition> definitions,
        IReadOnlyCollection<ObjectId> userIds,
        Func<WorkflowDefinition, IEnumerable<PropertyDefinition>>? getProperties = null)
    {
        if (userIds.Count == 0)
            return null;

        var selectProperties = getProperties ?? GetUserProperties;
        var builder = Builders<WorkflowInstance>.Filter;
        var filters = new List<FilterDefinition<WorkflowInstance>>();
        foreach (var definition in definitions)
        {
            var roleFilters = selectProperties(definition)
                .Select(property => builder.In($"Properties.{property.Name}._id", userIds))
                .ToArray();
            if (roleFilters.Length > 0)
                filters.Add(builder.Eq(instance => instance.WorkflowDefinition, definition.Name) &
                            builder.Or(roleFilters));
        }

        return filters.Count == 0 ? null : builder.Or(filters);
    }

    public static IEnumerable<ObjectId> GetUserIds(WorkflowInstance instance, WorkflowDefinition definition) =>
        GetUserProperties(definition)
            .SelectMany(property => instance.GetProperty(property.Name) switch
            {
                BsonDocument user => [user],
                BsonArray users => users.OfType<BsonDocument>(),
                _ => []
            })
            .Select(user => user.GetValue("_id", BsonNull.Value))
            .OfType<BsonObjectId>()
            .Select(id => id.Value);
}