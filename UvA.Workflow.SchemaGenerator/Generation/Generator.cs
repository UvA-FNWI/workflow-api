using System.Reflection;
using NJsonSchema;
using UvA.Workflow.Entities.Domain;
using YamlDotNet.Serialization;

namespace UvA.Workflow.SchemaGenerator.Generation;

public class Generator(DocumentationReader documentationReader)
{
    private readonly Dictionary<Type, JsonSchema> _schemas = new();
    private readonly NullabilityInfoContext _nullabilityInfoContext = new();

    public JsonSchema Generate(Type type)
    {
        _schemas.Clear();
        
        var schema = Get(type);
        schema.Title = type.Name;
        foreach (var entry in _schemas.Where(s => s.Key != type))
            schema.Definitions.Add(entry.Key.Name, entry.Value);
        return schema;
    }
    
    private JsonSchema Get(Type type)
    {
        if (_schemas.TryGetValue(type, out var schema))
            return schema;

        if (type.IsEnum)
        {
            schema = new JsonSchema
            {
                Type = JsonObjectType.String,
            };
            foreach (var entry in type.GetEnumNames())
                schema.Enumeration.Add(entry);
            _schemas.Add(type, schema);
        }
        else
        {
            schema = new JsonSchema
            {
                Type = JsonObjectType.Object,
                AllowAdditionalProperties = false,
            };
            _schemas.Add(type, schema);

            foreach (var property in GetProperties(type))
            {
                schema.Properties.Add(property.Key, ToProperty(property.Value));
                if (IsRequired(property.Value))
                    schema.RequiredProperties.Add(property.Key);
            }
        }

        return schema;
    }
    
    private bool IsRequired(PropertyInfo property)
    {
        var isNullable = _nullabilityInfoContext.Create(property).WriteState == NullabilityState.Nullable;
        return !isNullable && property.PropertyType is {IsEnum: false, IsArray: false}
                           && property.PropertyType != typeof(bool)
                           && property.PropertyType.Name != "List`1"
                           && property.PropertyType.Name != "Dictionary`2";
    }

    private string GetName(PropertyInfo property)
    {
        var attribute = property.GetCustomAttribute<YamlMemberAttribute>();
        return attribute?.Alias ?? $"{property.Name[..1].ToLower()}{property.Name[1..]}";
    }
    
    private Dictionary<string, PropertyInfo> GetProperties(Type type)
        => type.GetProperties()
            .Where(p => p.GetCustomAttribute<YamlIgnoreAttribute>() == null)
            .Where(p => p.SetMethod != null)
            .ToDictionary(GetName, p => p);

    private static readonly Dictionary<Type, JsonObjectType> TypeMapping = new()
    {
        [typeof(string)] = JsonObjectType.String,
        [typeof(int)] = JsonObjectType.Integer,
        [typeof(bool)] = JsonObjectType.Boolean,
        [typeof(DateTime)] = JsonObjectType.String,
        [typeof(DateTime?)] = JsonObjectType.String,
        [typeof(decimal)] = JsonObjectType.Number,
        [typeof(decimal?)] = JsonObjectType.Number,
        [typeof(double)] = JsonObjectType.Number,
        [typeof(double?)] = JsonObjectType.Number
    };

    private JsonSchema GetReference(Type type)
    {
        if (TypeMapping.TryGetValue(type, out var jsonType))
            return new JsonSchema { Type = jsonType };
        return new JsonSchema { Reference = Get(type) };
    }

    private JsonSchemaProperty CreateOneOf(params JsonSchema?[] schemas)
    {
        var prop = new JsonSchemaProperty();
        foreach (var schema in schemas.Where(s => s != null))
            prop.OneOf.Add(schema!);
        return prop;
    }
    
    private JsonSchema Null => new() {Type = JsonObjectType.Null};
    
    private JsonSchemaProperty ToProperty(PropertyInfo property)
    {
        var targetType = property.PropertyType.Name == "Nullable`1"
            ? property.PropertyType.GenericTypeArguments[0]
            : property.PropertyType;
        
        var isNullable = _nullabilityInfoContext.Create(property).WriteState == NullabilityState.Nullable;
        
        var basicProp = targetType switch
        {
            // types that are compatible with string
            {Name: "BilingualString" or "EventCondition"} => CreateOneOf(
                isNullable ? Null : null,
                new JsonSchema {Type = JsonObjectType.String},
                new JsonSchemaProperty { Reference = Get(targetType) }
            ),
            {IsArray: true} or {Name: "List`1"} => new JsonSchemaProperty
            {
                Type = JsonObjectType.Array, 
                Item = GetReference(targetType.IsArray 
                    ? targetType.GetElementType()! 
                    : targetType.GenericTypeArguments[0]
                )
            },
            // Make LayoutOptions strongly typed in the schema even though it isn't in the backend
            {Name: "Dictionary`2"} when property.Name == "Layout" => CreateOneOf(System.Reflection.Assembly
                .GetAssembly(typeof(LayoutOptions))?.GetTypes()
                .Where(type => type.IsSubclassOf(typeof(LayoutOptions)))
                .Select(GetReference)
                .Append(Null)
                .ToArray() ?? [Null]),
            {Name: "Dictionary`2"} => new JsonSchemaProperty
            {
                Type = JsonObjectType.Object,
                AdditionalPropertiesSchema = GetReference(targetType.GenericTypeArguments[1])
            },
            {IsClass: true, Name: not "String"} or { IsEnum: true } => new JsonSchemaProperty {Reference = Get(targetType)},
            _ => new JsonSchemaProperty
            {
                Type = TypeMapping.GetValueOrDefault(targetType, JsonObjectType.Object)
            }
        };

        basicProp.Description = documentationReader.GetSummary(property);
        
        if (!isNullable || basicProp.OneOf.Any())
            return basicProp;

        if (basicProp.Reference == null)
        {
            basicProp.Type |= JsonObjectType.Null;
            return basicProp;
        }

        return CreateOneOf(Null, basicProp);
    }
}