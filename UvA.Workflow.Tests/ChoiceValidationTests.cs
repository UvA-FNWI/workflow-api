using System.Text.Json;
using UvA.Workflow.WorkflowInstances;
using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Tests;

public class ChoiceValidationTests
{
    private static readonly PropertyDefinition Choice = new()
    {
        Name = "Reason",
        Type = "PassFail",
        Values = [new Choice { Name = "AVV" }, new Choice { Name = "NAV" }]
    };

    private static string? Check(string json) =>
        AnswerConversionService.FindInvalidChoice(JsonDocument.Parse(json).RootElement, Choice);

    [Theory]
    [InlineData("\"AVV\"", null)] // valid single choice
    [InlineData("\"NAV\"", null)] // valid single choice
    [InlineData("null", null)] // non-string = cleared, allowed
    [InlineData("\"\"", "")] // empty string is not a valid choice
    [InlineData("\"XYZ\"", "XYZ")] // not in valueset
    [InlineData("[\"AVV\", \"NAV\"]", null)] // valid multiple choice
    [InlineData("[\"AVV\", \"BAD\"]", "BAD")] // one bad element in array
    [InlineData("[\"AVV\", \"\"]", "")] // empty element rejected, can't pad an array with blanks
    public void FindInvalidChoice_ReturnsFirstInvalidValueOrNull(string json, string? expected)
        => Assert.Equal(expected, Check(json));
}