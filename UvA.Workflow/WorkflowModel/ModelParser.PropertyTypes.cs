using YamlDotNet.Serialization.NamingConventions;

namespace UvA.Workflow.WorkflowModel;

public partial class ModelParser
{
    private IDeserializer CreateDeserializer(string[] folders)
    {
        // Only names and embedded status are needed before reading the typed properties.
        var headerReader = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties().Build();
        var headers = folders.Select(folder => headerReader.Deserialize<WorkflowTypeHeader>(
                _contentProvider.GetFile(Path.Combine(folder, "Entity.yaml"))))
            .ToDictionary(header => header.Name);
        var propertyTypes = new Dictionary<string, Type>(BuiltInPropertyTypes);
        foreach (var header in headers.Values)
        {
            var current = header;
            var seen = new HashSet<string>();
            while (current.IsEmbedded == null && current.InheritsFrom != null &&
                   headers.TryGetValue(current.InheritsFrom, out var parent))
            {
                if (!seen.Add(current.Name))
                    throw new Exception($"Cyclic inheritsFrom detected involving '{current.Name}'");
                current = parent;
            }

            var type = current.IsEmbedded == true
                ? typeof(ObjectPropertyDefinition)
                : typeof(ReferencePropertyDefinition);
            foreach (var name in new[] { header.Name, $"{header.Name}!", $"[{header.Name}]", $"[{header.Name}]!" })
                propertyTypes.TryAdd(name, type);
        }

        return new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .WithDuplicateKeyChecking()
            .WithTypeDiscriminatingNodeDeserializer(options =>
            {
                options.AddKeyValueTypeDiscriminator<PropertyDefinition>("type", propertyTypes);
                options.AddKeyValueTypeDiscriminator<UserSourceDefinition>("type",
                    UserSourceDefinition.KnownTypes.ToDictionary());
                options.AddUniqueKeyTypeDiscriminator<PropertyDefinition>(new Dictionary<string, Type>
                {
                    ["type"] = typeof(ChoicePropertyDefinition)
                });
            }).Build();
    }

    private void ValidateUserSources()
    {
        var model = new ModelService(this);
        foreach (var definition in WorkflowDefinitions.Values)
        foreach (var property in definition.Properties.OfType<UserPropertyDefinition>())
        foreach (var source in property.UserSources ?? [])
        {
            if (source == null)
                throw new Exception($"Empty user source on '{definition.Name}.{property.Name}'.");
            if (source is not PropertyUserSource propertySource)
                continue;

            var path = propertySource.Property;
            if (string.IsNullOrWhiteSpace(path) ||
                model.GetProperty(definition, path.Split('.')) is not UserPropertyDefinition)
                throw new Exception(
                    $"User source '{path}' on '{definition.Name}.{property.Name}' must reference a User or [User] property.");

            definition.ValidateReadLookups([new PropertyLookup(path)]);
        }
    }

    private sealed class WorkflowTypeHeader
    {
        public string Name { get; set; } = null!;
        public bool? IsEmbedded { get; set; }
        public string? InheritsFrom { get; set; }
    }
}