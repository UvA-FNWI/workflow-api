using UvA.Workflow.Api.WorkflowDefinitions.Dtos;
using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Tests.WorkflowDefinitions;

public class WorkflowDefinitionDtoTests
{
    [Fact]
    public void Create_ExposesMessagesEnabled()
    {
        var definition = new WorkflowDefinition
        {
            Name = "Project",
            TitlePlural = new BilingualString("Projects", "Projecten"),
            Screens = [new Screen { Name = "Projects", Columns = [] }],
            Steps = [],
            Messages = new MessagesSettings { Enabled = true }
        };

        var dto = WorkflowDefinitionDto.Create(definition);

        Assert.True(dto.MessagesEnabled);
        Assert.Equal(["Projects"], dto.Screens);
    }
}