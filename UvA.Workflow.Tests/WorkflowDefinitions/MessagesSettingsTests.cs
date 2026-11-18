using UvA.Workflow.Api.WorkflowDefinitions.Dtos;
using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Tests.WorkflowDefinitions;

public class MessagesSettingsTests
{
    [Fact]
    public void AllowsMessages_InstanceRequiresCardWhenDisabled()
    {
        var definition = new WorkflowDefinition
        {
            Name = "Project",
            Messages = new MessagesSettings { Enabled = true, Card = false }
        };
        definition.Properties.Add(new StringPropertyDefinition
        {
            Name = "Title",
            Type = "String",
            ParentType = definition
        });

        Assert.False(definition.AllowsMessages(null));
        Assert.True(definition.AllowsMessages("Title"));
    }

    [Fact]
    public void AllowsMessages_DenyAllFields_RequiresPropertyOptIn()
    {
        var definition = new WorkflowDefinition
        {
            Name = "Project",
            Messages = new MessagesSettings { Enabled = true, Fields = false }
        };
        definition.Properties.Add(new StringPropertyDefinition
        {
            Name = "Public", Type = "String", ParentType = definition
        });
        definition.Properties.Add(new StringPropertyDefinition
        {
            Name = "Tagged", Type = "String", ParentType = definition, Messages = true
        });
        definition.Properties.Add(new StringPropertyDefinition
        {
            Name = "OptedOut", Type = "String", ParentType = definition, Messages = false
        });

        Assert.False(definition.AllowsMessages("Public"));
        Assert.True(definition.AllowsMessages("Tagged"));
        Assert.False(definition.AllowsMessages("OptedOut"));
    }

    [Fact]
    public void WorkflowDefinitionDto_ExposesMessagesSettings()
    {
        var definition = new WorkflowDefinition
        {
            Name = "Project",
            TitlePlural = new BilingualString("Projects", "Projecten"),
            Screens = [],
            Steps = [],
            Messages = new MessagesSettings
            {
                Enabled = true,
                Card = true,
                Fields = false,
                Layout = "flat",
                Close = false,
                Callout = true,
                CalloutTitle = new BilingualString("Privacy", "Privacy"),
                CalloutText = new BilingualString("Be careful", "Pas op")
            }
        };

        var dto = WorkflowDefinitionDto.Create(definition);

        Assert.NotNull(dto.Messages);
        Assert.True(dto.Messages.Card);
        Assert.False(dto.Messages.FieldsAllowAll);
        Assert.Equal("flat", dto.Messages.Layout);
        Assert.False(dto.Messages.AllowClose);
        Assert.True(dto.Messages.Callout);
        Assert.Equal("Privacy", dto.Messages.CalloutTitle?.En);
        Assert.Equal("Be careful", dto.Messages.CalloutText?.En);
    }
}