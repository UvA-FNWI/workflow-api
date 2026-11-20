using UvA.Workflow.Api.WorkflowDefinitions.Dtos;
using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Tests.WorkflowDefinitions;

public class MessagesSettingsTests
{
    [Fact]
    public void AllowsMessages_InstanceRequiresEnabledMessagesInfoCard()
    {
        var definition = new WorkflowDefinition
        {
            Name = "Project",
            Messages = new MessagesSettings { Enabled = true },
            InfoCards = [new InfoCard { Name = "Messages", Type = InfoCardType.Messages, Enabled = false }]
        };
        definition.Properties.Add(new StringPropertyDefinition
        {
            Name = "Title",
            Type = "String",
            ParentType = definition
        });

        Assert.False(definition.AllowsMessages(null));
        Assert.True(definition.AllowsMessages("Title"));

        definition.InfoCards[0].Enabled = true;
        Assert.True(definition.AllowsMessages(null));

        definition.Messages.Enabled = false;
        Assert.False(definition.AllowsMessages(null));
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
                Fields = false,
                Layout = "flat",
                Close = false,
                Callout = new MessageCalloutSettings
                {
                    Variant = CalloutVariant.Info,
                    Title = new BilingualString("Privacy", "Privacy"),
                    Text = new BilingualString("Be careful", "Pas op")
                },
                Term = new BilingualString("note", "notitie"),
                TermPlural = new BilingualString("notes", "notities")
            }
        };

        var dto = WorkflowDefinitionDto.Create(definition);

        Assert.NotNull(dto.Messages);
        Assert.False(dto.Messages.FieldsAllowAll);
        Assert.Equal("flat", dto.Messages.Layout);
        Assert.False(dto.Messages.AllowClose);
        Assert.NotNull(dto.Messages.Callout);
        Assert.Equal(CalloutVariant.Info, dto.Messages.Callout.Variant);
        Assert.Equal("Privacy", dto.Messages.Callout.Title?.En);
        Assert.Equal("Be careful", dto.Messages.Callout.Text?.En);
        Assert.Equal("note", dto.Messages.Term?.En);
        Assert.Equal("notities", dto.Messages.TermPlural?.Nl);
    }
}