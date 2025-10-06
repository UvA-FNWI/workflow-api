# JSON schemas
This folder contains JSON schemas for the various YAML files. They can be generated with `NJsonSchema`:
```csharp
var generator = new JsonSchemaGenerator(new SystemTextJsonSchemaGeneratorSettings
{
    SerializerOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters =
        {
            new JsonStringEnumConverter()
        }
    }
});

var formSchema = generator.Generate(typeof(Form)).ToJson();
```