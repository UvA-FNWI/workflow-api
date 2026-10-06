using System.Reflection;
using NJsonSchema;
using UvA.Workflow.WorkflowModel;
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

        if (type == typeof(PropertyDefinition))
        {
            schema = new JsonSchema
            {
                Type = JsonObjectType.Object,
                Description = documentationReader.GetSummary(type)
            };
            _schemas.Add(type, schema);
            // Named choices, references and objects can share the same type name.
            // anyOf allows that overlap while built-in variants match their exact type strings.
            foreach (var propertyType in PropertyTypes)
                schema.AnyOf.Add(new JsonSchema { Reference = Get(propertyType) });
        }
        else if (type.IsEnum)
        {
            schema = new JsonSchema
            {
                Type = JsonObjectType.String,
            };
            var entries = type.GetFields(BindingFlags.Public | BindingFlags.Static);
            var descriptions = entries.Select(documentationReader.GetSummary).ToArray();
            var hasDescriptions = descriptions.Any(description => description != null);
            for (var i = 0; i < entries.Length; i++)
            {
                var option = new JsonSchema { Description = descriptions[i] };
                option.Enumeration.Add(entries[i].Name);
                if (hasDescriptions)
                    schema.OneOf.Add(option);
                else
                    schema.Enumeration.Add(entries[i].Name);
            }

            _schemas.Add(type, schema);
        }
        else
        {
            schema = new JsonSchema
            {
                Type = JsonObjectType.Object,
                AllowAdditionalProperties = false,
                Description = documentationReader.GetSummary(type),
                Title = type.Name
            };
            _schemas.Add(type, schema);

            foreach (var property in GetProperties(type))
            {
                schema.Properties.Add(property.Key, ToProperty(property.Value));
                if (IsRequired(property.Value))
                    schema.RequiredProperties.Add(property.Key);
            }

            if (typeof(PropertyDefinition).IsAssignableFrom(type))
            {
                var dataType = ((PropertyDefinition)Activator.CreateInstance(type)!).DataType;
                var typeProperty = schema.Properties["type"];
                if (dataType is DataType.Choice or DataType.Reference or DataType.Object)
                {
                    typeProperty.Not = new JsonSchema();
                    foreach (var primitive in PrimitiveTypeNames)
                        typeProperty.Not.Enumeration.Add(primitive);
                }
                else
                {
                    foreach (var name in TypeNames(dataType.ToString()))
                        typeProperty.Enumeration.Add(name);
                }
            }
        }

        return schema;
    }

    private bool IsRequired(PropertyInfo property)
    {
        var isNullable = _nullabilityInfoContext.Create(property).WriteState == NullabilityState.Nullable;
        return !isNullable && property.PropertyType is { IsEnum: false, IsArray: false }
                           && property.PropertyType != typeof(bool)
                           && property.PropertyType.Name != "List`1"
                           && property.PropertyType.Name != "Dictionary`2";
    }

    private string GetName(PropertyInfo property)
    {
        var attribute = property.GetCustomAttribute<YamlMemberAttribute>();
        return attribute?.Alias ?? $"{property.Name[..1].ToLower()}{property.Name[1..]}";
    }

    private static readonly Type[] PropertyTypes = typeof(PropertyDefinition).Assembly.GetTypes()
        .Where(type => type.IsSubclassOf(typeof(PropertyDefinition)) && !type.IsAbstract).ToArray();

    private static readonly string[] PrimitiveTypeNames = PropertyTypes
        .Select(type => ((PropertyDefinition)Activator.CreateInstance(type)!).DataType)
        .Where(type => type is not (DataType.Choice or DataType.Reference or DataType.Object))
        .SelectMany(type => TypeNames(type.ToString())).ToArray();

    private static string[] TypeNames(string name) => [name, $"{name}!", $"[{name}]", $"[{name}]!"];

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

    private JsonSchema GetReference(Type type, bool isNullable = false)
    {
        var underlyingType = Nullable.GetUnderlyingType(type);
        isNullable |= underlyingType != null;
        type = underlyingType ?? type;

        if (TypeMapping.TryGetValue(type, out var jsonType))
            return new JsonSchema { Type = isNullable ? jsonType | JsonObjectType.Null : jsonType };

        var reference = new JsonSchema { Reference = Get(type) };
        return isNullable ? CreateOneOf(Null, reference) : reference;
    }

    private JsonSchemaProperty CreateOneOf(params JsonSchema?[] schemas)
    {
        var prop = new JsonSchemaProperty();
        foreach (var schema in schemas.Where(s => s != null))
            prop.OneOf.Add(schema!);
        return prop;
    }

    private JsonSchema Null => new() { Type = JsonObjectType.Null };

    private JsonSchemaProperty ToProperty(PropertyInfo property)
    {
        var targetType = property.PropertyType.Name == "Nullable`1"
            ? property.PropertyType.GenericTypeArguments[0]
            : property.PropertyType;

        var nullability = _nullabilityInfoContext.Create(property);
        var isNullable = nullability.WriteState == NullabilityState.Nullable;

        if (property.Name == "Icon" && targetType == typeof(string))
        {
            var icon = new JsonSchemaProperty { Type = JsonObjectType.String };
            foreach (var value in IconSet.Values)
                icon.Enumeration.Add(value);
            return CreateOneOf(isNullable ? Null : null, icon);
        }

        var basicProp = targetType switch
        {
            // types that are compatible with string
            { Name: "BilingualString" or "EventCondition" or "Condition" or "DeadlineCondition" } => CreateOneOf(
                isNullable ? Null : null,
                new JsonSchema { Type = JsonObjectType.String },
                new JsonSchemaProperty { Reference = Get(targetType) }
            ),
            // a single recipient string or a list of recipient strings
            { Name: "Recipients" } => CreateOneOf(
                isNullable ? Null : null,
                new JsonSchema { Type = JsonObjectType.String },
                new JsonSchema { Type = JsonObjectType.Array, Item = GetReference(typeof(string)) }
            ),
            { IsArray: true } or { Name: "List`1" } => new JsonSchemaProperty
            {
                Type = JsonObjectType.Array,
                Item = GetReference(
                    targetType.IsArray ? targetType.GetElementType()! : targetType.GenericTypeArguments[0],
                    (targetType.IsArray ? nullability.ElementType : nullability.GenericTypeArguments[0])
                    ?.WriteState == NullabilityState.Nullable
                )
            },
            { Name: "Dictionary`2" } => new JsonSchemaProperty
            {
                Type = JsonObjectType.Object,
                AdditionalPropertiesSchema = GetReference(targetType.GenericTypeArguments[1])
            },
            { IsClass: true, Name: not "String" } or { IsEnum: true } => new JsonSchemaProperty
                { Reference = Get(targetType) },
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