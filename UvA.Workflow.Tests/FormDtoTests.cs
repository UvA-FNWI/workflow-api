using UvA.Workflow.Api.Submissions.Dtos;

namespace UvA.Workflow.Tests;

public class FormDtoTests
{
    [Fact]
    public void Create_CalculatesQuestionPercentagesAcrossAllPages()
    {
        var definition = new WorkflowDefinition { Name = "Assessment" };
        var quality = Question("Quality", definition, 2);
        var depth = Question("Depth", definition, 1);
        var comments = Question("Comments", definition);

        var form = new Form
        {
            Name = "Review",
            Pages =
            [
                new Page { Name = "Report", Fields = [quality, comments] },
                new Page { Name = "Process", Fields = [depth] }
            ]
        };

        var result = FormDto.Create(form, new ObjectContext([]));

        Assert.Equal(2m / 3m * 100m, result.Pages[0].Questions.Single(q => q.Name == "Quality").Percentage);
        Assert.Equal(1m / 3m * 100m, result.Pages[1].Questions.Single(q => q.Name == "Depth").Percentage);
        Assert.Null(result.Pages[0].Questions.Single(q => q.Name == "Comments").Percentage);
    }

    private static PropertyDefinition Question(string name, WorkflowDefinition parent, decimal? weight = null) =>
        new()
        {
            Name = name,
            Type = "Double",
            ParentType = parent,
            Calculation = weight == null ? null : new CalculationSettings { Weight = weight }
        };
}