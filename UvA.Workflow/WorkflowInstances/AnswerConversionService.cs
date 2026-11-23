using System.Text.Json;
using UvA.Workflow.Infrastructure;
using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.WorkflowInstances;

/// <summary>
/// Service responsible for converting answer input data to BsonValue based on propertyDefinition data types.
/// Handles proper type conversion and user resolution through the user cache.
/// </summary>
public class AnswerConversionService(
    IUserService userService,
    IUserRepository userRepository,
    UserSourceResolver? userSourceResolver = null)
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
    public Task<BsonValue> ConvertToValue(JsonElement? element, PropertyDefinition propertyDefinition,
        CancellationToken ct) => ConvertToValue(element, propertyDefinition, null, ct);

    public async Task<BsonValue> ConvertToValue(JsonElement? element, PropertyDefinition propertyDefinition,
        WorkflowInstance instance, string[] pathParts, CancellationToken ct, string? addedExternalUserName = null)
    {
        var verifiedUsers = await ValidateUserSources(element, propertyDefinition, instance, pathParts, ct,
            addedExternalUserName);
        return await ConvertToValue(element, propertyDefinition, verifiedUsers, ct);
    }

    public async Task<IReadOnlyDictionary<string, UserSearchResult>> ValidateUserSources(JsonElement? element,
        PropertyDefinition propertyDefinition, WorkflowInstance instance, string[] pathParts, CancellationToken ct,
        string? addedExternalUserName = null)
    {
        var verifiedUsers = new Dictionary<string, UserSearchResult>(StringComparer.OrdinalIgnoreCase);
        await ValidateUserSources(element, propertyDefinition, instance, pathParts, instance.GetProperty(pathParts),
            verifiedUsers, addedExternalUserName, ct);
        return verifiedUsers;
    }

    private async Task ValidateUserSources(JsonElement? element, PropertyDefinition property,
        WorkflowInstance instance, string[] pathParts, BsonValue? storedValue,
        Dictionary<string, UserSearchResult> verifiedUsers, string? addedExternalUserName, CancellationToken ct)
    {
        if (element is not { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) } value)
            return;

        if (property is UserPropertyDefinition { UserSources: not null } userProperty)
        {
            IEnumerable<JsonElement> users = property.IsArray && value.ValueKind == JsonValueKind.Array
                ? value.EnumerateArray()
                : [value];
            var containingProperty = pathParts.Length > 1 ? string.Join('.', pathParts[..^1]) : null;

            foreach (var userValue in users)
            {
                if (userValue.ValueKind == JsonValueKind.Null)
                    continue;

                var name = ReadUserInput(userValue)?.UserName;
                if (string.IsNullOrWhiteSpace(name))
                    throw UserOutsideConfiguredSources(instance, pathParts);

                if ((userProperty.AllowsExternalUsers == true &&
                     string.Equals(name, addedExternalUserName, StringComparison.OrdinalIgnoreCase)) ||
                    await IsStoredUser(storedValue, name, ct))
                    continue;

                var search = userSourceResolver ??
                             throw new InvalidOperationException("User source validation is unavailable.");
                var user = await search.FindEligible(instance, userProperty, name, ct, containingProperty);
                if (user == null)
                    throw UserOutsideConfiguredSources(instance, pathParts);

                verifiedUsers[name] = user;
            }
        }
        else if (property is ObjectPropertyDefinition { WorkflowDefinition: { } definition })
        {
            if (value.ValueKind == JsonValueKind.Object)
            {
                foreach (var child in definition.Properties)
                    if (value.TryGetProperty(child.Name, out var childValue))
                        await ValidateUserSources(childValue, child, instance, [.. pathParts, child.Name],
                            (storedValue as BsonDocument)?.GetValue(child.Name, BsonNull.Value), verifiedUsers,
                            null, ct);
            }
            else if (property.IsArray && value.ValueKind == JsonValueKind.Array)
            {
                var storedItems = storedValue as BsonArray;
                var index = 0;
                foreach (var item in value.EnumerateArray())
                {
                    var storedItem = storedItems != null && index < storedItems.Count ? storedItems[index] : null;
                    await ValidateUserSources(item, property, instance, pathParts, storedItem, verifiedUsers, null, ct);
                    index++;
                }
            }
        }
    }

    private async Task<bool> IsStoredUser(BsonValue? value, string name, CancellationToken ct)
    {
        if (value is not (BsonDocument or BsonArray { Count: > 0 }))
            return false;
        if (ContainsStoredUser(value, "UserName", name))
            return true;

        // Updating a pending external user's email may also change their username.
        var user = await userService.GetUser(name, ct);
        return user?.Id != null && ContainsStoredUser(value, "_id", user.Id);
    }

    private static bool ContainsStoredUser(BsonValue value, string field, string identifier) => value switch
    {
        BsonDocument user when user.TryGetValue(field, out var storedId) &&
                               (storedId.IsString || storedId.IsObjectId) =>
            string.Equals(storedId.ToString(), identifier, StringComparison.OrdinalIgnoreCase),
        BsonArray users => users.Any(user => ContainsStoredUser(user, field, identifier)),
        _ => false
    };

    private static InvalidWorkflowStateException UserOutsideConfiguredSources(WorkflowInstance instance,
        string[] pathParts) => UserOutsideConfiguredSources(instance.Id ?? pathParts[^1], string.Join('.', pathParts));

    private static InvalidWorkflowStateException UserOutsideConfiguredSources(string stateId, string propertyName)
        => new(stateId, "UserOutsideConfiguredSources",
            $"Choose a user from the configured sources for '{propertyName}'.");

    public async Task<BsonValue> ConvertToValue(JsonElement? element, PropertyDefinition propertyDefinition,
        IReadOnlyDictionary<string, UserSearchResult>? verifiedUsers, CancellationToken ct)
    {
        propertyDefinition.EnsureWritable();
        if (element == null)
            return BsonNull.Value;

        var value = element.Value;

        if (propertyDefinition.IsArray && value.ValueKind == JsonValueKind.Array)
            return new BsonArray(await value.EnumerateArray().SelectAsync(
                (el, t) => ConvertToValue(el, propertyDefinition, verifiedUsers, t), ct));

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

            DataType.Check =>
                value.ValueKind is JsonValueKind.True or JsonValueKind.False
                    ? value.GetBoolean()
                    : BsonNull.Value,

            DataType.Currency => ConvertCurrency(value),

            DataType.User => await ConvertUser(value, propertyDefinition.Name, verifiedUsers, ct),

            DataType.Object => await ConvertObject(value, (ObjectPropertyDefinition)propertyDefinition, verifiedUsers,
                ct),

            _ => throw new NotImplementedException(
                $"Data type {propertyDefinition.DataType} is not supported for propertyDefinition '{propertyDefinition.DisplayName}'")
        };
    }

    /// <summary>
    /// Returns the first submitted choice not in the property's allowed values, or null if all are valid.
    /// </summary>
    public static string? FindInvalidChoice(JsonElement value, ChoicePropertyDefinition property)
    {
        var allowed = property.Values?.Select(v => v.Name).ToHashSet() ?? [];
        IEnumerable<JsonElement> elements = value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray()
            : [value];

        return elements
            .Where(el => el.ValueKind == JsonValueKind.String)
            .Select(el => el.GetString())
            .FirstOrDefault(name => !allowed.Contains(name!));
    }

    /// <summary>
    /// Converts a Json object to a BsonValue for an embedded object question.
    /// </summary>
    private async Task<BsonValue> ConvertObject(JsonElement value, ObjectPropertyDefinition propertyDefinition,
        IReadOnlyDictionary<string, UserSearchResult>? verifiedUsers, CancellationToken ct)
    {
        if (propertyDefinition.WorkflowDefinition == null)
            throw new Exception("WorkflowDefinition is null");

        var properties = await propertyDefinition.WorkflowDefinition.Properties
            .Where(p => value.TryGetProperty(p.Name, out _))
            .ToDictionaryAsync(
                p => p.Name,
                (p, t) => ConvertToValue(value.GetProperty(p.Name), p, verifiedUsers, t),
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
    private async Task<BsonValue> ConvertUser(JsonElement value, string propertyName,
        IReadOnlyDictionary<string, UserSearchResult>? verifiedUsers, CancellationToken ct)
    {
        if (ReadUserInput(value) is not { UserName: { } userName } userInput)
            return BsonNull.Value;

        var verified = verifiedUsers?.GetValueOrDefault(userName);
        if (verified != null)
            userInput = new UserAnswerInput(verified.UserName, verified.DisplayName, verified.Email,
                verified.IsExternal, verified.Organization);

        var user = await userService.GetUser(userInput.UserName, ct);
        if (verified != null && verified.IsExternal != (user != null && UserProviderKeys.IsExternal(user.ProviderKey)))
            throw UserOutsideConfiguredSources(propertyName, propertyName);

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
                null,
                ct);
        }

        return BsonTypeMapper.MapToBsonValue(InstanceUser.FromUser(user).ToBsonDocument());
    }

    public async Task<bool> ContainsExternalUserSelection(JsonElement value, bool isArray, CancellationToken ct)
    {
        if (isArray && value.ValueKind == JsonValueKind.Array)
        {
            foreach (var userValue in value.EnumerateArray())
            {
                if (await IsExternalUserSelection(userValue, ct))
                    return true;
            }

            return false;
        }

        return await IsExternalUserSelection(value, ct);
    }

    private async Task<bool> IsExternalUserSelection(JsonElement value, CancellationToken ct)
    {
        var userInput = ReadUserInput(value);
        if (userInput == null)
            return false;

        if (userInput.IsExternal)
            return true;

        var resolvedUser = await userService.GetUser(userInput.UserName, ct);
        return resolvedUser != null && UserProviderKeys.IsExternal(resolvedUser.ProviderKey);
    }

    private static UserAnswerInput? ReadUserInput(JsonElement value)
    {
        try
        {
            return value.Deserialize<UserAnswerInput>(Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record UserAnswerInput(
        string UserName,
        string DisplayName,
        string Email,
        bool IsExternal = false,
        Organization? Organization = null);
}