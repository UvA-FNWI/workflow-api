using Microsoft.AspNetCore.Http;

namespace UvA.Workflow.Services;

public record AnswerInput(
    string QuestionName,
    string? Text = null,
    DateTime? DateTime = null,
    double? Number = null,
    IFormFile? File = null,
    ExternalUser? User = null,
    int? DeleteFileId = null);

/// <summary>
/// Service responsible for converting answer input data to BsonValue based on question data types.
/// Handles proper type conversion and user resolution through the user cache.
/// </summary>
public class AnswerConversionService(UserCacheService userCacheService)
{
    /// <summary>
    /// Converts an answer input to a BsonValue based on the question's data type.
    /// </summary>
    /// <param name="answerInput">The object containing the answers for the given question</param>
    /// <param name="question">The question definition containing data type information</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>BsonValue representation of the answer</returns>
    public async Task<BsonValue> ConvertToValue(AnswerInput answerInput, Question question, CancellationToken ct)
    {
        return question.DataType switch
        {
            DataType.String or DataType.Choice or DataType.Reference => answerInput.Text,
            DataType.Double => answerInput.Number,
            DataType.Int => Convert.ToInt32(answerInput.Number),
            DataType.DateTime or DataType.Date => answerInput.DateTime,
            _ when answerInput.Text is null => BsonNull.Value,
            DataType.Currency when answerInput.Number is not null => new CurrencyAmount(answerInput.Text,
                answerInput.Number.Value).ToBsonDocument(),
            DataType.User when question.IsArray => await ConvertUserArray(answerInput.Text, ct),
            DataType.User => await ConvertUser(answerInput.User, ct),
            _ => throw new NotImplementedException(
                $"Data type {question.DataType} is not supported for question '{answerInput.QuestionName}'")
        };
    }

    /// <summary>
    /// Converts a single user to BsonValue using the user cache service.
    /// </summary>
    private async Task<BsonValue> ConvertUser(ExternalUser? externalUser, CancellationToken ct)
    {
        if (externalUser == null)
            return BsonNull.Value;

        var user = await userCacheService.GetUser(externalUser, ct);
        return BsonTypeMapper.MapToBsonValue(user.ToBsonDocument());
    }

    /// <summary>
    /// Handles user array conversion. Currently returns empty string as per original implementation.
    /// TODO: Implement proper user array handling
    /// </summary>
    private Task<BsonValue> ConvertUserArray(string? text, CancellationToken ct)
    {
        // TODO: Implement proper user array conversion
        // This matches the current behavior in the original code
        return Task.FromResult<BsonValue>("");
    }
}