using UvA.Workflow.Api.WorkflowDefinitions.Dtos;
using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Tests.WorkflowDefinitions;

public class WorkflowDefinitionDtoTests
{
    [Fact]
    public void Create_ExposesScreenSummariesIconAndDescription()
    {
        var definition = new WorkflowDefinition
        {
            Name = "Application",
            TitlePlural = new BilingualString("Subjects", "Onderwerpen"),
            Description = new BilingualString("IB&P assessments", "IB&P-beoordelingen"),
            Icon = new Icon { Type = "shield-check-line" },
            Screens =
            [
                new Screen
                {
                    Name = "Applications",
                    Title = new BilingualString("Applications", "Aanvragen"),
                    Description = new BilingualString("All subjects", "Alle onderwerpen"),
                    Index = 1,
                    Columns = []
                }
            ],
            Steps = []
        };

        var dto = WorkflowDefinitionDto.Create(definition);

        Assert.Equal("shield-check-line", dto.Icon!.Type);
        Assert.Equal("IB&P assessments", dto.Description!.En);
        var screen = Assert.Single(dto.Screens);
        Assert.Equal("Applications", screen.Name);
        Assert.Equal("Applications", screen.Title!.En);
        Assert.Equal("All subjects", screen.Description!.En);
        Assert.Equal(1, screen.Index);
    }

    [Fact]
    public void CreateForAccessibleOverview_ExcludesRelationScreens()
    {
        var definition = new WorkflowDefinition
        {
            Name = "FunctionAssignment",
            TitlePlural = new BilingualString("Assignments", "Toewijzingen"),
            Screens =
            [
                new Screen { Name = "FunctionAssignments", Columns = [] },
                new Screen { Name = "OtherAssignments", Relation = "FunctionAssignment", Columns = [] },
            ],
            Steps = []
        };

        var dto = WorkflowDefinitionDto.CreateForAccessibleOverview(definition);

        Assert.Single(dto.Screens);
        Assert.Equal("FunctionAssignments", dto.Screens[0].Name);
    }
}