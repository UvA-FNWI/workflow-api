using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using UvA.Workflow.DataNose;

namespace UvA.Workflow.Services;

public record AnswerInput(
    JsonElement? Value,
    int? DeleteFileId = null);

/// <summary>
/// Service responsible for converting answer input data to BsonValue based on propertyDefinition data types.
/// Handles proper type conversion and user resolution through the user cache.
/// </summary>
public class AnswerConversionService(IUserService userService)
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
    /// Converts a single user to BsonValue.
    /// For internal (DataNose) users, ensures a user row exists so that role
    /// resolution works. For external (EduID) users that we have not seen yet,
    /// the <see cref="InstanceUser"/> is embedded inline without creating a
    /// placeholder user row; the actual user is created later by the EduID
    /// invitation flow or on first sign-in.
    /// </summary>
    private async Task<BsonValue> ConvertUser(JsonElement value, CancellationToken ct)
    {
        try
        {
            var userSearchResult = value.Deserialize<UserSearchResult>(Options);
            if (userSearchResult == null)
                return BsonNull.Value;

            var user = await userService.GetUser(userSearchResult.UserName, ct);
            if (user != null)
                return BsonTypeMapper.MapToBsonValue(InstanceUser.FromUser(user).ToBsonDocument());

            if (userSearchResult.SearchSource == UserSearchSource.EduId)
                return BsonTypeMapper.MapToBsonValue(
                    InstanceUser.FromSearchResult(userSearchResult).ToBsonDocument());

            user = await userService.AddOrUpdateUser(userSearchResult.UserName, userSearchResult.DisplayName,
                userSearchResult.Email, ct);
            return BsonTypeMapper.MapToBsonValue(InstanceUser.FromUser(user).ToBsonDocument());
        }
        catch
        {
            return BsonNull.Value;
        }
    }
}