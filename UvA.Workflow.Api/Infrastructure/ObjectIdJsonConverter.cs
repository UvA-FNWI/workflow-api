namespace UvA.Workflow.Api.Infrastructure;

public sealed class ObjectIdJsonConverter : System.Text.Json.Serialization.JsonConverter<ObjectId>
{
    public override ObjectId Read(ref System.Text.Json.Utf8JsonReader reader, Type typeToConvert, System.Text.Json.JsonSerializerOptions options)
        => reader.TokenType == System.Text.Json.JsonTokenType.String
            ? ObjectId.Parse(reader.GetString()!)
            : throw new System.Text.Json.JsonException("Expected string for ObjectId");

    public override void Write(System.Text.Json.Utf8JsonWriter writer, ObjectId value, System.Text.Json.JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString());
}