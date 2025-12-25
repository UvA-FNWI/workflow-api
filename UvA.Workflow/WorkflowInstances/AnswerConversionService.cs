using System.Text.Json;
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
        { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Converts an answer input to a BsonValue based on the propertyDefinition's data type.
    /// </summary>
    /// <param name="answerInput">The object containing the answers for the given propertyDefinition</param>
    /// <param name="propertyDefinition">The propertyDefinition definition containing data type information</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>BsonValue representation of the answer</returns>
    public async Task<BsonValue> ConvertToValue(AnswerInput answerInput, PropertyDefinition propertyDefinition,
        CancellationToken ct)
    {
        if (answerInput.Value == null)
            return BsonNull.Value;

        var value = answerInput.Value.Value;

        if (propertyDefinition.IsArray && value.ValueKind == JsonValueKind.Array)
        {
            var res = new List<BsonValue>();
            foreach (var item in value.EnumerateArray())
                res.Add(await ConvertToValue(new AnswerInput(item), propertyDefinition, ct));
            return new BsonArray(res);
        }

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

            DataType.Currency => await ConvertCurrency(value, ct),

            DataType.User when propertyDefinition.IsArray => await ConvertUserArray(value, ct),

            DataType.User => await ConvertUser(value, ct),


            _ => throw new NotImplementedException(
                $"Data type {propertyDefinition.DataType} is not supported for propertyDefinition '{propertyDefinition.DisplayName}'")
        };
    }

    /// <summary>
    /// Converts Currency from JsonElement to BsonValue
    /// </summary>
    private Task<BsonValue> ConvertCurrency(JsonElement value, CancellationToken ct)
    {
        try
        {
            var currency = value.GetProperty("currency").GetString();
            var amount = value.GetProperty("amount").GetDouble();

            if (currency == null)
                return Task.FromResult<BsonValue>(BsonNull.Value);

            return Task.FromResult<BsonValue>(new CurrencyAmount(currency, amount).ToBsonDocument());
        }
        catch
        {
            return Task.FromResult<BsonValue>(BsonNull.Value);
        }
    }

    /// <summary>
    /// Converts a single user to BsonValue using the user cache service.
    /// </summary>
    private async Task<BsonValue> ConvertUser(JsonElement value, CancellationToken ct)
    {
        try
        {
            var userSearchResult = value.Deserialize<UserSearchResult>(Options);
            if (userSearchResult == null)
                return BsonNull.Value;

            // Try to get user or create a new one if it doesn't exist'
            var user = await userService.GetUser(userSearchResult.UserName, ct);
            user ??= await userService.AddOrUpdateUser(userSearchResult.UserName, userSearchResult.DisplayName,
                userSearchResult.Email, ct);

            return BsonTypeMapper.MapToBsonValue(user.ToBsonDocument());
        }
        catch
        {
            return BsonNull.Value;
        }
    }

    /// <summary>
    /// Handles user array conversion.
    /// </summary>
    private async Task<BsonValue> ConvertUserArray(JsonElement value, CancellationToken ct)
    {
        try
        {
            var searchResults = value.Deserialize<UserSearchResult[]>(Options);
            if (searchResults == null || searchResults.Length == 0)
                return BsonNull.Value;

            var users = new List<BsonDocument>();
            foreach (var userSearchResult in searchResults)
            {
                // Try to get user or create a new one if it doesn't exist'
                var user = await userService.GetUser(userSearchResult.UserName, ct);
                user ??= await userService.AddOrUpdateUser(userSearchResult.UserName, userSearchResult.DisplayName,
                    userSearchResult.Email, ct);
                users.Add(user.ToBsonDocument());
            }

            return new BsonArray(users);
        }
        catch
        {
            return BsonNull.Value;
        }
    }
}