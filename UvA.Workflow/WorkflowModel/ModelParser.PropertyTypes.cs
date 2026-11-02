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
            .WithTypeDiscriminatingNodeDeserializer(options =>
            {
                options.AddKeyValueTypeDiscriminator<PropertyDefinition>("type", propertyTypes);
                options.AddUniqueKeyTypeDiscriminator<PropertyDefinition>(new Dictionary<string, Type>
                {
                    ["type"] = typeof(ChoicePropertyDefinition)
                });
            }).Build();
    }

    private sealed class WorkflowTypeHeader
    {
        public string Name { get; set; } = null!;
        public bool? IsEmbedded { get; set; }
        public string? InheritsFrom { get; set; }
    }
}