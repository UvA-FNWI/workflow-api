using System.Globalization;
using System.Text.Json;
using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Submissions;

public partial class FormPreviewService
{
    private static BsonValue ConvertInput(JsonElement? input, PropertyDefinition question, bool arrayItem = false)
    {
        if (question.IsComputed || question.DataType == DataType.File)
            throw new ArgumentException($"Question '{question.Name}' cannot accept preview input.");
        if (input is null || input.Value.ValueKind == JsonValueKind.Null)
            return BsonNull.Value;
        var value = input.Value;
        if (question.IsArray && !arrayItem)
            return value.ValueKind == JsonValueKind.Array
                ? new BsonArray(value.EnumerateArray().Select(item => ConvertInput(item, question, true)))
                : throw new ArgumentException($"Expected an array for '{question.Name}'.");

        switch (question.DataType)
        {
            case DataType.Choice when value.ValueKind == JsonValueKind.String:
                if (((ChoicePropertyDefinition)question).Values?.Any(choice => choice.Name == value.GetString()) !=
                    true)
                    throw new ArgumentException($"Unknown choice for '{question.Name}'.");
                return value.GetString()!;
            case DataType.String or DataType.Reference when value.ValueKind == JsonValueKind.String:
                return value.GetString()!;
            case DataType.Double when value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) &&
                                      double.IsFinite(number):
                return number;
            case DataType.Int when value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var integer):
                return integer;
            case DataType.Check when value.ValueKind is JsonValueKind.True or JsonValueKind.False:
                return value.GetBoolean();
            case DataType.Date or DataType.DateTime when value.ValueKind == JsonValueKind.String &&
                                                         DateTime.TryParse(value.GetString(),
                                                             CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind,
                                                             out var date):
                return date;
            case DataType.Currency when value.ValueKind == JsonValueKind.Object &&
                                        value.TryGetProperty("currency", out var currency) &&
                                        currency.ValueKind == JsonValueKind.String &&
                                        value.TryGetProperty("amount", out var amount) &&
                                        amount.ValueKind == JsonValueKind.Number &&
                                        amount.TryGetDouble(out var currencyAmount) &&
                                        double.IsFinite(currencyAmount):
                return new CurrencyAmount(currency.GetString()!, currencyAmount).ToBsonDocument();
            case DataType.User when value.ValueKind == JsonValueKind.Object:
                InstanceUser? user;
                try
                {
                    user = value.Deserialize<InstanceUser>(AnswerConversionService.Options);
                }
                catch (JsonException exception)
                {
                    throw new ArgumentException($"Invalid user for '{question.Name}'.", exception);
                }

                if (user == null || string.IsNullOrWhiteSpace(user.UserName))
                    break;
                // This identity exists only in the evaluation snapshot, never in the user repository.
                user.Id = ObjectId.Empty.ToString();
                return user.ToBsonDocument();
        }

        throw new ArgumentException($"Invalid value for '{question.Name}'.");
    }
}