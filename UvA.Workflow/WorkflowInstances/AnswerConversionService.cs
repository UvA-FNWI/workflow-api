using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace UvA.Workflow.Services;

public record AnswerInput(
    JsonElement? Value = null,
    int? DeleteFileId = null);

/// <summary>
/// Service responsible for converting answer input data to BsonValue based on question data types.
/// Handles proper type conversion and user resolution through the user cache.
/// </summary>
public class AnswerConversionService(UserCacheService userCacheService)
{
    public static readonly JsonSerializerOptions Options = new() {PropertyNamingPolicy = JsonNamingPolicy.CamelCase};
    
    /// <summary>
    /// Converts an answer input to a BsonValue based on the question's data type.
    /// </summary>
    /// <param name="answerInput">The object containing the answers for the given question</param>
    /// <param name="question">The question definition containing data type information</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>BsonValue representation of the answer</returns>
    public async Task<BsonValue> ConvertToValue(AnswerInput answerInput, Question question, CancellationToken ct)
    {
        if (answerInput.Value == null)
            return BsonNull.Value;

        var value = answerInput.Value.Value;

        return question.DataType switch
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

            DataType.User when question.IsArray => await ConvertUserArray(value, ct),

            DataType.User => await ConvertUser(value, ct),

            _ => throw new NotImplementedException(
                $"Data type {question.DataType} is not supported for question '{question.DisplayName}'")
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
            var externalUser = value.Deserialize<ExternalUser>(Options);
            if (externalUser == null)
                return BsonNull.Value;

            var user = await userCacheService.GetUser(externalUser, ct);
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
            var externalUsers = value.Deserialize<ExternalUser[]>(Options);
            if (externalUsers == null || externalUsers.Length == 0)
                return BsonNull.Value;

            var users = new List<BsonDocument>();
            foreach (var externalUser in externalUsers)
            {
                var user = await userCacheService.GetUser(externalUser, ct);
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