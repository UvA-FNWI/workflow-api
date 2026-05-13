using System.Text.Json;
using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.WorkflowInstances;

/// <summary>
/// Service responsible for converting answer input data to BsonValue based on propertyDefinition data types.
/// Handles proper type conversion and user resolution through the user cache.
/// </summary>
public class AnswerConversionService(IUserService userService, IUserRepository userRepository)
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters =
        {
            new JsonStringEnumConverter()
        }
    };

    /// <summary>
    /// Converts an answer input to a BsonValue based on the propertyDefinition's data type.
    /// </summary>
    /// <param name="element">The answer for the given propertyDefinition</param>
    /// <param name="propertyDefinition">The propertyDefinition definition containing data type information</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>BsonValue representation of the answer</returns>
    public async Task<BsonValue> ConvertToValue(JsonElement? element, PropertyDefinition propertyDefinition,
        CancellationToken ct)
    {
        if (element == null)
            return BsonNull.Value;

        var value = element.Value;

        if (propertyDefinition.IsArray && value.ValueKind == JsonValueKind.Array)
            return new BsonArray(await value.EnumerateArray().SelectAsync(
                async (el, t) => await ConvertToValue(el, propertyDefinition, t), ct));

        return propertyDefinition.DataType switch
        {
            DataType.String or DataType.Choice or DataType.Reference =>
                value.ValueKind == JsonValueKind.String ? value.GetString() : BsonNull.Value,

            DataType.Double =>
                value.ValueKind == JsonValueKind.Number ? value.GetDouble() : BsonNull.Value,

            DataType.Int =>
                value.ValueKind == JsonValueKind.Number ? value.GetInt32() : BsonNull.Value,

            DataType.DateTime or DataType.Date =>
                value.ValueKind == JsonValueKind.String && DateTime.TryParse(value.GetString(), out var dt)
                    ? dt
                    : BsonNull.Value,

            DataType.Boolean =>
                value.ValueKind is JsonValueKind.True or JsonValueKind.False
                    ? value.GetBoolean()
                    : BsonNull.Value,

            DataType.Currency => ConvertCurrency(value),

            DataType.User => await ConvertUser(value, ct),

            DataType.Object => await ConvertObject(value, propertyDefinition, ct),

            _ => throw new NotImplementedException(
                $"Data type {propertyDefinition.DataType} is not supported for propertyDefinition '{propertyDefinition.DisplayName}'")
        };
    }

    /// <summary>
    /// Converts a Json object to a BsonValue for an embedded object question.
    /// </summary>
    private async Task<BsonValue> ConvertObject(JsonElement value, PropertyDefinition propertyDefinition,
        CancellationToken ct)
    {
        if (propertyDefinition.WorkflowDefinition == null)
            throw new Exception("WorkflowDefinition is null");

        var properties = await propertyDefinition.WorkflowDefinition.Properties
            .Where(p => value.TryGetProperty(p.Name, out _))
            .ToDictionaryAsync(
                p => p.Name,
                (p, t) => ConvertToValue(value.GetProperty(p.Name), p, t),
                ct
            );
        return new BsonDocument(properties);
    }

    /// <summary>
    /// Converts Currency from JsonElement to BsonValue
    /// </summary>
    private BsonValue ConvertCurrency(JsonElement value)
    {
        try
        {
            var currency = value.GetProperty("currency").GetString();
            var amount = value.GetProperty("amount").GetDouble();

            if (currency == null)
                return BsonNull.Value;

            return new CurrencyAmount(currency, amount).ToBsonDocument();
        }
        catch
        {
            return BsonNull.Value;
        }
    }

    /// <summary>
    /// Converts a single user to BsonValue using the user cache service.
    /// </summary>
    private async Task<BsonValue> ConvertUser(JsonElement value, CancellationToken ct)
    {
        UserAnswerInput? userInput;
        try
        {
            userInput = value.Deserialize<UserAnswerInput>(Options);
        }
        catch (JsonException)
        {
            return BsonNull.Value;
        }

        if (userInput == null)
            return BsonNull.Value;

        var user = await userService.GetUser(userInput.UserName, ct);
        if (user == null)
        {
            if (userInput.IsExternal && !string.IsNullOrWhiteSpace(userInput.Email))
            {
                var existingExternalUser = await userRepository.GetByEmail(userInput.Email, ct);
                if (existingExternalUser != null && UserProviderKeys.IsExternal(existingExternalUser.ProviderKey))
                    user = existingExternalUser;
            }

            // External users must already exist; don't recreate them from answer payloads.
            if (user == null && userInput.IsExternal)
                return BsonNull.Value;

            user ??= await userService.AddOrUpdateUser(
                userInput.UserName,
                userInput.DisplayName,
                userInput.Email,
                UserProviderKeys.Internal,
                userInput.Organization,
                ct);
        }

        return BsonTypeMapper.MapToBsonValue(InstanceUser.FromUser(user).ToBsonDocument());
    }

    private sealed record UserAnswerInput(
        string UserName,
        string DisplayName,
        string Email,
        bool IsExternal = false,
        Organization? Organization = null);
}